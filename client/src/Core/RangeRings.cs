using System.Globalization; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

public sealed record RangeRing(double Inches, string Label, IReadOnlyList<(Point2 A, Point2 B)> Segments);

/// <summary>Spec → Range rings: per distinct printed Range, the union outline of the rings at that distance from the base edge of each model on the table
/// carrying a weapon of that range. No Range, "-" or 0 is melee and draws none. The label is the printed value; a rule that changes a range is not shown.</summary>
public static class RangeRings
{
    public const int Points = 96;
    public static double? Inches(string? printed) => double.TryParse(printed?.Trim().TrimEnd('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r > 0 ? r : null;

    public static IReadOnlyList<RangeRing> Of(Projection p, Guid unitId)
    {
        var models = p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable)
            .Select(m => (Ranges: m.Slot.Weapons.Select(w => Inches(w.Stats.GetValueOrDefault("Range"))).OfType<double>().ToHashSet(), Placed: new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement))).ToList();
        var printed = p.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).SelectMany(m => m.Slot.Weapons).Select(w => w.Stats.GetValueOrDefault("Range")).OfType<string>()
            .Where(r => Inches(r) is not null).DistinctBy(r => Inches(r)).OrderBy(r => Inches(r));
        return printed.Select(r => { var d = Inches(r)!.Value; return new RangeRing(d, r.Trim(), Union(models.Where(m => m.Ranges.Contains(d)).Select(m => m.Placed).ToList(), d)); }).ToList();
    }

    /// <summary>Each ring sampled on Points rays from its model's centre; a segment stays when neither end is inside another carrier's ring.</summary>
    static IReadOnlyList<(Point2, Point2)> Union(IReadOnlyList<PlacedModel> models, double range)
    {
        var segments = new List<(Point2, Point2)>();
        for (var i = 0; i < models.Count; i++)
        {
            var ring = Enumerable.Range(0, Points).Select(k => Reach(models[i], range, 2 * Math.PI * k / Points)).ToList();
            bool Outside(Point2 q) => models.Where((_, j) => j != i).All(o => Distance.EdgeToEdge(o, q) >= range - 1e-6);
            for (var k = 0; k < Points; k++) if (Outside(ring[k]) && Outside(ring[(k + 1) % Points])) segments.Add((ring[k], ring[(k + 1) % Points]));
        }
        return segments;
    }

    /// <summary>The point on the ray at `range` from the base edge, bisected: a base is convex, so the edge distance grows along the ray.</summary>
    static Point2 Reach(PlacedModel m, double range, double angle)
    {
        var c = m.Placement.Position.Horizontal; var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
        double lo = 0, hi = Math.Max(m.Body.Base.A, m.Body.Base.B) + range;
        for (var n = 0; n < 40; n++) { var mid = (lo + hi) / 2; if (Distance.EdgeToEdge(m, c + dir * mid) < range) lo = mid; else hi = mid; }
        return c + dir * lo;
    }
}
