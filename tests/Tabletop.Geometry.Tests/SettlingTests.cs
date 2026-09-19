namespace Tabletop.Geometry.Tests;

public class SettlingTests
{
    [Fact] public void Open_table_is_zero() => Assert.Equal(0, Settling.SurfaceHeightAt(new Point2(3, 3), []));
    [Fact] public void Lands_on_a_solid_top()
    {
        var hill = Fx.Box(12, 12, 6, 6, 0, 2);
        Assert.Equal(2, Settling.SurfaceHeightAt(new Point2(12, 12), [hill]), 6);
        Assert.Equal(0, Settling.SurfaceHeightAt(new Point2(20, 20), [hill]));
    }
    [Fact] public void Area_volumes_are_not_stood_on() => Assert.Equal(0, Settling.SurfaceHeightAt(new Point2(0, 0), [Fx.Cylinder(0, 0, 4, 0, 8, VolumeKind.Area)]));
    [Fact] public void Highest_solid_wins_from_above()
    {
        WorldVolume[] ruin = [Fx.Box(0, 0, 6, 6, 0, 2), Fx.Box(0, 0, 6, 6, 4, 8)];
        Assert.Equal(8, Settling.SurfaceHeightAt(new Point2(0, 0), ruin), 6);
    }
    [Fact] public void Dropping_from_below_a_slab_lands_beneath_it()
    {
        WorldVolume[] ruin = [Fx.Box(0, 0, 6, 6, 0, 2), Fx.Box(0, 0, 6, 6, 4, 8)];
        Assert.Equal(2, Settling.SurfaceHeightAt(new Point3(0, 0, 3), ruin), 6);
        Assert.Equal(0, Settling.SurfaceHeightAt(new Point3(0, 0, 1), [Fx.Box(0, 0, 6, 6, 4, 8)]));
    }
    [Fact] public void Scale_and_z_raise_the_top()
    {
        Assert.Equal(4, Settling.SurfaceHeightAt(new Point2(0, 0), [Fx.Box(0, 0, 6, 6, 0, 2, scale: 2)]), 6);
        Assert.Equal(5, Settling.SurfaceHeightAt(new Point2(0, 0), [Fx.Box(0, 0, 6, 6, 0, 2, scale: 2, z: 1)]), 6);
    }
    [Fact] public void Rotated_footprint_is_respected()
    {
        var beam = Fx.Box(0, 0, 6, 2, 0, 1, yaw: 90);
        Assert.Equal(1, Settling.SurfaceHeightAt(new Point2(0, 2.5), [beam]), 6);
        Assert.Equal(0, Settling.SurfaceHeightAt(new Point2(2.5, 0), [beam]));
    }
}
