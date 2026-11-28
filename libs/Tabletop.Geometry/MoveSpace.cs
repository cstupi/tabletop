using System.Runtime.CompilerServices;

namespace Tabletop.Geometry;

/// <summary>
/// One base's moves from where it stands, in its configuration space: every obstacle as a <see cref="MoveShape"/>, the table as the box the base
/// centre must stay in, and the corners round each obstacle (<see cref="MoveShape.Ring"/>) that a shortest path bends at. Built once per base and
/// asked many times (MoveSpace.Search.cs). Obstacles come in groups a search turns on or off: impassable walls, other models' bodies (a base, or a
/// unit's perimeter), a unit's perimeter grown by the separation and, beside it, its plain perimeter; and the difficult, dangerous and unstable
/// (dangerous only to a path over a length) volumes a path avoids when it can.
/// </summary>
internal sealed partial class MoveSpace
{
    /// <summary>`Bodies`: bases, and a unit's perimeter with no grown one beside it. `Spacing`: a unit's perimeter grown by the separation.
    /// `Perimeter`: the plain perimeter of a unit whose grown one is in Spacing, which only the search that lets the separation go reads.</summary>
    public const int Walls = 1, Bodies = 2, Difficult = 4, Dangerous = 8, Unstable = 16, Spacing = 32, Perimeter = 64;
    /// <summary>The groups whose obstacles are models' bodies, met in plan and height.</summary>
    public const int Models = Bodies | Spacing | Perimeter;
    /// <summary>What holds every move: walls, bodies and the separation.</summary>
    public const int Held = Walls | Bodies | Spacing;
    /// <summary>How far outside an obstacle its graph corners stand.</summary>
    const double Margin = 1e-3;

    /// <summary>`AlwaysMeets`: a body whose height interval meets the base's at every height the base can rest at inside its shape, so no
    /// position there needs the base rested to know (MeetsInHeight is true wherever Holds is). `Rests`: for a body, the surfaces the base centred
    /// inside its shape can meet (Near), highest first; the base rests on the first it meets there, else on the table.</summary>
    internal sealed record Obstacle(MoveShape Shape, int Group, Guid Id, double Bottom, double Top, bool StartInside, bool AlwaysMeets = false,
        Rest[]? Rests = null);

    /// <summary>A surface the base may rest on: its footprint, the circle that holds it, and the height the base rests at on it.</summary>
    internal readonly record struct Rest(Outline Outline, Point2 Centre, double Radius, double Height);

    /// <summary>A graph corner: its point, the obstacle it rounds and its two neighbours on that obstacle's ring, the groups whose obstacles hold it.</summary>
    internal readonly record struct Node(Point2 At, Point2 Prev, Point2 Next, int Inside);

    readonly PlacedModel _mover;
    readonly Outline _base;
    readonly (Point2 Centre, double Radius) _baseBound;
    readonly IReadOnlyList<WorldVolume> _surfaces;
    readonly (double X0, double X1, double Y0, double Y1)? _box;
    readonly List<Obstacle> _obstacles = [];
    List<Node>? _nodes;

    public Point2 Origin { get; }
    public int Present { get; private set; }
    public IReadOnlyList<Obstacle> Obstacles => _obstacles;

