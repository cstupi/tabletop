using System.Buffers.Binary;
using System.Text.Json;

namespace Tabletop.Client.Core;

/// <summary>
/// Structural checks on a downloaded GLB before Godot parses it: the container, the JSON chunk, every accessor and buffer view
/// inside the BIN chunk, no external URIs, and caps on size, vertices, triangles, images and image dimensions.
/// <see cref="Check(ReadOnlySpan{byte})"/> returns null for a file that passes, else the reason it was refused.
/// </summary>
public static class GlbPreflight
{
    public const long MaxBytes = 64L << 20;
    public const int MaxJsonBytes = 1 << 20;
    public const int MaxImages = 8;
    public const int MaxImageSide = 4096;
    public const long MaxVertices = 500_000;
    public const long MaxTriangles = 1_000_000;
    /// <summary>Per accessor: an index list of <see cref="MaxTriangles"/> triangles.</summary>
    public const long MaxAccessorCount = 3 * MaxTriangles;
    /// <summary>Per accessor and summed over all of them, as count × element size: what Godot may allocate, buffer view or not.</summary>
    public const long MaxAccessorBytes = MaxBytes, MaxTotalAccessorBytes = 2 * MaxBytes;

    /// <summary>No EXT_meshopt_compression: its buffer views carry compressed data this check cannot bound, and the web converter
    /// (web/src/lib/mesh/glb.ts) writes no extensions.</summary>
    public static readonly IReadOnlySet<string> AllowedRequiredExtensions = new HashSet<string>
    {
        "KHR_mesh_quantization", "KHR_texture_transform", "KHR_materials_emissive_strength",
    };

    const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;

    sealed class Refused(string reason) : Exception(reason);

