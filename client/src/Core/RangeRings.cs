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

    /// <summary>Per ring, in angle: the crossings with every other carrier's ring are bisected; an interval between crossings stays when its middle lies
    /// outside every other ring, sampled at about Points per full turn with the crossings as its ends, so neighbouring arcs meet.</summary>
    static IReadOnlyList<(Point2, Point2)> Union(IReadOnlyList<PlacedModel> models, double range)
    {
        const int Scan = 8 * Points; const double Turn = 2 * Math.PI;
        var segments = new List<(Point2, Point2)>();
        for (var i = 0; i < models.Count; i++)
        {
            var m = models[i]; var others = models.Where((_, j) => j != i).ToList();
            var crossings = new List<double>();
            var scan = Enumerable.Range(0, Scan + 1).Select(k => Reach(m, range, Turn * k / Scan)).ToList();
            foreach (var o in others)
            {
                double F(double a) => Distance.EdgeToEdge(o, Reach(m, range, a)) - range;
                for (var k = 0; k < Scan; k++)
                {
                    double lo = Turn * k / Scan, hi = Turn * (k + 1) / Scan, flo = Distance.EdgeToEdge(o, scan[k]) - range, fhi = Distance.EdgeToEdge(o, scan[k + 1]) - range;
                    if (flo < 0 == fhi < 0) continue;
                    for (var n = 0; n < 50; n++) { var mid = (lo + hi) / 2; if (F(mid) < 0 == flo < 0) lo = mid; else hi = mid; }
                    crossings.Add((lo + hi) / 2 % Turn);
                }
            }
            bool Outside(double a) { var q = Reach(m, range, a); return others.All(o => Distance.EdgeToEdge(o, q) >= range); }
            void Arc(double from, double to, int count)
            {
                var pts = Enumerable.Range(0, count + 1).Select(k => Reach(m, range, from + (to - from) * k / count)).ToList();
                if (to - from >= Turn) pts[count] = pts[0];                                                                     // a whole ring closes on itself
                for (var k = 0; k < count; k++) segments.Add((pts[k], pts[k + 1]));
            }
            if (crossings.Count == 0) { if (Outside(0)) Arc(0, Turn, Points); continue; }
            crossings.Sort();
            for (var c = 0; c < crossings.Count; c++)
            {
                var from = crossings[c]; var to = c + 1 < crossings.Count ? crossings[c + 1] : crossings[0] + Turn;
                if (to - from > 1e-9 && Outside((from + to) / 2)) Arc(from, to, Math.Max(1, (int)Math.Ceiling((to - from) / Turn * Points)));
            }
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
