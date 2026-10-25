namespace Tabletop.Geometry.Tests;

/// <summary>
/// Moves measured along the shortest path the base can take (rulebook p.5: "always measure so that no part of its base moves further than the
/// total distance"; p.9: "charge moves don't have to be in a straight line"), not the straight line from start to destination.
/// </summary>
public class MovementPathTests
{
    static readonly TableSize Opr = new(72, 48);
    // A 1.26" base (radius 0.63) and a 2" x 2" wood at x 15..17, y 9..11.
    static readonly WorldVolume Wood = Fx.Box(16, 10, 2, 2, 0, 3, VolumeKind.Area, TerrainDeclarations.Difficult);
    static readonly PlacedModel Start = Fx.Model(10, 10);

    [Fact]
    public void A_rush_around_a_wood_is_not_capped_when_the_way_round_fits_the_allowance()
    {
        var check = Movement.Check(Start, new(21, 10), 12, [], [Wood], 6, Opr);
        const double round = 11.588;                                                       // the exact way round the wood's rounded corners
        Assert.False(check.EntersDifficult);
        Assert.True(check.WithinAllowance);
        Assert.Equal(12, check.EffectiveAllowance);
        Assert.InRange(check.Distance, round - 1e-3, round + 0.05);
        Assert.True(check.Path.Count > 2);                                                 // it bends
        Assert.Equal(new Point2(10, 10), check.Path[0]);
        Assert.Equal(new Point2(21, 10), check.Path[^1]);
    }

    [Fact]
    public void A_destination_only_reached_through_the_wood_is_held_to_the_cap()
    {
        var through = Movement.Check(Start, new(16, 10), 12, [], [Wood], 6, Opr);       // in the wood
        Assert.True(through.EntersDifficult);
        Assert.Equal(6, through.EffectiveAllowance);
        Assert.True(through.WithinAllowance);
        var far = Movement.Check(Start, new(22.5, 10), 12, [], [Wood], 6, Opr);          // the way round is over 12", the way through over 6"
        Assert.False(far.WithinAllowance);
    }

    [Fact]
    public void A_path_goes_round_an_impassable_block_to_reach_behind_it()
    {
        var block = Fx.Box(14, 10, 2, 4, 0, 3, d: TerrainDeclarations.Impassable);       // x 13..15, y 8..12
        var behind = new Point2(17, 10);
        var check = Movement.Check(Start, behind, 12, [block], [], null, Opr);
        const double round = 9.4249;                                                       // the exact way round
        Assert.False(check.CrossesImpassable);
        Assert.True(check.WithinAllowance);
        Assert.InRange(check.Distance, round - 1e-3, round + 0.05);
        Assert.True(Movement.Check(Start, behind, round - 0.2, [block], [], null, Opr) is { WithinAllowance: false, CrossesImpassable: true });
    }

    [Fact]
    public void A_destination_walled_in_is_unreachable()
    {
        WorldVolume[] ring = [Fx.Box(20, 14, 10, 1, 0, 3, d: TerrainDeclarations.Impassable), Fx.Box(20, 6, 10, 1, 0, 3, d: TerrainDeclarations.Impassable),
            Fx.Box(15, 10, 1, 9, 0, 3, d: TerrainDeclarations.Impassable), Fx.Box(25, 10, 1, 9, 0, 3, d: TerrainDeclarations.Impassable)];
        var check = Movement.Check(Start, new(20, 10), 40, ring, [], null, Opr);
        Assert.True(check.CrossesImpassable);
        Assert.False(check.WithinAllowance);
    }

    [Fact]
    public void A_base_standing_in_impassable_terrain_may_head_out_but_never_along_or_deeper_in()
    {
        var wall = Fx.Box(4, 10, 1, 10, 0, 4, d: TerrainDeclarations.Impassable);       // x 3.5..4.5, y 5..15
        var inside = Fx.Model(4, 10);
        Assert.False(Movement.Check(inside, new(6, 10), 12, [wall], [], null, null).CrossesImpassable);     // straight out, east
        Assert.False(Movement.Check(inside, new(2, 11), 12, [wall], [], null, null).CrossesImpassable);     // out, west and a little north
        Assert.True(Movement.Check(inside, new(4, 13), 12, [wall], [], null, null).CrossesImpassable);      // along it
        Assert.True(Movement.Check(inside, new(4, 10), 12, [wall], [], null, null).CrossesImpassable);      // no direction taken: still in it
        var touching = Fx.Model(3.2, 10);
        Assert.False(Movement.Check(touching, new(0, 10), 12, [wall], [], null, null).CrossesImpassable);   // back off it
        var round = Movement.Check(touching, new(6, 10), 20, [wall], [], null, null);                       // out the way it came, then round the end
        Assert.False(round.CrossesImpassable);
        Assert.True(round.Distance > 10);
    }

