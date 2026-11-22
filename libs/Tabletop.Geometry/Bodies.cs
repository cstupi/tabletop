namespace Tabletop.Geometry;

/// <summary>A model is a base and a height, upright, yaw only (geometry values → Height, position, facing).</summary>
public static class Bodies
{
    /// <summary>An oval base is a convex polygon with this many vertices; the error is under 0.2% of its radius.</summary>
    public const int OvalVertices = 64;

    /// <summary>Height of the base's underside: the settled Z.</summary>
    public static double Bottom(this PlacedModel m) => m.Placement.Position.Z;

    /// <summary>Top of the line-of-sight body.</summary>
    public static double Top(this PlacedModel m) => m.Placement.Position.Z + m.Body.Height;

    /// <summary>
    /// Where line of sight is drawn from: the centre of the body, the base centre raised half the height
    /// (line of sight is centre to any part of target; rulebook p.8).
    /// </summary>
    public static Point3 Eye(this PlacedModel m) => new(m.Placement.Position.X, m.Placement.Position.Y, m.Placement.Position.Z + m.Body.Height / 2);

    /// <summary>
    /// Rulebook p.11: other units are sight blockers. A body as a Solid, BlocksLineOfSight volume the caller can
    /// pass to <see cref="LineOfSight"/>. An oval base becomes its bounding rectangle; a rect base is already one.
    /// </summary>
    public static WorldVolume AsBlockingVolume(Guid id, PlacedModel m)
    {
        var b = m.Body.Base;
        var footprint = b.Shape == BaseShape.Circle ? Footprint.Circle(b.A / 2) : Footprint.Rect(b.B, b.A);
        var volume = new Volume(VolumeKind.Solid, footprint, new Point2(0, 0), new Yaw(0), 0, m.Body.Height, TerrainDeclarations.BlocksLineOfSight, null);
        return new WorldVolume(id, volume, new TerrainTransform(m.Placement.Position, m.Placement.Yaw, 1));
    }

    /// <summary>The radius about the base centre that holds the whole base (an oval's polygon included), for broad-phase culls.</summary>
    internal static double Reach(this ModelBody body)
    {
        var b = body.Base;
        return b.Shape switch
        {
            BaseShape.Circle => b.A / 2,
            BaseShape.Rect => Math.Sqrt(b.A * b.A + b.B * b.B) / 2,
            _ => Math.Max(b.A, b.B) / 2,
        };
    }

    /// <summary>The two bases are too far apart, centre to centre, for their outlines to come within `gap` of each other (broad phase).</summary>
    internal static bool Apart(PlacedModel a, PlacedModel b, double gap) =>
        a.Placement.Position.Horizontal.DistanceTo(b.Placement.Position.Horizontal) > a.Body.Reach() + b.Body.Reach() + gap + 1e-6;

    internal static Outline BaseOutline(this PlacedModel m) => m.BaseOffsets() is { } offsets
        ? BaseAt(offsets, m.Placement.Position.Horizontal)
        : new CircleOutline(m.Placement.Position.Horizontal, m.Body.Base.A / 2);

    /// <summary>A rect's or oval's outline vertices about its centre, turned by its yaw (the same points BaseOutline adds the centre to); null for a circle.</summary>
    internal static Point2[]? BaseOffsets(this PlacedModel m)
    {
        var yaw = m.Placement.Yaw;
        var b = m.Body.Base;
        switch (b.Shape)
        {
            case BaseShape.Circle:
                return null;
            case BaseShape.Rect:
            {
                var (hw, hl) = (b.B / 2, b.A / 2);                       // width across X, length along the facing axis Y, as the oval below
                return [.. new Point2[] { new(-hw, -hl), new(hw, -hl), new(hw, hl), new(-hw, hl) }.Select(p => p.Rotate(yaw))];
            }
            case BaseShape.Oval:
            {
                var local = Enumerable.Range(0, OvalVertices).Select(i =>
                {
                    var t = 2 * Math.PI * i / OvalVertices;
                    return new Point2(b.B / 2 * Math.Cos(t), b.A / 2 * Math.Sin(t));
                });
                return [.. local.Select(p => p.Rotate(yaw))];
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(m), b.Shape, "Unknown base shape.");
        }
    }

    /// <summary>A rect's or oval's outline with its centre at `centre`, from its BaseOffsets.</summary>
    internal static PolygonOutline BaseAt(Point2[] offsets, Point2 centre)
    {
        var vertices = new Point2[offsets.Length];
        for (var i = 0; i < offsets.Length; i++) vertices[i] = offsets[i] + centre;
        return new PolygonOutline(vertices);
    }

    /// <summary>Local points → world: rotate by yaw about the origin, then translate to centre.</summary>
    /// <summary>A circle's outline in a unit's perimeter: the circumscribed polygon with this many sides, so the hull holds the whole base.</summary>
    const int PerimeterCircleSides = 16;

    /// <summary>
    /// A unit's perimeter (rulebook p.5: models can't see through "the perimeter of other units"): the convex hull of the models' base outlines,
    /// from the lowest base to the tallest body's top, as one sight prism with the given id. A circle base enters as its circumscribed
    /// 16-gon. Optional-rules spec → Units as sight blockers.
    /// </summary>
    public static SightPrism Perimeter(Guid id, IReadOnlyList<PlacedModel> models)
    {
        if (models.Count == 0) throw new ArgumentException("A perimeter needs at least one model.", nameof(models));
        var points = models.SelectMany(m => m.BaseOutline() switch
        {
            CircleOutline c => Enumerable.Range(0, PerimeterCircleSides).Select(i =>
            {
                var t = 2 * Math.PI * i / PerimeterCircleSides;
                return c.Centre + new Point2(Math.Cos(t), Math.Sin(t)) * (c.Radius / Math.Cos(Math.PI / PerimeterCircleSides));
            }),
            PolygonOutline poly => poly.Vertices,
            _ => throw new ArgumentOutOfRangeException(nameof(models)),
        }).ToList();
        return new SightPrism(id, ConvexHull(points), models.Min(m => m.Bottom()), models.Max(m => m.Top()));
    }

    /// <summary>Andrew's monotone chain, counter-clockwise, collinear points dropped.</summary>
    internal static IReadOnlyList<Point2> ConvexHull(IReadOnlyList<Point2> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3) return sorted;
        var hull = new List<Point2>();
        for (var pass = 0; pass < 2; pass++)
        {
            var start = hull.Count;
            foreach (var p in sorted)
            {
                while (hull.Count >= start + 2 && (hull[^1] - hull[^2]).Cross(p - hull[^2]) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            sorted.Reverse();
        }
        return hull;
    }

    internal static List<Point2> Place(IEnumerable<Point2> local, Point2 centre, Yaw yaw) => local.Select(p => p.Rotate(yaw) + centre).ToList();
}
