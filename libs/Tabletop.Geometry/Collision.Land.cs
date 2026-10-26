namespace Tabletop.Geometry;

/// <summary>The models a landing is drawn into base contact with, and how near it must already end (a charge: MoveRegion.ContactWith and ContactGrace).</summary>
public sealed record Abutment(IReadOnlyList<PlacedModel> Models, double Within);

public static partial class Collision
{
    /// <summary>How far over the highest thing under it a lifted piece is drawn (held pieces lift over spec → Lifted).</summary>
    public const double Lift = 0.5;

    /// <summary>How far from its target a lifted model's landing is searched: a drag frame's work stays bounded in a crowd (about 30,000 spots at most).</summary>
    const double LandReach = 24;

    /// <summary>
    /// Held pieces lift over spec → Landing: a lifted model lands at the nearest spot to its target (rings outward, as <see cref="Clear"/> searches)
    /// where its base enters no Solid wall, it overlaps no model's body rested there (<see cref="Overlap.Bases"/>), and `within` takes it (inside a
    /// move step, the model's reach). It rests on its highest open surface there. The target, rested, when nothing within <see cref="LandReach"/> is free.
    /// With `abut` (a charge's target), a landing that ends near one of its models is drawn flush against it (<see cref="Abut"/>).
    /// </summary>
    public static Point3 Land(PlacedModel moving, Point2 to, IReadOnlyList<PlacedModel> models, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes,
        Func<Point2, bool>? within = null, Abutment? abut = null)
    {
        var solid = Solids(walls);
        var landed = Rested(moving, to + (Ring(by => Free(moving, to + by, solid, models, volumes, within), LandReach) ?? new Point2(0, 0)), volumes);
        return abut is null ? landed : Abut(moving, landed, abut, solid, models, volumes, within);
    }

    /// <summary>A lifted group lands in drag order: each against the stationary models and the held ones already landed.</summary>
    public static IReadOnlyList<Point3> Land(IReadOnlyList<(PlacedModel Moving, Point2 To, Func<Point2, bool>? Within)> held, IReadOnlyList<PlacedModel> stationary,
        IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes, Abutment? abut = null)
    {
        var placed = stationary.ToList();
        var ends = new List<Point3>(held.Count);
        foreach (var (moving, to, within) in held)
        {
            var end = Land(moving, to, placed, walls, volumes, within, abut);
            ends.Add(end);
            placed.Add(moving with { Placement = moving.Placement with { Position = end } });
        }
        return ends;
    }

    /// <summary>
    /// A landing whose base ends apart from the nearest of `abut.Models` by no more than `abut.Within`, moved straight toward that model by the gap
    /// between their closest points (<see cref="Outline.Separation"/>), so the bases touch, when that spot is free as a landing's is (no Solid wall,
    /// no model overlapped, `within` takes it); otherwise where it landed.
    /// </summary>
    public static Point3 Abut(PlacedModel moving, Point3 landed, Abutment abut, IReadOnlyList<PlacedModel> models, IReadOnlyList<WorldVolume> walls,
        IReadOnlyList<WorldVolume> volumes, Func<Point2, bool>? within = null) =>
        Abut(moving, landed, abut, Solids(walls), models, volumes, within);

    static Point3 Abut(PlacedModel moving, Point3 landed, Abutment abut, IReadOnlyList<Outline> solid, IReadOnlyList<PlacedModel> models,
        IReadOnlyList<WorldVolume> volumes, Func<Point2, bool>? within)
    {
        if (abut.Models.Count == 0) return landed;
        var outline = (moving with { Placement = moving.Placement with { Position = landed } }).BaseOutline();
        var nearest = abut.Models.Select(m => Outline.Separation(outline, m.BaseOutline())).MinBy(s => s.Distance);
        if (nearest.Distance <= Tolerance.Distance || nearest.Distance > abut.Within + Tolerance.Distance) return landed;
        var flush = landed.Horizontal + (nearest.OnB - nearest.OnA);
        return Free(moving, flush, solid, models, volumes, within) ? Rested(moving, flush, volumes) : landed;
    }

    static List<Outline> Solids(IReadOnlyList<WorldVolume> walls) => walls.Where(w => w.Volume.Kind == VolumeKind.Solid).Select(w => w.WorldOutline()).ToList();

    static PlacedModel There(PlacedModel moving, Point2 at) => moving with { Placement = moving.Placement with { Position = new Point3(at.X, at.Y, 0) } };

    static Point3 Rested(PlacedModel moving, Point2 at, IReadOnlyList<WorldVolume> volumes) => new(at.X, at.Y, RestHeight(There(moving, at), volumes));

    static bool Free(PlacedModel moving, Point2 at, IReadOnlyList<Outline> solid, IReadOnlyList<PlacedModel> models, IReadOnlyList<WorldVolume> volumes, Func<Point2, bool>? within)
    {
        if (solid.Any(s => Outline.InteriorsIntersect(There(moving, at).BaseOutline(), s))) return false;
        var placed = moving with { Placement = moving.Placement with { Position = Rested(moving, at, volumes) } };
        return !models.Any(m => Overlap.Bases(placed, m)) && (within is null || within(at));
    }

    /// <summary>A lifted terrain piece lands where its footprint at the target enters no wall, else at the nearest spot that does (<see cref="Clear"/>).</summary>
    public static TerrainTransform Land(IReadOnlyList<WorldVolume> piece, Point2 to, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes) =>
        Clear(Moved(piece, to), walls, volumes);

    /// <summary>Where a lifted model is drawn: <see cref="Lift"/> over the highest top of any volume or model under its base.</summary>
    public static double Hover(PlacedModel at, IReadOnlyList<WorldVolume> volumes, IReadOnlyList<PlacedModel> models)
    {
        var outline = at.BaseOutline();
        return Lift + volumes.Where(v => Outline.InteriorsIntersect(outline, v.WorldOutline())).Select(v => v.WorldTop())
            .Concat(models.Where(m => !Bodies.Apart(at, m, 0) && Outline.InteriorsIntersect(outline, m.BaseOutline())).Select(m => m.Top())).DefaultIfEmpty(0).Max();
    }

    /// <summary>Where a lifted terrain piece is drawn: <see cref="Lift"/> over the highest top of any other volume under its footprints.</summary>
    public static double Hover(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> volumes)
    {
        var shape = piece.Select(v => v.WorldOutline()).ToList();
        return Lift + Others(piece, volumes).Where(v => { var o = v.WorldOutline(); return shape.Any(s => Outline.InteriorsIntersect(s, o)); })
            .Select(v => v.WorldTop()).DefaultIfEmpty(0).Max();
    }
}
