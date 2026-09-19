namespace Tabletop.Geometry;

/// <summary>A closed convex outline on the table plane in world coordinates. Every base and every footprint becomes one of these.</summary>
internal abstract record Outline
{
    /// <summary>Boundary inclusive.</summary>
    public abstract bool Contains(Point2 p);
    /// <summary>Distance from p to the boundary, whether p is inside or outside.</summary>
    public abstract double BoundaryDistance(Point2 p);
    /// <summary>Nearest point of the outline to p; p itself when inside.</summary>
    public abstract Point2 ClosestPointTo(Point2 p);
    public double DistanceTo(Point2 p) => Contains(p) ? 0 : ClosestPointTo(p).DistanceTo(p);
    /// <summary>Does the segment a–b touch the outline anywhere.</summary>
    public abstract bool IntersectsSegment(Point2 a, Point2 b);
    /// <summary>Points on the boundary, for sampling sight lines.</summary>
    public abstract IReadOnlyList<Point2> BoundaryPoints(int count);

    /// <summary>The whole of inner lies inside this outline. Both convex.</summary>
    public bool ContainsOutline(Outline inner) => inner switch
    {
        CircleOutline c => Contains(c.Centre) && BoundaryDistance(c.Centre) >= c.Radius - Tolerance.Distance,
        PolygonOutline p => p.Vertices.All(Contains),
        _ => throw new ArgumentOutOfRangeException(nameof(inner)),
    };

    /// <summary>Shortest gap between two outlines and the points it is measured between. 0 when they touch or overlap.</summary>
    public static (double Distance, Point2 OnA, Point2 OnB) Separation(Outline a, Outline b)
    {
        switch (a, b)
        {
            case (CircleOutline ca, CircleOutline cb):
            {
                var d = cb.Centre - ca.Centre;
                var len = d.Length;
                var u = len < Tolerance.Distance ? new Point2(1, 0) : d * (1 / len);
                return (Math.Max(0, len - ca.Radius - cb.Radius), ca.Centre + u * ca.Radius, cb.Centre - u * cb.Radius);
            }
            case (CircleOutline c, PolygonOutline p):
            {
                var (dist, onP, onC) = CircleToPolygon(c, p);
                return (dist, onC, onP);
            }
            case (PolygonOutline p, CircleOutline c):
            {
                var (dist, onP, onC) = CircleToPolygon(c, p);
                return (dist, onP, onC);
            }
            case (PolygonOutline pa, PolygonOutline pb):
                return PolygonToPolygon(pa, pb);
            default:
                throw new ArgumentOutOfRangeException(nameof(a));
        }
    }

    static (double, Point2 OnPolygon, Point2 OnCircle) CircleToPolygon(CircleOutline c, PolygonOutline p)
    {
        if (p.Contains(c.Centre)) return (0, c.Centre, c.Centre);
        var q = p.ClosestBoundaryPoint(c.Centre);
        var d = q - c.Centre;
        var len = d.Length;
        return (Math.Max(0, len - c.Radius), q, c.Centre + d * (c.Radius / len));
    }

    static (double, Point2, Point2) PolygonToPolygon(PolygonOutline a, PolygonOutline b)
    {
        var best = (Distance: double.PositiveInfinity, OnA: a.Vertices[0], OnB: b.Vertices[0]);
        foreach (var v in a.Vertices)
        {
            var q = b.ClosestBoundaryPoint(v);
            var d = v.DistanceTo(q);
            if (d < best.Distance) best = (d, v, q);
        }
        foreach (var v in b.Vertices)
        {
            var q = a.ClosestBoundaryPoint(v);
            var d = v.DistanceTo(q);
            if (d < best.Distance) best = (d, q, v);
        }
        return InteriorsIntersect(a, b) ? (0, best.OnA, best.OnB) : best;
    }

    /// <summary>The two overlap with positive area. Touching edges do not count.</summary>
    public static bool InteriorsIntersect(Outline a, Outline b) => (a, b) switch
    {
        (CircleOutline ca, CircleOutline cb) => ca.Centre.DistanceTo(cb.Centre) < ca.Radius + cb.Radius - Tolerance.Distance,
        (CircleOutline c, PolygonOutline p) => p.DistanceTo(c.Centre) < c.Radius - Tolerance.Distance,
        (PolygonOutline p, CircleOutline c) => p.DistanceTo(c.Centre) < c.Radius - Tolerance.Distance,
        (PolygonOutline pa, PolygonOutline pb) => PolygonOutline.SeparatingAxisTest(pa, pb),
        _ => throw new ArgumentOutOfRangeException(nameof(a)),
    };

    internal static Point2 ClosestOnSegment(Point2 a, Point2 b, Point2 p)
    {
        var ab = b - a;
        var l2 = ab.Dot(ab);
        if (l2 < Tolerance.Distance * Tolerance.Distance) return a;
        return a + ab * Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
    }

