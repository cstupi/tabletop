using System.Runtime.CompilerServices;

namespace Tabletop.Geometry;

/// <summary>
/// Movement.ContactPoints' test, asked some forty times per bearing: whether a polygon base (rect or oval), centred at a point, is under a gap of a
/// polygon target, with the answer Distance.EdgeToEdge gives (Outline.Separation: the least vertex-to-boundary distance either way, 0 when the
/// interiors meet). Each polygon's boundary is held in runs of up to eight edges, each run in a circle; the target's runs, edges and axes are built
/// once. Circles that hold each polygon and lie inside it settle the far and the deep cases; otherwise only a vertex near enough to a run of the
/// other's boundary is measured, against each edge of the run near enough to it, as Separation measures it, the first under the gap answering. When
/// none is, the overlap test (PolygonOutline.SeparatingAxisTest's, with the base's own vertices, whose answer is the same in any axis order) tries the
/// edges facing the other polygon first. Every bound is padded by Slack, so a skipped vertex or edge is one Separation measures at the gap or more.
/// </summary>
internal sealed class ContactProbe
{
    /// <summary>Padding on every bound, far above the rounding in a table's coordinates.</summary>
    const double Slack = 1e-9;

    const int RunLength = 8;

    /// <summary>One polygon's vertices and edges (edge i runs from vertex i-1 to vertex i), its runs of edges and its circles, about some origin.</summary>
    sealed class Shape
    {
        public readonly Point2[] Vertices;
        public readonly Point2[] Mids;
        public readonly double[] Halves;
        public readonly (int Start, int End, Point2 Centre, double Radius)[] Runs;   // edges Start..End-1 and the vertices they join, in a circle
        public readonly Point2 Centre;
        public readonly double Outer, Inner;

