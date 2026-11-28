using System.Diagnostics;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Xunit.Abstractions;

namespace Tabletop.Client.Tests;

/// <summary>
/// Wall times of a move Ask's areas (`TABLETOP_BENCH=1 dotnet test -c Release --filter MoveAreaBench`): on the predefined tables an infantry unit of
/// 5, 10 or 20 on 32 mm bases advancing 6" or rushing 12" and five cavalry on 60 × 35 mm ovals rushing 16", among the table's terrain and an enemy
/// unit; and two Rush Asks of a played game as the client held them (`Fixtures/move-scenes/`: the Ask, the models, the board's volumes). Prints the
/// best of five runs: the lead's area alone, the Ask opening (the lead's area while Prepare builds every reach and the unit's area), Prepare to the
/// end, the whole unit's area cold, and the triangles the node draws. Skipped without the variable.
/// </summary>
[Collection(TimingCollection.Name)]
public class MoveAreaBench(ITestOutputHelper output)
{
    static readonly Guid Mine = Guid.CreateVersion7(), Enemy = Guid.CreateVersion7();
    static readonly Base Infantry = Base.Circle(1.2598), Oval = Base.Oval(2.362204724409449, 1.3779527559055118);

    internal static IReadOnlyList<WorldVolume> Table(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "../../../../../client/content/builtin/tables", name + ".json");
        return ContentJson.Deserialize<TableState>(File.ReadAllText(path))!.Terrain.SelectMany(t => t.WorldVolumes()).ToList();
    }

    /// <summary>The unit in rows of five, 1.6" apart, at the first anchor where no base centre stands in a volume.</summary>
    internal static List<PlacedModel> Unit(IReadOnlyList<WorldVolume> volumes, int count, Base b, double height, double spacing, Point2[] anchors)
    {
        foreach (var anchor in anchors)
        {
            var models = Enumerable.Range(0, count).Select(i => new PlacedModel(new ModelBody(b, height),
                new Placement(new Point3(anchor.X + i % 5 * spacing, anchor.Y + i / 5 * 1.6, 0), new Yaw(0)))).ToList();
            if (models.All(m => Containment.VolumesContaining(m, volumes).Count == 0)) return models;
        }
        throw new InvalidOperationException("no free anchor");
    }

    internal static (MoveRegions Regions, Guid[] Ids) Scene(IReadOnlyList<WorldVolume> volumes, List<PlacedModel> unit, double allowance, List<PlacedModel> others)
    {
        var ids = unit.Select(_ => Guid.CreateVersion7()).ToArray();
        List<Guid> Ids(TerrainDeclarations d) => volumes.Where(v => v.Volume.Declarations.HasFlag(d)).Select(v => v.Id).ToList();
        var legal = new MoveRegion(ids, allowance, Ids(TerrainDeclarations.Impassable), Ids(TerrainDeclarations.Difficult), 6, Separation: 1,
            SteepVolumeIds: Ids(TerrainDeclarations.Steep), DangerousVolumeIds: Ids(TerrainDeclarations.Dangerous),
            UnstableVolumeIds: Ids(TerrainDeclarations.DangerousOnRushCharge), UnstableOver: 6);
        // The enemy is one unit, held to as its perimeter grown by the separation (p.7, MoveUnits), as MoveRegions.Of reads a game.
        var units = new MoveUnits([.. others.Select(_ => Enemy)], Mine, legal.Separation);
        return (new MoveRegions(ids.Zip(unit).ToDictionary(p => p.First, p => p.Second), legal, volumes, new TableSize(72, 48), others, units), ids);
    }

    static readonly Point2[] Anchors = [new(30, 20), new(28, 26), new(34, 18), new(26, 14), new(38, 28), new(20, 24), new(44, 20), new(30, 10), new(36, 34),
        new(14, 24), new(50, 24), new(30, 38), new(46, 12), new(56, 30), new(18, 8)];

    public static IEnumerable<object[]> Cases()
    {
        foreach (var table in new[] { "ruins", "death" })
        {
            foreach (var n in new[] { 5, 10, 20 })
                foreach (var allowance in new[] { 6.0, 12.0 })
                    yield return [table, n, allowance, false];
            yield return [table, 5, 16.0, true];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Times(string table, int count, double allowance, bool cavalry)
    {
        if (Environment.GetEnvironmentVariable("TABLETOP_BENCH") != "1") return;
        var volumes = Table(table);
        var unit = cavalry ? Unit(volumes, count, Oval, 4.43, 1.6, Anchors) : Unit(volumes, count, Infantry, 1.89, 1.5, Anchors);
        var enemy = Unit(volumes, 10, Infantry, 1.89, 1.5, [.. Anchors.Select(a => a + new Point2(0, -9)), .. Anchors.Select(a => a + new Point2(10, 0))])
            .Where(m => unit.All(u => !Overlap.Bases(u, m))).ToList();
        await Measure($"{table} {(cavalry ? "cavalry" : "infantry")} x{count} {allowance}\"", () => Scene(volumes, unit, allowance, enemy));
    }

    /// <summary>A move Ask as the client held it: the Ask, its models where they stood, the board's volumes and every other model.</summary>
    sealed record MoveScene(IReadOnlyList<PlacedModel> Starts, MoveRegion Legal, IReadOnlyList<WorldVolume> Volumes, TableSize Size, IReadOnlyList<PlacedModel> Others);

    [Theory]
    [InlineData("rush-6")]
    [InlineData("rush-4")]
    public async Task Game(string name)
    {
        if (Environment.GetEnvironmentVariable("TABLETOP_BENCH") != "1") return;
        var scene = System.Text.Json.JsonSerializer.Deserialize<MoveScene>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/move-scenes", name + ".json")), WireJson.Options)!;
        await Measure($"game {name} {scene.Legal.Allowance}\"", () =>
            (new MoveRegions(scene.Legal.ModelIds.Zip(scene.Starts).ToDictionary(p => p.First, p => p.Second), scene.Legal, scene.Volumes, scene.Size, scene.Others),
                [.. scene.Legal.ModelIds]));
    }

    async Task Measure(string name, Func<(MoveRegions Regions, Guid[] Ids)> make)
    {
        // Warm the JIT on a throwaway scene.
        { var (r, ids) = make(); await r.Prepare(ids[0]); }
        const int Runs = 5;
        double lead = 1e9, open = 1e9, prepare = 1e9, cold = 1e9; int tris = 0;
        for (var run = 0; run < Runs; run++)
        {
            {
                var (r, ids) = make();
                var w = Stopwatch.StartNew(); r.Bands(ids[0]); lead = Math.Min(lead, w.Elapsed.TotalMilliseconds);
            }
            {
                var (r, ids) = make();
                var w = Stopwatch.StartNew();
                var shown = r.AreaAsync([ids[0]]); var prep = r.Prepare(ids[0]);
                await shown; open = Math.Min(open, w.Elapsed.TotalMilliseconds);
                await prep; prepare = Math.Min(prepare, w.Elapsed.TotalMilliseconds);
                var all = r.Area(ids);
                tris = (all.Clear.Count + all.Difficult.Count + all.Dangerous.Count) / 3;
            }
            {
                var (r, ids) = make();
                var w = Stopwatch.StartNew(); await r.AreaAsync(ids); cold = Math.Min(cold, w.Elapsed.TotalMilliseconds);
            }
        }
        output.WriteLine($"{name}: lead {lead:F0} ms, open {open:F0} ms, prepare {prepare:F0} ms, unit cold {cold:F0} ms, {tris} triangles (best of {Runs})");
    }
}
