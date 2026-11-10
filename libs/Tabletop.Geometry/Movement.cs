namespace Tabletop.Geometry;

/// <summary>
/// Where a base centre can go from its start, measured along the path (MoveReach), as triangles (three points each) the client draws.
/// A prediction for responsiveness; the server's event wins (architecture.md → Geometry).
/// </summary>
public sealed record MovementRegion(Point2 Origin, double Allowance, bool CappedByDifficultTerrain, IReadOnlyList<Point2> Triangles);

/// <summary>
/// One move judged against the same parameters, along the path the base takes (Path, from the start to the destination). Distance is that path's
/// length (the ruler readout). CrossesModels: no path within the allowance goes round the other models, but one through them does (Path is that one).
/// </summary>
public sealed record MoveCheck(double Distance, double EffectiveAllowance, bool WithinAllowance, bool CrossesImpassable, bool EntersDifficult, bool LeavesTable,
    IReadOnlyList<Point2> Path, bool CrossesModels = false);

/// <summary>
/// Renders the server's move parameters — allowance, the volumes that cannot be crossed, the difficult volumes and the cap
/// GameRules computed — as geometry (architecture.md → How the client stays responsive). Footprints are tested in plan, models in plan and height:
/// the server already chose which volumes count. A move is measured along the shortest path its base can take (rulebook p.5 "always measure so that
/// no part of its base moves further than the total distance"; p.9 "charge moves don't have to be in a straight line"), found in the base's
/// configuration space (MoveSpace).
/// </summary>
public static class Movement
{
    public const int Rays = 180;
    public const double Step = 0.25;

    /// <summary>The reachable area by path length (MoveReach.Triangles). `least`, a minimum move's straight distance from the start, cuts the middle out.</summary>
    public static MovementRegion Region(PlacedModel start, double allowance, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table, IReadOnlyList<PlacedModel>? models = null,
        IReadOnlyList<WorldVolume>? surfaces = null, Func<Point2, double>? least = null)
    {
        var reach = MoveReach.Build(start, allowance, impassable, difficult, difficultCap, table, models, surfaces);
        return new MovementRegion(reach.Origin, reach.Allowance, reach.CappedByDifficultTerrain, reach.Triangles(least));
    }

