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
        var own = piece.Select(v => v.Id).ToHashSet();
        var sides = Sides(walls.Where(w => !own.Contains(w.Id)).ToList(), shape);
        var end = Move(shape, from, to, at => sides.FirstOrDefault(s => at.Any(o => Outline.InteriorsIntersect(o, s))));
        var moved = piece.Select(v => v with { Transform = origin with { Position = new Point3(end.X, end.Y, 0) } }).ToList();
        return origin with { Position = new Point3(end.X, end.Y, RestHeight(moved, volumes)) };
    }

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

    /// <summary>To the first contact, then the rest of the drag projected on the contact's tangent, once.</summary>
    static Point2 Move(IReadOnlyList<Outline> shape, Point2 from, Point2 to, Func<IReadOnlyList<Outline>, Outline?> obstacle)
    {
        Outline? Hit(Point2 at) => obstacle(Shifted(shape, at - from));
        var (contact, by) = Advance(from, to, Hit);
        if (by is null) return to;
        var (_, onMe, onIt) = shape.Select(o => Outline.Separation(o.Shift(contact - from), by)).MinBy(s => s.Distance);
        var n = onMe - onIt;
        if (n.Length < Tolerance.Distance) return contact;
        n *= 1 / n.Length;
        var rest = to - contact;
        var along = rest - n * rest.Dot(n);
        return along.Length <= Contact ? contact : Advance(contact, contact + along, Hit).At;
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
            return (from + d * lo, by);
        }
        return (to, null);
    }
}