    [Fact]
    public void A_path_goes_round_other_models_and_says_when_only_a_path_through_them_fits()
    {
        PlacedModel[] one = [Fx.Model(13, 10)];
        var check = Movement.Check(Start, new(16, 10), 12, [], [], null, Opr, one);
        Assert.False(check.CrossesModels);
        Assert.True(check.WithinAllowance);
        Assert.True(check.Distance > 6);
        var line = Enumerable.Range(0, 38).Select(i => Fx.Model(13, 0.63 + i * 1.26)).ToList();           // a solid line of bases across the table
        var blocked = Movement.Check(Start, new(16, 10), 12, [], [], null, Opr, line);
        Assert.True(blocked.CrossesModels);
        Assert.Equal(6, blocked.Distance, 6);
        Assert.False(Movement.Check(Start, new(16, 10), 12, [], [], null, Opr, [Fx.Model(10.5, 10)]).CrossesModels);   // one it overlaps at the start: left out
    }

    [Fact]
    public void A_path_passes_above_a_lower_model()
    {
        var roof = Fx.Box(10, 10, 4, 20, 0, 4);                                           // x 8..12, y 0..20, top 4
        var start = Fx.Model(11, 3, z: 4, diameter: 3);
        PlacedModel[] below = [Fx.Model(12.8, 10)];
        var check = Movement.Check(start, new(11, 13), 10, [], [], null, null, below, [roof]);
        Assert.False(check.CrossesModels);
        Assert.Equal(10, check.Distance, 6);                                              // straight, over it
        Assert.True(Movement.Check(start, new(11, 13), 10, [], [], null, null, below).CrossesModels);         // rested on nothing, it meets the model, and round it is over 10"
        Assert.False(Movement.Check(start, new(11, 13), 11, [], [], null, null, below).CrossesModels);
    }

    [Fact]
    public void A_path_avoids_dangerous_terrain_when_the_way_round_fits()
    {
        var pit = Fx.Cylinder(13, 10, 1, 0, 1, VolumeKind.Area, TerrainDeclarations.Dangerous);
        var check = Movement.Check(Start, new(16, 10), 12, [], [], null, Opr, dangerous: [pit]);
        Assert.Empty(Movement.VolumesOnPath(Start, check.Path, [pit]));
        Assert.Equal([pit.Id], Movement.VolumesOnPath(Start, Movement.Check(Start, new(16, 10), 6.05, [], [], null, Opr, dangerous: [pit]).Path, [pit]));
    }

    [Fact]
    public void The_table_edge_holds_the_path_and_a_landing_off_it_leaves_the_table()
    {
        Assert.True(Movement.Check(Fx.Model(71, 24), new(72, 24), 6, [], [], null, Opr).LeavesTable);
        var hugging = Fx.Box(36, 47, 2, 6, 0, 3, d: TerrainDeclarations.Impassable);      // y 44..50: no way round its north end on the table
        var check = Movement.Check(Fx.Model(33, 46), new(39, 46), 20, [hugging], [], null, Opr);
        Assert.True(check.Path.All(p => p.Y <= 48 - 0.63 + 1e-6));
        Assert.True(check.Distance > 6);
    }

    [Fact]
    public void The_region_reaches_behind_a_wall_round_its_end()
    {
        var wall = Fx.Box(13, 10, 1, 6, 0, 3, d: TerrainDeclarations.Impassable);          // x 12.5..13.5, y 7..13
        var region = Movement.Region(Start, 11, [wall], [], null, Opr);
        Assert.True(Inside(region, new(15, 10)));                                          // behind it, round an end: about 10.1"
        Assert.False(Inside(region, new(13, 10)));                                         // in it
        Assert.False(Inside(region, new(18.5, 10)));                                       // straight on 8.5", but round the end 12"
        Assert.True(Inside(region, new(10, 1.5)));                                         // open ground
        Assert.False(Inside(region, new(10, 0.5)));                                        // the edge cuts the base off
    }

    [Fact]
    public void The_region_takes_the_way_round_a_wood_at_the_full_allowance()
    {
        var region = Movement.Region(Start, 12, [], [Wood], 6, Opr);
        Assert.True(Inside(region, new(21, 10)));                                          // round the wood, about 11.6"
        Assert.True(Inside(region, new(15.8, 10)));                                        // in it, 5.8"
        Assert.False(Inside(region, new(22.5, 10)));
        Assert.True(region.CappedByDifficultTerrain);
    }

