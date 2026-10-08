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
        _mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.45f, 0.3f) };
        // BoxMesh UVs are a 3x2 atlas of its six faces, so a texture scale on the box tiles 36" x 24" per repeat; the top is its own plane, whose UVs run 0..1 across the face, so one tile is 12" x 12".
        if (texture.IsBuiltin && ResourceLoader.Exists(Core.BuiltinContent.TextureFile(texture.Builtin!)))
        {
            var top = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2((float)size.Width, (float)size.Length) }, Position = new Vector3(0, Thickness / 2 + 0.002f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoTexture = GD.Load<Texture2D>(Core.BuiltinContent.TextureFile(texture.Builtin!)), Uv1Scale = new Vector3((float)size.Width / 12, (float)size.Length / 12, 1) } };
            _mesh.AddChild(top);                                                           // child of the box, so the one QueueFree on the next Set takes both
        }
        AddChild(_mesh);
    }
}