    /// <summary>
    /// The path a move from the start to the destination is measured along, and the verdicts on it. The path is the shortest that keeps the whole
    /// base on the table, meets no impassable volume (one the base already stands in it may head straight out of first, never move along or deeper
    /// into) and, given `models`, meets no other model in plan and height (one it starts on left out; the base rested on `surfaces`). Of those, the
    /// first that fits, in order: round every difficult and dangerous volume within the allowance; round every difficult one within it; round every
    /// dangerous one within the cap; any within the cap (the cap is the allowance where there is none, and then dangerous ground is avoided first).
    /// A player going round a wood or a pit never chose to enter it, and the cap binds the whole unit (p.11), so keeping out of difficult ground
    /// comes first. `steep` volumes count as difficult for this move when the start's base shares no interior with their footprint (SteepEntering), and as
    /// open ground when the base stands on one. When none fits: with models, the same choice without them (CrossesModels); else the shortest path at any length, for its length,
    /// with CrossesImpassable when the straight line meets an impassable volume or no path gets there at all.
    /// </summary>
    public static MoveCheck Check(PlacedModel start, Point2 destination, double allowance, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table, IReadOnlyList<PlacedModel>? models = null,
        IReadOnlyList<WorldVolume>? surfaces = null, IReadOnlyList<WorldVolume>? dangerous = null, IReadOnlyList<WorldVolume>? steep = null)
    {
        if (steep is { Count: > 0 } && SteepEntering(start, steep) is { Count: > 0 } onto) difficult = [.. difficult, .. onto];
        var origin = start.Placement.Position.Horizontal;
        var straight = origin.DistanceTo(destination);
        // A path no longer than the allowance stays within it of the start, so nothing farther can hold one; the fallback for a move that fits no
        // path, measured only to say how far over it is, looks farther.
        var space = new MoveSpace(start, Math.Max(allowance, 0) + 1e-3, impassable, difficult, dangerous ?? [], table, models, surfaces);
        var far = Math.Min(2 * Math.Max(Math.Max(allowance, straight), 1), 4 * Table.MaxEdge);
        var capLimit = difficultCap is { } cap ? Math.Min(allowance, cap) : allowance;
        List<Point2> line = [origin, destination];
        bool Enters(IReadOnlyList<Point2> path) => (space.Present & MoveSpace.Difficult) != 0 && space.Touched(path, MoveSpace.Difficult).Count > 0;
        MoveSpace Wide() => new(start, far, impassable, difficult, [], table, null, surfaces);
        // p.7: no part of the base off the table at the destination. A base on the table goes no further; one already off it (a hand move) is
        // held to no table on the way, so its path is still found.
        var leaves = table is { } slab && !Table.Outline(slab).ContainsOutline(start.BaseOutline().Shift(destination - origin));
        if (leaves && space.HasTable)
        {
            var wide = Wide();
            return new MoveCheck(straight, allowance, straight <= allowance + Tolerance.Distance, !wide.Clear(origin, destination, MoveSpace.Walls, fromStart: true),
                (wide.Present & MoveSpace.Difficult) != 0 && wide.Touched(line, MoveSpace.Difficult).Count > 0, true, line);
        }
        MoveCheck? Fit(int walls)
        {
            (int Avoid, double Limit)[] order = difficultCap is null
                ? [(MoveSpace.Difficult | MoveSpace.Dangerous, allowance), (MoveSpace.Dangerous, allowance), (0, allowance)]
                : [(MoveSpace.Difficult | MoveSpace.Dangerous, allowance), (MoveSpace.Difficult, allowance), (MoveSpace.Dangerous, capLimit), (0, capLimit)];
            var tried = new HashSet<(int, double)>();
            foreach (var (avoid, limit) in order)
            {
                var present = avoid & space.Present;
                if (!tried.Add((present, limit))) continue;                   // a group with nothing in reach adds nothing to avoid
                if (space.Route(destination, walls | present, limit) is not { } route) continue;
                var enters = (present & MoveSpace.Difficult) == 0 && Enters(route.Path);
                var effective = enters && difficultCap is not null ? capLimit : allowance;
                if (route.Length > effective + Tolerance.Distance) continue;
                return new MoveCheck(route.Length, effective, true, false, enters, leaves, route.Path);
            }
            return null;
        }
        if (Fit(MoveSpace.Walls | MoveSpace.Bodies) is { } fits) return fits;
        if ((space.Present & MoveSpace.Bodies) != 0 && Fit(MoveSpace.Walls) is { } through) return through with { CrossesModels = true };
        var outer = Wide();
        bool Through(IReadOnlyList<Point2> path) => (outer.Present & MoveSpace.Difficult) != 0 && outer.Touched(path, MoveSpace.Difficult).Count > 0;
        var crosses = !outer.Clear(origin, destination, MoveSpace.Walls, fromStart: true);
        if (outer.Route(destination, MoveSpace.Walls, far) is { } longer)
        {
            var enters = Through(longer.Path);
            return new MoveCheck(longer.Length, enters && difficultCap is not null ? capLimit : allowance, false, crosses, enters, leaves, longer.Path);
        }
        return new MoveCheck(straight, allowance, false, true, Through(line), leaves, line);
    }

    /// <summary>
    /// The path a move is measured along when some volumes are dangerous only to a long path: Check round the `dangerous` volumes; when that path is
    /// longer than `unstableOver` and enters a dangerous or `unstable` volume, Check again round both, so such a path goes round unstable ground when
    /// that fits (MoveReach.Route draws the same path). With no unstable volume it is Check.
    /// </summary>
    public static MoveCheck Measured(PlacedModel start, Point2 destination, double allowance, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table, IReadOnlyList<PlacedModel>? models, IReadOnlyList<WorldVolume>? surfaces,
        IReadOnlyList<WorldVolume> dangerous, IReadOnlyList<WorldVolume> unstable, double unstableOver, IReadOnlyList<WorldVolume>? steep = null)
    {
        MoveCheck Path(IReadOnlyList<WorldVolume> avoid) => Check(start, destination, allowance, impassable, difficult, difficultCap, table, models, surfaces, avoid, steep);
        var path = Path(dangerous);
        if (unstable.Count > 0 && path.Distance > unstableOver + Tolerance.Distance && VolumesOnPath(start, path.Path, [.. dangerous, .. unstable]).Count > 0)
            path = Path([.. dangerous, .. unstable]);
        return path;
    }

