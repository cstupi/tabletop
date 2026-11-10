using System.Diagnostics;
using Xunit.Abstractions;

namespace Tabletop.Geometry.Tests;

/// <summary>MoveReach's bands: what reaching a point costs, and the area split by it.</summary>
[Collection(TimingCollection.Name)]
public class MoveBandsTests(ITestOutputHelper output)
{
    static readonly PlacedModel Start = Fx.Model(0, 0);
    static readonly WorldVolume Wood = Fx.Cylinder(8, 0, 4, 0, 8, VolumeKind.Area, TerrainDeclarations.Difficult);
    static readonly WorldVolume Pit = Fx.Cylinder(8, 0, 2, 0, 1, VolumeKind.Area, TerrainDeclarations.Dangerous);
    static readonly WorldVolume Ruin = Fx.Cylinder(8, 0, 4, 0, 4, VolumeKind.Area, TerrainDeclarations.DangerousOnRushCharge);

    static MoveReach Reach(double allowance, IReadOnlyList<WorldVolume>? difficult = null, double? cap = null, IReadOnlyList<WorldVolume>? dangerous = null,
        IReadOnlyList<WorldVolume>? unstable = null, double over = 0) =>
        MoveReach.Build(Start, allowance, [], difficult ?? [], cap, null, dangerous: dangerous, unstable: unstable, unstableOver: over);

    static double Area(IReadOnlyList<Point2> t)
    {
        var sum = 0.0;
        for (var i = 0; i + 2 < t.Count; i += 3) sum += Math.Abs((t[i + 1] - t[i]).Cross(t[i + 2] - t[i])) / 2;
        return sum;
    }

