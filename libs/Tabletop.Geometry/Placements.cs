namespace Tabletop.Geometry;

/// <summary>Position and yaw. For a model, Z is the settled height the server computed.</summary>
public readonly record struct Placement(Point3 Position, Yaw Yaw);

/// <summary>A terrain piece's transform: position, yaw and a uniform scale.</summary>
public readonly record struct TerrainTransform(Point3 Position, Yaw Yaw, double Scale);

/// <summary>A model body standing somewhere.</summary>
public readonly record struct PlacedModel(ModelBody Body, Placement Placement);

/// <summary>A volume placed in the world. Id is the terrain_volume id the move Ask names as impassable.</summary>
public sealed record WorldVolume(Guid Id, Volume Volume, TerrainTransform Transform);
