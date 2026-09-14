using System.Text.Json;
using System.Text.Json.Serialization;
using Tabletop.Geometry;

namespace Tabletop.Content;

/// <summary>A built-in name or a library asset, exactly one (schema.md → Built-in or asset, exactly one).</summary>
[JsonConverter(typeof(VisualRefJsonConverter))]
public readonly record struct VisualRef
{
    public string? Builtin { get; }
    public Guid? AssetId { get; }

    [JsonConstructor]
    public VisualRef(string? builtin, Guid? assetId)
    {
        if ((builtin is null) == (assetId is null))
            throw new ArgumentException("Exactly one of builtin or assetId must be set.");
        Builtin = builtin;
        AssetId = assetId;
    }

    /// <summary>Derived, never serialised.</summary>
    [JsonIgnore] public bool IsBuiltin => Builtin is not null;
    /// <summary>False only for default(VisualRef): a property the JSON left out never reaches the converter, so the parent's
    /// constructor receives the default. Servers check this on every inbound edit or layout before writing a row.</summary>
    [JsonIgnore] public bool IsValid => (Builtin is null) != (AssetId is null);
    public static VisualRef OfBuiltin(string name) => new(name, null);
    public static VisualRef OfAsset(Guid assetId) => new(null, assetId);
}

/// <summary>A volume row on a placement. Always present: copied from the asset or built-in on add, editable in place.</summary>
public sealed record TerrainVolume(Guid Id, Volume Volume);

public sealed record TerrainPlacement(Guid Id, VisualRef Mesh, Point3 Position, Yaw Yaw, double Scale, IReadOnlyList<TerrainVolume> Volumes)
{
    /// <summary>Derived, never serialised.</summary>
    [JsonIgnore] public TerrainTransform Transform => new(Position, Yaw, Scale);
    public IReadOnlyList<WorldVolume> WorldVolumes() => Volumes.Select(v => new WorldVolume(v.Id, v.Volume, Transform)).ToList();
}

public enum MarkerKind { Objective, Token }

/// <summary>Objectives and tokens are one record. Markers ignore terrain.</summary>
public sealed record Marker(Guid Id, MarkerKind Kind, Point3 Position, Yaw Yaw, double Diameter, string? Label, VisualRef Visual);

/// <summary>A built-in zone shape and the team ordinal it belongs to. Shape ids are namespaced by style, e.g. "frontline/1" — the ids Tabletop.Geometry.ZoneStyles.ShapesOf(style) lists (geometry plan Task 13); one row per shape, keyed by team ordinal 1..n.</summary>
public sealed record DeploymentZone(Guid Id, string Shape, int TeamOrdinal);

/// <summary>The setup phase's draft and result, and a saved layout: one shape, two owners.</summary>
public sealed record TableState(
    Guid Id, string? Name, TableSize Size, VisualRef Texture,
    IReadOnlyList<TerrainPlacement> Terrain, IReadOnlyList<Marker> Markers, IReadOnlyList<DeploymentZone> Zones);

/// <summary>
/// Reads and writes a VisualRef as <c>{"builtin": …}</c> or <c>{"assetId": …}</c>, the same shape the naming policy produced before,
/// and turns an object with neither or both members into a JsonException at the parse boundary instead of an ArgumentException
/// out of the constructor. The property names are written literally in camelCase because a converter bypasses the naming policy.
/// </summary>
public sealed class VisualRefJsonConverter : JsonConverter<VisualRef>
{
    public override VisualRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A VisualRef is an object.");
        string? builtin = null;
        Guid? assetId = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a property name in a VisualRef.");
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "builtin": builtin = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                case "assetId": assetId = reader.TokenType == JsonTokenType.Null ? null : reader.GetGuid(); break;
                default: reader.Skip(); break;
            }
        }
        try { return new VisualRef(builtin, assetId); }
        catch (ArgumentException e) { throw new JsonException(e.Message, e); }
    }

    public override void Write(Utf8JsonWriter writer, VisualRef value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Builtin is not null) writer.WriteString("builtin", value.Builtin);
        if (value.AssetId is not null) writer.WriteString("assetId", value.AssetId.Value);
        writer.WriteEndObject();
    }
}
