namespace Tabletop.Geometry;

/// <summary>
/// One obstacle in the moving base's configuration space: the set of base-centre positions where the base's interior meets the obstacle's.
/// For a base and an obstacle that are each a convex polygon or a circle, that set is exactly a convex core (a point or a convex polygon,
/// the Minkowski sum of the obstacle's core and the base's reflected core) grown by a radius (the sum of the two circles' radii). A position
/// is inside when its signed distance to the core is under the radius by more than Tolerance.Distance, which is Outline.InteriorsIntersect's test.
/// </summary>
internal sealed class MoveShape
{
    /// <summary>The core: one point, or a convex polygon counter-clockwise.</summary>
    public readonly Point2[] Core;
    public readonly double Radius;
    /// <summary>A circle holding the whole grown shape, for cheap rejection.</summary>
    public readonly Point2 Centre;
    public readonly double Bound;

    MoveShape(Point2[] core, double radius)
    {
        Core = core;
        Radius = radius;
        Centre = new Point2(core.Average(p => p.X), core.Average(p => p.Y));
        Bound = core.Max(p => p.DistanceTo(Centre)) + radius;
    }

    /// <summary>The shape `obstacle` makes for a base whose outline, placed at `origin`, is `mover`.</summary>
    public static MoveShape Of(Outline obstacle, Outline mover, Point2 origin)
    {
        var (oCore, oRadius) = Split(obstacle, new Point2(0, 0));
        var (mCore, mRadius) = Split(mover, origin);
        var reflected = mCore.Select(p => p * -1).ToArray();          // a half-turn keeps counter-clockwise order
        Point2[] core = (oCore.Length, reflected.Length) switch
        {
            (1, 1) => [oCore[0] + reflected[0]],
            (1, _) => reflected.Select(p => p + oCore[0]).ToArray(),
            (_, 1) => oCore.Select(p => p + reflected[0]).ToArray(),
            _ => MinkowskiSum(oCore, reflected),
        };
        return new MoveShape(core, oRadius + mRadius);
    }

    /// <summary>The Centre and Bound <see cref="Of"/> gives two circles, without building the shape; null for any other pair.</summary>
    public static (Point2 Centre, double Bound)? CircleBounds(Outline obstacle, Outline mover, Point2 origin) =>
        obstacle is CircleOutline o && mover is CircleOutline m ? (o.Centre + (m.Centre - origin) * -1, 0 + (o.Radius + m.Radius)) : null;

    /// <summary>An outline as a core and a radius, relative to `origin`.</summary>
    static (Point2[] Core, double Radius) Split(Outline outline, Point2 origin) => outline switch
    {
        CircleOutline c => ([c.Centre - origin], c.Radius),
        PolygonOutline p => (p.Vertices.Select(v => v - origin).ToArray(), 0),
        _ => throw new ArgumentOutOfRangeException(nameof(outline)),
    };

    /// <summary>The Minkowski sum of two convex counter-clockwise polygons, counter-clockwise, collinear vertices dropped.</summary>
    internal static Point2[] MinkowskiSum(IReadOnlyList<Point2> a, IReadOnlyList<Point2> b)
    {
        static int Lowest(IReadOnlyList<Point2> p)
        {
            var best = 0;
            for (var i = 1; i < p.Count; i++)
                if (p[i].Y < p[best].Y || (p[i].Y == p[best].Y && p[i].X < p[best].X)) best = i;
            return best;
        }
        int ia = Lowest(a), ib = Lowest(b), na = a.Count, nb = b.Count;
        var result = new List<Point2>(na + nb);
        int i = 0, j = 0;
        while (i < na || j < nb)
        {
            result.Add(a[(ia + i) % na] + b[(ib + j) % nb]);
            var ea = a[(ia + i + 1) % na] - a[(ia + i) % na];
            var eb = b[(ib + j + 1) % nb] - b[(ib + j) % nb];
            var cross = ea.Cross(eb);
            if (j >= nb || (i < na && cross > 0)) i++;
            else if (i >= na || cross < 0) j++;
            else { i++; j++; }
        }
        var hull = new List<Point2>(result.Count);
        for (var k = 0; k < result.Count; k++)
        {
            var prev = result[(k + result.Count - 1) % result.Count];
            var next = result[(k + 1) % result.Count];
            if (Math.Abs((result[k] - prev).Cross(next - result[k])) > 1e-12 || hull.Count == 0 && result.Count < 3) hull.Add(result[k]);
        }
        return hull.Count >= 3 ? hull.ToArray() : result.ToArray();
    }

