using Godot; using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol; using Tabletop.Client.Theming;
namespace Tabletop.Client.Table;

/// <summary>ModelMarks on the table: red dots for wounds left and blue for spell tokens, billboarded over each model, and a yellow star ring
/// around the base of every shaken model, and a green band around the base of every model whose unit has activated this round. Each follows its model's node,
/// so a glide or a drag carries it; dots hide at table level (End). The band belongs to the model's node (PieceNode.SetActivated): it sits outside the
/// base ring that carries the selection, hover and team colours, the body that carries the warning and the mesh's own base, so none of those covers
/// it or is covered; over a shaken model it lies on the star's inner band, its spikes showing beyond.</summary>
public partial class ModelMarkNodes : Node3D
{
    /// <summary>The condition key whose models wear the ring: the one the theme's shaken icon is looked up by.</summary>
    const string Ringed = "shaken";
    const float Above = 0.3f, Stack = 0.28f, StarLift = 0.02f;
    Session _s = null!; TableView _view = null!;
    readonly List<(Guid Model, Node3D Mark, Vector3 Fallback, float Lift)> _marks = new();
    HashSet<PieceNode> _banded = new();
    Action<RenderCause>? _onFrame;
    Action? _onTheme;

    public void Bind(Session session, TableView view)
    {
        _s = session; _view = view;
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board | RenderCause.Facts)) != 0) Render(); };
        session.Frame.Render += _onFrame; Render();
        _onTheme = () => Bands(_s.Projection); Ui.Changed += _onTheme;   // the band is the theme's success green
    }
    public override void _ExitTree()
    {
        if (_s is not null) _s.Frame.Render -= _onFrame;
        if (_onTheme is not null) Ui.Changed -= _onTheme;
        foreach (var node in _banded) if (IsInstanceValid(node)) node.SetActivated(null);
        _banded.Clear();
    }

    void Render()
    {
        foreach (var (_, mark, _, _) in _marks) mark.QueueFree(); _marks.Clear();
        if (_s.Projection is not { } p) { Bands(null); return; }
        Bands(p);
        foreach (var d in ModelMarks.Dots(p, _s.MaxWounds))
        {
            var lift = Above;
            if (d.Wounds > 0) { Add(d.ModelId, Dots(d.Wounds, Palette.WoundDot), WorldFrame.ToGodot(d.Anchor), lift); lift += Stack; }
            if (d.Tokens > 0) Add(d.ModelId, Dots(d.Tokens, Palette.TokenDot), WorldFrame.ToGodot(d.Anchor), lift);
        }
        foreach (var id in ModelMarks.Ringed(p, Ringed))
            if (p.Models[id].Location is OnTable on) Add(id, Ring(p.Models[id].Slot.Base), WorldFrame.ToGodot(on.Placement.Position), float.NaN);
    }

    /// <summary>The activated bands: every piece of an activated model wears one (PieceNode.SetActivated), every other piece that wore one gives it up.</summary>
    void Bands(Tabletop.Protocol.Projection? p)
    {
        var material = Palette.Flat(Palette.ActivatedRing);
        var now = new HashSet<PieceNode>();
        foreach (var id in p is null ? [] : ModelMarks.Activated(p))
            if (_view.PieceAt(id) is { } node && IsInstanceValid(node)) { node.SetActivated(material); now.Add(node); }
        foreach (var node in _banded) if (!now.Contains(node) && IsInstanceValid(node)) node.SetActivated(null);
        _banded = now;
    }

    void Add(Guid model, Node3D mark, Vector3 fallback, float lift) { AddChild(mark); _marks.Add((model, mark, fallback, lift)); }

    internal static Label3D Dots(int n, Color colour) => new()
    {
        Text = new string('●', n), Modulate = colour, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 28, PixelSize = 0.01f, OutlineSize = 6, NoDepthTest = true,
    };

    /// <summary>A flat band between the base's outline and the star (Palette.Strip), in the shaken ring's shared flat material.</summary>
    internal static MeshInstance3D Ring(Base b) => new()
    {
        Mesh = Palette.Strip(ModelMarks.StarRing(b)), MaterialOverride = Palette.Flat(Palette.ShakenRing), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    public override void _Process(double delta)
    {
        var tableLevel = CameraFocus.TableLevel(_view.Camera.Pitch);
        foreach (var (model, mark, fallback, lift) in _marks)
        {
            var node = _view.PieceAt(model);
            var ring = float.IsNaN(lift);
            mark.Visible = ring || !tableLevel;
            if (node is null || !IsInstanceValid(node)) { mark.Position = fallback + Vector3.Up * (ring ? StarLift : lift); continue; }
            if (ring) { mark.GlobalPosition = node.GlobalPosition + Vector3.Up * StarLift; mark.GlobalRotation = node.GlobalRotation; }
            else mark.GlobalPosition = new Vector3(node.GlobalPosition.X, node.Bounds.End.Y + lift, node.GlobalPosition.Z);
        }
    }
}
