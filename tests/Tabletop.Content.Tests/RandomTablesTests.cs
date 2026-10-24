using Tabletop.Geometry;

namespace Tabletop.Content.Tests;

public class RandomTablesTests
{
    static readonly TableSize Opr = new(72, 48);
    static readonly Planet[] Planets = Enum.GetValues<Planet>();
    static readonly string[] TwoTeamStyles = ZoneStyles.All.Where(s => s != ZoneStyles.FreeForAll).ToArray();

    static IReadOnlyList<DeploymentZone> Zones(string style, int count = 0) => TableSidesTests.Zones(style, count);

    static LaidTable Lay(Planet planet, int seed, IReadOnlyList<DeploymentZone>? zones = null, TableSize? size = null, IReadOnlyDictionary<string, BuiltinTerrain>? catalog = null) =>
        RandomTables.Lay(planet, size ?? Opr, zones ?? Zones("frontline"), seed, catalog ?? RepoCatalog.Terrain);

    static IEnumerable<(Planet, int)> Cases(int seeds = 30) => Planets.SelectMany(p => Enumerable.Range(0, seeds).Select(s => (p, s)));

    static int Signature(TerrainPlacement p)
    {
        var d = p.Volumes.Aggregate(TerrainDeclarations.None, (a, v) => a | v.Volume.Declarations);
        return (d.HasFlag(TerrainDeclarations.BlocksLineOfSight) ? 1 : 0) | (d.HasFlag(TerrainDeclarations.Cover) ? 2 : 0) | (d.HasFlag(TerrainDeclarations.Difficult) ? 4 : 0);
    }

    [Fact]
    public void Every_piece_lies_on_the_table_clear_of_the_others()
    {
        foreach (var (planet, seed) in Cases())
        {
            var t = Lay(planet, seed);
            var volumes = t.Pieces.Select(p => p.Placement.WorldVolumes()).ToList();
            Assert.All(volumes.SelectMany(v => v), v => Assert.True(Table.ContainsVolume(Opr, v), $"{planet} {seed}"));
            for (var i = 0; i < volumes.Count; i++)
                for (var j = i + 1; j < volumes.Count; j++)
                    foreach (var a in volumes[i]) foreach (var b in volumes[j])
                        Assert.True(Distance.Between(a, b) >= 0.5 - 1e-6, $"{planet} {seed}: {i} and {j}");
            Assert.All(t.Pieces, p => { Assert.Equal(0, p.Placement.Position.Z); Assert.Equal(1, p.Placement.Scale); });
        }
    }

    [Theory, MemberData(nameof(TwoTeam))]
    public void Two_teams_mirror_about_the_centre(string style)
    {
        foreach (var (planet, seed) in Cases(10))
        {
            var t = Lay(planet, seed, Zones(style));
            var team2 = t.Pieces.Where(p => p.Team == 2).ToList();
            foreach (var p in t.Pieces.Where(p => p.Team == 1))
            {
                var partners = team2.Where(q => Signature(q.Placement) == Signature(p.Placement)
                    && Math.Abs(q.Placement.Position.X - (72 - p.Placement.Position.X)) < 1e-6 && Math.Abs(q.Placement.Position.Y - (48 - p.Placement.Position.Y)) < 1e-6
                    && Math.Abs((q.Placement.Yaw.Degrees - p.Placement.Yaw.Degrees - 180 + 720) % 360) < 1e-6).ToList();
                Assert.Single(partners);
            }
            Assert.Equal(t.Pieces.Count(p => p.Team == 1), team2.Count);
        }
    }

    [Theory, MemberData(nameof(TwoTeam))]
    public void Two_teams_share_each_category_equally(string style)
    {
        var zones = Zones(style);
        foreach (var (planet, seed) in Cases(10))
        {
            var s = TableSides.Shares(Opr, zones, Lay(planet, seed, zones).Pieces.Select(p => p.Placement).ToList());
            Assert.Equal(s[1].SightBlockers, s[2].SightBlockers, 1e-9);
            Assert.Equal(s[1].Cover, s[2].Cover, 1e-9);
            Assert.Equal(s[1].Difficult, s[2].Difficult, 1e-9);
        }
    }

    public static TheoryData<string> TwoTeam() => new(TwoTeamStyles);

    [Fact]
    public void Each_dealt_piece_stands_on_its_teams_side()
    {
        var anchors = TableSides.Anchors(Opr, Zones("frontline"));
        foreach (var (planet, seed) in Cases())
            Assert.All(Lay(planet, seed).Pieces.Where(p => p.Team is not null), p => Assert.Equal(p.Team, TableSides.SideOf(anchors, p.Placement.Position.Horizontal)));
    }

    [Fact]
    public void Neutral_pieces_sit_on_the_dividing_line()
    {
        var anchors = TableSides.Anchors(Opr, Zones("frontline"));
        var neutrals = 0;
        foreach (var (planet, seed) in Cases())
            foreach (var p in Lay(planet, seed).Pieces.Where(p => p.Team is null))
            {
                neutrals++;
                Assert.Null(TableSides.SideOf(anchors, p.Placement.Position.Horizontal));
            }
        Assert.True(neutrals > 0);
    }

