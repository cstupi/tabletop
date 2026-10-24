using System.Globalization; using Tabletop.Content; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

public sealed record RangeRing(double Inches, string Label, IReadOnlyList<(Point2 A, Point2 B)> Segments);

/// <summary>Spec → Range rings: per distinct printed Range, the union outline of the rings at that distance from the base edge of each model on the table
/// carrying a weapon of that range. No Range, "-" or 0 is melee and draws none. The label is the printed value; a rule that changes a range is not shown.</summary>
public static class RangeRings
{
    public const int Points = RangeOutline.Points;
    public static double? Inches(string? printed) => double.TryParse(printed?.Trim().TrimEnd('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r > 0 ? r : null;

    public static IReadOnlyList<RangeRing> Of(Projection p, Guid unitId)
    {
        var models = p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable)
            .Select(m => (Ranges: m.Slot.Weapons.Select(w => Inches(w.Stats.GetValueOrDefault("Range"))).OfType<double>().ToHashSet(), Placed: new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement))).ToList();
        var printed = p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).SelectMany(m => m.Slot.Weapons).Select(w => w.Stats.GetValueOrDefault("Range")).OfType<string>()
            .Where(r => Inches(r) is not null).DistinctBy(r => Inches(r)).OrderBy(r => Inches(r));
        return printed.Select(r => { var d = Inches(r)!.Value; return new RangeRing(d, r.Trim(), RangeOutline.Of(models.Where(m => m.Ranges.Contains(d)).Select(m => m.Placed).ToList(), d)); }).ToList();
    }

    /// <summary>What <see cref="Of"/> reads: the unit's models on the table, their slots and placements. Equal keys give equal rings.</summary>
    public static IReadOnlyList<(Guid Model, ModelSlot Slot, Placement At)> Key(Projection p, Guid unitId) =>
        p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).Select(m => (m.Id, m.Slot, ((OnTable)m.Location).Placement)).ToList();
}

/// <summary>One unit's range rings, recomputed only when the unit asked about or its models' slots or placements change (<see cref="RangeRings.Key"/>).</summary>
public sealed class RangeRingCache
{
    Guid _unit; IReadOnlyList<(Guid, ModelSlot, Placement)>? _key; IReadOnlyList<RangeRing> _rings = [];

    public IReadOnlyList<RangeRing> Of(Projection p, Guid unitId)
    {
        var key = RangeRings.Key(p, unitId);
        if (_key is not null && unitId == _unit && key.SequenceEqual(_key)) return _rings;
        (_unit, _key, _rings) = (unitId, key, RangeRings.Of(p, unitId));
        return _rings;
    }
}
