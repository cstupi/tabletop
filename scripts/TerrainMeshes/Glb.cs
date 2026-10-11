using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Tabletop.TerrainMeshes;

/// <summary>Writes one flat-shaded mesh as a glTF binary: one node, one mesh, one primitive, one material, POSITION and NORMAL interleaved at stride 24.</summary>
public static class Glb
{
    public static byte[] Of(string name, IReadOnlyList<(Vector3 Position, Vector3 Normal)> vertices, float[] colour)
    {
        var floats = new float[vertices.Count * 6];
        for (var i = 0; i < vertices.Count; i++)
        {
            var (p, n) = vertices[i];
            new[] { p.X, p.Y, p.Z, n.X, n.Y, n.Z }.CopyTo(floats, i * 6);
        }
        var bin = new byte[floats.Length * 4];
        Buffer.BlockCopy(floats, 0, bin, 0, bin.Length);
        var min = new[] { vertices.Min(v => v.Position.X), vertices.Min(v => v.Position.Y), vertices.Min(v => v.Position.Z) };
        var max = new[] { vertices.Max(v => v.Position.X), vertices.Max(v => v.Position.Y), vertices.Max(v => v.Position.Z) };
        var json = JsonSerializer.Serialize(new
        {
            asset = new { generator = "scripts/TerrainMeshes", version = "2.0" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { name, mesh = 0 } },
            meshes = new[] { new { name, primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 }, mode = 4, material = 0 } } } },
            materials = new[] { new { name, pbrMetallicRoughness = new { baseColorFactor = colour, metallicFactor = 0 } } },
            buffers = new[] { new { byteLength = bin.Length } },
            bufferViews = new[] { new { buffer = 0, byteOffset = 0, byteLength = bin.Length, byteStride = 24, target = 34962 } },
            accessors = new object[]
            {
                new { bufferView = 0, byteOffset = 0, componentType = 5126, count = vertices.Count, type = "VEC3", min, max },
                new { bufferView = 0, byteOffset = 12, componentType = 5126, count = vertices.Count, type = "VEC3" },
            },
        });
        var (text, data) = (Pad(Encoding.UTF8.GetBytes(json), (byte)' '), Pad(bin, 0)); using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + text.Length + 8 + data.Length));
        w.Write((uint)text.Length); w.Write(0x4E4F534Au); w.Write(text);
        w.Write((uint)data.Length); w.Write(0x004E4942u); w.Write(data);
        return s.ToArray();
    }

    static byte[] Pad(byte[] b, byte with) => b.Concat(Enumerable.Repeat(with, (4 - b.Length % 4) % 4)).ToArray();
}
