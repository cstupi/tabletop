namespace Tabletop.Geometry;

/// <summary>A volume's prism in world coordinates: the piece's position, yaw and uniform scale applied (geometry values → The terrain transform).</summary>
public static class WorldVolumes
{
    public static double WorldBottom(this WorldVolume v) => v.Transform.Position.Z + v.Volume.Bottom * v.Transform.Scale;
    public static double WorldTop(this WorldVolume v) => v.Transform.Position.Z + v.Volume.Top * v.Transform.Scale;
    public static Point2 WorldCentre(this WorldVolume v) => v.Transform.Position.Horizontal + (v.Volume.Offset * v.Transform.Scale).Rotate(v.Transform.Yaw);
    public static Yaw WorldYaw(this WorldVolume v) => new(v.Transform.Yaw.Degrees + v.Volume.Yaw.Degrees);

    internal static Outline WorldOutline(this WorldVolume v)
    {
        var s = v.Transform.Scale;
        var centre = v.WorldCentre();
        var f = v.Volume.Footprint;
        if (f.Kind == FootprintKind.Circle) return new CircleOutline(centre, f.A * s);
        var hw = f.A * s / 2;
        var hl = f.B * s / 2;
        return new PolygonOutline(Bodies.Place([new(-hw, -hl), new(hw, -hl), new(hw, hl), new(-hw, hl)], centre, v.WorldYaw()));
    }

    internal static bool VerticalRangeContains(this WorldVolume v, double z) =>
        z >= v.WorldBottom() - Tolerance.Distance && z <= v.WorldTop() + Tolerance.Distance;

    internal static bool ContainsPoint(this WorldVolume v, Point3 p) => v.VerticalRangeContains(p.Z) && v.WorldOutline().Contains(p.Horizontal);

    /// <summary>Does the 3D segment pass through the prism: clip to the vertical range, then a 2D segment test.</summary>
    internal static bool IntersectsSegment(this WorldVolume v, Point3 a, Point3 b)
    {
        var bottom = v.WorldBottom();
        var top = v.WorldTop();
        double t0 = 0, t1 = 1;
        var dz = b.Z - a.Z;
        if (Math.Abs(dz) < Tolerance.Distance)
        {
            if (a.Z < bottom - Tolerance.Distance || a.Z > top + Tolerance.Distance) return false;
        }
        else
        {
            var tb = (bottom - a.Z) / dz;
            var tt = (top - a.Z) / dz;
            t0 = Math.Max(0, Math.Min(tb, tt));
            t1 = Math.Min(1, Math.Max(tb, tt));
            if (t0 > t1) return false;
        }
        var p = new Point2(a.X + (b.X - a.X) * t0, a.Y + (b.Y - a.Y) * t0);
        var q = new Point2(a.X + (b.X - a.X) * t1, a.Y + (b.Y - a.Y) * t1);
        return v.WorldOutline().IntersectsSegment(p, q);
    }
}
