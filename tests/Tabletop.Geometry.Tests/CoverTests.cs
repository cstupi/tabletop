namespace Tabletop.Geometry.Tests;

public class CoverTests
{
    static readonly WorldVolume Forest = Fx.Cylinder(0, 0, 4, 0, 8, VolumeKind.Area, TerrainDeclarations.Cover | TerrainDeclarations.Difficult | Fx.ForestSight);
    static readonly WorldVolume Field = Fx.Cylinder(20, 0, 4, 0, 1, VolumeKind.Area, TerrainDeclarations.Difficult);

    [Fact] public void Fully_inside_a_cover_volume()
    {
        Assert.Equal(new[] { Forest.Id }, Cover.InsideCoverVolumes(Fx.Model(3, 0), [Forest, Field]));
        Assert.Empty(Cover.InsideCoverVolumes(Fx.Model(3.5, 0), [Forest]));
    }
    [Fact] public void A_volume_without_cover_gives_none() => Assert.Empty(Cover.InsideCoverVolumes(Fx.Model(20, 0), [Field]));
    [Fact] public void A_low_wall_is_a_sight_blocker_the_target_is_behind()
    {
        var wall = Fx.Box(5, 0, 1, 6, 0, 1, d: Fx.Blocks);
        Assert.True(LineOfSight.Check(Fx.Model(0, 0), Fx.Model(10, 0), [wall]).Clear);
        Assert.Equal(new[] { wall.Id }, Cover.SightBlockersBetween(Fx.Model(0, 0), Fx.Model(10, 0), [wall]));
    }
    [Fact] public void Nothing_between_means_no_blockers() => Assert.Empty(Cover.SightBlockersBetween(Fx.Model(0, 0), Fx.Model(10, 0), [Fx.Box(5, 5, 1, 6, 0, 4, d: Fx.Blocks)]));
    [Fact] public void A_full_block_is_also_listed()
    {
        var wall = Fx.Box(5, 0, 1, 6, 0, 4, d: Fx.Blocks);
        Assert.Equal(new[] { wall.Id }, Cover.SightBlockersBetween(Fx.Model(0, 0), Fx.Model(10, 0), [wall]));
    }
    [Fact] public void A_transparent_wall_is_not_a_sight_blocker() => Assert.Empty(Cover.SightBlockersBetween(Fx.Model(0, 0), Fx.Model(10, 0), [Fx.Box(5, 0, 1, 6, 0, 1)]));
    [Fact] public void SightBlockersBetween_reports_input_order_not_sample_order()
    {
        var low = Fx.Box(5, 0, 1, 6, 0, 1, d: Fx.Blocks);
        var tall = Fx.Box(5, 0, 1, 6, 0, 4, d: Fx.Blocks);
        Assert.Equal(new[] { low.Id, tall.Id }, Cover.SightBlockersBetween(Fx.Model(0, 0), Fx.Model(10, 0), [low, tall]));
    }
    [Fact] public void Another_unit_is_a_sight_blocker()
    {
        var screen = Bodies.AsBlockingVolume(Guid.CreateVersion7(), Fx.Model(5, 0.8, height: 1));
        Assert.True(LineOfSight.Check(Fx.Model(0, 0), Fx.Model(10, 0), [screen]).Clear);
        Assert.Equal(new[] { screen.Id }, Cover.SightBlockersBetween(Fx.Model(0, 0), Fx.Model(10, 0), [screen]));
    }
    // p.11 "Terrain features that models can hide in or behind ... count as cover terrain"; p.18 "Wall or Fence: Counts as Cover".
    // A fence 0.5" thick across x 4.75..5.25, y -3..3, 1.5" tall, declaring cover and not blocking line of sight.
    static WorldVolume Fence() => Fx.Box(5, 0, 0.5, 6, 0, 1.5, d: TerrainDeclarations.Cover | TerrainDeclarations.Difficult);

    [Fact] public void A_fence_the_lines_pass_through_is_cover_terrain_between()
    {
        var fence = Fence();
        Assert.True(LineOfSight.Check(Fx.Model(0, 0), Fx.Model(5.88, 0), [fence]).Clear);              // a fence does not block line of sight
        Assert.Equal(new[] { fence.Id }, Cover.CoverTerrainBetween(Fx.Model(0, 0), Fx.Model(5.88, 0), [fence]));   // in base contact behind it
        Assert.Equal(new[] { fence.Id }, Cover.CoverTerrainBetween(Fx.Model(0, 0), Fx.Model(10, 0), [fence]));
    }
    [Fact] public void A_model_beside_the_fence_is_not_behind_it() => Assert.Empty(Cover.CoverTerrainBetween(Fx.Model(0, 0), Fx.Model(6, 5), [Fence()]));
    [Fact] public void A_model_in_contact_on_the_shooters_side_is_not_behind_it() => Assert.Empty(Cover.CoverTerrainBetween(Fx.Model(0, 0), Fx.Model(4.12, 0), [Fence()]));
    [Fact] public void A_model_standing_on_the_cover_is_not_behind_it() => Assert.Empty(Cover.CoverTerrainBetween(Fx.Model(0, 0), Fx.Model(5, 0, z: 1.5), [Fence()]));
    [Fact] public void Cover_the_shooter_stands_in_is_not_between()
    {
        var field = Fx.Box(2, 0, 6, 6, 0, 1, kind: VolumeKind.Area, d: TerrainDeclarations.Cover);
        Assert.Empty(Cover.CoverTerrainBetween(Fx.Model(0, 0), Fx.Model(10, 0), [field]));
        Assert.Equal(new[] { field.Id }, Cover.CoverTerrainBetween(Fx.Model(-10, 0), Fx.Model(10, 0), [field]));
    }
    [Fact] public void Cover_that_blocks_line_of_sight_is_left_to_the_sight_blocker_test() =>
        Assert.Empty(Cover.CoverTerrainBetween(Fx.Model(0, 0), Fx.Model(10, 0), [Fx.Box(5, 0, 0.5, 6, 0, 1.5, d: TerrainDeclarations.Cover | Fx.Blocks)]));
}
