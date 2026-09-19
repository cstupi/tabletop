namespace Tabletop.Geometry.Tests;

public class PolygonTests
{
    static readonly Polygon Square = new([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
    static readonly Polygon Clockwise = new([new(0, 0), new(0, 1), new(1, 1), new(1, 0)]);
    static readonly Polygon L = new([new(0, 0), new(2, 0), new(2, 1), new(1, 1), new(1, 2), new(0, 2)]);

    [Fact] public void Interior_point_is_inside() => Assert.True(Square.Contains(new(0.5, 0.5)));
    [Fact] public void Exterior_point_is_outside() => Assert.False(Square.Contains(new(1.5, 0.5)));
    [Fact] public void Edge_and_vertex_count_as_inside()
    {
        Assert.True(Square.Contains(new(1, 0.5)));
        Assert.True(Square.Contains(new(0, 0)));
    }
    [Fact] public void Vertex_order_does_not_matter() => Assert.True(Clockwise.Contains(new(0.5, 0.5)));
    [Fact] public void Concave_polygon_excludes_its_notch()
    {
        Assert.False(L.Contains(new(1.5, 1.5)));
        Assert.True(L.Contains(new(0.5, 1.5)));
    }
    [Fact] public void Rotate_turns_counter_clockwise_from_plus_y()
    {
        var p = new Point2(0, 1).Rotate(new Yaw(90));
        Assert.Equal(-1, p.X, 6);
        Assert.Equal(0, p.Y, 6);
    }
    [Fact] public void Vector_helpers() => Assert.Equal(5, (new Point2(3, 4) - new Point2(0, 0)).Length, 6);
}
