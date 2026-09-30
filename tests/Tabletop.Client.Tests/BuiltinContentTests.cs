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
