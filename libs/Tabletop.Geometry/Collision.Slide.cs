namespace Tabletop.Geometry;

public static partial class Collision
{
    /// <summary>The path is sampled this finely, then bisected to <see cref="Contact"/>; a 0.5" wall cannot be stepped over.</summary>
    public const double Step = 0.1;
    const double Contact = 1e-3;

    /// <summary>
    /// A model dragged toward `to`: to the first contact with another model's body or a wall, then along it (<see cref="Move"/>), rested.
    /// At a level (held pieces lift over spec → At a level) the body rests at each sample on its open surface nearest the level over the
    /// volumes less the walls it can meet, so a wall's top is never stood on mid-drag, and a wall stops it only where their height
    /// intervals overlap: it passes under a Solid whose bottom is at or above its top. A wall it starts in stays ground, so a model on a
    /// bridge stays on it. <see cref="double.PositiveInfinity"/> is today's drag: the highest rest, every wall stopping it in plan.
    /// </summary>
    public static Point3 Slide(PlacedModel moving, Point2 to, IReadOnlyList<PlacedModel> models, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes,
        double level = double.PositiveInfinity)
    {
        var from = moving.Placement.Position.Horizontal;
        var body = PartsOf(moving);
        Outline[] shape = [body[0].Outline];
        var bodies = models.Where(m => !Overlap.Bases(moving, m)).Select(m => (Body: m, Outline: m.BaseOutline())).ToList();
        var sides = Sides(walls, shape);
        var ground = Ground(volumes, sides, level);
        double Bottom(IReadOnlyList<Part> at) => Nearest(Open(at, ground, floors: true), level);
        Outline? Obstacle(Point2 by)
        {
            var parts = Shifted(body, by);
            var bottom = Bottom(parts);
            foreach (var (other, outline) in bodies)
                if (Overlap.IntervalsOverlap(bottom, bottom + moving.Body.Height, other.Bottom(), other.Top()) && Outline.InteriorsIntersect(parts[0].Outline, outline)) return outline;
            return Meets(sides, parts, bottom, level);
        }
        var end = Move(shape, from, to, Obstacle);
        return new Point3(end.X, end.Y, Bottom(Shifted(body, end - from)));
    }

    /// <summary>A terrain piece dragged toward `to`, as a model is (<see cref="Slide(PlacedModel, Point2, IReadOnlyList{PlacedModel}, IReadOnlyList{WorldVolume}, IReadOnlyList{WorldVolume}, double)"/>), each of its volumes at its own heights.</summary>
    public static TerrainTransform Slide(IReadOnlyList<WorldVolume> piece, Point2 to, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes, double level = double.PositiveInfinity)
    {
        var origin = piece[0].Transform;
        var from = origin.Position.Horizontal;
        var body = PartsOf(piece);
        var shape = body.Select(p => p.Outline).ToList();
        var sides = Sides(Others(piece, walls), shape);
        var ground = Ground(Others(piece, volumes), sides, level);
        Outline? Obstacle(Point2 by)
        {
            var parts = Shifted(body, by);
            return Meets(sides, parts, Nearest(Open(parts, ground, floors: false), level), level);
        }
        var end = Move(shape, from, to, Obstacle);
        return double.IsPositiveInfinity(level) ? Rested(piece, end, volumes) : origin with { Position = new Point3(end.X, end.Y, Nearest(Open(Shifted(body, end - from), ground, floors: false), level)) };
    }

    /// <summary>
    /// A terrain piece in hand: it goes straight to the cursor when its footprint there meets no wall (<see cref="Blocked"/>); otherwise it slides
    /// from where it is toward it (<see cref="Slide(IReadOnlyList{WorldVolume}, Point2, IReadOnlyList{WorldVolume}, IReadOnlyList{WorldVolume}, double)"/>),
    /// so it never stays trapped behind a wall the cursor has passed. Rests either way, near the level.
    /// </summary>
    public static TerrainTransform Follow(IReadOnlyList<WorldVolume> piece, Point2 to, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes, double level = double.PositiveInfinity)
    {
        var at = Moved(piece, to);
        return Blocked(at, walls, volumes, level) ? Slide(piece, to, walls, volumes, level) : Rested(piece, to, Ground(Others(piece, volumes), Others(piece, walls), level), level);
    }

