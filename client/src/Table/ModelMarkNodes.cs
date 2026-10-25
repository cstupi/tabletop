using Godot; using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>ModelMarks on the table: red dots for wounds left and blue for spell tokens, billboarded over each model, and a yellow star ring
/// around the base of every shaken model. Each follows its model's node, so a glide or a drag carries it; dots hide at table level (End).</summary>
public partial class ModelMarkNodes : Node3D
{
    /// <summary>The condition key whose models wear the ring: the one the theme's shaken icon is looked up by.</summary>
    const string Ringed = "shaken";
    const float Above = 0.3f, Stack = 0.28f;
    Session _s = null!; TableView _view = null!;
    readonly List<(Guid Model, Node3D Mark, Vector3 Fallback, float Lift)> _marks = new();
    Action<RenderCause>? _onFrame;

    public void Bind(Session session, TableView view)
    {
        _s = session; _view = view;
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board | RenderCause.Facts)) != 0) Render(); };
        session.Frame.Render += _onFrame; Render();
    }
    public override void _ExitTree() { if (_s is not null) _s.Frame.Render -= _onFrame; }

    void Render()
    {
        foreach (var (_, mark, _, _) in _marks) mark.QueueFree(); _marks.Clear();
        if (_s.Projection is not { } p) return;
        foreach (var d in ModelMarks.Dots(p, _s.MaxWounds))
        {
            var lift = Above;
            if (d.Wounds > 0) { Add(d.ModelId, Dots(d.Wounds, Palette.WoundDot), WorldFrame.ToGodot(d.Anchor), lift); lift += Stack; }
            if (d.Tokens > 0) Add(d.ModelId, Dots(d.Tokens, Palette.TokenDot), WorldFrame.ToGodot(d.Anchor), lift);
        }
        foreach (var id in ModelMarks.Ringed(p, Ringed))
            if (p.Models[id].Location is OnTable on) Add(id, Ring(p.Models[id].Slot.Base), WorldFrame.ToGodot(on.Placement.Position), float.NaN);
    }

    void Add(Guid model, Node3D mark, Vector3 fallback, float lift) { AddChild(mark); _marks.Add((model, mark, fallback, lift)); }

    internal static Label3D Dots(int n, Color colour) => new()
    {
        Text = new string('●', n), Modulate = colour, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 28, PixelSize = 0.01f, OutlineSize = 6, NoDepthTest = true,
    };

    /// <summary>A flat band between the base's outline and the star: two triangles per sample, wire (x, y) to Godot (x, 0, -y).</summary>
    internal static MeshInstance3D Ring(Base b)
    {
        var ring = ModelMarks.StarRing(b);
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        static Vector3 G(Point2 q) => new((float)q.X, 0, (float)-q.Y);
        for (var i = 0; i < ring.Count; i++)
        {
            var (a, c) = (ring[i], ring[(i + 1) % ring.Count]);
            foreach (var v in new[] { a.Inner, a.Outer, c.Outer, a.Inner, c.Outer, c.Inner }) st.AddVertex(G(v));
        }
        var material = new StandardMaterial3D { AlbedoColor = Palette.ShakenRing, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material };
    }

    public override void _Process(double delta)
    {
        var tableLevel = CameraFocus.TableLevel(_view.Camera.Pitch);
        foreach (var (model, mark, fallback, lift) in _marks)
        {
            var node = _view.PieceAt(model);
            var ring = float.IsNaN(lift);
            mark.Visible = ring || !tableLevel;
            if (node is null || !IsInstanceValid(node)) { mark.Position = fallback + Vector3.Up * (ring ? 0.02f : lift); continue; }
            if (ring) { mark.GlobalPosition = node.GlobalPosition + Vector3.Up * 0.02f; mark.GlobalRotation = node.GlobalRotation; }
            else mark.GlobalPosition = new Vector3(node.GlobalPosition.X, node.Bounds.End.Y + lift, node.GlobalPosition.Z);
        }
    }
}
