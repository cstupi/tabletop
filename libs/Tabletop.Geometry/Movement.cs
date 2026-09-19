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

    public static MovementRegion Region(PlacedModel start, double allowance, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table)
    {
        var origin = start.Placement.Position.Horizontal;
        var boundary = new List<Point2>(Rays);
        var capped = false;
        for (var r = 0; r < Rays; r++)
        {
            var angle = 2 * Math.PI * r / Rays;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            var limit = allowance;
            var reached = 0.0;
            for (var k = 1; ; k++)
            {
                var d = Math.Min(k * Step, limit);
                var at = Displace(start, dir * d);
                if (Touches(at, impassable) || (table is { } t && !Table.ContainsBase(t, at))) break;
                if (difficultCap is { } cap && cap < limit && Touches(at, difficult))
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
        bool crosses = false, enters = false, leaves = false;
        for (var k = 0; k <= steps; k++)
        {
            var at = Displace(start, delta * ((double)k / steps));
            crosses |= Touches(at, impassable);
            enters |= Touches(at, difficult);
            leaves |= table is { } t && !Table.ContainsBase(t, at);
        }
        var effective = enters && difficultCap is { } cap ? Math.Min(allowance, cap) : allowance;
        return new MoveCheck(distance, effective, distance <= effective + Tolerance.Distance, crosses, enters, leaves);
    }

    /// <summary>
    /// Volumes whose footprint the base touches at any point of the straight path from start to destination, in input
    /// order. Rulebook p.11's "moves in or through" — a dangerous terrain test is owed for a piece crossed and left behind.
    /// </summary>
    public static IReadOnlyList<Guid> VolumesCrossed(PlacedModel start, Point2 destination, IReadOnlyList<WorldVolume> volumes)
    {
        var delta = destination - start.Placement.Position.Horizontal;
        var steps = Math.Max(1, (int)Math.Ceiling(delta.Length / Step));
        var touched = new HashSet<Guid>();
        for (var k = 0; k <= steps; k++)
        {
            var outline = Displace(start, delta * ((double)k / steps)).BaseOutline();
            foreach (var v in volumes)
                if (Outline.InteriorsIntersect(outline, v.WorldOutline())) touched.Add(v.Id);
        }
        return volumes.Where(v => touched.Contains(v.Id)).Select(v => v.Id).ToList();
    }

    static PlacedModel Displace(PlacedModel start, Point2 by)
    {
        var p = start.Placement.Position;
        return start with { Placement = start.Placement with { Position = new Point3(p.X + by.X, p.Y + by.Y, p.Z) } };
    }

    static bool Touches(PlacedModel at, IReadOnlyList<WorldVolume> volumes)
    {
        var outline = at.BaseOutline();
        return volumes.Any(v => Outline.InteriorsIntersect(outline, v.WorldOutline()));
    }
}
