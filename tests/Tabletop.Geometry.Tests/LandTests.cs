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

    [Fact]
    public void A_lifted_piece_lands_clear_of_walls()
    {
        var crate = Fx.Box(2, 10, 2, 2, 0, 2);
        var end = Collision.Land([crate], new(10, 10), [Wall], [Wall]);
        Assert.Equal(Collision.Clear([crate with { Transform = crate.Transform with { Position = new Point3(10, 10, 0) } }], [Wall], [Wall]), end);
        Assert.False(Collision.Blocked([crate with { Transform = end }], [Wall]));
    }
}
