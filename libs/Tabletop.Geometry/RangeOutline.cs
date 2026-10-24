namespace Tabletop.Geometry;

/// <summary>The union outline of the rings at `range` from the base edge of each model.</summary>
public static class RangeOutline
{
    public const int Points = 96;
    const int Scan = 8 * Points; const double Turn = 2 * Math.PI;

    /// <summary>Per ring, in angle: the crossings with every nearby carrier's ring are bisected; an interval between crossings stays when its middle lies
    /// outside every other ring, sampled at about Points per full turn with the crossings as its ends, so neighbouring arcs meet. Each ring is scanned once;
    /// a carrier whose ring cannot reach this one is skipped, and a point is measured exactly only when the two bounding circles of a base leave it undecided.</summary>
    public static IReadOnlyList<(Point2 A, Point2 B)> Of(IReadOnlyList<PlacedModel> models, double range)
    {
        var edges = models.Select(m => new BaseEdge(m)).ToArray();
        var scans = edges.Select(e => Enumerable.Range(0, Scan + 1).Select(k => Reach(e, range, Turn * k / Scan)).ToArray()).ToArray();
        var segments = new List<(Point2 A, Point2 B)>();
        for (var i = 0; i < edges.Length; i++)
        {
            var m = edges[i]; var scan = scans[i];
            var others = edges.Where((o, j) => j != i && o.Centre.DistanceTo(m.Centre) <= m.Outer + o.Outer + 2 * range + 1e-9).ToList();   // farther off, the rings neither cross nor cover
            var crossings = new List<double>();
            var inside = new bool[Scan + 1];
            foreach (var o in others)
            {
                for (var k = 0; k <= Scan; k++) inside[k] = Within(o, scan[k], range);
                for (var k = 0; k < Scan; k++)
                {
                    if (inside[k] == inside[k + 1]) continue;
                    double lo = Turn * k / Scan, hi = Turn * (k + 1) / Scan;
                    for (var n = 0; n < 20; n++) { var mid = (lo + hi) / 2; if (Within(o, Reach(m, range, mid), range) == inside[k]) lo = mid; else hi = mid; }
                    crossings.Add((lo + hi) / 2 % Turn);
                }
            }
            bool Outside(double a) { var q = Reach(m, range, a); return others.All(o => !Within(o, q, range)); }
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

    /// <summary>Is q nearer than range to the base's edge. Decided by the base's bounding circles where they agree, measured otherwise.</summary>
    static bool Within(BaseEdge o, Point2 q, double range)
    {
        var r = o.Centre.DistanceTo(q);
        if (r - o.Outer >= range) return false;
        if (r - o.Inner < range) return true;
        return o.To(q) < range;
    }

    /// <summary>The point on the ray at `range` from the base edge. A round base grows by the range; any other lies between its two bounding circles, so the
    /// point is between range + Inner and range + Outer from the centre, where the edge distance only grows along the ray: regula falsi (Illinois) finds it.</summary>
    static Point2 Reach(BaseEdge e, double range, double angle)
    {
        var c = e.Centre; var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
        if (e.Outer - e.Inner < 1e-12) return c + dir * (e.Outer + range);
        double a = range + e.Inner, b = range + e.Outer, fa = e.To(c + dir * a) - range, fb = e.To(c + dir * b) - range;
        if (fa >= 0) return c + dir * a; if (fb <= 0) return c + dir * b;
        var side = 0; var t = a;
        for (var n = 0; n < 60 && b - a > 1e-12; n++)
        {
            t = (a * fb - b * fa) / (fb - fa); var ft = e.To(c + dir * t) - range;
            if (Math.Abs(ft) < 1e-12) break;
            if (ft > 0) { b = t; fb = ft; if (side == -1) fa /= 2; side = -1; }
            else { a = t; fa = ft; if (side == 1) fb /= 2; side = 1; }
        }
        return c + dir * t;
    }
}
