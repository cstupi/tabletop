using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;

namespace Tabletop.Management.Content.Assets;

public sealed record StarterMesh(string Name, Base ModelledBase, byte[] Glb, string Hash);

/// <summary>The miniatures every account's library starts with (spec 2026-10-01-starter-meshes-design). Inches, Z up, front toward +Y,
/// so each asset row is scale 1 and orientation (0, 0, 0). Built in memory and deterministic: the bytes change only when this code does.</summary>
public static class StarterMeshes
{
    private static readonly int[] RoundBasesMm = [25, 32, 40, 50, 60];
    private const float BaseThickness = 0.125f;

    public static IReadOnlyList<StarterMesh> All { get; } = Build();

    public static IReadOnlyList<StarterMesh> Build()
    {
        var meshes = RoundBasesMm.Select(mm =>
        {
            var b = Base.Circle(mm / 25.4);
            return Mesh($"Figure, {mm}mm round", b, Figure((float)b.A, (float)ArmyForgeImport.HeightFromBase(b)));
        }).ToList();
        var hull = ArmyForgeImport.DefaultBody;
        meshes.Add(Mesh("Hull, 221 × 119mm", hull.Base, Hull((float)hull.Base.A, (float)hull.Base.B, (float)hull.Height)));
        return meshes;
    }

    private static StarterMesh Mesh(string name, Base b, List<Vector3> triangles)
    {
        var glb = Glb(triangles);
        return new StarterMesh(name, b, glb, Convert.ToHexStringLower(SHA256.HashData(glb)));
    }

    /// <summary>A base disc, a tapered body, a head reaching the full height, and a wedge on the +Y side of the body showing the facing.</summary>
    private static List<Vector3> Figure(float d, float h)
    {
        var t = new List<Vector3>();
        Frustum(t, 32, d / 2, d / 2, 0, BaseThickness);
        var bodyTop = 0.72f * h;
        Frustum(t, 16, 0.30f * d, 0.20f * d, BaseThickness, bodyTop);
        var r = (h - bodyTop) / 2;
        Sphere(t, 16, 8, new Vector3(0, 0, h - r), r);
        float w = 0.08f * d, y0 = 0.22f * d, y1 = 0.42f * d, z0 = 0.35f * h, z1 = 0.55f * h;
        Prism(t, [new(-w, y0), new(w, y0), new(0, y1)], z0, z1, xy: true);
        return t;
    }

    /// <summary>The rect footprint extruded to the height, its top front edge bevelled so the facing reads from any side.</summary>
    private static List<Vector3> Hull(float length, float width, float height)
    {
        var t = new List<Vector3>();
        float l = length / 2;
        // The side profile in (Y, Z), extruded across X.
        Prism(t, [new(-l, 0), new(l, 0), new(l, 0.45f * height), new(l - 0.2f * length, height), new(-l, height)], -width / 2, width / 2, xy: false);
        return t;
    }

    /// <summary>An n-sided frustum between z0 and z1 with both caps. Vertex 0 sits on +X, so with n divisible by 4 the extents are exactly ±r on both axes.</summary>
    private static void Frustum(List<Vector3> t, int n, float r0, float r1, float z0, float z1)
    {
        Vector3 At(int i, float r, float z) { var a = 2 * MathF.PI * i / n; return new(r * MathF.Cos(a), r * MathF.Sin(a), z); }
        for (var i = 0; i < n; i++)
        {
            Vector3 a0 = At(i, r0, z0), b0 = At(i + 1, r0, z0), a1 = At(i, r1, z1), b1 = At(i + 1, r1, z1);
            Quad(t, a0, b0, b1, a1);
            Tri(t, new(0, 0, z0), b0, a0);
            Tri(t, new(0, 0, z1), a1, b1);
        }
    }

    private static void Sphere(List<Vector3> t, int slices, int stacks, Vector3 c, float r)
    {
        Vector3 At(int i, int j)
        {
            float phi = MathF.PI * j / stacks, theta = 2 * MathF.PI * i / slices;
            return c + new Vector3(r * MathF.Sin(phi) * MathF.Cos(theta), r * MathF.Sin(phi) * MathF.Sin(theta), r * MathF.Cos(phi));
        }
        for (var j = 0; j < stacks; j++)
            for (var i = 0; i < slices; i++)
                Quad(t, At(i, j + 1), At(i + 1, j + 1), At(i + 1, j), At(i, j));
    }