    /// <summary>
    /// Obstacles whose shape comes within `reach` of the start (a path no longer than that never meets the others). `table` keeps the whole base on
    /// the slab, unless the base is already off it (only a hand move puts it there). A model whose body the base already overlaps is left out, as a
    /// body it starts in does not stop it; a model meets the base only where their heights overlap too, the base rested on `surfaces` where it stands.
    /// With `units` (MoveUnits), each unit other than the mover's own and those held as bases is one obstacle, its perimeter (the convex hull of its
    /// bases, UnitCore, from its lowest base to its tallest top): grown by the separation in Spacing, with the plain one in Perimeter; the plain one
    /// alone in Bodies when the base starts inside the grown one, or there is no separation; its bases in Bodies when the base starts inside the
    /// plain one. Each body obstacle carries its unit's id.
    /// </summary>
    public MoveSpace(PlacedModel start, double reach, IReadOnlyList<WorldVolume> impassable, IReadOnlyList<WorldVolume> difficult,
        IReadOnlyList<WorldVolume> dangerous, TableSize? table, IReadOnlyList<PlacedModel>? models, IReadOnlyList<WorldVolume>? surfaces,
        IReadOnlyList<WorldVolume>? unstable = null, MoveUnits? units = null)
    {
        _mover = start;
        Origin = start.Placement.Position.Horizontal;
        _base = start.BaseOutline();
        _baseBound = _base.BoundingCircle();
        _surfaces = surfaces ?? [];
        if (table is { } t)
        {
            var (x0, x1, y0, y1) = Extent(_base, Origin);
            var box = (X0: -x0, X1: t.Width - x1, Y0: -y0, Y1: t.Length - y1);
            if (Origin.X >= box.X0 - Tolerance.Distance && Origin.X <= box.X1 + Tolerance.Distance && Origin.Y >= box.Y0 - Tolerance.Distance && Origin.Y <= box.Y1 + Tolerance.Distance)
                _box = box;
        }
        void Add(IEnumerable<WorldVolume> volumes, int group)
        {
            foreach (var v in volumes)
            {
                var shape = MoveShape.Of(v.WorldOutline(), _base, Origin);
                if (shape.Centre.DistanceTo(Origin) > reach + shape.Bound + Tolerance.Distance) continue;
                _obstacles.Add(new Obstacle(shape, group, v.Id, 0, 0, group == Walls && shape.Holds(Origin)));
                Present |= group;
            }
        }
        Add(impassable, Walls);
        Add(difficult, Difficult);
        Add(dangerous, Dangerous);
        Add(unstable ?? [], Unstable);
        if (models is not { Count: > 0 }) return;
        if (units is null)
        {
            foreach (var m in models) AddBase(m, Guid.Empty, reach);
            return;
        }
        if (units.Of.Count != models.Count) throw new ArgumentException($"{units.Of.Count} units for {models.Count} models", nameof(units));
        foreach (var unit in Enumerable.Range(0, models.Count).GroupBy(i => units.Of[i]))
        {
            if (unit.Key == units.Own || units.Bases?.Contains(unit.Key) == true) foreach (var i in unit) AddBase(models[i], unit.Key, reach);
            else AddUnit([.. unit.Select(i => models[i])], unit.Key, units.Separation ?? 0, reach);
        }
    }

    /// <summary>A model's base as a body, unless it is out of reach or the base starts on it.</summary>
    void AddBase(PlacedModel m, Guid unit, double reach)
    {
        var outline = m.BaseOutline();
        if (MoveShape.CircleBounds(outline, _base, Origin) is { } far && far.Centre.DistanceTo(Origin) > reach + far.Bound + Tolerance.Distance) return;
        var shape = MoveShape.Of(outline, _base, Origin);
        if (Beyond(shape, reach) || Overlap.Bases(_mover, m)) return;
        Add(Body(shape, Bodies, unit, m.Bottom(), m.Top()));
    }

    /// <summary>A unit's perimeter (UnitCore), grown by `grow` where the base starts outside the grown one; the plain one where it starts outside
    /// that; else its bases.</summary>
    void AddUnit(IReadOnlyList<PlacedModel> members, Guid unit, double grow, double reach)
    {
        var (core, radius) = UnitCore(members);
        double bottom = members.Min(m => m.Bottom()), top = members.Max(m => m.Top());
        var plain = MoveShape.Of(core, radius, _base, Origin);
        if (grow > Tolerance.Distance)
        {
            var grown = MoveShape.Of(core, radius + grow, _base, Origin);
            if (Beyond(grown, reach)) return;
            var outer = Body(grown, Spacing, unit, bottom - grow, top + grow);
            if (!HoldsAt(outer, Origin))
            {
                Add(outer);
                if (!Beyond(plain, reach)) Add(Body(plain, Perimeter, unit, bottom, top));
                return;
            }
        }
        if (Beyond(plain, reach)) return;
        var body = Body(plain, Bodies, unit, bottom, top);
        if (!HoldsAt(body, Origin)) { Add(body); return; }
        foreach (var m in members) AddBase(m, unit, reach);
    }

