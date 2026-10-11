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
        // Every piece names one of the eleven group meshes, so the art set is eleven files and not 35.
        Assert.Equal(11, BuiltinContent.TerrainMeshGroups.Count);
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
        Assert.EndsWith("hill-rect.glb", BuiltinContent.TerrainMeshFile("small-dunes"));
        Assert.Contains("hill-rect", BuiltinContent.TerrainMeshGroups);
    }
}
