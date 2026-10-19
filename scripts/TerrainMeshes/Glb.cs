using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Tabletop.TerrainMeshes;

/// <summary>Writes a <see cref="Model"/> as a glTF binary: one primitive per material, in one node and mesh for the opaque
/// materials and a second for the translucent ones (smog, water), so the client can keep the translucent ones out of the shadow
/// pass. Each primitive has POSITION and NORMAL (float), COLOR_0 (normalized unsigned bytes, linear) and indices (16- or 32-bit),
/// each in its own buffer view. Neither node has a transform.</summary>
public static class Glb
{
    public static byte[] Of(Model model)
    {
        var bin = new MemoryStream();
        var views = new List<object>(); var accessors = new List<object>(); var materials = new List<object>();
        var layers = new[] { (Name: model.Name, Primitives: new List<object>()), (Name: $"{model.Name}-translucent", Primitives: new List<object>()) };
        int View(byte[] bytes, int target)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            views.Add(new { buffer = 0, byteOffset = bin.Length, byteLength = bytes.Length, target });
            bin.Write(bytes);
            return views.Count - 1;
        }
        int Accessor(object a) { accessors.Add(a); return accessors.Count - 1; }

        foreach (var part in model.Parts)
        {
            var (p, n, c, ix) = Welded(part);
            var count = p.Count;
            var positions = Floats(p); var normals = Floats(n);
            var colours = c.SelectMany(c => new[] { Byte(Linear(c.X)), Byte(Linear(c.Y)), Byte(Linear(c.Z)), Byte(c.W) }).ToArray();
            var wide = count > ushort.MaxValue;
            var indices = wide ? ix.SelectMany(i => BitConverter.GetBytes((uint)i)).ToArray() : ix.SelectMany(i => BitConverter.GetBytes((ushort)i)).ToArray();
            var position = Accessor(new
            {
                bufferView = View(positions, 34962), componentType = 5126, count, type = "VEC3",
                min = new[] { p.Min(v => v.X), p.Min(v => v.Y), p.Min(v => v.Z) }, max = new[] { p.Max(v => v.X), p.Max(v => v.Y), p.Max(v => v.Z) },
            });
            var normal = Accessor(new { bufferView = View(normals, 34962), componentType = 5126, count, type = "VEC3" });
            var colour = Accessor(new { bufferView = View(colours, 34962), componentType = 5121, normalized = true, count, type = "VEC4" });
            var index = Accessor(new { bufferView = View(indices, 34963), componentType = wide ? 5125 : 5123, count = ix.Count, type = "SCALAR" });
            var m = part.Material;
            materials.Add(new Dictionary<string, object>
            {
                ["name"] = m.Name,
                ["pbrMetallicRoughness"] = new { baseColorFactor = new[] { 1f, 1f, 1f, m.Alpha }, metallicFactor = m.Metallic, roughnessFactor = m.Roughness },
                ["alphaMode"] = m.Blend ? "BLEND" : "OPAQUE",
                ["doubleSided"] = m.DoubleSided,
                ["emissiveFactor"] = m.Emissive is { } e ? new[] { Linear(e.X), Linear(e.Y), Linear(e.Z) } : new[] { 0f, 0f, 0f },
            });
            layers[m.Blend ? 1 : 0].Primitives.Add(new { attributes = new { POSITION = position, NORMAL = normal, COLOR_0 = colour }, indices = index, mode = 4, material = materials.Count - 1 });
        }
        while (bin.Length % 4 != 0) bin.WriteByte(0);
        var data = bin.ToArray();
        var used = layers.Where(l => l.Primitives.Count > 0).ToList();
        var json = JsonSerializer.Serialize(new
        {
            asset = new { generator = "scripts/TerrainMeshes", version = "2.0" },
            scene = 0,
            scenes = new[] { new { nodes = used.Select((_, i) => i).ToArray() } },
            nodes = used.Select((l, i) => new { name = l.Name, mesh = i }).ToArray(),
            meshes = used.Select(l => new { name = l.Name, primitives = l.Primitives }).ToArray(),
            materials,
            buffers = new[] { new { byteLength = data.Length } },
            bufferViews = views,
            accessors,
        });
        var text = Encoding.UTF8.GetBytes(json);
        text = text.Concat(Enumerable.Repeat((byte)' ', (4 - text.Length % 4) % 4)).ToArray();
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + text.Length + 8 + data.Length));
        w.Write((uint)text.Length); w.Write(0x4E4F534Au); w.Write(text);
        w.Write((uint)data.Length); w.Write(0x004E4942u); w.Write(data);
        return s.ToArray();
    }

    /// <summary>The part with exact duplicate vertices (same position, normal and written colour) shared, in first-use order.</summary>
    static (List<Vector3> P, List<Vector3> N, List<Vector4> C, List<int> I) Welded(Part part)
    {
        var seen = new Dictionary<(Vector3, Vector3, uint), int>();
        var (p, n, c, ix) = (new List<Vector3>(), new List<Vector3>(), new List<Vector4>(), new List<int>(part.Indices.Count));
        foreach (var i in part.Indices)
        {
            var col = part.Colours[i];
            var key = (part.Positions[i], part.Normals[i], (uint)(Byte(Linear(col.X)) | Byte(Linear(col.Y)) << 8 | Byte(Linear(col.Z)) << 16 | Byte(col.W) << 24));
            if (!seen.TryGetValue(key, out var at)) { seen[key] = at = p.Count; p.Add(part.Positions[i]); n.Add(part.Normals[i]); c.Add(col); }
            ix.Add(at);
        }
        return (p, n, c, ix);
    }

    static byte[] Floats(List<Vector3> v)
    {
        var f = new float[v.Count * 3];
        for (var i = 0; i < v.Count; i++) (f[i * 3], f[i * 3 + 1], f[i * 3 + 2]) = (v[i].X, v[i].Y, v[i].Z);
        var b = new byte[f.Length * 4];
        Buffer.BlockCopy(f, 0, b, 0, b.Length);
        return b;
    }

    /// <summary>sRGB to linear: glTF vertex colours and colour factors are linear.</summary>
    public static float Linear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    static byte Byte(float v) => (byte)Math.Clamp(MathF.Round(v * 255), 0, 255);
}
