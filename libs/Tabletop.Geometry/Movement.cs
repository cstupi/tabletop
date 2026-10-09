namespace Tabletop.Geometry;

/// <summary>
/// The straight-line reach of a base centre from its start: one boundary vertex per ray, drawn by the client as a polygon.
/// A prediction for responsiveness; the server's event wins (architecture.md → Geometry).
/// </summary>
public sealed record MovementRegion(Point2 Origin, double Allowance, bool CappedByDifficultTerrain, IReadOnlyList<Point2> Boundary);

/// <summary>One straight move judged against the same parameters. Distance is the base's displacement (the ruler readout).</summary>
public sealed record MoveCheck(double Distance, double EffectiveAllowance, bool WithinAllowance, bool CrossesImpassable, bool EntersDifficult, bool LeavesTable);

/// <summary>
/// Renders the server's move parameters — allowance, the volumes that cannot be crossed, the difficult volumes and the cap
/// GameRules computed — as geometry (architecture.md → How the client stays responsive). Footprints are tested in 2D:
/// the server already chose which volumes count.
/// </summary>
public static class Movement
{
    public const int Rays = 180;
    public const double Step = 0.25;

    /// <summary>A volume's world outline, hoisted once, with the bounding circle used to reject it cheaply.</summary>
    readonly record struct NearVolume(Guid Id, Outline Outline, Point2 Centre, double Radius);

    public static MovementRegion Region(PlacedModel start, double allowance, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table)
    {
        var origin = start.Placement.Position.Horizontal;
        var baseOutline = start.BaseOutline();
        var (_, baseRadius) = baseOutline.BoundingCircle();
        var reach = allowance + baseRadius;
        var impassableNear = Prefilter(impassable, origin, reach);
        var difficultNear = Prefilter(difficult, origin, reach);
        var slab = table is { } t ? Table.Outline(t) : null;

        var boundary = new List<Point2>(Rays);
        var capped = false;
        var startLimit = allowance;
        if (difficultCap is { } cap0 && cap0 < startLimit && Touches(origin, baseRadius, baseOutline, difficultNear))
        {
            startLimit = cap0;
            capped = true;
        }
        for (var r = 0; r < Rays; r++)
        {
            var angle = 2 * Math.PI * r / Rays;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            var limit = startLimit;
            var reached = 0.0;
            for (var k = 1; ; k++)
            {
                var d = Math.Min(k * Step, limit);
                var at = origin + dir * d;
                var atOutline = baseOutline.Shift(dir * d);
                if (Touches(at, baseRadius, atOutline, impassableNear) || (slab is not null && !slab.ContainsOutline(atOutline))) break;
                if (difficultCap is { } cap && cap < limit && Touches(at, baseRadius, atOutline, difficultNear))
                {
                    limit = cap;
                    capped = true;
                    if (d > limit + Tolerance.Distance) break;
                }
                reached = d;
                if (d >= limit - Tolerance.Distance) break;
            }
            boundary.Add(origin + dir * reached);
        }
        return new MovementRegion(origin, allowance, capped, boundary);
    }

    public static MoveCheck Check(PlacedModel start, Point2 destination, double allowance, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table)
    {
        var origin = start.Placement.Position.Horizontal;
        var delta = destination - origin;
        var distance = delta.Length;
        var steps = Math.Max(1, (int)Math.Ceiling(distance / Step));
        var baseOutline = start.BaseOutline();
        var (_, baseRadius) = baseOutline.BoundingCircle();
        var reach = distance + baseRadius;
        var impassableNear = Prefilter(impassable, origin, reach);
        var difficultNear = Prefilter(difficult, origin, reach);
        var slab = table is { } t ? Table.Outline(t) : null;
        bool crosses = false, enters = false, leaves = false;
        for (var k = 0; k <= steps; k++)
        {
            var by = delta * ((double)k / steps);
            var at = origin + by;
            var atOutline = baseOutline.Shift(by);
            crosses |= Touches(at, baseRadius, atOutline, impassableNear);
            enters |= Touches(at, baseRadius, atOutline, difficultNear);
            leaves |= slab is not null && !slab.ContainsOutline(atOutline);
        }
        var effective = enters && difficultCap is { } cap ? Math.Min(allowance, cap) : allowance;
        return new MoveCheck(distance, effective, distance <= effective + Tolerance.Distance, crosses, enters, leaves);
    }

