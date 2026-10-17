namespace Tabletop.Geometry;

/// <summary>Position and yaw. For a model, Z is the settled height the server computed.</summary>
public readonly record struct Placement(Point3 Position, Yaw Yaw);

/// <summary>A terrain piece's transform: position, yaw and a uniform scale.</summary>
public readonly record struct TerrainTransform(Point3 Position, Yaw Yaw, double Scale);

/// <summary>A model body standing somewhere.</summary>
public readonly record struct PlacedModel(ModelBody Body, Placement Placement);

/// <summary>A volume placed in the world. Id is the terrain_volume id the move Ask names as impassable.</summary>
public sealed record WorldVolume(Guid Id, Volume Volume, TerrainTransform Transform);

/// <summary>A sight blocker that is no terrain volume: a solid prism over a convex outline (world coordinates) between two heights. A unit's
/// perimeter is one (Bodies.Perimeter). It blocks every line through it, as a BlocksLineOfSight Solid volume does.</summary>
public sealed record SightPrism(Guid Id, IReadOnlyList<Point2> Outline, double Bottom, double Top);
