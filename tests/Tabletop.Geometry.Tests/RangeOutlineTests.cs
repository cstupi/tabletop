namespace Tabletop.Geometry.Tests;

public class RangeOutlineTests
{
    [Fact]
    public void One_round_base_is_one_closed_ring_at_range_plus_radius()
    {
        var m = Fx.Model(10, 10, diameter: 1);
        var ring = RangeOutline.Of([m], 6);
        Assert.Equal(RangeOutline.Points, ring.Count);
        foreach (var (a, b) in ring)
        {
            Assert.InRange(a.DistanceTo(new Point2(10, 10)), 6.5 - 1e-9, 6.5 + 1e-9);
            Assert.InRange(b.DistanceTo(new Point2(10, 10)), 6.5 - 1e-9, 6.5 + 1e-9);
        }
        Assert.Equal(ring[0].A, ring[^1].B);
    }

    [Fact]
    public void Two_far_apart_bases_give_two_rings() =>
        Assert.Equal(2 * RangeOutline.Points, RangeOutline.Of([Fx.Model(10, 10, diameter: 1), Fx.Model(40, 10, diameter: 1)], 6).Count);

    [Fact]
    public void A_covered_arc_is_dropped()
    {
        var a = Fx.Model(10, 10, diameter: 1); var b = Fx.Model(12, 10, diameter: 1);
        var ring = RangeOutline.Of([a, b], 6);
        Assert.InRange(ring.Count, 1, 2 * RangeOutline.Points - 1);
        foreach (var (p, q) in ring)
            foreach (var m in new[] { a, b }) { Assert.True(Distance.EdgeToEdge(m, p) >= 6 - 1e-6); Assert.True(Distance.EdgeToEdge(m, q) >= 6 - 1e-6); }
    }

    [Fact]
    public void Each_model_may_carry_its_own_range_and_the_outline_is_the_union()
    {
        var a = Fx.Model(10, 10, diameter: 1); var b = Fx.Model(18, 10, diameter: 1);
        var ring = RangeOutline.Of([a, b], [6.0, 12.0]);
        Assert.NotEmpty(ring);
        foreach (var (p, q) in ring)
            foreach (var x in new[] { p, q })
            {
                var (da, db) = (Distance.EdgeToEdge(a, x), Distance.EdgeToEdge(b, x));
                Assert.True(da >= 6 - 1e-6 && db >= 12 - 1e-6);                                        // outside both
                Assert.True(Math.Abs(da - 6) < 1e-6 || Math.Abs(db - 12) < 1e-6);                      // on one of them
            }
        Assert.Contains(ring, s => Math.Abs(Distance.EdgeToEdge(a, s.A) - 6) < 1e-6);               // a's ring pokes out to the left
        Assert.Equal(RangeOutline.Of([a, b], 6), RangeOutline.Of([a, b], [6.0, 6.0]));
    }
}
