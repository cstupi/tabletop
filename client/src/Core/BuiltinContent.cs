using Tabletop.Content;
using Tabletop.Geometry;
using static Tabletop.Geometry.TerrainDeclarations;

namespace Tabletop.Client.Core;

/// <summary>A built-in terrain piece: its volume (the data) and an optional mesh file (the appearance). Owned by nobody, in no library.</summary>
public sealed record BuiltinTerrain(string Name, string Label, Volume Volume, string? MeshFile);

/// <summary>What ships inside the client: terrain, textures, marker visuals. The server never reads any of it (setup phase → Terrain).</summary>
public static class BuiltinContent
{
    const string Solid = "solid", Area = "area";
    static Volume V(string kind, Footprint f, double top, TerrainDeclarations d, string? notes = null) =>
        new(kind == Solid ? VolumeKind.Solid : VolumeKind.Area, f, new Point2(0, 0), new Yaw(0), 0, top, d, notes);
    // A built-in names its group's mesh, not one of its own: thirteen meshes cover the 35 pieces, differing by footprint,
    // height, scale and yaw. MeshLoader.LoadBuiltin fits the group mesh to the placement's volume (Task 11).
    static BuiltinTerrain T(string name, string label, Volume v, string group) => new(name, label, v, $"res://content/builtin/terrain/{group}.glb");
    const string RushCharge = "Dangerous terrain when using Rush/Charge actions";
    const string OnTop = "Units on top may ignore one unit/terrain for line of sight";
    const string MovingUp = "Difficult terrain for units moving up";

    static readonly BuiltinTerrain[] All =
    [
        // Generic (p.18)
        T("barricade", "Barricade", V(Solid, Footprint.Rect(3, 1), 1, Difficult | TerrainDeclarations.Cover), "wall"),        // as Wall or Fence, p.18: Cover, and Difficult when moving through it
        T("large-forest", "Large Forest", V(Area, Footprint.Circle(3), 6, Difficult | TerrainDeclarations.Cover | BlocksLineOfSight | SeeInAndOut), "woods"),
        T("large-field", "Large Field", V(Area, Footprint.Rect(6, 6), 1, Difficult | TerrainDeclarations.Cover), "field"),
        T("large-sight-blocker", "Large Sight Blocker", V(Solid, Footprint.Rect(6, 6), 6, Impassable | BlocksLineOfSight), "building"),   // p.11: over 3" is elevated and impassable
        T("large-hill", "Large Hill", V(Solid, Footprint.Circle(4), 2, TerrainDeclarations.Cover, OnTop), "hill"),
        T("large-ruins", "Large Ruins", V(Area, Footprint.Rect(6, 6), 4, TerrainDeclarations.Cover, RushCharge), "ruins"),
        T("large-lake", "Large Lake", V(Area, Footprint.Circle(4), 0.5, Impassable), "lake"),
        // Hospitable (p.18)
        T("wall-or-fence", "Wall or Fence", V(Solid, Footprint.Rect(6, 0.5), 1.5, Difficult | TerrainDeclarations.Cover), "wall"),   // p.18: "Cover, as well as Difficult terrain when moving through it"
        T("hill", "Hill", V(Solid, Footprint.Circle(3), 2, TerrainDeclarations.Cover, OnTop), "hill"),
        T("plantation", "Plantation", V(Area, Footprint.Rect(6, 6), 6, Difficult | TerrainDeclarations.Cover | BlocksLineOfSight | SeeInAndOut), "woods"),
        T("forest", "Forest", V(Area, Footprint.Circle(3), 6, Difficult | TerrainDeclarations.Cover | BlocksLineOfSight | SeeInAndOut), "woods"),
        T("large-building", "Large Building", V(Solid, Footprint.Rect(6, 6), 8, Impassable | BlocksLineOfSight), "building"),
        T("deep-river", "Deep River or Lake", V(Area, Footprint.Rect(3, 12), 0.5, Impassable), "water"),
        T("shallow-river", "Shallow River or Stream", V(Area, Footprint.Rect(3, 12), 0.5, Difficult, "Optionally one 3\"-6\" wide bridge per full 24\""), "water"),
        T("tall-crops-field", "Tall Crops Field", V(Area, Footprint.Rect(6, 6), 1, Difficult | TerrainDeclarations.Cover), "field"),
        T("ruins", "Ruins or Debris", V(Area, Footprint.Rect(6, 6), 4, TerrainDeclarations.Cover, RushCharge), "ruins"),
        T("small-buildings", "Group of Small Buildings", V(Solid, Footprint.Rect(6, 6), 4, Impassable | BlocksLineOfSight), "building"),
        T("steep-hill", "Steep Hill", V(Solid, Footprint.Circle(3), 3, TerrainDeclarations.Cover, $"{MovingUp}. {OnTop}"), "hill"),
        // Wasteland (p.19)
        T("small-dunes", "Group of Small Dunes", V(Solid, Footprint.Rect(6, 4), 1.5, TerrainDeclarations.Cover, OnTop), "hill-rect"),
        T("large-rock", "Large Rock or Two to Three Small Rocks", V(Solid, Footprint.Rect(4, 4), 4, Impassable | BlocksLineOfSight), "rock"),
        T("crater", "Crater", V(Area, Footprint.Circle(3), 1, TerrainDeclarations.Cover), "crater"),
        T("toxic-smog", "Toxic Smog", V(Area, Footprint.Circle(4), 6, TerrainDeclarations.Cover | Dangerous | BlocksLineOfSight | SeeInAndOut), "haze"),
        T("toxic-river", "Toxic River", V(Area, Footprint.Rect(3, 12), 0.5, Difficult | Dangerous, "Optionally one 3\"-6\" wide bridge per full 24\""), "water"),
        T("sludge-stream", "Sludge Stream or Canal", V(Area, Footprint.Rect(2, 12), 0.5, Dangerous, "Optionally one 3\"-6\" wide bridge per full 24\""), "water"),
        T("sludge-field", "Sludge Field", V(Area, Footprint.Rect(6, 6), 0.5, Difficult), "pad"),
        T("mine-field", "Mine Field", V(Area, Footprint.Rect(6, 6), 0.1, Dangerous), "pad"),
        // Death (p.19)
        T("overgrown-ruins", "Overgrown Ruins", V(Area, Footprint.Rect(6, 6), 4, Difficult | TerrainDeclarations.Cover), "ruins"),
        T("deadly-vegetation", "Deadly Vegetation", V(Area, Footprint.Circle(3), 6, Difficult | TerrainDeclarations.Cover | Dangerous), "woods"),
        T("dense-vegetation", "Dense Vegetation", V(Area, Footprint.Circle(3), 6, Difficult | TerrainDeclarations.Cover | BlocksLineOfSight | SeeInAndOut), "woods"),
        T("death-marsh", "Death Marsh", V(Area, Footprint.Rect(6, 6), 0.2, Difficult | Dangerous), "pad"),
        T("crumbly-rock", "Crumbly Rock", V(Solid, Footprint.Rect(4, 4), 2, Difficult), "rock"),
        T("beast-lair", "Beast Lair", V(Solid, Footprint.Circle(2), 2, Dangerous), "hill"),
        T("quicksand", "Quicksand", V(Area, Footprint.Rect(4, 4), 0.1, None, RushCharge), "pad"),
        // Names the contracts plan's samples use
        T("hill-small", "Small Hill", V(Solid, Footprint.Rect(6, 6), 2, None), "hill-rect"),
        T("ruin", "Ruin", V(Area, Footprint.Rect(4, 4), 4, Difficult | TerrainDeclarations.Cover | BlocksLineOfSight | SeeInAndOut), "ruins"),   // owner ruling: passable, difficult
    ];

