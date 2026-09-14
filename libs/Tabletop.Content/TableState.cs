using System.Text.Json.Serialization;
using Tabletop.Geometry;

namespace Tabletop.Content;

/// <summary>A built-in name or a library asset, exactly one (schema.md → Built-in or asset, exactly one).</summary>
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
