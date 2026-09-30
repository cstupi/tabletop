namespace Tabletop.Geometry;

public enum FootprintKind { Circle, Rect }

/// <summary>Circle: A = B = radius. Rect: A = width (x), B = length (y).</summary>
public readonly record struct Footprint(FootprintKind Kind, double A, double B)
{
    public static Footprint Circle(double radius) => new(FootprintKind.Circle, radius, radius);
    public static Footprint Rect(double width, double length) => new(FootprintKind.Rect, width, length);

    /// <summary>
    /// The outline WorldVolumes builds from this footprint at the given scale can be built and measured: a defined kind, a finite
    /// scale and scaled size above zero, and for a rect the polygon constructor's own area test on the scaled corners.
    /// </summary>
    public bool IsValid(double scale)
    {
        static bool Positive(double v) => double.IsFinite(v) && v > 0;
        if (!Enum.IsDefined(Kind) || !Positive(scale) || !Positive(A * scale)) return false;
        return Kind == FootprintKind.Circle || (Positive(B * scale) && PolygonOutline.Builds(Corners(scale)));
    }

    /// <summary>A rect's corners at the given scale, centred on the origin and unrotated.</summary>
    internal IReadOnlyList<Point2> Corners(double scale)
    {
        var (hw, hl) = (A * scale / 2, B * scale / 2);
        return [new(-hw, -hl), new(hw, -hl), new(hw, hl), new(-hw, hl)];
    }
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
