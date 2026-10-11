using Godot;
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
    Color _base = Palette.Proxy;

    public void SetBody(Base b, double height)
    {
        _proxy?.QueueFree();
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
        if (scene is not null) { AddChild(scene); _proxy.MaterialOverride = Palette.Translucent(_base with { A = 0.12f }); }
    }

    public void SetHighlight(Color? colour)
    {
        _base = colour is { } c ? new Color(c, 0.6f) : Palette.Proxy;
        _proxy.MaterialOverride = Palette.Translucent(_mesh is null ? _base : _base with { A = 0.12f });
    }

    public Aabb Bounds => _proxy.GlobalTransform * _proxy.GetAabb();
}
