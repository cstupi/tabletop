namespace Tabletop.Geometry;

/// <summary>Flagged, never prevented: base overlap and terrain intersection (architecture.md → Rules enforcement).</summary>
public static class Overlap
{
    /// <summary>Two bases share area and a vertical extent. Touching is not overlapping.</summary>
    public static bool Bases(PlacedModel a, PlacedModel b) =>
        IntervalsOverlap(a.Bottom(), a.Top(), b.Bottom(), b.Top()) && Outline.InteriorsIntersect(a.BaseOutline(), b.BaseOutline());

    /// <summary>The body is inside a Solid volume. Standing on its top is not; Area volumes never intersect.</summary>
    public static bool WithSolid(PlacedModel model, WorldVolume volume) =>
        volume.Volume.Kind == VolumeKind.Solid
        && IntervalsOverlap(model.Bottom(), model.Top(), volume.WorldBottom(), volume.WorldTop())
        && Outline.InteriorsIntersect(model.BaseOutline(), volume.WorldOutline());

    internal static bool IntervalsOverlap(double a0, double a1, double b0, double b1) => Math.Min(a1, b1) - Math.Max(a0, b0) > Tolerance.Distance;
}
