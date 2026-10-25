namespace Tabletop.Geometry;

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
    /// </summary>
    public static Point3 Land(PlacedModel moving, Point2 to, IReadOnlyList<PlacedModel> models, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes,
        Func<Point2, bool>? within = null)
    {
        var solid = walls.Where(w => w.Volume.Kind == VolumeKind.Solid).Select(w => w.WorldOutline()).ToList();
        PlacedModel There(Point2 at) => moving with { Placement = moving.Placement with { Position = new Point3(at.X, at.Y, 0) } };
        PlacedModel Rested(Point2 at) { var m = There(at); return m with { Placement = m.Placement with { Position = new Point3(at.X, at.Y, RestHeight(m, volumes)) } }; }
        bool Free(Point2 by)
        {
            var at = to + by;
            var outline = There(at).BaseOutline();
            if (solid.Any(s => Outline.InteriorsIntersect(outline, s))) return false;
            var placed = Rested(at);
            return !models.Any(m => Overlap.Bases(placed, m)) && (within is null || within(at));
        }
        return Rested(to + (Ring(Free, LandReach) ?? new Point2(0, 0))).Placement.Position;
    }

    /// <summary>A lifted group lands in drag order: each against the stationary models and the held ones already landed.</summary>
    public static IReadOnlyList<Point3> Land(IReadOnlyList<(PlacedModel Moving, Point2 To, Func<Point2, bool>? Within)> held, IReadOnlyList<PlacedModel> stationary,
        IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)
    {
        var placed = stationary.ToList();
        var ends = new List<Point3>(held.Count);
        foreach (var (moving, to, within) in held)
        {
            var end = Land(moving, to, placed, walls, volumes, within);
            ends.Add(end);
            placed.Add(moving with { Placement = moving.Placement with { Position = end } });
        }
        return ends;
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
