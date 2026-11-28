using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Geometry.Tests;

/// <summary>
/// The reference game's table at its first move Ask of round 1 (seq 210): 150 models (15 units of 5 a side), a ruin and two forests.
/// Fixtures/reference-move.json is written from the viewer perf fixtures by projecting the log to that seq.
/// </summary>
internal sealed record ReferenceScene(long Seq, TableSize Size, List<WorldVolume> Volumes, List<ReferenceScene.Model> Models, List<Guid> Movers,
    double Allowance, List<Guid> Impassable, List<Guid> Difficult, double? DifficultCap, double? Separation, bool PassesModels)
{
    internal sealed record Model(Guid Id, Guid UnitId, ModelBody Body, Placement Placement)
    {
        public PlacedModel Placed => new(Body, Placement);
    }

    internal static readonly JsonSerializerOptions Json = Options();

    static JsonSerializerOptions Options()
    {
        var o = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static ReferenceScene Load() =>
        JsonSerializer.Deserialize<ReferenceScene>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference-move.json")), Json)!;

    /// <summary>The units in id order, each with its models in id order.</summary>
    public IReadOnlyList<IReadOnlyList<Model>> Units() =>
        Models.GroupBy(m => m.UnitId).OrderBy(g => g.Key).Select(g => (IReadOnlyList<Model>)g.OrderBy(m => m.Id).ToList()).ToList();

    public List<PlacedModel> Others(IEnumerable<Model> unit)
    {
        var ids = unit.Select(m => m.Id).ToHashSet();
        return Models.Where(m => !ids.Contains(m.Id)).Select(m => m.Placed).ToList();
    }

    /// <summary>Others' units, in Others' order, with the unit as the mover's own (MoveUnits), held to the scene's separation.</summary>
    public MoveUnits OthersUnits(IReadOnlyList<Model> unit)
    {
        var ids = unit.Select(m => m.Id).ToHashSet();
        return new MoveUnits([.. Models.Where(m => !ids.Contains(m.Id)).Select(m => m.UnitId)], unit[0].UnitId, Separation);
    }

    public List<WorldVolume> DifficultVolumes => Volumes.Where(v => Difficult.Contains(v.Id)).ToList();

    /// <summary>The ruin (the scene's one Solid) declared Impassable, for the impassable paths.</summary>
    public List<WorldVolume> WithImpassableRuin() =>
        Volumes.Select(v => v.Volume.Kind == VolumeKind.Solid ? v with { Volume = v.Volume with { Declarations = v.Volume.Declarations | TerrainDeclarations.Impassable } } : v).ToList();

    /// <summary>An exact digest of a point list: every coordinate's bits, in order.</summary>
    public static string Digest(IEnumerable<double> values)
    {
        var bytes = values.SelectMany(v => BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(v))).ToArray();
        return Convert.ToHexString(SHA256.HashData(bytes))[..32];
    }

    /// <summary>Repo path of a fixture in this test project, for rewriting it (TABLETOP_WRITE_FIXTURES=1).</summary>
    public static string SourceFixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tabletop.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "Tabletop.Geometry.Tests", "Fixtures", name);
    }
}
