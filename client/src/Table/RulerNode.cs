using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>A line from where a piece started to where it is, with the distance in inches and, in a declared move, what is left.</summary>
public partial class RulerNode : Node3D
{
    readonly ImmediateMesh _mesh = new();
    readonly MeshInstance3D _line = new(); readonly Label3D _label = new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = 0.01f };
    public override void _Ready() { _line.Mesh = _mesh; AddChild(_line); AddChild(_label); Hide(); }

    public void Show(Point3 from, Point3 to, RulerReadout readout, Color colour) => Show([from, to], readout, colour);

    /// <summary>A move's ruler along the path its base takes (a straight line is a path of two points), with its length and what is left.</summary>
    public void Show(IReadOnlyList<Point3> path, RulerReadout readout, Color colour) =>
        Draw(path, readout.Remaining is { } r ? $"{readout.Distance:0.0}\"  ({r:0.0}\" left)" : $"{readout.Distance:0.0}\"", readout.OutOfRange ? Colors.Red : colour, readout.OutOfRange ? Colors.Red : Colors.White);

    /// <summary>A measurement: the line between its two points and its reading in its mode (`12.4" (2D)`, `13.0" (3D)  Δh 3.0"`).</summary>
    public void Show(Point3 from, Point3 to, MeasureReading reading, Color colour) => Draw([from, to], reading.Label, colour, Colors.White);

    void Draw(IReadOnlyList<Point3> path, string text, Color line, Color label)
    {
        var points = path.Select(p => WorldFrame.ToGodot(p) + Vector3.Up * 0.3f).ToList();
        // One mesh and one material per colour, refilled each drag frame: a ruler per held model is redrawn every frame.
        _mesh.ClearSurfaces(); _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (var i = 0; i + 1 < points.Count; i++) { _mesh.SurfaceAddVertex(points[i]); _mesh.SurfaceAddVertex(points[i + 1]); }
        _mesh.SurfaceEnd();
        _line.MaterialOverride = Palette.Unshaded(line);
        _label.Position = points[points.Count / 2 - (points.Count % 2 == 0 ? 1 : 0)].Lerp(points[points.Count / 2], 0.5f) + Vector3.Up * 0.8f;
        if (_label.Text != text) _label.Text = text;
        _label.Modulate = label;
        Visible = true;
    }

    public new void Hide() => Visible = false;
}
