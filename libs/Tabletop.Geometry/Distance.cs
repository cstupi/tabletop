namespace Tabletop.Geometry;

/// <summary>Edge-to-edge between bases, never centre-to-centre. Horizontal, in inches; 0 when the outlines touch or overlap.</summary>
public static class Distance
{
    public static double EdgeToEdge(PlacedModel a, PlacedModel b) => Outline.Separation(a.BaseOutline(), b.BaseOutline()).Distance;

    /// <summary>
    /// Between the two bodies (each base outline extruded from its settled height to its top), in three dimensions: the horizontal edge-to-edge
    /// distance and the gap between their height intervals, combined. Equal to <see cref="EdgeToEdge(PlacedModel, PlacedModel)"/> when the heights overlap.
    /// </summary>
    public static double BodyToBody(PlacedModel a, PlacedModel b)
    {
        var across = EdgeToEdge(a, b);
        var up = Math.Max(0, Math.Max(a.Bottom(), b.Bottom()) - Math.Min(a.Top(), b.Top()));
        return Math.Sqrt(across * across + up * up);
    }

    public static double EdgeToEdge(PlacedModel a, Point2 point) => a.BaseOutline().DistanceTo(point);

    /// <summary>Base edge to a placed volume's footprint edge.</summary>
    public static double EdgeToEdge(PlacedModel a, WorldVolume volume) => Outline.Separation(a.BaseOutline(), volume.WorldOutline()).Distance;

    /// <summary>Base edge to a marker's edge; a marker is a circle of a diameter (geometry values → Marker size).</summary>
    public static double EdgeToMarker(PlacedModel a, Point2 centre, double diameter) =>
        Outline.Separation(a.BaseOutline(), new CircleOutline(centre, diameter / 2)).Distance;

    /// <summary>The two edge points a measurement is drawn between (presence → A measurement is two points). Z is each base's settled height.</summary>
    public static (Point3 OnA, Point3 OnB) ClosestPoints(PlacedModel a, PlacedModel b)
    {
        var (_, pa, pb) = Outline.Separation(a.BaseOutline(), b.BaseOutline());
        return (new Point3(pa.X, pa.Y, a.Bottom()), new Point3(pb.X, pb.Y, b.Bottom()));
    }

    /// <summary>Edge-to-edge within range, inclusive.</summary>
    public static bool InRange(PlacedModel a, PlacedModel b, double range) => EdgeToEdge(a, b) <= range + Tolerance.Distance;

    /// <summary>Difference of the two base heights (rulebook p.9: "within 2" horizontally and 4" vertically").</summary>
    public static double Vertical(PlacedModel a, PlacedModel b) => Math.Abs(a.Bottom() - b.Bottom());
}
