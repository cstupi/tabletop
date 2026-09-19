namespace Tabletop.Geometry;

/// <summary>Cross-platform determinism is not required (architecture.md → Geometry); comparisons use this slack.</summary>
internal static class Tolerance
{
    public const double Distance = 1e-6;
}