    /// <summary>Signed distance from p to the core: negative inside a polygon core.</summary>
    public double CoreSigned(Point2 p)
    {
        if (Core.Length == 1) return p.DistanceTo(Core[0]);
        var inside = true; var deepest = double.NegativeInfinity;
        for (int i = 0, j = Core.Length - 1; i < Core.Length; j = i++)
        {
            var e = Core[i] - Core[j];
            var len = e.Length;
            var signed = e.Cross(p - Core[j]) / -len;                     // outward is to the right of a counter-clockwise edge
            if (signed > 0) inside = false;
            deepest = Math.Max(deepest, signed);
        }
        if (inside) return deepest;
        var best = double.PositiveInfinity;
        for (int i = 0, j = Core.Length - 1; i < Core.Length; j = i++) best = Math.Min(best, Outline.ClosestOnSegment(Core[j], Core[i], p).DistanceTo(p));
        return best;
    }

    /// <summary>How far p is outside the shape; negative inside.</summary>
    public double Depth(Point2 p) => CoreSigned(p) - Radius;

    /// <summary>The base at p meets the obstacle.</summary>
    public bool Holds(Point2 p) => Depth(p) < -Tolerance.Distance;

    /// <summary>The nearest point of the shape's boundary to p, from outside (p itself when inside).</summary>
    public Point2 Nearest(Point2 p)
    {
        if (Core.Length == 1) { var d = p - Core[0]; var len = d.Length; return len <= Radius || len < 1e-12 ? p : Core[0] + d * (Radius / len); }
        var signed = CoreSigned(p);
        if (signed < Radius) return p;
        var best = Core[0]; var bestD = double.PositiveInfinity;
        for (int i = 0, j = Core.Length - 1; i < Core.Length; j = i++)
        {
            var q = Outline.ClosestOnSegment(Core[j], Core[i], p);
            var d = q.DistanceTo(p);
            if (d < bestD) { bestD = d; best = q; }
        }
        return Radius <= 0 ? best : best + (p - best) * (Radius / bestD);
    }

    /// <summary>Some point of the segment a–b is inside the shape.</summary>
    public bool Blocks(Point2 a, Point2 b)
    {
        if (Radius > Tolerance.Distance) return SegmentDistance(a, b) < Radius - Tolerance.Distance;
        return Core.Length >= 3 && Clip(a, b) is not null;
    }

    /// <summary>The part of a–b inside the shape, as the parameters t in [0, 1] it spans; null when none is.</summary>
    public (double T0, double T1)? Interval(Point2 a, Point2 b)
    {
        if (Radius <= Tolerance.Distance) return Core.Length >= 3 ? Clip(a, b) : null;
        if (!Blocks(a, b)) return null;
        var d = b - a;
        double F(double t) => Depth(a + d * t) + Tolerance.Distance;          // negative inside; convex in t
        // Golden-section search for the deepest point, then bisect out to each side's crossing.
        double lo = 0, hi = 1;
        const double g = 0.6180339887498949;
        for (var k = 0; k < 60 && hi - lo > 1e-9; k++)
        {
            double m1 = hi - g * (hi - lo), m2 = lo + g * (hi - lo);
            if (F(m1) < F(m2)) hi = m2; else lo = m1;
        }
        var deepest = (lo + hi) / 2;
        if (F(deepest) >= 0) return null;
        double Cross(double outside, double inside)
        {
            if (F(outside) < 0) return outside;
            for (var k = 0; k < 50; k++) { var mid = (outside + inside) / 2; if (F(mid) < 0) inside = mid; else outside = mid; }
            return inside;
        }
        return (Cross(0, deepest), Cross(1, deepest));
    }

    /// <summary>
    /// Interval(a, b) is not null, known without its search: the segment comes inside by clearly more than the search's own error (its deepest
    /// point found to 1e-9 of the length). False only says "not known".
    /// </summary>
    public bool SurelyEnters(Point2 a, Point2 b)
    {
        if (Radius <= Tolerance.Distance) return Core.Length >= 3 && Clip(a, b) is not null;
        return SegmentDistance(a, b) < Radius - Tolerance.Distance - 1e-6 - 1e-8 * (b - a).Length;
    }