    /// <summary>The thirteen mesh files the 35 built-ins share, by their basename under res://content/builtin/terrain/.</summary>
    public static IReadOnlyList<string> TerrainMeshGroups { get; } = ["hill", "hill-rect", "woods", "ruins", "building", "wall", "rock", "field", "water", "lake", "pad", "crater", "haze"];

    public static IReadOnlyDictionary<string, BuiltinTerrain> Terrain { get; } = All.ToDictionary(t => t.Name);
    public static IReadOnlyList<string> Textures { get; } = ["grass", "desert", "urban", "snow", "ash"];
    public static IReadOnlyList<string> MarkerVisuals { get; } = ["objective-ring", "chip"];
    public static IReadOnlyList<string> ZoneStyleIds => ZoneStyles.All;

    public static string TextureFile(string name) => $"res://content/builtin/textures/{name}.png";
    /// <summary>The built-in meshes are glTF, so Y-up: height on glTF +Y in [0, 1], the footprint on X and Z. Pitch 90 reads
    /// them into the table-frame unit box (z up, base at z = 0), the same value the web app writes for a Y-up asset.</summary>
    public static readonly Orientation MeshOrientation = new(Pitch: 90, Yaw: 0, Roll: 0);

    public static string? TerrainMeshFile(string name) => Terrain.TryGetValue(name, out var t) ? t.MeshFile ?? $"res://content/builtin/terrain/{name}.glb" : null;

    public static IReadOnlyList<TerrainVolume> VolumesOf(string name) => [new TerrainVolume(Guid.CreateVersion7(), Terrain[name].Volume)];

    /// <summary>A placement of a built-in: the volumes are copied on add, so the draft is self-contained and the game server never reads `asset_volume` (`docs/schema.md` → A terrain placement names a built-in or an asset).</summary>
    public static TerrainPlacement Place(string name, Point3 position, Yaw yaw, double scale = 1) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin(name), position, yaw, scale, VolumesOf(name));
}
