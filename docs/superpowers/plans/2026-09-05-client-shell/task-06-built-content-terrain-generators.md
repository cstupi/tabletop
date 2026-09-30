# Task 6: Built-in content and the four terrain generators

**Files:**
- Create: `client/src/Core/BuiltinContent.cs`, `client/src/Core/TerrainGenerators.cs`
- Test: `tests/Tabletop.Client.Tests/BuiltinContentTests.cs`, `tests/Tabletop.Client.Tests/TerrainGeneratorTests.cs`
- Author (art, not code — Step 4): `client/content/builtin/terrain/{hill,woods,ruins,building,wall,rock,field,water,pad,haze}.glb`, `client/content/builtin/textures/{grass,desert,urban,snow,ash}.png`

**Interfaces:**
- Consumes: `Volume`, `VolumeKind`, `Footprint`, `TerrainDeclarations`, `Point2`, `Point3`, `Yaw`, `TableSize`, `ZoneStyles` (Geometry); `TerrainPlacement`, `TerrainVolume`, `VisualRef` (Content); `AddTerrain` (Protocol).
- Produces: `sealed record BuiltinTerrain(string Name, string Label, Volume Volume, string? MeshFile)`; `static class BuiltinContent` — `IReadOnlyDictionary<string, BuiltinTerrain> Terrain`, `IReadOnlyList<string> Textures` (`grass`, `desert`, `urban`, `snow`, `ash`), `IReadOnlyList<string> MarkerVisuals` (`objective-ring`, `chip`), `IReadOnlyList<string> TerrainMeshGroups` (the ten shared mesh basenames), `IReadOnlyList<string> ZoneStyleIds => ZoneStyles.All`, `TerrainPlacement Place(string name, Point3 position, Yaw yaw, double scale = 1)` (volumes copied from the built-in — the placement's rows are the volumes), `IReadOnlyList<TerrainVolume> VolumesOf(string name)` (what "clear overrides" re-copies), `string TextureFile(string name)`, `string? TerrainMeshFile(string name)`; `enum Planet { Generic, Hospitable, Wasteland, Death }`; `static class TerrainGenerators` — `IReadOnlyList<AddTerrain> Generate(Planet planet, TableSize table, Random rng)` (nine fixed pieces then 4D3 rolled on 2D6, placed off the slab beside the long edge at `x = width + 4`, in rows 8" apart), `IReadOnlyList<string> Fixed(Planet)`, `IReadOnlyList<string> Rolled(Planet, int twoD6)` (the alternatives an entry names).
- **A minimal art set ships, grouped ten ways**, so a generated table looks like a table. 35 distinct meshes is not minimal, and the pieces fall into ten shapes that differ only by footprint, height, scale and yaw: `hill` (hill, large-hill, steep-hill, hill-small, small-dunes, beast-lair — 6), `woods` (forest, large-forest, plantation, dense-vegetation, deadly-vegetation — 5), `ruins` (ruins, large-ruins, overgrown-ruins, ruin — 4), `building` (large-building, small-buildings, large-sight-blocker — 3), `wall` (wall-or-fence, barricade — 2), `rock` (large-rock, crumbly-rock — 2), `field` (large-field, tall-crops-field — 2), `water` (large-lake, deep-river, shallow-river, toxic-river, sludge-stream — 5), `pad` (crater, sludge-field, mine-field, death-marsh, quicksand — 5), `haze` (toxic-smog — 1). Ten meshes, 35 pieces. Each built-in's `MeshFile` names its group's file, so `TerrainMeshFile` returns the group path; the per-name fallback `res://content/builtin/terrain/<name>.glb` stays for a built-in whose `MeshFile` is null, which lets one piece be given bespoke art later without a code change.
- **Every mesh file is still optional, and a missing one still draws the hitbox.** `MeshLoader.LoadBuiltin` returns null when the file is absent and `TerrainNode` keeps its per-volume proxy visible (asset library → the proxy; setup phase → "the hitbox is the data and the mesh only decorates it"). That is the degraded path, not the plan: it is what happens for whichever of the fifteen files has not been made yet, and it is kept rather than deleted so a build with no `content/builtin/` at all still renders a playable table.
- Declarations follow the tables literally; conditional clauses ("dangerous when using Rush/Charge", "difficult for units moving up", "units on top may ignore one unit/terrain for line of sight") are `Notes`, never flags (geometry values → Declarations: "Everything else OPR does with terrain is free text").

Spec: setup phase → Terrain: imported, built-in, generated (built-ins ship with the client; generators are the rulebook's four tables, nine fixed plus 4D3 on 2D6; a batch of adds; land beside the table; the client rolls; the tables ship with the client and the server never reads them); Grimdark Future p.18–19 (the tables, read above); p.18 size classes (scatter 1–3", large 4–8", up to 12").

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class BuiltinContentTests
{
    [Fact]
    public void Every_generator_entry_names_a_built_in()
    {
        foreach (var planet in Enum.GetValues<Planet>())
        {
            Assert.All(TerrainGenerators.Fixed(planet), n => Assert.True(BuiltinContent.Terrain.ContainsKey(n), $"{planet}: {n}"));
            for (var roll = 2; roll <= 12; roll++)
                Assert.All(TerrainGenerators.Rolled(planet, roll), n => Assert.True(BuiltinContent.Terrain.ContainsKey(n), $"{planet} {roll}: {n}"));
        }
    }

    [Fact]
    public void Declarations_follow_the_rulebook_text()
    {
        Assert.Equal(TerrainDeclarations.Difficult | TerrainDeclarations.Cover | TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.SeeInAndOut, BuiltinContent.Terrain["forest"].Volume.Declarations);
        Assert.Equal(TerrainDeclarations.Impassable | TerrainDeclarations.BlocksLineOfSight, BuiltinContent.Terrain["large-building"].Volume.Declarations);
        Assert.Equal(TerrainDeclarations.Cover, BuiltinContent.Terrain["ruins"].Volume.Declarations);            // "dangerous when using Rush/Charge" is a note
        Assert.Contains("Rush/Charge", BuiltinContent.Terrain["ruins"].Volume.Notes);
        Assert.Equal(TerrainDeclarations.None, BuiltinContent.Terrain["quicksand"].Volume.Declarations);
        Assert.Equal(TerrainDeclarations.Difficult | TerrainDeclarations.Cover, BuiltinContent.Terrain["wall-or-fence"].Volume.Declarations);   // "Difficult when moving through it" is what Difficult means
        Assert.Equal(VolumeKind.Area, BuiltinContent.Terrain["deep-river"].Volume.Kind);
        Assert.Equal(TerrainDeclarations.Impassable, BuiltinContent.Terrain["deep-river"].Volume.Declarations);
    }

    [Fact]
    public void Samples_and_the_servers_default_render()
    {
        Assert.Contains("grass", BuiltinContent.Textures);                 // the game server's default draft texture
        Assert.Contains("desert", BuiltinContent.Textures);
        Assert.Contains("objective-ring", BuiltinContent.MarkerVisuals); Assert.Contains("chip", BuiltinContent.MarkerVisuals);
        Assert.True(BuiltinContent.Terrain.ContainsKey("hill-small")); Assert.True(BuiltinContent.Terrain.ContainsKey("ruin"));
        Assert.Equal("res://content/builtin/textures/grass.png", BuiltinContent.TextureFile("grass"));
        // Every piece names one of the ten group meshes, so the art set is ten files and not 35.
        Assert.Equal(10, BuiltinContent.TerrainMeshGroups.Count);
        Assert.All(BuiltinContent.Terrain.Values, t => Assert.Contains(t.MeshFile, BuiltinContent.TerrainMeshGroups.Select(g => $"res://content/builtin/terrain/{g}.glb")));
        Assert.Equal("res://content/builtin/terrain/hill.glb", BuiltinContent.TerrainMeshFile("steep-hill"));
        Assert.Equal("res://content/builtin/terrain/water.glb", BuiltinContent.TerrainMeshFile("sludge-stream"));
        Assert.Null(BuiltinContent.TerrainMeshFile("no-such-piece"));
    }

    [Fact]
    public void Placing_copies_the_volumes_with_fresh_ids()
    {
        var a = BuiltinContent.Place("forest", new Point3(10, 10, 0), new Yaw(0));
        var b = BuiltinContent.Place("forest", new Point3(20, 10, 0), new Yaw(0));
        Assert.NotEqual(a.Id, b.Id); Assert.NotEqual(a.Volumes[0].Id, b.Volumes[0].Id);
        Assert.Equal(BuiltinContent.Terrain["forest"].Volume, a.Volumes[0].Volume);
        Assert.Equal("forest", a.Mesh.Builtin);
        Assert.Single(BuiltinContent.VolumesOf("forest"));
    }
}
```

```csharp
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class TerrainGeneratorTests
{
    static readonly TableSize Opr = new(72, 48);

    [Theory] [InlineData(Planet.Generic)] [InlineData(Planet.Hospitable)] [InlineData(Planet.Wasteland)] [InlineData(Planet.Death)]
    public void Nine_fixed_plus_4d3_rolled_land_beside_the_slab(Planet planet)
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var batch = TerrainGenerators.Generate(planet, Opr, new Random(seed));
            Assert.InRange(batch.Count, 9 + 4, 9 + 12);
            Assert.All(batch, add => Assert.False(Table.ContainsPoint(Opr, add.Placement.Position.Horizontal)));
            Assert.All(batch, add => Assert.True(add.Placement.Position.X >= Opr.Width + 4));
            Assert.Equal(batch.Count, batch.Select(a => a.Placement.Id).Distinct().Count());
            Assert.All(batch, add => Assert.NotEmpty(add.Placement.Volumes));
        }
    }

    [Fact]
    public void Generic_fixed_pieces_are_the_rulebooks_nine()
    {
        var fixedNames = TerrainGenerators.Fixed(Planet.Generic);
        Assert.Equal(9, fixedNames.Count);
        Assert.Equal(2, fixedNames.Count(n => n == "barricade"));
        Assert.Equal(5, fixedNames.Count(n => n == "large-sight-blocker"));
        Assert.Equal(["large-hill"], TerrainGenerators.Rolled(Planet.Generic, 2));
        Assert.Equal(["large-hill"], TerrainGenerators.Rolled(Planet.Generic, 3));
        Assert.Equal(["barricade"], TerrainGenerators.Rolled(Planet.Generic, 7));
        Assert.Equal(["large-lake"], TerrainGenerators.Rolled(Planet.Generic, 12));
        Assert.Equal(["hill", "forest", "tall-crops-field"], TerrainGenerators.Rolled(Planet.Hospitable, 7));   // "Hill, Forest, or Field"
        Assert.Equal(["quicksand"], TerrainGenerators.Rolled(Planet.Death, 12));
    }

    [Fact]
    public void The_same_seed_gives_the_same_batch() =>
        Assert.Equal(TerrainGenerators.Generate(Planet.Wasteland, Opr, new Random(7)).Select(a => a.Placement.Mesh.Builtin),
                     TerrainGenerators.Generate(Planet.Wasteland, Opr, new Random(7)).Select(a => a.Placement.Mesh.Builtin));
}
```

- [ ] **Step 2: Run; expect compile errors. Write `BuiltinContent.cs`**

```csharp
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
    // A built-in names its group's mesh, not one of its own: ten meshes cover the 35 pieces, differing by footprint,
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
        T("large-sight-blocker", "Large Sight Blocker", V(Solid, Footprint.Rect(6, 6), 6, BlocksLineOfSight), "building"),
        T("large-hill", "Large Hill", V(Solid, Footprint.Circle(4), 2, TerrainDeclarations.Cover, OnTop), "hill"),
        T("large-ruins", "Large Ruins", V(Area, Footprint.Rect(6, 6), 4, TerrainDeclarations.Cover, RushCharge), "ruins"),
        T("large-lake", "Large Lake", V(Area, Footprint.Circle(4), 0.5, Impassable), "water"),
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
        T("small-dunes", "Group of Small Dunes", V(Solid, Footprint.Rect(6, 4), 1.5, TerrainDeclarations.Cover, OnTop), "hill"),
        T("large-rock", "Large Rock or Two to Three Small Rocks", V(Solid, Footprint.Rect(4, 4), 4, Impassable | BlocksLineOfSight), "rock"),
        T("crater", "Crater", V(Area, Footprint.Circle(3), 1, TerrainDeclarations.Cover), "pad"),
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
        T("hill-small", "Small Hill", V(Solid, Footprint.Rect(6, 6), 2, None), "hill"),
        T("ruin", "Ruin", V(Solid, Footprint.Rect(4, 4), 4, TerrainDeclarations.Cover | BlocksLineOfSight | SeeInAndOut), "ruins"),
    ];

    /// <summary>The ten mesh files the 35 built-ins share, by their basename under res://content/builtin/terrain/.</summary>
    public static IReadOnlyList<string> TerrainMeshGroups { get; } = ["hill", "woods", "ruins", "building", "wall", "rock", "field", "water", "pad", "haze"];

    public static IReadOnlyDictionary<string, BuiltinTerrain> Terrain { get; } = All.ToDictionary(t => t.Name);
    public static IReadOnlyList<string> Textures { get; } = ["grass", "desert", "urban", "snow", "ash"];
    public static IReadOnlyList<string> MarkerVisuals { get; } = ["objective-ring", "chip"];
    public static IReadOnlyList<string> ZoneStyleIds => ZoneStyles.All;

    public static string TextureFile(string name) => $"res://content/builtin/textures/{name}.png";
    public static string? TerrainMeshFile(string name) => Terrain.TryGetValue(name, out var t) ? t.MeshFile ?? $"res://content/builtin/terrain/{name}.glb" : null;

    public static IReadOnlyList<TerrainVolume> VolumesOf(string name) => [new TerrainVolume(Guid.CreateVersion7(), Terrain[name].Volume)];

    /// <summary>A placement of a built-in: the volumes are copied on add, so the draft is self-contained and the game server never reads `asset_volume` (`docs/schema.md` → A terrain placement names a built-in or an asset).</summary>
    public static TerrainPlacement Place(string name, Point3 position, Yaw yaw, double scale = 1) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin(name), position, yaw, scale, VolumesOf(name));
}
```

`Cover` is written `TerrainDeclarations.Cover` in the table: under `using static Tabletop.Geometry.TerrainDeclarations` the bare name is ambiguous.

- [ ] **Step 3: Write `TerrainGenerators.cs`**

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum Planet { Generic, Hospitable, Wasteland, Death }

/// <summary>Grimdark Future p.18–19: nine fixed pieces, then 4D3 more each rolled on 2D6. Runs on the client; the result is a batch of adds.</summary>
public static class TerrainGenerators
{
    static readonly Dictionary<Planet, string[]> FixedPieces = new()
    {
        [Planet.Generic] = ["barricade", "barricade", "large-forest", "large-field", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker"],
        [Planet.Hospitable] = ["wall-or-fence", "wall-or-fence", "hill", "plantation", "forest", "forest", "forest", "large-building", "large-building"],
        [Planet.Wasteland] = ["small-dunes", "small-dunes", "large-rock", "crater", "large-building", "large-building", "large-building", "toxic-smog", "toxic-smog"],
        [Planet.Death] = ["overgrown-ruins", "overgrown-ruins", "deadly-vegetation", "hill", "dense-vegetation", "dense-vegetation", "dense-vegetation", "steep-hill", "steep-hill"],
    };

    // 2D6 → the entry's alternatives, index 0 = a roll of 2.
    static readonly Dictionary<Planet, string[][]> Table = new()
    {
        [Planet.Generic] = [["large-hill"], ["large-hill"], ["large-ruins"], ["large-field"], ["barricade"], ["barricade"], ["barricade"], ["large-forest"], ["large-sight-blocker"], ["large-lake"], ["large-lake"]],
        [Planet.Hospitable] = [["deep-river"], ["shallow-river"], ["tall-crops-field"], ["ruins"], ["forest", "plantation"], ["hill", "forest", "tall-crops-field"], ["hill"], ["wall-or-fence"], ["large-building"], ["small-buildings"], ["steep-hill"]],
        [Planet.Wasteland] = [["toxic-river"], ["sludge-stream"], ["sludge-field"], ["ruins"], ["large-rock"], ["small-dunes"], ["crater"], ["toxic-smog"], ["large-building"], ["mine-field"], ["small-buildings"]],
        [Planet.Death] = [["death-marsh"], ["crumbly-rock"], ["overgrown-ruins"], ["deadly-vegetation"], ["dense-vegetation"], ["dense-vegetation", "hill"], ["hill"], ["small-buildings"], ["steep-hill"], ["beast-lair"], ["quicksand"]],
    };

    public static IReadOnlyList<string> Fixed(Planet planet) => FixedPieces[planet];
    public static IReadOnlyList<string> Rolled(Planet planet, int twoD6) => Table[planet][Math.Clamp(twoD6, 2, 12) - 2];

    /// <summary>The pieces land beside the long edge, off the slab, in rows 8" apart, four to a row; players drag them on.</summary>
    public static IReadOnlyList<AddTerrain> Generate(Planet planet, TableSize table, Random rng)
    {
        var names = new List<string>(Fixed(planet));
        var extra = Enumerable.Range(0, 4).Sum(_ => rng.Next(1, 4));                        // 4D3
        for (var i = 0; i < extra; i++)
        {
            var alternatives = Rolled(planet, rng.Next(1, 7) + rng.Next(1, 7));            // 2D6
            names.Add(alternatives[rng.Next(alternatives.Count)]);
        }
        return names.Select((name, i) => new AddTerrain(BuiltinContent.Place(name, new Point3(table.Width + 4 + (i % 4) * 8, 4 + (i / 4) * 8, 0), new Yaw(0)))).ToList();
    }
}
```