    /// <summary>Unsigned distance from the segment to the core: 0 when they meet.</summary>
    double SegmentDistance(Point2 a, Point2 b)
    {
        if (Core.Length == 1) return Outline.ClosestOnSegment(a, b, Core[0]).DistanceTo(Core[0]);
        if (CoreSigned(a) <= 0 || CoreSigned(b) <= 0) return 0;
        for (int i = 0, j = Core.Length - 1; i < Core.Length; j = i++)
            if (Outline.SegmentsIntersect(a, b, Core[j], Core[i])) return 0;
        var best = Math.Min(CoreSigned(a), CoreSigned(b));
        foreach (var v in Core) best = Math.Min(best, Outline.ClosestOnSegment(a, b, v).DistanceTo(v));
        return best;
    }

    /// <summary>Cyrus–Beck: the part of a–b more than Tolerance.Distance inside a polygon core.</summary>
    (double, double)? Clip(Point2 a, Point2 b)
    {
        double t0 = 0, t1 = 1;
        var d = b - a;
        for (int i = 0, j = Core.Length - 1; i < Core.Length; j = i++)
        {
            var e = Core[i] - Core[j];
            var len = e.Length;
            // Inside this edge's inset half-plane: signed(p) = e.Cross(p - Core[j]) / -len < -tol, i.e. e.Cross(p - Core[j]) > tol * len.
            var num = e.Cross(a - Core[j]) - Tolerance.Distance * len;
            var den = e.Cross(d);
            if (Math.Abs(den) < 1e-15) { if (num <= 0) return null; continue; }
            var t = -num / den;
            if (den > 0) t0 = Math.Max(t0, t); else t1 = Math.Min(t1, t);
            if (t0 >= t1) return null;
        }
        return (t0, t1);
    }

    /// <summary>
    /// The graph's corners round this shape: its boundary pushed out by `margin` and circumscribed by a polygon, so every corner and the edge between
    /// two neighbours lies outside the shape. A polygon core's corner gets one, pushed out along its bisector; a rounded corner gets enough to keep the
    /// polygon's extra length round it small (about 0.02" on a half-turn), at most 64 to a full turn. In counter-clockwise order.
    /// </summary>
    public List<Point2> Ring(double margin)
    {
        var ring = new List<Point2>();
        var r = Radius + margin;
        if (Core.Length == 1)
        {
            AddArc(ring, Core[0], 0, 2 * Math.PI, r);
            return ring;
        }
        for (var i = 0; i < Core.Length; i++)
        {
            var prev = Core[(i + Core.Length - 1) % Core.Length];
            var v = Core[i];
            var next = Core[(i + 1) % Core.Length];
            var nIn = Normal(prev, v);
            var nOut = Normal(v, next);
            var a0 = Math.Atan2(nIn.Y, nIn.X);
            var sweep = Math.Atan2(nIn.Cross(nOut), nIn.Dot(nOut));
            if (sweep < 0) sweep += 2 * Math.PI;
            if (Radius <= Tolerance.Distance)
            {
                var bisector = nIn + nOut;
                var len = bisector.Length;
                ring.Add(len < 1e-9 ? v + nIn * margin : v + bisector * (margin / Math.Cos(sweep / 2) / len));
            }
            else AddArc(ring, v, a0, sweep, r);
        }
        return ring;
    }

    static Point2 Normal(Point2 from, Point2 to)
    {
        var e = to - from;
        return new Point2(e.Y, -e.X) * (1 / e.Length);
    }

    static void AddArc(List<Point2> ring, Point2 centre, double start, double sweep, double radius)
    {
        var half = Math.Min(Math.PI / 8, Math.Sqrt(0.06 / (Math.PI * Math.Max(radius, 1e-3))));
        var k = Math.Max(1, (int)Math.Ceiling(sweep / (2 * half)));
        k = Math.Min(k, (int)Math.Ceiling(64 * sweep / (2 * Math.PI)));
        var step = sweep / k;
        var reach = radius / Math.Cos(step / 2);
        for (var j = 0; j < k; j++)
        {
            var angle = start + (j + 0.5) * step;
            ring.Add(centre + new Point2(Math.Cos(angle), Math.Sin(angle)) * reach);
        }
    }
}
