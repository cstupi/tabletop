using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// One move Ask's regions, per model from where it stands: the outline drawn round each held model (MoveRuler.Region, with walls, the
/// other units' models and the table), the minimum's inner edge, and the reach a drag is clamped to (MoveRuler.Reach: allowance and
/// difficult cap). Each is computed the first time it is asked for and kept for the Ask. The unit's allowance is the one thing read from
/// where every model is going (Allowance): p.11 holds all of them to the cap once any one's path enters difficult terrain.
/// </summary>
public sealed class MoveRegions(IReadOnlyDictionary<Guid, PlacedModel> starts, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size, IReadOnlyList<PlacedModel> others)
{
    readonly Dictionary<Guid, IReadOnlyList<Point2>> _outlines = [], _reaches = [];

    /// <summary>The Ask's models on the table; the others collide where the projection has them.</summary>
    public static MoveRegions Of(Projection p, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size) =>
        new(legal.ModelIds.Where(id => p.Models.GetValueOrDefault(id)?.Location is OnTable).ToDictionary(id => id, id =>
        {
            var m = p.Models[id];
            return new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement);
        }), legal, volumes, size, DragBodies.Stationary(p, legal.ModelIds));

    public bool Has(Guid id) => starts.ContainsKey(id);

    public IReadOnlyList<Point2> Outline(Guid id)
    {
        if (!_outlines.TryGetValue(id, out var outline))
            _outlines[id] = outline = MoveRuler.Region(starts[id], legal with { Allowance = Math.Max(0, legal.Allowance) }, volumes, size, others);
        return outline;
    }

    /// <summary>The least this model must move going towards `toward` (MoveRuler.Minimum; with no direction, the farthest the table allows), or
    /// null when the Ask sets none.</summary>
    public double? Minimum(Guid id, Point2? toward = null) => MoveRuler.Minimum(starts[id], legal, size, toward);

    /// <summary>The inner edge a minimum cuts from this model's outline, or null: each vertex pulled in to the least its own ray must go.</summary>
    public IReadOnlyList<Point2>? Inner(Guid id)
    {
        if (legal.MinDistance is null) return null;
        var origin = starts[id].Placement.Position.Horizontal;
        return Outline(id).Select(p => origin.DistanceTo(p) is var d && Minimum(id, p) is { } least && d > least ? origin + (p - origin) * (least / d) : p).ToList();
    }

    /// <summary>
    /// The unit's allowance with its models going to these points (from where each stands; a model missing from the map, or not moved, is where it
    /// stands): the Ask's, or its DifficultCap once any model's straight path touches one of its difficult volumes. p.11: "If any model in a unit moves
    /// in or through difficult terrain at any point of its move, then all models in the unit may not move more than 6" for that movement."
    /// </summary>
    public double Allowance(IReadOnlyDictionary<Guid, Point2> at)
    {
        var allowance = Math.Max(0, legal.Allowance);
        if (legal.DifficultCap is not { } cap || cap >= allowance) return allowance;
        var difficult = volumes.Where(v => legal.DifficultVolumeIds.Contains(v.Id)).ToList();
        return starts.Any(kv => at.TryGetValue(kv.Key, out var to) && kv.Value.Placement.Position.Horizontal.DistanceTo(to) > 1e-6
            && Movement.Check(kv.Value, to, allowance, [], difficult, cap, null).EntersDifficult) ? cap : allowance;
    }

    /// <summary>The target held to this model's reach, and to `cap` when the unit is held to one (Allowance); a model the Ask does not move is not held.</summary>
    public Point2 Clamp(Guid id, Point2 target, double? cap = null)
    {
        if (!starts.TryGetValue(id, out var start)) return target;
        if (!_reaches.TryGetValue(id, out var reach)) _reaches[id] = reach = MoveRuler.Reach(start, legal, volumes);
        var origin = start.Placement.Position.Horizontal;
        var held = MoveRuler.Clamp(origin, reach, target);
        var d = origin.DistanceTo(held);
        return cap is { } c && d > c ? origin + (held - origin) * (c / d) : held;
    }
}
