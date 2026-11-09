using Godot; using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol; using Tabletop.Client.Theming;
namespace Tabletop.Client.Table;

/// <summary>ModelMarks on the table: red dots for wounds left and blue for spell tokens, billboarded over each model, and a yellow star ring
/// around the base of every shaken model, and a green band around the base of every model whose unit has activated this round. Dots follow their
/// model's node each frame and hide at table level (End). The star and the band belong to the model's node (PieceNode.SetShaken, SetActivated), round the
/// declared base or the mesh's own wider one: outside the base ring that carries the selection, hover and team colours, the body that carries the
/// warning and the mesh's base, so none of those covers them or is covered; over a shaken model the band lies on the star's inner band, its spikes beyond.</summary>
public partial class ModelMarkNodes : Node3D
{
    /// <summary>The condition key whose models wear the ring: the one the theme's shaken icon is looked up by.</summary>
    const string Ringed = "shaken";
    const float Above = 0.3f, Stack = 0.28f;
    Session _s = null!; TableView _view = null!;
    readonly List<(Guid Model, Node3D Mark, Vector3 Fallback, float Lift)> _marks = new();
    HashSet<PieceNode> _banded = new(), _starred = new();
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
        foreach (var node in _starred) if (IsInstanceValid(node)) node.SetShaken(null);
        _banded.Clear(); _starred.Clear();
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
    }

    /// <summary>The ground marks: every piece of an activated model wears the band (PieceNode.SetActivated) and of a shaken one the star
    /// (SetShaken); every other piece that wore one gives it up.</summary>
    void Bands(Tabletop.Protocol.Projection? p)
    {
        _banded = Wear(p is null ? [] : ModelMarks.Activated(p), _banded, Palette.Flat(Palette.ActivatedRing), (n, m) => n.SetActivated(m));
        _starred = Wear(p is null ? [] : ModelMarks.Ringed(p, Ringed), _starred, Palette.Flat(Palette.ShakenRing), (n, m) => n.SetShaken(m));
    }

    HashSet<PieceNode> Wear(IEnumerable<Guid> ids, HashSet<PieceNode> before, Material material, Action<PieceNode, Material?> set)
    {
        var now = new HashSet<PieceNode>();
        foreach (var id in ids)
            if (_view.PieceAt(id) is { } node && IsInstanceValid(node)) { set(node, material); now.Add(node); }
        foreach (var node in before) if (!now.Contains(node) && IsInstanceValid(node)) set(node, null);
        return now;
    }

    void Add(Guid model, Node3D mark, Vector3 fallback, float lift) { AddChild(mark); _marks.Add((model, mark, fallback, lift)); }

    internal static Label3D Dots(int n, Color colour) => new()
    {
        Text = new string('●', n), Modulate = colour, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 28, PixelSize = 0.01f, OutlineSize = 6, NoDepthTest = true,
    };

    /// <summary>The shaken star ring of a base on its own, in its shared flat material: the shader warm-up's.</summary>
    internal static MeshInstance3D Ring(Base b) => new()
    {
        Mesh = Palette.StarRing(b), MaterialOverride = Palette.Flat(Palette.ShakenRing), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    public override void _Process(double delta)
    {
        var tableLevel = CameraFocus.TableLevel(_view.Camera.Pitch);
        foreach (var (model, mark, fallback, lift) in _marks)
        {
            var node = _view.PieceAt(model);
            mark.Visible = !tableLevel;
            if (node is null || !IsInstanceValid(node)) { mark.Position = fallback + Vector3.Up * lift; continue; }
            mark.GlobalPosition = new Vector3(node.GlobalPosition.X, node.Bounds.End.Y + lift, node.GlobalPosition.Z);
        }
    }
}
