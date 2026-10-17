using System.Text;
using System.Text.Json.Nodes;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class GlbHeaderTests
{
    static RangeReader Over(byte[] b) => (first, last) => Task.FromResult(b[(int)first..(int)(last + 1)]);
    static Task<ContentCheck> Check(byte[] glb) => GlbHeader.Check(glb.Length, Over(glb));
    static byte[] With(Action<JsonObject> mutate, params (int, int?, bool)[] prims) => TestGlb.Make(prims.Length == 0 ? [(3, null, true)] : prims, null, mutate);

    static async Task Rejected(byte[] glb, string? contains = null)
    {
        var c = await Check(glb);
        Assert.NotNull(c.Rejection);
        if (contains is not null) Assert.Contains(contains, c.Rejection);
    }

    [Fact]
    public void Recognises_the_magic_and_reads_the_json_length()
    {
        var glb = TestGlb.Build((36, null, true));
        Assert.True(GlbHeader.IsGlb(glb.AsSpan(0, 12)));
        Assert.False(GlbHeader.IsGlb("solid cube\n\n"u8));
        Assert.Equal(BitConverter.ToInt32(glb, 12), GlbHeader.JsonChunkLength(glb.AsSpan(0, 20)));
    }

    [Theory]
    [InlineData(36, null, true, 12)]      // indexed triangles
    [InlineData(30, 4, false, 10)]        // unindexed triangles from POSITION count
    [InlineData(10, 5, true, 8)]          // triangle strip
    [InlineData(10, 6, true, 8)]          // triangle fan
    [InlineData(10, 1, true, 0)]          // lines count nothing
    public async Task Counts_triangles_per_mode(int count, int? mode, bool indexed, long expected)
    {
        var c = await Check(TestGlb.Build((count, mode, indexed)));
        Assert.Null(c.Rejection);
        Assert.Equal(expected, c.Triangles);
    }

    [Fact]
    public async Task Sums_over_primitives_and_only_warns_above_100k()
    {
        var c = await Check(TestGlb.Build((300_003, null, true), (3, null, true)));
        Assert.Null(c.Rejection);
        Assert.Equal(100_002, c.Triangles);
        Assert.True(c.Triangles > UploadLimits.WarnTriangles);
    }

    [Fact]
    public async Task The_starter_meshes_pass() =>
        Assert.All(await Task.WhenAll(StarterMeshes.All.Select(m => Check(m.Glb))), c => Assert.Null(c.Rejection));

    [Fact]
    public async Task Hard_limits_on_vertices_and_triangles()
    {
        Assert.Null((await Check(TestGlb.Build((500_000, null, false)))).Rejection);
        await Rejected(TestGlb.Build((250_001, null, false), (250_001, null, false)), "500,000 vertices");
        Assert.Null((await Check(TestGlb.Build((3_000_000, null, true)))).Rejection);
        await Rejected(TestGlb.Build((1_500_003, null, true), (1_500_003, null, true)), "1,000,000 triangles");
    }

    [Fact]
    public async Task A_json_chunk_above_1_mib_is_refused()
    {
        var glb = With(r => r["extras"] = new string('x', 1 << 20));
        await Rejected(glb, "JSON");
    }

    [Fact]
    public async Task Exactly_one_embedded_buffer_and_no_uri_anywhere()
    {
        await Rejected(With(r => r["buffers"]!.AsArray().Add(new JsonObject { ["byteLength"] = 4 })));
        await Rejected(With(r => r["buffers"] = new JsonArray()));
        await Rejected(With(r => r["buffers"]![0]!["uri"] = "data:application/octet-stream;base64,AAAA"));
        await Rejected(TestGlb.Make([(3, null, true)], [(TestImages.Png(4, 4), "image/png")], r => r["images"]![0]!["uri"] = "http://example.com/a.png"));
        await Rejected(TestGlb.Make([(3, null, true)], [(TestImages.Png(4, 4), "image/png")], r => { r["images"]![0]!.AsObject().Remove("bufferView"); r["images"]![0]!["uri"] = "a.png"; }));
    }

    [Fact]
    public async Task Images_are_embedded_at_most_eight_each_at_most_4096_square()
    {
        Assert.Null((await Check(TestGlb.Make([(3, null, true)], [(TestImages.Png(4096, 4096), "image/png"), (TestImages.Jpeg(4096, 16), "image/jpeg"), (TestImages.WebP(16, 4096), "image/webp")]))).Rejection);
        await Rejected(TestGlb.Make([(3, null, true)], Enumerable.Repeat((TestImages.Png(4, 4), "image/png"), 9).ToList()), "8 images");
        await Rejected(TestGlb.Make([(3, null, true)], [(TestImages.Png(4097, 4), "image/png")]), "4096");
        await Rejected(TestGlb.Make([(3, null, true)], [(TestImages.Jpeg(4, 4097), "image/jpeg")]), "4096");
        await Rejected(TestGlb.Make([(3, null, true)], [(TestImages.WebP(4097, 4, TestImages.WebPForm.Lossless), "image/webp")]), "4096");
        await Rejected(TestGlb.Make([(3, null, true)], [(TestImages.Png(4, 4), "image/jpeg")]));             // bytes are not the declared type
        await Rejected(TestGlb.Make([(3, null, true)], [("not an image"u8.ToArray(), "image/png")]));
    }

    [Fact]
    public async Task Accessors_stay_inside_their_buffer_view_and_views_inside_the_bin_chunk()
    {
        await Rejected(With(r => r["accessors"]![0]!["byteOffset"] = 4));
        await Rejected(With(r => r["accessors"]![0]!["count"] = 4));                 // a 4th VEC3 runs past the 36-byte view
        await Rejected(With(r => r["bufferViews"]![0]!["byteLength"] = 1_000));
        await Rejected(With(r => r["bufferViews"]![1]!["byteOffset"] = 1 << 30));
        await Rejected(With(r => r["accessors"]![0]!["bufferView"] = 99));
        await Rejected(With(r => r["meshes"]![0]!["primitives"]![0]!["indices"] = 99));
        await Rejected(With(r => r["accessors"]![0]!["componentType"] = 1));
    }

    [Fact]
    public async Task Offsets_that_would_overflow_are_refused()
    {
        await Rejected(With(r => r["accessors"]![0]!["byteOffset"] = long.MaxValue - 20));
        await Rejected(With(r => r["bufferViews"]![0]!["byteOffset"] = long.MaxValue - 10));
        await Rejected(With(r => r["bufferViews"]![0]!["byteLength"] = long.MaxValue));
        await Rejected(With(r => r["buffers"]![0]!["byteLength"] = long.MaxValue));
        await Rejected(With(r => r["accessors"]![1]!["sparse"] = new JsonObject
        {
            ["count"] = 1,
            ["indices"] = new JsonObject { ["bufferView"] = 1, ["byteOffset"] = long.MaxValue - 1, ["componentType"] = 5121 },
            ["values"] = new JsonObject { ["bufferView"] = 1, ["byteOffset"] = 0 },
        }));
    }

    [Fact]
    public async Task Every_accessor_is_capped_even_without_a_buffer_view()
    {
        // An accessor with no bufferView is zero-filled by the loader: count is memory without a byte in the file.
        await Rejected(With(r => r["accessors"]!.AsArray().Add(new JsonObject { ["componentType"] = 5126, ["count"] = 100_000_000, ["type"] = "MAT4" })));
        await Rejected(With(r =>
        {
            for (var i = 0; i < 40; i++) r["accessors"]!.AsArray().Add(new JsonObject { ["componentType"] = 5126, ["count"] = 2_900_000, ["type"] = "VEC4" });
        }));
    }

    [Fact]
    public async Task Attributes_and_morph_targets_match_the_position_count()
    {
        await Rejected(With(r =>
        {
            r["accessors"]!.AsArray().Add(new JsonObject { ["componentType"] = 5126, ["count"] = 4, ["type"] = "VEC3" });
            r["meshes"]![0]!["primitives"]![0]!["attributes"]!["NORMAL"] = 2;
        }));
        await Rejected(With(r =>
        {
            r["accessors"]!.AsArray().Add(new JsonObject { ["componentType"] = 5126, ["count"] = 4, ["type"] = "VEC3" });
            r["meshes"]![0]!["primitives"]![0]!["targets"] = new JsonArray(new JsonObject { ["POSITION"] = 2 });
        }));
        Assert.Null((await Check(With(r =>
        {
            r["accessors"]!.AsArray().Add(new JsonObject { ["componentType"] = 5126, ["count"] = 3, ["type"] = "VEC3" });
            r["meshes"]![0]!["primitives"]![0]!["targets"] = new JsonArray(new JsonObject { ["POSITION"] = 2 });
        }))).Rejection);
    }

    [Fact]
    public async Task Extensions_required_are_an_allowlist()
    {
        Assert.Null((await Check(With(r => r["extensionsRequired"] = new JsonArray("KHR_mesh_quantization")))).Rejection);
        await Rejected(With(r => r["extensionsRequired"] = new JsonArray("EXT_meshopt_compression")), "extension");
        await Rejected(With(r => r["extensionsRequired"] = new JsonArray("KHR_draco_mesh_compression")), "extension");
        await Rejected(With(r => r["extensionsRequired"] = new JsonObject()));
    }

    [Fact]
    public async Task Wrong_json_shapes_are_refused_not_skipped()
    {
        await Rejected(With(r => r["meshes"] = new JsonObject()));
        await Rejected(With(r => r["meshes"]![0]!["primitives"] = 3));
        await Rejected(With(r => r["meshes"]![0]!["primitives"]![0]!["attributes"] = new JsonArray()));
        await Rejected(With(r => r["accessors"]![0] = 7));
        await Rejected(With(r => r["images"] = "png"));
        await Rejected(With(r => r["accessors"]![0]!["count"] = 1.5));
        await Rejected(With(r => r["meshes"]![0]!["primitives"]![0]!["attributes"]!.AsObject().Remove("POSITION")));
    }

    [Fact]
    public async Task Parse_failures_answer_one_fixed_message_and_keep_the_detail_apart()
    {
        var bad = TestGlb.BuildRaw(0x4E4F534A, "{ \"secret\": <script>"u8.ToArray());
        var c = await Check(bad);
        Assert.Equal(GlbHeader.Malformed, c.Rejection);
        Assert.NotNull(c.Detail);
        Assert.Equal(GlbHeader.Malformed, (await Check(TestGlb.BuildRaw(0x004E4942, "x"u8.ToArray()))).Rejection);
        Assert.Equal(GlbHeader.NotGlb, (await Check("solid cube\nendsolid cube\n"u8.ToArray())).Rejection);
        var truncated = TestGlb.Build((3, null, true));
        Assert.NotNull((await GlbHeader.Check(truncated.Length - 4, Over(truncated[..^4]))).Rejection);
    }

    [Fact]
    public async Task A_read_failure_is_not_a_verdict_on_the_file()
    {
        var glb = TestGlb.Build((3, null, true));
        await Assert.ThrowsAsync<IOException>(() => GlbHeader.Check(glb.Length, (_, _) => throw new IOException("network")));
    }
}

