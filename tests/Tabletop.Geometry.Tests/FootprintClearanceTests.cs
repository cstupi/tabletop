namespace Tabletop.Geometry.Tests;

public class FootprintClearanceTests
{
    static readonly TableSize Opr = new(72, 48);

    [Fact] public void Rects_six_inches_apart_are_six_apart() =>
        Assert.Equal(6, Distance.Between(Fx.Box(10, 10, 6, 6, 0, 1), Fx.Box(22, 10, 6, 6, 0, 1)), 1e-6);

    [Fact] public void A_turned_rect_measures_its_corner() =>
        Assert.Equal(6 - (3 * Math.Sqrt(2) - 3), Distance.Between(Fx.Box(10, 10, 6, 6, 0, 1), Fx.Box(22, 10, 6, 6, 0, 1, yaw: 45)), 1e-6);

    [Fact] public void Overlapping_and_touching_are_zero()
    {
        Assert.Equal(0, Distance.Between(Fx.Box(10, 10, 6, 6, 0, 1), Fx.Box(14, 10, 6, 6, 0, 1)), 1e-6);
        Assert.Equal(0, Distance.Between(Fx.Box(10, 10, 6, 6, 0, 1), Fx.Box(16, 10, 6, 6, 0, 1)), 1e-6);
    }

    [Fact] public void Circle_to_rect_is_edge_to_edge() =>
        Assert.Equal(4, Distance.Between(Fx.Cylinder(20, 10, 3, 0, 1), Fx.Box(10, 10, 6, 6, 0, 1)), 1e-6);

    [Fact] public void A_point_inside_is_zero_and_outside_is_the_gap()
    {
        var box = Fx.Box(10, 10, 6, 6, 0, 1);
        Assert.Equal(0, Distance.Between(box, new Point2(10, 10)), 1e-6);
        Assert.Equal(3, Distance.Between(box, new Point2(16, 10)), 1e-6);
    }

    [Fact] public void Scale_grows_the_footprint() =>
        Assert.Equal(3, Distance.Between(Fx.Box(10, 10, 6, 6, 0, 1), Fx.Box(22, 10, 6, 6, 0, 1, scale: 2)), 1e-6);

    [Fact] public void A_footprint_hanging_off_the_edge_is_not_contained()
    {
        Assert.False(Table.ContainsVolume(Opr, Fx.Box(2, 24, 6, 6, 0, 1)));
        Assert.True(Table.ContainsVolume(Opr, Fx.Box(3, 24, 6, 6, 0, 1)));
        Assert.True(Table.ContainsVolume(Opr, Fx.Cylinder(69, 45, 3, 0, 1)));
        Assert.False(Table.ContainsVolume(Opr, Fx.Cylinder(70, 45, 3, 0, 1)));
    }
}
