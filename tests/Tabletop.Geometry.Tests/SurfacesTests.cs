namespace Tabletop.Geometry.Tests;

/// <summary>Held pieces lift over spec → Surfaces: the heights a body could rest at, open where it overlaps no Solid; the rest nearest a level.</summary>
public class SurfacesTests
{
    static readonly WorldVolume Hill = Fx.Box(12, 12, 6, 6, 0, 2);                       // x and y 9..15, top 2
    static readonly WorldVolume Deck = Fx.Box(12, 10, 8, 4, 3, 4);                       // x 8..16, y 8..12, Z 3..4

    [Fact] public void The_open_table_has_one_surface() => Assert.Equal([0.0], Collision.Surfaces(Fx.Model(30, 30), []));

    [Fact]
    public void A_hill_hides_the_table()
    {
        Assert.Equal([2.0], Collision.Surfaces(Fx.Model(15.2, 12), [Hill]));
        Assert.Equal(2, Collision.RestNear(Fx.Model(15.2, 12), [Hill], 0), 6);
    }

    [Fact]
    public void A_bridge_with_room_has_two_surfaces()
    {
        var under = Fx.Model(12, 10);
        Assert.Equal([0.0, 4.0], Collision.Surfaces(under, [Deck]));
        Assert.Equal(0, Collision.RestNear(under, [Deck], 0));
        Assert.Equal(4, Collision.RestNear(under, [Deck], 3.9), 6);
        Assert.Equal(4, Collision.RestNear(under, [Deck], double.PositiveInfinity), 6);
    }

    [Fact] public void A_low_bridge_has_no_room_under_it() => Assert.Equal([2.0], Collision.Surfaces(Fx.Model(12, 10), [Fx.Box(12, 10, 8, 4, 1, 2)]));

    [Fact] public void A_tie_takes_the_higher() => Assert.Equal(4, Collision.RestNear(Fx.Model(12, 10), [Deck], 2), 6);

    [Fact]
    public void An_area_floor_is_a_surface_for_a_model_not_a_piece()
    {
        var lifted = Fx.Cylinder(30, 30, 3, 0, 6, VolumeKind.Area, z: 2);
        Assert.Equal([0.0, 2.0], Collision.Surfaces(Fx.Model(30, 30), [lifted]));
        Assert.Equal([0.0], Collision.Surfaces([Fx.Box(30, 30, 2, 2, 0, 1)], [lifted]));
    }

    [Fact]
    public void A_piece_reads_each_of_its_volumes_heights()
    {
        Assert.Equal([0.0, 4.0], Collision.Surfaces([Fx.Box(12, 10, 2, 2, 0, 2)], [Deck]));
        Assert.Equal([4.0], Collision.Surfaces([Fx.Box(12, 10, 2, 2, 0, 3.5)], [Deck]));
    }

    [Fact]
    public void The_highest_open_surface_is_the_rest_height()
    {
        PlacedModel[] models = [Fx.Model(30, 30), Fx.Model(15.2, 12), Fx.Model(15.63, 12), Fx.Model(15, 12), Fx.Model(12, 10)];
        WorldVolume[] volumes = [Hill, Fx.Box(16, 12, 2, 2, 0, 4), Deck, Fx.Cylinder(16, 12, 3, 0, 6, VolumeKind.Area, z: 2)];
        foreach (var m in models) Assert.Equal(Collision.RestHeight(m, volumes), Collision.Surfaces(m, volumes)[^1], 6);
    }
}
