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

    internal static Outline BaseOutline(this PlacedModel m)
    {
        var centre = m.Placement.Position.Horizontal;
        var yaw = m.Placement.Yaw;
        var b = m.Body.Base;
        switch (b.Shape)
        {
            case BaseShape.Circle:
                return new CircleOutline(centre, b.A / 2);
            case BaseShape.Rect:
            {
                var (hw, hl) = (b.B / 2, b.A / 2);                       // width across X, length along the facing axis Y, as the oval below
                return new PolygonOutline(Place([new(-hw, -hl), new(hw, -hl), new(hw, hl), new(-hw, hl)], centre, yaw));
            }
            case BaseShape.Oval:
            {
                var local = Enumerable.Range(0, OvalVertices).Select(i =>
                {
                    var t = 2 * Math.PI * i / OvalVertices;
                    return new Point2(b.B / 2 * Math.Cos(t), b.A / 2 * Math.Sin(t));
                });
                return new PolygonOutline(Place(local, centre, yaw));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(m), b.Shape, "Unknown base shape.");
        }
    }

    /// <summary>Local points → world: rotate by yaw about the origin, then translate to centre.</summary>
    internal static List<Point2> Place(IEnumerable<Point2> local, Point2 centre, Yaw yaw) => local.Select(p => p.Rotate(yaw) + centre).ToList();
}
