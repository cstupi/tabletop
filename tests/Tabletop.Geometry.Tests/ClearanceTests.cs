namespace Tabletop.Geometry.Tests;

public class ClearanceTests
{
    static double Area(IReadOnlyList<(Point2 A, Point2 B)> segments) => segments.Sum(s => s.A.X * s.B.Y - s.B.X * s.A.Y) / 2;

    [Fact]
    public void Without_lifted_models_the_outline_is_the_rings_union()
    {
        var a = Fx.Model(10, 10, diameter: 1); var b = Fx.Model(30, 10, diameter: 1);
        Assert.Equal(RangeOutline.Of([a, b], [9.0, 12.0]), Clearance.Outline([a, b], [9.0, 12.0], [], 0));
    }

    [Fact]
    public void No_model_gives_no_outline() => Assert.Empty(Clearance.Outline([], [], [], 0));

    [Fact]
    public void A_base_within_the_distance_breaks_it_and_one_over_it_does_not()
    {
        var enemy = Fx.Model(10, 10, diameter: 1);
        Assert.Equal(0, Clearance.Breaks(Fx.Model(19, 10, diameter: 1), [enemy], [9.0], [], 0));     // 8" apart edge to edge
        Assert.Equal(-1, Clearance.Breaks(Fx.Model(20.1, 10, diameter: 1), [enemy], [9.0], [], 0));  // 9.1"
    }

    [Fact]
    public void Each_model_is_kept_at_its_own_distance()
    {
        var near = Fx.Model(10, 10, diameter: 1); var repel = Fx.Model(40, 10, diameter: 1);
        Assert.Equal(1, Clearance.Breaks(Fx.Model(29, 10, diameter: 1), [near, repel], [9.0, 12.0], [], 0));   // 10" from the second, kept 12"
    }

    [Fact]
    public void A_base_within_reach_of_a_lifted_model_ignores_the_distance()
    {
        var enemy = Fx.Model(10, 10, diameter: 1); var beacon = Fx.Model(24, 10, diameter: 1);
        var placed = Fx.Model(18, 10, diameter: 1);                                                  // 7" from the enemy, 5" from the beacon
        Assert.Equal(-1, Clearance.Breaks(placed, [enemy], [9.0], [beacon], 6));
        Assert.Equal(0, Clearance.Breaks(placed, [enemy], [9.0], [beacon], 4));
    }

    [Fact]
    public void A_lifted_area_cuts_the_rings_and_the_loop_closes_round_what_is_left()
    {
        var enemy = Fx.Model(10, 10, diameter: 1); var beacon = Fx.Model(24, 10, diameter: 1);
        var full = Clearance.Outline([enemy], [9.0], [], 0);
        var cut = Clearance.Outline([enemy], [9.0], [beacon], 6);
        Assert.InRange(Area(cut), 0.5, Area(full) - 1);                                              // smaller, still counter-clockwise
        foreach (var (p, q) in cut)
            foreach (var x in new[] { p, q })
                Assert.True(Distance.EdgeToEdge(enemy, x) <= 9 + 1e-3 && Distance.EdgeToEdge(beacon, x) >= 6 - 1e-3);
        // every end meets another segment's start: the loop is closed
        foreach (var (_, q) in cut) Assert.Contains(cut, s => s.A.DistanceTo(q) < 1e-3);
    }

    [Fact]
    public void A_lifted_area_holding_the_whole_ring_leaves_nothing()
    {
        var enemy = Fx.Model(10, 10, diameter: 1); var beacon = Fx.Model(10, 10, diameter: 1);
        Assert.Empty(Clearance.Outline([enemy], [3.0], [beacon], 6));
    }
}
