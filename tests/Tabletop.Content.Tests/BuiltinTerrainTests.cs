using System.Text.Json;
using System.Text.Json.Nodes;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Content.Tests;

public class BuiltinTerrainTests
{
    static JsonObject Catalog() => JsonNode.Parse("""
        {
          "meshes": ["hill"],
          "terrain": [
            { "id": "hill", "label": "Hill", "mesh": "hill", "kind": "solid", "footprint": { "kind": "circle", "a": 3 }, "top": 2, "declarations": ["cover"], "notes": "On top" },
            { "id": "pond", "label": "Pond", "mesh": "hill", "kind": "area", "footprint": { "kind": "rect", "a": 4, "b": 6 }, "top": 0.5, "declarations": ["impassable", "dangerous"] }
          ]
        }
        """)!.AsObject();

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
    };

    [Theory, MemberData(nameof(Faults))]
    public void A_faulty_entry_is_refused_naming_the_fault(string fault, Action<JsonObject> spoil)
    {
        var j = Catalog(); spoil(j);
        var terrain = JsonDocument.Parse(j["terrain"]!.ToJsonString()).RootElement;
        Assert.Contains(fault, Assert.Throws<FormatException>(() => BuiltinTerrain.Parse(terrain, ["hill"])).Message);
    }

    [Fact]
    public void Steep_is_the_eighth_declaration_and_a_catalog_entry_may_carry_it()
    {
        // 3.6.0 p.12 Steep; 3.5.1 p.12 Hills "Difficult when moving up".
        var flags = Enum.GetValues<TerrainDeclarations>().Where(f => f != TerrainDeclarations.None).ToList();
        Assert.Equal(8, flags.Count);
        Assert.Equal(flags.Order(), BuiltinTerrain.DeclarationNames.Values.Order());
        Assert.Equal(TerrainDeclarations.Steep, BuiltinTerrain.DeclarationNames["steep"]);
        var j = Catalog(); j["terrain"]![0]!["declarations"] = new JsonArray("cover", "steep");
        var terrain = BuiltinTerrain.Parse(JsonDocument.Parse(j["terrain"]!.ToJsonString()).RootElement, ["hill"]);
        Assert.Equal(TerrainDeclarations.Cover | TerrainDeclarations.Steep, terrain["hill"].Volume.Declarations);
    }

    [Fact]
    public void The_repo_catalog_parses()
    {
        var b = RepoCatalog.Terrain["barricade"];
        Assert.Equal(new Volume(VolumeKind.Solid, Footprint.Rect(3, 1), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Cover | TerrainDeclarations.Difficult, null), b.Volume);
    }
}
