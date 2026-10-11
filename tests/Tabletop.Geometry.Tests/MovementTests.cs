namespace Tabletop.Geometry.Tests;

public class MovementTests
{
    static readonly PlacedModel Start = Fx.Model(0, 0);
    static readonly WorldVolume Wall = Fx.Box(4, 0, 1, 10, 0, 4, d: TerrainDeclarations.Impassable);
    static readonly WorldVolume Forest = Fx.Cylinder(3, 0, 1, 0, 8, VolumeKind.Area, TerrainDeclarations.Difficult);
    static readonly TableSize Opr = new(72, 48);

    [Fact] public void The_farthest_on_table_reach_is_the_inset_corner_farthest_from_the_start()
    {
        var model = Fx.Model(18, 18);
        var (_, radius) = model.BaseOutline().BoundingCircle();
        var (distance, to) = Movement.FarthestOnTable(model, new TableSize(36, 36));
        Assert.Equal(radius, Math.Min(to.X, to.Y), 6);
        Assert.Equal(Math.Sqrt(2) * (18 - radius), distance, 6);
        Assert.False(Movement.Check(model, to, distance, [], [], null, new TableSize(36, 36)).LeavesTable);
        Assert.Equal(Math.Sqrt((72 - radius) * (72 - radius) + (48 - radius) * (48 - radius)), Movement.FarthestOnTable(Fx.Model(0, 0), Opr).Distance, 6);
    }

    [Fact] public void The_room_along_a_direction_is_how_far_the_base_centre_goes_that_way_with_the_whole_base_on_the_table()
    {
        var model = Fx.Model(18, 18);
        var (_, radius) = model.BaseOutline().BoundingCircle();
        var table = new TableSize(36, 36);
        Assert.Equal(18 - radius, Movement.RoomAlong(model, new Point2(20, 18), table), 6);                    // east, to the inset edge
        Assert.Equal(Math.Sqrt(2) * (18 - radius), Movement.RoomAlong(model, new Point2(19, 19), table), 6);   // to the inset corner
        Assert.Equal(Movement.FarthestOnTable(model, table).Distance, Movement.RoomAlong(model, Movement.FarthestOnTable(model, table).To, table), 6);
        Assert.Equal(0, Movement.RoomAlong(model, new Point2(18, 18), table));                                 // no direction taken
    }

    [Fact] public void Open_ground_is_a_full_circle()
    {
        var r = Movement.Region(Start, 6, [], [], null, null);
        Assert.Equal(Movement.Rays, r.Boundary.Count);
        Assert.All(r.Boundary, p => Assert.Equal(6, p.DistanceTo(r.Origin), 6));
        Assert.False(r.CappedByDifficultTerrain);
    }
    [Fact] public void An_impassable_volume_stops_the_base_short()
    {
        var r = Movement.Region(Start, 6, [Wall], [], null, null);
        Assert.InRange(r.Boundary[0].X, 2.5, 2.87);
        Assert.Equal(-6, r.Boundary[Movement.Rays / 2].X, 6);
    }
    [Fact] public void Difficult_terrain_caps_the_rays_that_enter_it()
    {
        var r = Movement.Region(Start, 12, [], [Forest], 6, null);
        Assert.Equal(6, r.Boundary[0].X, 6);
        Assert.Equal(-12, r.Boundary[Movement.Rays / 2].X, 6);
        Assert.True(r.CappedByDifficultTerrain);
    }
    [Fact] public void Without_a_cap_difficult_terrain_changes_nothing() =>
        Assert.Equal(12, Movement.Region(Start, 12, [], [Forest], null, null).Boundary[0].X, 6);
    [Fact] public void The_table_edge_stops_the_base()
    {
        var r = Movement.Region(Fx.Model(71, 24), 6, [], [], null, Opr);
        Assert.InRange(r.Boundary[0].X, 71, 71.37);
    }
    [Fact] public void A_non_multiple_allowance_reaches_its_end() =>
        Assert.Equal(7.3, Movement.Region(Start, 7.3, [], [], null, null).Boundary[0].X, 6);
    [Fact] public void Check_measures_displacement_against_the_allowance()
    {
        var ok = Movement.Check(Start, new(5, 0), 6, [], [], null, null);
        Assert.Equal(5, ok.Distance, 6);
        Assert.True(ok.WithinAllowance);
        Assert.False(Movement.Check(Start, new(7, 0), 6, [], [], null, null).WithinAllowance);
    }
    [Fact] public void Check_flags_impassable_and_the_table_edge()
    {
        Assert.True(Movement.Check(Start, new(6, 0), 6, [Wall], [], null, null).CrossesImpassable);
        Assert.False(Movement.Check(Start, new(2, 0), 6, [Wall], [], null, null).CrossesImpassable);
        Assert.True(Movement.Check(Fx.Model(71, 24), new(72, 24), 6, [], [], null, Opr).LeavesTable);
    }
    [Fact] public void Volumes_crossed_names_what_the_path_touched()
    {
        var pit = Fx.Cylinder(3, 0, 1, 0, 1, VolumeKind.Area, TerrainDeclarations.Dangerous);
        Assert.Equal(new[] { pit.Id }, Movement.VolumesCrossed(Start, new(8, 0), [pit]));   // moved through it and ended past it (p.11)
        Assert.Empty(Containment.VolumesContaining(Fx.Model(8, 0), [pit]));                 // which the landing position alone does not say
        Assert.Empty(Movement.VolumesCrossed(Start, new(0, 8), [pit]));
    }
    [Fact] public void A_start_already_touching_difficult_terrain_is_capped_from_the_first_ray()
    {
        var overlapping = Fx.Cylinder(1.5, 0, 1, 0, 8, VolumeKind.Area, TerrainDeclarations.Difficult);
        var r = Movement.Region(Start, 12, [], [overlapping], 6, null);
        Assert.True(r.CappedByDifficultTerrain);
        Assert.All(r.Boundary, p => Assert.True(p.DistanceTo(r.Origin) <= 6 + 1e-6));
        var check = Movement.Check(Start, new(-12, 0), 12, [], [overlapping], 6, null);
        Assert.True(check.EntersDifficult);
        Assert.False(check.WithinAllowance);
    }
    [Fact] public void Check_applies_the_difficult_cap()
    {
        var r = Movement.Check(Start, new(8, 0), 12, [], [Forest], 6, null);
        Assert.True(r.EntersDifficult);
        Assert.Equal(6, r.EffectiveAllowance);
        Assert.False(r.WithinAllowance);
        Assert.True(Movement.Check(Start, new(-8, 0), 12, [], [Forest], 6, null).WithinAllowance);
    }

    [Fact]
    public void The_region_goes_around_other_models()
    {
        var start = Fx.Model(10, 10);
        var open = Movement.Region(start, 6, [], [], null, null);
        var blocked = Movement.Region(start, 6, [], [], null, null, [Fx.Model(13, 10), Fx.Model(10.5, 10)]);   // the second overlaps the start: ignored
        Assert.Equal(16, open.Boundary[0].X, 6);                                          // ray 0 is +X
        Assert.True(blocked.Boundary[0].X <= 13 - 1.26 + 1e-6);                          // stops short of contact, centres 1.26 apart
        Assert.Equal(open.Boundary[45], blocked.Boundary[45]);                           // ray 45 is +Y, clear
    }
}
