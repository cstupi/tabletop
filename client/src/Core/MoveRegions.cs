using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// One move Ask's regions, per model from where it stands: the outline drawn round each held model (MoveRuler.Region, with walls, the
/// other units' models and the table), the minimum's inner edge, and the reach a drag is clamped to (MoveRuler.Reach: allowance and
/// difficult cap). Each is computed the first time it is asked for and kept for the Ask.
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

    /// <summary>The least this model must move (MoveRuler.Minimum), or null when the Ask sets none.</summary>
    public double? Minimum(Guid id) => MoveRuler.Minimum(starts[id], legal, size);

    /// <summary>The inner edge a minimum cuts from this model's outline, or null.</summary>
    public IReadOnlyList<Point2>? Inner(Guid id) =>
        Minimum(id) is { } least ? MoveRuler.Inner(starts[id].Placement.Position.Horizontal, Outline(id), least) : null;

    /// <summary>The target held to this model's reach; a model the Ask does not move is not held.</summary>
    public Point2 Clamp(Guid id, Point2 target)
    {
        if (!starts.TryGetValue(id, out var start)) return target;
        if (!_reaches.TryGetValue(id, out var reach)) _reaches[id] = reach = MoveRuler.Reach(start, legal, volumes);
        return MoveRuler.Clamp(start.Placement.Position.Horizontal, reach, target);
    }
}
