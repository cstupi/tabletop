using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>Wounds is the wounds left of a model that takes more than one, else 0; Tokens its spell tokens. Anchor is the top of the model.</summary>
public sealed record ModelDots(Guid ModelId, Point3 Anchor, int Wounds, int Tokens);
/// <summary>One sample of the star ring in the model's frame (x across, y along the facing): the band's inner edge and its outer, star edge.</summary>
public readonly record struct RingSample(Point2 Inner, Point2 Outer);

/// <summary>Spec → Markers: what sits on each model rather than over its unit. Dots over a model; a star ring around the base of every
/// model whose unit or self carries a condition (the caller passes the condition key, the one its icon is looked up by); a band around the base of every
/// model whose unit has activated this round.</summary>
public static class ModelMarks
{
    /// <summary>Beyond the base edge: the band's inner edge, the valleys between spikes, the spike tips.</summary>
    public const double Gap = 0.05, Valley = 0.2, Spike = 0.45;

    public static IReadOnlyList<ModelDots> Dots(Projection p, IReadOnlyDictionary<Guid, int> maxWounds) =>
        p.Models.Values.Where(m => m.Location is OnTable).Select(m =>
        {
            var max = maxWounds.GetValueOrDefault(m.Id, 1);
            var at = ((OnTable)m.Location).Placement.Position;
            return new ModelDots(m.Id, at with { Z = at.Z + m.Slot.Height }, max > 1 ? Math.Max(0, max - m.Wounds) : 0, m.SpellTokens);
        }).Where(d => d.Wounds > 0 || d.Tokens > 0).ToList();

    public static IReadOnlyList<Guid> Ringed(Projection p, string condition) =>
        p.Models.Values.Where(m => m.Location is OnTable && (m.Conditions.Contains(condition) || p.Units.GetValueOrDefault(m.UnitId)?.Conditions.Contains(condition) == true))
            .Select(m => m.Id).ToList();

    /// <summary>The activated band beyond the base edge: clear of the base ring on the outline (0.08" wide), inside the star ring's spikes.</summary>
    public const double BandGap = 0.06, BandWidth = 0.14;
    public const int BandSegments = 48;

    /// <summary>Every model on the table of a unit with an action recorded this round (the roster's ✓, the header's dots), any team. The server
    /// clears the record at the round's turn; a model on the shelf or inside a transport has none.</summary>
    public static IReadOnlyList<Guid> Activated(Projection p) =>
        p.Models.Values.Where(m => m.Location is OnTable && p.Units.GetValueOrDefault(m.UnitId)?.Actions.Count > 0).Select(m => m.Id).ToList();

    /// <summary>The base the activated band goes round: the declared base, grown to the drawn mesh's footprint at the ground (its half extents
    /// across, x, and along the facing, y) where the mesh's own base overhangs it, so the band is never under the model. A circle stays a circle.</summary>
    public static Base BandBase(Base b, double halfX, double halfY)
    {
        double width = Math.Max(b.B, 2 * halfX), length = Math.Max(b.A, 2 * halfY);
        if (width == b.B && length == b.A) return b;
        return b.Shape switch
        {
            BaseShape.Circle => Base.Circle(Math.Max(width, length)),
            BaseShape.Oval => Base.Oval(length, width),
            _ => Base.Rect(length, width),
        };
    }

    /// <summary>The activated band as a closed loop of (inner, outer) samples in the model's frame, BandGap to BandGap + BandWidth beyond the base:
    /// a round or oval base's ellipse in BandSegments samples (the first on +x), a rectangle's square-cornered frame in four corners counter-clockwise from (+x, +y).</summary>
    public static IReadOnlyList<RingSample> Band(Base b)
    {
        double hx = b.B / 2, hy = b.A / 2, inner = BandGap, outer = BandGap + BandWidth;
        if (b.Shape == BaseShape.Rect)
            return new[] { (1, 1), (-1, 1), (-1, -1), (1, -1) }.Select(c =>
                new RingSample(new Point2(c.Item1 * (hx + inner), c.Item2 * (hy + inner)), new Point2(c.Item1 * (hx + outer), c.Item2 * (hy + outer)))).ToList();
        return Enumerable.Range(0, BandSegments).Select(i =>
        {
            var t = 2 * Math.PI * i / BandSegments; double c = Math.Cos(t), s = Math.Sin(t);
            return new RingSample(new Point2((hx + inner) * c, (hy + inner) * s), new Point2((hx + outer) * c, (hy + outer) * s));
        }).ToList();
    }

    /// <summary>2 × spikes samples around the base's ellipse (a rect's circumscribed one), even ones at a tip. A = length along y, B = width along x.</summary>
    public static IReadOnlyList<RingSample> StarRing(Base b, int spikes = 12)
    {
        var grow = b.Shape == BaseShape.Rect ? Math.Sqrt(2) : 1;
        double rx = b.B / 2 * grow, ry = b.A / 2 * grow;
        return Enumerable.Range(0, 2 * spikes).Select(i =>
        {
            var t = Math.PI * i / spikes; double c = Math.Cos(t), s = Math.Sin(t);
            var reach = i % 2 == 0 ? Spike : Valley;
            return new RingSample(new Point2((rx + Gap) * c, (ry + Gap) * s), new Point2((rx + reach) * c, (ry + reach) * s));
        }).ToList();
    }
}
