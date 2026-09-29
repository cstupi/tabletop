using Godot;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>Wire frame (X width, Y length, Z up, corner origin) to Godot (Y up, −Z forward). Yaw 0 faces +Y = Godot −Z; positive yaw is counter-clockwise from above, which is Godot's positive rotation about +Y.</summary>
public static class WorldFrame
{
    public static Vector3 ToGodot(Point3 p) => new((float)p.X, (float)p.Z, (float)-p.Y);
    public static Point3 FromGodot(Vector3 v) => new(v.X, -v.Z, v.Y);
    public static float Radians(Yaw yaw) => Mathf.DegToRad((float)yaw.Degrees);
    public static Yaw YawFrom(float radians) => new(Mathf.RadToDeg(radians));
}
