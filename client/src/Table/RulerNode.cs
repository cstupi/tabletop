using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>A line from where a piece started to where it is, with the distance in inches and, in a declared move, what is left.</summary>
public partial class RulerNode : Node3D
{
    readonly MeshInstance3D _line = new(); readonly Label3D _label = new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = 0.01f };
    public override void _Ready() { AddChild(_line); AddChild(_label); Hide(); }

    public void Show(Point3 from, Point3 to, RulerReadout readout, Color colour) =>
        Draw(from, to, readout.Remaining is { } r ? $"{readout.Distance:0.0}\"  ({r:0.0}\" left)" : $"{readout.Distance:0.0}\"", readout.OutOfRange ? Colors.Red : colour, readout.OutOfRange ? Colors.Red : Colors.White);

    /// <summary>A measurement: the line between its two points and its reading in its mode (`12.4" (2D)`, `13.0" (3D)  Δh 3.0"`).</summary>
    public void Show(Point3 from, Point3 to, MeasureReading reading, Color colour) => Draw(from, to, reading.Label, colour, Colors.White);

    void Draw(Point3 from, Point3 to, string text, Color line, Color label)
    {
        var a = WorldFrame.ToGodot(from) + Vector3.Up * 0.3f; var b = WorldFrame.ToGodot(to) + Vector3.Up * 0.3f;
        var im = new ImmediateMesh(); im.SurfaceBegin(Mesh.PrimitiveType.Lines); im.SurfaceAddVertex(a); im.SurfaceAddVertex(b); im.SurfaceEnd();
        _line.Mesh = im; _line.MaterialOverride = new StandardMaterial3D { AlbedoColor = line, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _label.Position = (a + b) / 2 + Vector3.Up * 0.8f;
        _label.Text = text; _label.Modulate = label;
        Visible = true;
    }

    public new void Hide() => Visible = false;
}