    bool Beyond(MoveShape shape, double reach) => shape.Centre.DistanceTo(Origin) > reach + shape.Bound + Tolerance.Distance;

    Obstacle Body(MoveShape shape, int group, Guid unit, double bottom, double top)
    {
        var rests = Near(shape);
        return new Obstacle(shape, group, unit, bottom, top, false, MeetsAtEveryRest(rests, bottom, top), rests);
    }

    void Add(Obstacle o)
    {
        _obstacles.Add(o);
        Present |= o.Group;
    }

    /// <summary>Within this of its smallest circle a unit of circles of different sizes keeps a round core: the rest of each circle enters the hull
    /// as its circumscribed polygon, so the hull holds every base and stands at most about 0.2% of that rest beyond it.</summary>
    const double HullInset = 0.05;
    const int HullSides = 16;

    /// <summary>
    /// A unit's perimeter as a convex core (counter-clockwise) and a radius: the convex hull of its models' bases. One base is itself. Bases all
    /// circles of one size: the hull of their centres grown by the radius, exactly, unless the centres lie on one line; circles otherwise: each the
    /// polygon circumscribing it less a radius HullInset under the smallest, which the core is grown by. With a rect or oval base: the polygons'
    /// vertices and each circle's circumscribed 16-gon, grown by nothing.
    /// </summary>
    internal static (Point2[] Core, double Radius) UnitCore(IReadOnlyList<PlacedModel> members)
    {
        var outlines = members.Select(m => m.BaseOutline()).ToList();
        if (outlines.Count == 1)
            return outlines[0] switch
            {
                CircleOutline c => ([c.Centre], c.Radius),
                PolygonOutline p => ([.. p.Vertices], 0),
                _ => throw new ArgumentOutOfRangeException(nameof(members)),
            };
        var circles = outlines.OfType<CircleOutline>().ToList();
        if (circles.Count == outlines.Count)
        {
            var least = circles.Min(c => c.Radius);
            if (circles.All(c => c.Radius - least < 1e-12) && Geometry.Bodies.ConvexHull([.. circles.Select(c => c.Centre)]) is { Count: not 2 } centres)
                return ([.. centres], least);
            var inner = Math.Max(0, least - HullInset);
            return ([.. Geometry.Bodies.ConvexHull([.. circles.SelectMany(c => Around(c.Centre, c.Radius - inner))])], inner);
        }
        return ([.. Geometry.Bodies.ConvexHull([.. outlines.SelectMany(o => o switch
        {
            CircleOutline c => Around(c.Centre, c.Radius),
            PolygonOutline p => (IEnumerable<Point2>)p.Vertices,
            _ => throw new ArgumentOutOfRangeException(nameof(members)),
        })])], 0);
    }

    /// <summary>The polygon of HullSides circumscribing the circle; its centre alone for a radius of nothing.</summary>
    static IEnumerable<Point2> Around(Point2 centre, double radius)
    {
        if (radius < 1e-9) { yield return centre; yield break; }
        var r = radius / Math.Cos(Math.PI / HullSides);
        for (var i = 0; i < HullSides; i++)
        {
            var t = 2 * Math.PI * i / HullSides;
            yield return centre + new Point2(Math.Cos(t), Math.Sin(t)) * r;
        }
    }

