namespace Tabletop.Geometry;

/// <summary>
/// What one move may do, as the move rules judge each model's: its allowance along the path its base takes round the impassable volumes and the
/// standing models (none when it passes models), the difficult volumes and their cap (held over the whole unit once one path enters, as p.11
/// holds it), the table, a landing on no other base and in no landing volume, and the unit's coherency after it (a chain within `Chain`, or
/// `Elevated` between models at different heights, every model within `Spread` of the rest). `Steep` volumes are difficult for a model whose move
/// starts outside them (Movement.SteepEntering). `ContactVertical`, when set, holds a contact landing to that height from the target's base, the
/// landing resting on its open surface nearest the target's level (Collision.RestNear); null judges contact in plan.
/// </summary>
public sealed record FormationTerms(double Allowance, IReadOnlyList<WorldVolume> Impassable, IReadOnlyList<WorldVolume> Difficult, double? DifficultCap,
    IReadOnlyList<WorldVolume> Landing, IReadOnlyList<WorldVolume> Volumes, TableSize Table, IReadOnlyList<PlacedModel> Standing, bool PassesModels,
    double Chain, double Elevated, double Spread, IReadOnlyList<WorldVolume>? Steep = null,
    IReadOnlyList<PlacedModel>? Apart = null, double Separation = 0, double? ContactVertical = null);

/// <summary>
/// A unit moved together: one model to a chosen landing and the rest formed up on it. Deterministic, so whoever asks gets the same answer: the
/// game server asks whether a charge could have reached base contact in coherency (p.9 "or as close as possible, whilst still maintaining unit
/// coherency"), and the simulator's policy moves that way.
/// </summary>
public static class UnitFormation
{
    /// <summary>How far short of touching a trial landing stands, so a rest and float noise never read it as on the base it touches.</summary>
    public const double ContactGap = 0.05;

    /// <summary>The gaps a following model tries beside one already placed, inside the 1" chain.</summary>
    static readonly double[] Gaps = [0.3, 0.7];

    /// <summary>How many lead landings InContact forms up on before it gives up: bounded work for a crowd.</summary>
    const int Attempts = 8;