    [Theory] [InlineData(3)] [InlineData(4)]
    public void Three_and_four_teams_get_equal_counts_per_signature(int teams)
    {
        var zones = Zones("free-for-all", teams);
        var anchors = TableSides.Anchors(Opr, zones);
        foreach (var (planet, seed) in Cases(10))
        {
            var t = Lay(planet, seed, zones);
            foreach (var sig in Enumerable.Range(0, 8))
            {
                var counts = anchors.Keys.Select(k => t.Pieces.Count(p => p.Team == k && Signature(p.Placement) == sig)).Distinct().ToList();
                Assert.Single(counts);
            }
            Assert.All(t.Pieces.Where(p => p.Team is null), p => Assert.True(p.Placement.Position.Horizontal.DistanceTo(new Point2(36, 24)) <= 12 + 1e-6));
            Assert.All(t.Pieces.Where(p => p.Team is not null), p => Assert.Equal(p.Team, TableSides.SideOf(anchors, p.Placement.Position.Horizontal)));
        }
    }

    [Fact]
    public void Swapped_zones_swap_the_sides()
    {
        var swapped = Zones("frontline").Select(z => z with { TeamOrdinal = 3 - z.TeamOrdinal }).ToList();
        foreach (var (planet, seed) in Cases(10))
            Assert.All(Lay(planet, seed, swapped).Pieces.Where(p => p.Team == 1), p => Assert.True(p.Placement.Position.Y < 24));
    }

    [Fact]
    public void No_zones_is_frontline()
    {
        foreach (var (planet, seed) in Cases(5))
            Assert.Equal(ContentJson.Serialize(Lay(planet, seed, Zones("frontline"))), ContentJson.Serialize(Lay(planet, seed, [])));
    }

    [Fact]
    public void A_crowded_table_withdraws_whole_pairs()
    {
        var small = new TableSize(24, 24);
        var zones = Zones("frontline");
        var any = false;
        for (var seed = 0; seed < 20; seed++)
        {
            var t = Lay(Planet.Wasteland, seed, zones, small);
            any |= t.Unplaced.Count > 0;
            var s = TableSides.Shares(small, zones, t.Pieces.Select(p => p.Placement).ToList());
            Assert.Equal(s[1].SightBlockers, s[2].SightBlockers, 1e-9);
            Assert.Equal(s[1].Cover, s[2].Cover, 1e-9);
            Assert.Equal(s[1].Difficult, s[2].Difficult, 1e-9);
            Assert.Equal(PlanetTerrain.Roll(Planet.Wasteland, new Random(seed)).Count, t.Pieces.Count + t.Unplaced.Count + t.Unlisted.Count);
        }
        Assert.True(any);
    }

    [Fact]
    public void A_piece_the_catalog_does_not_list_is_unlisted()
    {
        var hills = RepoCatalog.Terrain.Where(kv => kv.Key == "hill").ToDictionary(kv => kv.Key, kv => kv.Value);
        var t = Lay(Planet.Hospitable, 1, catalog: hills);
        Assert.NotEmpty(t.Pieces);
        Assert.All(t.Pieces, p => Assert.Equal("hill", p.Placement.Mesh.Builtin));
        Assert.NotEmpty(t.Unlisted);
        Assert.DoesNotContain("hill", t.Unlisted);
    }

    [Fact]
    public void The_seed_pins_the_table()
    {
        Assert.Equal(ContentJson.Serialize(Lay(Planet.Ruins, 7)), ContentJson.Serialize(Lay(Planet.Ruins, 7)));
        Assert.NotEqual(ContentJson.Serialize(Lay(Planet.Ruins, 7)), ContentJson.Serialize(Lay(Planet.Ruins, 8)));
        var t = Lay(Planet.Hospitable, 1);
        var first = t.Pieces.Take(3).Select(p => $"{p.Placement.Mesh.Builtin} {p.Placement.Position.X:0.00} {p.Placement.Position.Y:0.00} {p.Placement.Yaw.Degrees:0.00}").ToList();
        Assert.Equal(16, t.Pieces.Count);
        Assert.Equal(["large-building 12.20 38.10 105.00", "large-building 59.80 9.90 285.00", "small-buildings 34.29 14.42 30.00"], first);
    }

    [Fact]
    public void Ids_are_distinct_and_seeded()
    {
        foreach (var (planet, seed) in Cases(10))
        {
            var t = Lay(planet, seed);
            var ids = t.Pieces.SelectMany(p => p.Placement.Volumes.Select(v => v.Id).Append(p.Placement.Id)).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.Equal('4', id.ToString()[14]));
            Assert.Equal(ids, Lay(planet, seed).Pieces.SelectMany(p => p.Placement.Volumes.Select(v => v.Id).Append(p.Placement.Id)).ToList());
        }
    }
}
