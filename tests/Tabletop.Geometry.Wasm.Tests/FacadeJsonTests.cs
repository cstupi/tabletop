using System.Text.Json;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Geometry.Wasm.Tests;

public class FacadeJsonTests
{
    static Volume Rect(VolumeKind kind, TerrainDeclarations d) => new(kind, Footprint.Rect(4, 4), new Point2(0, 0), new Yaw(0), 0, 3, d, null);

    [Fact]
    public void Reads_what_ContentJson_writes()
    {
        var placement = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruin"), new Point3(10, 12, 0), new Yaw(30), 1.5,
        [
            new TerrainVolume(Guid.CreateVersion7(), Rect(VolumeKind.Solid, TerrainDeclarations.Impassable | TerrainDeclarations.BlocksLineOfSight)),
            new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Circle(2), new Point2(1, 1), new Yaw(0), 0, 1, TerrainDeclarations.Cover, "note")),
        ]);
        var json = ContentJson.Serialize(placement);
        var piece = JsonSerializer.Deserialize<Piece>(json, FacadeJson.Options)!;
        Assert.Equal(placement.WorldVolumes(), piece.WorldVolumes());
    }

    [Fact]
    public void Writes_enums_as_ContentJson_does()
    {
        Assert.Contains("\"kind\":\"in_impassable\"", JsonSerializer.Serialize(new ContactFound(Guid.Empty, ContactKind.InImpassable, null), FacadeJson.Options));
        var b = Base.Oval(2, 1);
        Assert.Equal(ContentJson.Serialize(b), JsonSerializer.Serialize(b, FacadeJson.Options));
        Assert.Equal(b, JsonSerializer.Deserialize<Base>(ContentJson.Serialize(b), FacadeJson.Options));
    }

    [Fact]
    public void Rejects_an_integer_enum() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Base>("{\"shape\":0,\"a\":1,\"b\":1}", FacadeJson.Options));
}
