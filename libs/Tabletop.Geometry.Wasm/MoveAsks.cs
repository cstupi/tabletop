namespace Tabletop.Geometry.Wasm;

/// <summary>
/// One move Ask's reaches, kept between calls (the Godot client's MoveRegions, for the browser): each model's MoveReach built the first time it is
/// asked for, from the Ask's parameters, and kept until the Ask is closed. The area (Area, in shares for several workers), each drag frame's routes,
/// the unit's allowance and the contacts (Frame) read the kept reaches, so no drag frame searches a path a reach already answers.
/// </summary>
public sealed class MoveAsk
{
    readonly MoveAskRequest _r;
    readonly List<WorldVolume> _volumes;
    readonly List<WorldVolume> _impassable, _difficult, _steep, _dangerous, _unstable;
    readonly Dictionary<Guid, PlacedModel> _starts;
    readonly Dictionary<Guid, MoveReach> _reaches = new();

    public MoveAsk(MoveAskRequest r)
    {
        _r = r;
        _volumes = r.Terrain.SelectMany(p => p.WorldVolumes()).ToList();
        List<WorldVolume> Named(IReadOnlyList<Guid>? ids) => ids is null ? [] : _volumes.Where(v => ids.Contains(v.Id)).ToList();
        (_impassable, _difficult, _steep, _dangerous, _unstable) = (Named(r.Impassable), Named(r.Difficult), Named(r.Steep), Named(r.Dangerous), Named(r.Unstable));
        _starts = r.Models.ToDictionary(m => m.Id, m => m.Start);
        if (r.Prepare) foreach (var m in r.Models) ReachOf(m.Id).PrepareRoute();
    }

    double Allowance => Math.Max(0, _r.Allowance);

    /// <summary>MoveRegions.ReachOf: the steep volumes the model starts outside are difficult for it (Movement.SteepEntering).</summary>
    MoveReach ReachOf(Guid id)
    {
        if (_reaches.TryGetValue(id, out var known)) return known;
        var start = _starts[id];
        var difficult = _difficult.ToList();
        if (_steep.Count > 0) difficult.AddRange(Movement.SteepEntering(start, _steep));
        return _reaches[id] = MoveReach.Build(start, Allowance, _impassable, difficult, _r.DifficultCap, _r.Size, _r.Stationary, _volumes, _dangerous, _unstable,
            _r.UnstableOver ?? 0, _r.PassesModels ? null : _r.Units);
    }

    /// <summary>MoveRuler.Minimum as the area reads it: the Ask's MinDistance where the table has room for it, else as far as it allows that way.</summary>
    Func<Point2, double>? Least(Guid id)
    {
        if (_r.MinDistance is not { } m) return null;
        var start = _starts[id];
        var origin = start.Placement.Position.Horizontal;
        var farthest = Movement.FarthestOnTable(start, _r.Size).Distance;
        return p => farthest >= m ? m : p.DistanceTo(origin) > 1e-6 ? Movement.RoomAlong(start, p, _r.Size) : farthest;
    }

    /// <summary>The model's area in its bands (MoveReach.Bands), or one share of it; empty for a model the Ask does not move.</summary>
    public MoveBands Area(Guid id, int part, int parts) =>
        _starts.ContainsKey(id) ? ReachOf(id).Bands(Least(id), part, parts) : new MoveBands([], [], []);

    /// <summary>
    /// One drag frame: each moved model's route (MoveReach.Route; the shortest once the unit is capped) with its base swept along it, the unit's
    /// allowance (the Ask's, or its cap once any moved model's route goes in or through difficult ground: p.11), and the landings' contacts
    /// (ContactCheck.Move: a landing its reach takes is not searched again for a path through a model).
    /// </summary>
    public MoveFrame Frame(IReadOnlyDictionary<Guid, Placement> proposed, bool contacts)
    {
        var moved = proposed.Where(kv => _starts.TryGetValue(kv.Key, out var s) && s.Placement.Position.Horizontal.DistanceTo(kv.Value.Position.Horizontal) > 1e-6)
            .Select(kv => (Id: kv.Key, To: kv.Value.Position.Horizontal)).ToList();
        var allowance = Allowance;
        var capped = _r.DifficultCap is { } cap && cap < allowance && moved.Any(m => ReachOf(m.Id).Route(m.To).EntersDifficult);
        var unit = capped ? _r.DifficultCap!.Value : allowance;
        var routes = moved.Select(m =>
        {
            var path = ReachOf(m.Id).Route(m.To, capped);
            var swept = SweptPath.Of(_starts[m.Id], path.Points, SweptPath.DangerousTo(path.Distance, _dangerous, _unstable, _r.UnstableOver ?? 0));
            return new RouteFound(m.Id, [.. path.Points], path.Distance, path.EntersDifficult, [.. swept.Footprints], [.. swept.Dangerous], [.. swept.Stretches]);
        }).ToArray();
        ContactFound[] found = [];
        if (contacts && moved.Count > 0)
        {
            double? held = capped ? unit : null;
            var terms = new ContactTerms(_r.Separation, _r.PassesModels, _r.Landing, new ContactMove(_r.Allowance, _r.Impassable, _r.Difficult, _r.DifficultCap, _r.Steep),
                _r.Size, held, (id, to) => _starts.ContainsKey(id) && ReachOf(id).Reaches(to, held), _r.Units?.Bases);
            found = Contacts.Of(_r.Bodies, proposed, _volumes, terms).Select(kv => new ContactFound(kv.Key, kv.Value.Kind, kv.Value.UnitId)).ToArray();
        }
        return new MoveFrame(unit, routes, found);
    }
}

/// <summary>The open move Asks by the caller's key; the oldest is dropped past <see cref="Cap"/>, so a page that never closes one stays bounded. A kept
/// Ask is read by one caller at a time (the browser's worker; a test).</summary>
public static class MoveAsks
{
    const int Cap = 4;
    static readonly Dictionary<string, MoveAsk> Open = new();
    static readonly List<string> Order = new();

    public static void Put(string key, MoveAsk ask)
    {
        lock (Open)
        {
            Drop(key);
            Open[key] = ask;
            Order.Add(key);
            while (Order.Count > Cap) Drop(Order[0]);
        }
    }

    public static MoveAsk Get(string key)
    {
        lock (Open) return Open.TryGetValue(key, out var ask) ? ask : throw new KeyNotFoundException($"no open move Ask '{key}'");
    }

    public static bool Remove(string key)
    {
        lock (Open) return Drop(key);
    }

    static bool Drop(string key)
    {
        Order.Remove(key);
        return Open.Remove(key);
    }
}
