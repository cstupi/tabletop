namespace Tabletop.Geometry.Tests;

public class LineOfSightTests
{
    static readonly PlacedModel Shooter = Fx.Model(0, 0);
    static readonly PlacedModel Target = Fx.Model(10, 0);

    [Fact] public void Open_table_is_clear()
    {
        var r = LineOfSight.Check(Shooter, Target, []);
        Assert.True(r.Clear);
        Assert.Empty(r.ObstructedBy);
    }
    [Fact] public void A_blocking_wall_obstructs()
    {
        var wall = Fx.Box(5, 0, 1, 6, 0, 4, d: Fx.Blocks);
        var r = LineOfSight.Check(Shooter, Target, [wall]);
        Assert.False(r.Clear);
        Assert.Equal(new[] { wall.Id }, r.ObstructedBy);
    }
    [Fact] public void A_low_wall_is_shot_over() => Assert.True(LineOfSight.Check(Shooter, Target, [Fx.Box(5, 0, 1, 6, 0, 1, d: Fx.Blocks)]).Clear);
    [Fact] public void Blocking_is_declared_not_derived() => Assert.True(LineOfSight.Check(Shooter, Target, [Fx.Box(5, 0, 1, 6, 0, 4)]).Clear);
    [Fact] public void A_forest_blocks_sight_through_it() =>
        Assert.False(LineOfSight.Check(Shooter, Target, [Fx.Cylinder(5, 0, 2, 0, 8, VolumeKind.Area, Fx.ForestSight)]).Clear);
    [Fact] public void A_forest_can_be_seen_into_out_of_and_within()
    {
        var forest = Fx.Cylinder(5, 0, 2, 0, 8, VolumeKind.Area, Fx.ForestSight);
        Assert.True(LineOfSight.Check(Shooter, Fx.Model(5, 0), [forest]).Clear);
        Assert.True(LineOfSight.Check(Fx.Model(4, 0), Target, [forest]).Clear);
        Assert.True(LineOfSight.Check(Fx.Model(4, 0), Fx.Model(6, 0), [forest]).Clear);
    }
    [Fact] public void See_in_and_out_means_nothing_without_blocking() =>
        Assert.True(LineOfSight.Check(Shooter, Target, [Fx.Cylinder(5, 0, 2, 0, 8, VolumeKind.Area, TerrainDeclarations.SeeInAndOut)]).Clear);
    [Fact] public void The_first_of_two_forests_still_blocks()
    {
        var near = Fx.Cylinder(4, 0, 1.5, 0, 8, VolumeKind.Area, Fx.ForestSight);
        var far = Fx.Cylinder(9, 0, 1.5, 0, 8, VolumeKind.Area, Fx.ForestSight);
        var r = LineOfSight.Check(Shooter, Fx.Model(9, 0), [near, far]);
        Assert.False(r.Clear);
        Assert.Equal(new[] { near.Id }, r.ObstructedBy);
    }
    [Fact] public void A_wall_off_the_line_does_nothing() => Assert.True(LineOfSight.Check(Shooter, Target, [Fx.Box(5, 5, 1, 6, 0, 4, d: Fx.Blocks)]).Clear);
    [Fact] public void Tall_bodies_see_over_what_short_ones_cannot()
    {
        var wall = Fx.Box(5, 0, 1, 6, 0, 2, d: Fx.Blocks);
        Assert.True(LineOfSight.Check(Fx.Model(0, 0, height: 4), Fx.Model(10, 0, height: 4), [wall]).Clear);   // eye at 2, the target's top at 4: z = 3 over the wall
        Assert.False(LineOfSight.Check(Fx.Model(0, 0, height: 2), Fx.Model(10, 0, height: 2), [wall]).Clear);  // eye at 1, the target's top level with the wall
    }
    [Fact] public void Other_units_block_as_bodies() =>
        Assert.False(LineOfSight.Check(Shooter, Target, [Bodies.AsBlockingVolume(Guid.CreateVersion7(), Fx.Model(5, 0, height: 3))]).Clear);
    [Fact] public void Partly_hidden_is_still_clear()
    {
        var r = LineOfSight.Check(Shooter, Target, [Fx.Box(5, 0.5, 1, 1, 0, 4, d: Fx.Blocks)]);
        Assert.True(r.Clear);
        Assert.Empty(r.ObstructedBy);
    }
    [Fact] public void Target_points_ring_the_body_at_three_heights()
    {
        Assert.Equal(27, LineOfSight.TargetPoints(Target).Count());                            // a circle: the centre and eight rim points at the base, half height and the top
        Assert.Equal(15, LineOfSight.TargetPoints(Fx.Model(Base.Rect(1, 1), 10, 0)).Count());   // a rect: its four vertices are its whole rim
        Assert.Contains(new Point3(10, 0, 0.75), LineOfSight.TargetPoints(Target));            // the centre-to-centre line is one of them
    }
    [Fact] public void Visible_models_are_reported_one_per_model()
    {
        var wall = Fx.Box(5, 0, 1, 3, 0, 4, d: Fx.Blocks);
        PlacedModel[] unit = [Fx.Model(10, 0), Fx.Model(10, 6)];
        var seen = LineOfSight.VisibleModels([Shooter], unit, [wall]);
        Assert.Equal(new[] { false, true }, seen);
        Assert.Equal(1, seen.Count(v => v));                                                   // the count is geometry's; p.11's majority is the ruleset's
    }
    [Fact] public void A_second_observer_sees_what_the_first_cannot()
    {
        var wall = Fx.Box(5, 0, 1, 3, 0, 4, d: Fx.Blocks);
        Assert.Equal(new[] { true }, LineOfSight.VisibleModels([Shooter, Fx.Model(0, 6)], [Fx.Model(10, 0)], [wall]));
    }
}