    /// <summary>The steep volumes a move from this start goes onto: those the start's base does not stand on, sharing no interior with their footprint
    /// in plan (a base on the footprint rests on the top, Collision.Surfaces; one only touching its edge stands beside it). A move from one the base
    /// stands on is moving off it or along its top, open ground for the whole move (3.6.0 p.12 Steep "units moving off it count it as open terrain";
    /// 3.5.1 p.12 Hills "Difficult when moving up").</summary>
    public static IReadOnlyList<WorldVolume> SteepEntering(PlacedModel start, IReadOnlyList<WorldVolume> steep)
    {
        if (steep.Count == 0) return [];
        var footing = start.BaseOutline();
        return steep.Where(v => !Outline.InteriorsIntersect(footing, v.WorldOutline())).ToList();
    }

    /// <summary>
    /// The shortest path from the start to base contact with `target` (its base touching the target's, in plan), on the table and meeting no
    /// impassable volume, when it is at most `limit`; null otherwise. Rulebook p.9: a charge target is valid when a model "is within charge distance
    /// of one model from the target unit, and has a clear path to reach it", and "charge moves don't have to be in a straight line". The contact point
    /// is the nearest one to the start, or to a corner of a way round; 0 when the bases already touch or overlap.
    /// </summary>
    public static double? ContactDistance(PlacedModel start, PlacedModel target, double limit, IReadOnlyList<WorldVolume> impassable, TableSize? table)
    {
        var origin = start.Placement.Position.Horizontal;
        var contact = MoveShape.Of(target.BaseOutline(), start.BaseOutline(), origin);
        if (contact.Depth(origin) <= Tolerance.Distance) return 0;
        var first = contact.Nearest(origin);
        if (origin.DistanceTo(first) > limit + Tolerance.Distance) return null;
        var space = new MoveSpace(start, limit, impassable, [], [], table, null, null);
        if (space.Clear(origin, first, MoveSpace.Walls, fromStart: true)) return origin.DistanceTo(first);
        var field = space.Settle(MoveSpace.Walls, limit);
        double? best = null;
        foreach (var corner in field.Corners)
        {
            var touch = contact.Nearest(corner.At);
            var length = corner.G + corner.At.DistanceTo(touch);
            if (length <= limit + Tolerance.Distance && (best is null || length < best) && space.Clear(corner.At, touch, MoveSpace.Walls)) best = length;
        }
        return best;
    }

    /// <summary>
    /// Where the start's base (its own facing) stands `gap` clear of the target's base, one point on each of `bearings` directions about the
    /// target's centre: the direction facing the start first, then alternately either side of it. A charge's landings in base contact (p.9);
    /// the caller judges each as a move.
    /// </summary>
    public static IReadOnlyList<Point2> ContactPoints(PlacedModel start, PlacedModel target, double gap, int bearings = 32)
    {
        var centre = target.Placement.Position.Horizontal;
        var facing = start.Placement.Position.Horizontal - centre;
        var bearing = facing.Length > 1e-9 ? Math.Atan2(facing.Y, facing.X) : 0;
        var points = new List<Point2>(bearings);
        for (var k = 0; k < bearings; k++)
        {
            var angle = bearing + (k + 1) / 2 * (k % 2 == 0 ? -1 : 1) * Math.Tau / bearings;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            PlacedModel There(double r) => start with { Placement = start.Placement with { Position = new Point3(centre.X + dir.X * r, centre.Y + dir.Y * r, start.Placement.Position.Z) } };
            double lo = 0, hi = 1;
            while (Distance.EdgeToEdge(There(hi), target) < gap && hi < 1024) hi *= 2;
            for (var i = 0; i < 40; i++) { var mid = (lo + hi) / 2; if (Distance.EdgeToEdge(There(mid), target) < gap) lo = mid; else hi = mid; }
            points.Add(centre + dir * hi);
        }
        return points;
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
    public static IReadOnlyList<Guid> VolumesCrossed(PlacedModel start, Point2 destination, IReadOnlyList<WorldVolume> volumes) =>
        VolumesOnPath(start, [start.Placement.Position.Horizontal, destination], volumes);

    /// <summary>The same along a path (MoveCheck.Path, from the start): the volumes the base touches on any leg, in input order.</summary>
    public static IReadOnlyList<Guid> VolumesOnPath(PlacedModel start, IReadOnlyList<Point2> path, IReadOnlyList<WorldVolume> volumes)
    {
        if (volumes.Count == 0) return [];
        var reach = path.Max(p => p.DistanceTo(start.Placement.Position.Horizontal));
        var space = new MoveSpace(start, reach + 1, [], volumes, [], null, null, null);
        var touched = space.Touched(path, MoveSpace.Difficult);
        return volumes.Where(v => touched.Contains(v.Id)).Select(v => v.Id).Distinct().ToList();
    }
}
