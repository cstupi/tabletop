namespace Tabletop.Geometry;

public static partial class Collision
{
    /// <summary>The path is sampled this finely, then bisected to <see cref="Contact"/>; a 0.5" wall cannot be stepped over.</summary>
    public const double Step = 0.1;
    const double Contact = 1e-3;

    public static Point3 Slide(PlacedModel moving, Point2 to, IReadOnlyList<PlacedModel> models, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)
    {
        var from = moving.Placement.Position.Horizontal;
        Outline[] shape = [moving.BaseOutline()];
        var bodies = models.Where(m => !Overlap.Bases(moving, m)).Select(m => (Body: m, Outline: m.BaseOutline())).ToList();
        var sides = Sides(walls, shape);
        Outline? Obstacle(IReadOnlyList<Outline> at)
        {
            var bottom = Rest(at, volumes, floors: true);
            foreach (var (body, outline) in bodies)
                if (Overlap.IntervalsOverlap(bottom, bottom + moving.Body.Height, body.Bottom(), body.Top()) && Outline.InteriorsIntersect(at[0], outline)) return outline;
            return sides.FirstOrDefault(s => Outline.InteriorsIntersect(at[0], s));
        }
        var end = Move(shape, from, to, Obstacle);
        return new Point3(end.X, end.Y, Rest(Shifted(shape, end - from), volumes, floors: true));
    }

    public static TerrainTransform Slide(IReadOnlyList<WorldVolume> piece, Point2 to, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)
    {
        var origin = piece[0].Transform;
        var from = origin.Position.Horizontal;
        var shape = piece.Select(v => v.WorldOutline()).ToList();
        var sides = Sides(Others(piece, walls), shape);
        var end = Move(shape, from, to, at => sides.FirstOrDefault(s => at.Any(o => Outline.InteriorsIntersect(o, s))));
        return Rested(piece, end, volumes);
    }

    /// <summary>
    /// A terrain piece in hand: lifted, it goes straight to the cursor when its footprint there enters no wall; otherwise it slides
    /// from where it is toward it (<see cref="Slide(IReadOnlyList{WorldVolume}, Point2, IReadOnlyList{WorldVolume}, IReadOnlyList{WorldVolume})"/>),
    /// so it never stays trapped behind a wall the cursor has passed. Rests either way.
    /// </summary>
    public static TerrainTransform Follow(IReadOnlyList<WorldVolume> piece, Point2 to, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)
    {
        var at = Moved(piece, to);
        return Blocked(at, walls) ? Slide(piece, to, walls, volumes) : Rested(piece, to, volumes);
    }

    /// <summary>The piece's footprints share area with a Solid wall that is not its own.</summary>
    public static bool Blocked(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> walls)
    {
        var shape = piece.Select(v => v.WorldOutline()).ToList();
        return Others(piece, walls).Where(w => w.Volume.Kind == VolumeKind.Solid).Any(w => { var s = w.WorldOutline(); return shape.Any(o => Outline.InteriorsIntersect(o, s)); });
    }

    /// <summary>Rings this far apart, searched outward for a free spot; and how far out the search goes.</summary>
    const double ClearStep = 0.25, ClearReach = 144;

    /// <summary>
    /// A piece where it stands if that enters no wall, else at the nearest spot that does (rings <see cref="ClearStep"/> apart,
    /// searched outward): how a turn that swings a piece into another is undone by moving it, not by refusing the turn. Rests.
    /// The piece unmoved when nothing within <see cref="ClearReach"/> is free.
    /// </summary>
    public static TerrainTransform Clear(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)
    {
        var from = piece[0].Transform.Position.Horizontal;
        var solid = Others(piece, walls).Where(w => w.Volume.Kind == VolumeKind.Solid).Select(w => w.WorldOutline()).ToList();
        var shape = piece.Select(v => v.WorldOutline()).ToList();
        bool Free(Point2 by) => !solid.Any(s => shape.Any(o => Outline.InteriorsIntersect(o.Shift(by), s)));
        if (Free(new Point2(0, 0))) return Rested(piece, from, volumes);
        for (var r = ClearStep; r <= ClearReach; r += ClearStep)
        {
            var n = Math.Max(16, (int)Math.Ceiling(2 * Math.PI * r / ClearStep));
            for (var i = 0; i < n; i++)
            {
                var t = 2 * Math.PI * i / n;
                var by = new Point2(Math.Cos(t), Math.Sin(t)) * r;
                if (Free(by)) return Rested(piece, from + by, volumes);
            }
        }
        return Rested(piece, from, volumes);
    }

    static List<WorldVolume> Others(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> walls)
    {
        var own = piece.Select(v => v.Id).ToHashSet();
        return walls.Where(w => !own.Contains(w.Id)).ToList();
    }

    static List<WorldVolume> Moved(IReadOnlyList<WorldVolume> piece, Point2 to) =>
        piece.Select(v => v with { Transform = v.Transform with { Position = new Point3(to.X, to.Y, 0) } }).ToList();

    static TerrainTransform Rested(IReadOnlyList<WorldVolume> piece, Point2 at, IReadOnlyList<WorldVolume> volumes) =>
        piece[0].Transform with { Position = new Point3(at.X, at.Y, RestHeight(Moved(piece, at), volumes)) };

    /// <summary>Each held body slides against the stationary ones and the held bodies already placed, in drag order.</summary>
    public static IReadOnlyList<Point3> Separate(IReadOnlyList<(PlacedModel Moving, Point2 To)> held, IReadOnlyList<PlacedModel> stationary, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)
    {
        var placed = stationary.ToList();
        var ends = new List<Point3>(held.Count);
        foreach (var (moving, to) in held)
        {
            var end = Slide(moving, to, placed, walls, volumes);
            ends.Add(end);
            placed.Add(moving with { Placement = moving.Placement with { Position = end } });
        }
        return ends;
    }

    static List<Outline> Sides(IReadOnlyList<WorldVolume> walls, IReadOnlyList<Outline> shape) =>
        walls.Where(w => w.Volume.Kind == VolumeKind.Solid).Select(w => w.WorldOutline()).Where(s => !shape.Any(o => Outline.InteriorsIntersect(o, s))).ToList();

    static IReadOnlyList<Outline> Shifted(IReadOnlyList<Outline> shape, Point2 by) => shape.Select(o => o.Shift(by)).ToList();

    /// <summary>A slide turns along at most this many contacts before it stops where it is.</summary>
    const int Contacts = 4;

    /// <summary>To the first contact, then what is left of the drag projected on the contact's tangent, again at each contact up to <see cref="Contacts"/>.</summary>
    static Point2 Move(IReadOnlyList<Outline> shape, Point2 from, Point2 to, Func<IReadOnlyList<Outline>, Outline?> obstacle)
    {
        Outline? Hit(Point2 at) => obstacle(Shifted(shape, at - from));
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
