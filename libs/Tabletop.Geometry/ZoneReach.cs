namespace Tabletop.Geometry;

/// <summary>
/// The area a base placed "fully within X of their deployment zone" stands in (OPR p.14 Scout 12", 3.6.0 p.15 9";
/// <see cref="ZoneStyles.BaseFullyWithin(string, TableSize, PlacedModel, double, ZoneLegs?)"/> judges it): each of the zone's convex polygons grown by
/// the reach, its corners rounded, cut to the table, and the pieces' union as outline segments, outer loops counter-clockwise (as RangeOutline's).
/// </summary>
public static class ZoneReach
{
    /// <summary>Arc points per full turn, as RangeOutline.Points.</summary>
    public const int Points = RangeOutline.Points;
    const double Eps = 1e-9;

    public static IReadOnlyList<(Point2 A, Point2 B)> Outline(IReadOnlyList<Polygon> zone, double inches, TableSize table)
    {
        var pieces = zone.Select(z => Clip(Grow(CounterClockwise(z.Vertices), Math.Max(0, inches)), table)).Where(p => p.Count >= 3).ToList();
        var segments = new List<(Point2, Point2)>();
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            for (var k = 0; k < piece.Count; k++)
            {
                var a = piece[k]; var b = piece[(k + 1) % piece.Count];
                // Split the edge where it crosses another piece's edges; keep the parts no other piece holds inside.
                var cuts = new List<double> { 0, 1 };
                for (var j = 0; j < pieces.Count; j++)
                    if (j != i)
                        for (var m = 0; m < pieces[j].Count; m++)
                            if (Crossing(a, b, pieces[j][m], pieces[j][(m + 1) % pieces[j].Count]) is { } t) cuts.Add(t);
                cuts.Sort();
                for (var c = 0; c + 1 < cuts.Count; c++)
                {
                    if (cuts[c + 1] - cuts[c] < Eps) continue;
                    var p = a + (b - a) * cuts[c]; var q = a + (b - a) * cuts[c + 1];
                    var mid = (p + q) * 0.5;
                    // An edge two pieces share along the table's edge is drawn once, from the first.
                    if (!pieces.Where((_, j) => j != i).Any(o => StrictlyInside(o, mid)) && !pieces.Take(i).Any(o => OnBoundary(o, mid))) segments.Add((p, q));
                }
            }
        }
        return segments;
    }

    static List<Point2> CounterClockwise(IReadOnlyList<Point2> v)
    {
        var area = 0.0;
        for (var i = 0; i < v.Count; i++) area += v[i].Cross(v[(i + 1) % v.Count]);
        var list = v.ToList();
        if (area < 0) list.Reverse();
        return list;
    }

    /// <summary>A convex counter-clockwise polygon grown by r: each edge pushed out along its normal, an arc about each vertex between them.</summary>
    static List<Point2> Grow(List<Point2> v, double r)
    {
        if (r <= 0) return v;
        var n = v.Count;
        var result = new List<Point2>();
        for (var i = 0; i < n; i++)
        {
            var prev = v[(i + n - 1) % n]; var at = v[i]; var next = v[(i + 1) % n];
            var a0 = Math.Atan2(-(at.X - prev.X), at.Y - prev.Y);        // the incoming edge's outward normal (right of the direction, counter-clockwise)
            var a1 = Math.Atan2(-(next.X - at.X), next.Y - at.Y);
            var sweep = a1 - a0;
            while (sweep < 0) sweep += 2 * Math.PI;
            while (sweep >= 2 * Math.PI) sweep -= 2 * Math.PI;
            var steps = Math.Max(1, (int)Math.Ceiling(sweep / (2 * Math.PI) * Points));
            for (var s = 0; s <= steps; s++)
            {
                var angle = a0 + sweep * s / steps;
                result.Add(new Point2(at.X + r * Math.Cos(angle), at.Y + r * Math.Sin(angle)));
            }
        }
        return result;
    }

    /// <summary>Sutherland–Hodgman against the table's four edges.</summary>
    static List<Point2> Clip(List<Point2> poly, TableSize table)
    {
        poly = ClipEdge(poly, true, 0, true); poly = ClipEdge(poly, true, table.Width, false);
        poly = ClipEdge(poly, false, 0, true); poly = ClipEdge(poly, false, table.Length, false);
        // Drop repeated points the clip leaves where a vertex lies on an edge.
        var clean = new List<Point2>();
        foreach (var p in poly) if (clean.Count == 0 || clean[^1].DistanceTo(p) > Eps) clean.Add(p);
        while (clean.Count > 1 && clean[0].DistanceTo(clean[^1]) <= Eps) clean.RemoveAt(clean.Count - 1);
        return clean;
    }

    static List<Point2> ClipEdge(List<Point2> poly, bool alongX, double at, bool keepAbove)
    {
        double Axis(Point2 p) => alongX ? p.X : p.Y;
        bool In(Point2 p) => keepAbove ? Axis(p) >= at - Eps : Axis(p) <= at + Eps;
        var output = new List<Point2>();
        for (var i = 0; i < poly.Count; i++)
        {
            var cur = poly[i]; var prev = poly[(i + poly.Count - 1) % poly.Count];
            if (In(cur))
            {
                if (!In(prev)) output.Add(Cut(prev, cur));
                output.Add(cur);
            }
            else if (In(prev)) output.Add(Cut(prev, cur));
        }
        return output;

        Point2 Cut(Point2 p, Point2 q)
        {
            var c = p + (q - p) * ((at - Axis(p)) / (Axis(q) - Axis(p)));
            return alongX ? c with { X = at } : c with { Y = at };
        }
    }

    /// <summary>The parameter along a→b where it properly crosses c→d, or null.</summary>
    static double? Crossing(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        var r = b - a; var s = d - c;
        var den = r.Cross(s);
        if (Math.Abs(den) < Eps) return null;
        var t = (c - a).Cross(s) / den; var u = (c - a).Cross(r) / den;
        return t > Eps && t < 1 - Eps && u >= -Eps && u <= 1 + Eps ? t : null;
    }

    static bool OnBoundary(List<Point2> convex, Point2 p)
    {
        for (var i = 0; i < convex.Count; i++)
            if (Polygon.OnSegment(convex[i], convex[(i + 1) % convex.Count], p)) return true;
        return false;
    }

    static bool StrictlyInside(List<Point2> convex, Point2 p)
    {
        for (var i = 0; i < convex.Count; i++)
            if ((convex[(i + 1) % convex.Count] - convex[i]).Cross(p - convex[i]) <= 1e-7) return false;
        return true;
    }
}
