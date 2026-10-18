using System.Text.Json.Nodes;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class BuiltinCatalogTests
{
    internal static string Hash(char c) => new(c, 64);

    /// <summary>A small catalog as published (version and files) or packed (neither).</summary>
    internal static JsonObject Catalog(string? version = "1.3.0", char meshHash = 'a', char textureHash = 'b', string? url = null) => JsonNode.Parse($$"""
        {
          {{(version is null ? "" : $"\"version\": \"{version}\",")}}
          "meshes": ["hill"],
          "textures": ["grass"],
          "terrain": [
            { "id": "hill", "label": "Hill", "mesh": "hill", "kind": "solid", "footprint": { "kind": "circle", "a": 3 }, "top": 2, "declarations": ["cover"], "notes": "On top" },
            { "id": "pond", "label": "Pond", "mesh": "hill", "kind": "area", "footprint": { "kind": "rect", "a": 4, "b": 6 }, "top": 0.5, "declarations": ["impassable", "dangerous"] }
          ]
          {{(version is null ? "" : $$"""
          , "files": {
            "terrain/hill.glb": { "sha256": "{{Hash(meshHash)}}", "size": 10{{(url is null ? "" : $", \"url\": \"{url}/m\"")}} },
            "textures/grass.png": { "sha256": "{{Hash(textureHash)}}", "size": 20{{(url is null ? "" : $", \"url\": \"{url}/t\"")}} }
          }
          """)}}
        }
        """)!.AsObject();

    [Fact]
    public void Parses_terrain_into_volumes_and_files_by_path()
    {
        var c = BuiltinCatalog.Parse(Catalog(url: "http://blobs").ToJsonString());
        Assert.Equal("1.3.0", c.Version);
        Assert.Equal(new Volume(VolumeKind.Solid, Footprint.Circle(3), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.Cover, "On top"), c.Terrain["hill"].Volume);
        Assert.Equal(new Volume(VolumeKind.Area, Footprint.Rect(4, 6), new Point2(0, 0), new Yaw(0), 0, 0.5, TerrainDeclarations.Impassable | TerrainDeclarations.Dangerous, null), c.Terrain["pond"].Volume);
        Assert.Equal(("Pond", "hill"), (c.Terrain["pond"].Label, c.Terrain["pond"].Mesh));
        Assert.Equal(new BuiltinFile(Hash('a'), 10, new Uri("http://blobs/m")), c.Files["terrain/hill.glb"]);
        Assert.Equal([("terrain/hill.glb", AssetCache.MeshCap), ("textures/grass.png", AssetCache.TextureCap)], c.Downloads.Select(d => (d.Path, d.Cap)));
    }

    [Fact]
    public void The_saved_form_drops_urls_and_reads_back_the_same()
    {
        var c = BuiltinCatalog.Parse(Catalog(url: "http://blobs").ToJsonString());
        var json = c.ToJson();
        Assert.DoesNotContain("http://blobs", json);
        var back = BuiltinCatalog.Parse(json);
        Assert.Equal(c.Version, back.Version);
        Assert.Equal(c.Terrain, back.Terrain);
        Assert.Equal(new BuiltinFile(Hash('a'), 10), back.Files["terrain/hill.glb"]);
    }

    [Fact]
    public void A_packed_catalog_has_no_version_and_resolves_to_res_paths()
    {
        var c = BuiltinCatalog.Parse(Catalog(version: null).ToJsonString());
        Assert.Null(c.Version);
        Assert.Empty(c.Files);
        Assert.Equal("res://content/builtin/terrain/hill.glb", c.FileFor("terrain/hill.glb", null));
    }

    [Fact]
    public void A_listed_file_resolves_to_the_cache_once_held_and_to_the_packed_file_before()
    {
        var dir = Directory.CreateTempSubdirectory("tabletop-builtin").FullName;
        try
        {
            var cache = new AssetCache(dir, new HttpClient());
            var c = BuiltinCatalog.Parse(Catalog().ToJsonString());
            Assert.Equal("res://content/builtin/terrain/hill.glb", c.FileFor("terrain/hill.glb", cache));
            File.WriteAllBytes(cache.PathFor(Hash('a')), [1]);
            Assert.Equal(cache.PathFor(Hash('a')), c.FileFor("terrain/hill.glb", cache));
            Assert.Equal("res://content/builtin/textures/grass.png", c.FileFor("textures/grass.png", cache));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Placing_copies_the_catalogs_volume()
    {
        var c = BuiltinCatalog.Parse(Catalog().ToJsonString());
        var p = c.Place("pond", new Point3(1, 2, 0), new Yaw(0));
        Assert.Equal("pond", p.Mesh.Builtin);
        Assert.Equal(c.Terrain["pond"].Volume, Assert.Single(p.Volumes).Volume);
    }

    public static TheoryData<string, Action<JsonObject>> Faults => new()
    {
        { "names mesh 'tower'", j => j["terrain"]![0]!["mesh"] = "tower" },
        { "listed twice", j => j["terrain"]![1]!["id"] = "hill" },
        { "not solid or area", j => j["terrain"]![0]!["kind"] = "lava" },
        { "not a terrain declaration", j => j["terrain"]![0]!["declarations"] = new JsonArray("cover", "slippery") },
        { "footprint", j => j["terrain"]![0]!["footprint"]!["a"] = 0 },
        { "has top", j => j["terrain"]![0]!["top"] = -1 },
        { "no label", j => j["terrain"]![0]!["label"] = new string('x', 65) },
        { "notes over 500", j => j["terrain"]![0]!["notes"] = new string('x', 501) },
        { "not a name", j => j["meshes"] = new JsonArray("../hill") },
        { "no sha256 for textures/grass.png", j => j["files"]!.AsObject().Remove("textures/grass.png") },
        { "no sha256 for terrain/hill.glb", j => j["files"]!["terrain/hill.glb"]!["sha256"] = "ABC" },
        { "cap", j => j["files"]!["textures/grass.png"]!["size"] = AssetCache.TextureCap + 1 },
        { "names no version", j => j.Remove("version") },
        { "not JSON", j => j["terrain"] = "x" },
    };

    [Theory, MemberData(nameof(Faults))]
    public void A_faulty_catalog_is_refused_naming_the_fault(string fault, Action<JsonObject> spoil)
    {
        var j = Catalog(); spoil(j);
        Assert.Contains(fault, Assert.Throws<FormatException>(() => BuiltinCatalog.Parse(j.ToJsonString())).Message);
    }
}