`Rolled` returns `IReadOnlyList<string>`, so the pick reads `alternatives.Count`.

- [ ] **Step 4: Author the fifteen art files.** **This step is not code, and no code in this plan produces its output.** It is a person with a modelling tool and an image editor making fifteen files and copying them into `client/content/builtin/`. Nothing else in the plan depends on it: every step before and after passes with the directory empty, and the table renders as its hitboxes until the files exist. A minimal art set ships so a generated table looks like a table; the minimum is these fifteen.

  **`client/content/builtin/terrain/{group}.glb` — ten meshes**, one per group above. Format glTF 2.0 binary, one mesh, one `BaseColor`-only PBR material with no texture (the material carries the colour; the slab owns the only image), no animation, no skin, no camera, no light, under 2000 triangles and under 256 KB each. **Authored in the unit box, in the table frame:** x and y in [−0.5, 0.5], z in [0, 1], z up, origin at the footprint centre and the base at z = 0 — `MeshLoader.LoadBuiltin` scales that box onto each placement's volume (Task 11), so one file serves a group with no per-piece variant. Each must read correctly at both extremes of its group: `water` is stretched from a 4"-radius lake to a 3"×12" river, `hill` from a 2"-radius mound to 6"×6", `pad` from 0.1" to 1" tall. Silhouette matters and detail does not, since the camera is a table-height orbit at 24"–96".
  - `hill` a smooth dome with a flat walkable crown; `woods` a ring of trunks with canopies above the crown, hollow in the middle so a base placed inside is visible (these carry `SeeInAndOut`); `ruins` two or three broken wall stubs on a rubble pad; `building` a closed box with a pitched roof and cut window and door reliefs; `wall` a straight low barrier along +x with posts; `rock` an angular boulder cluster; `field` a low mat of crop rows; `water` a flat surface inset just below z = 1 with a lipped bank around it; `pad` a shallow flat dish, which is the crater and every ground hazard; `haze` a soft cloud of two or three nested translucent shells.

  **`client/content/builtin/textures/{name}.png` — five tables**, `grass`, `desert`, `urban`, `snow`, `ash`, matching `BuiltinContent.Textures`. Seamless and tileable, 1024×1024, sRGB, no alpha, under 1.5 MB each. `SlabNode.Set` sets `Uv1Scale` from `width/12`, so one tile is a 12"×12" square of table: author them at that scale, not as a whole table.

  **What is not needed.** No mesh for a marker: `MarkerNode` builds its disc and `Label3D` procedurally, so `objective-ring` and `chip` are ids and not files. No mesh and no texture for a zone: `ZoneNode` builds flat `ArrayMesh` fans from `ZoneStyles.Resolve`. No mesh for a model: army models come from the player's own assets through the manifest and the cache.

  **These ship with the build and never enter the asset cache.** They are `res://` resources bundled into the export's `.pck`, read by `GltfDocument`/`ResourceLoader` from the local file (Task 11), with no hash, no manifest entry, no presigned URL and no `user://cache/` write. The content-addressed cache is only for a player's uploaded assets, which arrive over HTTP and are keyed by SHA-256. Adding a built-in therefore means shipping a new client build, which is the trade the ruling accepts.

- [ ] **Step 5: Run `dotnet test tests/Tabletop.Client.Tests`; expect 52 passed. Commit: `Ship the built-in terrain inventory and the rulebook's four terrain generators`.** The art files, if Step 4 has produced them, are a separate commit: `Add the built-in terrain meshes and table textures`.
