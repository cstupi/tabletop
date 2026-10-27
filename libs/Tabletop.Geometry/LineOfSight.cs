namespace Tabletop.Geometry;

/// <summary>Which volumes obstruct the line. Empty when clear.</summary>
public sealed record LineOfSightResult(bool Clear, IReadOnlyList<Guid> ObstructedBy);

/// <summary>
/// Line of sight between two base-and-height bodies against placed volumes. Only volumes declaring
/// BlocksLineOfSight obstruct; a SeeInAndOut volume obstructs a line passing through it but not one
/// that starts or ends inside it. The mesh is never consulted.
/// Centre to any part of target (rulebook p.8): lines run from the centre of the shooter's body
/// to the target's centre and up to eight rim points at each of three heights — its base, half height and its
/// top — 27 points for a circle or an oval, 15 for a rect, whose four vertices are its whole rim. Sight is clear when any one line is unobstructed.
/// Every sample is a real point of the body, so Clear is never wrong; the error is one-sided, under-reporting
/// visibility when the only visible part falls between samples.
/// </summary>
public static class LineOfSight
{
    public const int RimSamples = 8;

    public static LineOfSightResult Check(PlacedModel from, PlacedModel to, IReadOnlyList<WorldVolume> volumes)
    {
        var eye = from.Eye();
        var blockers = Blockers(volumes);
        var seen = new HashSet<Guid>();
        foreach (var target in TargetPoints(to))
        {
            var hits = Obstructions(eye, target, blockers);
            if (hits.Count == 0) return new LineOfSightResult(true, []);
            foreach (var id in hits) seen.Add(id);
        }
        return new LineOfSightResult(false, volumes.Where(v => seen.Contains(v.Id)).Select(v => v.Id).ToList());
    }

    /// <summary>
    /// Which models of a unit at least one observer can see, index-aligned with <paramref name="unit"/>.
    /// The count is the caller's: p.11's majority, read as fewer than half of a unit's models seen, is a ruleset threshold,
    /// not geometry. A single shooter is a one-element observers list.
    /// </summary>
    public static IReadOnlyList<bool> VisibleModels(
        IReadOnlyList<PlacedModel> observers, IReadOnlyList<PlacedModel> unit, IReadOnlyList<WorldVolume> volumes) =>
        unit.Select(m => observers.Any(o => Check(o, m, volumes).Clear)).ToList();

    internal static List<WorldVolume> Blockers(IReadOnlyList<WorldVolume> volumes) =>
        volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight)).ToList();

    /// <summary>The centre and the rim at three heights: the top, half height, then the base, an equal count at each; the order is fixed, so an index names a point.</summary>
    public static IEnumerable<Point3> TargetPoints(PlacedModel to)
    {
        var p = to.Placement.Position;
        var rim = Rim(to.BaseOutline());
        double[] heights = [to.Top(), to.Eye().Z, to.Bottom()];   // the top, the body centre, the base
        foreach (var z in heights)
        {
            yield return new Point3(p.X, p.Y, z);
            foreach (var r in rim) yield return new Point3(r.X, r.Y, z);
        }
    }

    /// <summary><see cref="TargetPoints(PlacedModel)"/> written into <paramref name="into"/> (room for 27); returns how many.</summary>
    internal static int TargetPoints(PlacedModel to, Span<Point3> into)
    {
        var n = 0;
        foreach (var t in TargetPoints(to)) into[n++] = t;
        return n;
    }

    /// <summary>Eight points around the outline: evenly around a circle; every n-th vertex of a polygon.</summary>
    internal static IReadOnlyList<Point2> Rim(Outline outline) => outline switch
    {
        CircleOutline c => c.BoundaryPoints(RimSamples),
        PolygonOutline poly when poly.Vertices.Count <= RimSamples => poly.Vertices,
        PolygonOutline poly => Enumerable.Range(0, RimSamples).Select(i => poly.Vertices[i * poly.Vertices.Count / RimSamples]).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(outline)),
    };

    /// <summary>Blocking volumes the segment passes through; a SeeInAndOut volume is skipped when either end is inside it.</summary>
    internal static List<Guid> Obstructions(Point3 p, Point3 q, IReadOnlyList<WorldVolume> blockers)
    {
        var hits = new List<Guid>();
        foreach (var v in blockers)
        {
            if (!v.IntersectsSegment(p, q)) continue;
            if (v.Volume.Declarations.HasFlag(TerrainDeclarations.SeeInAndOut) && (v.ContainsPoint(p) || v.ContainsPoint(q))) continue;
            hits.Add(v.Id);
        }
        return hits;
    }
}
