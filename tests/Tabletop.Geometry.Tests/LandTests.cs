namespace Tabletop.Geometry.Tests;

/// <summary>Held pieces lift over spec → Lifted and Landing: straight to the target, drawn over what is under it, landed at the nearest clear spot.</summary>
public class LandTests
{
    static readonly WorldVolume Wall = Fx.Box(10, 10, 0.5, 20, 0, 6);                     // x 9.75..10.25, y 0..20
    static readonly WorldVolume Hill = Fx.Box(12, 12, 6, 6, 0, 2);                       // x and y 9..15, top 2
    static PlacedModel At(Point3 p) => Fx.Model(p.X, p.Y, p.Z);

    [Fact] public void A_free_spot_is_landed_on_as_it_is() => Assert.Equal(new Point3(5, 5, 0), Collision.Land(Fx.Model(0, 0), new(5, 5), [], [], []));

    [Fact]
    public void A_spot_on_a_base_lands_at_the_nearest_clear_spot()
    {
        var other = Fx.Model(5, 5);
        var end = Collision.Land(Fx.Model(0, 0), new(5, 5), [other], [], []);
        Assert.False(Overlap.Bases(At(end), other));
        Assert.True(end.Horizontal.DistanceTo(new Point2(5, 5)) <= 1.5 + 1e-9, $"at {end}");
    }

    [Fact]
    public void A_spot_in_a_wall_lands_beside_it()
    {
        var end = Collision.Land(Fx.Model(0, 0), new(10, 10), [], [Wall], [Wall]);
        Assert.True(Math.Abs(end.X - 10) >= 0.25 + 0.63 - 1e-3, $"at {end}");
        Assert.True(end.Horizontal.DistanceTo(new Point2(10, 10)) <= 1 + 1e-9, $"at {end}");
    }

    [Fact] public void A_model_on_another_storey_does_not_move_the_landing() =>
        Assert.Equal(new Point3(5, 5, 0), Collision.Land(Fx.Model(0, 0), new(5, 5), [Fx.Model(5, 5, z: 4)], [], []));

    [Fact]
    public void The_landing_rests_on_its_highest_open_surface()
    {
        Assert.Equal(new Point3(12, 12, 2), Collision.Land(Fx.Model(0, 0), new(12, 12), [], [], [Hill]));
        var deck = Fx.Box(30, 10, 8, 4, 3, 4);
        Assert.Equal(new Point3(30, 10, 4), Collision.Land(Fx.Model(0, 0), new(30, 10), [], [], [deck]));
    }

    [Fact]
    public void Only_allowed_spots_are_candidates()
    {
        var end = Collision.Land(Fx.Model(0, 0), new(5, 5), [Fx.Model(5, 5)], [], [], p => p.X <= 4);
        Assert.True(end.X <= 4, $"at {end}");
    }

    [Fact]
    public void A_group_lands_in_drag_order()
    {
        var ends = Collision.Land([(Fx.Model(0, 0), new Point2(5, 5), null), (Fx.Model(0, 2), new Point2(5, 5), null)], [], [], []);
        Assert.Equal(new Point3(5, 5, 0), ends[0]);
        Assert.False(Overlap.Bases(At(ends[0]), At(ends[1])));
    }

    [Fact]
    public void The_hover_is_half_an_inch_over_the_highest_thing_under_it()
    {
        Assert.Equal(4, Collision.Hover(Fx.Model(12, 12), [Hill], [Fx.Model(12.5, 12, z: 2)]), 6);
        Assert.Equal(Collision.Lift, Collision.Hover(Fx.Model(40, 40), [Hill], []), 6);
        var wood = Fx.Cylinder(30, 30, 3, 0, 6, VolumeKind.Area);
        Assert.Equal(6.5, Collision.Hover([Fx.Box(30, 30, 2, 2, 0, 1)], [wood]), 6);
    }

    // A charger from (20, 24) towards a target model at (28, 24): 1.26" bases touch with centres 1.26" apart, at x 26.74.
    static readonly PlacedModel Charger = Fx.Model(20, 24), Target = Fx.Model(28, 24);
    static MoveReach Reach12 => MoveReach.Build(Charger, 12, [], [], null, new TableSize(72, 48), [Target]);
    static double Gap(Point3 end, PlacedModel to) => Distance.EdgeToEdge(At(end), to);

