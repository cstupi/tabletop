using System.Net;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Games;
using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

/// <summary>Shared models (spec 2026-10-25): A shares, B copies and plays the copy; A's unshare, delete and account delete leave it working.</summary>
public class SharedAssetTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public SharedAssetTests(ManagementFactory f) => _f = f;

    private async Task<AssetDto> Upload(HttpClient who, byte[] glb, string name)
    {
        var req = new CreateAssetRequest(name, AssetKind.Miniature, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, "model/gltf-binary", 1.5,
            new Orientation(90, 0, 0), Base.Circle(1.26));
        var created = await who.PostJson<CreateAssetResponse>("/v1/assets", req);
        using var http = _f.Blobs.Client();
        await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new(req.ContentType) } });
        return (await who.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { })).Asset;
    }

    private async Task<byte[]> Download(HttpClient who, string url)
    {
        using var http = _f.Blobs.Client();
        return await http.GetByteArrayAsync((await who.GetJson<DownloadResponse>(url)).Url);
    }

    [Fact]
    public async Task A_shares_B_copies_into_a_folder_and_plays_it_and_the_copy_outlives_the_source()
    {
        _f.Rows.SeedProperties("sha256:shared", "GF", "grimdark-future");
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "Sharer"); var b = await _f.AuthenticatedClient(bId, "Copier");
        var glb = TestGlb.Build((9, null, true));
        var mesh = await Upload(a, glb, "Dread");
        var kept = await Upload(a, TestGlb.Build((12, null, true)), "Private");

        var share = await a.PostJson<BulkAssetsResponse>("/v1/assets/share", new BulkAssetsRequest([mesh.Id, kept.Id, Guid.Empty]));
        Assert.Equal([mesh.Id, kept.Id], share.Done);
        Assert.Equal([Guid.Empty], share.NotFound);
        await a.PostJson<BulkAssetsResponse>("/v1/assets/unshare", new BulkAssetsRequest([kept.Id]));
        Assert.True((await a.GetJson<AssetDto>($"/v1/assets/{mesh.Id}")).Shared);

        var listed = await b.GetJson<Page<SharedAssetDto>>("/v1/shared-assets?q=dread&kind=miniature");
        var item = Assert.Single(listed.Items);
        Assert.Equal((mesh.Id, "Dread", "Sharer", 1.5), (item.Id, item.Name, item.OwnerName, item.Scale));
        Assert.Empty((await b.GetJson<Page<SharedAssetDto>>("/v1/shared-assets?q=private")).Items);
        Assert.Empty((await a.GetJson<Page<SharedAssetDto>>("/v1/shared-assets?q=dread")).Items);         // never one's own
        Assert.Equal("Dread", (await b.GetJson<SharedAssetDto>($"/v1/shared-assets/{mesh.Id}")).Name);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/shared-assets/{kept.Id}")).StatusCode);
        Assert.Equal(glb, await Download(b, $"/v1/shared-assets/{mesh.Id}/download"));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/shared-assets/{kept.Id}/download")).StatusCode);

        var folder = await b.PostJson<FolderDto>("/v1/folders", new FolderRequest("Borrowed", null));
        var copied = await b.PostJson<CopySharedResponse>("/v1/shared-assets/copy", new CopySharedRequest([mesh.Id, kept.Id], folder.Id));
        var copy = Assert.Single(copied.Copied);
        Assert.Equal([kept.Id], copied.NotFound);
        Assert.Equal(("Dread", 1.5, new Orientation(90, 0, 0), false, true), (copy.Name, copy.Scale, copy.Orientation, copy.Shared, copy.Uploaded));
        Assert.Contains(copy.Id, (await b.GetJson<List<FolderDto>>("/v1/folders")).Single(f => f.Id == folder.Id).AssetIds);
        Assert.Equal(glb, await Download(b, $"/v1/assets/{copy.Id}/download"));

        // B's army takes the copy, as any upload of B's; A's original is still not B's to assign.
        var army = await b.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.PatchJson($"/v1/armies/{army.Id}", new PatchArmyRequest(null, [new UnitPatch(army.Units[0].Id, mesh.Id)], null))).StatusCode);
        await b.PatchJson<Army>($"/v1/armies/{army.Id}", new PatchArmyRequest(null, [new UnitPatch(army.Units[0].Id, copy.Id)], null));

        // A game between them: A's manifest presigns B's copy.
        var game = await a.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Shared", "sha256:shared", SchedulerShape.PlayerTurn, Enforcement.Silent, false));
        await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        _f.Rows.PickArmy(game.Id, bId, army.Id);
        var entry = (await a.GetJson<Manifest>($"/v1/games/{game.Id}/assets")).Assets.Single(m => m.AssetId == copy.Id);
        using (var http = _f.Blobs.Client()) Assert.Equal(glb, await http.GetByteArrayAsync(entry.Url));

        // A unshares and deletes the source: the copy still downloads.
        await a.PostJson<BulkAssetsResponse>("/v1/assets/unshare", new BulkAssetsRequest([mesh.Id]));
        Assert.Empty((await b.GetJson<Page<SharedAssetDto>>("/v1/shared-assets?q=dread")).Items);
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/assets/{mesh.Id}")).StatusCode);
        Assert.Equal(glb, await Download(b, $"/v1/assets/{copy.Id}/download"));

        // B deletes the copy: now nothing names the object and it goes.
        Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync($"/v1/assets/{copy.Id}")).StatusCode);
        Assert.Null(await _f.Blobs.Size(IBlobStore.Key(aId, mesh.Hash)));
    }

    [Fact]
    public async Task A_copy_survives_the_sharers_account_delete_and_bulk_delete()
    {
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "Leaver"); var b = await _f.AuthenticatedClient(bId, "Keeper");
        var glb = TestGlb.Build((15, null, true)); var other = TestGlb.Build((18, null, true));
        var mesh = await Upload(a, glb, "Gone soon");
        var second = await Upload(a, other, "Also gone");
        await a.PostJson<BulkAssetsResponse>("/v1/assets/share", new BulkAssetsRequest([mesh.Id, second.Id]));
        var copies = (await b.PostJson<CopySharedResponse>("/v1/shared-assets/copy", new CopySharedRequest([mesh.Id, second.Id], null))).Copied;

        await a.PostJson<BulkAssetsResponse>("/v1/assets/delete", new BulkAssetsRequest([second.Id]));
        Assert.Equal(other, await Download(b, $"/v1/assets/{copies[1].Id}/download"));
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/v1/account")).StatusCode);
        Assert.Equal(glb, await Download(b, $"/v1/assets/{copies[0].Id}/download"));
    }

    [Fact]
    public async Task Bad_requests()
    {
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "A"); var b = await _f.AuthenticatedClient(bId, "B");
        var mesh = await Upload(a, TestGlb.Build((21, null, true)), "Mesh");
        await a.PostJson<BulkAssetsResponse>("/v1/assets/share", new BulkAssetsRequest([mesh.Id]));
        var theirs = await a.PostJson<FolderDto>("/v1/folders", new FolderRequest("Mine", null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.PostAsync("/v1/assets/share", JsonContent(new BulkAssetsRequest([])))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.GetAsync("/v1/shared-assets?kind=hat")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.GetAsync($"/v1/shared-assets?q={new string('x', 65)}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.PostAsync("/v1/shared-assets/copy", JsonContent(new CopySharedRequest([], null)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync("/v1/shared-assets/copy", JsonContent(new CopySharedRequest([mesh.Id], theirs.Id)))).StatusCode);
        Assert.Equal([mesh.Id], (await b.PostJson<BulkAssetsResponse>("/v1/assets/share", new BulkAssetsRequest([mesh.Id]))).NotFound);   // not B's to share
        await b.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("Not a texture", AssetKind.Texture, mesh.Hash, 10, "image/png", 1, new Orientation(0, 0, 0), null));
        Assert.Equal(HttpStatusCode.Conflict, (await b.PostAsync("/v1/shared-assets/copy", JsonContent(new CopySharedRequest([mesh.Id], null)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/v1/shared-assets")).StatusCode);
    }

    private static HttpContent JsonContent(object body) => System.Net.Http.Json.JsonContent.Create(body, options: ContentJson.Options);
}
