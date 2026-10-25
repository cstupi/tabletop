namespace Tabletop.Geometry;

/// <summary>A path for the base centre from its start, its length, and whether the base goes in or through a difficult volume on it.</summary>
public sealed record MovePath(IReadOnlyList<Point2> Points, double Distance, bool EntersDifficult);

/// <summary>
/// Where one base can go from where it stands, measured along the path (rulebook p.5: "no part of its base moves further than the total
/// distance"): built once for a move Ask and asked many times by the client's drag, ruler and region. A destination counts as reached without
/// entering difficult terrain when a path round every difficult volume fits the allowance; otherwise a path through one must fit the cap (p.11).
/// Walls, the other models (in plan and height) and the table edge hold every path. The same choice <see cref="Movement.Check"/> makes, settled for
/// every corner at once rather than searched per destination.
/// </summary>
public sealed class MoveReach
{
    readonly MoveSpace _space;
    readonly MoveSpace.Field? _avoiding;
    readonly MoveSpace.Field _any;
    readonly double _capLimit;

    MoveReach(MoveSpace space, double allowance, double capLimit, bool capBinds)
    {
        _space = space;
        Allowance = allowance;
        _capLimit = capLimit;
        var walls = MoveSpace.Walls | MoveSpace.Bodies;
        _any = space.Settle(walls, allowance);
        if (capBinds) _avoiding = space.Settle(walls | MoveSpace.Difficult, allowance);
    }

    public Point2 Origin => _space.Origin;
    public double Allowance { get; }
    /// <summary>The cap binds somewhere: a difficult volume is within reach and the cap is under the allowance.</summary>
    public bool CappedByDifficultTerrain => _avoiding is not null;

    /// <summary>`models` stop the base where its body meets theirs, in plan and in height, the base rested on `surfaces` where it is; one it starts on does not.</summary>
    public static MoveReach Build(PlacedModel start, double allowance, IReadOnlyList<WorldVolume> impassable, IReadOnlyList<WorldVolume> difficult,
        double? difficultCap, TableSize? table, IReadOnlyList<PlacedModel>? models = null, IReadOnlyList<WorldVolume>? surfaces = null)
    {
        allowance = Math.Max(0, allowance);
        var binds = difficultCap is { } cap && cap < allowance;
        var space = new MoveSpace(start, allowance, impassable, binds ? difficult : [], [], table, models, surfaces);
        var capLimit = binds ? Math.Max(0, difficultCap!.Value) : allowance;
        return new MoveReach(space, allowance, capLimit, binds && (space.Present & MoveSpace.Difficult) != 0);
    }

    /// <summary>
    /// The path a move to `to` is measured along: round every difficult volume when that fits the allowance, else the shortest; the straight line when
    /// no path within the allowance reaches it (then its length says how far over it is). `shortest`: the shortest whatever it enters, for a unit the
    /// cap already holds (p.11), where going round no longer buys anything.
    /// </summary>
    public MovePath Route(Point2 to, bool shortest = false)
    {
        if (!shortest && _avoiding?.Path(to) is { } round && round.Length <= Allowance + Tolerance.Distance) return new MovePath(round.Path, round.Length, false);
        if (_any.Path(to) is { } any) return new MovePath(any.Path, any.Length, Enters(any.Path));
        List<Point2> straight = [Origin, to];
        return new MovePath(straight, Origin.DistanceTo(to), Enters(straight));
    }

    bool Enters(IReadOnlyList<Point2> path) => _avoiding is not null && _space.Touched(path, MoveSpace.Difficult).Count > 0;

    /// <summary>
    /// The target held to the reach: untouched when a path within the allowance (or, through difficult terrain, within the cap) gets there, else the
    /// point nearest it that one does. With `cap`, every path is held to it (the unit is capped: p.11 "all models in the unit may not move more than 6"").
    /// </summary>
    public Point2 Clamp(Point2 target, double? cap = null)
    {
        if (cap is { } c)
        {
            var limit = Math.Min(Allowance, c);
            return _any.Distance(target) <= limit + Tolerance.Distance ? target : _any.Nearest(target, limit)?.At ?? Origin;
        }
        if (Excess(target) <= Tolerance.Distance) return target;
        var best = _any.Nearest(target, _capLimit);
        if (_avoiding?.Nearest(target, Allowance) is { } round && (best is null || round.Gap < best.Value.Gap)) best = round;
        return best?.At ?? Origin;
    }

    /// <summary>Whether a path within the allowance (through difficult terrain, within the cap; with `cap`, within it) gets to the point: exactly where
    /// <see cref="Clamp"/> leaves it as it is, without searching for the nearest point when it does not.</summary>
    public bool Reaches(Point2 target, double? cap = null) => cap is { } c
        ? _any.Distance(target) <= Math.Min(Allowance, c) + Tolerance.Distance
        : Excess(target) <= Tolerance.Distance;

    /// <summary>How far past what it may reach a point is: negative inside.</summary>
    double Excess(Point2 p)
    {
        // Past the limit by more than a mesh cell's diagonal the value only has to say "outside" (ReachMesh), so no search looks farther.
        const double beyond = 2.5;
        var any = _any.Distance(p, _capLimit + beyond) - _capLimit;
        // A path round the difficult ground is no shorter than the straight line: when that alone cannot beat the value, it is not searched.
        if (_avoiding is null || any <= Origin.DistanceTo(p) - Allowance) return any;
        return Math.Min(any, _avoiding.Distance(p, Allowance + beyond) - Allowance);
    }

    /// <summary>
    /// The reachable area as triangles (three points each), for drawing (ReachMesh). With `least`, the area is also held to at least that far from
    /// the start in a straight line (a minimum move, rulebook p.13 Aircraft), so it is a ring.
    /// </summary>
    public IReadOnlyList<Point2> Triangles(Func<Point2, double>? least = null)
    {
        if (Allowance <= Tolerance.Distance) return [];
        double Value(Point2 p)
        {
            var v = Excess(p);
            if (least is not null) v = Math.Max(v, least(p) - Origin.DistanceTo(p));
            return double.IsFinite(v) ? v : 1e9;
        }
        var walls = MoveSpace.Walls | MoveSpace.Bodies | (_avoiding is null ? 0 : MoveSpace.Difficult);
        return ReachMesh.Build(Origin, Allowance, Value, (centre, half) => _space.Straddles(centre, half, walls, MoveSpace.Walls));
    }
}