    /// <summary>
    /// The surfaces the base centred anywhere inside `shape` can rest on (Collision.Rest with floors: a Solid's top, an Area's floor), highest
    /// first: only one whose bounding circle comes within the base's reach of the shape's can be met, and one at or under the table never lifts it.
    /// A sample inside the shape rests over these alone (RestAt), not over every surface on the table.
    /// </summary>
    Rest[] Near(MoveShape shape)
    {
        var (bc, br) = _baseBound;
        var reach = shape.Bound + bc.DistanceTo(Origin) + br;
        var near = new List<Rest>();
        foreach (var v in _surfaces)
        {
            var h = v.Volume.Kind == VolumeKind.Solid ? v.WorldTop() : v.WorldBottom();
            if (h <= 0) continue;
            var outline = v.WorldOutline();
            var (c, r) = outline.BoundingCircle();
            if (c.DistanceTo(shape.Centre) > reach + r + Tolerance.Distance) continue;
            near.Add(new Rest(outline, c, r, h));
        }
        return [.. near.OrderByDescending(n => n.Height)];
    }

    /// <summary>
    /// The base centred anywhere inside the body's shape meets a body between these heights whatever it rests on there: it rests at 0 or at the
    /// height of a surface its base meets (Near).
    /// </summary>
    bool MeetsAtEveryRest(Rest[] rests, double bottom, double top)
    {
        var height = _mover.Body.Height;
        if (!Overlap.IntervalsOverlap(0, 0 + height, bottom, top)) return false;
        foreach (var r in rests)
            if (!Overlap.IntervalsOverlap(r.Height, r.Height + height, bottom, top)) return false;
        return true;
    }

    /// <summary>The ring corners, built on first use: a move whose straight line is clear never needs them.</summary>
    public IReadOnlyList<Node> Nodes
    {
        get
        {
            if (_nodes is not null) return _nodes;
            _nodes = [];
            foreach (var o in _obstacles)
            {
                var ring = o.Shape.Ring(Margin);
                for (var i = 0; i < ring.Count; i++)
                {
                    var at = ring[i];
                    if (!InBox(at)) continue;
                    var inside = 0;
                    foreach (var other in _obstacles)
                        if ((inside & other.Group) == 0 && Near(other, at) && HoldsAt(other, at)) inside |= other.Group;
                    _nodes.Add(new Node(at, ring[(i + ring.Count - 1) % ring.Count], ring[(i + 1) % ring.Count], inside));
                }
            }
            return _nodes;
        }
    }

    static bool Near(Obstacle o, Point2 p) => p.DistanceTo(o.Shape.Centre) <= o.Shape.Bound + Tolerance.Distance;

    /// <summary>The base outline's extent about its origin.</summary>
    static (double X0, double X1, double Y0, double Y1) Extent(Outline o, Point2 origin) => o switch
    {
        CircleOutline c => (c.Centre.X - c.Radius - origin.X, c.Centre.X + c.Radius - origin.X, c.Centre.Y - c.Radius - origin.Y, c.Centre.Y + c.Radius - origin.Y),
        PolygonOutline p => (p.Vertices.Min(v => v.X) - origin.X, p.Vertices.Max(v => v.X) - origin.X, p.Vertices.Min(v => v.Y) - origin.Y, p.Vertices.Max(v => v.Y) - origin.Y),
        _ => throw new ArgumentOutOfRangeException(nameof(o)),
    };

    /// <summary>Whether the table holds the base at p: always when there is no table to hold it to.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool InBox(Point2 p) => _box is not { } b
        || (p.X >= b.X0 - Tolerance.Distance && p.X <= b.X1 + Tolerance.Distance && p.Y >= b.Y0 - Tolerance.Distance && p.Y <= b.Y1 + Tolerance.Distance);

    public bool HasTable => _box is not null;

    /// <summary>The base centred at p meets this obstacle: in plan, and for a body in height too.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    bool HoldsAt(Obstacle o, Point2 p) => o.Shape.Holds(p) && ((o.Group & Models) == 0 || o.AlwaysMeets || MeetsInHeight(o, p));

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    bool MeetsInHeight(Obstacle o, Point2 p)
    {
        var bottom = RestAt(o.Rests ?? [], p);
        return Overlap.IntervalsOverlap(bottom, bottom + _mover.Body.Height, o.Bottom, o.Top);
    }

