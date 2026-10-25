namespace Tabletop.Geometry.Tests;

public class ContainmentTests
{
    static readonly WorldVolume Forest = Fx.Cylinder(0, 0, 4, 0, 8, VolumeKind.Area, TerrainDeclarations.Cover | TerrainDeclarations.Difficult);

    [Fact] public void Centre_inside_an_area_volume()
    {
        Assert.Equal(new[] { Forest.Id }, Containment.VolumesContaining(Fx.Model(1, 1), [Forest]));
        Assert.Empty(Containment.VolumesContaining(Fx.Model(5, 0), [Forest]));
    }
    [Fact] public void A_model_on_a_hill_under_a_forest_is_in_both()
    {
        var hill = Fx.Box(0, 0, 8, 8, 0, 2);
        var canopy = Fx.Cylinder(0, 0, 4, 2, 8, VolumeKind.Area, TerrainDeclarations.Cover);
        Assert.Equal(new[] { hill.Id, canopy.Id }, Containment.VolumesContaining(Fx.Model(0, 0, 2), [hill, canopy]));
    }
    [Fact] public void Ground_beneath_a_second_storey_is_not_inside_it()
    {
        var slab = Fx.Box(0, 0, 6, 6, 4, 8);
        Assert.Empty(Containment.VolumesContaining(Fx.Model(0, 0, 0), [slab]));
        Assert.Equal(new[] { slab.Id }, Containment.VolumesContaining(Fx.Model(0, 0, 8), [slab]));
    }
    [Fact] public void Fully_inside_needs_the_whole_base()
    {
        Assert.Equal(new[] { Forest.Id }, Containment.VolumesFullyContaining(Fx.Model(3, 0), [Forest]));
        Assert.Empty(Containment.VolumesFullyContaining(Fx.Model(3.5, 0), [Forest]));
        Assert.Equal(new[] { Forest.Id }, Containment.VolumesContaining(Fx.Model(3.5, 0), [Forest]));
    }
    [Fact] public void Fully_inside_a_rotated_rectangle()
    {
        var field = Fx.Box(0, 0, 10, 4, 0, 1, VolumeKind.Area, TerrainDeclarations.Difficult, yaw: 90);
        Assert.Equal(new[] { field.Id }, Containment.VolumesFullyContaining(Fx.Model(0, 4), [field]));
        Assert.Empty(Containment.VolumesFullyContaining(Fx.Model(4, 0), [field]));
    }
    [Fact] public void Fully_within_a_distance_of_a_container_measures_from_its_base_edge()
    {
        // A 2" rect container at the origin: its edge is at x = 1, so the far rim of a 1"-diameter round base
        // sits 6" out when the base centre is at x = 6.5 (1 + 6 − 0.5).
        var bus = Fx.Model(Base.Rect(2, 2), 0, 0);
        Assert.True(Containment.BaseFullyWithin(Fx.Model(6, 0, diameter: 1), bus, 6));
        Assert.True(Containment.BaseFullyWithin(Fx.Model(6.5, 0, diameter: 1), bus, 6));
        Assert.False(Containment.BaseFullyWithin(Fx.Model(6.6, 0, diameter: 1), bus, 6));
        Assert.True(Containment.BaseFullyWithin(Fx.Model(0, 0, diameter: 1), bus, 6));                       // still on top of it
        // A polygon base's farthest point is a vertex: a 2" rect base's far corner is 6" out at centre x = 6.
        Assert.True(Containment.BaseFullyWithin(Fx.Model(Base.Rect(2, 2), 5.9, 0), bus, 6));
        Assert.False(Containment.BaseFullyWithin(Fx.Model(Base.Rect(2, 2), 6.1, 0), bus, 6));
    }

    static readonly TableSize Slab = new(72, 48);
    static PlacedModel Truck(double x, double y) => new(new ModelBody(Base.Circle(4), 2), new Placement(new Point3(x, y, 0), new Yaw(0)));

    [Fact] public void Exit_yaw_of_a_round_base_within_reach_is_zero() =>
        Assert.Equal(new Yaw(0), Containment.ExitYaw(new ModelBody(Base.Circle(1), 1), Truck(10, 10), 6, new Point2(14, 10), [], Slab));

    [Fact] public void Exit_yaw_beyond_reach_is_null() =>
        Assert.Null(Containment.ExitYaw(new ModelBody(Base.Circle(1), 1), Truck(10, 10), 6, new Point2(30, 10), [], Slab));

    [Fact] public void Exit_yaw_of_an_oval_is_the_first_of_eight_facings_that_fits()
    {
        Assert.Equal(8, Containment.ExitYaws);
        var oval = new ModelBody(Base.Oval(3, 1), 1);
        Assert.Equal(new Yaw(0), Containment.ExitYaw(oval, Truck(10, 10), 6, new Point2(14, 10), [], Slab));
        Assert.Null(Containment.ExitYaw(oval, Truck(10, 10), 6, new Point2(30, 10), [], Slab));
    }

    [Fact] public void Exit_yaw_refuses_a_position_in_an_impassable_volume()
    {
        var wall = Fx.Box(14, 10, 2, 2, 0, 3);
        Assert.Null(Containment.ExitYaw(new ModelBody(Base.Circle(1), 1), Truck(10, 10), 6, new Point2(14, 10), [wall], Slab));
    }
}
