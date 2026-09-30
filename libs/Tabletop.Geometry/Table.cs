namespace Tabletop.Geometry;

/// <summary>The slab: a rectangle with its corner at the origin, X along Width (0..W), Y along Length (0..L), top face at Z = 0 — the wire frame.</summary>
public static class Table
{
    public static Polygon Bounds(TableSize size) =>
        new([new(0, 0), new(size.Width, 0), new(size.Width, size.Length), new(0, size.Length)]);

    /// <summary>Edge inclusive: 0 ≤ x ≤ width and 0 ≤ y ≤ length — the game server's OnSlab rule. A terrain origin failing this is off the slab (setup phase → Terrain).</summary>
    public static bool ContainsPoint(TableSize size, Point2 p) =>
        p.X >= -Tolerance.Distance && p.X <= size.Width + Tolerance.Distance && p.Y >= -Tolerance.Distance && p.Y <= size.Length + Tolerance.Distance;

    /// <summary>No part of the base off the battlefield (rulebook p.7).</summary>
    public static bool ContainsBase(TableSize size, PlacedModel model) => Outline(size).ContainsOutline(model.BaseOutline());

    internal static PolygonOutline Outline(TableSize size) => new(Bounds(size).Vertices);

    /// <summary>
    /// Every outline built from the table size can be built: both edges finite and above zero, and the polygon constructor's own area
    /// test passes on the slab and on every built-in deployment zone's polygons, which are resolved against the size.
    /// </summary>
    public static bool IsValid(TableSize size)
    {
        static bool Positive(double v) => double.IsFinite(v) && v > 0;
        return Positive(size.Width) && Positive(size.Length) && PolygonOutline.Builds(Bounds(size).Vertices)
            && ZoneStyles.All.SelectMany(ZoneStyles.ShapesOf).SelectMany(id => ZoneStyles.Resolve(id, size)).All(z => PolygonOutline.Builds(z.Vertices));
    }
}
