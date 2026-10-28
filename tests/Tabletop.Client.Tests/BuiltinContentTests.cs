using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class BuiltinContentTests
{
    [Fact]
    public void Every_builtin_solid_over_three_inches_is_impassable() =>   // p.11 elevated terrain
        Assert.All(BuiltinContent.Terrain.Values.Select(t => t.Volume).Where(v => v.Kind == VolumeKind.Solid && v.Top > 3),
            v => Assert.True(v.Declarations.HasFlag(TerrainDeclarations.Impassable)));

    [Fact]
    public void The_ruin_is_walk_in_difficult_ground_seen_into_and_out_of() =>   // owner ruling: passable, difficult, not seen through
        Assert.Equal((VolumeKind.Area, TerrainDeclarations.Difficult | TerrainDeclarations.Cover | TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.SeeInAndOut),
            (BuiltinContent.Terrain["ruin"].Volume.Kind, BuiltinContent.Terrain["ruin"].Volume.Declarations));

    [Fact]
    public void Declarations_follow_the_rulebook_text()
    {
        Assert.Equal(TerrainDeclarations.Difficult | TerrainDeclarations.Cover | TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.SeeInAndOut, BuiltinContent.Terrain["forest"].Volume.Declarations);
        Assert.Equal(TerrainDeclarations.Impassable | TerrainDeclarations.BlocksLineOfSight, BuiltinContent.Terrain["large-building"].Volume.Declarations);
        // p.18/p.19 "Ruins or Debris: Counts as Cover terrain, as well as Dangerous terrain when using Rush/Charge actions."
        foreach (var ruins in new[] { "ruins", "large-ruins" })
        {
            Assert.Equal((VolumeKind.Area, TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge),
                (BuiltinContent.Terrain[ruins].Volume.Kind, BuiltinContent.Terrain[ruins].Volume.Declarations));
            Assert.Null(BuiltinContent.Terrain[ruins].Volume.Notes);
        }
        // p.19 "Quicksand: Counts as Dangerous terrain when using Rush/Charge actions."
        Assert.Equal(TerrainDeclarations.DangerousOnRushCharge, BuiltinContent.Terrain["quicksand"].Volume.Declarations);
        Assert.Equal(TerrainDeclarations.Difficult | TerrainDeclarations.Cover, BuiltinContent.Terrain["wall-or-fence"].Volume.Declarations);   // "Difficult when moving through it" is what Difficult means
        Assert.Equal(VolumeKind.Area, BuiltinContent.Terrain["deep-river"].Volume.Kind);
        Assert.Equal(TerrainDeclarations.Impassable, BuiltinContent.Terrain["deep-river"].Volume.Declarations);
        // The ruins pack's own pieces. p.12 "Ruins - Cover + Dangerous when using rush/charge", as the generators' ruins.
        Assert.Equal((VolumeKind.Area, TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge),
            (BuiltinContent.Terrain["ruined-corner"].Volume.Kind, BuiltinContent.Terrain["ruined-corner"].Volume.Declarations));
        // p.11 Blocking terrain "Examples: Walls, Buildings, Rocks"; Elevated terrain "over 3" tall ... are impassable".
        Assert.Equal((VolumeKind.Solid, TerrainDeclarations.Impassable | TerrainDeclarations.BlocksLineOfSight),
            (BuiltinContent.Terrain["ruined-wall"].Volume.Kind, BuiltinContent.Terrain["ruined-wall"].Volume.Declarations));
        // p.12 "Rubble - Difficult".
        Assert.Equal((VolumeKind.Area, TerrainDeclarations.Difficult),
            (BuiltinContent.Terrain["rubble-field"].Volume.Kind, BuiltinContent.Terrain["rubble-field"].Volume.Declarations));
    }

    [Fact]
    public void Samples_and_the_servers_default_render()
    {
        Assert.Contains("grass", BuiltinContent.Textures);                 // the game server's default draft texture
        Assert.Contains("desert", BuiltinContent.Textures);
        Assert.Equal(["objective-ring"], BuiltinContent.MarkerVisuals);
        Assert.True(BuiltinContent.Terrain.ContainsKey("hill-small")); Assert.True(BuiltinContent.Terrain.ContainsKey("ruin"));
        Assert.Equal("res://content/builtin/textures/grass.png", BuiltinContent.TextureFile("grass"));
        // Every piece names one of the catalog's meshes: 33 files for 38 pieces, shared only by the same terrain at other sizes.
        Assert.Equal(33, BuiltinContent.TerrainMeshGroups.Count);
        Assert.All(BuiltinContent.Terrain.Values, t => Assert.Contains(BuiltinContent.TerrainMeshFile(t.Name), BuiltinContent.TerrainMeshGroups.Select(g => $"res://content/builtin/terrain/{g}.glb")));
        Assert.Equal("res://content/builtin/terrain/hill.glb", BuiltinContent.TerrainMeshFile("steep-hill"));
        Assert.Equal("res://content/builtin/terrain/sludge-canal.glb", BuiltinContent.TerrainMeshFile("sludge-stream"));
        Assert.Equal("res://content/builtin/terrain/death-marsh.glb", BuiltinContent.TerrainMeshFile("death-marsh"));
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

    /// <summary>The packed catalog names exactly the files the build ships, and the generator writes every mesh it names.</summary>
    [Fact]
    public void The_packed_catalog_names_the_shipped_files()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client/content/builtin"));
        Assert.Equal(BuiltinContent.Packed.Meshes.Order(), Directory.GetFiles(Path.Combine(root, "terrain"), "*.glb").Select(Path.GetFileNameWithoutExtension).Order());
        Assert.Equal(BuiltinContent.Packed.Textures.Order(), Directory.GetFiles(Path.Combine(root, "textures"), "*.png").Select(Path.GetFileNameWithoutExtension).Order());
        Assert.Null(BuiltinContent.Packed.Version);
        Assert.Empty(BuiltinContent.Packed.Files);
        Assert.Equal(38, BuiltinContent.Packed.Terrain.Count);
    }

    /// <summary>Each shipped mesh's POSITION bounds, read under BuiltinContent.MeshOrientation in the Orientation convention
    /// (pitch about table X, then roll about Y, then yaw about Z), land in the unit box: x and y in [−0.5, 0.5], z in [0, 1].</summary>
    [Fact]
    public void Built_in_meshes_stand_in_the_unit_box_under_their_orientation()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client/content/builtin/terrain"));
        var files = Directory.GetFiles(dir, "*.glb");
        Assert.NotEmpty(files);
        var o = BuiltinContent.MeshOrientation;
        foreach (var file in files)
        {
            var (min, max) = PositionBounds(file);
            var corners = from x in new[] { min[0], max[0] } from y in new[] { min[1], max[1] } from z in new[] { min[2], max[2] } select Rotate(x, y, z, o);
            var c = corners.ToList();
            const double e = 1e-3;
            Assert.True(c.Min(p => p.Z) >= -e && c.Max(p => p.Z) <= 1 + e && c.Max(p => p.Z) > 0.05, $"{Path.GetFileName(file)}: z in [{c.Min(p => p.Z):0.###}, {c.Max(p => p.Z):0.###}]");
            Assert.True(c.All(p => Math.Abs(p.X) <= 0.5 + e && Math.Abs(p.Y) <= 0.5 + e), $"{Path.GetFileName(file)}: footprint outside the unit square");
        }
    }

    /// <summary>MeshLoader reads a built-in mesh raw (FileAccess, GltfDocument.AppendFromFile), not through ResourceLoader. An
    /// export ships an imported file only as its import, so each mesh must be imported as Keep File to reach the .pck as-is.</summary>
    [Fact]
    public void Every_built_in_mesh_ships_raw_in_the_export()
    {
        var client = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client"));
        foreach (var file in BuiltinContent.Packed.Meshes.Select(m => BuiltinContent.Packed.FileFor(BuiltinCatalog.MeshPath(m), null)))
        {
            var path = Path.Combine(client, file["res://".Length..]);
            Assert.True(File.Exists(path), $"{file} is missing");
            Assert.True(File.ReadAllText(path + ".import").Contains("importer=\"keep\""), $"{file}.import is not Keep File");
        }
    }

    /// <summary>A print's alpha is its height (PrintTextures), so its import must leave the colour under a low height alone (no
    /// alpha-border fix) and stay lossless, so the client can read the pixels back.</summary>
    [Fact]
    public void Every_packed_print_imports_lossless_with_its_alpha_as_data()
    {
        var client = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client"));
        var prints = BuiltinContent.Packed.Textures.Select(t => Path.Combine(client, "content", "builtin", BuiltinCatalog.TexturePath(t)))
            .Concat(Directory.GetFiles(Path.Combine(client, "content", "table"), "*.png"));
        foreach (var path in prints)
        {
            var import = File.ReadAllText(path + ".import");
            Assert.True(import.Contains("compress/mode=0") && import.Contains("process/fix_alpha_border=false") && import.Contains("detect_3d/compress_to=0"),
                $"{Path.GetFileName(path)}.import is not lossless with its alpha kept as data");
        }
    }

    static (double X, double Y, double Z) Rotate(double x, double y, double z, Orientation o)
    {
        double R(double d) => d * Math.PI / 180;
        var (cp, sp, cr, sr, cy, sy) = (Math.Cos(R(o.Pitch)), Math.Sin(R(o.Pitch)), Math.Cos(R(o.Roll)), Math.Sin(R(o.Roll)), Math.Cos(R(o.Yaw)), Math.Sin(R(o.Yaw)));
        (y, z) = (y * cp - z * sp, y * sp + z * cp);
        (x, z) = (x * cr + z * sr, -x * sr + z * cr);
        (x, y) = (x * cy - y * sy, x * sy + y * cy);
        return (x, y, z);
    }

    static (double[] Min, double[] Max) PositionBounds(string glb)
    {
        var bytes = File.ReadAllBytes(glb);
        var length = BitConverter.ToInt32(bytes, 12);
        using var json = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(20, length));
        var accessors = json.RootElement.GetProperty("accessors");
        double[] min = [double.MaxValue, double.MaxValue, double.MaxValue], max = [double.MinValue, double.MinValue, double.MinValue];
        foreach (var mesh in json.RootElement.GetProperty("meshes").EnumerateArray())
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                var a = accessors[prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                for (var i = 0; i < 3; i++) { min[i] = Math.Min(min[i], a.GetProperty("min")[i].GetDouble()); max[i] = Math.Max(max[i], a.GetProperty("max")[i].GetDouble()); }
            }
        return (min, max);
    }

    [Fact]
    public void Rect_hills_draw_the_square_plateau()
    {
        Assert.EndsWith("hill-rect.glb", BuiltinContent.TerrainMeshFile("hill-small"));
        Assert.EndsWith("dunes.glb", BuiltinContent.TerrainMeshFile("small-dunes"));
        Assert.Contains("hill-rect", BuiltinContent.TerrainMeshGroups);
    }
}
