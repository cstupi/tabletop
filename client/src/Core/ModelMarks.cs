using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>Wounds is the wounds left of a model that takes more than one, else 0; Tokens its spell tokens. Anchor is the top of the model.</summary>
public sealed record ModelDots(Guid ModelId, Point3 Anchor, int Wounds, int Tokens);
/// <summary>One sample of the star ring in the model's frame (x across, y along the facing): the band's inner edge and its outer, star edge.</summary>
public readonly record struct RingSample(Point2 Inner, Point2 Outer);

/// <summary>Spec → Markers: what sits on each model rather than over its unit. Dots over a model; a star ring around the base of every
/// model whose unit or self carries a condition (the caller passes the condition key, the one its icon is looked up by).</summary>
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
