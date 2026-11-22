namespace Tabletop.Geometry;

/// <summary>Base overlap and a body inside a Solid.</summary>
public static class Overlap
{
    /// <summary>Two bases share area and a vertical extent. Touching is not overlapping.</summary>
    public static bool Bases(PlacedModel a, PlacedModel b) =>
        !Bodies.Apart(a, b, 0) && IntervalsOverlap(a.Bottom(), a.Top(), b.Bottom(), b.Top()) && Outline.InteriorsIntersect(a.BaseOutline(), b.BaseOutline());

    /// <summary>
    /// Bases(a, b), for many tests of one landing: two polygon bases whose inner circles (about their centres, inside their outlines) overlap
    /// overlap; otherwise the separating axes are tried facing the other base first (PolygonOutline.SeparatingAxisTest's answer in any order).
    /// </summary>
    internal static bool BasesFacing(PlacedModel a, PlacedModel b)
    {
        if (Bodies.Apart(a, b, 0) || !IntervalsOverlap(a.Bottom(), a.Top(), b.Bottom(), b.Top())) return false;
        var (oa, ob) = (a.BaseOutline(), b.BaseOutline());
        if (oa is not PolygonOutline pa || ob is not PolygonOutline pb) return Outline.InteriorsIntersect(oa, ob);
        var toward = b.Placement.Position.Horizontal - a.Placement.Position.Horizontal;
        if (toward.Length < Inner(a) + Inner(b) - 1e-5) return true;
        return PolygonOutline.SeparatingAxisTest(pa, pb, toward);
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<(Base, Yaw), double> Inners = new();

    /// <summary>The radius about a polygon base's centre of a circle inside its outline, padded down.</summary>
    static double Inner(PlacedModel m) => Inners.GetOrAdd((m.Body.Base, m.Placement.Yaw), _ =>
    {
        var v = m.BaseOffsets()!;
        var inner = double.PositiveInfinity;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
        {
            var e = v[i] - v[j];
            if (e.Length > 0) inner = Math.Min(inner, Math.Abs(e.Cross(v[j])) / e.Length);   // the line's distance from the centre (the origin)
        }
        return Math.Max(0, inner - 1e-9);
    });

    /// <summary>The body is inside a Solid volume. Standing on its top is not; Area volumes never intersect.</summary>
    public static bool WithSolid(PlacedModel model, WorldVolume volume) =>
        volume.Volume.Kind == VolumeKind.Solid
        && IntervalsOverlap(model.Bottom(), model.Top(), volume.WorldBottom(), volume.WorldTop())
        && Outline.InteriorsIntersect(model.BaseOutline(), volume.WorldOutline());

    internal static bool IntervalsOverlap(double a0, double a1, double b0, double b1) => Math.Min(a1, b1) - Math.Max(a0, b0) > Tolerance.Distance;
}
