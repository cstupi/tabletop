namespace Tabletop.Client.Core;

/// <summary>
/// The level held pieces rest at (held pieces lift over spec → Held: lifted, or at a level). Null is lifted, every pick-up's start. PageDown
/// lowers the held pieces one surface, PageUp raises them one; above the highest open surface under them is lifted again. The surfaces are
/// the lead piece's where it stands (Collision.Surfaces), ascending.
/// </summary>
public static class HoldLevel
{
    const double Tolerance = 1e-6;

    /// <summary>From lifted, the highest surface; else the highest below the level, or the level when none is.</summary>
    public static double? Lower(double? level, IReadOnlyList<double> surfaces)
    {
        if (surfaces.Count == 0) return level;
        if (level is not { } at) return surfaces[^1];
        return surfaces.Where(s => s < at - Tolerance).Select(s => (double?)s).LastOrDefault() ?? at;
    }

    /// <summary>The lowest surface above the level; lifted when none is, and a lifted piece stays lifted.</summary>
    public static double? Raise(double? level, IReadOnlyList<double> surfaces) =>
        level is { } at ? surfaces.Where(s => s > at + Tolerance).Select(s => (double?)s).FirstOrDefault() : null;
}
