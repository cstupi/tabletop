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
    internal readonly record struct NearVolume(Guid Id, Outline Outline, Point2 Centre, double Radius);

    /// <summary>Another model's base outline and the height its body spans, with the bounding circle used to reject it cheaply.</summary>
    internal readonly record struct NearBody(Outline Outline, Point2 Centre, double Radius, double Bottom, double Top);

    /// <summary>
    /// `models` stop the base where its body meets theirs, in plan and in height: the moving body is rested at each step on `surfaces`
    /// (<see cref="Collision.RestHeight(PlacedModel, IReadOnlyList{WorldVolume})"/>), so a base passing above or below another model is not stopped.
    /// </summary>
    public static MovementRegion Region(PlacedModel start, double allowance, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table, IReadOnlyList<PlacedModel>? models = null,
        IReadOnlyList<WorldVolume>? surfaces = null)
    {
        var origin = start.Placement.Position.Horizontal;
        var baseOutline = start.BaseOutline();
        var (baseCentre, baseRadius) = baseOutline.BoundingCircle();
        var reach = allowance + baseRadius;
        var impassableNear = Prefilter(impassable, origin, reach);
        var difficultNear = Prefilter(difficult, origin, reach);
        var slab = table is { } t ? Table.Outline(t) : null;
        var bodies = NearBodies(start, allowance, models ?? []);

        var boundary = new List<Point2>(Rays);
        var capped = false;
        var startLimit = allowance;
        if (difficultCap is { } cap0 && cap0 < startLimit && Touches(origin, baseRadius, baseOutline, difficultNear))
        {
            startLimit = cap0;
            capped = true;
        }
        // A base already touching an impassable volume (only a hand move puts it there) may head out of it on a ray, never along or into it (Blocking).
        var standingIn = impassableNear.Where(v => Touches(origin, baseRadius, baseOutline, [v])).ToList();
        for (var r = 0; r < Rays; r++)
        {
            var angle = 2 * Math.PI * r / Rays;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            var walls = standingIn.Count == 0 ? impassableNear : impassableNear.Where(v => !standingIn.Contains(v) || !HeadsOut(origin, dir, v.Outline)).ToList();
            var limit = startLimit;
            var reached = 0.0;
            for (var k = 1; k <= Sampling.MaxSamples; k++)
            {
                var d = Math.Min(k * Step, limit);
                var at = origin + dir * d;
                var atOutline = baseOutline.Shift(dir * d);
                if (Touches(at, baseRadius, atOutline, walls) || Meets(baseCentre + dir * d, baseRadius, atOutline, start.Body.Height, bodies, surfaces ?? [])
                    || (slab is not null && !slab.ContainsOutline(atOutline))) break;
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
        var steps = Sampling.Steps(distance, Step);
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
    /// The impassable volumes a straight move from the start to the destination may not touch: all of them, less each one the base already
    /// touches where it stands that the move heads out of — its centre's signed distance from the volume's footprint grows from the first step,
    /// and on a straight line through a convex footprint it then grows all the way out. A base a hand move left in impassable terrain may so move
    /// out of it, never along it or deeper in; a move with no direction leaves it where it is, touching.
    /// </summary>
    public static IReadOnlyList<WorldVolume> Blocking(PlacedModel start, Point2 destination, IReadOnlyList<WorldVolume> impassable)
    {
        var origin = start.Placement.Position.Horizontal;
        var delta = destination - origin;
        if (delta.Length <= Tolerance.Distance) return impassable;
        var baseOutline = start.BaseOutline();
        var (_, baseRadius) = baseOutline.BoundingCircle();
        var dir = delta * (1 / delta.Length);
        return impassable.Where(v =>
        {
            var outline = v.WorldOutline();
            var (centre, radius) = outline.BoundingCircle();
            return !Touches(origin, baseRadius, baseOutline, [new NearVolume(v.Id, outline, centre, radius)]) || !HeadsOut(origin, dir, outline);
        }).ToList();
    }

    /// <summary>The base centre's signed distance from a footprint (negative inside) grows along `dir` from `origin`.</summary>
    static bool HeadsOut(Point2 origin, Point2 dir, Outline footprint)
    {
        const double h = 1e-3;
        static double Signed(Outline o, Point2 p) => o.Contains(p) ? -o.BoundaryDistance(p) : o.DistanceTo(p);
        return Signed(footprint, origin + dir * h) - Signed(footprint, origin) > 1e-9;
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
    /// How far the base centre can go from its start in a straight line towards `toward` with the whole base still on the table (the slab
    /// inset as FarthestOnTable insets it). Zero when `toward` is the start or the base is wider than the slab.
    /// </summary>
    public static double RoomAlong(PlacedModel start, Point2 toward, TableSize table)
    {
        var origin = start.Placement.Position.Horizontal;
        var (centre, bound) = start.BaseOutline().BoundingCircle();
        var radius = bound + centre.DistanceTo(origin);
        var delta = toward - origin;
        if (delta.Length <= Tolerance.Distance || 2 * radius > table.Width || 2 * radius > table.Length) return 0;
        double dx = delta.X / delta.Length, dy = delta.Y / delta.Length, room = double.PositiveInfinity;
        if (dx > 1e-12) room = Math.Min(room, (table.Width - radius - origin.X) / dx);
        if (dx < -1e-12) room = Math.Min(room, (radius - origin.X) / dx);
        if (dy > 1e-12) room = Math.Min(room, (table.Length - radius - origin.Y) / dy);
        if (dy < -1e-12) room = Math.Min(room, (radius - origin.Y) / dy);
        return Math.Max(0, room);
    }

    /// <summary>
    /// Volumes whose footprint the base touches at any point of the straight path from start to destination, in input
    /// order. Rulebook p.11's "moves in or through" — a dangerous terrain test is owed for a piece crossed and left behind.
    /// </summary>
    public static IReadOnlyList<Guid> VolumesCrossed(PlacedModel start, Point2 destination, IReadOnlyList<WorldVolume> volumes)
    {
        var origin = start.Placement.Position.Horizontal;
        var delta = destination - origin;
        var steps = Sampling.Steps(delta.Length, Step);
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

    /// <summary>
    /// The other models' bases the moving base could touch within the allowance: those whose bounding circle lies within
    /// `allowance` plus both radii of the start, less those whose body it already overlaps (a model is not stopped by a body it starts in).
    /// The base centre never travels farther than the allowance, so a base outside that ring can never touch it.
    /// </summary>
    internal static List<NearBody> NearBodies(PlacedModel start, double allowance, IReadOnlyList<PlacedModel> models)
    {
        var origin = start.Placement.Position.Horizontal;
        var (centre, bound) = start.BaseOutline().BoundingCircle();
        var reach = allowance + bound + centre.DistanceTo(origin);
        var result = new List<NearBody>();
        foreach (var m in models)
        {
            var outline = m.BaseOutline();
            var (c, radius) = outline.BoundingCircle();
            if (origin.DistanceTo(c) > reach + radius + Tolerance.Distance || Overlap.Bases(start, m)) continue;
            result.Add(new NearBody(outline, c, radius, m.Bottom(), m.Top()));
        }
        return result;
    }

    /// <summary>The base at a step meets a body: in plan, then in height, its bottom rested on the surfaces under it (rested only on a plan hit).</summary>
    static bool Meets(Point2 at, double atRadius, Outline atOutline, double height, IReadOnlyList<NearBody> bodies, IReadOnlyList<WorldVolume> surfaces)
    {
        double? bottom = null;
        foreach (var b in bodies)
        {
            if (at.DistanceTo(b.Centre) > atRadius + b.Radius + Tolerance.Distance || !Outline.InteriorsIntersect(atOutline, b.Outline)) continue;
            bottom ??= Collision.Rest([atOutline], surfaces, floors: true);
            if (Overlap.IntervalsOverlap(bottom.Value, bottom.Value + height, b.Bottom, b.Top)) return true;
        }
        return false;
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
