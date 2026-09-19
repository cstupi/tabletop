namespace Tabletop.Geometry.Tests;

public class OutlineTests
{
    internal static PolygonOutline Rect(double cx, double cy, double w, double l) =>
        new([new(cx - w / 2, cy - l / 2), new(cx + w / 2, cy - l / 2), new(cx + w / 2, cy + l / 2), new(cx - w / 2, cy + l / 2)]);

    [Fact] public void Circles_separate_along_the_centre_line()
    {
        var (d, onA, onB) = Outline.Separation(new CircleOutline(new(0, 0), 0.63), new CircleOutline(new(6, 0), 0.63));
        Assert.Equal(4.74, d, 6);
        Assert.Equal(0.63, onA.X, 6);
        Assert.Equal(5.37, onB.X, 6);
    }
    [Fact] public void Overlapping_circles_are_at_distance_zero() =>
        Assert.Equal(0, Outline.Separation(new CircleOutline(new(0, 0), 1), new CircleOutline(new(1, 0), 1)).Distance);
    [Fact] public void Circle_to_rectangle_measures_to_the_nearest_edge()
    {
        var (d, _, onRect) = Outline.Separation(new CircleOutline(new(0, 0), 0.5), Rect(5, 0, 4, 4));
        Assert.Equal(2.5, d, 6);
        Assert.Equal(3, onRect.X, 6);
    }
    [Fact] public void Rectangle_to_rectangle_uses_the_closest_vertex_edge_pair()
    {
        Assert.Equal(1, Outline.Separation(Rect(0, 0, 2, 2), Rect(3, 0, 2, 2)).Distance, 6);
        Assert.Equal(Math.Sqrt(2), Outline.Separation(Rect(0, 0, 2, 2), Rect(3, 3, 2, 2)).Distance, 6);
        Assert.Equal(0, Outline.Separation(Rect(0, 0, 2, 2), Rect(1, 1, 2, 2)).Distance);
    }
    [Fact] public void Touching_is_not_intersecting()
    {
        Assert.False(Outline.InteriorsIntersect(Rect(0, 0, 2, 2), Rect(2, 0, 2, 2)));
        Assert.True(Outline.InteriorsIntersect(Rect(0, 0, 2, 2), Rect(1.5, 0, 2, 2)));
        Assert.False(Outline.InteriorsIntersect(new CircleOutline(new(0, 0), 1), new CircleOutline(new(2, 0), 1)));
        Assert.True(Outline.InteriorsIntersect(new CircleOutline(new(0, 0), 1), Rect(1.5, 0, 2, 2)));
    }
    [Fact] public void Rotated_squares_intersect_by_separating_axis()
    {
        var diamond = new PolygonOutline([new(1.4, 0), new(0, 1.4), new(-1.4, 0), new(0, -1.4)]);
        Assert.True(Outline.InteriorsIntersect(diamond, Rect(2, 0, 2, 2)));
        Assert.False(Outline.InteriorsIntersect(diamond, Rect(2.5, 2.5, 2, 2)));
    }
    [Fact] public void Clockwise_vertices_are_reordered()
    {
        var p = new PolygonOutline([new(0, 0), new(0, 1), new(1, 1), new(1, 0)]);
        Assert.True(p.Contains(new(0.5, 0.5)));
        Assert.False(p.Contains(new(2, 0.5)));
        Assert.True(p.Contains(new(1, 0.5)));
    }
    [Fact] public void Segment_intersection()
    {
        var wall = Rect(5, 0, 1, 6);
        Assert.True(wall.IntersectsSegment(new(0, 0), new(10, 0)));
        Assert.False(wall.IntersectsSegment(new(0, 4), new(10, 4)));
        Assert.True(wall.IntersectsSegment(new(5, 0), new(5, 1)));
        Assert.True(new CircleOutline(new(5, 0), 1).IntersectsSegment(new(0, 0.5), new(10, 0.5)));
        Assert.False(new CircleOutline(new(5, 0), 1).IntersectsSegment(new(0, 1.5), new(10, 1.5)));
    }
    [Fact] public void Whole_outline_containment()
    {
        var forest = new CircleOutline(new(0, 0), 4);
        Assert.True(forest.ContainsOutline(new CircleOutline(new(3, 0), 0.63)));
        Assert.False(forest.ContainsOutline(new CircleOutline(new(3.5, 0), 0.63)));
        Assert.True(Rect(0, 0, 10, 10).ContainsOutline(Rect(1, 1, 2, 2)));
        Assert.False(Rect(0, 0, 10, 10).ContainsOutline(Rect(5, 1, 2, 2)));
    }
    [Fact] public void Boundary_points_of_a_circle_lie_on_it()
    {
        var pts = new CircleOutline(new(1, 1), 2).BoundaryPoints(8);
        Assert.Equal(8, pts.Count);
        Assert.All(pts, p => Assert.Equal(2, p.DistanceTo(new(1, 1)), 6));
    }
}