    /// <summary>Where the base centred at p rests (Collision.Rest with floors) over these surfaces, highest first: the first whose footprint
    /// shares interior with the base, else the table.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    double RestAt(Rest[] rests, Point2 p)
    {
        if (rests.Length == 0) return 0;
        var (bc, br) = _baseBound;
        var centre = bc + (p - Origin);
        Outline? shifted = null;
        foreach (var r in rests)
        {
            if (centre.DistanceTo(r.Centre) >= br + r.Radius) continue;
            shifted ??= _base.Shift(p - Origin);
            if (Outline.InteriorsIntersect(shifted, r.Outline)) return r.Height;
        }
        return 0;
    }

    internal enum Cover { Free, Edge, Covered }

    /// <summary>
    /// What the square of half-side `half` about `centre` holds: Covered when it lies wholly inside one obstacle of the groups in `covers` (a body
    /// only counts where the heights meet, so never covers) or wholly off the table; Edge when the boundary of an obstacle of the groups in `mask`, or
    /// the table's, may cross it; else Free.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public Cover Straddles(Point2 centre, double half, int mask, int covers)
    {
        var r = half * Math.Sqrt(2);
        if (_box is { } b)
        {
            if (centre.X + half < b.X0 || centre.X - half > b.X1 || centre.Y + half < b.Y0 || centre.Y - half > b.Y1) return Cover.Covered;
            if (centre.X - half < b.X0 || centre.X + half > b.X1 || centre.Y - half < b.Y0 || centre.Y + half > b.Y1) return Cover.Edge;
        }
        var edge = false;
        foreach (var o in _obstacles)
        {
            if ((o.Group & mask) == 0 || centre.DistanceTo(o.Shape.Centre) > o.Shape.Bound + r) continue;
            var depth = o.Shape.Depth(centre);
            if (depth < -r && (o.Group & covers & ~Models) != 0) return Cover.Covered;
            if (Math.Abs(depth) <= r || ((o.Group & Models) != 0 && depth < -r)) edge = true;
        }
        return edge ? Cover.Edge : Cover.Free;
    }

    /// <summary>The base centred at p meets an obstacle of these groups, or the table does not hold it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool Blocked(Point2 p, int mask)
    {
        if (!InBox(p)) return true;
        foreach (var o in _obstacles)
            if ((o.Group & mask) != 0 && Near(o, p) && HoldsAt(o, p)) return true;
        return false;
    }

    /// <summary>
    /// The base can go straight from a to b meeting no obstacle of these groups, the whole way on the table. From the start (`fromStart`), an
    /// impassable volume the base already stands in does not hold it on a line heading out of it — the core's signed distance growing from the first
    /// step, which on a straight line from a convex shape it then does all the way — so a base a hand move left in one may leave it, never along it or deeper in.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool Clear(Point2 a, Point2 b, int mask, bool fromStart = false)
    {
        if (!InBox(a) || !InBox(b)) return false;
        var d = b - a;
        var length = d.Length;
        if (length < 1e-12) return !Blocked(a, mask);
        foreach (var o in _obstacles)
        {
            if ((o.Group & mask) == 0) continue;
            if (Outline.ClosestOnSegment(a, b, o.Shape.Centre).DistanceTo(o.Shape.Centre) > o.Shape.Bound + Tolerance.Distance) continue;
            if (o.StartInside && fromStart && HeadsOut(o.Shape, a, d * (1 / length))) continue;
            if (!o.Shape.Blocks(a, b)) continue;
            if ((o.Group & Models) == 0 || Crosses(o, a, b)) return false;
        }
        return true;
    }

    /// <summary>A body the segment's plan meets is met where, at some sample of the part inside it, the heights overlap too.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    bool Crosses(Obstacle o, Point2 a, Point2 b)
    {
        // Every sample lies inside the shape, where such a body always meets the base: met as soon as some part is surely inside.
        if (o.AlwaysMeets && (_surfaces.Count == 0 || o.Shape.SurelyEnters(a, b))) return true;
        if (_surfaces.Count == 0) return MeetsInHeight(o, a);
        // Every sample rests on the table or on a surface the base can meet somewhere along a–b: when those heights all agree, the samples do.
        switch (MeetsAlong(o, a, b))
        {
            case false: return false;
            case true when o.Shape.SurelyEnters(a, b): return true;
        }
        if (o.Shape.Interval(a, b) is not { } span) return false;
        var d = b - a;
        var steps = Sampling.Steps((span.T1 - span.T0) * d.Length, Collision.Step);
        for (var k = 0; k <= steps; k++)
        {
            var t = span.T0 + (span.T1 - span.T0) * k / steps;
            if (MeetsInHeight(o, a + d * t)) return true;
        }
        return false;
    }

