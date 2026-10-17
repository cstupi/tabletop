using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace Tabletop.Management.Content.Assets;

/// <summary>The server-side check of an uploaded GLB: the 12-byte header, the JSON chunk, the BIN chunk's bounds, and each
/// embedded image's header. Every structural fault answers one fixed text; the detail is for the log.</summary>
public static class GlbHeader
{
    public const uint Magic = 0x46546C67;       // "glTF"
    public const uint JsonChunkType = 0x4E4F534A; // "JSON"
    public const uint BinChunkType = 0x004E4942;  // "BIN\0"
    public const int HeaderBytes = 20;
    public const string NotGlb = "Not a GLB. STL and .gltf with external files are not accepted.";
    public const string Malformed = "The GLB is malformed.";

    public static bool IsGlb(ReadOnlySpan<byte> first12) =>
        first12.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(first12) == Magic && BinaryPrimitives.ReadUInt32LittleEndian(first12[4..]) == 2;

    public static int JsonChunkLength(ReadOnlySpan<byte> first20)
    {
        if (first20.Length < HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(first20[16..]) != JsonChunkType)
            throw new FormatException("First chunk is not JSON.");
        return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(first20[12..]));
    }

    /// <summary>Checks the object of this length; Triangles is set when it passes. A failure of read itself is rethrown: it says
    /// nothing about the file.</summary>
    public static async Task<ContentCheck> Check(long length, RangeReader read)
    {
        RangeReader guarded = async (first, last) =>
        {
            try { return await read(first, last); }
            catch (Exception e) { throw new ReadFailed(e); }
        };
        try { return ContentCheck.Pass(await Validate(length, guarded)); }
        catch (ReadFailed e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
        catch (Refused r) { return ContentCheck.Reject(r.Message, r.Detail); }
        catch (Exception e) { return ContentCheck.Reject(Malformed, $"{e.GetType().Name}: {e.Message}"); }
    }

    private sealed class ReadFailed(Exception inner) : Exception("read failed", inner);
    private sealed class Refused(string message, string detail) : Exception(message) { public string Detail { get; } = detail; }
    private static Exception Bad(string detail) => new Refused(Malformed, detail);

    private sealed record View(long Offset, long Length, long? Stride);

    private static async Task<long> Validate(long length, RangeReader read)
    {
        if (length < HeaderBytes) throw new Refused(NotGlb, $"{length} bytes");
        var head = await read(0, HeaderBytes - 1);
        if (!IsGlb(head)) throw new Refused(NotGlb, "magic or version");
        if (BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8)) != length) throw Bad("header length is not the object length");
        if (BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(16)) != JsonChunkType) throw Bad("first chunk is not JSON");
        long jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(12));
        if (jsonLength > UploadLimits.JsonChunkBytes) throw new Refused($"The glTF JSON is above {UploadLimits.JsonChunkBytes / 1024 / 1024} MiB.", $"{jsonLength} bytes");
        if (jsonLength == 0 || HeaderBytes + jsonLength > length) throw Bad("JSON chunk past the end");

        long binStart = -1, binLength = -1;
        var binHeader = HeaderBytes + jsonLength;
        if (binHeader + 8 <= length)
        {
            var bh = await read(binHeader, binHeader + 7);
            if (BinaryPrimitives.ReadUInt32LittleEndian(bh.AsSpan(4)) != BinChunkType) throw Bad("second chunk is not BIN");
            binLength = BinaryPrimitives.ReadUInt32LittleEndian(bh);
            binStart = binHeader + 8;
            if (binStart + binLength > length) throw Bad("BIN chunk past the end");
        }

        using var doc = JsonDocument.Parse(await read(HeaderBytes, HeaderBytes + jsonLength - 1));
        var root = Obj(doc.RootElement, "root");

        foreach (var ext in Array(root, "extensionsRequired"))
        {
            if (ext.ValueKind != JsonValueKind.String) throw Bad("extensionsRequired entry");
            if (!UploadLimits.RequiredExtensions.Contains(ext.GetString()!))
                throw new Refused("The model requires a glTF extension this table does not load.", $"extensionsRequired {ext.GetString()}");
        }

        var buffers = Array(root, "buffers").ToList();
        if (buffers.Count != 1 || binLength < 0) throw new Refused("A GLB embeds exactly one binary buffer.", $"{buffers.Count} buffers, BIN chunk {(binLength < 0 ? "absent" : "present")}");
        var buffer = Obj(buffers[0], "buffer");
        if (buffer.TryGetProperty("uri", out _)) throw new Refused("A GLB references no external files.", "buffer uri");
        var bufferLength = Int(buffer, "byteLength");
        if (bufferLength < 0 || bufferLength > binLength) throw Bad("buffer byteLength exceeds the BIN chunk");

        var views = new List<View>();
        foreach (var v in Array(root, "bufferViews"))
        {
            var o = Obj(v, "bufferView");
            if (Int(o, "buffer") != 0) throw Bad("bufferView buffer");
            var (offset, len) = (Int(o, "byteOffset", 0), Int(o, "byteLength"));
            if (len < 1 || !Fits(offset, len, bufferLength)) throw Bad("bufferView outside the buffer");
            long? stride = o.TryGetProperty("byteStride", out _) ? Int(o, "byteStride") : null;
            if (stride is { } s && (s < 4 || s > 252 || s % 4 != 0)) throw Bad("byteStride");
            views.Add(new View(offset, len, stride));
        }

        var counts = new List<long>();
        long accessorBytes = 0;
        foreach (var a in Array(root, "accessors"))
        {
            var o = Obj(a, "accessor");
            var componentSize = ComponentSize(Int(o, "componentType"));
            var type = Str(o, "type");
            var element = ElementSize(type, componentSize);
            var count = Int(o, "count");
            if (count < 1 || count > UploadLimits.AccessorElements) throw new Refused($"An accessor holds more than {UploadLimits.AccessorElements:N0} elements.", $"count {count}");
            accessorBytes += count * element;
            if (accessorBytes > UploadLimits.AccessorBytes) throw new Refused($"The model's accessors exceed {UploadLimits.AccessorBytes / 1024 / 1024} MiB.", $"{accessorBytes} bytes");
            if (o.TryGetProperty("bufferView", out _))
            {
                var view = ViewAt(views, Int(o, "bufferView"));
                var offset = Int(o, "byteOffset", 0);
                var stride = view.Stride ?? element;
                // stride <= 252 and count <= AccessorElements, so the span cannot overflow; Fits bounds the offset.
                if (stride < element || !Fits(offset, stride * (count - 1) + element, view.Length)) throw Bad("accessor outside its bufferView");
            }
            if (o.TryGetProperty("sparse", out var sp))
            {
                var sparse = Obj(sp, "sparse");
                var n = Int(sparse, "count");
                if (n < 1 || n > count) throw Bad("sparse count");
                var indices = Obj(sparse.GetProperty("indices"), "sparse indices");
                var indexSize = Int(indices, "componentType") switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw Bad("sparse index type") };
                Within(ViewAt(views, Int(indices, "bufferView")), Int(indices, "byteOffset", 0), n * indexSize);
                var values = Obj(sparse.GetProperty("values"), "sparse values");
                Within(ViewAt(views, Int(values, "bufferView")), Int(values, "byteOffset", 0), n * element);
            }
            counts.Add(count);
        }

        long vertices = 0, triangles = 0;
        foreach (var m in Array(root, "meshes"))
        foreach (var p in Array(Obj(m, "mesh"), "primitives", required: true))
        {
            var prim = Obj(p, "primitive");
            var attributes = Obj(prim.GetProperty("attributes"), "attributes");
            var positions = CountAt(counts, Int(attributes, "POSITION"));
            foreach (var attr in attributes.EnumerateObject())
                if (CountAt(counts, AsInt(attr.Value)) != positions) throw Bad($"attribute {attr.Name} count differs from POSITION");
            foreach (var t in Array(prim, "targets"))
            foreach (var attr in Obj(t, "target").EnumerateObject())
                if (CountAt(counts, AsInt(attr.Value)) != positions) throw Bad($"target {attr.Name} count differs from POSITION");
            var drawn = prim.TryGetProperty("indices", out _) ? CountAt(counts, Int(prim, "indices")) : positions;
            var mode = Int(prim, "mode", 4);
            if (mode is < 0 or > 6) throw Bad("mode");
            vertices += positions;
            triangles += mode switch { 4 => drawn / 3, 5 or 6 => Math.Max(0, drawn - 2), _ => 0 };
        }
        if (vertices > UploadLimits.Vertices) throw new Refused($"The model has more than {UploadLimits.Vertices:N0} vertices.", $"{vertices} vertices");
        if (triangles > UploadLimits.Triangles) throw new Refused($"The model has more than {UploadLimits.Triangles:N0} triangles.", $"{triangles} triangles");

        var images = Array(root, "images").ToList();
        if (images.Count > UploadLimits.Images) throw new Refused($"A GLB embeds at most {UploadLimits.Images} images.", $"{images.Count} images");
        foreach (var i in images)
        {
            var image = Obj(i, "image");
            if (image.TryGetProperty("uri", out _)) throw new Refused("A GLB references no external files.", "image uri");
            var view = ViewAt(views, Int(image, "bufferView"));
            var declared = Str(image, "mimeType");
            if (declared is not ("image/png" or "image/jpeg" or "image/webp")) throw Bad($"image mimeType {declared}");
            var info = await ImageHeader.Read(read, binStart + view.Offset, view.Length);
            if (info is null || info.ContentType != declared) throw Bad("embedded image header");
            if (ImageHeader.Within(info) is { } refused) throw new Refused(refused.Rejection!, refused.Detail!);
        }
        return triangles;
    }

    private static JsonElement Obj(JsonElement e, string what) => e.ValueKind == JsonValueKind.Object ? e : throw Bad($"{what} is not an object");

    private static IEnumerable<JsonElement> Array(JsonElement o, string name, bool required = false)
    {
        if (!o.TryGetProperty(name, out var v)) return required ? throw Bad($"{name} missing") : [];
        return v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : throw Bad($"{name} is not an array");
    }

    private static long AsInt(JsonElement v) => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : throw Bad("not an integer");

    private static long Int(JsonElement o, string name, long? fallback = null) =>
        o.TryGetProperty(name, out var v) ? AsInt(v) : fallback ?? throw Bad($"{name} missing");

    private static string Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw Bad($"{name} missing");

    private static View ViewAt(List<View> views, long i) => i >= 0 && i < views.Count ? views[(int)i] : throw Bad("bufferView index");

    private static long CountAt(List<long> counts, long i) => i >= 0 && i < counts.Count ? counts[(int)i] : throw Bad("accessor index");

    private static void Within(View view, long offset, long bytes)
    {
        if (!Fits(offset, bytes, view.Length)) throw Bad("sparse data outside its bufferView");
    }

    /// <summary>offset..offset+length lies inside 0..limit, without computing a sum that could overflow.</summary>
    private static bool Fits(long offset, long length, long limit) => offset >= 0 && length >= 0 && offset <= limit && length <= limit - offset;

    private static int ComponentSize(long componentType) => componentType switch
    {
        5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => throw Bad("componentType"),
    };

    /// <summary>Matrix columns start on 4-byte boundaries, so 1- and 2-byte MAT2/MAT3 elements carry padding.</summary>
    private static long ElementSize(string type, int componentSize) => (type, componentSize) switch
    {
        ("SCALAR", _) => componentSize, ("VEC2", _) => 2 * componentSize, ("VEC3", _) => 3 * componentSize, ("VEC4", _) => 4 * componentSize,
        ("MAT2", 1) => 8, ("MAT2", _) => 4 * componentSize,
        ("MAT3", 1) => 12, ("MAT3", 2) => 24, ("MAT3", _) => 9 * componentSize,
        ("MAT4", _) => 16 * componentSize,
        _ => throw Bad("accessor type"),
    };
}
