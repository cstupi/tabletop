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

    /// <summary>
    /// A many-sided core's coarse bounds (an oval base has 64 sides): the circles about Centre inside it (`_inner`) and holding it (`_outer`), and
    /// polygons of a few sides inside it (some of its own vertices) and holding it (its support lines in a few directions). A distance to one of
    /// them bounds the distance to the core, so most tests are settled on them and the core itself is measured only near the limit.
    /// </summary>
    readonly double _inner, _outer;
    readonly Edged? _within, _around;
    readonly Edged _core;
    const int CoarseSides = 16;
    /// <summary>What the bounds leave to the exact test: well above the rounding in either distance.</summary>
    const double Slack = 1e-9;

    MoveShape(Point2[] core, double radius)
    {
        Core = core;
        Radius = radius;
        Centre = new Point2(core.Average(p => p.X), core.Average(p => p.Y));
        Bound = core.Max(p => p.DistanceTo(Centre)) + radius;
        _core = new Edged(core);
        if (core.Length <= 2 * CoarseSides) return;
        _outer = Bound - radius;
        _inner = double.PositiveInfinity;
        for (int i = 0, j = core.Length - 1; i < core.Length; j = i++)
        {
            var e = core[i] - core[j];
            _inner = Math.Min(_inner, e.Cross(Centre - core[j]) / e.Length);
        }
        _inner = Math.Max(0, _inner);
        var normals = Enumerable.Range(0, CoarseSides)
            .Select(k => new Point2(Math.Cos(2 * Math.PI * k / CoarseSides), Math.Sin(2 * Math.PI * k / CoarseSides))).ToArray();
        var support = normals.Select(n => core.Max(v => v.Dot(n))).ToArray();
        var around = new Point2[CoarseSides];
        for (var k = 0; k < CoarseSides; k++)
        {
            var (n1, n2) = (normals[k], normals[(k + 1) % CoarseSides]);
            var (h1, h2) = (support[k], support[(k + 1) % CoarseSides]);
            var det = n1.Cross(n2);
            around[k] = new Point2((h1 * n2.Y - h2 * n1.Y) / det, (n1.X * h2 - n2.X * h1) / det);
        }
        _around = new Edged(around);
        var within = new List<Point2>();
        foreach (var n in normals)
        {
            var v = core.MaxBy(v => v.Dot(n));
            if (within.Count == 0 || (within[^1] - v).Length > 1e-9) within.Add(v);
        }
        while (within.Count > 1 && (within[^1] - within[0]).Length <= 1e-9) within.RemoveAt(within.Count - 1);
        if (within.Count >= 3) _within = new Edged([.. within]);
    }

    /// <summary>SegmentDistance(Core, a, b) &lt; limit, exactly: settled on the coarse bounds when they clear the limit by Slack, else measured.</summary>
    bool SegmentWithin(Point2 a, Point2 b, double limit)
    {
        if (_around is not null)
        {
            var s = Outline.ClosestOnSegment(a, b, Centre).DistanceTo(Centre);
            if (s - _outer >= limit + Slack) return false;
            if (s - _inner < limit - Slack) return true;
            if (SegmentDistance(_around, a, b) >= limit + Slack) return false;
            if (_within is not null && SegmentDistance(_within, a, b) < limit - Slack) return true;
        }
        return SegmentDistance(_core, a, b) < limit;
    }

    /// <summary>Clip(a, b) is not null for a core with no radius, exactly: settled on the circles about Centre when they clear it by Slack.</summary>
    bool Enters(Point2 a, Point2 b)
    {
        if (Core.Length < 3) return false;
        if (_around is not null)
        {
            var s = Outline.ClosestOnSegment(a, b, Centre).DistanceTo(Centre);
            if (s - _outer >= Slack) return false;
            if (s < _inner - Tolerance.Distance - Slack) return true;
        }
        return Clip(a, b) is not null;
    }

    /// <summary>A convex polygon (or one point) with the length of each edge, edge i running from vertex i − 1 to vertex i.</summary>
    sealed class Edged(Point2[] points)
    {
        public readonly Point2[] Points = points;
        public readonly double[] Lengths = points.Select((p, i) => (p - points[(i + points.Length - 1) % points.Length]).Length).ToArray();
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
    public double CoreSigned(Point2 p) => CoreSigned(_core, p);

    /// <summary>
    /// Outside, the least distance to an edge, each measured as ClosestOnSegment does; an edge whose line is already farther than the least so far
    /// (its line distance bounds its segment distance from below, by more than the rounding in either) cannot be the least, so is not measured.
    /// </summary>
    static double CoreSigned(Edged polygon, Point2 p)
    {
        var core = polygon.Points;
        if (core.Length == 1) return p.DistanceTo(core[0]);
        var n = core.Length;
        Span<double> signs = n <= 256 ? stackalloc double[n] : new double[n];
        var inside = true; var deepest = double.NegativeInfinity; var facing = 0;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var e = core[i] - core[j];
            var signed = e.Cross(p - core[j]) / -polygon.Lengths[i];       // outward is to the right of a counter-clockwise edge
            signs[i] = signed;
            if (signed > 0) inside = false;
            if (signed > signs[facing]) facing = i;
            deepest = Math.Max(deepest, signed);
        }
        if (inside) return deepest;
        var best = Outline.ClosestOnSegment(core[(facing + n - 1) % n], core[facing], p).DistanceTo(p);
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (i == facing || Math.Abs(signs[i]) > best + Slack) continue;
            best = Math.Min(best, Outline.ClosestOnSegment(core[j], core[i], p).DistanceTo(p));
        }
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
        if (Radius > Tolerance.Distance) return SegmentWithin(a, b, Radius - Tolerance.Distance);
        return Enters(a, b);
    }

    /// <summary>
    /// The part of a–b inside the shape, as the parameters t in [0, 1] it spans; null when none is. Inside is a core distance under
    /// Radius − Tolerance.Distance: the core's interior, a band of that width outside each core edge and a disc of that radius about each core
    /// vertex. Each meets the line in an interval found in closed form, and the shape is convex, so their union is the one interval from the least
    /// start to the greatest end.
    /// </summary>
    public (double T0, double T1)? Interval(Point2 a, Point2 b)
    {
        if (Radius <= Tolerance.Distance) return Core.Length >= 3 ? Clip(a, b) : null;
        if (!Blocks(a, b)) return null;
        var d = b - a;
        var rho = Radius - Tolerance.Distance;
        if (d.Dot(d) < 1e-24) return (0, 1);
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        void Take((double, double)? span)
        {
            if (span is not var (t0, t1)) return;
            lo = Math.Min(lo, t0); hi = Math.Max(hi, t1);
        }
        if (Core.Length == 1) Take(Disc(a, d, Core[0], rho));
        else
        {
            Take(Slab(a, d, Core));
            for (int i = 0, j = Core.Length - 1; i < Core.Length; j = i++)
            {
                Take(Disc(a, d, Core[i], rho));
                Take(Band(a, d, Core[j], Core[i], rho));
            }
        }
        lo = Math.Max(lo, 0); hi = Math.Min(hi, 1);
        return lo < hi ? (lo, hi) : null;
    }

    /// <summary>The t where |a + t·d − c| &lt; r, unclamped.</summary>
    static (double, double)? Disc(Point2 a, Point2 d, Point2 c, double r)
    {
        var w = a - c;
        double qa = d.Dot(d), qb = w.Dot(d), qc = w.Dot(w) - r * r;
        var disc = qb * qb - qa * qc;
        if (disc <= 0) return null;
        var root = Math.Sqrt(disc);
        return ((-qb - root) / qa, (-qb + root) / qa);
    }

    /// <summary>The t where a + t·d lies over the edge p–q (its projection on the edge inside it) within r of the edge's line, unclamped.</summary>
    static (double, double)? Band(Point2 a, Point2 d, Point2 p, Point2 q, double r)
    {
        var e = q - p;
        var len = e.Length;
        if (len < 1e-12) return null;
        var u = e * (1 / len);
        var n = new Point2(u.Y, -u.X);
        double t0 = double.NegativeInfinity, t1 = double.PositiveInfinity;
        // Along the edge: 0 ≤ (x − p)·u ≤ len; across it: −r ≤ (x − p)·n ≤ r.
        return Between(ref t0, ref t1, (a - p).Dot(u), d.Dot(u), 0, len) && Between(ref t0, ref t1, (a - p).Dot(n), d.Dot(n), -r, r) && t0 < t1
            ? (t0, t1) : null;
    }

    /// <summary>The t where a + t·d lies inside the polygon core (every edge's signed distance negative), unclamped.</summary>
    static (double, double)? Slab(Point2 a, Point2 d, Point2[] core)
    {
        double t0 = double.NegativeInfinity, t1 = double.PositiveInfinity;
        for (int i = 0, j = core.Length - 1; i < core.Length; j = i++)
        {
            var e = core[i] - core[j];
            // signed(x) = e.Cross(x − core[j]) / −|e| < 0, i.e. e.Cross(x − core[j]) > 0.
            var num = e.Cross(a - core[j]);
            var den = e.Cross(d);
            if (Math.Abs(den) < 1e-15) { if (num <= 0) return null; continue; }
            var t = -num / den;
            if (den > 0) t0 = Math.Max(t0, t); else t1 = Math.Min(t1, t);
            if (t0 >= t1) return null;
        }
        return (t0, t1);
    }

    /// <summary>Narrows [t0, t1] to where lo ≤ x0 + t·dx ≤ hi; false when that leaves nothing.</summary>
    static bool Between(ref double t0, ref double t1, double x0, double dx, double lo, double hi)
    {
        if (Math.Abs(dx) < 1e-15) return x0 >= lo && x0 <= hi;
        double ta = (lo - x0) / dx, tb = (hi - x0) / dx;
        if (ta > tb) (ta, tb) = (tb, ta);
        t0 = Math.Max(t0, ta); t1 = Math.Min(t1, tb);
        return t0 < t1;
    }

    /// <summary>
    /// Interval(a, b) is not null, known before it is computed: the segment comes inside by clearly more than its rounding. False only says
    /// "not known".
    /// </summary>
    public bool SurelyEnters(Point2 a, Point2 b)
    {
        if (Radius <= Tolerance.Distance) return Enters(a, b);
        return SegmentWithin(a, b, Radius - Tolerance.Distance - 1e-6 - 1e-8 * (b - a).Length);
    }

    /// <summary>Unsigned distance from the segment to a core: 0 when they meet.</summary>
    static double SegmentDistance(Edged polygon, Point2 a, Point2 b)
    {
        var core = polygon.Points;
        if (core.Length == 1) return Outline.ClosestOnSegment(a, b, core[0]).DistanceTo(core[0]);
        var (fromA, fromB) = (CoreSigned(polygon, a), CoreSigned(polygon, b));
        if (fromA <= 0 || fromB <= 0) return 0;
        for (int i = 0, j = core.Length - 1; i < core.Length; j = i++)
            if (Outline.SegmentsIntersect(a, b, core[j], core[i])) return 0;
        var best = Math.Min(fromA, fromB);
        var d = b - a;
        var length = d.Length;
        foreach (var v in core)
        {
            // The line's distance bounds the segment's from below: a vertex beyond the least so far is not measured.
            if (length > 1e-9 && Math.Abs(d.Cross(v - a)) / length > best + Slack) continue;
            best = Math.Min(best, Outline.ClosestOnSegment(a, b, v).DistanceTo(v));
        }
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