public class ImageHeaderTests
{
    static RangeReader Over(byte[] b) => (first, last) => Task.FromResult(b[(int)first..(int)Math.Min(last + 1, b.Length)]);

    [Theory]
    [InlineData("png")] [InlineData("jpeg")] [InlineData("webp-lossy")] [InlineData("webp-lossless")] [InlineData("webp-extended")]
    public async Task Reads_the_type_and_dimensions(string form)
    {
        var (bytes, type) = form switch
        {
            "png" => (TestImages.Png(640, 480), "image/png"),
            "jpeg" => (TestImages.Jpeg(640, 480), "image/jpeg"),
            "webp-lossy" => (TestImages.WebP(640, 480, TestImages.WebPForm.Lossy), "image/webp"),
            "webp-lossless" => (TestImages.WebP(640, 480, TestImages.WebPForm.Lossless), "image/webp"),
            _ => (TestImages.WebP(640, 480), "image/webp"),
        };
        Assert.Equal(new ImageInfo(type, 640, 480), await ImageHeader.Read(Over(bytes), 0, bytes.Length));
    }

    [Fact]
    public async Task One_bounded_read_finds_a_jpeg_frame_header_behind_large_segments()
    {
        var jpeg = TestImages.Jpeg(300, 200, appSegments: 2);
        var reads = 0;
        RangeReader counted = (first, last) => { reads++; return Over(jpeg)(first, last); };
        Assert.Equal(new ImageInfo("image/jpeg", 300, 200), await ImageHeader.Read(counted, 0, jpeg.Length));
        Assert.Equal(1, reads);
        var deep = TestImages.Jpeg(300, 200, appSegments: 17);   // the frame header sits past the first MiB
        Assert.Null(await ImageHeader.Read(Over(deep), 0, deep.Length));
    }

    [Fact]
    public async Task Texture_check_matches_the_declared_type_and_caps_the_side()
    {
        async Task<string?> Verdict(byte[] b, string declared) => (await ImageHeader.CheckTexture(declared, b.Length, Over(b))).Rejection;
        Assert.Null(await Verdict(TestImages.Png(4096, 4096), "image/png"));
        Assert.NotNull(await Verdict(TestImages.Png(16, 16), "image/jpeg"));
        Assert.NotNull(await Verdict(TestImages.Png(4097, 16), "image/png"));
        Assert.NotNull(await Verdict(TestImages.Png(0, 16), "image/png"));
        Assert.NotNull(await Verdict([0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4], "image/png"));
        Assert.NotNull(await Verdict(Encoding.ASCII.GetBytes("GIF89a......"), "image/png"));
    }
}
