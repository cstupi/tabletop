namespace Tabletop.Geometry;

public static partial class Collision
{
    /// <summary>One part of a body: an outline and its height interval above where the body rests.</summary>
    internal readonly record struct Part(Outline Outline, double Bottom, double Top);

    internal static Part[] PartsOf(PlacedModel model) => [new(model.BaseOutline(), 0, model.Body.Height)];

    internal static Part[] PartsOf(IReadOnlyList<WorldVolume> piece) =>
        piece.Select(v => new Part(v.WorldOutline(), v.WorldBottom() - v.Transform.Position.Z, v.WorldTop() - v.Transform.Position.Z)).ToArray();

    internal static IReadOnlyList<Part> Shifted(IReadOnlyList<Part> body, Point2 by) => body.Select(p => p with { Outline = p.Outline.Shift(by) }).ToList();

    /// <summary>A model's surfaces where it stands, ascending (held pieces lift over spec → Surfaces).</summary>
    public static IReadOnlyList<double> Surfaces(PlacedModel model, IReadOnlyList<WorldVolume> volumes) => Open(PartsOf(model), volumes, floors: true);

    /// <summary>A terrain piece's surfaces where it stands, ascending: its own volumes and every Area floor left out, as <see cref="RestHeight(IReadOnlyList{WorldVolume}, IReadOnlyList{WorldVolume})"/> reads them.</summary>
    public static IReadOnlyList<double> Surfaces(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> volumes) => Open(PartsOf(piece), Others(piece, volumes), floors: false);

    /// <summary>The model's open surface nearest the level, the higher on a tie; <see cref="double.PositiveInfinity"/> gives the highest, today's rest.</summary>
    public static double RestNear(PlacedModel model, IReadOnlyList<WorldVolume> volumes, double level) => Nearest(Surfaces(model, volumes), level);

    /// <summary>The piece's open surface nearest the level, the higher on a tie.</summary>
    public static double RestNear(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> volumes, double level) => Nearest(Surfaces(piece, volumes), level);

    /// <summary>
    /// The spec's open surfaces: the table (0), each Solid top and (with floors) each Area floor whose footprint shares interior with
    /// a part, where the body standing on it shares no vertical interval with a Solid under that part (touching is not overlapping).
    /// Never empty: the highest candidate is open, since a Solid overlapping a body on it would have a higher top.
    /// </summary>
    internal static List<double> Open(IReadOnlyList<Part> body, IReadOnlyList<WorldVolume> volumes, bool floors)
    {
        var under = new List<(WorldVolume Volume, Part[] Parts)>();
        foreach (var v in volumes)
        {
            if (v.Volume.Kind != VolumeKind.Solid && !floors) continue;
            var outline = v.WorldOutline();
            var parts = body.Where(p => Outline.InteriorsIntersect(p.Outline, outline)).ToArray();
            if (parts.Length > 0) under.Add((v, parts));
        }
        var candidates = new List<double> { 0 };
        foreach (var (v, _) in under) candidates.Add(v.Volume.Kind == VolumeKind.Solid ? v.WorldTop() : v.WorldBottom());
        candidates.Sort();
        var open = new List<double>();
        foreach (var s in candidates)
        {
            if (open.Count > 0 && s - open[^1] <= Tolerance.Distance) continue;
            if (under.Any(u => u.Volume.Volume.Kind == VolumeKind.Solid && u.Parts.Any(p => Overlap.IntervalsOverlap(s + p.Bottom, s + p.Top, u.Volume.WorldBottom(), u.Volume.WorldTop())))) continue;
            open.Add(s);
        }
        return open;
    }

    /// <summary>The surface nearest the level, the higher on a tie; an infinite level the highest.</summary>
    internal static double Nearest(IReadOnlyList<double> open, double level)
    {
        if (double.IsPositiveInfinity(level)) return open[^1];
        var best = open[0];
        foreach (var s in open) if (Math.Abs(s - level) <= Math.Abs(best - level) + Tolerance.Distance) best = s;
        return best;
    }
}
