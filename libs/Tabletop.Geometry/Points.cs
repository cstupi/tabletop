using System.Text.Json.Serialization;

namespace Tabletop.Geometry;

/// <summary>A point on the table plane, in inches. Origin at the table's corner; X along Width (0..W), Y along Length (0..L).</summary>
public readonly record struct Point2(double X, double Y)
{
    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2 operator *(Point2 a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double DistanceTo(Point2 other) => (this - other).Length;
    public double Dot(Point2 other) => X * other.X + Y * other.Y;
    public double Cross(Point2 other) => X * other.Y - Y * other.X;

    /// <summary>Rotates about the origin. Yaw 0 faces +Y; positive yaw turns counter-clockwise seen from above.</summary>
    public Point2 Rotate(Yaw yaw)
    {
        var (s, c) = Math.SinCos(yaw.Degrees * Math.PI / 180);
        return new(X * c - Y * s, X * s + Y * c);
    }
}

/// <summary>A point in world units (inches). Z is up.</summary>
public readonly record struct Point3(double X, double Y, double Z)
{
    /// <summary>Derived, never serialised: System.Text.Json writes every get-only property, and a Point3 is on the wire everywhere.</summary>
    [JsonIgnore] public Point2 Horizontal => new(X, Y);
}

/// <summary>Facing about the vertical axis, in degrees. Models are yaw-only and always upright.</summary>
public readonly record struct Yaw(double Degrees);

/// <summary>
/// An asset's import rotation, in degrees, measured against the table's axes. The one convention (stated here; the web app's
/// converter writes it, the client's mesh loader applies it): the mesh's raw coordinates are read as table-frame coordinates and
/// rotated by Pitch about the table's X axis, then Roll about Y, then Yaw about Z — right-handed, extrinsic — and only then scaled.
/// A Y-up glTF stood upright in the Z-up table frame is Pitch = 90.
/// </summary>
public readonly record struct Orientation(double Pitch, double Yaw, double Roll);

/// <summary>The slab: width and length in inches. A rectangle, nothing more.</summary>
public readonly record struct TableSize(double Width, double Length);

/// <summary>A flat polygon on the table plane; deployment zones resolve to sets of these. Any vertex order; may be concave.</summary>
public sealed record Polygon(IReadOnlyList<Point2> Vertices)
{
    /// <summary>Even-odd point-in-polygon. A point on an edge or a vertex is inside.</summary>
    public bool Contains(Point2 p)
    {
        var v = Vertices;
        var inside = false;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
        {
            var a = v[j];
            var b = v[i];
            if (OnSegment(a, b, p)) return true;
            if ((a.Y > p.Y) != (b.Y > p.Y))
            {
                var x = a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                if (p.X < x) inside = !inside;
            }
        }
        return inside;
    }

    internal static bool OnSegment(Point2 a, Point2 b, Point2 p)
    {
        var ab = b - a;
        var ap = p - a;
        if (Math.Abs(ab.Cross(ap)) > Tolerance.Distance * Math.Max(1, ab.Length)) return false;
        var t = ab.Dot(ap);
        return t >= -Tolerance.Distance && t <= ab.Dot(ab) + Tolerance.Distance;
    }
}
