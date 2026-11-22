namespace Tabletop.Geometry.Tests;

/// <summary>
/// A move's swept base along its path: the footprints drawn along it and the dangerous volumes any part of the base enters (3.6.0 p.12 "If any model
/// in a unit moves into or through Dangerous terrain"; owner ruling 2026-11-21 "Any part of the base counts"), by Movement.VolumesOnPath, the call the
/// server's ApplyMove tests with.
/// </summary>
public class SweptPathTests
{
    // A game's 16" Rush of five Flayer Cavalry (60 × 35 mm ovals, facing +Y) past an 8"-wide toxic smog: rider 1's centre line passes 4.72" from the
    // smog's centre, its base edge about 0.35" inside it; the server tested dangerous terrain for rider 1 alone.
    internal static readonly Base Oval = Base.Oval(2.362204724409449, 1.3779527559055118);
    internal static readonly WorldVolume Smog = Fx.Cylinder(15.955780558267506, 16.17559109636377, 4, 0, 6, VolumeKind.Area, TerrainDeclarations.Dangerous);
    internal static readonly (Point2 From, Point2 To)[] Riders =
    [
        (new(14.497011184692383, 9.693385124206543), new(28.048744327367796, 18.199296529206755)),
        (new(16.074962615966797, 9.693385124206543), new(29.9576309014231, 17.64772490045357)),
        (new(17.652915954589844, 9.693385124206543), new(31.882734934652806, 17.00859201086668)),
        (new(19.23086929321289, 9.693385124206543), new(33.763499793903854, 16.240039245426026)),
        (new(20.808820724487305, 9.693385124206543), new(34.76118264699538, 15.017517407540039)),
    ];

    static PlacedModel Rider(int i) => Fx.Model(Oval, Riders[i].From.X, Riders[i].From.Y, height: 4.43);

    [Fact]
    public void Rider_one_base_edge_clips_the_smog_while_the_others_miss_it()
    {
        for (var i = 0; i < Riders.Length; i++)
        {
            var swept = SweptPath.Of(Rider(i), [Riders[i].From, Riders[i].To], [Smog]);
            Assert.Equal(i == 0 ? [Smog.Id] : [], swept.Dangerous);
            Assert.Equal(swept.Dangerous.Count > 0, swept.Footprints.Any(f => f.Dangerous));
        }
    }

    [Fact]
    public void Footprints_stand_a_base_length_apart_from_the_start_to_the_end()
    {
        var swept = SweptPath.Of(Rider(1), [Riders[1].From, Riders[1].To], [Smog]);
        Assert.Equal(Riders[1].From, swept.Footprints[0].At);
        Assert.Equal(Riders[1].To, swept.Footprints[^1].At);
        var length = Riders[1].From.DistanceTo(Riders[1].To);
        Assert.Equal((int)Math.Ceiling(length / Oval.A) + 1, swept.Footprints.Count);
        for (var i = 1; i < swept.Footprints.Count - 1; i++)
            Assert.Equal(Oval.A, swept.Footprints[i - 1].At.DistanceTo(swept.Footprints[i].At), 6);
    }

    [Fact]
    public void A_hit_adds_a_dangerous_footprint_where_the_base_is_deepest_in_the_volume()
    {
        var swept = SweptPath.Of(Rider(0), [Riders[0].From, Riders[0].To], [Smog]);
        // The straight pass's deepest point is the foot of the perpendicular from the smog's centre to the line.
        var (a, b) = Riders[0];
        var dir = (b - a) * (1 / a.DistanceTo(b));
        var centre = new Point2(15.955780558267506, 16.17559109636377);
        var t = (centre.X - a.X) * dir.X + (centre.Y - a.Y) * dir.Y;
        Assert.Contains(swept.Footprints, f => f.Dangerous && f.At.DistanceTo(a + dir * t) < 0.05);
    }

    [Fact]
    public void A_path_with_corners_keeps_its_spacing_along_the_walked_length()
    {
        var start = Fx.Model(10, 10);
        var swept = SweptPath.Of(start, [new(10, 10), new(13, 10), new(13, 13)], []);
        Assert.Equal([new Point2(10, 10), new(11.26, 10), new(12.52, 10), new(13, 10.78), new(13, 12.04), new(13, 13)],
            swept.Footprints.Select(f => new Point2(Math.Round(f.At.X, 6), Math.Round(f.At.Y, 6))));
    }

    [Fact]
    public void Unstable_ground_is_dangerous_only_to_a_path_over_what_is_left_of_six_inches()
    {
        var unstable = Fx.Cylinder(20, 20, 2, 0, 1, VolumeKind.Area, TerrainDeclarations.DangerousOnRushCharge);
        Assert.Equal([Smog], SweptPath.DangerousTo(6, [Smog], [unstable], 6));
        Assert.Equal([Smog, unstable], SweptPath.DangerousTo(6.5, [Smog], [unstable], 6));
        Assert.Equal([Smog, unstable], SweptPath.DangerousTo(3, [Smog], [unstable], 2));
    }

    [Fact]
    public void A_hit_gives_the_stretch_of_the_walk_along_which_the_base_touches_the_volume()
    {
        var (a, b) = Riders[0];
        var swept = SweptPath.Of(Rider(0), [a, b], [Smog]);
        var stretch = Assert.Single(swept.Stretches);
        var total = a.DistanceTo(b);
        Assert.InRange(stretch.From, 0, stretch.To);
        Assert.InRange(stretch.To, stretch.From, total);
        Assert.True(stretch.To - stretch.From > 0.5, $"{stretch}");
        var dir = (b - a) * (1 / total);
        bool Touches(double s) => Movement.VolumesOnPath(Fx.Model(Oval, (a + dir * s).X, (a + dir * s).Y, height: 4.43), [a + dir * s], [Smog]).Count > 0;
        Assert.True(Touches((stretch.From + stretch.To) / 2));
        Assert.False(Touches(stretch.From - 0.01));
        Assert.False(Touches(stretch.To + 0.01));
    }

    [Fact]
    public void A_miss_has_no_stretch()
    {
        var swept = SweptPath.Of(Rider(1), [Riders[1].From, Riders[1].To], [Smog]);
        Assert.Empty(swept.Stretches);
    }
}
