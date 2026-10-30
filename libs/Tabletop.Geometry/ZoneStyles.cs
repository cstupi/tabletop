namespace Tabletop.Geometry;

/// <summary>
/// The built-in deployment zone styles: Grimdark Future p.22 (the six basic styles) and p.50 (free-for-all corners).
/// A shape id is "<style>/<n>", n the team ordinal the setup phase assigns in order. A zone is a set of polygons.
/// Measurements are anchored to table features — edges, corners, edge midpoints — never scaled (geometry values → Deployment shapes),
/// except Side Battle, whose legs are fractions of their edges (<see cref="ZoneLegs"/>), which the rule set's edition gives.
/// </summary>
public static class ZoneStyles
{
    public const string Frontline = "frontline";
    public const string GroundWar = "ground-war";
    public const string SideBattle = "side-battle";
    public const string Disordered = "disordered";
    public const string Spearhead = "spearhead";
    public const string OpposingForces = "opposing-forces";
    public const string FreeForAll = "free-for-all";

    public static IReadOnlyList<string> All { get; } = [Frontline, GroundWar, SideBattle, Disordered, Spearhead, OpposingForces, FreeForAll];

    /// <summary>The style's shape ids in team-ordinal order — what SetZoneStyle carries. Two for the basic styles, four for free-for-all.</summary>
    public static IReadOnlyList<string> ShapesOf(string style)
    {
        var count = style == FreeForAll ? 4 : All.Contains(style) ? 2 : throw new ArgumentException($"Unknown zone style '{style}'.", nameof(style));
        return Enumerable.Range(1, count).Select(n => $"{style}/{n}").ToList();
    }

    /// <summary>The polygons a shape id resolves to on a table, in the table frame (origin at the table's corner, 0..W × 0..L).</summary>
    public static IReadOnlyList<Polygon> Resolve(string shapeId, TableSize table, ZoneLegs? legs = null)
    {
        var (sideLong, sideShort) = legs ?? ZoneLegs.Default;
        var slash = shapeId.LastIndexOf('/');
        if (slash < 0 || !int.TryParse(shapeId[(slash + 1)..], out var n))
            throw new ArgumentException($"Malformed shape id '{shapeId}'.", nameof(shapeId));
        var style = shapeId[..slash];
        var f = new Frame(table);
        var a = f.Long;      // the long edge, u from 0 to a
        var b = f.Short;     // the short edge, v from 0 to b
        IReadOnlyList<IReadOnlyList<Point2>> shapes = (style, n) switch
        {
            // Frontline (also the core rules' deployment, p.6): 12" in from each long edge, the full width.
            (Frontline, 1) => [Rect(0, a, b - 12, b)],
            (Frontline, 2) => [Rect(0, a, 0, 12)],
            // Ground War: 24" in from each short edge.
            (GroundWar, 1) => [Rect(0, 24, 0, b)],
            (GroundWar, 2) => [Rect(a - 24, a, 0, b)],
            // Side Battle: a triangle in each of two opposite corners, legs proportional to the edges they run along.
            (SideBattle, 1) => [Tri(P(0, 0), P(a * sideLong, 0), P(0, b * sideShort))],
            (SideBattle, 2) => [Tri(P(a, b), P(a * (1 - sideLong), b), P(a, b * (1 - sideShort)))],
            // Disordered: a diamond through the edge midpoints; one team has both corners on one long edge, the other team the other two.
            (Disordered, 1) => [Tri(P(0, b / 2), P(a / 2, b), P(0, b)), Tri(P(a / 2, b), P(a, b / 2), P(a, b))],
            (Disordered, 2) => [Tri(P(0, 0), P(a / 2, 0), P(0, b / 2)), Tri(P(a / 2, 0), P(a, 0), P(a, b / 2))],
            // Spearhead: a triangle on each short edge, apex 24" in at the edge's centre.
            (Spearhead, 1) => [Tri(P(0, 0), P(24, b / 2), P(0, b))],
            (Spearhead, 2) => [Tri(P(a, b), P(a - 24, b / 2), P(a, 0))],
            // Opposing Forces: a 24"×24" square in two diagonally opposite corners.
            (OpposingForces, 1) => [Rect(0, 24, b - 24, b)],
            (OpposingForces, 2) => [Rect(a - 24, a, 0, 24)],
            // Free-for-all (p.50): a 2'×1' area in each corner — 24" along the long edge, 12" along the short — clockwise from the far-left corner (u = 0, v = b).
            (FreeForAll, 1) => [Rect(0, 24, b - 12, b)],
            (FreeForAll, 2) => [Rect(a - 24, a, b - 12, b)],
            (FreeForAll, 3) => [Rect(a - 24, a, 0, 12)],
            (FreeForAll, 4) => [Rect(0, 24, 0, 12)],
            _ => throw new ArgumentException($"Unknown shape id '{shapeId}'.", nameof(shapeId)),
        };
        return shapes.Select(s => new Polygon(s.Select(f.ToTable).ToList())).ToList();
    }