    /// <summary>Whether the base centred anywhere on a–b meets the body in height: true at every height it can rest at there (the table, or a
    /// surface of the body's Rests whose bounding circle comes within the base's of the segment), false at none of them, null when they differ.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    bool? MeetsAlong(Obstacle o, Point2 a, Point2 b)
    {
        var height = _mover.Body.Height;
        var meets = Overlap.IntervalsOverlap(0, height, o.Bottom, o.Top);
        var (bc, br) = _baseBound;
        var shift = bc - Origin;
        foreach (var r in o.Rests ?? [])
        {
            if (Overlap.IntervalsOverlap(r.Height, r.Height + height, o.Bottom, o.Top) == meets) continue;
            if (Outline.ClosestOnSegment(a + shift, b + shift, r.Centre).DistanceTo(r.Centre) < br + r.Radius) return null;
        }
        return meets;
    }

    static bool HeadsOut(MoveShape shape, Point2 from, Point2 dir)
    {
        const double h = 1e-3;
        return shape.CoreSigned(from + dir * h) - shape.CoreSigned(from) > 1e-9;
    }

    /// <summary>The first point along a–b where the way is no longer clear, a itself clear (bisected to 1e-4 of the length).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public Point2 Reach(Point2 a, Point2 b, int mask, bool fromStart)
    {
        if (Clear(a, b, mask, fromStart)) return b;
        double lo = 0, hi = 1;
        for (var k = 0; k < 14; k++) { var mid = (lo + hi) / 2; if (Clear(a, a + (b - a) * mid, mask, fromStart)) lo = mid; else hi = mid; }
        return a + (b - a) * lo;
    }

    /// <summary>The id of the first obstacle of these groups the path meets as Clear meets it (in plan, and a body in height too), in order along the
    /// path; null when it meets none. The unit a path goes through or passes near.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public Guid? FirstMet(IReadOnlyList<Point2> path, int mask)
    {
        for (var i = 0; i + 1 < path.Count; i++)
        {
            var (a, b) = (path[i], path[i + 1]);
            Obstacle? first = null;
            var at = double.PositiveInfinity;
            foreach (var o in _obstacles)
            {
                if ((o.Group & mask) == 0) continue;
                if (Outline.ClosestOnSegment(a, b, o.Shape.Centre).DistanceTo(o.Shape.Centre) > o.Shape.Bound + Tolerance.Distance) continue;
                if (!o.Shape.Blocks(a, b) || ((o.Group & Models) != 0 && !Crosses(o, a, b))) continue;
                var t = o.Shape.Interval(a, b)?.T0 ?? 0;
                if (t < at) (first, at) = (o, t);
            }
            if (first is not null) return first.Id;
        }
        return null;
    }

    /// <summary>The volumes of these groups the base meets anywhere along the path, sampled every Collision.Step, by id.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public HashSet<Guid> Touched(IReadOnlyList<Point2> path, int mask)
    {
        var touched = new HashSet<Guid>();
        for (var i = 0; i < path.Count; i++)
        {
            var a = path[i];
            var b = i + 1 < path.Count ? path[i + 1] : a;
            foreach (var o in _obstacles)
                if ((o.Group & mask) != 0 && !touched.Contains(o.Id) && Outline.ClosestOnSegment(a, b, o.Shape.Centre).DistanceTo(o.Shape.Centre) <= o.Shape.Bound + Tolerance.Distance
                    && (o.Shape.Holds(a) || o.Shape.Blocks(a, b))) touched.Add(o.Id);
        }
        return touched;
    }
}
