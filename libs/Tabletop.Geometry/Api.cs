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
