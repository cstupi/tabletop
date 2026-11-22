using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>
/// What each band of a move's area (MoveRegions.Bands) draws in. Clear: the moving team's colour, as the whole area was. Difficult: the same colour
/// muted halfway to grey and fainter. Dangerous: the terrain's own dangerous colour (TerrainEffects, the legend's and the table's tint), striped
/// every StripeSpacing inches across the table, so it reads apart from every team colour, the activated band's green and the threat rings.
/// </summary>
public static class MoveBandLook
{
    public const float ClearAlpha = 0.25f, DifficultAlpha = 0.2f, DangerousAlpha = 0.4f;
    /// <summary>The stripes' period on the table, in inches; half of it is drawn.</summary>
    public const double StripeSpacing = 0.6;
    static readonly Rgba Grey = new(0.5f, 0.5f, 0.5f);

    public static Rgba Colour(MoveBand band, Rgba team) => band switch
    {
        MoveBand.Clear => team with { A = ClearAlpha },
        MoveBand.Difficult => new Rgba((team.R + Grey.R) / 2, (team.G + Grey.G) / 2, (team.B + Grey.B) / 2, DifficultAlpha),
        _ => TerrainEffects.Of(TerrainDeclarations.Dangerous).Colour with { A = DangerousAlpha },
    };

    /// <summary>The band's draw order among see-through layers (a higher one draws later, over the rest), so a dangerous sliver of one model's area
    /// stays visible under the clear area of another's.</summary>
    public static int Priority(MoveBand band) => band == MoveBand.Dangerous ? 1 : 0;

    /// <summary>The band's height over the slab, in inches: the dangerous band a hair over the others.</summary>
    public static double Lift(MoveBand band) => band == MoveBand.Dangerous ? 0.035 : 0.03;

    /// <summary>Whether the band is striped.</summary>
    public static bool Striped(MoveBand band) => band == MoveBand.Dangerous;

    /// <summary>The bands in drawing order, each with its triangles from every model's area.</summary>
    public static IReadOnlyList<(MoveBand Band, IReadOnlyList<IReadOnlyList<Point2>> Areas)> Layers(IReadOnlyList<MoveBands> areas) =>
    [
        (MoveBand.Clear, areas.Select(a => a.Clear).ToList()),
        (MoveBand.Difficult, areas.Select(a => a.Difficult).ToList()),
        (MoveBand.Dangerous, areas.Select(a => a.Dangerous).ToList()),
    ];
}
