using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// What hovering a unit shows (spec: in-game UX → Hover a unit): its name in its team's colour; with a unit selected, the distance between
/// the two units and the two base-edge points it runs between; the wounds left of each model that takes more than one.
/// </summary>
public sealed record HoverInfo(Guid UnitId, string Name, int TeamOrdinal, double? Distance, Point3? From, Point3? To, IReadOnlyList<(Guid ModelId, int Left, int Max)> Wounds);

public static class HoverView
{
    /// <summary>
    /// Null when the hovered unit is unknown or has no model on the table. <paramref name="selectedUnit"/> is the caller's selection when it is
    /// one of my units, else null. Distance is null without one, when it is the hovered unit, or when none of its models is on the table.
    /// Team ordinal 0: the unit's participant has no team. An id absent from <paramref name="maxWounds"/> takes one wound.
    /// </summary>
    /// <summary>The multi-wound models' wounds left as one line under the name, like ones counted (`3/3 ×2 · 1/3`), in model order; null when
    /// none takes more than one.</summary>
    public static string? WoundsLine(IReadOnlyList<(Guid ModelId, int Left, int Max)> wounds) =>
        wounds.Count == 0 ? null : string.Join(" · ", wounds.GroupBy(w => (w.Left, w.Max)).Select(g => g.Count() > 1 ? $"{g.Key.Left}/{g.Key.Max} ×{g.Count()}" : $"{g.Key.Left}/{g.Key.Max}"));

    public static HoverInfo? Of(Projection p, GameInfo game, Names n, Guid hoveredUnit, Guid? selectedUnit, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        if (!p.Units.TryGetValue(hoveredUnit, out var unit)) return null;
        var hovered = OnTable(p, hoveredUnit);
        if (hovered.Count == 0) return null;
        var teamId = game.Participants.FirstOrDefault(x => x.Id == unit.ParticipantId)?.TeamId;
        var ordinal = game.Teams.FirstOrDefault(t => t.Id == teamId)?.Ordinal ?? 0;

        double? distance = null; Point3? from = null, to = null;
        if (selectedUnit is { } sel && sel != hoveredUnit && OnTable(p, sel) is { Count: > 0 } selected)
        {
            // p.5: between two units, from/to the closest model in each; between two models, from/to the closest point of their bases.
            var (a, b) = selected.SelectMany(s => hovered.Select(h => (s.Placed, h.Placed))).MinBy(pair => Distance.EdgeToEdge(pair.Item1, pair.Item2));
            distance = Distance.EdgeToEdge(a, b);
            (from, to) = Distance.ClosestPoints(a, b);
        }

        var wounds = hovered.Select(m => (m.Model.Id, Max: maxWounds.GetValueOrDefault(m.Model.Id, 1), m.Model.Wounds))
            .Where(m => m.Max > 1).Select(m => (m.Id, Math.Max(0, m.Max - m.Wounds), m.Max)).ToList();
        return new HoverInfo(hoveredUnit, n.Unit(hoveredUnit), ordinal, distance, from, to, wounds);
    }

    /// <summary>The unit's models on the table, by slot ordinal, each with its placed body.</summary>
    static IReadOnlyList<(ModelInPlay Model, PlacedModel Placed)> OnTable(Projection p, Guid unitId) =>
        p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).OrderBy(m => m.Slot.Ordinal)
            .Select(m => (m, new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement))).ToList();
}
