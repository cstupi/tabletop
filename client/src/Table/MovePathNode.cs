using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>
/// Move paths on the table as marks (PathMarks): each model's boot prints along its route, a vehicle's two tread tracks, in the moving team's colour
/// in the theme's look (MovePaths.MarkColour: Fantasy an ink stamp, Sci-fi a faint light trace); a mark where the base touches
/// dangerous ground in the dangerous colour, each over a dark rim; the kept last move fainter (`Kept`). Drawn just over the slab and under the
/// ghosts: one MultiMesh per shape and per rim or mark, an instance a mark with its own colour, refilled on each Show.
/// </summary>
public partial class MovePathNode : Node3D
{
    /// <summary>The last move kept on the table (LastMovePath): its marks fainter.</summary>
    protected virtual bool Kept => false;

    readonly Dictionary<(MarkKind Kind, bool Rim), MultiMeshInstance3D> _layers = new();
    readonly Dictionary<ModelPath, IReadOnlyList<PathMark>> _marks = new(ReferenceEqualityComparer.Instance);
    IReadOnlyList<ModelPath> _shown = []; Color _team; bool _ink; Func<Guid, bool> _vehicle = _ => false;
    Action? _onTheme;

    public override void _EnterTree() { _onTheme = Restyle; Ui.Changed += _onTheme; }
    public override void _ExitTree() { if (_onTheme is not null) Ui.Changed -= _onTheme; }

    static bool InkLook => Ui.Current.Id == UiThemes.Fantasy.Id;

    /// <summary>A theme switch recolours the marks shown.</summary>
    void Restyle() { if (_ink != InkLook && _shown.Count > 0) Show(_shown, _team, _vehicle); }

    /// <summary>Refilled only when a path, the colour or the theme changed: a drag frame hands back the paths of ghosts that stayed put (MovePathCache).
    /// `vehicle`: whether a model is a vehicle (MovePaths.Vehicle), drawn as treads.</summary>
    public void Show(IReadOnlyList<ModelPath> paths, Color team, Func<Guid, bool> vehicle)
    {
        var ink = InkLook;
        if (team == _team && ink == _ink && paths.Count == _shown.Count && paths.Select((path, i) => ReferenceEquals(path, _shown[i])).All(same => same)) { Visible = paths.Count > 0; return; }
        _shown = paths; _team = team; _ink = ink; _vehicle = vehicle;
        var rgba = new Rgba(team.R, team.G, team.B, team.A);
        var kept = new Dictionary<ModelPath, IReadOnlyList<PathMark>>(ReferenceEqualityComparer.Instance);
        var byLayer = new Dictionary<(MarkKind, bool), List<(Transform3D, Color)>>
        {
            [(MarkKind.Foot, false)] = [], [(MarkKind.Foot, true)] = [], [(MarkKind.Tread, false)] = [], [(MarkKind.Tread, true)] = [],
        };
        var r = MovePaths.RimColour(Kept);
        var rim = new Color(r.R, r.G, r.B, r.A);
        foreach (var path in paths)
        {
            // A ghost that stayed put keeps its marks: only the moved ones are laid again.
            if (!_marks.TryGetValue(path, out var marks)) marks = PathMarks.Of(path, vehicle(path.ModelId));
            kept[path] = marks;
            var z = path.Start.Placement.Position.Z + 0.04;
            foreach (var m in marks)
            {
                var c = MovePaths.MarkColour(m, rgba, ink, Kept);
                byLayer[(m.Kind, false)].Add((Place(m, z, 1), new Color(c.R, c.G, c.B, c.A)));
                byLayer[(m.Kind, true)].Add((Place(m, z - 0.005, MovePaths.RimScale), rim));
            }
        }
        _marks.Clear();
        foreach (var (path, marks) in kept) _marks[path] = marks;
        foreach (var (layer, instances) in byLayer) Fill(layer, instances);
        Visible = paths.Count > 0;
    }

