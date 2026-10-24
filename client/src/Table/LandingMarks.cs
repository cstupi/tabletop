using Godot;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>
/// Held pieces lift over spec → Lifted: "A base outline drawn on the surface marks where it will land." One ring per held model's base, or per
/// footprint of a held terrain piece, at its landing (DragBodies' HeldEnd.Lands); a ring is kept while its shape is.
/// </summary>
public partial class LandingMarks : Node3D
{
    readonly List<(Base Shape, Node3D Ring)> _rings = [];

    /// <summary>Rings at these spots, each turned to its yaw; the rest hidden.</summary>
    public void Show(IReadOnlyList<(Point3 At, Yaw Yaw, Base Shape)> marks)
    {
        for (var i = 0; i < marks.Count; i++)
        {
            var (at, yaw, shape) = marks[i];
            if (i == _rings.Count) { var made = Palette.BaseRing(shape); AddChild(made); _rings.Add((shape, made)); }
            else if (_rings[i].Shape != shape) { _rings[i].Ring.QueueFree(); var made = Palette.BaseRing(shape); AddChild(made); _rings[i] = (shape, made); }
            var ring = _rings[i].Ring;
            ring.Position = WorldFrame.ToGodot(at); ring.Rotation = new Vector3(0, WorldFrame.Radians(yaw), 0); ring.Visible = true;
        }
        for (var i = marks.Count; i < _rings.Count; i++) _rings[i].Ring.Visible = false;
    }

    /// <summary>A terrain piece's footprints where it lands, at the height it rests on: a rect footprint as a rect base, a circle as a circle.</summary>
    public void ShowPiece(IReadOnlyList<WorldVolume> piece, double z) =>
        Show(piece.Select(v =>
        {
            var f = v.Volume.Footprint; var s = v.Transform.Scale; var c = v.WorldCentre();
            var shape = f.Kind == FootprintKind.Circle ? Base.Circle(2 * f.A * s) : Base.Rect(f.B * s, f.A * s);
            return (new Point3(c.X, c.Y, z), v.WorldYaw(), shape);
        }).ToList());

    public void Clear() { foreach (var (_, ring) in _rings) ring.Visible = false; }
}
