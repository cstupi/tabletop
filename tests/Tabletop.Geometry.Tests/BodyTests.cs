namespace Tabletop.Geometry.Tests;

public class BodyTests
{
    [Fact] public void Circle_base_is_an_exact_circle()
    {
        var o = Assert.IsType<CircleOutline>(Fx.Model(1, 2).BaseOutline());
        Assert.Equal(new Point2(1, 2), o.Centre);
        Assert.Equal(0.63, o.Radius, 6);
    }
    [Fact] public void Rect_base_rotates_with_yaw()
    {
        var o = Assert.IsType<PolygonOutline>(Fx.Model(Base.Rect(2, 2), 0, 0, yaw: 45).BaseOutline());
        Assert.Contains(o.Vertices, v => Math.Abs(v.X - Math.Sqrt(2)) < 1e-6 && Math.Abs(v.Y) < 1e-6);
    }
    [Fact] public void Oval_length_lies_along_the_facing_axis()
    {
        var o = Assert.IsType<PolygonOutline>(Fx.Model(Base.Oval(2.36, 1.38), 0, 0).BaseOutline());
        Assert.Equal(Bodies.OvalVertices, o.Vertices.Count);
        Assert.Equal(1.18, o.Vertices.Max(v => v.Y), 6);
        Assert.Equal(0.69, o.Vertices.Max(v => v.X), 6);
        var turned = Assert.IsType<PolygonOutline>(Fx.Model(Base.Oval(2.36, 1.38), 0, 0, yaw: 90).BaseOutline());
        Assert.Equal(1.18, turned.Vertices.Max(v => v.X), 6);
    }
    [Fact] public void Eye_is_the_body_centre() => Assert.Equal(new Point3(3, 4, 2.75), Fx.Model(3, 4, 2).Eye());   // z 2, height 1.5, so half height is 2.75
    [Fact] public void A_model_as_a_sight_blocker_keeps_its_footprint_and_height()
    {
        var v = Bodies.AsBlockingVolume(Guid.CreateVersion7(), Fx.Model(Base.Oval(2.36, 1.38), 5, 5, height: 2));
        Assert.Equal(VolumeKind.Solid, v.Volume.Kind);
        Assert.Equal(TerrainDeclarations.BlocksLineOfSight, v.Volume.Declarations);
        Assert.Equal(Footprint.Rect(1.38, 2.36), v.Volume.Footprint);
        Assert.Equal(2, v.WorldTop(), 6);
        Assert.Equal(Footprint.Circle(0.63), Bodies.AsBlockingVolume(Guid.CreateVersion7(), Fx.Model(0, 0)).Volume.Footprint);
    }
    [Fact] public void World_volume_applies_scale_yaw_offset_and_z()
    {
        var piece = new WorldVolume(Guid.CreateVersion7(),
            new Volume(VolumeKind.Solid, Footprint.Rect(6, 2), new Point2(1, 0), new Yaw(0), 0, 2, TerrainDeclarations.None, null),
            new TerrainTransform(new Point3(10, 10, 1), new Yaw(90), 2));
        Assert.Equal(1, piece.WorldBottom(), 6);
        Assert.Equal(5, piece.WorldTop(), 6);
        var c = piece.WorldCentre();
        Assert.Equal(10, c.X, 6);
        Assert.Equal(12, c.Y, 6);
        var o = piece.WorldOutline();
        Assert.True(o.Contains(new(10, 17.5)));
        Assert.False(o.Contains(new(12.5, 12)));
        Assert.True(o.Contains(new(11.5, 12)));
    }
    [Fact] public void World_yaw_composes_but_world_centre_turns_by_the_transform_yaw_only()
    {
        var piece = new WorldVolume(Guid.CreateVersion7(),
            new Volume(VolumeKind.Solid, Footprint.Rect(2, 2), new Point2(1, 0), new Yaw(30), 0, 1, TerrainDeclarations.None, null),
            new TerrainTransform(new Point3(5, 5, 0), new Yaw(60), 1));
        Assert.Equal(90, piece.WorldYaw().Degrees, 6);
        var expected = new Point3(5, 5, 0).Horizontal + new Point2(1, 0).Rotate(new Yaw(60));   // the transform's yaw (60), not the composed 90
        var c = piece.WorldCentre();
        Assert.Equal(expected.X, c.X, 6);
        Assert.Equal(expected.Y, c.Y, 6);
    }
    [Fact] public void Circle_footprint_scales_its_radius() =>
        Assert.True(Fx.Cylinder(0, 0, 1, 0, 2, scale: 3).WorldOutline().Contains(new(2.9, 0)));
    [Fact] public void Segment_through_a_prism()
    {
        var wall = Fx.Box(5, 0, 1, 6, 0, 4);
        Assert.True(wall.IntersectsSegment(new(0, 0, 1.5), new(10, 0, 1.5)));
        Assert.False(wall.IntersectsSegment(new(0, 0, 5), new(10, 0, 5)));
        Assert.True(wall.IntersectsSegment(new(0, 0, 5), new(10, 0, 3)));
        Assert.False(Fx.Box(5, 0, 1, 6, 0, 1).IntersectsSegment(new(0, 0, 1.5), new(10, 0, 1.5)));
        Assert.False(wall.IntersectsSegment(new(0, 4, 1.5), new(10, 4, 1.5)));
    }
    [Fact] public void Point_in_prism_is_footprint_plus_vertical_range()
    {
        var slab = Fx.Box(0, 0, 6, 6, 4, 8);
        Assert.True(slab.ContainsPoint(new(1, 1, 6)));
        Assert.False(slab.ContainsPoint(new(1, 1, 1)));
        Assert.True(slab.ContainsPoint(new(1, 1, 8)));
    }
}
