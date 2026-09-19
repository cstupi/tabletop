namespace Tabletop.Geometry;

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
