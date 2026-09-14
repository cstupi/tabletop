namespace Tabletop.Geometry;

/// <summary>Edge-to-edge between bases, never centre-to-centre. Horizontal, in inches; 0 when the outlines overlap.</summary>
public static class Distance
{
    public static double EdgeToEdge(PlacedModel a, PlacedModel b) => throw new NotImplementedException("Implemented by the geometry plan.");
    public static double EdgeToEdge(PlacedModel a, Point2 point) => throw new NotImplementedException("Implemented by the geometry plan.");
}

/// <summary>Which volumes obstruct the line. Empty when clear.</summary>
public sealed record LineOfSightResult(bool Clear, IReadOnlyList<Guid> ObstructedBy);

/// <summary>
/// Line of sight between two base-and-height bodies against placed volumes. Only volumes declaring
/// BlocksLineOfSight obstruct; a SeeInAndOut volume obstructs a line passing through it but not one
/// that starts or ends inside it. The mesh is never consulted.
/// </summary>
public static class LineOfSight
{
    public static LineOfSightResult Check(PlacedModel from, PlacedModel to, IReadOnlyList<WorldVolume> volumes) =>
        throw new NotImplementedException("Implemented by the geometry plan.");
}

/// <summary>
/// Raycast down: the highest Solid top under the point, or 0 (the slab) when nothing is. The geometry plan adds a
/// <c>SurfaceHeightAt(Point3 from, …)</c> overload that drops from a height — the highest Solid top at or below it — so a model
/// dragged along the ground passes under a second storey; the server uses the client's <c>To.Z</c> as the drop height.
/// </summary>
public static class Settling
{
    public static double SurfaceHeightAt(Point2 point, IReadOnlyList<WorldVolume> volumes) =>
        throw new NotImplementedException("Implemented by the geometry plan.");
}

/// <summary>Ids of the volumes whose footprint and vertical range contain the model's base centre (Area or Solid).</summary>
public static class Containment
{
    public static IReadOnlyList<Guid> VolumesContaining(PlacedModel model, IReadOnlyList<WorldVolume> volumes) =>
        throw new NotImplementedException("Implemented by the geometry plan.");
}

/// <summary>Flagged, never prevented: base overlap and terrain intersection (architecture.md → Rules enforcement).</summary>
public static class Overlap
{
    public static bool Bases(PlacedModel a, PlacedModel b) => throw new NotImplementedException("Implemented by the geometry plan.");
    public static bool WithSolid(PlacedModel model, WorldVolume volume) => throw new NotImplementedException("Implemented by the geometry plan.");
}
