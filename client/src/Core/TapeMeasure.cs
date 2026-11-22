using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What a measurement reads in its mode: the distance, the height difference between its ends, and the label.</summary>
public sealed record MeasureReading(double Distance, double Rise, MeasureMode Mode)
{
    public static MeasureReading Of(Point3 from, Point3 to, MeasureMode mode)
    {
        var flat = from.Horizontal.DistanceTo(to.Horizontal); var rise = Math.Abs(to.Z - from.Z);
        return new(mode == MeasureMode.Straight ? Math.Sqrt(flat * flat + rise * rise) : flat, rise, mode);
    }

    /// <summary>`12.4" (2D)`, or `13.0" (3D)  Δh 3.0"`.</summary>
    public string Label => Mode == MeasureMode.Straight ? $"{Distance:0.0}\" (3D)  Δh {Rise:0.0}\"" : $"{Distance:0.0}\" (2D)";
}

public static class MeasureModes
{
    public static MeasureMode Toggled(this MeasureMode mode) => mode == MeasureMode.Straight ? MeasureMode.Horizontal : MeasureMode.Straight;
}

/// <summary>
/// The free ruler: anchored on a press (Ctrl going down, or the M tool's first click) and following the cursor until it is
/// cleared (Ctrl up) or finished (the M tool's second click). Each end is a body: a model's base, or a base of diameter zero for a
/// point, so the line runs base edge to base edge (p.5) through Distance.ClosestPoints.
/// </summary>
public sealed class TapeMeasure
{
    /// <summary>Under this the two ends are one point and no line is drawn: a press that has not moved (Ctrl+Z) shows nothing.</summary>
    const double Least = 0.05;

    public PlacedModel? Anchor { get; private set; }
    public PlacedModel? Current { get; private set; }
    public MeasureMode Mode { get; set; }
    /// <summary>The line's far end still follows the cursor.</summary>
    public bool Following { get; private set; }

    public void Start(PlacedModel anchor) { Anchor = anchor; Current = null; Following = true; }
    public void Follow(PlacedModel to) { if (Following) Current = to; }
    public void Finish(PlacedModel to) { Follow(to); Following = false; }
    public void Clear() { Anchor = null; Current = null; Following = false; }

    /// <summary>The two resolved points to draw and relay, with the mode; null with no line.</summary>
    public Measurement? Line
    {
        get
        {
            if (Anchor is not { } a || Current is not { } b) return null;
            var (from, to) = Distance.ClosestPoints(a, b);
            return MeasureReading.Of(from, to, MeasureMode.Straight).Distance < Least ? null : new Measurement(from, to, Mode);
        }
    }

    public MeasureReading? Reading => Line is { } m ? MeasureReading.Of(m.From, m.To, Mode) : null;

    /// <summary>A ruler end: a model's base (p.5) where its node is drawn, which in a move Ask is its ghost and not the placement the
    /// projection still holds; anything else the point.</summary>
    public static PlacedModel EndAt(Point3 point, ModelBody? body, Placement? drawn) =>
        body is { } b && drawn is { } at ? new PlacedModel(b, at) : new PlacedModel(new ModelBody(Base.Circle(0), 0), new Placement(point, new Yaw(0)));

    /// <summary>A point picked on a terrain piece rests on the piece's top under it (a ray that meets the box's side lands on the
    /// top), and a point off every Solid on the table.</summary>
    public static Point3 OnSurface(Point3 hit, IReadOnlyList<WorldVolume> volumes) => hit with { Z = Collision.RestHeight(hit.Horizontal, volumes) };
}
