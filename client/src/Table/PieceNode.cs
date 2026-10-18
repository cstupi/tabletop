using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>A model: its base extruded to its height, then the mesh decorating it. The proxy is the thing the rules measure; it never goes away.</summary>
public partial class PieceNode : Node3D, IDrawn<ModelInPlay>
{
    public Guid ModelId { get; init; }
    public Guid Id => ModelId;
    public ModelInPlay Drawn { get; set; } = null!;
    public Guid UnitId { get; init; }
    MeshInstance3D _proxy = null!;
    Node3D? _mesh;
    Color _base = Palette.Proxy, _tint = Palette.Proxy;
    bool _highlighted, _ghost, _showHitboxes = true;
    Node3D? _ring;
    readonly StandardMaterial3D _ringMaterial = new() { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    Base _footprint = Base.Circle(1);

    public void SetBody(Base b, double height)
    {
        _proxy?.QueueFree(); _ring?.QueueFree(); _ring = null;
        _footprint = b;
        var h = (float)height;
        Mesh mesh = b.Shape switch
        {
            BaseShape.Circle => new CylinderMesh { TopRadius = (float)b.A / 2, BottomRadius = (float)b.A / 2, Height = h },
            BaseShape.Rect => new BoxMesh { Size = new Vector3((float)b.B, h, (float)b.A) },      // width across X, length along the facing axis
            _ => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = h },
        };
        _proxy = new MeshInstance3D { Mesh = mesh, Position = new Vector3(0, h / 2, 0), MaterialOverride = Palette.Translucent(_base) };
        if (b.Shape == BaseShape.Oval) _proxy.Scale = new Vector3((float)b.B, 1, (float)b.A);   // oval length along facing (+Y wire = −Z Godot)
        AddChild(_proxy);
    }

    public void SetMesh(Node3D? scene)
    {
        _mesh?.QueueFree(); _mesh = scene;
        if (scene is null) return;
        if (_ghost) Ghostly(scene);
        AddChild(scene); Apply();
    }

    /// <summary>
    /// A placement ghost (a deployment, a reserve or a disembark being placed, another player's proposal): the model's own mesh, once
    /// loaded, drawn see-through under the ghost colour and casting no shadow, the hitbox faint beneath, and a ring on the base's
    /// outline, so it reads as the model that will stand there and not as one that does. Until the mesh loads, the hitbox alone.
    /// </summary>
    public void SetGhost(Color colour)
    {
        _ghost = true; _tint = new Color(colour, 0.6f);
        if (!_highlighted) _base = _tint;
        AddChild(Palette.BaseRing(_footprint));
        if (_mesh is not null) Ghostly(_mesh);
        Apply();
    }

    /// <summary>Every surface under the mesh: see-through per instance and tinted by one shared overlay, so the mesh resources stay the template's.</summary>
    static void Ghostly(Node node)
    {
        if (node is GeometryInstance3D g) { g.Transparency = Palette.GhostTransparency; g.MaterialOverlay = Palette.GhostOverlay; g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; }
        foreach (var child in node.GetChildren()) Ghostly(child);
    }

    /// <summary>A highlight (selection, glow, eligibility, ghost) over the team tint; null goes back to the tint.</summary>
    public void SetHighlight(Color? colour)
    {
        _highlighted = colour is not null;
        _base = colour is { } c ? new Color(c, 0.6f) : _tint;
        Apply();
    }

    /// <summary>The unhighlighted colour: Palette.Tinted for the model's team. A highlight keeps precedence until it is cleared. A ghost keeps its colour.</summary>
    public void SetTint(Color tint)
    {
        if (tint == _tint || _ghost) return;
        _tint = tint;
        if (!_highlighted) { _base = tint; Apply(); }
    }

    /// <summary>Show hitboxes (ClientSettings.ShowHitboxes), live. Off, a model with a mesh draws the mesh and a thin base ring in its team colour or highlight.</summary>
    public void SetShowHitboxes(bool on) { if (on == _showHitboxes) return; _showHitboxes = on; Apply(); }

    void Apply()
    {
        var look = HitboxLook.Piece(_showHitboxes, _mesh is not null, _ghost);
        _proxy.Visible = look != PieceProxy.RingOnly;
        _proxy.MaterialOverride = Palette.Translucent(look == PieceProxy.Solid ? _base : _base with { A = 0.12f });
        if (look != PieceProxy.RingOnly) { _ring?.QueueFree(); _ring = null; return; }
        _ringMaterial.AlbedoColor = _base with { A = 1 };
        if (_ring is null) AddChild(_ring = Palette.BaseRing(_footprint, _ringMaterial));
    }

    public Aabb Bounds => _proxy.GlobalTransform * _proxy.GetAabb();
}