    public static bool Contains(string shapeId, TableSize table, Point2 p, ZoneLegs? legs = null) => Resolve(shapeId, table, legs).Any(z => z.Contains(p));

    /// <summary>Rulebook p.6 "fully within": the whole base inside one of the zone's polygons.</summary>
    public static bool BaseFullyWithin(string shapeId, TableSize table, PlacedModel model, ZoneLegs? legs = null)
    {
        var outline = model.BaseOutline();
        return Resolve(shapeId, table, legs).Any(z => new PolygonOutline(z.Vertices).ContainsOutline(outline));
    }

    /// <summary>
    /// Rulebook p.14 Scout, "fully within 12” of their deployment zone": every point of the base within <paramref name="inches"/> of one of the
    /// zone's polygons. Both convex, so the farthest point of the base is exact as Containment.BaseFullyWithin takes it: a vertex of a polygon
    /// base, the centre pushed out by the radius for a circular one. A zone of two polygons is measured to each in turn, which can only refuse a
    /// base straddling the point where they meet.
    /// </summary>
    public static bool BaseFullyWithin(string shapeId, TableSize table, PlacedModel model, double inches, ZoneLegs? legs = null)
    {
        var limit = inches + Tolerance.Distance;
        var outline = model.BaseOutline();
        return Resolve(shapeId, table, legs).Select(z => new PolygonOutline(z.Vertices)).Any(z => outline switch
        {
            CircleOutline c => z.DistanceTo(c.Centre) + c.Radius <= limit,
            PolygonOutline p => p.Vertices.All(v => z.DistanceTo(v) <= limit),
            var other => throw new ArgumentOutOfRangeException(nameof(model), other.GetType().Name),
        });
    }

    static Point2 P(double u, double v) => new(u, v);
    static IReadOnlyList<Point2> Tri(Point2 p, Point2 q, Point2 r) => [p, q, r];
    static IReadOnlyList<Point2> Rect(double u0, double u1, double v0, double v1) => [P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1)];

    /// <summary>Canonical frame: u along the long edge from 0, v along the short edge from 0, the origin the table's corner. A square table takes X as long.</summary>
    readonly record struct Frame(TableSize Table)
    {
        public bool XIsLong => Table.Width >= Table.Length;
        public double Long => XIsLong ? Table.Width : Table.Length;
        public double Short => XIsLong ? Table.Length : Table.Width;
        public Point2 ToTable(Point2 uv) => XIsLong ? uv : new(uv.Y, uv.X);
    }
}

/// <summary>
/// Side Battle's legs as fractions of the edges they run along, measured from zone A's corner: <paramref name="SideBattleLong"/> of the long edge,
/// <paramref name="SideBattleShort"/> of the short. Grimdark Future 3.5.1 p.22 draws 54" and 30" on 72×48 (<see cref="Default"/>, 3/4 and 5/8);
/// 3.6.0 p.21 draws 48" and 36" (2/3 and 3/4). The game server sends the game's legs in Welcome.
/// </summary>
public sealed record ZoneLegs(double SideBattleLong, double SideBattleShort)
{
    public static ZoneLegs Default { get; } = new(0.75, 0.625);
}
