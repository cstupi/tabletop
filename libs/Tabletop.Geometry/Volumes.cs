namespace Tabletop.Geometry;

public enum FootprintKind { Circle, Rect }

/// <summary>Circle: A = B = radius. Rect: A = width (x), B = length (y).</summary>
public readonly record struct Footprint(FootprintKind Kind, double A, double B)
{
    public static Footprint Circle(double radius) => new(FootprintKind.Circle, radius, radius);
    public static Footprint Rect(double width, double length) => new(FootprintKind.Rect, width, length);
}

/// <summary>Solid: occupied, its top is what a model settles onto. Area: passed through, stood inside.</summary>
public enum VolumeKind { Solid, Area }

/// <summary>The six terrain declarations the engine reads. Everything else is <see cref="Volume.Notes"/>.</summary>
[Flags]
public enum TerrainDeclarations
{
    None = 0,
    Impassable = 1,
    BlocksLineOfSight = 2,
    SeeInAndOut = 4,
    Cover = 8,
    Difficult = 16,
    Dangerous = 32,
}

/// <summary>An extruded prism relative to its piece's origin. Bottom and Top are heights above the piece's base.</summary>
public sealed record Volume(
    VolumeKind Kind,
    Footprint Footprint,
    Point2 Offset,
    Yaw Yaw,
    double Bottom,
    double Top,
    TerrainDeclarations Declarations,
    string? Notes);