    [Theory]
    [InlineData(0.0)] [InlineData(0.05)] [InlineData(0.2)] [InlineData(0.5)]
    public void A_drop_onto_the_target_s_base_lands_touching_it(double overlap)
    {
        // The drag's path (TableInput.Place): the target held to the reach (MoveReach.Clamp), then landed within it.
        var reach = Reach12;
        var end = Collision.Land(Charger, reach.Clamp(new Point2(26.74 + overlap, 24)), [Target], [], [], p => reach.Reaches(p));
        Assert.InRange(Gap(end, Target), 0, 1e-3);
    }

    [Fact]
    public void Without_an_abutment_a_drop_shortByof_the_target_stays_where_it_was_dropped()
    {
        // Why a charge needed an overlapping drop: a drop 0.3" short is a free spot, so it lands there, outside the old 0.25" slack.
        var reach = Reach12;
        var end = Collision.Land(Charger, new Point2(26.44, 24), [Target], [], [], p => reach.Reaches(p));
        Assert.Equal(0.3, Gap(end, Target), 6);
    }

    [Theory]
    [InlineData(0.1)] [InlineData(0.3)] [InlineData(0.48)]
    public void With_an_abutment_a_drop_within_its_distance_lands_flush_against_the_target(double shortBy)
    {
        var reach = Reach12;
        var end = Collision.Land(Charger, new Point2(26.74 - shortBy, 24.1), [Target], [], [], p => reach.Reaches(p), new Abutment([Target], 0.5));
        Assert.InRange(Gap(end, Target), 0, 1e-6);
        Assert.False(Overlap.Bases(At(end), Target));
    }

    [Fact]
    public void A_drop_farther_than_the_abutment_distance_stays_where_it_was_dropped()
    {
        var end = Collision.Land(Charger, new Point2(26.04, 24), [Target], [], [], null, new Abutment([Target], 0.5));
        Assert.Equal(0.7, Gap(end, Target), 6);
    }

    [Fact]
    public void An_oval_target_is_abutted_at_its_nearest_edge()
    {
        var oval = Fx.Model(Base.Oval(2.36, 1.38), 28, 24, yaw: 30);
        var end = Collision.Land(Charger, new Point2(26.3, 24.5), [oval], [], [], null, new Abutment([oval], 0.5));
        Assert.InRange(Gap(end, oval), 0, 1e-6);
        Assert.False(Overlap.Bases(At(end), oval));
    }

    [Fact]
    public void An_abutment_that_would_overlap_another_model_is_not_taken()
    {
        // A friend standing where the flush spot would put the charger on its base.
        var friend = Fx.Model(26.6, 25.25);
        var dropped = new Point2(26.4, 24);   // 0.34" from the target; flush at (26.74, 24) would overlap the friend
        var end = Collision.Land(Charger, dropped, [Target, friend], [], [], null, new Abutment([Target], 0.5));
        Assert.Equal(new Point3(26.4, 24, 0), end);
    }

    [Fact]
    public void An_abutment_outside_the_reach_is_not_taken()
    {
        var reach = MoveReach.Build(Charger, 6.5, [], [], null, new TableSize(72, 48), [Target]);
        var end = Collision.Land(Charger, new Point2(26.44, 24), [Target], [], [], p => reach.Reaches(p), new Abutment([Target], 0.5));
        Assert.Equal(new Point3(26.44, 24, 0), end);
    }

    [Fact]
    public void A_lifted_piece_lands_clear_of_walls()
    {
        var crate = Fx.Box(2, 10, 2, 2, 0, 2);
        var end = Collision.Land([crate], new(10, 10), [Wall], [Wall]);
        Assert.Equal(Collision.Clear([crate with { Transform = crate.Transform with { Position = new Point3(10, 10, 0) } }], [Wall], [Wall]), end);
        Assert.False(Collision.Blocked([crate with { Transform = end }], [Wall]));
    }
}
