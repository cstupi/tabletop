using System.Text.Json.Serialization;

namespace Tabletop.Geometry;

/// <summary>A point on the table plane, in inches.</summary>
public readonly record struct Point2(double X, double Y);

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

/// <summary>A flat polygon on the table plane; deployment zones resolve to sets of these.</summary>
public sealed record Polygon(IReadOnlyList<Point2> Vertices)
{
    /// <summary>Point-in-polygon. Implemented by the geometry plan.</summary>
    public bool Contains(Point2 point) => throw new NotImplementedException("Implemented by the geometry plan.");
}
