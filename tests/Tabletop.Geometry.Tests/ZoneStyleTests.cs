namespace Tabletop.Geometry.Tests;

public class ZoneStyleTests
{
    static readonly TableSize Opr = new(72, 48);

    [Fact] public void Every_style_lists_its_shapes_and_resolves_them()
    {
        Assert.Equal(new[] { "frontline/1", "frontline/2" }, ZoneStyles.ShapesOf(ZoneStyles.Frontline));
        Assert.Equal(4, ZoneStyles.ShapesOf(ZoneStyles.FreeForAll).Count);
        Assert.All(ZoneStyles.All, s => Assert.All(ZoneStyles.ShapesOf(s), id => Assert.NotEmpty(ZoneStyles.Resolve(id, Opr))));
        Assert.Throws<ArgumentException>(() => ZoneStyles.ShapesOf("diagonal"));
    }
    [Fact] public void Frontline_is_twelve_inches_in_from_each_long_edge()
    {
        var zone = Assert.Single(ZoneStyles.Resolve("frontline/1", Opr));
        Assert.Equal(36, zone.Vertices.Min(v => v.Y), 6);
        Assert.Equal(48, zone.Vertices.Max(v => v.Y), 6);
        Assert.Equal(0, zone.Vertices.Min(v => v.X), 6);
        Assert.Equal(72, zone.Vertices.Max(v => v.X), 6);
        Assert.True(ZoneStyles.Contains("frontline/1", Opr, new(36, 44)));
        Assert.False(ZoneStyles.Contains("frontline/1", Opr, new(36, 24)));
        Assert.True(ZoneStyles.Contains("frontline/2", Opr, new(36, 4)));
    }
    [Fact] public void The_long_edge_follows_the_longer_dimension()
    {
        var zone = Assert.Single(ZoneStyles.Resolve("frontline/1", new TableSize(48, 72)));
        Assert.Equal(36, zone.Vertices.Min(v => v.X), 6);
        Assert.Equal(48, zone.Vertices.Max(v => v.X), 6);
    }
    [Fact] public void Ground_war_is_twenty_four_in_from_each_short_edge()
    {
        Assert.True(ZoneStyles.Contains("ground-war/1", Opr, new(6, 24)));
        Assert.False(ZoneStyles.Contains("ground-war/1", Opr, new(26, 24)));
        Assert.True(ZoneStyles.Contains("ground-war/2", Opr, new(66, 24)));
    }
    [Fact] public void Side_battle_legs_scale_from_the_diagrams_54_and_30()
    {
        var tri = Assert.Single(ZoneStyles.Resolve("side-battle/1", Opr));
        Assert.Contains(tri.Vertices, v => Math.Abs(v.X - 54) < 1e-6 && Math.Abs(v.Y) < 1e-6);
        Assert.Contains(tri.Vertices, v => Math.Abs(v.X) < 1e-6 && Math.Abs(v.Y - 30) < 1e-6);
        Assert.True(ZoneStyles.Contains("side-battle/1", Opr, new(6, 4)));
        Assert.False(ZoneStyles.Contains("side-battle/1", Opr, new(36, 24)));
        Assert.True(ZoneStyles.Contains("side-battle/2", Opr, new(66, 44)));
        // A second in-scope size is what distinguishes a fraction from a constant: at 72×48 alone, 0.75a and a fixed 54" are the same polygon.
        var other = Assert.Single(ZoneStyles.Resolve("side-battle/1", new TableSize(60, 44)));   // 3/4 and 5/8 of 60×44
        Assert.Contains(other.Vertices, v => Math.Abs(v.X - 45) < 1e-6 && Math.Abs(v.Y) < 1e-6);
        Assert.Contains(other.Vertices, v => Math.Abs(v.X) < 1e-6 && Math.Abs(v.Y - 27.5) < 1e-6);
    }
    [Fact] public void Side_battle_legs_36_and_48_under_the_3_6_0_diagram()
    {
        // 3.6.0 p.21: legs 36" up the short edge and 48" along the long edge on 72×48.
        var legs = new ZoneLegs(2.0 / 3, 0.75);
        var tri = Assert.Single(ZoneStyles.Resolve("side-battle/1", Opr, legs));
        Assert.Contains(tri.Vertices, v => Math.Abs(v.X - 48) < 1e-6 && Math.Abs(v.Y) < 1e-6);
        Assert.Contains(tri.Vertices, v => Math.Abs(v.X) < 1e-6 && Math.Abs(v.Y - 36) < 1e-6);
        var b = Assert.Single(ZoneStyles.Resolve("side-battle/2", Opr, legs));
        Assert.Contains(b.Vertices, v => Math.Abs(v.X - 24) < 1e-6 && Math.Abs(v.Y - 48) < 1e-6);
        Assert.Contains(b.Vertices, v => Math.Abs(v.X - 72) < 1e-6 && Math.Abs(v.Y - 12) < 1e-6);
        Assert.True(ZoneStyles.Contains("side-battle/1", Opr, new(2, 33), legs));
        Assert.False(ZoneStyles.Contains("side-battle/1", Opr, new(2, 33)));
        Assert.Equal(ZoneLegs.Default, new ZoneLegs(0.75, 0.625));
    }
    [Fact] public void Disordered_gives_each_team_two_corners()
    {
        Assert.Equal(2, ZoneStyles.Resolve("disordered/1", Opr).Count);
        Assert.True(ZoneStyles.Contains("disordered/1", Opr, new(6, 44)));
        Assert.True(ZoneStyles.Contains("disordered/1", Opr, new(66, 44)));
        Assert.False(ZoneStyles.Contains("disordered/1", Opr, new(36, 24)));
        Assert.False(ZoneStyles.Contains("disordered/1", Opr, new(36, 44)));
        Assert.True(ZoneStyles.Contains("disordered/2", Opr, new(6, 4)));
    }
    [Fact] public void Spearhead_apex_is_24_in_from_the_short_edge()
    {
        var tri = Assert.Single(ZoneStyles.Resolve("spearhead/1", Opr));
        Assert.Contains(tri.Vertices, v => Math.Abs(v.X - 24) < 1e-6 && Math.Abs(v.Y - 24) < 1e-6);
        Assert.True(ZoneStyles.Contains("spearhead/1", Opr, new(6, 24)));
        Assert.False(ZoneStyles.Contains("spearhead/1", Opr, new(26, 24)));
        Assert.False(ZoneStyles.Contains("spearhead/1", Opr, new(6, 44)));
    }
    [Fact] public void Opposing_forces_are_24_inch_corner_squares()
    {
        Assert.True(ZoneStyles.Contains("opposing-forces/1", Opr, new(6, 44)));
        Assert.False(ZoneStyles.Contains("opposing-forces/1", Opr, new(6, 4)));
        Assert.True(ZoneStyles.Contains("opposing-forces/2", Opr, new(66, 4)));
        Assert.False(ZoneStyles.Contains("opposing-forces/2", Opr, new(36, 4)));
    }
    [Fact] public void Free_for_all_has_a_two_by_one_foot_corner_each()
    {
        var corner = Assert.Single(ZoneStyles.Resolve("free-for-all/3", Opr));
        Assert.Equal(48, corner.Vertices.Min(v => v.X), 6);
        Assert.Equal(72, corner.Vertices.Max(v => v.X), 6);
        Assert.Equal(0, corner.Vertices.Min(v => v.Y), 6);
        Assert.Equal(12, corner.Vertices.Max(v => v.Y), 6);
    }
    [Fact] public void Fully_within_needs_the_whole_base()
    {
        Assert.False(ZoneStyles.BaseFullyWithin("frontline/1", Opr, Fx.Model(36, 36.5)));
        Assert.True(ZoneStyles.BaseFullyWithin("frontline/1", Opr, Fx.Model(36, 37)));
        Assert.True(ZoneStyles.BaseFullyWithin("disordered/1", Opr, Fx.Model(66, 46)));
    }
    [Fact] public void Fully_within_a_distance_of_the_zone_measures_the_far_edge_of_the_base()
    {
        // frontline/1 starts at y = 36: a 1" base centred at y = 24.5 has its far rim 12" from the zone.
        Assert.True(ZoneStyles.BaseFullyWithin("frontline/1", Opr, Fx.Model(36, 24.5, diameter: 1), 12));
        Assert.False(ZoneStyles.BaseFullyWithin("frontline/1", Opr, Fx.Model(36, 24.4, diameter: 1), 12));
        Assert.True(ZoneStyles.BaseFullyWithin("frontline/1", Opr, Fx.Model(36, 40, diameter: 1), 12));         // inside the zone itself
        Assert.True(ZoneStyles.BaseFullyWithin("frontline/1", Opr, Fx.Model(Base.Rect(2, 2), 36, 25), 12));    // a polygon base: its far corner at y = 24
        Assert.False(ZoneStyles.BaseFullyWithin("frontline/1", Opr, Fx.Model(Base.Rect(2, 2), 36, 24.9), 12));
    }
    [Fact] public void Malformed_ids_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ZoneStyles.Resolve("frontline", Opr));
        Assert.Throws<ArgumentException>(() => ZoneStyles.Resolve("frontline/3", Opr));
    }
}
