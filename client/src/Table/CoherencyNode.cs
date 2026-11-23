using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>
/// A placement's unit coherency on the table (CoherencyView): each ghost's ring at the coherency distance from its base edge, faint in the team's
/// colour, and for each model out of coherency a dashed line in the warning colour from its base to the model it is measured to, with its words
/// (`1.4" apart, needs 1"`) over the line's middle, a control in the node's own canvas layer, placed each frame by ViolationMarks with the mark
/// labels in one placement (MarkLabels.Place), off each other, the Hud's panels and the bar over the unit. Drawn by the decision box.
/// </summary>
public partial class CoherencyNode : Node3D
{
    readonly ArrayMesh _rings = new(), _lines = new();
    readonly MeshInstance3D _ringNode, _lineNode;
    readonly CanvasLayer _layer = new();
    readonly List<Label> _labels = [];
    readonly List<Vector3> _anchors = [];
    CoherencyPicture _shown = CoherencyPicture.None; Color _team;
    LabelSettings? _look;

    /// <summary>The dashes along a line, in inches: on, then off.</summary>
    const double Dash = 0.25, Gap = 0.15;

    public CoherencyNode()
    {
        _ringNode = new MeshInstance3D { Mesh = _rings, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _lineNode = new MeshInstance3D { Mesh = _lines, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_ringNode); AddChild(_lineNode); AddChild(_layer);
        Ui.Changed += OnTheme;
    }

    public override void _ExitTree() => Ui.Changed -= OnTheme;

    void OnTheme() { _look = null; var shown = _shown; _shown = CoherencyPicture.None; Show(shown, _team); }

    public void Show(CoherencyPicture picture, Color team)
    {
        if (ReferenceEquals(picture, _shown) && team == _team) return;
        _shown = picture; _team = team;
        var rings = new List<Vector3>();
        foreach (var ring in picture.Rings)
            foreach (var (a, b) in ring.Segments) { rings.Add(At(a, ring.Z)); rings.Add(At(b, ring.Z)); }
        Fill(_rings, _ringNode, rings, new Color(team.R, team.G, team.B, 0.55f));
        var dashes = new List<Vector3>();
        foreach (var line in picture.Lines)
        {
            var from = line.From; var to = line.To;
            var length = new Point2(to.X - from.X, to.Y - from.Y).Length;
            for (var s = 0.0; s < length; s += Dash + Gap)
            {
                var e = Math.Min(length, s + Dash);
                dashes.Add(Along(from, to, s / Math.Max(length, 1e-9))); dashes.Add(Along(from, to, e / Math.Max(length, 1e-9)));
            }
        }
        Fill(_lines, _lineNode, dashes, Ui.Warning);
        _look ??= new LabelSettings { FontSize = 16, FontColor = Ui.Warning, OutlineSize = 6, OutlineColor = Ui.Background };
        _anchors.Clear();
        for (var i = 0; i < picture.Lines.Count; i++)
        {
            if (i == _labels.Count) { var made = new Label { MouseFilter = Control.MouseFilterEnum.Ignore }; _layer.AddChild(made); _labels.Add(made); }
            var line = picture.Lines[i];
            if (!ReferenceEquals(_labels[i].LabelSettings, _look)) _labels[i].LabelSettings = _look;
            if (_labels[i].Text != line.Text) _labels[i].Text = line.Text;
            _anchors.Add(Along(line.From, line.To, 0.5));
        }
        for (var i = picture.Lines.Count; i < _labels.Count; i++) _labels[i].Visible = false;
        Visible = picture.Rings.Count + picture.Lines.Count > 0;
    }

    public new void Hide() => Show(CoherencyPicture.None, _team);

    /// <summary>Each shown gap label's wanted place (its line's middle on the screen and its size), into `wanted`, its control into `order`;
    /// every label hidden first, so one behind the camera stays hidden. ViolationMarks places them.</summary>
    public void Wanted(Camera3D cam, List<GapLabel> wanted, List<Label> order)
    {
        for (var i = 0; i < _labels.Count; i++) _labels[i].Visible = false;
        if (!Visible) return;
        for (var i = 0; i < _anchors.Count; i++)
        {
            if (cam.IsPositionBehind(_anchors[i])) continue;
            var at = cam.UnprojectPosition(_anchors[i]); var box = _labels[i].GetCombinedMinimumSize();
            wanted.Add(new GapLabel((at.X, at.Y), box.X, box.Y)); order.Add(_labels[i]);
        }
    }

    static Vector3 At(Point2 p, double z) => WorldFrame.ToGodot(new Point3(p.X, p.Y, z + 0.06));

    static Vector3 Along(Point3 a, Point3 b, double t) =>
        WorldFrame.ToGodot(new Point3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t + 0.08));

    static void Fill(ArrayMesh mesh, MeshInstance3D node, List<Vector3> vertices, Color colour)
    {
        mesh.ClearSurfaces();
        node.Visible = vertices.Count > 0;
        if (vertices.Count == 0) return;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        node.MaterialOverride = Material(colour);
    }

    static readonly Dictionary<Color, StandardMaterial3D> _materials = new();

    /// <summary>An unlit see-through line material per colour, drawn over the move's bands and paths; shared, never changed.</summary>
    static StandardMaterial3D Material(Color c) => _materials.TryGetValue(c, out var m) ? m : _materials[c] = new StandardMaterial3D
    {
        AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        RenderPriority = MoveBandLook.Priority(MoveBand.Dangerous) + 2,
    };
}
