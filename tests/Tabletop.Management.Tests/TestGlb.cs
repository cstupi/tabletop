using System.Text;

namespace Tabletop.Management.Tests;

/// <summary>Builds a structurally valid GLB whose JSON chunk declares the given primitives. The binary chunk is empty.</summary>
public static class TestGlb
{
    public static byte[] Build(params (int count, int? mode, bool indexed)[] primitives)
    {
        var accessors = string.Join(",", primitives.Select(p => $"{{\"componentType\":5123,\"count\":{p.count},\"type\":\"SCALAR\"}}"));
        var prims = string.Join(",", primitives.Select((p, i) =>
            (p.indexed ? $"{{\"attributes\":{{\"POSITION\":{i}}},\"indices\":{i}" : $"{{\"attributes\":{{\"POSITION\":{i}}}") + (p.mode is null ? "}" : $",\"mode\":{p.mode}}}")));
        var json = Encoding.UTF8.GetBytes($"{{\"asset\":{{\"version\":\"2.0\"}},\"accessors\":[{accessors}],\"meshes\":[{{\"primitives\":[{prims}]}}]}}");
        var padded = json.Length % 4 == 0 ? json : json.Concat(Enumerable.Repeat((byte)0x20, 4 - json.Length % 4)).ToArray();
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + padded.Length));
        w.Write((uint)padded.Length); w.Write(0x4E4F534Au); w.Write(padded);
        return ms.ToArray();
    }

    /// <summary>A structurally valid GLB header (magic, version 2) around a first chunk of the given type and raw bytes — for exercising malformed chunk content.</summary>
    public static byte[] BuildRaw(uint chunkType, byte[] payload)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + payload.Length));
        w.Write((uint)payload.Length); w.Write(chunkType); w.Write(payload);
        return ms.ToArray();
    }
}
