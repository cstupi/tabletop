namespace Tabletop.Geometry.Tests;

public class SlideTests
{
    static readonly WorldVolume Wall = Fx.Box(10, 10, 0.5, 20, 0, 6);                     // x 9.75..10.25, y 0..20
    static readonly WorldVolume Hill = Fx.Box(12, 12, 6, 6, 0, 2);
    static PlacedModel At(PlacedModel m, Point3 p) => m with { Placement = m.Placement with { Position = p } };

    [Fact] public void A_free_drag_reaches_its_target() => Assert.Equal(new Point3(5, 3, 0), Collision.Slide(Fx.Model(2, 3), new(5, 3), [], [], []));
    [Fact] public void A_climbable_top_is_climbed() => Assert.Equal(new Point3(12, 12, 2), Collision.Slide(Fx.Model(2, 12), new(12, 12), [], [], [Hill]));
    [Fact] public void A_model_on_another_storey_does_not_block() => Assert.Equal(5, Collision.Slide(Fx.Model(2, 3), new(5, 3), [Fx.Model(4, 3, z: 4)], [], []).X, 6);
    [Fact] public void A_body_starting_inside_an_obstacle_can_leave() => Assert.Equal(5, Collision.Slide(Fx.Model(10, 10), new(5, 10), [], [Wall], [Wall]).X, 6);
    [Fact] public void A_long_drag_does_not_tunnel_through_a_thin_wall() => Assert.True(Collision.Slide(Fx.Model(8, 10), new(18, 10), [], [Wall], [Wall]).X < 9.75 - 0.62);

    [Fact]
    public void Stops_at_a_wall_and_slides_along_it()
    {
        var end = Collision.Slide(Fx.Model(8, 10), new(12, 12), [], [Wall], [Wall]);
        Assert.Equal(9.12, end.X, 2);                                                     // 9.75 less the 0.63 radius
        Assert.Equal(12, end.Y, 2);
    }

    [Fact]
    public void Slides_along_a_model_without_entering_it()
    {
        var other = Fx.Model(5, 0);
        var end = Collision.Slide(Fx.Model(2, 0), new(8, 0.3), [other], [], []);
        Assert.False(Overlap.Bases(At(Fx.Model(0, 0), end), other));
        Assert.True(end.X > 3.7);
        Assert.True(end.Y > 0.5);                                                         // slid round it to y 0.59; a dead stop at contact is at y 0.09
    }

    [Fact]
    public void A_group_compresses_against_an_obstacle()
    {
        PlacedModel[] row = [Fx.Model(5, 10), Fx.Model(3.5, 10), Fx.Model(2, 10)];          // drag order: the lead first
        var ends = Collision.Separate(row.Select(m => (m, m.Placement.Position.Horizontal + new Point2(6, 0))).ToList(), [], [Wall], [Wall]);
        var placed = ends.Select(p => At(Fx.Model(0, 0), p)).ToList();
        Assert.All(placed, m => Assert.False(Overlap.WithSolid(m, Wall)));
        Assert.False(Overlap.Bases(placed[0], placed[1]) || Overlap.Bases(placed[1], placed[2]) || Overlap.Bases(placed[0], placed[2]));
        Assert.Equal(9.12, ends[0].X, 2);
    }

    [Fact]
    public void A_model_it_starts_under_blocks_once_it_climbs_to_it()
    {
        var step = Fx.Box(11.5, 3, 17, 10, 0, 4);                                            // x 3..20, top 4
        var above = Fx.Model(1.5, 3, z: 4);                                                  // x 0.87..2.13, z 4..5.5, over the mover's start
        var end = Collision.Slide(Fx.Model(1, 3), new(6, 3), [above], [], [step]);
        Assert.True(end.X < 2.5, $"stopped at {end}");                                       // climbing the step at x 2.37 meets it
    }

    [Fact]
    public void A_terrain_piece_slides_and_rests()
    {
        var wood = Fx.Cylinder(2, 30, 3, 0, 6, VolumeKind.Area);
        Assert.Equal(new Point3(12, 12, 2), Collision.Slide([wood], new(12, 12), [], [Hill]).Position);
        Assert.True(Collision.Slide([Fx.Cylinder(2, 10, 3, 0, 6, VolumeKind.Area)], new(16, 10), [Wall], [Wall]).Position.X < 9.75 - 2.99);
    }

    [Fact]
    public void A_slide_turns_along_a_second_wall_it_meets()
    {
        var slant = Fx.Box(8, 22, 0.5, 12, 0, 6, yaw: 45);                                  // the line x + y = 30, from (3.8, 26.2) to (12.2, 17.8)
        var end = Collision.Slide(Fx.Model(8, 10), new(12, 30), [], [Wall, slant], [Wall, slant]);
        Assert.True(end.X < 8 && end.Y > 21, $"stopped at {end}");                          // a single projection stops at (9.12, 19.6) under the slant
        Assert.False(Overlap.WithSolid(At(Fx.Model(0, 0), end), slant) || Overlap.WithSolid(At(Fx.Model(0, 0), end), Wall));
    }

    [Fact]
    public void A_turned_wall_stops_a_body_where_its_turned_footprint_stands()
    {
        var across = Fx.Box(10, 10, 0.5, 20, 0, 6, yaw: 90);                                 // x 0..20, y 9.75..10.25
        Assert.Equal(9.12, Collision.Slide(Fx.Model(5, 8), new(5, 12), [], [across], [across]).Y, 2);
        Assert.Equal(12, Collision.Slide(Fx.Model(5, 8), new(5, 12), [], [Wall], [Wall]).Y, 6);          // unturned it stands at x 9.75..10.25, clear of x 5
    }

    [Fact]
    public void A_terrain_piece_follows_a_free_cursor_past_an_obstacle()
    {
        var wood = Fx.Cylinder(5, 10, 2, 0, 6, VolumeKind.Area);
        Assert.Equal(new Point3(15, 10, 0), Collision.Follow([wood], new(15, 10), [Wall], [Wall]).Position);   // lifted over: the spot is free
        Assert.True(Collision.Follow([wood], new(10, 10), [Wall], [Wall]).Position.X < 9.75 - 1.99);       // the spot is the wall: the nearest free spot on the way
    }

    [Fact]
    public void A_turned_piece_is_cleared_out_of_what_it_turned_into()
    {
        var bar = Fx.Box(8.5, 10, 0.5, 6, 0, 2, VolumeKind.Area, yaw: 90);                   // x 5.5..11.5 once turned: through the wall
        var cleared = Collision.Clear([bar], [Wall], [Wall]);
        Assert.False(Collision.Blocked([bar with { Transform = cleared }], [Wall]));
        Assert.True(cleared.Position.Horizontal.DistanceTo(new Point2(8.5, 10)) < 3.5);
        Assert.Equal(new Yaw(90), cleared.Yaw);
    }
}
