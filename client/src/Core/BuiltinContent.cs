using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>The built-in content in use: the packed catalog (<c>client/content/builtin/catalog.json</c>, embedded) until
/// <see cref="BuiltinSync"/> brings the server's. Its files come from the cache, from res:// in a build that packs them (development),
/// else on demand (spec 2026-11-07-assets-on-demand-and-client-releases-design.md). The game server never reads any of it (setup phase → Terrain).</summary>
public static class BuiltinContent
{
    /// <summary>The catalog this build carries: ids, kinds and volumes, no files.</summary>
    public static BuiltinCatalog Packed { get; } = ReadPacked();

    static BuiltinCatalog ReadPacked()
    {
        using var stream = typeof(BuiltinContent).Assembly.GetManifestResourceStream("builtin-catalog.json")
            ?? throw new InvalidOperationException("The packed built-in catalog is not embedded.");
        using var reader = new StreamReader(stream);
        return BuiltinCatalog.Parse(reader.ReadToEnd());
    }

    static volatile BuiltinCatalog _catalog = Packed;
    public static BuiltinCatalog Catalog => _catalog;
    /// <summary>Raised on the main thread after <see cref="Use"/>.</summary>
    public static event Action? Changed;
    /// <summary>Main thread only.</summary>
    public static void Use(BuiltinCatalog catalog) { _catalog = catalog; Changed?.Invoke(); }
    /// <summary>Where served files are; null until <c>Main</c> sets it, and in tests.</summary>
    public static AssetCache? Cache { get; set; }
    /// <summary>Whether this build packs <c>res://content/builtin/&lt;path&gt;</c>: a development run does, a release export does not.
    /// <c>Main</c> asks Godot; tests take every file as packed.</summary>
    public static Func<string, bool> Packs { get; set; } = _ => true;
    /// <summary>Fetches a file not on hand; null until <c>Main</c> sets it, and in tests.</summary>
    public static BuiltinSync? Sync { get; set; }

    public static IReadOnlyDictionary<string, BuiltinTerrain> Terrain => Catalog.Terrain;
    public static IReadOnlyList<string> Textures => Catalog.Textures;
    /// <summary>The meshes the built-ins share; several pieces name each, differing by footprint, height, scale and yaw.</summary>
    public static IReadOnlyList<string> TerrainMeshGroups => Catalog.Meshes;
    public static IReadOnlyList<string> MarkerVisuals { get; } = ["objective-ring"];
    public static IReadOnlyList<string> ZoneStyleIds => ZoneStyles.All;

    /// <summary>The file at a catalog path (<see cref="BuiltinCatalog.MeshPath"/>, <see cref="BuiltinCatalog.TexturePath"/>) when on hand:
    /// a cached blob or a packed res:// path; null means fetch it (<see cref="FileAsync"/>).</summary>
    public static string? File(string path) => Catalog.FileFor(path, Cache, Packs);

    /// <summary><see cref="File"/>, else the file fetched by the catalog's hash; null when it cannot be had.</summary>
    public static async Task<string?> FileAsync(string path) => File(path) ?? (Sync is { } sync ? await sync.FileAsync(path) : null);
    /// <summary>The built-in meshes are glTF, so Y-up: height on glTF +Y in [0, 1], the footprint on X and Z. Pitch 90 reads
    /// them into the table-frame unit box (z up, base at z = 0), the same value the web app writes for a Y-up asset.</summary>
    public static readonly Orientation MeshOrientation = new(Pitch: 90, Yaw: 0, Roll: 0);

    /// <summary>The catalog path of the piece's mesh; null for a name the catalog does not list.</summary>
    public static string? MeshPathOf(string name) => Terrain.TryGetValue(name, out var t) ? BuiltinCatalog.MeshPath(t.Mesh) : null;

    public static IReadOnlyList<TerrainVolume> VolumesOf(string name) => [new TerrainVolume(Guid.CreateVersion7(), Terrain[name].Volume)];

    /// <summary><see cref="BuiltinCatalog.Place"/> in the catalog in use.</summary>
    public static TerrainPlacement Place(string name, Point3 position, Yaw yaw, double scale = 1) => Catalog.Place(name, position, yaw, scale);
}
