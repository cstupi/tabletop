using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>The built-in content in use: the packed catalog (<c>client/content/builtin/catalog.json</c>, embedded) until
/// <see cref="BuiltinSync"/> brings the server's. The game server never reads any of it (setup phase → Terrain).</summary>
public static class BuiltinContent
{
    /// <summary>The catalog this build carries, with its files under <c>res://content/builtin/</c>.</summary>
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

    public static IReadOnlyDictionary<string, BuiltinTerrain> Terrain => Catalog.Terrain;
    public static IReadOnlyList<string> Textures => Catalog.Textures;
    /// <summary>The meshes the built-ins share; several pieces name each, differing by footprint, height, scale and yaw.</summary>
    public static IReadOnlyList<string> TerrainMeshGroups => Catalog.Meshes;
    public static IReadOnlyList<string> MarkerVisuals { get; } = ["objective-ring", "chip"];
    public static IReadOnlyList<string> ZoneStyleIds => ZoneStyles.All;

    public static string TextureFile(string name) => Catalog.FileFor(BuiltinCatalog.TexturePath(name), Cache);
    /// <summary>The built-in meshes are glTF, so Y-up: height on glTF +Y in [0, 1], the footprint on X and Z. Pitch 90 reads
    /// them into the table-frame unit box (z up, base at z = 0), the same value the web app writes for a Y-up asset.</summary>
    public static readonly Orientation MeshOrientation = new(Pitch: 90, Yaw: 0, Roll: 0);

    /// <summary>The file of the piece's mesh: a cached blob or a packed res:// path. Null for a name the catalog does not list.</summary>
    public static string? TerrainMeshFile(string name) => Terrain.TryGetValue(name, out var t) ? Catalog.FileFor(BuiltinCatalog.MeshPath(t.Mesh), Cache) : null;

    public static IReadOnlyList<TerrainVolume> VolumesOf(string name) => [new TerrainVolume(Guid.CreateVersion7(), Terrain[name].Volume)];

    /// <summary><see cref="BuiltinCatalog.Place"/> in the catalog in use.</summary>
    public static TerrainPlacement Place(string name, Point3 position, Yaw yaw, double scale = 1) => Catalog.Place(name, position, yaw, scale);
}