    /// <summary>The mark's instance transform: the shape (made in the table plane, +Y along the facing) turned to the facing, a left print mirrored, at
    /// its point just over the start's height, `scale` times its size (the rim under it is larger) times the mark's own Size.</summary>
    static Transform3D Place(PathMark m, double z, double scale)
    {
        scale *= m.Size;
        var x = new Point2(m.Kind == MarkKind.Foot && m.Left ? -scale : scale, 0).Rotate(m.Facing);
        var y = new Point2(0, scale).Rotate(m.Facing);
        Vector3 Dir(Point2 d, double up = 0) => WorldFrame.ToGodot(new Point3(d.X, d.Y, up));
        // Mesh axes are Godot's: table X is Godot X, table Y is Godot -Z, up is Godot Y.
        var basis = new Basis(Dir(x), Dir(default, 1), -Dir(y));
        return new Transform3D(basis, WorldFrame.ToGodot(new Point3(m.At.X, m.At.Y, z)));
    }

    void Fill((MarkKind Kind, bool Rim) key, List<(Transform3D Transform, Color Colour)> instances)
    {
        if (!_layers.TryGetValue(key, out var layer))
        {
            if (instances.Count == 0) return;
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = Shape(key.Kind) };
            layer = new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = Material(key.Rim), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(layer); _layers[key] = layer;
        }
        var mm = layer.Multimesh;
        if (mm.InstanceCount < instances.Count || mm.InstanceCount > 4 * Math.Max(64, instances.Count)) mm.InstanceCount = Math.Max(64, instances.Count * 3 / 2);
        mm.VisibleInstanceCount = instances.Count;
        // One buffer handed to Godot at once: 12 floats of transform, then 4 of colour, an instance.
        var buffer = new float[mm.InstanceCount * 16];
        for (var i = 0; i < instances.Count; i++)
        {
            var (t, c) = instances[i];
            var o = i * 16;
            (buffer[o], buffer[o + 1], buffer[o + 2], buffer[o + 3]) = (t.Basis.X.X, t.Basis.Y.X, t.Basis.Z.X, t.Origin.X);
            (buffer[o + 4], buffer[o + 5], buffer[o + 6], buffer[o + 7]) = (t.Basis.X.Y, t.Basis.Y.Y, t.Basis.Z.Y, t.Origin.Y);
            (buffer[o + 8], buffer[o + 9], buffer[o + 10], buffer[o + 11]) = (t.Basis.X.Z, t.Basis.Y.Z, t.Basis.Z.Z, t.Origin.Z);
            (buffer[o + 12], buffer[o + 13], buffer[o + 14], buffer[o + 15]) = (c.R, c.G, c.B, c.A);
        }
        mm.Buffer = buffer;
        layer.Visible = instances.Count > 0;
    }

    /// <summary>The shape's triangles in the table plane (Godot X and -Z), +Y of the shape along -Z.</summary>
    static ArrayMesh Shape(MarkKind kind)
    {
        var vertices = new List<Vector3>();
        foreach (var outline in kind == MarkKind.Foot ? PathMarks.Boot : [PathMarks.TreadLink])
        {
            var flat = outline.Select(p => new Vector2((float)p.X, (float)p.Y)).ToArray();
            var triangles = Geometry2D.TriangulatePolygon(flat);
            foreach (var i in triangles) vertices.Add(new Vector3(flat[i].X, 0, -flat[i].Y));
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Both shapes drawn for one frame under the slab at `at`, then hidden unless paths came meanwhile: their meshes and
    /// materials exist and have drawn before the first pickup.</summary>
    public void Warm(Point3 at, Color team)
    {
        if (_shown.Count > 0) return;
        var under = new Transform3D(Basis.Identity, WorldFrame.ToGodot(new Point3(at.X, at.Y, -1)));
        foreach (var kind in new[] { MarkKind.Foot, MarkKind.Tread }) foreach (var rim in new[] { false, true }) Fill((kind, rim), [(under, team)]);
        Visible = true;
        AfterDraw();
    }

    async void AfterDraw()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (IsInstanceValid(this) && _shown.Count == 0) Visible = false;
    }

    public new void Hide() => Visible = false;

    static readonly Dictionary<bool, StandardMaterial3D> _materials = new();

    /// <summary>An unlit two-sided see-through material coloured per instance (the look is in the colour), drawn after the move's bands
    /// (MoveBandLook.Priority) so the marks read over them, the rims first. Shared, never changed.</summary>
    static StandardMaterial3D Material(bool rim) => _materials.TryGetValue(rim, out var m) ? m : _materials[rim] = new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = MoveBandLook.Priority(MoveBand.Dangerous) + (rim ? 1 : 2),
    };
}