    [Fact]
    public void Open_ground_is_a_disc_of_the_allowance()
    {
        var region = Movement.Region(Fx.Model(30, 24), 6, [], [], null, Opr);
        Assert.True(Inside(region, new(35.9, 24)));
        Assert.False(Inside(region, new(36.2, 24)));
        Assert.InRange(Area(region), Math.PI * 36 * 0.98, Math.PI * 36 * 1.01);
    }

    [Fact]
    public void A_minimum_cuts_the_middle_out()
    {
        var region = Movement.Region(Fx.Model(36, 24), 36, [], [], null, Opr, least: _ => 30);
        Assert.False(Inside(region, new(40, 24)));
        Assert.True(Inside(region, new(36 + 32, 24)));
    }

    [Fact]
    public void A_reach_clamps_a_target_to_the_nearest_point_it_can_get_to()
    {
        var wall = Fx.Box(13, 10, 1, 6, 0, 3, d: TerrainDeclarations.Impassable);
        var reach = MoveReach.Build(Start, 11, [wall], [], null, Opr);
        Assert.Equal(new Point2(15, 10), reach.Clamp(new(15, 10)));                        // reachable: untouched
        var held = reach.Clamp(new(25, 10));
        Assert.True(reach.Route(held).Distance <= 11 + 1e-3);
        Assert.True(held.X > 13.5);                                                        // round the wall, not stopped at it
        var capped = reach.Clamp(new(10, 30), cap: 4);
        Assert.Equal(4, capped.DistanceTo(new(10, 10)), 3);
    }

    [Fact]
    public void A_reach_routes_as_check_does()
    {
        var reach = MoveReach.Build(Start, 12, [], [Wood], 6, Opr);
        var route = reach.Route(new(21, 10));
        var check = Movement.Check(Start, new(21, 10), 12, [], [Wood], 6, Opr);
        Assert.Equal(check.Distance, route.Distance, 2);
        Assert.False(route.EntersDifficult);
        Assert.True(reach.Route(new(16, 10)).EntersDifficult);
    }

    [Fact]
    public void Contact_is_reached_round_impassable_terrain()
    {
        var block = Fx.Box(14, 10, 2, 4, 0, 3, d: TerrainDeclarations.Impassable);        // x 13..15, y 8..12
        var enemy = Fx.Model(17, 10);
        var d = Movement.ContactDistance(Start, enemy, 12, [block], Opr);
        Assert.NotNull(d);
        Assert.True(d > 17 - 10 - 1.26);
        Assert.Null(Movement.ContactDistance(Start, enemy, 5.5, [block], Opr));
        Assert.Equal(17 - 10 - 1.26, Movement.ContactDistance(Start, enemy, 12, [], Opr)!.Value, 6);
    }

    [Fact]
    public void Contact_points_stand_the_gap_clear_of_the_target_nearest_side_first()
    {
        var enemy = Fx.Model(17, 10);
        var points = Movement.ContactPoints(Start, enemy, 0.05, 8);
        Assert.Equal(8, points.Count);
        Assert.Equal(17 - 1.26 - 0.05, points[0].X, 6);                                     // facing the start
        Assert.Equal(10, points[0].Y, 6);
        Assert.All(points, p => Assert.Equal(0.05, Distance.EdgeToEdge(Fx.Model(p.X, p.Y), enemy), 6));
        Assert.Equal(17 + 1.26 + 0.05, points[^1].X, 6);                                    // the far side last
    }

    static bool Inside(MovementRegion region, Point2 p)
    {
        var t = region.Triangles;
        for (var i = 0; i + 2 < t.Count; i += 3)
        {
            double d1 = (t[i + 1] - t[i]).Cross(p - t[i]), d2 = (t[i + 2] - t[i + 1]).Cross(p - t[i + 1]), d3 = (t[i] - t[i + 2]).Cross(p - t[i + 2]);
            if (Math.Abs(d1) + Math.Abs(d2) + Math.Abs(d3) > 0 && ((d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0))) return true;
        }
        return false;
    }

    static double Area(MovementRegion region)
    {
        var t = region.Triangles; var area = 0.0;
        for (var i = 0; i + 2 < t.Count; i += 3) area += Math.Abs((t[i + 1] - t[i]).Cross(t[i + 2] - t[i])) / 2;
        return area;
    }
}
