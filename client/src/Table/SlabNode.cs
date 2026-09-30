using Godot;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>The board at its real size, 1" thick, top face at Z = 0. Corner origin: the slab spans [0,W] × [0,L] in the wire frame.</summary>
public partial class SlabNode : Node3D
{
    const float Thickness = 1f;
    MeshInstance3D? _mesh;

    public void Set(TableSize size, VisualRef texture)
    {
        _mesh?.QueueFree();
        _mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3((float)size.Width, Thickness, (float)size.Length) } };
        _mesh.Position = WorldFrame.ToGodot(new Point3(size.Width / 2, size.Length / 2, -Thickness / 2));
        var material = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.45f, 0.3f) };
        if (texture.IsBuiltin && ResourceLoader.Exists(Core.BuiltinContent.TextureFile(texture.Builtin!)))
        { material.AlbedoTexture = GD.Load<Texture2D>(Core.BuiltinContent.TextureFile(texture.Builtin!)); material.AlbedoColor = Colors.White; material.Uv1Scale = new Vector3((float)size.Width / 12, (float)size.Length / 12, 1); }
        _mesh.MaterialOverride = material;
        AddChild(_mesh);
    }
}
