namespace Tabletop.Geometry.Tests;

/// <summary>Client UX spec → Physical collision: rest on the highest Solid top under any part of the body, else 0.</summary>
public class CollisionTests
{
    static readonly WorldVolume Hill = Fx.Box(12, 12, 6, 6, 0, 2);                       // x and y 9..15, top 2

    [Fact] public void Open_table_is_zero() => Assert.Equal(0, Collision.RestHeight(Fx.Model(30, 30), [Hill]));
    [Fact] public void A_base_half_over_a_hill_stands_on_it() => Assert.Equal(2, Collision.RestHeight(Fx.Model(15.2, 12), [Hill]), 6);
    [Fact] public void Touching_a_side_is_not_standing_on_it() => Assert.Equal(0, Collision.RestHeight(Fx.Model(15.63, 12), [Hill]));
    [Fact] public void The_highest_solid_under_any_part_wins() => Assert.Equal(4, Collision.RestHeight(Fx.Model(15, 12), [Hill, Fx.Box(16, 12, 2, 2, 0, 4)]), 6);
    [Fact] public void A_point_rests_on_the_solid_containing_it() => Assert.Equal(2, Collision.RestHeight(new Point2(12, 12), [Hill]), 6);

    [Fact]
    public void An_area_lifted_onto_a_hill_floors_at_its_base()
    {
        Assert.Equal(2, Collision.RestHeight(Fx.Model(18, 12), [Hill, Fx.Cylinder(16, 12, 3, 0, 6, VolumeKind.Area, z: 2)]), 6);
        Assert.Equal(0, Collision.RestHeight(Fx.Model(18, 12), [Fx.Cylinder(16, 12, 3, 0, 6, VolumeKind.Area)]));
    }

    [Fact]
    public void Terrain_rests_on_terrain_and_ignores_itself()
    {
        var wood = Fx.Cylinder(13, 12, 3, 0, 6, VolumeKind.Area);
        Assert.Equal(2, Collision.RestHeight([wood], [Hill, wood]), 6);                   // a wood dropped on a hill sits on the hill
        Assert.Equal(0, Collision.RestHeight([Hill], [Hill, wood]));                      // an Area is never a floor for a piece
    }
}