    /// <summary>
    /// The piece's footprints share area with a Solid wall that is not its own. At a finite level only a wall whose height interval overlaps a
    /// volume's, the piece rested near the level on the volumes less the walls, blocks it: it fits under a bridge with room.
    /// </summary>
    public static bool Blocked(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume>? volumes = null, double level = double.PositiveInfinity)
    {
        var body = PartsOf(piece);
        var solid = Others(piece, walls).Where(w => w.Volume.Kind == VolumeKind.Solid).ToList();
        var bottom = double.IsPositiveInfinity(level) ? 0 : Nearest(Open(body, Ground(Others(piece, volumes ?? []), solid, level), floors: false), level);
        return Meets(solid, body, bottom, level) is not null;
    }

    /// <summary>The ground a body rests on mid-drag: at a finite level every volume but the walls it can meet; today's drag rests on everything.</summary>
    static IReadOnlyList<WorldVolume> Ground(IReadOnlyList<WorldVolume> volumes, IReadOnlyList<WorldVolume> sides, double level)
    {
        if (double.IsPositiveInfinity(level)) return volumes;
        var walls = sides.Select(w => w.Id).ToHashSet();
        return volumes.Where(v => !walls.Contains(v.Id)).ToList();
    }

    /// <summary>The first wall a part shares interior with, standing at `bottom`; at a finite level only where their height intervals overlap.</summary>
    static Outline? Meets(IReadOnlyList<WorldVolume> walls, IReadOnlyList<Part> parts, double bottom, double level)
    {
        foreach (var w in walls)
        {
            var outline = w.WorldOutline();
            foreach (var p in parts)
                if (Outline.InteriorsIntersect(p.Outline, outline)
                    && (double.IsPositiveInfinity(level) || Overlap.IntervalsOverlap(bottom + p.Bottom, bottom + p.Top, w.WorldBottom(), w.WorldTop()))) return outline;
        }
        return null;
    }

    /// <summary>Rings this far apart, searched outward for a free spot; and how far out the search goes.</summary>
    const double ClearStep = 0.25, ClearReach = 144;

    /// <summary>
    /// A piece where it stands if that meets no wall (<see cref="Blocked"/> at the level), else at the nearest spot that does (<see cref="Ring"/>):
    /// how a turn that swings a piece into another is undone by moving it, not by refusing the turn, and where a lifted piece lands. Rests
    /// near the level. The piece unmoved when nothing within <see cref="ClearReach"/> is free.
    /// </summary>
    public static TerrainTransform Clear(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes, double level = double.PositiveInfinity)
    {
        var from = piece[0].Transform.Position.Horizontal;
        var ground = Ground(Others(piece, volumes), Others(piece, walls), level);
        var by = Ring(by => !Blocked(Moved(piece, from + by), walls, volumes, level)) ?? new Point2(0, 0);
        return Rested(piece, from + by, ground, level);
    }

    /// <summary>The first offset `free` takes: none, then rings <see cref="ClearStep"/> apart out to `reach` (<see cref="ClearReach"/>); null when none is.</summary>
    static Point2? Ring(Func<Point2, bool> free, double reach = ClearReach)
    {
        if (free(new Point2(0, 0))) return new Point2(0, 0);
        for (var r = ClearStep; r <= reach; r += ClearStep)
        {
            var n = Math.Max(16, (int)Math.Ceiling(2 * Math.PI * r / ClearStep));
            for (var i = 0; i < n; i++)
            {
                var t = 2 * Math.PI * i / n;
                var by = new Point2(Math.Cos(t), Math.Sin(t)) * r;
                if (free(by)) return by;
            }
        }
        return null;
    }

