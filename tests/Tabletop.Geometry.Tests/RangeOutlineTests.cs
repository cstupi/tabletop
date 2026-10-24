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
}
