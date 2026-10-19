using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// One move Ask's reach, per model from where it stands (MoveReach: measured along the path the base takes, p.5): the area drawn round each held
/// model, a minimum cutting its middle out, the point a drag is held to, and the path a ruler shows. The area is one: wherever the model gets by going
/// round difficult terrain within the allowance or through it within the cap (p.11). Each reach is built the first time it is asked for and kept for
/// the Ask. The unit's allowance is the one thing read from where every model is going (Allowance): p.11 holds all of them to the cap once any one
/// of them goes in or through difficult terrain.
/// </summary>
public sealed class MoveRegions(IReadOnlyDictionary<Guid, PlacedModel> starts, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size, IReadOnlyList<PlacedModel> others)
{
    readonly Dictionary<Guid, MoveReach> _reaches = [];
    readonly Dictionary<Guid, IReadOnlyList<Point2>> _areas = [];

    /// <summary>The Ask's models on the table; the others stand where the projection has them.</summary>
    public static MoveRegions Of(Projection p, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size) =>
        new(legal.ModelIds.Where(id => p.Models.GetValueOrDefault(id)?.Location is OnTable).ToDictionary(id => id, id =>
        {
            var m = p.Models[id];
            return new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement);
        }), legal, volumes, size, DragBodies.Stationary(p, legal.ModelIds));

    public bool Has(Guid id) => starts.ContainsKey(id);

    /// <summary>The model's reach from the Ask's parameters: its allowance, its impassable and difficult volumes by id and its cap; the other models stop
    /// it where the bodies meet in plan and height (unless the Ask PassesModels), the mover rested on the board's volumes; the table edge.</summary>
    MoveReach ReachOf(Guid id)
    {
        if (_reaches.TryGetValue(id, out var reach)) return reach;
        var impassable = volumes.Where(v => legal.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var difficult = volumes.Where(v => legal.DifficultVolumeIds.Contains(v.Id)).ToList();
        return _reaches[id] = MoveReach.Build(starts[id], Math.Max(0, legal.Allowance), impassable, difficult, legal.DifficultCap, size,
            legal.PassesModels ? null : others, volumes);
    }

    /// <summary>The area drawn round this model, as triangles; a minimum (Minimum) cuts its middle out.</summary>
    public IReadOnlyList<Point2> Outline(Guid id)
    {
        if (_areas.TryGetValue(id, out var area)) return area;
        Func<Point2, double>? least = legal.MinDistance is null ? null : p => Minimum(id, p) ?? 0;
        return _areas[id] = ReachOf(id).Triangles(least);
    }

    /// <summary>The least this model must move going towards `toward` (MoveRuler.Minimum; with no direction, the farthest the table allows), or
    /// null when the Ask sets none.</summary>
    public double? Minimum(Guid id, Point2? toward = null) => MoveRuler.Minimum(starts[id], legal, size, toward);

    /// <summary>The path a move of this model to `to` is measured along (MoveReach.Route), the shortest when the unit is `capped` (Allowance); null for a
    /// model the Ask does not move.</summary>
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
}
