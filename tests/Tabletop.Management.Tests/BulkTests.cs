using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

/// <summary>The library's multi-select: filing, unfiling and deleting many assets in one request.</summary>
public class BulkTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public BulkTests(ManagementFactory f) => _f = f;

    async Task<AssetDto> Asset(HttpClient c, string name)
    {
        var glb = TestGlb.Build((3, null, true));
        var r = new CreateAssetRequest(name, AssetKind.Miniature, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, "model/gltf-binary", 1, new(0, 0, 0), Base.Circle(1.26));
        return (await c.PostJson<CreateAssetResponse>("/v1/assets", r)).Asset;
    }

    static Task<HttpResponseMessage> Post(HttpClient c, string url, IReadOnlyList<Guid> ids) => c.PostAsJsonAsync(url, new BulkAssetsRequest(ids), ContentJson.Options);

    [Fact]
    public async Task Files_and_unfiles_many_assets_and_reports_the_ones_not_found()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var other = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var folder = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Orks", null));
        var a = await Asset(c, "A"); var b = await Asset(c, "B"); var theirs = await Asset(other, "Theirs");

        var added = await (await Post(c, $"/v1/folders/{folder.Id}/assets", [a.Id, b.Id, theirs.Id])).Content.ReadFromJsonAsync<BulkAssetsResponse>(ContentJson.Options);
        Assert.Equal([a.Id, b.Id], added!.Done);
        Assert.Equal([theirs.Id], added.NotFound);
        Assert.Equal([a.Id, b.Id], (await c.GetJson<List<FolderDto>>("/v1/folders")).Single().AssetIds.Order());

        var removed = await (await Post(c, $"/v1/folders/{folder.Id}/assets/remove", [a.Id])).Content.ReadFromJsonAsync<BulkAssetsResponse>(ContentJson.Options);
        Assert.Equal([a.Id], removed!.Done);
        Assert.Equal([b.Id], (await c.GetJson<List<FolderDto>>("/v1/folders")).Single().AssetIds);
    }

    [Fact]
    public async Task Another_owners_folder_is_not_found()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var other = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var folder = await other.PostJson<FolderDto>("/v1/folders", new FolderRequest("Theirs", null));
        var a = await Asset(c, "A");
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, $"/v1/folders/{folder.Id}/assets", [a.Id])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, $"/v1/folders/{folder.Id}/assets/remove", [a.Id])).StatusCode);
    }

    [Fact]
    public async Task Deletes_many_assets_and_leaves_another_owners()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var other = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var a = await Asset(c, "A"); var b = await Asset(c, "B"); var theirs = await Asset(other, "Theirs");
        var r = await (await Post(c, "/v1/assets/delete", [a.Id, b.Id, theirs.Id])).Content.ReadFromJsonAsync<BulkAssetsResponse>(ContentJson.Options);
        Assert.Equal([a.Id, b.Id], r!.Done);
        Assert.Equal([theirs.Id], r.NotFound);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/v1/assets/{a.Id}")).StatusCode);
        Assert.True((await other.GetAsync($"/v1/assets/{theirs.Id}")).IsSuccessStatusCode);
        Assert.DoesNotContain(_f.Blobs.Keys, k => k.Contains(a.Id.ToString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Limits.Bulk + 1)]
    public async Task A_bulk_request_names_one_to_200_assets(int count)
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var folder = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("F", null));
        var ids = Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToList();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(c, "/v1/assets/delete", ids)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(c, $"/v1/folders/{folder.Id}/assets", ids)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(c, $"/v1/folders/{folder.Id}/assets/remove", ids)).StatusCode);
    }
}