    [Fact] public void Open_ground_is_all_clear()
    {
        var reach = Reach(6);
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(5, 0)));
        Assert.Null(reach.BandAt(new(6.5, 0)));
        var bands = reach.Bands();
        Assert.Empty(bands.Difficult);
        Assert.Empty(bands.Dangerous);
        Assert.Equal(Area(reach.Triangles()), Area(bands.Clear), 6);
    }

    [Fact] public void Ground_reached_only_through_difficult_terrain_within_the_cap_is_the_difficult_band()
    {
        var reach = Reach(12, [Wood], 6);
        Assert.Equal(MoveBand.Difficult, reach.BandAt(new(5, 0)));       // in the wood, under 6"
        Assert.Null(reach.BandAt(new(7, 0)));                            // in it, over 6"
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(3, 5.5)));         // beside it, within 12"
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(-11, 0)));
        Assert.NotEmpty(reach.Bands().Difficult);
    }

    [Fact] public void Difficult_terrain_out_of_reach_caps_nothing()
    {
        var far = Fx.Cylinder(40, 0, 3, 0, 8, VolumeKind.Area, TerrainDeclarations.Difficult);
        var reach = Reach(12, [far], 6);
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(10, 0)));
        Assert.True(reach.Reaches(new(10, 0)));
    }

    [Fact] public void A_unit_with_no_cap_has_no_difficult_band()
    {
        var reach = Reach(12, [Wood], null);
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(7, 0)));
        Assert.Empty(reach.Bands().Difficult);
    }

    [Fact] public void Ground_reached_only_by_entering_dangerous_terrain_is_the_dangerous_band()
    {
        var reach = Reach(12, dangerous: [Pit]);
        Assert.Equal(MoveBand.Dangerous, reach.BandAt(new(8, 0)));       // in the pit
        Assert.Equal(MoveBand.Dangerous, reach.BandAt(new(11, 0)));      // behind it: round it is over 12"
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(8, 3.5)));         // beside it
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(-11, 0)));
        Assert.NotEmpty(reach.Bands().Dangerous);
        Assert.True(reach.Reaches(new(11, 0)));                // the bands never change what is reached
    }

    [Fact] public void Ground_both_difficult_and_dangerous_is_the_dangerous_band()
    {
        var bog = Fx.Cylinder(8, 0, 4, 0, 1, VolumeKind.Area, TerrainDeclarations.Difficult | TerrainDeclarations.Dangerous);
        var reach = Reach(12, [bog], 6, [bog]);
        Assert.Equal(MoveBand.Dangerous, reach.BandAt(new(5, 0)));
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(3, 5.5)));
    }

    [Fact] public void Difficult_and_dangerous_pieces_apart_each_give_their_band()
    {
        var pit = Fx.Cylinder(0, 8, 2, 0, 1, VolumeKind.Area, TerrainDeclarations.Dangerous);
        var reach = Reach(12, [Wood], 6, [pit]);
        Assert.Equal(MoveBand.Difficult, reach.BandAt(new(5, 0)));
        Assert.Equal(MoveBand.Dangerous, reach.BandAt(new(0, 8)));
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(-5, 0)));
    }

    [Fact] public void Unstable_ground_is_dangerous_only_to_a_path_longer_than_its_threshold()
    {
        var reach = Reach(12, unstable: [Ruin], over: 6);
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(5, 0)));           // in it, a 5" path
        Assert.Equal(MoveBand.Dangerous, reach.BandAt(new(10, 0)));      // in it, a 10" path
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(3, 5.5)));
        // An earlier segment of the move used 4": what is left of the 6" is 2".
        Assert.Equal(MoveBand.Dangerous, Reach(12, unstable: [Ruin], over: 2).BandAt(new(5, 0)));
    }

    [Fact] public void Unstable_ground_on_a_move_no_longer_than_its_threshold_is_clear()
    {
        var reach = Reach(6, unstable: [Ruin], over: 6);
        Assert.Equal(MoveBand.Clear, reach.BandAt(new(5, 0)));
        Assert.Empty(reach.Bands().Dangerous);
    }

    [Fact] public void Unstable_and_difficult_ground_within_the_cap_is_difficult_not_dangerous()
    {
        // A 12" Rush capped at 6" through the wood: a path through the ruin within the cap is never over 6".
        var reach = Reach(12, [Wood], 6, unstable: [Ruin], over: 6);
        Assert.Equal(MoveBand.Difficult, reach.BandAt(new(5, 0)));
        Assert.Empty(reach.Bands().Dangerous);
    }

    [Fact] public void The_bands_cover_the_area_without_overlapping()
    {
        var pit = Fx.Cylinder(0, 8, 2, 0, 1, VolumeKind.Area, TerrainDeclarations.Dangerous);
        var reach = Reach(12, [Wood], 6, [pit]);
        var bands = reach.Bands();
        var whole = Area(reach.Triangles());
        Assert.Equal(whole, Area(bands.Clear) + Area(bands.Difficult) + Area(bands.Dangerous), whole * 0.005);
        // Each band's triangles lie in that band: their centroids, away from a boundary, read as it.
        foreach (var (band, t) in new[] { (MoveBand.Clear, bands.Clear), (MoveBand.Difficult, bands.Difficult), (MoveBand.Dangerous, bands.Dangerous) })
        {
            var checkedCount = 0;
            for (var i = 0; i + 2 < t.Count; i += 3)
            {
                if (Area([t[i], t[i + 1], t[i + 2]]) < 0.1) continue;
                var centroid = new Point2((t[i].X + t[i + 1].X + t[i + 2].X) / 3, (t[i].Y + t[i + 1].Y + t[i + 2].Y) / 3);
                Assert.Equal(band, reach.BandAt(centroid));
                checkedCount++;
            }
            Assert.True(checkedCount > 0);
        }
    }

    [Fact] public void A_minimum_cuts_the_middle_out_of_every_band()
    {
        var bands = Reach(12, dangerous: [Pit]).Bands(_ => 9);
        static bool Near(IReadOnlyList<Point2> t) => t.Any(p => p.Length < 8.9);
        Assert.False(Near(bands.Clear) || Near(bands.Dangerous));
    }

    /// <summary>The path the server measures (GfMoves.ApplyMove): Movement.Measured round the dangerous volumes and, past the threshold, the unstable ones.</summary>
    static MoveCheck ServerPath(Point2 to, double allowance, IReadOnlyList<WorldVolume> difficult, double? cap, IReadOnlyList<WorldVolume> dangerous,
        IReadOnlyList<WorldVolume> unstable, double over) => Movement.Measured(Start, to, allowance, [], difficult, cap, null, null, null, dangerous, unstable, over);

    [Fact]
    public void Measured_goes_round_unstable_ground_only_on_a_path_over_its_threshold()
    {
        // Through a small ruin to (10, 0) is 10"; round it is longer but fits 12".
        var ruin = Fx.Cylinder(5, 0, 1.5, 0, 4, VolumeKind.Area, TerrainDeclarations.DangerousOnRushCharge);
        var through = Movement.Measured(Start, new(10, 0), 12, [], [], null, null, null, null, [], [ruin], 12);
        var round = Movement.Measured(Start, new(10, 0), 12, [], [], null, null, null, null, [], [ruin], 6);
        Assert.Equal(10, through.Distance, 3);
        Assert.True(round.Distance > 10.1);
        Assert.Empty(Movement.VolumesOnPath(Start, round.Path, [ruin]));
    }

    public static TheoryData<string> Scenes => new() { "pit", "wood and pit", "wood, pit and ruin", "ruin", "bog" };

    static (double Allowance, WorldVolume[] Difficult, double? Cap, WorldVolume[] Dangerous, WorldVolume[] Unstable, double Over) Scene(string name)
    {
        var pit = Fx.Cylinder(0, 8, 2, 0, 1, VolumeKind.Area, TerrainDeclarations.Dangerous);
        var bog = Fx.Cylinder(8, 0, 3, 0, 1, VolumeKind.Area, TerrainDeclarations.Difficult | TerrainDeclarations.Dangerous);
        return name switch
        {
            "pit" => (12, [], null, [Pit], [], 0),
            "wood and pit" => (12, [Wood], 6, [pit], [], 0),
            "wood, pit and ruin" => (12, [Fx.Cylinder(8, -6, 3, 0, 8, VolumeKind.Area, TerrainDeclarations.Difficult)], 6, [pit], [Fx.Cylinder(8, 4, 2.5, 0, 4, VolumeKind.Area, TerrainDeclarations.DangerousOnRushCharge)], 6),
            "ruin" => (12, [], null, [], [Ruin], 6),
            _ => (12, [bog], 6, [bog], [], 0),
        };
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void The_route_is_the_path_the_server_measures(string name)
    {
        var (allowance, difficult, cap, dangerous, unstable, over) = Scene(name);
        var reach = Reach(allowance, difficult, cap, dangerous, unstable, over);
        var compared = 0;
        for (var x = -12.0; x <= 12; x += 1.7)
            for (var y = -12.0; y <= 12; y += 1.7)
            {
                var to = new Point2(x, y);
                if (!reach.Reaches(to)) continue;
                var server = ServerPath(to, allowance, difficult, cap, dangerous, unstable, over);
                if (!server.WithinAllowance) continue;
                var route = reach.Route(to);
                Assert.True(Math.Abs(server.Distance - route.Distance) < 0.05, $"{name} {to}: server {server.Distance:0.###}, route {route.Distance:0.###}");
                Assert.Equal(server.EntersDifficult, route.EntersDifficult);
                compared++;
            }
        Assert.True(compared > 100, $"{compared} targets");
    }

    [Fact]
    public void A_route_goes_round_a_pit_when_that_fits_and_the_bands_agree()
    {
        var reach = Reach(12, dangerous: [Pit]);
        var behind = new Point2(10.9, 1.8);                                                       // its straight line crosses the pit
        var route = reach.Route(behind);
        Assert.True(route.Distance > Start.Placement.Position.Horizontal.DistanceTo(behind) + 0.05);   // round the pit, longer than the straight line
        Assert.Empty(Movement.VolumesOnPath(Start, route.Points, [Pit]));
        Assert.Equal(MoveBand.Clear, reach.BandAt(behind));
        Assert.Equal(behind, route.Points[^1]);
    }

    [Fact]
    public void With_no_dangerous_volume_the_route_is_what_it_was()
    {
        var plain = Reach(12, [Wood], 6);
        var other = Fx.Cylinder(0, -30, 2, 0, 1, VolumeKind.Area, TerrainDeclarations.Dangerous);   // out of reach
        var listed = Reach(12, [Wood], 6, [other]);
        for (var x = -12.0; x <= 12; x += 2.3)
            for (var y = -12.0; y <= 12; y += 2.3)
                Assert.Equal(plain.Route(new(x, y)).Points, listed.Route(new(x, y)).Points);
    }

    /// <summary>The cavalry Rush under 3.6 with one of its difficult pieces dangerous instead and a lair unstable, every band drawn.</summary>
    [Fact] public void The_cavalry_leads_bands_build_within_about_one_and_a_half_areas()
    {
        var difficult = CavalryRushScene.DifficultGround.Skip(1).Concat(Movement.SteepEntering(CavalryRushScene.Riders[0], CavalryRushScene.Hills)).ToList();
        MoveReach Build() => MoveReach.Build(CavalryRushScene.Riders[0], 16, [CavalryRushScene.Building], difficult, 6, CavalryRushScene.Table,
            CavalryRushScene.Others, CavalryRushScene.Volumes, [CavalryRushScene.DifficultGround[0]], [CavalryRushScene.Lairs[1]], 6);
        Build().Bands(); Build().Triangles();                                                   // warm
        var watch = Stopwatch.StartNew();
        Build().Triangles();
        var area = watch.ElapsedMilliseconds;
        watch.Restart();
        var bands = Build().Bands();
        var split = watch.ElapsedMilliseconds;
        output.WriteLine($"area {area} ms, bands {split} ms; {bands.Clear.Count / 3}/{bands.Difficult.Count / 3}/{bands.Dangerous.Count / 3} triangles");
        Assert.NotEmpty(bands.Dangerous);
        Assert.InRange(split, 0, area * 2.5 + 200);                                            // measured back to back, so a loaded machine slows both
    }

    /// <summary>A drag frame clamps and routes each held model on the main thread. With the route's fields settled when the Ask opens (PrepareRoute,
    /// on the pool), routing round dangerous ground costs a frame about what it did round difficult ground alone.</summary>
    [Fact] public void A_drag_frames_route_round_dangerous_ground_costs_about_what_it_did()
    {
        var difficult = CavalryRushScene.DifficultGround.Skip(1).Concat(Movement.SteepEntering(CavalryRushScene.Riders[0], CavalryRushScene.Hills)).ToList();
        MoveReach Build(bool dangers) => MoveReach.Build(CavalryRushScene.Riders[0], 16, [CavalryRushScene.Building], difficult, 6, CavalryRushScene.Table,
            CavalryRushScene.Others, CavalryRushScene.Volumes, dangers ? [CavalryRushScene.DifficultGround[0]] : null, dangers ? [CavalryRushScene.Lairs[1]] : null, 6);
        var targets = new List<Point2>();
        for (var x = 16.0; x <= 46; x += 2.9) for (var y = 2.0; y <= 20; y += 2.3) targets.Add(new Point2(x, y));
        double Frame(MoveReach reach)
        {
            reach.PrepareRoute();
            foreach (var t in targets) { reach.Clamp(t); reach.Route(t); }                           // warm
            var watch = Stopwatch.StartNew();
            foreach (var t in targets) reach.Route(reach.Clamp(t));
            return watch.Elapsed.TotalMilliseconds / targets.Count;
        }
        var (plain, dangerous) = (Frame(Build(false)), Frame(Build(true)));
        var watch = Stopwatch.StartNew();
        Build(true).PrepareRoute();
        output.WriteLine($"a held model's clamp and route: {plain:0.###} ms plain, {dangerous:0.###} ms with dangerous ground; PrepareRoute with the build {watch.ElapsedMilliseconds} ms");
        Assert.InRange(dangerous, 0, Math.Max(2 * plain, 1));
    }
}
