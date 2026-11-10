using System.Collections.Concurrent;

namespace Tabletop.Geometry;

/// <summary>A path for the base centre from its start, its length, and whether the base goes in or through a difficult volume on it.</summary>
public sealed record MovePath(IReadOnlyList<Point2> Points, double Distance, bool EntersDifficult);

/// <summary>What reaching a point costs (MoveReach.BandAt): nothing; only difficult ground, the cap holding the unit; only a volume dangerous to the
/// path.</summary>
public enum MoveBand { Clear, Difficult, Dangerous }

/// <summary>The reachable area in three disjoint parts, each as triangles (three points each); together they cover MoveReach.Triangles' area.</summary>
public sealed record MoveBands(IReadOnlyList<Point2> Clear, IReadOnlyList<Point2> Difficult, IReadOnlyList<Point2> Dangerous);

/// <summary>
/// Where one base can go from where it stands, measured along the path (rulebook p.5: "no part of its base moves further than the total
/// distance"): built once for a move Ask and asked many times by the client's drag, ruler and region. A destination counts as reached without
/// entering difficult terrain when a path round every difficult volume fits the allowance; otherwise a path through one must fit the cap (p.11).
/// Walls, the other models (in plan and height) and the table edge hold every path. The same choice <see cref="Movement.Check"/> makes, settled for
/// every corner at once rather than searched per destination. The bands (Bands, BandAt) split that area by what reaching a point costs.
/// </summary>
public sealed class MoveReach
{
    readonly MoveSpace _space;
    readonly MoveSpace.Field? _avoiding;
    readonly MoveSpace.Field _any;
    readonly double _capLimit;
    readonly double _unstableOver;
    readonly Lazy<Levels> _levels;
    readonly ConcurrentDictionary<(int Mask, double Limit), Lazy<MoveSpace.Field>> _fields = new();

    MoveReach(MoveSpace space, double allowance, double capLimit, bool capBinds, double unstableOver)
    {
        _space = space;
        Allowance = allowance;
        _capLimit = capLimit;
        _unstableOver = unstableOver;
        var walls = MoveSpace.Walls | MoveSpace.Bodies;
        _any = space.Settle(walls, allowance);
        if (capBinds) _avoiding = space.Settle(walls | MoveSpace.Difficult, allowance);
        _levels = new Lazy<Levels>(() => new Levels(this));
    }

    public Point2 Origin => _space.Origin;
    public double Allowance { get; }
    /// <summary>The cap binds somewhere: a difficult volume is within reach and the cap is under the allowance.</summary>
    public bool CappedByDifficultTerrain => _avoiding is not null;

    /// <summary>`models` stop the base where its body meets theirs, in plan and in height, the base rested on `surfaces` where it is; one it starts on
    /// does not. `dangerous` volumes are dangerous to any path that enters them, `unstable` ones only to a path longer than `unstableOver`; they
    /// change the bands (Bands, BandAt) and the route (a path goes round them when that fits, as Movement.Check's does), never what is reached or the
    /// clamp.</summary>
    public static MoveReach Build(PlacedModel start, double allowance, IReadOnlyList<WorldVolume> impassable, IReadOnlyList<WorldVolume> difficult,
        double? difficultCap, TableSize? table, IReadOnlyList<PlacedModel>? models = null, IReadOnlyList<WorldVolume>? surfaces = null,
        IReadOnlyList<WorldVolume>? dangerous = null, IReadOnlyList<WorldVolume>? unstable = null, double unstableOver = 0)
    {
        allowance = Math.Max(0, allowance);
        var binds = difficultCap is { } cap && cap < allowance;
        unstableOver = Math.Max(0, unstableOver);
        // No path within the allowance is longer than it, so an unstable volume is then never dangerous.
        if (unstableOver >= allowance - Tolerance.Distance) unstable = null;
        var space = new MoveSpace(start, allowance, impassable, binds ? difficult : [], dangerous ?? [], table, models, surfaces, unstable);
        // The cap binds only where a difficult volume is in reach: one listed out of reach caps nothing (Movement.Check fits round it within the allowance).
        binds &= (space.Present & MoveSpace.Difficult) != 0;
        var capLimit = binds ? Math.Max(0, difficultCap!.Value) : allowance;
        return new MoveReach(space, allowance, capLimit, binds, unstableOver);
    }

