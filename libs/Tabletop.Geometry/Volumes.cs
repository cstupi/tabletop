namespace Tabletop.Geometry;

public enum FootprintKind { Circle, Rect }

/// <summary>Circle: A = B = radius. Rect: A = width (x), B = length (y).</summary>
public readonly record struct Footprint(FootprintKind Kind, double A, double B)
{
    public static Footprint Circle(double radius) => new(FootprintKind.Circle, radius, radius);
    public static Footprint Rect(double width, double length) => new(FootprintKind.Rect, width, length);

    /// <summary>
    /// The outline WorldVolumes builds from this footprint at the given scale can be built and measured: a defined kind, a finite
    /// scale and scaled size above zero, and for a rect twice the scaled corners' area at least twice the polygon constructor's threshold.
    /// The margin is there because the outline is built from the corners after rotation and translation, and rounding there can take
    /// an area sitting exactly on the threshold below it.
    /// </summary>
    public bool IsValid(double scale)
    {
        static bool Positive(double v) => double.IsFinite(v) && v > 0;
        if (!Enum.IsDefined(Kind) || !Positive(scale) || !Positive(A * scale)) return false;
        return Kind == FootprintKind.Circle || (Positive(B * scale) && PolygonOutline.Builds(Corners(scale), 2 * Tolerance.Distance));
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

/// <summary>The eight terrain declarations the engine reads. Everything else is <see cref="Volume.Notes"/>.
/// DangerousOnRushCharge is rulebook p.12's "Dangerous when using rush/charge" (Rivers, Ruins): Dangerous for a unit that Rushes or Charges;
/// under 3.6.0 p.12 it is Unstable: Dangerous for a model that moves over 6" into or through it.
/// Steep is 3.5.1 p.12's Hills "Difficult when moving up" and 3.6.0 p.12's Steep "Units moving onto steep terrain count it as being in Difficult
/// terrain, but units moving off it count it as open terrain": difficult for a move whose base centre starts outside its footprint.</summary>
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
    DangerousOnRushCharge = 64,
    Steep = 128,
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
