using System.Globalization; using Tabletop.Content; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>What a ring measures: a weapon's printed range (V), a threat's charge or shooting reach, or the area a placement keeps out of
/// (<see cref="KeepAwayArea"/>). Its look follows (<see cref="RingLook"/>).</summary>
public enum RingKind { Range, Charge, Shoot, KeepAway }

public sealed record RangeRing(double Inches, string Label, IReadOnlyList<(Point2 A, Point2 B)> Segments, RingKind Kind = RingKind.Range);

/// <summary>Spec → Range rings: per distinct printed Range, the union outline of the rings at that distance from the base edge of each model on the table
/// carrying a weapon of that range. No Range, "-" or 0 is melee and draws none. The label is the printed value; a rule that changes a range is not shown.</summary>
public static class RangeRings
{
    public const int Points = RangeOutline.Points;
    public static double? Inches(string? printed) => double.TryParse(printed?.Trim().TrimEnd('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r > 0 ? r : null;

    public static IReadOnlyList<RangeRing> Of(Projection p, Guid unitId) => Builder(p, unitId)();

    /// <summary>The unit whose weapon ranges draw: the unit the card shows (SheetFocus: the selected unit, else my team's activating unit) when it
    /// is of the team I act for; null for another team's unit, whose threat rings draw instead, and for a watcher.</summary>
    public static Guid? UnitFor(Projection p, GameInfo game, Guid? myTeamId, Guid? selectedPiece) =>
        myTeamId is { } mine && SheetFocus.Of(p, game, mine, selectedPiece) is { } unit && SheetFocus.TeamOf(p, game, unit)?.Id == mine ? unit : null;

    /// <summary><see cref="Of"/> in two halves: the unit's models and printed ranges read now, on the caller's thread; the outlines built when the returned function runs, on any thread.</summary>
    public static Func<IReadOnlyList<RangeRing>> Builder(Projection p, Guid unitId)
    {
        var models = p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable)
            .Select(m => (Ranges: m.Slot.Weapons.Select(w => Inches(w.Stats.GetValueOrDefault("Range"))).OfType<double>().ToHashSet(), Placed: new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement))).ToList();
        var printed = p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).SelectMany(m => m.Slot.Weapons).Select(w => w.Stats.GetValueOrDefault("Range")).OfType<string>()
            .Where(r => Inches(r) is not null).DistinctBy(r => Inches(r)).OrderBy(r => Inches(r)).ToList();
        return () => printed.Select(r => { var d = Inches(r)!.Value; return new RangeRing(d, r.Trim(), RangeOutline.Of(models.Where(m => m.Ranges.Contains(d)).Select(m => m.Placed).ToList(), d)); }).ToList();
    }

    /// <summary>What <see cref="Of"/> reads: the unit's models on the table, their slots and placements. Equal keys give equal rings.</summary>
    public static IReadOnlyList<(Guid Model, ModelSlot Slot, Placement At)> Key(Projection p, Guid unitId) =>
        p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).Select(m => (m.Id, m.Slot, ((OnTable)m.Location).Placement)).ToList();
}