    /// <summary>
    /// The path a move to `to` is measured along, in Movement.Check's order: round every difficult and dangerous volume within the allowance; round
    /// every difficult one within it; round every dangerous one within the cap; else the shortest; the straight line when no path within the allowance
    /// reaches it (then its length says how far over it is). An unstable volume counts as dangerous once the path is longer than `unstableOver` and
    /// enters a dangerous or unstable one: the path is then chosen again, round both (Movement.Measured). `shortest`: the shortest whatever it enters,
    /// for a unit the cap already holds (p.11), which is judged by its shortest path.
    /// </summary>
    public MovePath Route(Point2 to, bool shortest = false)
    {
        if (shortest) return Shortest(to);
        var path = RouteRound(to, MoveSpace.Dangerous);
        if ((_space.Present & MoveSpace.Unstable) != 0 && path.Distance > _unstableOver + Tolerance.Distance
            && _space.Touched(path.Points, MoveSpace.Dangerous | MoveSpace.Unstable).Count > 0)
            path = RouteRound(to, MoveSpace.Dangerous | MoveSpace.Unstable);
        return path;
    }

    MovePath RouteRound(Point2 to, int danger)
    {
        const int walls = MoveSpace.Walls | MoveSpace.Bodies;
        danger &= _space.Present;
        if (_avoiding is not null)
        {
            if (danger != 0 && FieldOf(walls | MoveSpace.Difficult | danger, Allowance).Path(to) is { } both && both.Length <= Allowance + Tolerance.Distance)
                return new MovePath(both.Path, both.Length, false);
            if (_avoiding.Path(to) is { } round && round.Length <= Allowance + Tolerance.Distance) return new MovePath(round.Path, round.Length, false);
        }
        if (danger != 0 && FieldOf(walls | danger, _capLimit).Path(to) is { } safe && safe.Length <= _capLimit + Tolerance.Distance)
            return new MovePath(safe.Path, safe.Length, Enters(safe.Path));
        return Shortest(to);
    }

    MovePath Shortest(Point2 to)
    {
        if (_any.Path(to) is { } any) return new MovePath(any.Path, any.Length, Enters(any.Path));
        List<Point2> straight = [Origin, to];
        return new MovePath(straight, Origin.DistanceTo(to), Enters(straight));
    }

    /// <summary>Settles now the fields Route reads round dangerous and unstable ground (the decision box asks it on the pool when the Ask opens, so a
    /// drag frame never settles one); nothing when none is in reach.</summary>
    public void PrepareRoute()
    {
        const int walls = MoveSpace.Walls | MoveSpace.Bodies;
        foreach (var danger in new[] { MoveSpace.Dangerous, MoveSpace.Dangerous | MoveSpace.Unstable })
        {
            if ((danger & _space.Present) == 0) continue;
            if (_avoiding is not null) _ = FieldOf(walls | MoveSpace.Difficult | danger, Allowance);
            _ = FieldOf(walls | danger, _capLimit);
        }
    }

