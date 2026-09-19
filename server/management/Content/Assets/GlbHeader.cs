using System.Buffers.Binary;
using System.Text.Json;

namespace Tabletop.Management.Content.Assets;

/// <summary>The only server-side look at an upload: the 12-byte header, the 8-byte chunk header, then the JSON chunk.</summary>
public static class GlbHeader
{
    public const uint Magic = 0x46546C67;       // "glTF"
    public const uint JsonChunkType = 0x4E4F534A; // "JSON"
    public const int HeaderBytes = 20;
    public const long WarnAbove = 100_000;

    public static bool IsGlb(ReadOnlySpan<byte> first12) =>
        first12.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(first12) == Magic && BinaryPrimitives.ReadUInt32LittleEndian(first12[4..]) == 2;

    public static int JsonChunkLength(ReadOnlySpan<byte> first20)
    {
        if (first20.Length < HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(first20[16..]) != JsonChunkType)
            throw new FormatException("First chunk is not JSON.");
        return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(first20[12..]));
    }

    public static long CountTriangles(ReadOnlySpan<byte> jsonChunk)
    {
        using var doc = JsonDocument.Parse(jsonChunk.ToArray());
        var root = doc.RootElement;
        if (!root.TryGetProperty("meshes", out var meshes) || !root.TryGetProperty("accessors", out var accessors)) return 0;
        long total = 0;
        foreach (var mesh in meshes.EnumerateArray())
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                var mode = prim.TryGetProperty("mode", out var m) ? m.GetInt32() : 4;
                int accessor = prim.TryGetProperty("indices", out var idx) ? idx.GetInt32()
                    : prim.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("POSITION", out var pos) ? pos.GetInt32() : -1;
                if (accessor < 0) continue;
                long count = accessors[accessor].GetProperty("count").GetInt64();
                total += mode switch { 4 => count / 3, 5 or 6 => Math.Max(0, count - 2), _ => 0 };
            }
        return total;
    }
}
