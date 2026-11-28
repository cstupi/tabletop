namespace Tabletop.Geometry;

/// <summary>
/// Where a base centre can go from its start, measured along the path (MoveReach), as triangles (three points each) the client draws.
/// A prediction for responsiveness; the server's event wins (architecture.md → Geometry).
/// </summary>
public sealed record MovementRegion(Point2 Origin, double Allowance, bool CappedByDifficultTerrain, IReadOnlyList<Point2> Triangles);

/// <summary>
/// One move judged against the same parameters, along the path the base takes (Path, from the start to the destination). Distance is that path's
/// length (the ruler readout). CrossesModels: no path within the allowance goes round the other models and units, but one through them does (Path is
/// that one). NearModels: none goes round the units' perimeters grown by the separation, but one round the plain perimeters does (Path is that one).
/// Unit: with MoveUnits, the unit such a path first goes through or comes within the separation of.
/// </summary>
public sealed record MoveCheck(double Distance, double EffectiveAllowance, bool WithinAllowance, bool CrossesImpassable, bool EntersDifficult, bool LeavesTable,
    IReadOnlyList<Point2> Path, bool CrossesModels = false, bool NearModels = false, Guid? Unit = null);

/// <summary>
/// The units of the models a move goes round (p.7: models "may never move through other models or units (friendly or enemy)" and "may never be within
/// 1” of models from other units, unless they are using a Charge action"): Of[i] is the unit of the i-th model given with it. Each unit other than
/// `Own` and those in `Bases` blocks a path as its perimeter, the convex hull of its models' bases from its lowest base to its tallest top, grown by
/// `Separation` when set (null in a Charge). `Own` and `Bases` (a charge's target) block as their models' bases. A base that starts inside a unit's
/// grown perimeter is held to its plain perimeter, and one that starts inside that to its models' bases, so where a model stands never traps it.
/// </summary>
public sealed record MoveUnits(IReadOnlyList<Guid> Of, Guid Own, double? Separation = null, IReadOnlyList<Guid>? Bases = null);

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
        IReadOnlyList<WorldVolume>? surfaces = null, Func<Point2, double>? least = null, MoveUnits? units = null)
    {
        var reach = MoveReach.Build(start, allowance, impassable, difficult, difficultCap, table, models, surfaces, units: units);
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
    /// open ground when the base stands on one. With `units`, each other unit is held to as its perimeter, grown by the separation (MoveUnits). When
    /// none fits: with a separation, the same choice round the plain perimeters (NearModels); with models, the same choice without them
    /// (CrossesModels), each naming the unit met first (Unit); else the shortest path at any length, for its length, with CrossesImpassable when the
    /// straight line meets an impassable volume or no path gets there at all.
    /// </summary>
    public static MoveCheck Check(PlacedModel start, Point2 destination, double allowance, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table, IReadOnlyList<PlacedModel>? models = null,
        IReadOnlyList<WorldVolume>? surfaces = null, IReadOnlyList<WorldVolume>? dangerous = null, IReadOnlyList<WorldVolume>? steep = null,
        MoveUnits? units = null)
    {
        if (steep is { Count: > 0 } && SteepEntering(start, steep) is { Count: > 0 } onto) difficult = [.. difficult, .. onto];
        var origin = start.Placement.Position.Horizontal;
        var straight = origin.DistanceTo(destination);
        // A path no longer than the allowance stays within it of the start, so nothing farther can hold one; the fallback for a move that fits no
        // path, measured only to say how far over it is, looks farther.
        var space = new MoveSpace(start, Math.Max(allowance, 0) + 1e-3, impassable, difficult, dangerous ?? [], table, models, surfaces, units: units);
        var far = Math.Min(2 * Math.Max(Math.Max(allowance, straight), 1), 4 * Table.MaxEdge);
        var capLimit = difficultCap is { } cap ? Math.Min(allowance, cap) : allowance;
        List<Point2> line = [origin, destination];
        MoveSpace Wide() => new(start, far, impassable, difficult, [], table, null, surfaces);
        // p.7: no part of the base off the table at the destination. A base on the table goes no further; one already off it (a hand move) is
        // held to no table on the way, so its path is still found.
        var leaves = Leaves(start.BaseOutline(), origin, destination, table);
        if (leaves && space.HasTable)
        {
            var wide = Wide();
            return new MoveCheck(straight, allowance, straight <= allowance + Tolerance.Distance, !wide.Clear(origin, destination, MoveSpace.Walls, fromStart: true),
                (wide.Present & MoveSpace.Difficult) != 0 && wide.Touched(line, MoveSpace.Difficult).Count > 0, true, line);
        }
        if (Fit(space, destination, allowance, difficultCap, MoveSpace.Held, leaves) is { } fits) return fits;
        if ((space.Present & MoveSpace.Spacing) != 0 && Fit(space, destination, allowance, difficultCap, MoveSpace.Walls | MoveSpace.Bodies | MoveSpace.Perimeter, leaves) is { } near)
            return near with { NearModels = true, Unit = UnitOf(space.FirstMet(near.Path, MoveSpace.Spacing)) };
        if ((space.Present & MoveSpace.Models) != 0 && Fit(space, destination, allowance, difficultCap, MoveSpace.Walls, leaves) is { } through)
            return through with { CrossesModels = true, Unit = UnitOf(space.FirstMet(through.Path, MoveSpace.Bodies | MoveSpace.Perimeter)) };
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

    static Guid? UnitOf(Guid? id) => id is { } g && g != Guid.Empty ? g : null;

    static bool Leaves(Outline start, Point2 origin, Point2 destination, TableSize? table) =>
        table is { } slab && !Table.Outline(slab).ContainsOutline(start.Shift(destination - origin));

    /// <summary>Check's path within the allowance meeting none of `walls`: the first of its four (three without a cap) choices that fits; null when none does.</summary>
    static MoveCheck? Fit(MoveSpace space, Point2 destination, double allowance, double? difficultCap, int walls, bool leaves)
    {
        var capLimit = difficultCap is { } cap ? Math.Min(allowance, cap) : allowance;
        (int Avoid, double Limit)[] order = difficultCap is null
            ? [(MoveSpace.Difficult | MoveSpace.Dangerous, allowance), (MoveSpace.Dangerous, allowance), (0, allowance)]
            : [(MoveSpace.Difficult | MoveSpace.Dangerous, allowance), (MoveSpace.Difficult, allowance), (MoveSpace.Dangerous, capLimit), (0, capLimit)];
        var tried = new HashSet<(int, double)>();
        foreach (var (avoid, limit) in order)
        {
            var present = avoid & space.Present;
            if (!tried.Add((present, limit))) continue;                   // a group with nothing in reach adds nothing to avoid
            if (space.Route(destination, walls | present, limit) is not { } route) continue;
            var enters = (present & MoveSpace.Difficult) == 0 && (space.Present & MoveSpace.Difficult) != 0 && space.Touched(route.Path, MoveSpace.Difficult).Count > 0;
            var effective = enters && difficultCap is not null ? capLimit : allowance;
            if (route.Length > effective + Tolerance.Distance) continue;
            return new MoveCheck(route.Length, effective, true, false, enters, leaves, route.Path);
        }
        return null;
    }

    /// <summary>
    /// Many destinations of one model's move judged as Check judges each, against one MoveSpace built once (its corners on first use): whether Check
    /// would find the move within the allowance, clear of impassable ground and the models, and on the table. UnitFormation's landings.
    /// </summary>
    internal sealed class Fitting
    {
        readonly PlacedModel _start;
        readonly Outline _base;
        readonly double _allowance;
        readonly double? _cap;
        readonly TableSize? _table;
        readonly MoveSpace _space;

        public Fitting(PlacedModel start, double allowance, IReadOnlyList<WorldVolume> impassable, IReadOnlyList<WorldVolume> difficult, double? difficultCap,
            TableSize? table, IReadOnlyList<PlacedModel>? models, IReadOnlyList<WorldVolume>? surfaces, IReadOnlyList<WorldVolume>? steep, MoveUnits? units = null)
        {
            if (steep is { Count: > 0 } && SteepEntering(start, steep) is { Count: > 0 } onto) difficult = [.. difficult, .. onto];
            (_start, _base, _allowance, _cap, _table) = (start, start.BaseOutline(), allowance, difficultCap, table);
            _space = new MoveSpace(start, Math.Max(allowance, 0) + 1e-3, impassable, difficult, [], table, models, surfaces, units: units);
        }

        /// <summary>Check(...) is { WithinAllowance: true, CrossesImpassable: false, LeavesTable: false, CrossesModels: false, NearModels: false }.</summary>
        public bool Fits(Point2 destination) =>
            !Leaves(_base, _start.Placement.Position.Horizontal, destination, _table)
            && Fit(_space, destination, _allowance, _cap, MoveSpace.Held, false) is not null;
    }

    /// <summary>
    /// The path a move is measured along when some volumes are dangerous only to a long path: Check round the `dangerous` volumes; when that path is
    /// longer than `unstableOver` and enters a dangerous or `unstable` volume, Check again round both, so such a path goes round unstable ground when
    /// that fits (MoveReach.Route draws the same path). With no unstable volume it is Check.
    /// </summary>
    public static MoveCheck Measured(PlacedModel start, Point2 destination, double allowance, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table, IReadOnlyList<PlacedModel>? models, IReadOnlyList<WorldVolume>? surfaces,
        IReadOnlyList<WorldVolume> dangerous, IReadOnlyList<WorldVolume> unstable, double unstableOver, IReadOnlyList<WorldVolume>? steep = null,
        MoveUnits? units = null)
    {
        MoveCheck Path(IReadOnlyList<WorldVolume> avoid) => Check(start, destination, allowance, impassable, difficult, difficultCap, table, models, surfaces, avoid, steep, units);
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
    /// is the nearest one to the start, or to a corner of a way round; 0 when the bases already touch or overlap. Given `models` (the target among
    /// them left out), the path goes round them as Check's does, in plan and height over `surfaces`, and with `units` round each other unit's
    /// perimeter (MoveUnits; the target's unit among its Bases).
    /// </summary>
    public static double? ContactDistance(PlacedModel start, PlacedModel target, double limit, IReadOnlyList<WorldVolume> impassable, TableSize? table,
        IReadOnlyList<PlacedModel>? models = null, MoveUnits? units = null, IReadOnlyList<WorldVolume>? surfaces = null)
    {
        var origin = start.Placement.Position.Horizontal;
        var contact = MoveShape.Of(target.BaseOutline(), start.BaseOutline(), origin);
        if (contact.Depth(origin) <= Tolerance.Distance) return 0;
        var first = contact.Nearest(origin);
        if (origin.DistanceTo(first) > limit + Tolerance.Distance) return null;
        if (models is { Count: > 0 } && models.Contains(target))
        {
            var keep = Enumerable.Range(0, models.Count).Where(i => !models[i].Equals(target)).ToList();
            units = units is null ? null : units with { Of = [.. keep.Select(i => units.Of[i])] };
            models = [.. keep.Select(i => models[i])];
        }
        var space = new MoveSpace(start, limit, impassable, [], [], table, models, surfaces, units: units);
        if (space.Clear(origin, first, MoveSpace.Held, fromStart: true)) return origin.DistanceTo(first);
        var field = space.Settle(MoveSpace.Held, limit);
        double? best = null;
        foreach (var corner in field.Corners)
        {
            var touch = contact.Nearest(corner.At);
            var length = corner.G + corner.At.DistanceTo(touch);
            if (length <= limit + Tolerance.Distance && (best is null || length < best) && space.Clear(corner.At, touch, MoveSpace.Held)) best = length;
        }
        return best;
    }

    /// <summary>
    /// Whether the start has a path within `limit` to a landing in base contact with `target` (ContactDistance) that rests within `vertical` of the
    /// target's base: the landing on its open surface nearest the target's level (Collision.RestNear, as a move settles a landing sent at that
    /// level), on one of 16 bearings about the target (ContactPoints, UnitFormation.ContactGap clear), on a path round the impassable volumes and on
    /// the table. Where no volume near the target has a surface above `vertical` and the target stands within it of the table, every landing is in
    /// reach and only the plan path is measured. With `difficult` volumes and a `difficultCap` (and `steep` ones, difficult when the start is off them,
    /// SteepEntering), each landing's path is judged as Check judges a move: round the difficult ground within the limit, else through it within the
    /// cap. Only where the shortest plan path is over the cap and difficult ground lies within the limit of the start is the landing search needed.
    /// Given `models` and `units`, every path goes round them as ContactDistance's and Check's do.
    /// </summary>
    public static bool ContactInReach(PlacedModel start, PlacedModel target, double limit, IReadOnlyList<WorldVolume> impassable, TableSize? table,
        IReadOnlyList<WorldVolume> volumes, double vertical, IReadOnlyList<WorldVolume>? difficult = null, double? difficultCap = null,
        IReadOnlyList<WorldVolume>? steep = null, IReadOnlyList<PlacedModel>? models = null, MoveUnits? units = null)
    {
        if (ContactDistance(start, target, limit, impassable, table, models, units, volumes) is not { } shortest) return false;
        var slowing = Slowing(start, limit, shortest, difficult, difficultCap, steep);
        var cap = slowing.Count > 0 ? difficultCap + UnitFormation.ContactGap : null;
        var level = target.Bottom();
        var origin = start.Placement.Position.Horizontal;
        var (centre, bound) = start.BaseOutline().BoundingCircle();
        var across = 2 * (bound + centre.DistanceTo(origin));                // a landing touching the target lies within this of its base
        var near = volumes.Where(v => Distance.EdgeToEdge(target, v) <= across + UnitFormation.ContactGap + Tolerance.Distance).ToList();
        if (slowing.Count == 0 && level <= vertical + Tolerance.Distance
            && near.All(v => (v.Volume.Kind == VolumeKind.Solid ? v.WorldTop() : v.WorldBottom()) <= vertical + Tolerance.Distance))
            return true;
        var reach = limit + UnitFormation.ContactGap;
        foreach (var at in Contacts(start, target, UnitFormation.ContactGap, 16))
        {
            if (origin.DistanceTo(at) > reach + Tolerance.Distance) continue;
            var there = start with { Placement = start.Placement with { Position = new Point3(at.X, at.Y, level) } };
            if (Math.Abs(Collision.RestNear(there, near, level) - level) > vertical + Tolerance.Distance) continue;
            if (Check(start, at, reach, impassable, slowing, cap, table, models, volumes, units: units)
                is { WithinAllowance: true, CrossesImpassable: false, LeavesTable: false, CrossesModels: false, NearModels: false }) return true;
        }
        return false;
    }

    /// <summary>The difficult volumes (steep ones the start is off included) that could bind a reach: none without a cap, when the cap is not under the
    /// limit, when the shortest path is within the cap, or when none lies within the limit of the start's base.</summary>
    static IReadOnlyList<WorldVolume> Slowing(PlacedModel start, double limit, double shortest, IReadOnlyList<WorldVolume>? difficult, double? cap,
        IReadOnlyList<WorldVolume>? steep)
    {
        if (cap is not { } c || c >= limit - Tolerance.Distance || shortest <= c + Tolerance.Distance) return [];
        IReadOnlyList<WorldVolume> all = [.. difficult ?? [], .. steep is { Count: > 0 } ? SteepEntering(start, steep) : []];
        return all.Where(v => Distance.EdgeToEdge(start, v) <= limit + Tolerance.Distance).ToList();
    }

    /// <summary>
    /// Where the start's base (its own facing) stands `gap` clear of the target's base, one point on each of `bearings` directions about the
    /// target's centre: the direction facing the start first, then alternately either side of it. A charge's landings in base contact (p.9);
    /// the caller judges each as a move.
    /// </summary>
    public static IReadOnlyList<Point2> ContactPoints(PlacedModel start, PlacedModel target, double gap, int bearings = 32) => [.. Contacts(start, target, gap, bearings)];

    /// <summary>ContactPoints one at a time, each found only when asked for: a caller that stops at the first that serves finds no more.</summary>
    internal static IEnumerable<Point2> Contacts(PlacedModel start, PlacedModel target, double gap, int bearings = 32)
    {
        var centre = target.Placement.Position.Horizontal;
        var facing = start.Placement.Position.Horizontal - centre;
        var bearing = facing.Length > 1e-9 ? Math.Atan2(facing.Y, facing.X) : 0;
        var other = target.BaseOutline();
        var probe = ContactProbe.For(start, target);
        var offsets = probe is null ? start.BaseOffsets() : null;
        var radius = start.Body.Base.A / 2;
        for (var k = 0; k < bearings; k++)
        {
            var angle = bearing + (k + 1) / 2 * (k % 2 == 0 ? -1 : 1) * Math.Tau / bearings;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            // Under gap of the target with the base centred r along dir: Distance.EdgeToEdge(start there, target) < gap, the outline built as BaseOutline builds it.
            bool Under(double r)
            {
                var at = new Point2(centre.X + dir.X * r, centre.Y + dir.Y * r);
                if (probe is not null) return probe.Under(at, gap);
                Outline there = offsets is null ? new CircleOutline(at, radius) : Bodies.BaseAt(offsets, at);
                return Outline.Separation(there, other).Distance < gap;
            }
            double lo = 0, hi = 1;
            while (Under(hi) && hi < 1024) hi *= 2;
            for (var i = 0; i < 40; i++) { var mid = (lo + hi) / 2; if (Under(mid)) lo = mid; else hi = mid; }
            yield return centre + dir * hi;
        }
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
