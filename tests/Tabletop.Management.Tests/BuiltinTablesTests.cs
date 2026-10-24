using System.Net;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Layouts;

namespace Tabletop.Management.Tests;

/// <summary>What a predefined table must satisfy (spec 2026-10-24-browser-play-design.md → Predefined tables; Grimdark Future p.6 and p.12).</summary>
internal static class BuiltinTableChecks
{
    public static readonly (double X, double Y)[] Objectives = [(18, 18), (54, 30), (54, 18), (18, 30)];

    /// <summary>p.12 asks a third difficult. The Wasteland roll tables carry almost no difficult terrain (nine fixed pieces with none, two
    /// rolled entries with any), and no seed of the first thousands reaches 20 % there, so its floor is 15 %, the most the roll gives.</summary>
    public static int DifficultPercent(Planet planet) => planet == Planet.Wasteland ? 15 : 33;

    /// <summary>Null when the table passes, else the first fault.</summary>
    public static string? Problem(TableState t, int difficultPercent = 33)
    {
        var n = t.Terrain.Count;
        if (n < 15) return $"{n} pieces, under 15";
        var declared = t.Terrain.Select(p => p.Volumes.Aggregate(TerrainDeclarations.None, (d, v) => d | v.Volume.Declarations)).ToList();
        if (declared.Count(d => d.HasFlag(TerrainDeclarations.BlocksLineOfSight)) * 2 < n) return "under half block line of sight";
        if (declared.Count(d => d.HasFlag(TerrainDeclarations.Cover)) * 3 < n) return "under a third give cover";
        if (declared.Count(d => d.HasFlag(TerrainDeclarations.Difficult)) * 100 < n * difficultPercent) return $"under {difficultPercent}% are difficult";
        var shares = TableSides.Shares(t.Size, t.Zones, t.Terrain);
        if (shares[1] != shares[2]) return "the teams' shares differ";
        var solid = t.Terrain.SelectMany(p => p.WorldVolumes())
            .Where(v => v.Volume.Kind == VolumeKind.Solid || v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
        foreach (var m in t.Markers)
            if (solid.Any(v => Distance.Between(v, m.Position.Horizontal) < m.Diameter / 2 + 1)) return $"a marker at {m.Position.X},{m.Position.Y} is within 1\" of impassable or solid terrain";
        return LayoutRules.Problem(t, nameRequired: true);
    }
}

/// <summary>Builds the four predefined tables (RandomTables, first passing seed) and writes them as data.</summary>
internal static class BuiltinTablesWriter
{
    static readonly JsonSerializerOptions Indented = new(ContentJson.Options) { WriteIndented = true };
    static readonly TableSize Size = new(72, 48);
    static readonly string[] Textures = ["grass", "wasteland", "alien", "urban"];

    public static string FileName(Planet planet) => planet.ToString().ToLowerInvariant() + ".json";

    public static string Text(Planet planet)
    {
        var terrain = BuiltinTerrain.FromCatalog(File.ReadAllText(Path.Combine(BuiltinTablesTests.TablesDir(), "..", "catalog.json")));
        var texture = Textures[Array.IndexOf(BuiltinLayouts.Planets, planet)];
        var zoneShapes = ZoneStyles.ShapesOf(ZoneStyles.Frontline);
        for (var seed = 1; seed <= 100000; seed++)
        {
            var ids = new Random(seed);
            DeploymentZone[] zones = [.. zoneShapes.Select((s, i) => new DeploymentZone(Id(ids), s, i + 1))];
            var laid = RandomTables.Lay(planet, Size, zones, seed, terrain);
            if (laid.Unplaced.Count > 0) continue;
            var markers = BuiltinTableChecks.Objectives.Select(o =>
                new Marker(Id(ids), MarkerKind.Objective, new Point3(o.X, o.Y, 0), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"))).ToList();
            var state = new TableState(Id(ids), $"{planet} planet", Size, VisualRef.OfBuiltin(texture), laid.Pieces.Select(p => p.Placement).ToList(), markers, zones);
            if (BuiltinTableChecks.Problem(state, BuiltinTableChecks.DifficultPercent(planet)) is null) return JsonSerializer.Serialize(state, Indented) + "\n";
        }
        throw new InvalidOperationException($"No seed from 1 to 100000 gives a {planet} table that passes the checks.");
    }

    static Guid Id(Random rng)
    {
        Span<byte> b = stackalloc byte[16];
        rng.NextBytes(b);
        b[6] = (byte)((b[6] & 0x0F) | 0x40);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b, bigEndian: true);
    }
}

public class BuiltinTablesTests : IClassFixture<ManagementFactory>
{
    readonly ManagementFactory _f;
    public BuiltinTablesTests(ManagementFactory f) => _f = f;

    internal static string TablesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tabletop.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Tabletop.sln not found."), "client", "content", "builtin", "tables");
    }

    static string Committed(Planet planet) => File.ReadAllText(Path.Combine(BuiltinTablesTests.TablesDir(), BuiltinTablesWriter.FileName(planet)));

    [Fact]
    public void Each_committed_table_passes_the_checks()
    {
        foreach (var planet in BuiltinLayouts.Planets)
        {
            var t = ContentJson.Deserialize<TableState>(Committed(planet));
            Assert.Null(BuiltinTableChecks.Problem(t, BuiltinTableChecks.DifficultPercent(planet)));
            Assert.Equal(new TableSize(72, 48), t.Size);
            Assert.Equal(BuiltinTableChecks.Objectives, t.Markers.Where(m => m.Kind == MarkerKind.Objective).Select(m => (m.Position.X, m.Position.Y)));
            Assert.Equal([("frontline/1", 1), ("frontline/2", 2)], t.Zones.Select(z => (z.Shape, z.TeamOrdinal)));
        }
    }

    [Fact]
    public void The_writer_reproduces_the_committed_files()
    {
        foreach (var planet in BuiltinLayouts.Planets) Assert.Equal(Committed(planet), BuiltinTablesWriter.Text(planet));
    }

    [Fact]
    public async Task Get_builtin_lists_the_four_tables()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var tables = await c.GetJson<List<TableState>>("/v1/layouts/builtin");
        Assert.Equal(["Hospitable planet", "Wasteland planet", "Death planet", "Ruins planet"], tables.Select(t => t.Name));
        Assert.Equal(BuiltinLayouts.Planets.Select(p => ContentJson.Serialize(ContentJson.Deserialize<TableState>(Committed(p)))), tables.Select(ContentJson.Serialize));
    }

    [Fact]
    public async Task The_account_list_is_unchanged()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        Assert.Empty(await c.GetJson<List<LayoutSummary>>("/v1/layouts"));
        await c.PostJson<TableState>("/v1/layouts", LayoutTests.Sample());
        Assert.Single(await c.GetJson<List<LayoutSummary>>("/v1/layouts"));
    }

    [Fact]
    public async Task Get_builtin_signed_out_is_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/v1/layouts/builtin")).StatusCode);
}

/// <summary>Not on the factory: the factory reads the files this test writes.</summary>
public class BuiltinTablesWriterTests
{
    [Fact]
    public void Write_the_builtin_tables()
    {
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_GENERATED") != "1") return;
        Directory.CreateDirectory(BuiltinTablesTests.TablesDir());
        foreach (var planet in BuiltinLayouts.Planets) File.WriteAllText(Path.Combine(BuiltinTablesTests.TablesDir(), BuiltinTablesWriter.FileName(planet)), BuiltinTablesWriter.Text(planet));
    }
}
