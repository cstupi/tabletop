namespace Tabletop.Geometry.Tests;

public class ValueTests
{
    [Fact]
    public void Circle_base_stores_diameter_in_both_dimensions()
    {
        var b = Base.Circle(1.26);
        Assert.Equal(BaseShape.Circle, b.Shape);
        Assert.Equal(1.26, b.A);
        Assert.Equal(1.26, b.B);
    }

    [Fact]
    public void Oval_base_is_length_then_width()
    {
        var b = Base.Oval(2.36, 1.38);
        Assert.Equal((2.36, 1.38), (b.A, b.B));
    }

    [Fact]
    public void Rect_footprint_is_width_then_length_and_circle_is_radius()
    {
        Assert.Equal((FootprintKind.Rect, 4.0, 6.0), (Footprint.Rect(4, 6).Kind, Footprint.Rect(4, 6).A, Footprint.Rect(4, 6).B));
        Assert.Equal((FootprintKind.Circle, 3.0, 3.0), (Footprint.Circle(3).Kind, Footprint.Circle(3).A, Footprint.Circle(3).B));
    }

    [Fact]
    public void Declarations_combine_as_flags()
    {
        var forest = TerrainDeclarations.Cover | TerrainDeclarations.Difficult | TerrainDeclarations.SeeInAndOut;
        Assert.True(forest.HasFlag(TerrainDeclarations.Cover));
        Assert.False(forest.HasFlag(TerrainDeclarations.Impassable));
    }

    [Fact]
    public void Point3_horizontal_drops_z() => Assert.Equal(new Point2(1, 2), new Point3(1, 2, 3).Horizontal);

    [Fact]
    public void Volume_is_a_value()
    {
        Volume Hill() => new(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.None, null);
        Assert.Equal(Hill(), Hill());
    }
}