        public Shape(Point2[] v)
        {
            var n = v.Length;
            Vertices = v;
            Mids = new Point2[n]; Halves = new double[n];
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Mids[i] = (v[i] + v[j]) * 0.5;
                Halves[i] = v[i].DistanceTo(v[j]) / 2 + Slack;
            }
            Runs = new (int, int, Point2, double)[(n + RunLength - 1) / RunLength];
            for (var r = 0; r < Runs.Length; r++)
            {
                var (start, end) = (r * RunLength, Math.Min(n, (r + 1) * RunLength));
                var joined = Enumerable.Range(start - 1, end - start + 1).Select(i => v[(i + n) % n]).ToList();
                var centre = new Point2(joined.Average(p => p.X), joined.Average(p => p.Y));
                Runs[r] = (start, end, centre, joined.Max(p => p.DistanceTo(centre)) + Slack);
            }
            Centre = new Point2(v.Average(p => p.X), v.Average(p => p.Y));
            double outer = 0, inner = double.PositiveInfinity;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                outer = Math.Max(outer, v[i].DistanceTo(Centre));
                var e = v[i] - v[j];
                if (e.Length > 0) inner = Math.Min(inner, Math.Abs(e.Cross(Centre - v[j])) / e.Length);
            }
            (Outer, Inner) = (outer + Slack, Math.Max(0, inner - Slack));
        }
    }

    readonly Shape _base;                // about the base's centre (Bodies.BaseOffsets), counter-clockwise
    readonly Shape _target;              // as the target's outline holds it
    readonly Point2[] _targetAxes;       // per target edge, SeparatingAxisTest's axis; (0, 0) for an edge it skips
    readonly double[] _targetMin, _targetMax;
    readonly Point2[] _vertices;         // the base's vertices at the asked centre

    ContactProbe(Point2[] offsets, PolygonOutline target)
    {
        _base = new Shape(offsets);
        _target = new Shape([.. target.Vertices]);
        var t = _target.Vertices;
        var n = t.Length;
        _targetAxes = new Point2[n]; _targetMin = new double[n]; _targetMax = new double[n];
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var e = t[i] - t[j];
            if (e.Length < Tolerance.Distance) continue;
            _targetAxes[i] = new Point2(-e.Y, e.X) * (1 / e.Length);
            (_targetMin[i], _targetMax[i]) = Project(t, _targetAxes[i]);
        }
        _vertices = new Point2[offsets.Length];
    }

    /// <summary>A probe for the start's base against the target's, or null when either is a circle (Separation is cheap there) or the base's
    /// vertices would not stand in its outline's order.</summary>
    public static ContactProbe? For(PlacedModel start, PlacedModel target)
    {
        if (start.BaseOffsets() is not { } offsets || target.BaseOutline() is not PolygonOutline other) return null;
        double twiceArea = 0;
        for (int i = 0, j = offsets.Length - 1; i < offsets.Length; j = i++) twiceArea += offsets[j].Cross(offsets[i]);
        return twiceArea > 1e-3 ? new ContactProbe(offsets, other) : null;
    }

    static (double Min, double Max) Project(Point2[] vertices, Point2 axis)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var v in vertices) { var d = v.Dot(axis); min = Math.Min(min, d); max = Math.Max(max, d); }
        return (min, max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Beyond(Point2 a, Point2 b, double reach) { var d = a - b; return d.X * d.X + d.Y * d.Y > reach * reach; }

    /// <summary>Distance.EdgeToEdge(the base centred at `centre`, the target) &lt; gap.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool Under(Point2 centre, double gap)
    {
        if (!(gap > 0)) return false;                                         // a distance is never under zero
        var own = _base.Centre + centre;
        var centres = own.DistanceTo(_target.Centre);
        if (centres - _base.Outer - _target.Outer > gap + Slack) return false;  // apart by more than gap: every vertex too, and no overlap
        if (centres < _base.Inner + _target.Inner - 1e-5) return true;       // the inner circles overlap: so do the interiors
        var v = _vertices;
        for (var i = 0; i < v.Length; i++) v[i] = _base.Vertices[i] + centre; // as Bodies.BaseAt places them
        var reach = gap + Slack;
        // A base vertex under gap of the target's boundary (Separation: b.ClosestBoundaryPoint(v)).
        foreach (var run in _base.Runs)
        {
            var at = run.Centre + centre;
            if (Beyond(at, _target.Centre, run.Radius + _target.Outer + reach)) continue;
            foreach (var edges in _target.Runs)
            {
                if (Beyond(at, edges.Centre, run.Radius + edges.Radius + reach)) continue;
                for (var k = run.Start - 1; k < run.End - 1; k++)            // the run's edges join vertices Start-1..End-1; each vertex once over all runs
                {
                    var p = v[(k + 1) % v.Length];
                    if (Beyond(p, edges.Centre, edges.Radius + reach)) continue;
                    for (var i = edges.Start; i < edges.End; i++)
                    {
                        if (Beyond(p, _target.Mids[i], _target.Halves[i] + reach)) continue;
                        var t = _target.Vertices;
                        if (Outline.ClosestOnSegment(t[(i + t.Length - 1) % t.Length], t[i], p).DistanceTo(p) < gap) return true;
                    }
                }
            }
        }
        // A target vertex under gap of the base's boundary (Separation: a.ClosestBoundaryPoint(w)).
        foreach (var run in _target.Runs)
        {
            if (Beyond(run.Centre, own, run.Radius + _base.Outer + reach)) continue;
            foreach (var edges in _base.Runs)
            {
                var at = edges.Centre + centre;
                if (Beyond(run.Centre, at, run.Radius + edges.Radius + reach)) continue;
                for (var k = run.Start - 1; k < run.End - 1; k++)
                {
                    var p = _target.Vertices[(k + 1) % _target.Vertices.Length];
                    if (Beyond(p, at, edges.Radius + reach)) continue;
                    for (var i = edges.Start; i < edges.End; i++)
                    {
                        if (Beyond(p, _base.Mids[i] + centre, _base.Halves[i] + reach)) continue;
                        if (p.DistanceTo(Outline.ClosestOnSegment(v[(i + v.Length - 1) % v.Length], v[i], p)) < gap) return true;
                    }
                }
            }
        }
        return Overlaps(_target.Centre - own);
    }

    /// <summary>PolygonOutline.SeparatingAxisTest(base, target): no axis of either's edges separates them.</summary>
    bool Overlaps(Point2 toward)
    {
        var v = _vertices;
        foreach (var k in Facing(v, toward))
        {
            var e = v[k] - v[(k + v.Length - 1) % v.Length];
            if (e.Length < Tolerance.Distance) continue;
            var axis = new Point2(-e.Y, e.X) * (1 / e.Length);
            var (minA, maxA) = Project(v, axis);
            var (minB, maxB) = Project(_target.Vertices, axis);
            if (maxA <= minB + Tolerance.Distance || maxB <= minA + Tolerance.Distance) return false;
        }
        foreach (var k in Facing(_target.Vertices, new Point2(-toward.X, -toward.Y)))
        {
            if (_targetAxes[k] is { X: 0, Y: 0 }) continue;
            var (minA, maxA) = Project(v, _targetAxes[k]);
            if (maxA <= _targetMin[k] + Tolerance.Distance || _targetMax[k] <= minA + Tolerance.Distance) return false;
        }
        return true;
    }

    /// <summary>Every edge index once, starting with the edge whose outward normal points most along `toward`, then alternately either side.</summary>
    static IEnumerable<int> Facing(Point2[] v, Point2 toward)
    {
        var n = v.Length;
        var first = 0;
        var most = double.NegativeInfinity;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var e = v[i] - v[j];
            var facing = e.Y * toward.X - e.X * toward.Y;                   // a counter-clockwise edge's outward normal, along toward
            if (facing > most) { most = facing; first = i; }
        }
        for (var k = 0; k < n; k++) yield return (first + (k + 1) / 2 * (k % 2 == 0 ? -1 : 1) + n) % n;
    }
}
