using System.Collections.Concurrent;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// One move Ask's reach, per model from where it stands (MoveReach: measured along the path the base takes, p.5): the area drawn round each held
/// model, a minimum cutting its middle out, the point a drag is held to, and the path a ruler shows. The area is wherever the model gets by going
/// round difficult terrain within the allowance or through it within the cap (p.11), drawn in bands by what getting there costs (MoveReach.Bands: clear,
/// only through difficult ground, only into ground the Ask names dangerous to the move). Each reach is built the first time it is asked for and kept for
/// the Ask. The unit's allowance is the one thing read from where every model is going (Allowance): p.11 holds all of them to the cap once any one
/// of them goes in or through difficult terrain. Safe across threads: the decision box draws the areas off the main thread (AreaAsync) while a
/// drag clamps and routes on it; each reach and area is still built once.
/// </summary>
public sealed class MoveRegions(IReadOnlyDictionary<Guid, PlacedModel> starts, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size, IReadOnlyList<PlacedModel> others)
{
    readonly ConcurrentDictionary<Guid, Lazy<MoveReach>> _reaches = new();
    readonly ConcurrentDictionary<Guid, Lazy<MoveBands>> _areas = new();

    /// <summary>The Ask's models on the table; the others stand where the projection has them.</summary>
    public static MoveRegions Of(Projection p, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size) =>
        new(legal.ModelIds.Where(id => p.Models.GetValueOrDefault(id)?.Location is OnTable).ToDictionary(id => id, id =>
        {
            var m = p.Models[id];
            return new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement);
        }), legal, volumes, size, DragBodies.Stationary(p, legal.ModelIds));

    public bool Has(Guid id) => starts.ContainsKey(id);

    /// <summary>The model's reach from the Ask's parameters: its allowance, its impassable and difficult volumes by id (with the steep ones it starts outside,
    /// Movement.SteepEntering), its cap and the volumes dangerous to the move (at any length, or unstable over UnstableOver); the other models stop
    /// it where the bodies meet in plan and height (unless the Ask PassesModels), the mover rested on the board's volumes; the table edge.</summary>
    MoveReach ReachOf(Guid id) => _reaches.GetOrAdd(id, key => new Lazy<MoveReach>(() =>
    {
        var impassable = volumes.Where(v => legal.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var difficult = volumes.Where(v => legal.DifficultVolumeIds.Contains(v.Id)).ToList();
        if (legal.SteepVolumeIds is { Count: > 0 } steep) difficult.AddRange(Movement.SteepEntering(starts[key], volumes.Where(v => steep.Contains(v.Id)).ToList()));
        var dangerous = volumes.Where(v => legal.DangerousVolumeIds?.Contains(v.Id) == true).ToList();
        var unstable = volumes.Where(v => legal.UnstableVolumeIds?.Contains(v.Id) == true).ToList();
        return MoveReach.Build(starts[key], Math.Max(0, legal.Allowance), impassable, difficult, legal.DifficultCap, size,
            legal.PassesModels ? null : others, volumes, dangerous, unstable, legal.UnstableOver ?? 0);
    })).Value;

    /// <summary>The area drawn round this model, in its bands, as triangles; a minimum (Minimum) cuts its middle out.</summary>
    public MoveBands Bands(Guid id) => _areas.GetOrAdd(id, key => new Lazy<MoveBands>(() => ReachOf(key).Bands(Least(key)))).Value;

    /// <summary>These models' areas drawn as one (MoveReach.Union: each band wherever any of them reaches a point in it), the ones the Ask moves;
    /// kept per set of models, in any order. One model's is its own Bands.</summary>
    public MoveBands Area(IReadOnlyList<Guid> ids)
    {
        // By id, so a set's area (and the grid it is cut on, its first model's) is the same in whatever order the models are held.
        var moved = ids.Where(Has).Distinct().Order().ToList();
        if (moved.Count == 1) return Bands(moved[0]);
        return _unions.GetOrAdd(string.Join(',', moved), _ => new Lazy<MoveBands>(() =>
            MoveReach.Union(moved.Select(ReachOf).ToList(), legal.MinDistance is null ? null : moved.Select(Least).ToList()))).Value;
    }

    readonly ConcurrentDictionary<string, Lazy<MoveBands>> _unions = new();

    Func<Point2, double>? Least(Guid id) => legal.MinDistance is null ? null : p => Minimum(id, p) ?? 0;

    /// <summary>Area on the thread pool: each model's reach and band fields built on its own pool thread at once (those Prepare has not built
    /// yet), then the one area walked, its coarse cells on several threads; it stays on the pool throughout (no step waits for the main thread's
    /// next turn). Kept, so a set Prepare has drawn returns at once.</summary>
    public async Task<MoveBands> AreaAsync(IReadOnlyList<Guid> ids)
    {
        var moved = ids.Where(Has).Distinct().ToList();
        await Task.WhenAll(moved.Select(id => Task.Run(() => ReachOf(id).PrepareBands()))).ConfigureAwait(false);
        return await Task.Run(() => Area(moved)).ConfigureAwait(false);
    }

    /// <summary>Every model's reach built on the thread pool, one task each, with the fields its route reads round dangerous ground
    /// (MoveReach.PrepareRoute) and its bands read (PrepareBands); `first`'s area (the lead's, which the Ask's opening draws) before the rest; then
    /// the whole unit's area (Area), what a plain drag picks up. The decision box starts it when the Ask opens, so a pickup's first drag frame
    /// (which clamps and routes each held model on the main thread) finds the reaches built, and the unit's area is drawn from the pickup's first
    /// frame, without the unit's area taking the pool from the lead's.</summary>
    public async Task Prepare(Guid? first = null)
    {
        var reaches = Task.WhenAll(starts.Keys.Select(id => Task.Run(() => { var reach = ReachOf(id); reach.PrepareRoute(); reach.PrepareBands(); })));
        if (first is { } lead && Has(lead)) await Task.Run(() => Bands(lead)).ConfigureAwait(false);
        await reaches.ConfigureAwait(false);
        await Task.Run(() => Area([.. starts.Keys])).ConfigureAwait(false);
    }

    /// <summary>Whether this model's reach is built.</summary>
    public bool Built(Guid id) => _reaches.TryGetValue(id, out var reach) && reach.IsValueCreated;

    /// <summary>The least this model must move going towards `toward` (MoveRuler.Minimum; with no direction, the farthest the table allows), or
    /// null when the Ask sets none.</summary>
    public double? Minimum(Guid id, Point2? toward = null) => MoveRuler.Minimum(starts[id], legal, size, toward);

    /// <summary>The path a move of this model to `to` is measured along (MoveReach.Route: the server's order, round dangerous ground the Ask names when
    /// that fits), the shortest when the unit is `capped` (Allowance); null for a model the Ask does not move.</summary>
    public MovePath? Route(Guid id, Point2 to, bool capped = false) => starts.ContainsKey(id) ? ReachOf(id).Route(to, capped) : null;

    /// <summary>
    /// The unit's allowance with its models going to these points (from where each stands; a model missing from the map, or not moved, is where it
    /// stands): the Ask's, or its DifficultCap once any model's path goes in or through one of its difficult volumes. p.11: "If any model in a unit
    /// moves in or through difficult terrain at any point of its move, then all models in the unit may not move more than 6" for that movement."
    /// </summary>
    public double Allowance(IReadOnlyDictionary<Guid, Point2> at)
    {
        var allowance = Math.Max(0, legal.Allowance);
        if (legal.DifficultCap is not { } cap || cap >= allowance) return allowance;
        return starts.Any(kv => at.TryGetValue(kv.Key, out var to) && kv.Value.Placement.Position.Horizontal.DistanceTo(to) > 1e-6
            && ReachOf(kv.Key).Route(to).EntersDifficult) ? cap : allowance;
    }

    /// <summary>The target held to this model's reach (MoveReach.Clamp), and to `cap` when the unit is held to one (Allowance); a model the Ask does not
    /// move is not held.</summary>
    public Point2 Clamp(Guid id, Point2 target, double? cap = null) => starts.ContainsKey(id) ? ReachOf(id).Clamp(target, cap) : target;

    /// <summary>Whether Clamp leaves the target where it is (MoveReach.Reaches): the drag's landing test, without the nearest-point search.</summary>
    public bool Reaches(Guid id, Point2 target, double? cap = null) => !starts.ContainsKey(id) || ReachOf(id).Reaches(target, cap);
}
