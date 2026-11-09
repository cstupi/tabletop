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

    [Fact] public void A_region_from_inside_an_impassable_volume_opens_only_the_ways_out_of_it()
    {
        var r = Movement.Region(Fx.Model(4, 0), 6, [Wall], [], null, null);
        Assert.True(Inside(r, new(9.5, 0)));                                                 // east, out and on
        Assert.True(Inside(r, new(-1.5, 0)));                                                // west, out and on
        Assert.False(Inside(r, new(4, 3)));                                                  // north, along the wall
    }

    [Fact] public void Open_ground_is_a_full_disc()
    {
        var r = Movement.Region(Start, 6, [], [], null, null);
        Assert.True(Inside(r, new(5.9, 0)) && Inside(r, new(0, -5.9)) && Inside(r, new(-4.2, 4.2)));
        Assert.False(Inside(r, new(6.1, 0)));
        Assert.False(r.CappedByDifficultTerrain);
    }

    [Fact] public void An_impassable_volume_is_gone_round()
    {
        var r = Movement.Region(Start, 6, [Wall], [], null, null);
        Assert.True(Inside(r, new(2.8, 0)));
        Assert.False(Inside(r, new(3, 0)));                                                  // the base would touch it
        Assert.False(Inside(r, new(5.5, 0)));                                                // round its end is far over 6"
        Assert.True(Inside(r, new(-5.9, 0)));
    }

    [Fact] public void Difficult_terrain_caps_what_is_only_reached_through_it()
    {
        var wood = Fx.Cylinder(8, 0, 4, 0, 8, VolumeKind.Area, TerrainDeclarations.Difficult);
        var r = Movement.Region(Start, 12, [], [wood], 6, null);
        Assert.True(Inside(r, new(5.9, 0)));                                                 // in the wood, under 6"
        Assert.False(Inside(r, new(7, 0)));                                                  // in it, over 6"
        Assert.True(Inside(r, new(-11.9, 0)));
        Assert.True(Inside(r, new(3, 5.5)));                                                 // beside it, within 12"
        Assert.True(Inside(Movement.Region(Start, 12, [], [Forest], 6, null), new(6.2, 0)));  // round a small one, within 12"
        Assert.True(r.CappedByDifficultTerrain);
    }

    [Fact] public void Without_a_cap_difficult_terrain_changes_nothing() =>
        Assert.True(Inside(Movement.Region(Start, 12, [], [Forest], null, null), new(11.9, 0)));

    [Fact] public void The_table_edge_stops_the_base()
    {
        var r = Movement.Region(Fx.Model(71, 24), 6, [], [], null, Opr);
        Assert.True(Inside(r, new(71.3, 24)));
        Assert.False(Inside(r, new(71.5, 24)));
    }

    [Fact] public void Check_measures_the_path_against_the_allowance()
    {
        var ok = Movement.Check(Start, new(5, 0), 6, [], [], null, null);
        Assert.Equal(5, ok.Distance, 6);
        Assert.True(ok.WithinAllowance);
        Assert.Equal([new(0, 0), new(5, 0)], ok.Path);
        Assert.False(Movement.Check(Start, new(7, 0), 6, [], [], null, null).WithinAllowance);
    }

    [Fact] public void Check_flags_impassable_and_the_table_edge()
    {
        Assert.True(Movement.Check(Start, new(6, 0), 6, [Wall], [], null, null).CrossesImpassable);   // through it, and round it is over 6"
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

    [Fact] public void A_start_already_touching_difficult_terrain_is_capped_everywhere()
    {
        var overlapping = Fx.Cylinder(1.5, 0, 1, 0, 8, VolumeKind.Area, TerrainDeclarations.Difficult);
        var r = Movement.Region(Start, 12, [], [overlapping], 6, null);
        Assert.True(r.CappedByDifficultTerrain);
        Assert.All(r.Triangles, p => Assert.True(p.DistanceTo(r.Origin) <= 6 + 0.05));
        var check = Movement.Check(Start, new(-12, 0), 12, [], [overlapping], 6, null);
        Assert.True(check.EntersDifficult);
        Assert.False(check.WithinAllowance);
    }

    [Fact] public void A_steep_volume_is_difficult_when_entered_and_open_when_left()
    {
        // 3.6.0 p.12: "Units moving onto steep terrain count it as being in Difficult terrain, but units moving off it count it as open terrain."
        var hill = Fx.Cylinder(8, 0, 4, 0, 3, VolumeKind.Area, TerrainDeclarations.Steep);
        var onto = Movement.Check(Start, new(8, 0), 12, [], [], 6, null, steep: [hill]);
        Assert.True(onto.EntersDifficult);
        Assert.False(onto.WithinAllowance);                                                  // 8" onto it, over the 6" cap
        var off = Movement.Check(Fx.Model(8, 0), new(18, 0), 12, [], [], 6, null, steep: [hill]);
        Assert.False(off.EntersDifficult);
        Assert.True(off.WithinAllowance);                                                    // 10" off it, open
        Assert.Equal([hill.Id], Movement.SteepEntering(Start, [hill]).Select(v => v.Id));
        Assert.Empty(Movement.SteepEntering(Fx.Model(8, 0), [hill]));
    }

    [Fact] public void A_model_standing_on_a_steep_hill_by_its_base_edge_moves_off_it_in_the_open()
    {
        // A 32 mm base resting on the top of a 3"-radius hill with its centre 3.47" out (it stands on the hill, Collision.Surfaces): moving off is
        // open ground (3.6.0 p.12), a 10" move down the slope past the hill's edge is within 12", not held to 6".
        var hill = Fx.Cylinder(28.74, 15.62, 3, 0, 3, VolumeKind.Solid, TerrainDeclarations.Steep);
        var rim = Fx.Model(25.28, 15.88, 3);
        Assert.Empty(Movement.SteepEntering(rim, [hill]));
        var down = Movement.Check(rim, new(30, 24.5), 12, [], [], 6, null, surfaces: [hill], steep: [hill]);
        Assert.False(down.EntersDifficult);
        Assert.True(down.WithinAllowance);
        // A base touching the hill's edge from the ground does not stand on it: crossing it is moving onto it.
        var foot = Fx.Model(28.74 - 3 - 0.63, 15.62);
        Assert.Equal([hill.Id], Movement.SteepEntering(foot, [hill]).Select(v => v.Id));
    }

    [Fact] public void Check_applies_the_difficult_cap()
    {
        var r = Movement.Check(Start, new(4, 0), 12, [], [Forest], 6, null);                // in the forest
        Assert.True(r.EntersDifficult);
        Assert.Equal(6, r.EffectiveAllowance);
        Assert.True(r.WithinAllowance);
        Assert.False(Movement.Check(Start, new(6.5, 0), 6.6, [], [Forest], 6, null).WithinAllowance);   // round it over 6.6", through it over 6"
        Assert.True(Movement.Check(Start, new(-8, 0), 12, [], [Forest], 6, null).WithinAllowance);
    }

    [Fact]
    public void The_region_goes_around_other_models()
    {
        var start = Fx.Model(10, 10);
        var blocked = Movement.Region(start, 6, [], [], null, null, [Fx.Model(13, 10), Fx.Model(10.5, 10)]);   // the second overlaps the start: ignored
        Assert.False(Inside(blocked, new(13, 10)));
        Assert.True(Inside(blocked, new(15, 10)));                                         // behind it, round it: about 5.7"
        Assert.True(Inside(blocked, new(10, 15.9)));
    }

    [Fact]
    public void The_region_passes_above_a_lower_model()
    {
        var roof = Fx.Box(10, 10, 4, 20, 0, 4);                                           // x 8..12, y 0..20, top 4
        var start = Fx.Model(11, 3, z: 4, diameter: 3);                                   // on the roof, its edge out to x 12.5
        PlacedModel[] below = [Fx.Model(12.8, 10)];                                       // on the ground at the roof's foot
        Assert.True(Inside(Movement.Region(start, 10, [], [], null, null, below, [roof]), new(11, 10)));
        Assert.False(Inside(Movement.Region(start, 10, [], [], null, null, below), new(11, 10)));    // rested on nothing, it meets the model
    }

    internal static bool Inside(MovementRegion region, Point2 p)
    {
        var t = region.Triangles;
        for (var i = 0; i + 2 < t.Count; i += 3)
        {
            double d1 = (t[i + 1] - t[i]).Cross(p - t[i]), d2 = (t[i + 2] - t[i + 1]).Cross(p - t[i + 1]), d3 = (t[i] - t[i + 2]).Cross(p - t[i + 2]);
            if (Math.Abs(d1) + Math.Abs(d2) + Math.Abs(d3) > 0 && ((d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0))) return true;
        }
        return false;
    }
}