    internal static bool SegmentsIntersect(Point2 p1, Point2 p2, Point2 q1, Point2 q2)
    {
        var d1 = (q2 - q1).Cross(p1 - q1);
        var d2 = (q2 - q1).Cross(p2 - q1);
        var d3 = (p2 - p1).Cross(q1 - p1);
        var d4 = (p2 - p1).Cross(q2 - p1);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
        return Polygon.OnSegment(q1, q2, p1) || Polygon.OnSegment(q1, q2, p2) || Polygon.OnSegment(p1, p2, q1) || Polygon.OnSegment(p1, p2, q2);
    }
}

internal sealed record CircleOutline(Point2 Centre, double Radius) : Outline
{
    public override bool Contains(Point2 p) => Centre.DistanceTo(p) <= Radius + Tolerance.Distance;
    public override double BoundaryDistance(Point2 p) => Math.Abs(Centre.DistanceTo(p) - Radius);
    public override Point2 ClosestPointTo(Point2 p)
    {
        var d = p - Centre;
        var len = d.Length;
        return len <= Radius + Tolerance.Distance ? p : Centre + d * (Radius / len);
    }
    public override bool IntersectsSegment(Point2 a, Point2 b) => Centre.DistanceTo(ClosestOnSegment(a, b, Centre)) <= Radius + Tolerance.Distance;
    public override IReadOnlyList<Point2> BoundaryPoints(int count) => Enumerable.Range(0, count)
        .Select(i => { var t = 2 * Math.PI * i / count; return Centre + new Point2(Math.Cos(t), Math.Sin(t)) * Radius; })
        .ToList();
}

/// <summary>A convex polygon. The constructor orders the vertices counter-clockwise.</summary>
internal sealed record PolygonOutline : Outline
{
    public IReadOnlyList<Point2> Vertices { get; }

    public PolygonOutline(IReadOnlyList<Point2> vertices)
    {
        if (vertices.Count < 3) throw new ArgumentException("A polygon outline needs at least three vertices.", nameof(vertices));
        double twiceArea = 0;
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++) twiceArea += vertices[j].Cross(vertices[i]);
        Vertices = twiceArea < 0 ? vertices.Reverse().ToList() : vertices.ToList();
    }

    public override bool Contains(Point2 p)
    {
        for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
            if ((Vertices[i] - Vertices[j]).Cross(p - Vertices[j]) < -Tolerance.Distance) return false;
        return true;
    }

    public Point2 ClosestBoundaryPoint(Point2 p)
    {
        var best = Vertices[0];
        var bestD = double.PositiveInfinity;
        for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
        {
            var q = ClosestOnSegment(Vertices[j], Vertices[i], p);
            var d = q.DistanceTo(p);
            if (d < bestD) { bestD = d; best = q; }
        }
        return best;
    }

    public override double BoundaryDistance(Point2 p) => ClosestBoundaryPoint(p).DistanceTo(p);
    public override Point2 ClosestPointTo(Point2 p) => Contains(p) ? p : ClosestBoundaryPoint(p);

    public override bool IntersectsSegment(Point2 a, Point2 b)
    {
        if (Contains(a) || Contains(b)) return true;
        for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
            if (SegmentsIntersect(a, b, Vertices[j], Vertices[i])) return true;
        return false;
    }

    /// <summary>Every vertex and every edge midpoint; count is ignored (the polygon decides).</summary>
    public override IReadOnlyList<Point2> BoundaryPoints(int count)
    {
        var pts = new List<Point2>(Vertices.Count * 2);
        for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
        {
            pts.Add(Vertices[i]);
            pts.Add((Vertices[i] + Vertices[j]) * 0.5);
        }
        return pts;
    }

    internal static bool SeparatingAxisTest(PolygonOutline a, PolygonOutline b)
    {
        foreach (var axis in Axes(a).Concat(Axes(b)))
        {
            var (minA, maxA) = Project(a, axis);
            var (minB, maxB) = Project(b, axis);
            if (maxA <= minB + Tolerance.Distance || maxB <= minA + Tolerance.Distance) return false;
        }
        return true;
    }

    static IEnumerable<Point2> Axes(PolygonOutline p)
    {
        for (int i = 0, j = p.Vertices.Count - 1; i < p.Vertices.Count; j = i++)
        {
            var e = p.Vertices[i] - p.Vertices[j];
            yield return new Point2(-e.Y, e.X) * (1 / e.Length);
        }
    }

    static (double Min, double Max) Project(PolygonOutline p, Point2 axis)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var v in p.Vertices) { var d = v.Dot(axis); min = Math.Min(min, d); max = Math.Max(max, d); }
        return (min, max);
    }
}