    /// <summary>
    /// The farthest the base centre can go from its start in a straight line with the whole base still on the table: the corner of the
    /// slab inset by the base's bounding radius that lies farthest from the start, and its distance. A minimum move the table has no room
    /// for is held to this. The start itself, at distance zero, when the base is wider than the slab.
    /// </summary>
    public static (double Distance, Point2 To) FarthestOnTable(PlacedModel start, TableSize table)
    {
        var origin = start.Placement.Position.Horizontal;
        var (centre, bound) = start.BaseOutline().BoundingCircle();
        var radius = bound + centre.DistanceTo(origin);
        if (2 * radius > table.Width || 2 * radius > table.Length) return (0, origin);
        Point2[] corners = [new(radius, radius), new(table.Width - radius, radius), new(radius, table.Length - radius), new(table.Width - radius, table.Length - radius)];
        var far = corners.MaxBy(c => origin.DistanceTo(c));
        return (origin.DistanceTo(far), far);
    }

    /// <summary>
    /// Volumes whose footprint the base touches at any point of the straight path from start to destination, in input
    /// order. Rulebook p.11's "moves in or through" — a dangerous terrain test is owed for a piece crossed and left behind.
    /// </summary>
    public static IReadOnlyList<Guid> VolumesCrossed(PlacedModel start, Point2 destination, IReadOnlyList<WorldVolume> volumes)
    {
        var origin = start.Placement.Position.Horizontal;
        var delta = destination - origin;
        var steps = Math.Max(1, (int)Math.Ceiling(delta.Length / Step));
        var baseOutline = start.BaseOutline();
        var (_, baseRadius) = baseOutline.BoundingCircle();
        var near = Prefilter(volumes, origin, delta.Length + baseRadius);
        var touched = new HashSet<Guid>();
        for (var k = 0; k <= steps; k++)
        {
            var by = delta * ((double)k / steps);
            var at = origin + by;
            var atOutline = baseOutline.Shift(by);
            foreach (var v in near)
                if (at.DistanceTo(v.Centre) <= baseRadius + v.Radius + Tolerance.Distance && Outline.InteriorsIntersect(atOutline, v.Outline))
                    touched.Add(v.Id);
        }
        return volumes.Where(v => touched.Contains(v.Id)).Select(v => v.Id).ToList();
    }

    /// <summary>Volumes whose bounding circle lies within reach of origin, with their world outline hoisted out of the sampling loop.</summary>
    static List<NearVolume> Prefilter(IReadOnlyList<WorldVolume> volumes, Point2 origin, double reach)
    {
        var result = new List<NearVolume>(volumes.Count);
        foreach (var v in volumes)
        {
            var outline = v.WorldOutline();
            var (centre, radius) = outline.BoundingCircle();
            if (origin.DistanceTo(centre) <= reach + radius + Tolerance.Distance) result.Add(new NearVolume(v.Id, outline, centre, radius));
        }
        return result;
    }

    /// <summary>Bounding-circle rejection, then the exact test, against the pre-hoisted near volumes.</summary>
    static bool Touches(Point2 at, double atRadius, Outline atOutline, IReadOnlyList<NearVolume> volumes)
    {
        foreach (var v in volumes)
        {
            if (at.DistanceTo(v.Centre) > atRadius + v.Radius + Tolerance.Distance) continue;
            if (Outline.InteriorsIntersect(atOutline, v.Outline)) return true;
        }
        return false;
    }
}
