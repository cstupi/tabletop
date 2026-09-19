namespace Tabletop.Geometry.Tests;

public class DistanceTests
{
    [Fact] public void Circles_measure_edge_to_edge() => Assert.Equal(4.74, Distance.EdgeToEdge(Fx.Model(0, 0), Fx.Model(6, 0)), 6);
    [Fact] public void Touching_or_overlapping_bases_are_at_zero()
    {
        Assert.Equal(0, Distance.EdgeToEdge(Fx.Model(0, 0), Fx.Model(1.26, 0)), 6);
        Assert.Equal(0, Distance.EdgeToEdge(Fx.Model(0, 0), Fx.Model(1, 0)));
    }
    [Fact] public void Height_does_not_enter_horizontal_distance() => Assert.Equal(4.74, Distance.EdgeToEdge(Fx.Model(0, 0, 0), Fx.Model(6, 0, 4)), 6);
    [Fact] public void Base_to_point()
    {
        Assert.Equal(2.37, Distance.EdgeToEdge(Fx.Model(0, 0), new Point2(3, 0)), 6);
        Assert.Equal(0, Distance.EdgeToEdge(Fx.Model(0, 0), new Point2(0.5, 0)));
    }
    [Fact] public void Rect_and_rotated_rect()
    {
        Assert.Equal(2, Distance.EdgeToEdge(Fx.Model(Base.Rect(1, 1), 0, 0), Fx.Model(3, 0, diameter: 1)), 6);
        Assert.Equal(4 - Math.Sqrt(2) - 0.5, Distance.EdgeToEdge(Fx.Model(Base.Rect(2, 2), 0, 0, yaw: 45), Fx.Model(4, 0, diameter: 1)), 6);
    }
    [Fact] public void Oval_measures_along_its_length_and_width()
    {
        var oval = Fx.Model(Base.Oval(2.36, 1.38), 0, 0);
        Assert.Equal(3.32, Distance.EdgeToEdge(oval, Fx.Model(0, 5, diameter: 1)), 3);
        Assert.Equal(3.81, Distance.EdgeToEdge(oval, Fx.Model(5, 0, diameter: 1)), 3);
    }
    [Fact] public void Base_to_terrain_footprint() => Assert.Equal(2.5, Distance.EdgeToEdge(Fx.Model(0, 0, diameter: 1), Fx.Box(5, 0, 4, 4, 0, 2)), 6);
    [Fact] public void Base_to_marker_edge() => Assert.Equal(2.87, Distance.EdgeToMarker(Fx.Model(0, 0), new Point2(5, 0), 3), 6);
    [Fact] public void Closest_points_carry_each_base_height()
    {
        var (a, b) = Distance.ClosestPoints(Fx.Model(0, 0, 0), Fx.Model(6, 0, 2));
        Assert.Equal(0.63, a.X, 6); Assert.Equal(0, a.Z);
        Assert.Equal(5.37, b.X, 6); Assert.Equal(2, b.Z);
    }
    [Fact] public void In_range_is_inclusive_with_tolerance()
    {
        Assert.True(Distance.InRange(Fx.Model(0, 0), Fx.Model(6, 0), 4.74));
        Assert.False(Distance.InRange(Fx.Model(0, 0), Fx.Model(6, 0), 4.7));
    }
    [Fact] public void Vertical_is_the_difference_of_base_heights() => Assert.Equal(3, Distance.Vertical(Fx.Model(0, 0, 0), Fx.Model(1, 0, 3)));
}