    static List<WorldVolume> Others(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> walls)
    {
        var own = piece.Select(v => v.Id).ToHashSet();
        return walls.Where(w => !own.Contains(w.Id)).ToList();
    }

    static List<WorldVolume> Moved(IReadOnlyList<WorldVolume> piece, Point2 to) =>
        piece.Select(v => v with { Transform = v.Transform with { Position = new Point3(to.X, to.Y, 0) } }).ToList();

    static TerrainTransform Rested(IReadOnlyList<WorldVolume> piece, Point2 at, IReadOnlyList<WorldVolume> volumes, double level = double.PositiveInfinity) =>
        piece[0].Transform with { Position = new Point3(at.X, at.Y, RestNear(Moved(piece, at), volumes, level)) };

    /// <summary>Each held body slides against the stationary ones and the held bodies already placed, in drag order, at the level.</summary>
    public static IReadOnlyList<Point3> Separate(IReadOnlyList<(PlacedModel Moving, Point2 To)> held, IReadOnlyList<PlacedModel> stationary, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes,
        double level = double.PositiveInfinity)
    {
        var placed = stationary.ToList();
        var ends = new List<Point3>(held.Count);
        foreach (var (moving, to) in held)
        {
            var end = Slide(moving, to, placed, walls, volumes, level);
            ends.Add(end);
            placed.Add(moving with { Placement = moving.Placement with { Position = end } });
        }
        return ends;
    }

    /// <summary>The Solid walls the body can meet: those its footprint does not start in, which it may leave.</summary>
    static List<WorldVolume> Sides(IReadOnlyList<WorldVolume> walls, IReadOnlyList<Outline> shape) =>
        walls.Where(w => w.Volume.Kind == VolumeKind.Solid && !shape.Any(o => Outline.InteriorsIntersect(o, w.WorldOutline()))).ToList();

    static IReadOnlyList<Outline> Shifted(IReadOnlyList<Outline> shape, Point2 by) => shape.Select(o => o.Shift(by)).ToList();

    /// <summary>A slide turns along at most this many contacts before it stops where it is.</summary>
    const int Contacts = 4;

    /// <summary>To the first contact, then what is left of the drag projected on the contact's tangent, again at each contact up to <see cref="Contacts"/>.</summary>
    static Point2 Move(IReadOnlyList<Outline> shape, Point2 from, Point2 to, Func<Point2, Outline?> obstacle)
    {
        Outline? Hit(Point2 at) => obstacle(at - from);
        var (at, target) = (from, to);
        for (var i = 0; i < Contacts; i++)
        {
            var (contact, by) = Advance(at, target, Hit);
            if (by is null) return target;
            var (_, onMe, onIt) = shape.Select(o => Outline.Separation(o.Shift(contact - from), by)).MinBy(s => s.Distance);
            var n = onMe - onIt;
            if (n.Length < Tolerance.Distance) n = contact - by.ClosestPointTo(contact);   // a grazing contact too close to measure: away from the obstacle's nearest point
            if (n.Length < Tolerance.Distance) return contact;
            n *= 1 / n.Length;
            var rest = target - contact;
            var along = rest - n * rest.Dot(n);
            if (along.Length <= Contact) return contact;
            (at, target) = (contact, contact + along);
        }
        return at;
    }

    static (Point2 At, Outline? By) Advance(Point2 from, Point2 to, Func<Point2, Outline?> hit)
    {
        var d = to - from;
        var steps = Sampling.Steps(d.Length, Step);
        var free = 0.0;
        for (var k = 1; k <= steps; k++)
        {
            var t = (double)k / steps;
            if (hit(from + d * t) is not { } by) { free = t; continue; }
            double lo = free, hi = t;
            while ((hi - lo) * d.Length > Contact) { var mid = (lo + hi) / 2; if (hit(from + d * mid) is null) lo = mid; else hi = mid; }
            return (from + d * Math.Max(free, lo - Contact / d.Length), by);   // backed off by Contact, so the gap is wide enough to measure the contact's normal
        }
        return (to, null);
    }
}