    /// <summary>The field round these groups (those not in reach dropped) within `limit`, settled once on first use, from any thread; the reach's own
    /// (settled within the allowance) for its groups. A field within the allowance serves a smaller limit too.</summary>
    MoveSpace.Field FieldOf(int mask, double limit)
    {
        const int walls = MoveSpace.Walls | MoveSpace.Bodies;
        mask = (mask & _space.Present & ~walls) | walls;
        if (mask == walls) return _any;
        if (_avoiding is not null && mask == (walls | MoveSpace.Difficult)) return _avoiding;
        if (_fields.TryGetValue((mask, Allowance), out var whole)) return whole.Value;
        return _fields.GetOrAdd((mask, limit), k => new Lazy<MoveSpace.Field>(() => _space.Settle(k.Mask, k.Limit))).Value;
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

    /// <summary>Past the limit by more than a mesh cell's diagonal a value only has to say "outside" (ReachMesh), so no search looks farther.</summary>
    const double Beyond = 2.5;

    /// <summary>How far past what it may reach a point is: negative inside.</summary>
    double Excess(Point2 p)
    {
        var any = _any.Distance(p, _capLimit + Beyond) - _capLimit;
        // A path round the difficult ground is no shorter than the straight line: when that alone cannot beat the value, it is not searched.
        if (_avoiding is null || any <= Origin.DistanceTo(p) - Allowance) return any;
        return Math.Min(any, _avoiding.Distance(p, Allowance + Beyond) - Allowance);
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

    /// <summary>
    /// What reaching the point costs, or null when no path within the allowance (through difficult ground, within the cap) gets there. Clear: a path
    /// within the allowance round every difficult volume (when the cap binds) and every volume dangerous to it. Difficult: else a path within the cap
    /// through difficult ground, round every volume dangerous to it. Dangerous: else. An unstable volume is dangerous only to a path longer than
    /// `unstableOver`, so a path no longer than that may enter it.
    /// </summary>
    public MoveBand? BandAt(Point2 p)
    {
        if (Excess(p) > Tolerance.Distance) return null;
        var levels = _levels.Value;
        var clear = levels.Clear(p);
        return clear <= Tolerance.Distance ? MoveBand.Clear : levels.Inner(p) <= Tolerance.Distance ? MoveBand.Difficult : MoveBand.Dangerous;
    }

    /// <summary>The reachable area (Triangles) split by BandAt, each band as triangles; `least` as Triangles takes it. A band nothing in reach gives is
    /// empty: with no difficult volume binding there is no Difficult band, with no volume dangerous to a path in reach no Dangerous band.</summary>
    public MoveBands Bands(Func<Point2, double>? least = null)
    {
        if (Allowance <= Tolerance.Distance) return new MoveBands([], [], []);
        var levels = _levels.Value;
        // Three nested levels, each at most zero inside: reached clear, reached round danger, reached at all. A band is one level less the one inside it.
        void Values(Point2 p, Span<double> into)
        {
            var r = Excess(p);
            double i, c;
            if (r > Beyond) i = c = r;                         // well outside the area only "outside" matters, and both are at least r
            else
            {
                c = Math.Max(levels.Clear(p), r);
                i = Math.Max(Math.Min(c, levels.Inner(p)), r);
            }
            if (least is not null)
            {
                var floor = least(p) - Origin.DistanceTo(p);
                r = Math.Max(r, floor); i = Math.Max(i, floor); c = Math.Max(c, floor);
            }
            into[0] = Finite(c);
            into[1] = Finite(Math.Max(i, -c));
            into[2] = Finite(Math.Max(r, -i));
        }
        var walls = MoveSpace.Walls | MoveSpace.Bodies | (_avoiding is null ? 0 : MoveSpace.Difficult) | MoveSpace.Dangerous | MoveSpace.Unstable;
        var bands = ReachMesh.Build(Origin, Allowance, 3, Values, (centre, half) => _space.Straddles(centre, half, walls, MoveSpace.Walls));
        return new MoveBands(bands[0], bands[1], bands[2]);
    }

    static double Finite(double v) => double.IsFinite(v) ? v : v > 0 ? 1e9 : -1e9;

    /// <summary>
    /// The fields the bands read, settled on first use (Bands, BandAt), each term a path length less its limit, a level the least of its terms.
    /// Clear: round difficult (when the cap binds), dangerous and unstable ground within the allowance, or round difficult and dangerous ground only
    /// within the allowance and `unstableOver`. Inner adds: round dangerous and unstable ground within the cap, or round dangerous ground only within
    /// the cap and `unstableOver`. A field whose groups add nothing in reach is the reach's own (_any, _avoiding), so a table with no dangerous or
    /// unstable volume in reach settles nothing more.
    /// </summary>
    sealed class Levels
    {
        readonly Point2 _origin;
        readonly (MoveSpace.Field Field, double Limit)[] _clear, _inner;

        public Levels(MoveReach reach)
        {
            _origin = reach.Origin;
            var space = reach._space;
            const int walls = MoveSpace.Walls | MoveSpace.Bodies;
            var difficult = reach._avoiding is null ? 0 : MoveSpace.Difficult;
            int dangerous = space.Present & MoveSpace.Dangerous, unstable = space.Present & MoveSpace.Unstable;
            var wanted = new List<(int Mask, double Limit, bool Clear)> { (walls | difficult | dangerous | unstable, reach.Allowance, true) };
            if (unstable != 0) wanted.Add((walls | difficult | dangerous, Math.Min(reach.Allowance, reach._unstableOver), true));
            if (difficult != 0)
            {
                wanted.Add((walls | dangerous | unstable, reach._capLimit, false));
                if (unstable != 0) wanted.Add((walls | dangerous, Math.Min(reach._capLimit, reach._unstableOver), false));
            }
            _clear = [.. wanted.Where(w => w.Clear).Select(w => (reach.FieldOf(w.Mask, w.Limit), w.Limit))];
            _inner = [.. wanted.Where(w => !w.Clear).Select(w => (reach.FieldOf(w.Mask, w.Limit), w.Limit))];
        }

        public double Clear(Point2 p) => Least(_clear, p);

        /// <summary>The terms Inner adds to Clear's; the inner level is the least of both.</summary>
        public double Inner(Point2 p) => Least(_inner, p);

        double Least((MoveSpace.Field Field, double Limit)[] terms, Point2 p)
        {
            var best = double.PositiveInfinity;
            var straight = _origin.DistanceTo(p);
            foreach (var (field, limit) in terms)
            {
                // No path is shorter than the straight line: a term that cannot beat the value so far is not searched.
                if (straight - limit >= best) continue;
                best = Math.Min(best, field.Distance(p, limit + Beyond) - limit);
            }
            return best;
        }
    }
}
