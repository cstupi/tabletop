namespace Tabletop.Geometry;

/// <summary>
/// Nothing on the table is inside anything else (client UX spec → Physical collision). A body rests on the highest Solid top under
/// any part of it, with no step-up limit: whether a climb is allowed is the server's terrain judgement. Touching is not under.
/// </summary>
public static partial class Collision
{
    /// <summary>A model: Solid tops, and the base height of an Area lifted onto something (its floor).</summary>
    public static double RestHeight(PlacedModel model, IReadOnlyList<WorldVolume> volumes) => Rest([model.BaseOutline()], volumes, floors: true);

    /// <summary>A terrain piece (its volumes share one transform): Solid tops under any of its footprints; its own volumes and every Area are ignored.</summary>
    public static double RestHeight(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> volumes)
    {
        var own = piece.Select(v => v.Id).ToHashSet();
        return Rest(piece.Select(v => v.WorldOutline()).ToList(), volumes.Where(v => !own.Contains(v.Id)).ToList(), floors: false);
    }

    /// <summary>A point (a marker): the highest Solid top whose footprint holds it, boundary inclusive.</summary>
    public static double RestHeight(Point2 point, IReadOnlyList<WorldVolume> volumes) =>
        volumes.Where(v => v.Volume.Kind == VolumeKind.Solid && v.WorldOutline().Contains(point)).Select(v => v.WorldTop()).DefaultIfEmpty(0).Max();

    internal static double Rest(IReadOnlyList<Outline> outlines, IReadOnlyList<WorldVolume> volumes, bool floors)
    {
        var best = 0.0;
        foreach (var v in volumes)
        {
            var height = v.Volume.Kind == VolumeKind.Solid ? v.WorldTop() : floors ? v.WorldBottom() : 0;
            if (height <= best) continue;
            var outline = v.WorldOutline();
            if (outlines.Any(o => Outline.InteriorsIntersect(o, outline))) best = height;
        }
        return best;
    }
}