    /// <summary>A counter-clockwise convex polygon extruded between lo and hi: along Z when xy (the polygon is in X, Y), along X otherwise (the polygon is in Y, Z).
    /// Both frames are right-handed (X × Y = Z, Y × Z = X), so one winding serves both.</summary>
    private static void Prism(List<Vector3> t, Vector2[] poly, float lo, float hi, bool xy)
    {
        Vector3 P(Vector2 p, float s) => xy ? new(p.X, p.Y, s) : new(s, p.X, p.Y);
        for (var i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
            Quad(t, P(a, lo), P(b, lo), P(b, hi), P(a, hi));
        }
        for (var i = 1; i + 1 < poly.Length; i++)
        {
            Tri(t, P(poly[0], lo), P(poly[i + 1], lo), P(poly[i], lo));
            Tri(t, P(poly[0], hi), P(poly[i], hi), P(poly[i + 1], hi));
        }
    }

    private static void Quad(List<Vector3> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(t, a, b, c); Tri(t, a, c, d); }

    /// <summary>Counter-clockwise seen from outside. A degenerate triangle (a sphere's pole) is dropped.</summary>
    private static void Tri(List<Vector3> t, Vector3 a, Vector3 b, Vector3 c)
    {
        if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-12f) return;
        t.Add(a); t.Add(b); t.Add(c);
    }

    /// <summary>One flat-shaded, non-indexed primitive: POSITION and NORMAL in one buffer, one grey material.</summary>
    private static byte[] Glb(List<Vector3> tris)
    {
        var normals = new List<Vector3>(tris.Count);
        for (var i = 0; i < tris.Count; i += 3)
        {
            var n = Vector3.Normalize(Vector3.Cross(tris[i + 1] - tris[i], tris[i + 2] - tris[i]));
            normals.Add(n); normals.Add(n); normals.Add(n);
        }
        var bin = new byte[tris.Count * 24];
        for (var i = 0; i < tris.Count; i++)
        {
            Write(bin.AsSpan(i * 12), tris[i]);
            Write(bin.AsSpan(tris.Count * 12 + i * 12), normals[i]);
        }
        var min = new[] { tris.Min(v => v.X), tris.Min(v => v.Y), tris.Min(v => v.Z) };
        var max = new[] { tris.Max(v => v.X), tris.Max(v => v.Y), tris.Max(v => v.Z) };
        var gltf = new
        {
            asset = new { version = "2.0", generator = "Tabletop.Management StarterMeshes" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0 } },
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 }, material = 0, mode = 4 } } } },
            materials = new[] { new { pbrMetallicRoughness = new { baseColorFactor = new[] { 0.6f, 0.6f, 0.62f, 1f }, metallicFactor = 0f, roughnessFactor = 0.8f } } },
            buffers = new[] { new { byteLength = bin.Length } },
            bufferViews = new[] { new { buffer = 0, byteOffset = 0, byteLength = tris.Count * 12, target = 34962 }, new { buffer = 0, byteOffset = tris.Count * 12, byteLength = tris.Count * 12, target = 34962 } },
            accessors = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = tris.Count, type = "VEC3", min, max },
                new { bufferView = 1, componentType = 5126, count = tris.Count, type = "VEC3" },
            },
        };
        var json = Pad(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(gltf)), 0x20);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(GlbHeader.Magic); w.Write(2u); w.Write((uint)(12 + 8 + json.Length + 8 + bin.Length));
        w.Write((uint)json.Length); w.Write(GlbHeader.JsonChunkType); w.Write(json);
        w.Write((uint)bin.Length); w.Write(0x004E4942u); w.Write(bin);   // "BIN\0"; 24 bytes a vertex keeps it 4-aligned
        return ms.ToArray();
    }

    private static void Write(Span<byte> to, Vector3 v)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(to, v.X);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(to[4..], v.Y);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(to[8..], v.Z);
    }

    private static byte[] Pad(byte[] b, byte with) => b.Length % 4 == 0 ? b : [.. b, .. Enumerable.Repeat(with, 4 - b.Length % 4)];
}