    /// <summary>The file's length is checked before any of it is read.</summary>
    public static string? CheckFile(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaxBytes) return $"file is {length} bytes, over {MaxBytes}";
        return Check(File.ReadAllBytes(path));
    }

    public static string? Check(ReadOnlySpan<byte> glb)
    {
        try { Validate(glb); return null; }
        catch (Refused r) { return r.Message; }
        catch (JsonException e) { return $"JSON chunk does not parse: {e.Message}"; }
        catch (Exception e) { return $"malformed: {e.GetType().Name}: {e.Message}"; }   // never accept on an unexpected shape
    }

    static void Validate(ReadOnlySpan<byte> glb)
    {
        if (glb.Length > MaxBytes) throw new Refused($"file is {glb.Length} bytes, over {MaxBytes}");
        if (glb.Length < 20) throw new Refused("shorter than a GLB header and chunk header");
        if (U32(glb, 0) != Magic) throw new Refused("not a GLB (magic)");
        if (U32(glb, 4) != 2) throw new Refused($"GLB version {U32(glb, 4)}, expected 2");
        if (U32(glb, 8) != (uint)glb.Length) throw new Refused($"header length {U32(glb, 8)} does not match the file's {glb.Length}");

        long jsonLength = U32(glb, 12);
        if (U32(glb, 16) != JsonChunk) throw new Refused("first chunk is not JSON");
        if (jsonLength > MaxJsonBytes) throw new Refused($"JSON chunk is {jsonLength} bytes, over {MaxJsonBytes}");
        if (20 + jsonLength > glb.Length) throw new Refused("JSON chunk runs past the end of the file");
        var json = glb.Slice(20, (int)jsonLength);

        var binStart = 20 + (int)jsonLength;
        var bin = ReadOnlySpan<byte>.Empty;
        if (binStart < glb.Length)
        {
            if (binStart + 8 > glb.Length) throw new Refused("truncated chunk header after JSON");
            long binLength = U32(glb, binStart);
            if (U32(glb, binStart + 4) != BinChunk) throw new Refused("second chunk is not BIN");
            if (binStart + 8 + binLength != glb.Length) throw new Refused("BIN chunk does not end the file");
            bin = glb.Slice(binStart + 8, (int)binLength);
        }

        using var doc = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new Refused("JSON root is not an object");

        if (Has(root, "extensionsRequired", out var required))
            foreach (var e in ArrayItems(required, "extensionsRequired", objects: false))
                if (e.ValueKind != JsonValueKind.String || !AllowedRequiredExtensions.Contains(e.GetString()!)) throw new Refused($"required extension {e} is not allowed");

        var buffers = Items(root, "buffers");
        if (buffers.Count != 1) throw new Refused($"{buffers.Count} buffers, expected exactly one");
        if (Has(buffers[0], "uri", out _)) throw new Refused("buffer has a uri");
        var bufferLength = Int(buffers[0], "byteLength");
        if (bufferLength > bin.Length) throw new Refused("buffer is longer than the BIN chunk");

        var views = Items(root, "bufferViews");
        foreach (var v in views)
        {
            if (Int(v, "buffer") != 0) throw new Refused("buffer view names a buffer that does not exist");
            if (!Within(Int(v, "byteOffset", 0), Int(v, "byteLength"), bufferLength)) throw new Refused("buffer view runs past its buffer");
            if (Has(v, "byteStride", out _) && Int(v, "byteStride") is < 4 or > 252) throw new Refused("buffer view stride out of range");
        }

        var accessors = Items(root, "accessors");
        long accessorBytes = 0;
        foreach (var a in accessors) accessorBytes += CheckAccessor(a, views);
        if (accessorBytes > MaxTotalAccessorBytes) throw new Refused($"{accessorBytes} accessor bytes in all, over {MaxTotalAccessorBytes}");

        var images = Items(root, "images");
        if (images.Count > MaxImages) throw new Refused($"{images.Count} images, over {MaxImages}");
        foreach (var image in images)
        {
            if (Has(image, "uri", out _)) throw new Refused("image has a uri");
            var view = Index(image, "bufferView", views.Count);
            var v = views[view];
            var bytes = bin.Slice((int)Int(v, "byteOffset", 0), (int)Int(v, "byteLength"));
            var (w, h) = ImageHeader.Size(bytes) ?? throw new Refused("embedded image is not a readable PNG or JPEG");
            if (w > MaxImageSide || h > MaxImageSide) throw new Refused($"embedded image is {w}x{h}, over {MaxImageSide}");
        }

        long vertices = 0, triangles = 0;
        foreach (var mesh in Items(root, "meshes"))
            foreach (var p in Items(mesh, "primitives"))
            {
                if (!Has(p, "attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object) throw new Refused("primitive has no attributes");
                if (!Has(attributes, "POSITION", out _)) throw new Refused("primitive has no POSITION");
                var positions = Int(accessors[Index(attributes, "POSITION", accessors.Count)], "count");
                SameCounts(attributes, "attribute", positions, accessors);
                if (Has(p, "targets", out var targets))
                    foreach (var target in ArrayItems(targets, "targets")) SameCounts(target, "morph target attribute", positions, accessors);
                var drawn = Has(p, "indices", out _) ? Int(accessors[Index(p, "indices", accessors.Count)], "count") : positions;
                vertices += positions;
                triangles += Int(p, "mode", 4) switch { 4 => drawn / 3, 5 or 6 => Math.Max(0, drawn - 2), _ => 0 };
            }
        if (vertices > MaxVertices) throw new Refused($"{vertices} vertices, over {MaxVertices}");
        if (triangles > MaxTriangles) throw new Refused($"{triangles} triangles, over {MaxTriangles}");
    }

    /// <summary>Every accessor an object names (attributes, or one morph target) has the primitive's POSITION count.</summary>
    static void SameCounts(JsonElement named, string what, long positions, List<JsonElement> accessors)
    {
        if (named.ValueKind != JsonValueKind.Object) throw new Refused($"{what}s are not an object");
        foreach (var attribute in named.EnumerateObject())
        {
            var count = Int(accessors[Index(named, attribute.Name, accessors.Count)], "count");
            if (count != positions) throw new Refused($"{what} {attribute.Name} has {count} elements, POSITION has {positions}");
        }
    }

    /// <summary>Checks one accessor's ranges and caps; returns its size in bytes (count × element size).</summary>
    static long CheckAccessor(JsonElement a, List<JsonElement> views)
    {
        var count = Int(a, "count");
        if (count < 1) throw new Refused("accessor count below one");
        if (count > MaxAccessorCount) throw new Refused($"accessor count {count}, over {MaxAccessorCount}");
        var component = ComponentSize(Int(a, "componentType"));
        var element = component * Components(Has(a, "type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "");
        if (count * element > MaxAccessorBytes) throw new Refused($"accessor is {count * element} bytes, over {MaxAccessorBytes}");
        if (Has(a, "bufferView", out _))
        {
            var view = views[Index(a, "bufferView", views.Count)];
            var stride = Has(view, "byteStride", out _) ? Int(view, "byteStride") : element;
            if (!Within(Int(a, "byteOffset", 0), checked((count - 1) * stride + element), Int(view, "byteLength"))) throw new Refused("accessor runs past its buffer view");
        }
        if (Has(a, "sparse", out var sparse))
        {
            var n = Int(sparse, "count");
            if (n < 1 || n > count) throw new Refused("sparse count out of range");
            if (!Has(sparse, "indices", out var indices) || !Has(sparse, "values", out var values)) throw new Refused("sparse accessor without indices or values");
            if (!Within(Int(indices, "byteOffset", 0), checked(n * ComponentSize(Int(indices, "componentType"))), Int(views[Index(indices, "bufferView", views.Count)], "byteLength"))) throw new Refused("sparse indices run past their buffer view");
            if (!Within(Int(values, "byteOffset", 0), checked(n * element), Int(views[Index(values, "bufferView", views.Count)], "byteLength"))) throw new Refused("sparse values run past their buffer view");
        }
        return count * element;
    }

    /// <summary>[offset, offset + length) lies inside [0, limit), compared without forming offset + length.</summary>
    static bool Within(long offset, long length, long limit) => offset >= 0 && length >= 0 && offset <= limit && length <= limit - offset;

    static long ComponentSize(long type) => type switch
    {
        5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4,
        _ => throw new Refused($"component type {type} is not a glTF component type"),
    };

    static long Components(string type) => type switch
    {
        "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT2" => 4, "MAT3" => 9, "MAT4" => 16,
        _ => throw new Refused($"accessor type '{type}' is not a glTF type"),
    };

    static List<JsonElement> Items(JsonElement parent, string name) =>
        Has(parent, name, out var array) ? ArrayItems(array, name) : [];

    static List<JsonElement> ArrayItems(JsonElement array, string name, bool objects = true)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new Refused($"{name} is not an array");
        var items = array.EnumerateArray().ToList();
        if (objects && items.Any(i => i.ValueKind != JsonValueKind.Object)) throw new Refused($"{name} holds a non-object");
        return items;
    }

    /// <summary>TryGetProperty that refuses, instead of throwing, when the element is not an object.</summary>
    static bool Has(JsonElement e, string name, out JsonElement value)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new Refused($"expected an object holding {name}, found {e.ValueKind}");
        return e.TryGetProperty(name, out value);
    }

    /// <summary>A non-negative integer property; required unless a fallback is given.</summary>
    static long Int(JsonElement e, string name, long? fallback = null)
    {
        if (!Has(e, name, out var v)) return fallback ?? throw new Refused($"missing {name}");
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out var n) || n < 0 || n > int.MaxValue) throw new Refused($"{name} is not a non-negative integer");
        return n;
    }

    static int Index(JsonElement e, string name, int count)
    {
        var i = Int(e, name);
        if (i >= count) throw new Refused($"{name} {i} names nothing");
        return (int)i;
    }

    static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b[at..]);

}

/// <summary>Width and height from a PNG or JPEG header, without decoding. Null for anything else or a truncated header.</summary>
public static class ImageHeader
{
    public static (int Width, int Height)? Size(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 24 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) && b[12..16].SequenceEqual("IHDR"u8))
        {
            var w = BinaryPrimitives.ReadUInt32BigEndian(b[16..]); var h = BinaryPrimitives.ReadUInt32BigEndian(b[20..]);
            return w > int.MaxValue || h > int.MaxValue ? null : ((int)w, (int)h);
        }
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            var i = 2;
            while (i + 4 <= b.Length)
            {
                if (b[i] != 0xFF) return null;
                var marker = b[i + 1];
                if (marker == 0xFF) { i++; continue; }                                   // fill byte
                if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }       // no length
                var length = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..]);
                if (length < 2) return null;
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    if (i + 9 > b.Length) return null;
                    return (BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]));
                }
                if (marker == 0xDA) return null;                                         // scan before any frame header
                i += 2 + length;
            }
        }
        return null;
    }
}
