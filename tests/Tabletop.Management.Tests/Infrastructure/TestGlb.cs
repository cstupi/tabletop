using System.Text;
using System.Text.Json.Nodes;

namespace Tabletop.Management.Tests;

/// <summary>Builds GLBs: one buffer in a BIN chunk, a bufferView per accessor and image, zero-filled geometry.</summary>
public static class TestGlb
{
    /// <summary>Per primitive, count is the accessor the triangle count reads: an indexed primitive has 3 vertices and count
    /// unsigned-byte indices; an unindexed one has count vertices.</summary>
    public static byte[] Build(params (int count, int? mode, bool indexed)[] primitives) => Make(primitives);

    /// <summary>As Build, plus images embedded through bufferViews, then the JSON handed to mutate before it is written.</summary>
    public static byte[] Make(IReadOnlyList<(int count, int? mode, bool indexed)> primitives, IReadOnlyList<(byte[] Bytes, string Mime)>? images = null, Action<JsonObject>? mutate = null)
    {
        var bin = new MemoryStream();
        var views = new JsonArray();
        var accessors = new JsonArray();
        var prims = new JsonArray();
        int View(byte[] data)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = data.Length });
            bin.Write(data);
            return views.Count - 1;
        }
        foreach (var (count, mode, indexed) in primitives)
        {
            var vertices = indexed ? 3 : count;
            accessors.Add(new JsonObject { ["bufferView"] = View(new byte[vertices * 12]), ["componentType"] = 5126, ["count"] = vertices, ["type"] = "VEC3" });
            var prim = new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = accessors.Count - 1 } };
            if (indexed)
            {
                accessors.Add(new JsonObject { ["bufferView"] = View(new byte[count]), ["componentType"] = 5121, ["count"] = count, ["type"] = "SCALAR" });
                prim["indices"] = accessors.Count - 1;
            }
            if (mode is { } m) prim["mode"] = m;
            prims.Add(prim);
        }
        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["accessors"] = accessors,
            ["meshes"] = new JsonArray(new JsonObject { ["primitives"] = prims }),
        };
        if (images is { Count: > 0 }) root["images"] = new JsonArray(images.Select(i => (JsonNode)new JsonObject { ["bufferView"] = View(i.Bytes), ["mimeType"] = i.Mime }).ToArray());
        root["bufferViews"] = views;
        root["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length });
        mutate?.Invoke(root);
        return Assemble(Encoding.UTF8.GetBytes(root.ToJsonString()), bin.ToArray());
    }

    /// <summary>The 12-byte header, the JSON chunk padded with spaces and, when bin is not null, the BIN chunk padded with zeros.</summary>
    public static byte[] Assemble(byte[] json, byte[]? bin)
    {
        var j = Pad(json, 0x20);
        var b = bin is null ? null : Pad(bin, 0);
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + j.Length + (b is null ? 0 : 8 + b.Length)));
        w.Write((uint)j.Length); w.Write(0x4E4F534Au); w.Write(j);
        if (b is not null) { w.Write((uint)b.Length); w.Write(0x004E4942u); w.Write(b); }
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

    private static byte[] Pad(byte[] b, byte with) => b.Length % 4 == 0 ? b : [.. b, .. Enumerable.Repeat(with, 4 - b.Length % 4)];
}

/// <summary>Image files whose headers carry the given dimensions; the pixel data is not decodable.</summary>
public static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        var b = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
        b.AddRange("IHDR"u8.ToArray()); b.AddRange(Be32(width)); b.AddRange(Be32(height)); b.AddRange([8, 6, 0, 0, 0, 0, 0, 0, 0]);
        b.AddRange([0, 0, 0, 0]); b.AddRange("IEND"u8.ToArray()); b.AddRange([0xAE, 0x42, 0x60, 0x82]);
        return [.. b];
    }

    /// <summary>JFIF, then appSegments 64 KiB APP1 segments ahead of the SOF0 frame header.</summary>
    public static byte[] Jpeg(int width, int height, int appSegments = 0)
    {
        var b = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        b.AddRange("JFIF\0"u8.ToArray()); b.AddRange([1, 1, 0, 0, 1, 0, 1, 0, 0]);
        for (var i = 0; i < appSegments; i++) { b.AddRange([0xFF, 0xE1, 0xFF, 0xFF]); b.AddRange(new byte[0xFFFF - 2]); }
        b.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1]);
        b.AddRange([0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 0x3F, 0, 0xFF, 0xD9]);
        return [.. b];
    }

    public enum WebPForm { Lossy, Lossless, Extended }

    public static byte[] WebP(int width, int height, WebPForm form = WebPForm.Extended)
    {
        var chunk = form switch
        {
            WebPForm.Lossy => [.. "VP8 "u8.ToArray(), .. Le32(10), 0, 0, 0, 0x9D, 0x01, 0x2A, (byte)width, (byte)((width >> 8) & 0x3F), (byte)height, (byte)((height >> 8) & 0x3F)],
            WebPForm.Lossless => [.. "VP8L"u8.ToArray(), .. Le32(5), 0x2F, .. Le32((width - 1) | ((height - 1) << 14))],
            _ => (byte[])[.. "VP8X"u8.ToArray(), .. Le32(10), 0, 0, 0, 0, (byte)(width - 1), (byte)((width - 1) >> 8), (byte)((width - 1) >> 16), (byte)(height - 1), (byte)((height - 1) >> 8), (byte)((height - 1) >> 16)],
        };
        if (chunk.Length % 2 == 1) chunk = [.. chunk, 0];
        return [.. "RIFF"u8.ToArray(), .. Le32(4 + chunk.Length), .. "WEBP"u8.ToArray(), .. chunk];
    }

    private static byte[] Be32(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    private static byte[] Le32(int v) => [(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24)];
}
