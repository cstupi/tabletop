namespace Tabletop.Geometry.Tests;

/// <summary>
/// MoveShape against the plain computations it replaced, over many segments and points about many-sided shapes: CoreSigned and Blocks equal them
/// bit for bit (the coarse bounds and pruning only skip work), and Interval in closed form spans what the golden-section and bisection search found.
/// </summary>
public class MoveShapeReferenceTests
{
    /// <summary>The signed distance to the core, every edge measured.</summary>
    static double PlainSigned(Point2[] core, Point2 p)
    {
        if (core.Length == 1) return p.DistanceTo(core[0]);
        var inside = true; var deepest = double.NegativeInfinity;
        for (int i = 0, j = core.Length - 1; i < core.Length; j = i++)
        {
            var e = core[i] - core[j];
            var signed = e.Cross(p - core[j]) / -e.Length;
            if (signed > 0) inside = false;
            deepest = Math.Max(deepest, signed);
        }
        if (inside) return deepest;
        var best = double.PositiveInfinity;
        for (int i = 0, j = core.Length - 1; i < core.Length; j = i++) best = Math.Min(best, Outline.ClosestOnSegment(core[j], core[i], p).DistanceTo(p));
        return best;
    }

    /// <summary>Some point of a–b inside the shape, every edge and vertex measured.</summary>
    static bool PlainBlocks(MoveShape shape, Point2 a, Point2 b)
    {
        var core = shape.Core;
        if (shape.Radius <= Tolerance.Distance) return shape.Core.Length >= 3 && shape.Interval(a, b) is not null;
        double distance;
        if (core.Length == 1) distance = Outline.ClosestOnSegment(a, b, core[0]).DistanceTo(core[0]);
        else if (PlainSigned(core, a) <= 0 || PlainSigned(core, b) <= 0) distance = 0;
        else
        {
            distance = Math.Min(PlainSigned(core, a), PlainSigned(core, b));
            for (int i = 0, j = core.Length - 1; i < core.Length; j = i++)
                if (Outline.SegmentsIntersect(a, b, core[j], core[i])) distance = 0;
            foreach (var v in core) distance = Math.Min(distance, Outline.ClosestOnSegment(a, b, v).DistanceTo(v));
        }
        return distance < shape.Radius - Tolerance.Distance;
    }

    [Fact]
    public void Signed_distances_and_blocking_are_what_every_edge_gives()
    {
        var random = new Random(11);
        var blocked = 0;
        foreach (var shape in Shapes())
        {
            for (var k = 0; k < 4000; k++)
            {
                Point2 Near() => shape.Centre + new Point2(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1) * (shape.Bound * 1.3);
                var a = Near();
                // Every fifth segment grazes the shape: from a point on its boundary, along it.
                var b = k % 5 == 0 ? shape.Nearest(a) + new Point2(random.NextDouble() - 0.5, random.NextDouble() - 0.5) * 0.01 : Near();
                Assert.Equal(PlainSigned(shape.Core, a), shape.CoreSigned(a));
                var plain = PlainBlocks(shape, a, b);
                Assert.Equal(plain, shape.Blocks(a, b));
                if (plain) blocked++;
            }
        }
        Assert.InRange(blocked, 1000, 30000);
    }

    /// <summary>The search: the deepest point by golden section, then each side's crossing bisected.</summary>
    static (double T0, double T1)? Searched(MoveShape shape, Point2 a, Point2 b)
    {
        if (!shape.Blocks(a, b)) return null;
        var d = b - a;
        double F(double t) => shape.Depth(a + d * t) + Tolerance.Distance;
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

    static IEnumerable<MoveShape> Shapes()
    {
        var origin = new Point2(10, 10);
        Outline[] movers =
        [
            new PlacedModel(new ModelBody(Base.Oval(2.3622, 1.378), 4), new Placement(new Point3(10, 10, 0), new Yaw(17))).BaseOutline(),
            new PlacedModel(new ModelBody(Base.Circle(1.26), 2), new Placement(new Point3(10, 10, 0), new Yaw(0))).BaseOutline(),
            new PlacedModel(new ModelBody(Base.Rect(2, 1), 2), new Placement(new Point3(10, 10, 0), new Yaw(30))).BaseOutline(),
        ];
        Outline[] obstacles =
        [
            new PlacedModel(new ModelBody(Base.Circle(1.26), 2), new Placement(new Point3(14, 12, 0), new Yaw(0))).BaseOutline(),
            new PlacedModel(new ModelBody(Base.Oval(2.3622, 1.378), 4), new Placement(new Point3(13, 9, 0), new Yaw(70))).BaseOutline(),
            new PlacedModel(new ModelBody(Base.Rect(3, 3), 2), new Placement(new Point3(12, 14, 0), new Yaw(15))).BaseOutline(),
        ];
        foreach (var m in movers)
            foreach (var o in obstacles)
                yield return MoveShape.Of(o, m, origin);
    }

    [Fact]
    public void The_closed_form_spans_what_the_search_found()
    {
        var random = new Random(7);
        var checkedCount = 0;
        foreach (var shape in Shapes())
        {
            for (var k = 0; k < 2000; k++)
            {
                Point2 Near() => shape.Centre + new Point2(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1) * (shape.Bound * 1.5);
                var a = Near();
                var b = k % 5 == 0 ? a + new Point2(random.NextDouble() - 0.5, random.NextDouble() - 0.5) * 0.05 : Near();
                var expected = Searched(shape, a, b);
                var actual = shape.Interval(a, b);
                if (expected is not { } e)
                {
                    // The search misses only a segment that barely grazes the shape.
                    if (actual is { } miss) Assert.True(miss.T1 - miss.T0 < 1e-6, $"{a} {b}: {miss}");
                    continue;
                }
                Assert.True(actual is not null, $"{a} {b}: expected {e}");
                var (t0, t1) = actual!.Value;
                var tolerance = 1e-8 / Math.Max(1e-9, (b - a).Length) + 1e-9;
                Assert.InRange(t0, e.T0 - tolerance, e.T0 + tolerance);
                Assert.InRange(t1, e.T1 - tolerance, e.T1 + tolerance);
                checkedCount++;
            }
        }
        Assert.True(checkedCount > 2000);
    }
}
