using Tabletop.Client.Core; using Tabletop.Geometry;
namespace Tabletop.Client.Tests;

/// <summary>Readable rings plan: a ring's outline as loops, its fill as triangles, and where its label may stand.</summary>
public class RingShapeTests
{
    static PlacedModel At(double x, double y, double diameter = 1) => new(new ModelBody(Base.Circle(diameter), 1), new Placement(new Point3(x, y, 0), new Yaw(0)));

    static double FillArea(RingShape s) => Enumerable.Range(0, s.Triangles.Count / 3)
        .Sum(t => Math.Abs((s.Triangles[3 * t + 1] - s.Triangles[3 * t]).Cross(s.Triangles[3 * t + 2] - s.Triangles[3 * t])) / 2);

    [Fact]
    public void One_round_base_is_one_counter_clockwise_loop_filled_to_its_disc()
    {
        var s = RingShape.Of(RangeOutline.Of([At(10, 10)], 6));
        var loop = Assert.Single(s.Loops);
        Assert.Equal(RangeOutline.Points, loop.Count);
        Assert.True(RingShape.Area(loop) > 0);
        Assert.Equal(Math.PI * 6.5 * 6.5, FillArea(s), 0);                 // the 96-gon is within 0.1% of the disc
        Assert.Equal(RangeOutline.Points - 2, s.Triangles.Count / 3);
    }

    [Fact]
    public void Two_overlapping_bases_fill_their_union_once()
    {
        var s = RingShape.Of(RangeOutline.Of([At(10, 10), At(16, 10)], 6));
        Assert.Single(s.Loops);
        var r = 6.5; var d = 6.0;                                          // two discs of radius r, centres d apart
        var lens = 2 * r * r * Math.Acos(d / (2 * r)) - d / 2 * Math.Sqrt(4 * r * r - d * d);
        Assert.Equal(2 * Math.PI * r * r - lens, FillArea(s), 0);
    }

    [Fact]
    public void Two_bases_far_apart_are_two_loops()
    {
        var s = RingShape.Of(RangeOutline.Of([At(10, 10), At(40, 10)], 6));
        Assert.Equal(2, s.Loops.Count);
        Assert.Equal(2 * Math.PI * 6.5 * 6.5, FillArea(s), 0);
    }

    [Fact]
    public void A_hole_stays_open()
    {
        Point2[] outer = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)], hole = [new(3, 3), new(3, 7), new(7, 7), new(7, 3)];   // the hole clockwise
        var triangles = RingShape.Fill([outer, hole]);
        var area = Enumerable.Range(0, triangles.Count / 3).Sum(t => Math.Abs((triangles[3 * t + 1] - triangles[3 * t]).Cross(triangles[3 * t + 2] - triangles[3 * t])) / 2);
        Assert.Equal(100 - 16, area, 6);
    }

    [Fact]
    public void Segments_given_out_of_order_still_close()
    {
        var segments = RangeOutline.Of([At(10, 10), At(16, 10)], 6).Reverse().ToList();
        Assert.Single(RingShape.LoopsOf(segments));
    }

    [Fact]
    public void Label_candidates_are_spread_along_the_loops()
    {
        var s = RingShape.Of(RangeOutline.Of([At(10, 10)], 6));
        Assert.Equal(RingShape.Labels, s.Candidates.Count);
        Assert.All(s.Candidates, q => Assert.Equal(6.5, q.DistanceTo(new Point2(10, 10)), 1));
        var gaps = s.Candidates.Zip(s.Candidates.Skip(1), (a, b) => a.DistanceTo(b)).ToList();
        Assert.True(gaps.Max() - gaps.Min() < 0.1);
    }

    [Fact]
    public void A_ring_kind_sets_its_look()
    {
        Assert.True(RingLook.Of(RingKind.Charge).FillAlpha > RingLook.Of(RingKind.Shoot).FillAlpha);
        Assert.Equal("charge", RingLook.Of(RingKind.Charge).Icon);
        Assert.Equal("shoot", RingLook.Of(RingKind.Shoot).Icon);
        Assert.Equal("shoot", RingLook.Of(RingKind.Range).Icon);
    }
}

public class RingLabelsTests
{
    static readonly HudRect View = new(0, 0, 1280, 720);

    [Fact]
    public void The_anchor_is_the_lowest_point_on_the_screen()
    {
        Assert.Equal((300f, 600f), RingLabels.Anchor([(100, 200), (300, 600), (500, 400), (700, 900)], View));   // 900 is off the screen
    }

    [Fact]
    public void A_ring_wholly_off_the_screen_anchors_at_its_nearest_point_pulled_inside()
    {
        Assert.Equal((640f, 712f), RingLabels.Anchor([(640, 800), (640, 1000)], View, 8));
        Assert.Null(RingLabels.Anchor([], View));
    }

    [Fact]
    public void Two_labels_at_one_anchor_are_placed_apart_and_inside_the_view()
    {
        var a = RingLabels.Under((640, 400), 120, 30); var b = RingLabels.Under((650, 405), 100, 30);
        var placed = RingLabels.Place([a, b], View);
        Assert.Equal(a, placed[0]);
        Assert.False(placed[0].Overlaps(placed[1]));
        Assert.True(placed[1].Bottom <= placed[0].Y);                       // moved up, toward the ring
    }

    [Fact]
    public void Labels_at_the_top_go_down_and_one_past_the_edge_comes_inside()
    {
        var placed = RingLabels.Place([new HudRect(600, 0, 100, 30), new HudRect(610, 5, 100, 30), new HudRect(1250, -20, 100, 30)], View);
        Assert.False(placed[0].Overlaps(placed[1]));
        Assert.True(placed[1].Y >= placed[0].Bottom);
        Assert.Equal(new HudRect(1180, 0, 100, 30), placed[2]);
        for (var i = 0; i < placed.Count; i++)
            for (var j = i + 1; j < placed.Count; j++) Assert.False(placed[i].Overlaps(placed[j]));
    }
}
