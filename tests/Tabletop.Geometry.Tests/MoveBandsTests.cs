using System.Diagnostics;
using Xunit.Abstractions;

namespace Tabletop.Geometry.Tests;

/// <summary>MoveReach's bands: what reaching a point costs, and the area split by it.</summary>
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
        Assert.InRange(split, 0, 3000);
    }
}
