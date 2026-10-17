using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class GlbPreflightTests
{
    /// <summary>One triangle: three float positions (36 bytes) and three uint16 indices (6 bytes, padded to 8).</summary>
    static (JsonObject Json, byte[] Bin) Triangle()
    {
        var json = JsonNode.Parse("""
        {
          "asset": { "version": "2.0" },
          "buffers": [ { "byteLength": 44 } ],
          "bufferViews": [ { "buffer": 0, "byteOffset": 0, "byteLength": 36 }, { "buffer": 0, "byteOffset": 36, "byteLength": 6 } ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
          ],
          "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1 } ] } ]
        }
        """)!.AsObject();
        return (json, new byte[44]);
    }

    static byte[] Glb(JsonNode json, byte[]? bin, uint version = 2, uint jsonType = 0x4E4F534A)
    {
        var text = Encoding.UTF8.GetBytes(json.ToJsonString());
        var jsonPadded = Pad(text, 0x20);
        var binPadded = bin is null ? null : Pad(bin, 0);
        var total = 12 + 8 + jsonPadded.Length + (binPadded is null ? 0 : 8 + binPadded.Length);
        var b = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(b, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), (uint)jsonPadded.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), jsonType);
        jsonPadded.CopyTo(b, 20);
        if (binPadded is not null)
        {
            var at = 20 + jsonPadded.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), (uint)binPadded.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at + 4), 0x004E4942);
            binPadded.CopyTo(b, at + 8);
        }
        return b;
    }

    static byte[] Pad(byte[] bytes, byte with)
    {
        var padded = new byte[(bytes.Length + 3) / 4 * 4];
        Array.Fill(padded, with);
        bytes.CopyTo(padded, 0);
        return padded;
    }

    static byte[] Png(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), 13);
        "IHDR"u8.CopyTo(b.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), (uint)h);
        return b;
    }

    static byte[] Jpeg(int w, int h) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,                                                          // APP0, 2 bytes of payload
        0xFF, 0xC0, 0x00, 0x0B, 0x08, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 0x01, 0x01, 0x11, 0x00,
        0xFF, 0xD9,
    ];

    /// <summary>The triangle plus the given images, each in its own buffer view after the geometry.</summary>
    static byte[] WithImages(params byte[][] images)
    {
        var (json, bin) = Triangle();
        var all = new List<byte>(bin);
        var views = json["bufferViews"]!.AsArray();
        var entries = new JsonArray();
        foreach (var image in images)
        {
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = all.Count, ["byteLength"] = image.Length });
            entries.Add(new JsonObject { ["bufferView"] = views.Count - 1, ["mimeType"] = "image/png" });
            all.AddRange(image);
            while (all.Count % 4 != 0) all.Add(0);
        }
        json["images"] = entries;
        json["buffers"]![0]!["byteLength"] = all.Count;
        return Glb(json, all.ToArray());
    }

    static void Passes(byte[] glb) => Assert.Null(GlbPreflight.Check(glb));
    static void Refused(byte[] glb, string fragment)
    {
        var reason = GlbPreflight.Check(glb);
        Assert.NotNull(reason);
        Assert.Contains(fragment, reason);
    }

    [Fact]
    public void A_well_formed_triangle_passes() { var (j, b) = Triangle(); Passes(Glb(j, b)); }

    [Fact]
    public void Wrong_magic_is_refused()
    {
        var (j, b) = Triangle(); var glb = Glb(j, b); glb[0] = (byte)'x';
        Refused(glb, "magic");
    }

    [Fact]
    public void Version_one_is_refused() { var (j, b) = Triangle(); Refused(Glb(j, b, version: 1), "version"); }

    [Fact]
    public void A_header_length_that_disagrees_with_the_file_is_refused()
    {
        var (j, b) = Triangle(); var glb = Glb(j, b);
        Refused([.. glb, 0, 0, 0, 0], "header length");
    }

    [Fact]
    public void A_file_over_64_MB_is_refused_before_it_is_read()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var f = File.OpenWrite(path)) f.SetLength(GlbPreflight.MaxBytes + 1);
            Assert.Contains("over", GlbPreflight.CheckFile(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_first_chunk_that_is_not_JSON_is_refused() { var (j, b) = Triangle(); Refused(Glb(j, b, jsonType: 0x004E4942), "not JSON"); }

    [Fact]
    public void A_JSON_chunk_over_1_MB_is_refused()
    {
        var (j, b) = Triangle();
        j["extras"] = new string('x', GlbPreflight.MaxJsonBytes);
        Refused(Glb(j, b), "JSON chunk is");
    }

    [Fact]
    public void JSON_that_does_not_parse_is_refused()
    {
        var (j, b) = Triangle(); var glb = Glb(j, b); glb[20] = (byte)'!';
        Refused(glb, "does not parse");
    }

    [Fact]
    public void Two_buffers_are_refused()
    {
        var (j, b) = Triangle();
        j["buffers"]!.AsArray().Add(new JsonObject { ["byteLength"] = 4 });
        Refused(Glb(j, b), "exactly one");
    }

    [Fact]
    public void A_buffer_with_a_uri_is_refused()
    {
        var (j, b) = Triangle();
        j["buffers"]![0]!["uri"] = "file:///etc/passwd";
        Refused(Glb(j, b), "buffer has a uri");
    }

    [Fact]
    public void An_image_with_a_uri_is_refused()
    {
        var (j, b) = Triangle();
        j["images"] = new JsonArray(new JsonObject { ["uri"] = "http://example.invalid/x.png" });
        Refused(Glb(j, b), "image has a uri");
    }

    [Fact]
    public void Embedded_images_within_limits_pass() => Passes(WithImages(Png(64, 64), Jpeg(4096, 4096)));

    [Fact]
    public void More_than_eight_images_are_refused() => Refused(WithImages(Enumerable.Repeat(Png(4, 4), 9).ToArray()), "9 images");

    [Fact]
    public void A_PNG_wider_than_4096_is_refused() => Refused(WithImages(Png(4097, 1)), "4097x1");

    [Fact]
    public void A_JPEG_taller_than_4096_is_refused() => Refused(WithImages(Jpeg(10, 5000)), "10x5000");

    [Fact]
    public void An_image_that_is_neither_PNG_nor_JPEG_is_refused() => Refused(WithImages("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()), "PNG or JPEG");

    [Fact]
    public void An_accessor_past_its_buffer_view_is_refused()
    {
        var (j, b) = Triangle();
        j["accessors"]![0]!["count"] = 4;
        Refused(Glb(j, b), "accessor runs past");
    }

    [Fact]
    public void A_strided_accessor_past_its_buffer_view_is_refused()
    {
        var (j, b) = Triangle();
        j["bufferViews"]![0]!["byteStride"] = 16;   // 2 * 16 + 12 = 44 > 36
        Refused(Glb(j, b), "accessor runs past");
    }

    [Fact]
    public void A_buffer_view_past_the_BIN_chunk_is_refused()
    {
        var (j, b) = Triangle();
        j["bufferViews"]![1]!["byteLength"] = 100;
        Refused(Glb(j, b), "buffer view runs past");
    }

    [Fact]
    public void A_buffer_longer_than_the_BIN_chunk_is_refused()
    {
        var (j, b) = Triangle();
        j["buffers"]![0]!["byteLength"] = 1000;
        Refused(Glb(j, b), "longer than the BIN chunk");
    }

    [Fact]
    public void More_than_500k_vertices_are_refused()
    {
        var (j, b) = Triangle();
        j["accessors"]!.AsArray().Add(new JsonObject { ["componentType"] = 5126, ["count"] = 500_001, ["type"] = "VEC3" });   // no buffer view: zeros
        j["meshes"]![0]!["primitives"]![0]!["attributes"]!["POSITION"] = 2;
        j["meshes"]![0]!["primitives"]![0]!.AsObject().Remove("indices");
        Refused(Glb(j, b), "vertices");
    }

    [Fact]
    public void More_than_a_million_triangles_are_refused()
    {
        var (j, b) = Triangle();
        j["accessors"]!.AsArray().Add(new JsonObject { ["componentType"] = 5125, ["count"] = 3_000_003, ["type"] = "SCALAR" });
        j["meshes"]![0]!["primitives"]![0]!["indices"] = 2;
        Refused(Glb(j, b), "triangles");
    }

    [Fact]
    public void An_allowed_required_extension_passes()
    {
        var (j, b) = Triangle();
        j["extensionsRequired"] = new JsonArray("KHR_mesh_quantization");
        Passes(Glb(j, b));
    }

    [Fact]
    public void An_unlisted_required_extension_is_refused()
    {
        var (j, b) = Triangle();
        j["extensionsRequired"] = new JsonArray("KHR_draco_mesh_compression");
        Refused(Glb(j, b), "KHR_draco_mesh_compression");
    }

    [Fact]
    public void An_index_that_names_no_accessor_is_refused()
    {
        var (j, b) = Triangle();
        j["meshes"]![0]!["primitives"]![0]!["indices"] = 9;
        Refused(Glb(j, b), "names nothing");
    }

    [Fact]
    public void A_negative_offset_is_refused()
    {
        var (j, b) = Triangle();
        j["bufferViews"]![0]!["byteOffset"] = -4;
        Refused(Glb(j, b), "non-negative");
    }

    /// <summary>Real GLBs from scripts/terrain-meshes (gltf-transform, as the web converter writes) pass.</summary>
    [Fact]
    public void The_built_in_terrain_meshes_pass()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client/content/builtin/terrain"));
        var files = Directory.GetFiles(dir, "*.glb");
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.Null(GlbPreflight.CheckFile(f)));
    }
}
