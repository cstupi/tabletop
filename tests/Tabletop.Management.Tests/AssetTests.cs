using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public class AssetTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public AssetTests(ManagementFactory f) => _f = f;

    static string Sha(byte[] b) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(b));
    static CreateAssetRequest Mini(byte[] glb, string name = "Marine") =>
        new(name, AssetKind.Miniature, Sha(glb), glb.Length, "model/gltf-binary", 1.0, new Orientation(0, 0, 0), Base.Circle(1.26));

    static async Task<string?> Detail(HttpResponseMessage r) => System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;

    async Task Put(Uri url, byte[] bytes, string contentType = "model/gltf-binary")
    {
        using var http = _f.Blobs.Client();
        Assert.True((await http.PutAsync(url, new ByteArrayContent(bytes) { Headers = { ContentType = new(contentType) } })).IsSuccessStatusCode);
    }

    /// <summary>Neither the staging key, the held copy nor the content key holds bytes.</summary>
    void AssertNothingStored(Guid owner, AssetDto asset) =>
        Assert.DoesNotContain(_f.Blobs.Keys, k => k.StartsWith(IBlobStore.StagingKey(owner, asset.Id)) || k == IBlobStore.Key(owner, asset.Hash));

    async Task<AssetDto> Upload(HttpClient c, byte[] glb, string name = "Marine")
    {
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, name));
        if (created!.UploadUrl is not null)
            using (var http = _f.Blobs.Client()) Assert.True((await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } })).IsSuccessStatusCode);
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { });
        return confirmed!.Asset;
    }

    [Fact]
    public async Task Create_upload_confirm_download()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((36, null, true));
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb));
        Assert.NotNull(created!.UploadUrl); Assert.False(created.Asset.Uploaded);
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync($"/v1/assets/{created.Asset.Id}/download")).StatusCode);
        using (var http = _f.Blobs.Client()) await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { });
        Assert.Equal(12, confirmed!.Triangles); Assert.Null(confirmed.Warning); Assert.True(confirmed.Asset.Uploaded);
        var dl = await c.GetJson<DownloadResponse>($"/v1/assets/{created.Asset.Id}/download");
        using var http2 = _f.Blobs.Client();
        Assert.Equal(glb, await http2.GetByteArrayAsync(dl!.Url));
    }

    [Fact]
    public async Task Same_bytes_twice_short_circuits_the_upload_and_deleting_the_last_reference_removes_the_blob()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var glb = TestGlb.Build((3, null, true));
        var first = await Upload(c, glb, "A");
        var second = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "B"));
        Assert.Null(second!.UploadUrl); Assert.True(second.Asset.Uploaded);
        var store = _f.Services.GetRequiredService<IBlobStore>();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/assets/{first.Id}")).StatusCode);
        Assert.NotNull(await store.Size(IBlobStore.Key(owner, first.Hash)));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/assets/{second.Asset.Id}")).StatusCode);
        Assert.Null(await store.Size(IBlobStore.Key(owner, first.Hash)));
    }

    [Fact]
    public async Task Stl_is_rejected_and_a_dense_glb_only_warns()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var stl = "solid cube\nendsolid cube\n"u8.ToArray();
        var req = Mini(stl) with { Name = "not a glb" };
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", req);
        using (var http = _f.Blobs.Client()) await http.PutAsync(created!.UploadUrl, new ByteArrayContent(stl) { Headers = { ContentType = new("model/gltf-binary") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsync($"/v1/assets/{created.Asset.Id}/confirm", null)).StatusCode);
        var dense = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(TestGlb.Build((300_003, null, true)), "dense"));
        using (var http = _f.Blobs.Client()) await http.PutAsync(dense!.UploadUrl, new ByteArrayContent(TestGlb.Build((300_003, null, true))) { Headers = { ContentType = new("model/gltf-binary") } });
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{dense.Asset.Id}/confirm", new { });
        Assert.Equal(100_001, confirmed!.Triangles); Assert.Contains("100,000", confirmed.Warning); Assert.True(confirmed.Asset.Uploaded);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsJsonAsync("/v1/assets", Mini(stl) with { ContentType = "model/stl" }, ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task A_non_json_first_chunk_is_rejected_and_the_object_is_deleted()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var glb = TestGlb.BuildRaw(0x004E4942, "not json"u8.ToArray());   // "BIN\0" where "JSON" belongs
        var req = Mini(glb) with { Name = "not json chunk" };
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", req);
        using (var http = _f.Blobs.Client()) await http.PutAsync(created!.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsync($"/v1/assets/{created.Asset.Id}/confirm", null)).StatusCode);
        AssertNothingStored(owner, created.Asset);
    }

    [Fact]
    public async Task A_malformed_json_chunk_is_rejected_and_the_object_is_deleted()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var glb = TestGlb.BuildRaw(0x4E4F534A, "{ not valid json"u8.ToArray());
        var req = Mini(glb) with { Name = "malformed json" };
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", req);
        using (var http = _f.Blobs.Client()) await http.PutAsync(created!.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        var response = await c.PostAsync($"/v1/assets/{created.Asset.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(GlbHeader.Malformed, await Detail(response));   // fixed text: the parser's message never reaches the caller
        AssertNothingStored(owner, created.Asset);
    }

    [Fact]
    public async Task A_size_mismatch_at_confirm_deletes_the_object()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var glb = TestGlb.Build((3, null, true));
        var req = Mini(glb) with { ByteSize = glb.Length + 1 };
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", req);
        using (var http = _f.Blobs.Client()) await http.PutAsync(created!.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsync($"/v1/assets/{created.Asset.Id}/confirm", null)).StatusCode);
        AssertNothingStored(owner, created.Asset);
    }

    [Fact]
    public async Task Library_is_owner_only_and_paged()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add((await Upload(mine, TestGlb.Build((3 * (i + 1), null, true)), $"m{i}")).Id);
        var page1 = await mine.GetJson<Page<AssetDto>>("/v1/assets?limit=2");
        Assert.Equal(2, page1!.Items.Count); Assert.Equal(ids[1], page1.Next);
        var page2 = await mine.GetJson<Page<AssetDto>>($"/v1/assets?limit=2&after={page1.Next}");
        Assert.Equal([ids[2]], page2!.Items.Select(a => a.Id)); Assert.Null(page2.Next);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/v1/assets/{ids[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.PatchJson($"/v1/assets/{ids[0]}", new PatchAssetRequest("x", null, null, null))).StatusCode);
        var patched = await mine.PatchJson<AssetDto>($"/v1/assets/{ids[0]}", new PatchAssetRequest("Renamed", 1.5, null, null));
        Assert.Equal(("Renamed", 1.5), (patched!.Name, patched.Scale));
    }

    [Fact]
    public async Task Patching_a_modelled_base_onto_a_non_miniature_is_rejected()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((3, null, true));
        var terrain = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "Ruin") with { Kind = AssetKind.Terrain, ModelledBase = null });
        var response = await c.PatchAsJsonAsync($"/v1/assets/{terrain!.Asset.Id}", new PatchAssetRequest(null, null, null, Base.Circle(1.26)), ContentJson.Options);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var unchanged = await c.GetJson<AssetDto>($"/v1/assets/{terrain.Asset.Id}");
        Assert.Null(unchanged!.ModelledBase);
    }

    [Fact]
    public async Task Terrain_volumes_round_trip_and_other_kinds_refuse_them()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((30, null, true));
        var terrain = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "Ruin") with { Kind = AssetKind.Terrain, ModelledBase = null });
        var volumes = new[] { new Volume(VolumeKind.Solid, Footprint.Rect(6, 4), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, "ruined wall") };
        var saved = await c.PutJson<List<TerrainVolume>>($"/v1/assets/{terrain!.Asset.Id}/volumes", volumes);
        Assert.Single(saved!); Assert.Equal(volumes[0], saved[0].Volume);
        Assert.Equal(saved, await c.GetJson<List<TerrainVolume>>($"/v1/assets/{terrain.Asset.Id}/volumes"));
        var mini = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "Mini"));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/v1/assets/{mini!.Asset.Id}/volumes", volumes, ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task The_list_filters_by_kind_in_its_wire_spelling()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((36, null, true));
        var mini = (await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "Mini"))).Asset;
        await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "Hill") with { Kind = AssetKind.Terrain, ModelledBase = null });
        Assert.Equal([mini.Id], (await c.GetJson<Page<AssetDto>>("/v1/assets?kind=miniature")).Items.Select(a => a.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.GetAsync("/v1/assets?kind=tank")).StatusCode);
    }

    [Fact]
    public async Task The_upload_url_names_a_staging_key_and_confirm_moves_the_bytes_to_the_content_key()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var glb = TestGlb.Build((36, null, true));
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb));
        Assert.Equal("/" + IBlobStore.StagingKey(owner, created.Asset.Id), created.UploadUrl!.AbsolutePath);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/v1/assets/{created.Asset.Id}/confirm", null)).StatusCode);   // nothing staged yet
        await Put(created.UploadUrl, glb);
        Assert.Null(await _f.Blobs.Size(IBlobStore.Key(owner, created.Asset.Hash)));
        await c.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { });
        Assert.Equal(glb.Length, await _f.Blobs.Size(IBlobStore.Key(owner, created.Asset.Hash)));
        Assert.DoesNotContain(_f.Blobs.Keys, k => k.StartsWith(IBlobStore.StagingKey(owner, created.Asset.Id)));
    }

    [Fact]
    public async Task Bytes_that_are_not_the_declared_hash_are_refused_and_never_reach_the_content_key()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var (declared, sent) = (TestGlb.Build((36, null, true)), TestGlb.Build((33, null, true)));
        Assert.Equal(declared.Length, sent.Length);
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(declared));
        await Put(created.UploadUrl!, sent);
        var response = await c.PostAsync($"/v1/assets/{created.Asset.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("hash", await Detail(response));
        AssertNothingStored(owner, created.Asset);
        Assert.False((await c.GetJson<AssetDto>($"/v1/assets/{created.Asset.Id}")).Uploaded);
    }

    [Fact]
    public async Task A_deduplicated_confirm_succeeds_without_a_put_and_counts_triangles()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var glb = TestGlb.Build((36, null, true));
        await Upload(c, glb, "first");
        var second = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "second"));
        Assert.Null(second.UploadUrl);
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{second.Asset.Id}/confirm", new { });
        Assert.True(confirmed.Asset.Uploaded);
        Assert.Equal(12, confirmed.Triangles);
    }

    [Fact]
    public async Task Declared_sizes_are_capped_per_kind_at_create()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((3, null, true));
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/v1/assets", Mini(glb) with { ByteSize = UploadLimits.MeshBytes }, ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/assets", Mini(glb) with { ByteSize = UploadLimits.MeshBytes + 1 }, ContentJson.Options)).StatusCode);
        var texture = Mini(glb) with { Kind = AssetKind.Texture, ContentType = "image/png", ModelledBase = null };
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/v1/assets", texture with { ByteSize = UploadLimits.TextureBytes }, ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/assets", texture with { ByteSize = UploadLimits.TextureBytes + 1 }, ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Undefined_kinds_and_unusable_numbers_are_refused_at_create()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((3, null, true));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/assets", Mini(glb) with { Kind = (AssetKind)9 }, ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/assets", Mini(glb) with { ModelledBase = new Base((BaseShape)9, 1, 1) }, ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/assets", Mini(glb) with { Scale = 0 }, ContentJson.Options)).StatusCode);
    }

    async Task<(HttpStatusCode Status, string? Detail)> UploadTexture(HttpClient c, byte[] bytes, string contentType)
    {
        var req = new CreateAssetRequest("board", AssetKind.Texture, Sha(bytes), bytes.Length, contentType, 1, new Orientation(0, 0, 0), null);
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", req);
        await Put(created.UploadUrl!, bytes, contentType);
        var response = await c.PostAsync($"/v1/assets/{created.Asset.Id}/confirm", null);
        return (response.StatusCode, await Detail(response));
    }

    [Fact]
    public async Task A_texture_is_the_declared_format_within_4096_square()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.OK, (await UploadTexture(c, TestImages.Png(1024, 1024), "image/png")).Status);
        Assert.Equal(HttpStatusCode.OK, (await UploadTexture(c, TestImages.Jpeg(4096, 2048), "image/jpeg")).Status);
        Assert.Equal(HttpStatusCode.OK, (await UploadTexture(c, TestImages.WebP(512, 512), "image/webp")).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await UploadTexture(c, TestImages.Png(64, 64), "image/jpeg")).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await UploadTexture(c, TestImages.Png(4097, 64), "image/png")).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await UploadTexture(c, "GIF89a not a png"u8.ToArray(), "image/png")).Status);
    }

    [Fact]
    public async Task An_account_holds_at_most_500_assets()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var repo = _f.Services.GetRequiredService<IAssetRepository>();
        for (var i = 0; i < UploadLimits.AccountAssets; i++) await repo.Create(owner, Mini(TestGlb.Build((3, null, true)), $"m{i}") with { Hash = Sha(BitConverter.GetBytes(i)) });
        var response = await c.PostAsJsonAsync("/v1/assets", Mini(TestGlb.Build((3, null, true)), "one more"), ContentJson.Options);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("500", await Detail(response));
    }

    [Fact]
    public async Task An_account_holds_at_most_1_gib()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var repo = _f.Services.GetRequiredService<IAssetRepository>();
        for (var i = 0; i < UploadLimits.AccountBytes / UploadLimits.MeshBytes; i++)
            await repo.Create(owner, Mini(TestGlb.Build((3, null, true)), $"m{i}") with { Hash = Sha(BitConverter.GetBytes(i)), ByteSize = UploadLimits.MeshBytes });
        var response = await c.PostAsJsonAsync("/v1/assets", Mini(TestGlb.Build((3, null, true)), "one more"), ContentJson.Options);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("GiB", await Detail(response));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a name of sixty-five characters, which is one more than allowed!!")]
    [InlineData("bell\u0007")]
    [InlineData("rlo‮evil")]
    public async Task Names_go_through_the_text_check_at_create_and_patch(string name)
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((3, null, true));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/assets", Mini(glb, name), ContentJson.Options)).StatusCode);
        var ok = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "fine"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchJson($"/v1/assets/{ok.Asset.Id}", new PatchAssetRequest(name, null, null, null))).StatusCode);
        Assert.Equal("fine", (await c.GetJson<AssetDto>($"/v1/assets/{ok.Asset.Id}")).Name);
    }

    [Fact]
    public async Task Volumes_are_capped_checked_and_their_notes_limited()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((30, null, true));
        var terrain = (await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "Ruin") with { Kind = AssetKind.Terrain, ModelledBase = null })).Asset;
        var wall = new Volume(VolumeKind.Solid, Footprint.Rect(6, 4), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, "multi\nline\tnotes");
        async Task<HttpStatusCode> PutVolumes(object body) => (await c.PutAsJsonAsync($"/v1/assets/{terrain.Id}/volumes", body, ContentJson.Options)).StatusCode;
        Assert.Equal(HttpStatusCode.OK, await PutVolumes(Enumerable.Repeat(wall, UploadLimits.Volumes).ToArray()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutVolumes(Enumerable.Repeat(wall, UploadLimits.Volumes + 1).ToArray()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutVolumes(new[] { wall with { Notes = new string('n', Limits.Notes + 1) } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutVolumes(new[] { wall with { Notes = "bell\u0007" } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutVolumes(new[] { wall with { Kind = (VolumeKind)9 } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutVolumes(new[] { wall with { Declarations = (TerrainDeclarations)1024 } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutVolumes(new[] { wall with { Footprint = new Footprint((FootprintKind)9, 1, 1) } }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await PutVolumes(new[] { wall with { Footprint = Footprint.Rect(0, 4) } }));
        Assert.Equal(UploadLimits.Volumes, (await c.GetJson<List<TerrainVolume>>($"/v1/assets/{terrain.Id}/volumes")).Count);
    }
}
