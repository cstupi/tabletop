using Godot;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>The board at its real size, 1" thick, top face at Z = 0, on a wooden table that fades into the room. Corner origin: the
/// slab spans [0,W] × [0,L] in the wire frame. A built-in print draws through <c>board.gdshader</c> (spec
/// 2026-10-19-tabletop-look-design.md → Boards); its edge is the print's mean colour, darkened like a painted board edge.</summary>
public partial class SlabNode : Node3D
{
    const float Thickness = 1f;
    /// <summary>Inches per pixel of a board print and of the table's wood, and the relief a full height rise stands for.</summary>
    const float BoardPixel = 12f / 1024, WoodPixel = 24f / 1024, BoardRelief = 0.05f, WoodRelief = 0.01f;
    const string TableWood = "res://content/table/wood.png";
    static readonly Shader BoardShader = GD.Load<Shader>("res://content/shaders/board.gdshader"), TableShader = GD.Load<Shader>("res://content/shaders/table.gdshader");
    MeshInstance3D? _mesh, _table;
    int _version;

    /// <summary>The board and the table for a size and print. The prints load off the main thread; the slab drawn before stays until
    /// the new one is built. Completes when it is drawn, or when a later call replaced it.</summary>
    public async Task Set(TableSize size, VisualRef texture)
    {
        var version = ++_version;
        var print = texture.IsBuiltin ? await PrintTextures.LoadAsync(Core.BuiltinContent.TextureFile(texture.Builtin!), BoardRelief / BoardPixel) : null;
        var wood = await PrintTextures.LoadAsync(TableWood, WoodRelief / WoodPixel);
        if (version != _version || !IsInstanceValid(this)) return;
        _mesh?.QueueFree(); _table?.QueueFree();
        float w = (float)size.Width, l = (float)size.Length;
        var edge = print is null ? new Color(0.22f, 0.24f, 0.2f) : print.Mean.Darkened(0.45f);
        _mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(w, Thickness, l) }, Position = WorldFrame.ToGodot(new Point3(size.Width / 2, size.Length / 2, -Thickness / 2)),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = edge, Roughness = 0.9f } };
        if (print is not null)
        {
            var top = new ShaderMaterial { Shader = BoardShader };
            top.SetShaderParameter("print", print.Albedo); top.SetShaderParameter("print_normal", print.Normal);
            _mesh.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(w, l) }, Position = new Vector3(0, Thickness / 2 + 0.002f, 0), MaterialOverride = top });
        }
        AddChild(_mesh);
        _table = Table(w, l, wood);
        if (_table is not null) AddChild(_table);
    }

    /// <summary>The table under the board, 60" past each edge, its wood fading to the clear colour from 3" to 30" out.</summary>
    static MeshInstance3D? Table(float w, float l, PrintTextures.Print? wood)
    {
        if (wood is null) return null;
        const float margin = 60;
        var material = new ShaderMaterial { Shader = TableShader };
        material.SetShaderParameter("wood", wood.Albedo); material.SetShaderParameter("wood_normal", wood.Normal);
        material.SetShaderParameter("board_min", new Vector2(0, -l)); material.SetShaderParameter("board_max", new Vector2(w, 0));
        material.SetShaderParameter("far_colour", RenderingServer.GetDefaultClearColor());
        return new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(w + 2 * margin, l + 2 * margin) }, Position = WorldFrame.ToGodot(new Point3(w / 2.0, l / 2.0, -Thickness)),
            MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }
}