    /// <summary>
    /// The unit (`unit`, every model on the table, in order) with one model in base contact with a `targets` model and every model's move within
    /// the terms, or null when the attempts find none. Leads are tried nearest a target first, each at the first trial landing in contact
    /// (Movement.ContactPoints, ContactGap clear) its own move reaches, against each target nearest first, up to `Attempts` landings; each is
    /// formed up on (Follow). `members` are the unit's models that do not move, held where they stand for coherency. With `ContactVertical` the
    /// lead rests on its open surface nearest the target's level, and a landing whose rest is farther than that from the target's base is no
    /// contact. Each landing is given with the height it rests at.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Point3>? InContact(IReadOnlyList<(Guid Id, PlacedModel Start)> unit, IReadOnlyList<PlacedModel> members,
        IReadOnlyList<PlacedModel> targets, FormationTerms t)
    {
        var tries = 0;
        foreach (var lead in unit.OrderBy(u => targets.Min(x => Distance.EdgeToEdge(u.Start, x))))
            foreach (var target in targets.OrderBy(x => Distance.EdgeToEdge(lead.Start, x)))
            {
                if (Movement.ContactDistance(lead.Start, target, t.Allowance, t.Impassable, t.Table) is null) continue;
                double? level = t.ContactVertical is null ? null : target.Bottom();
                foreach (var at in Movement.ContactPoints(lead.Start, target, ContactGap))
                {
                    if (t.ContactVertical is { } reach && Math.Abs(Rest(lead.Start, at, t, level).Bottom() - target.Bottom()) > reach + Tolerance.Distance) continue;
                    if (!Fits(lead.Start, at, t, [], level)) continue;
                    if (Follow(unit, members, lead.Id, at, t, level) is { } formed) return formed;
                    if (++tries >= Attempts) return null;
                    break;
                }
            }
        return null;
    }

    /// <summary>
    /// The lead at `leadAt`, and each other model in order at the first landing its move reaches: its start moved as the lead moved, else beside a
    /// model already placed (the newest first) or a standing member, at each of Gaps on 16 bearings (Movement.ContactPoints, the side facing the
    /// model first). Null when a model has nowhere, the unit is out of coherency, or the difficult cap over the unit is broken. The lead rests on
    /// its open surface nearest `leadLevel` (the highest when null), every other model on its highest.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Point3>? Follow(IReadOnlyList<(Guid Id, PlacedModel Start)> unit, IReadOnlyList<PlacedModel> members, Guid lead,
        Point2 leadAt, FormationTerms t, double? leadLevel = null)
    {
        var start = unit.Single(u => u.Id == lead).Start;
        var shift = leadAt - start.Placement.Position.Horizontal;
        var result = new Dictionary<Guid, Point2> { [lead] = leadAt };
        var placed = new List<PlacedModel> { Rest(start, leadAt, t, leadLevel) };
        foreach (var (id, from) in unit.Where(u => u.Id != lead))
        {
            var moved = from.Placement.Position.Horizontal + shift;
            Point2? at = Fits(from, moved, t, placed) ? moved : null;
            at ??= ((IEnumerable<PlacedModel>)placed).Reverse().Concat(members)
                .SelectMany(o => Gaps.SelectMany(gap => Movement.ContactPoints(from, o, gap, 16)))
                .Select(p => (Point2?)p).FirstOrDefault(p => Fits(from, p!.Value, t, placed));
            if (at is not { } there) return null;
            result[id] = there;
            placed.Add(Rest(from, there, t));
        }
        if (!Coherency.Check([.. placed, .. members], t.Chain, t.Elevated, t.Spread).Coherent) return null;
        if (t.DifficultCap is { } cap && cap < t.Allowance)
        {
            var paths = unit.Select(u => Path(u.Start, result[u.Id], t, t.Allowance, t.Difficult, cap, t.Steep)).ToList();
            if (paths.Any(p => p.EntersDifficult) && unit.Any(u => Path(u.Start, result[u.Id], t, cap, [], null, null).Distance > cap + Tolerance.Distance)) return null;
        }
        var rests = placed.Zip(unit.Where(u => u.Id != lead).Select(u => u.Id).Prepend(lead)).ToDictionary(x => x.Second, x => x.First.Bottom());
        return result.ToDictionary(kv => kv.Key, kv => new Point3(kv.Value.X, kv.Value.Y, rests[kv.Key]));
    }

    /// <summary>The body at a point, rested on its highest surface there, or with a `level` on its open surface nearest it (Collision.RestNear).</summary>
    static PlacedModel Rest(PlacedModel start, Point2 at, FormationTerms t, double? level = null)
    {
        var there = start with { Placement = start.Placement with { Position = new Point3(at.X, at.Y, 0) } };
        var z = level is { } l ? Collision.RestNear(there, t.Volumes, l) : Collision.RestHeight(there, t.Volumes);
        return there with { Placement = there.Placement with { Position = new Point3(at.X, at.Y, z) } };
    }

    static MoveCheck Path(PlacedModel start, Point2 to, FormationTerms t, double allowance, IReadOnlyList<WorldVolume> difficult, double? cap, IReadOnlyList<WorldVolume>? steep) =>
        Movement.Check(start, to, allowance, t.Impassable, difficult, cap, t.Table, t.PassesModels ? null : t.Standing, t.Volumes, steep: steep);

    /// <summary>One model's landing: on no standing or placed base, in no landing volume, on a path within the allowance (and cap) that goes round
    /// what it may not pass and stays on the table.</summary>
    static bool Fits(PlacedModel start, Point2 at, FormationTerms t, IReadOnlyList<PlacedModel> placed, double? level = null)
    {
        var landed = Rest(start, at, t, level);
        if (t.Standing.Any(o => Overlap.Bases(landed, o)) || placed.Any(o => Overlap.Bases(landed, o))) return false;
        if (t.Apart is { } apart && apart.Any(o => Distance.BodyToBody(landed, o) < t.Separation - 1e-6)) return false;   // other units' models a landing keeps Separation from
        if (t.Landing.Count > 0 && Movement.Check(landed, at, 0, t.Landing, [], null, null).CrossesImpassable) return false;
        return Path(start, at, t, t.Allowance, t.Difficult, t.DifficultCap, t.Steep) is { WithinAllowance: true, CrossesImpassable: false, LeavesTable: false, CrossesModels: false };
    }

    /// <summary>
    /// The unit back in coherency by this move (p.7: a model out of coherency "must use an action so that the model gets back into coherency"):
    /// the models of its largest chain where they stand, and each model outside it at the first landing its own move reaches beside a model
    /// already in the chain (nearest first, at each of Gaps on 16 bearings). `unit` are the models that may move, `members` the unit's models
    /// that do not. Null when a model outside the chain does not move or has no such landing: this search finds none, which is not proof that
    /// none exists. A unit already in coherency is returned as it stands.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Point2>? Regroup(IReadOnlyList<(Guid Id, PlacedModel Start)> unit, IReadOnlyList<PlacedModel> members, FormationTerms t)
    {
        IReadOnlyList<PlacedModel> all = [.. unit.Select(u => u.Start), .. members];
        var check = Coherency.Check(all, t.Chain, t.Elevated, t.Spread);
        var outOf = check.OutOfCoherency.ToHashSet();
        if (outOf.Any(i => i >= unit.Count)) return null;
        var result = new Dictionary<Guid, Point2>();
        var chain = new List<PlacedModel>(members);
        for (var i = 0; i < unit.Count; i++)
            if (!outOf.Contains(i)) { result[unit[i].Id] = unit[i].Start.Placement.Position.Horizontal; chain.Add(unit[i].Start); }
        if (chain.Count == 0) return null;
        foreach (var (id, from) in outOf.Order().Select(i => unit[i]).OrderBy(u => chain.Min(o => Distance.EdgeToEdge(u.Start, o))))
        {
            var here = from.Placement.Position.Horizontal;
            Point2? at = chain.OrderBy(o => Distance.EdgeToEdge(from, o))
                .SelectMany(o => Gaps.SelectMany(gap => Movement.ContactPoints(from, o, gap, 16)))
                .Where(p => here.DistanceTo(p) <= t.Allowance + Tolerance.Distance)          // a straight line is the shortest path: beyond it, no path fits
                .Select(p => (Point2?)p).FirstOrDefault(p => Fits(from, p!.Value, t, chain));
            if (at is not { } there) return null;
            result[id] = there;
            chain.Add(Rest(from, there, t));
        }
        return Coherency.Check(chain, t.Chain, t.Elevated, t.Spread).Coherent ? result : null;
    }

    /// <summary>
    /// Whether some model outside the unit's largest chain can end this move nearer it ("or as close as possible", p.7): a landing part of the
    /// way along the straight line to the chain's nearest model that its move reaches. False for a unit in coherency or one that cannot move.
    /// </summary>
    public static bool CanClose(IReadOnlyList<(Guid Id, PlacedModel Start)> unit, IReadOnlyList<PlacedModel> members, FormationTerms t) => Closer(unit, members, t) is not null;

    /// <summary>The landing CanClose finds: a model outside the chain and where it ends nearer it. Null when there is none.</summary>
    public static (Guid Id, Point2 At)? Closer(IReadOnlyList<(Guid Id, PlacedModel Start)> unit, IReadOnlyList<PlacedModel> members, FormationTerms t)
    {
        IReadOnlyList<PlacedModel> all = [.. unit.Select(u => u.Start), .. members];
        var outOf = Coherency.Check(all, t.Chain, t.Elevated, t.Spread).OutOfCoherency.ToHashSet();
        var chain = all.Where((_, i) => !outOf.Contains(i)).ToList();
        if (chain.Count == 0 || t.Allowance <= Tolerance.Distance) return null;
        var others = all.Where((_, i) => outOf.Contains(i)).ToList();
        foreach (var i in outOf.Where(i => i < unit.Count))
        {
            var from = unit[i].Start; var here = from.Placement.Position.Horizontal;
            var nearest = chain.MinBy(o => Distance.EdgeToEdge(from, o));
            var gap = Distance.EdgeToEdge(from, nearest);
            if (gap <= ContactGap) continue;
            var to = nearest.Placement.Position.Horizontal; var line = here.DistanceTo(to);
            if (line <= Tolerance.Distance) continue;
            foreach (var share in (double[])[1, 0.5, 0.25])
            {
                var step = Math.Min(t.Allowance, gap - ContactGap) * share;
                var at = new Point2(here.X + (to.X - here.X) / line * step, here.Y + (to.Y - here.Y) / line * step);
                if (Fits(from, at, t, [.. chain, .. others.Where(o => o != from)])) return (unit[i].Id, at);
            }
        }
        return null;
    }
}
