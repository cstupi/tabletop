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

    async Task<AssetDto> Upload(HttpClient c, byte[] glb, string name = "Marine")
    {
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, name));
        if (created!.UploadUrl is not null)
            using (var http = new HttpClient()) Assert.True((await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } })).IsSuccessStatusCode);
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
        using (var http = new HttpClient()) await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { });
        Assert.Equal(12, confirmed!.Triangles); Assert.Null(confirmed.Warning); Assert.True(confirmed.Asset.Uploaded);
        var dl = await c.GetJson<DownloadResponse>($"/v1/assets/{created.Asset.Id}/download");
        using var http2 = new HttpClient();
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
        using (var http = new HttpClient()) await http.PutAsync(created!.UploadUrl, new ByteArrayContent(stl) { Headers = { ContentType = new("model/gltf-binary") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsync($"/v1/assets/{created.Asset.Id}/confirm", null)).StatusCode);
        var dense = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(TestGlb.Build((300_003, null, true)), "dense"));
        using (var http = new HttpClient()) await http.PutAsync(dense!.UploadUrl, new ByteArrayContent(TestGlb.Build((300_003, null, true))) { Headers = { ContentType = new("model/gltf-binary") } });
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{dense.Asset.Id}/confirm", new { });
        Assert.Equal(100_001, confirmed!.Triangles); Assert.Contains("100,000", confirmed.Warning); Assert.True(confirmed.Asset.Uploaded);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsJsonAsync("/v1/assets", Mini(stl) with { ContentType = "model/stl" }, ContentJson.Options)).StatusCode);
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
}
