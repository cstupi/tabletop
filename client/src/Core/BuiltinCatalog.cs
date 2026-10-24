using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>A published file: its content hash and size, and where to download it (a served catalog only).</summary>
public sealed record BuiltinFile(string Sha256, long Size, Uri? Url = null);

/// <summary>
/// The built-in content: terrain, its meshes, the table textures (spec 2026-10-18-builtin-content-from-server-design.md).
/// One shape for <c>client/content/builtin/catalog.json</c>, the published <c>builtin/catalog.json</c> (plus <c>version</c>
/// and <c>files</c>) and <c>GET /v1/builtin</c> (plus each file's <c>url</c>). The packed catalog has no version and no files.
/// </summary>
public sealed partial class BuiltinCatalog
{
    public string? Version { get; }
    public IReadOnlyList<string> Meshes { get; }
    public IReadOnlyList<string> Textures { get; }
    public IReadOnlyDictionary<string, BuiltinTerrain> Terrain { get; }
    /// <summary>By path under <c>client/content/builtin/</c>: <see cref="MeshPath"/> and <see cref="TexturePath"/>.</summary>
    public IReadOnlyDictionary<string, BuiltinFile> Files { get; }
    readonly Dto _dto;

    BuiltinCatalog(Dto dto, string? version, IReadOnlyList<string> meshes, IReadOnlyList<string> textures, IReadOnlyDictionary<string, BuiltinTerrain> terrain, IReadOnlyDictionary<string, BuiltinFile> files)
        => (_dto, Version, Meshes, Textures, Terrain, Files) = (dto, version, meshes, textures, terrain, files);

    public static string MeshPath(string mesh) => $"terrain/{mesh}.glb";
    public static string TexturePath(string texture) => $"textures/{texture}.png";

    /// <summary>Every file the catalog names, with the download cap for its kind.</summary>
    public IEnumerable<(string Path, BuiltinFile File, long Cap)> Downloads =>
        Meshes.Select(m => (MeshPath(m), AssetCache.MeshCap)).Concat(Textures.Select(t => (TexturePath(t), AssetCache.TextureCap)))
            .Where(x => Files.ContainsKey(x.Item1)).Select(x => (x.Item1, Files[x.Item1], x.Item2));

    /// <summary>The cached blob when this catalog lists the path and the cache holds its hash, else the packed file.</summary>
    public string FileFor(string path, AssetCache? cache) =>
        Files.TryGetValue(path, out var f) && cache is not null && cache.Has(f.Sha256) ? cache.PathFor(f.Sha256) : $"res://content/builtin/{path}";

    /// <summary>A placement of a built-in: the volumes are copied on add, so the draft is self-contained and a later catalog never
    /// changes a placed piece (docs/schema.md → A terrain placement names a built-in or an asset).</summary>
    public TerrainPlacement Place(string name, Point3 position, Yaw yaw, double scale = 1) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin(name), position, yaw, scale, [new TerrainVolume(Guid.CreateVersion7(), Terrain[name].Volume)]);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true };

    /// <summary>The catalog without download URLs, for <c>builtin.json</c> in the user dir.</summary>
    public string ToJson() => JsonSerializer.Serialize(_dto with { Files = _dto.Files?.ToDictionary(kv => kv.Key, kv => kv.Value with { Url = null }) }, Json);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")] private static partial Regex Name();
    [GeneratedRegex("^[0-9a-f]{64}$")] private static partial Regex Sha256();

    /// <summary>Reads and checks a catalog; <see cref="FormatException"/> names the first fault.</summary>
    public static BuiltinCatalog Parse(string json)
    {
        Dto dto;
        try { dto = JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new FormatException("The catalog is empty."); }
        catch (JsonException e) { throw new FormatException($"The catalog is not JSON: {e.Message}"); }
        var meshes = Names(dto.Meshes, "mesh"); var textures = Names(dto.Textures, "texture");
        if (meshes.Count == 0) throw new FormatException("The catalog names no mesh.");
        if (textures.Count == 0) throw new FormatException("The catalog names no texture.");
        var terrain = BuiltinTerrain.Parse(dto.Terrain ?? throw new FormatException("The catalog has no terrain."), meshes);
        var files = new Dictionary<string, BuiltinFile>();
        if (dto.Files is { } named)
        {
            if (string.IsNullOrWhiteSpace(dto.Version)) throw new FormatException("A catalog with files names no version.");
            foreach (var (path, cap) in meshes.Select(m => (MeshPath(m), AssetCache.MeshCap)).Concat(textures.Select(x => (TexturePath(x), AssetCache.TextureCap))))
            {
                if (!named.TryGetValue(path, out var f) || f.Sha256 is null || !Sha256().IsMatch(f.Sha256)) throw new FormatException($"The catalog has no sha256 for {path}.");
                if (f.Size is <= 0 || f.Size > cap) throw new FormatException($"{path} is {f.Size} bytes, over the {cap}-byte cap or empty.");
                files[path] = new BuiltinFile(f.Sha256, f.Size, f.Url);
            }
        }
        return new BuiltinCatalog(dto, string.IsNullOrWhiteSpace(dto.Version) ? null : dto.Version, meshes, textures, terrain, files);
    }

    static List<string> Names(List<string>? names, string what)
    {
        var list = names ?? [];
        foreach (var n in list) if (!Name().IsMatch(n)) throw new FormatException($"The {what} name '{n}' is not a name.");
        if (list.Distinct().Count() != list.Count) throw new FormatException($"A {what} is listed twice.");
        return list;
    }

    sealed record Dto(string? Version, List<string>? Meshes, List<string>? Textures, JsonElement? Terrain, Dictionary<string, FileDto>? Files);
    sealed record FileDto(string? Sha256, long Size, Uri? Url);
}
