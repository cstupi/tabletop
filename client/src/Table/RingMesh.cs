using Godot; using Tabletop.Client.Core; using Tabletop.Geometry;
namespace Tabletop.Client.Table;

/// <summary>Readable rings plan: a set of rings as one fill mesh (each ring's area in its kind's opacity of the colour) and one outline mesh,
/// a ribbon the ring_line shader holds at its kind's width in screen pixels. The fill and the outline depth-test, so models and terrain
/// stand over them; a second pass of the outline skips the depth test at low opacity, so a line behind a building still shows.</summary>
public static class RingMesh
{
    /// <summary>Above the board: the fill under the outline, both under any base.</summary>
    public const float FillLift = 0.04f, LineLift = 0.08f, BehindOpacity = 0.3f;
    static readonly Shader LineShader = GD.Load<Shader>("res://content/shaders/ring_line.gdshader");
    static Shader? _behindShader;
    static StandardMaterial3D? _fill; static ShaderMaterial? _line, _behind;

    public static StandardMaterial3D FillMaterial => _fill ??= new()
    {
        VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = -1,
    };
    public static ShaderMaterial LineMaterial => _line ??= new() { Shader = LineShader };
    public static ShaderMaterial BehindMaterial => _behind ??= Behind();

    static ShaderMaterial Behind()
    {
        _behindShader = new Shader { Code = LineShader.Code.Replace("shadows_disabled;", "shadows_disabled, depth_test_disabled;") };
        var m = new ShaderMaterial { Shader = _behindShader, RenderPriority = 1 };
        m.SetShaderParameter("opacity", BehindOpacity);
        return m;
    }

    /// <summary>The fill: every ring's triangles in the colour at its kind's opacity. Null when there is nothing to fill.</summary>
    public static ArrayMesh? Fill(IReadOnlyList<(RingShape Shape, RingLook Look)> rings, Color colour) => Fill(RingArrays.Fill(rings, colour));
    public static ArrayMesh? Fill(RingArrays.Surface? arrays) => arrays is { } a ? Surface(a.Vertices, a.Colours, null, null, null) : null;

    /// <summary>The outline ribbon: four vertices per segment (each end on both sides), UV the side and the width, UV2 the other end.</summary>
    public static ArrayMesh? Line(IReadOnlyList<(RangeRing Ring, RingLook Look)> rings, Color colour) => Line(RingArrays.Line(rings, colour));
    public static ArrayMesh? Line(RingArrays.Surface? arrays) => arrays is { } a ? Surface(a.Vertices, a.Colours, a.Uv, a.Uv2, a.Index) : null;

    static ArrayMesh Surface(Vector3[] vertices, Color[] colours, Vector2[]? uv, Vector2[]? uv2, int[]? index)
    {
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.Color] = colours;
        if (uv is not null) arrays[(int)Mesh.ArrayType.TexUV] = uv;
        if (uv2 is not null) arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        if (index is not null) arrays[(int)Mesh.ArrayType.Index] = index;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (uv is not null) mesh.CustomAabb = Bounds(vertices);   // the shader moves vertices on the screen; keep the ribbon from being culled at the edge
        return mesh;
    }

    static Aabb Bounds(Vector3[] v)
    {
        var box = new Aabb(v[0], Vector3.Zero);
        foreach (var p in v) box = box.Expand(p);
        return box.Grow(1);
    }
}

/// <summary>RingMesh's vertex arrays, built on any thread (no Godot resource is touched), so a ring's mesh costs the main thread only its upload.</summary>
public static class RingArrays
{
    public sealed record Surface(Vector3[] Vertices, Color[] Colours, Vector2[]? Uv = null, Vector2[]? Uv2 = null, int[]? Index = null);

    static Vector3 At(Point2 q, float lift) => WorldFrame.ToGodot(new Point3(q.X, q.Y, lift));

    public static Surface? Fill(IReadOnlyList<(RingShape Shape, RingLook Look)> rings, Color colour)
    {
        var vertices = new List<Vector3>(); var colours = new List<Color>();
        foreach (var (shape, look) in rings)
        {
            var c = colour with { A = look.FillAlpha };
            foreach (var q in shape.Triangles) { vertices.Add(At(q, RingMesh.FillLift)); colours.Add(c); }
        }
        return vertices.Count == 0 ? null : new Surface(vertices.ToArray(), colours.ToArray());
    }

    public static Surface? Line(IReadOnlyList<(RangeRing Ring, RingLook Look)> rings, Color colour)
    {
        var n = rings.Sum(r => r.Ring.Segments.Count);
        if (n == 0) return null;
        var vertices = new Vector3[4 * n]; var uv = new Vector2[4 * n]; var uv2 = new Vector2[4 * n]; var colours = new Color[4 * n]; var index = new int[6 * n];
        var linear = colour.SrgbToLinear() with { A = 1 };
        var k = 0;
        foreach (var (ring, look) in rings)
            foreach (var (a, b) in ring.Segments)
            {
                Vector3 ga = At(a, RingMesh.LineLift), gb = At(b, RingMesh.LineLift); var v = 4 * k;
                // A's two sides, then B's; B looks back at A, so its side +1 lies on A's side -1.
                (vertices[v], uv[v], uv2[v]) = (ga, new(-1, look.LineWidth), new(gb.X, gb.Z));
                (vertices[v + 1], uv[v + 1], uv2[v + 1]) = (ga, new(1, look.LineWidth), new(gb.X, gb.Z));
                (vertices[v + 2], uv[v + 2], uv2[v + 2]) = (gb, new(1, look.LineWidth), new(ga.X, ga.Z));
                (vertices[v + 3], uv[v + 3], uv2[v + 3]) = (gb, new(-1, look.LineWidth), new(ga.X, ga.Z));
                for (var j = 0; j < 4; j++) colours[v + j] = linear;
                var i = 6 * k; (index[i], index[i + 1], index[i + 2], index[i + 3], index[i + 4], index[i + 5]) = (v, v + 1, v + 3, v, v + 3, v + 2);
                k++;
            }
        return new Surface(vertices, colours, uv, uv2, index);
    }
}
