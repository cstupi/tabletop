using System.Text.Json;
using Tabletop.Geometry;

namespace Tabletop.Content.Tests;

public class TableStateTests
{
    public static TableState Sample()
    {
        var hill = new TerrainVolume(Guid.CreateVersion7(),
            new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.None, null));
        var placement = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill-small"), new Point3(12, 12, 0), new Yaw(45), 1.0, [hill]);
        var objective = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(36, 24, 0), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"));
        return new TableState(Guid.CreateVersion7(), "Sample", new TableSize(72, 48), VisualRef.OfBuiltin("grass"),
            [placement], [objective],
            [new DeploymentZone(Guid.CreateVersion7(), "frontline/1", 1), new DeploymentZone(Guid.CreateVersion7(), "frontline/2", 2)]);
    }

    [Fact]
    public void VisualRef_is_exactly_one_of_builtin_or_asset()
    {
        Assert.True(VisualRef.OfBuiltin("grass").IsBuiltin);
        Assert.False(VisualRef.OfAsset(Guid.CreateVersion7()).IsBuiltin);
        Assert.Throws<ArgumentException>(() => new VisualRef("grass", Guid.CreateVersion7()));
        Assert.Throws<ArgumentException>(() => new VisualRef(null, null));
    }

    [Fact]
    public void Placement_projects_its_volumes_into_the_world()
    {
        var placement = Sample().Terrain[0];
        var world = placement.WorldVolumes();
        Assert.Single(world);
        Assert.Equal(placement.Volumes[0].Id, world[0].Id);
        Assert.Equal(new TerrainTransform(placement.Position, placement.Yaw, placement.Scale), world[0].Transform);
    }

    [Fact]
    public void TableState_round_trips_through_json()
    {
        var json = ContentJson.Serialize(Sample());
        var back = ContentJson.Deserialize<TableState>(json);
        Assert.Equal(json, ContentJson.Serialize(back));
        Assert.Contains("\"kind\":\"solid\"", json);
        Assert.Contains("\"declarations\":\"none\"", json);
        // Derived properties stay off the wire: System.Text.Json writes every get-only property unless it is [JsonIgnore].
        foreach (var derived in new[] { "\"horizontal\"", "\"isBuiltin\"", "\"isValid\"", "\"transform\"" }) Assert.DoesNotContain(derived, json);
    }

    [Fact]
    public void VisualRef_rejects_neither_or_both_at_the_parse_boundary()
    {
        Assert.Throws<JsonException>(() => ContentJson.Deserialize<VisualRef>("{}"));
        Assert.Throws<JsonException>(() => ContentJson.Deserialize<VisualRef>($"{{\"builtin\":\"grass\",\"assetId\":\"{Guid.CreateVersion7()}\"}}"));
        Assert.Equal(VisualRef.OfBuiltin("grass"), ContentJson.Deserialize<VisualRef>("{\"builtin\":\"grass\"}"));
    }

    [Fact]
    public void An_absent_visual_is_the_invalid_default_the_server_checks()
    {
        // The property is missing altogether, so no converter runs and Marker's constructor receives default(VisualRef).
        var json = $"{{\"id\":\"{Guid.CreateVersion7()}\",\"kind\":\"objective\",\"position\":{{\"x\":0,\"y\":0,\"z\":0}},\"yaw\":{{\"degrees\":0}},\"diameter\":3}}";
        Assert.False(ContentJson.Deserialize<Marker>(json).Visual.IsValid);
        Assert.True(VisualRef.OfBuiltin("grass").IsValid);
    }

    [Fact]
    public void Terrain_declarations_are_a_flags_string()
    {
        // The [Flags] wire form the web app parses and formats: "none", one name, or names joined by ", " in ascending flag order.
        Assert.Equal("\"none\"", ContentJson.Serialize(TerrainDeclarations.None));
        Assert.Equal("\"impassable, cover\"", ContentJson.Serialize(TerrainDeclarations.Cover | TerrainDeclarations.Impassable));
        Assert.Equal(TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, ContentJson.Deserialize<TerrainDeclarations>("\"blocks_line_of_sight, cover\""));
    }

    [Fact]
    public void Army_round_trips_through_json()
    {
        var json = ContentJson.Serialize(ArmyTests.Sample());
        Assert.Equal(json, ContentJson.Serialize(ContentJson.Deserialize<Army>(json)));
    }
}
