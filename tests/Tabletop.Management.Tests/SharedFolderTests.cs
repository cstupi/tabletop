using System.Net;
using System.Net.Http.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Shared;
using Tabletop.Management.Http;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests;

/// <summary>Shared folders (spec 2026-11-07): A sets a username and shares a folder tree; B browses it by username, previews and copies a
/// model and a sub-folder; nothing outside A's Shared folder is visible; A's delete leaves B's copy working.</summary>
public class SharedFolderTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public SharedFolderTests(ManagementFactory f) => _f = f;

    private static HttpContent Json(object body) => JsonContent.Create(body, options: ContentJson.Options);

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

    private static string Username(string stem) => stem + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task A_username_is_set_checked_unique_and_cleared()
    {
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "Clay"); var b = await _f.AuthenticatedClient(bId, "Clay");
        var name = Username("clay_");
        Assert.Equal(new AccountRow(aId, "Clay", name), await a.PutJson<AccountRow>("/v1/account/username", new UsernameRequest(name)));
        Assert.Equal(name, (await a.GetJson<AccountRow>("/v1/account")).Username);
        Assert.Equal(HttpStatusCode.Conflict, (await b.PutAsync("/v1/account/username", Json(new UsernameRequest(name.ToUpperInvariant())))).StatusCode);
        foreach (var bad in new[] { "ab", "has space", new string('x', 33), "émile" })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.PutAsync("/v1/account/username", Json(new UsernameRequest(bad)))).StatusCode);
        Assert.Null((await a.PutJson<AccountRow>("/v1/account/username", new UsernameRequest(null))).Username);
        Assert.Equal(name, (await b.PutJson<AccountRow>("/v1/account/username", new UsernameRequest(name))).Username);
    }

    [Fact]
    public async Task B_browses_As_shared_folder_by_username_copies_a_model_and_a_tree_and_the_copies_outlive_the_sources()
    {
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "Sharer"); var b = await _f.AuthenticatedClient(bId, "Copier");
        var user = Username("sharer");
        await a.PutJson<AccountRow>("/v1/account/username", new UsernameRequest(user));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/shared/{user}")).StatusCode);            // no Shared folder yet

        var shared = await a.PostJson<FolderDto>("/v1/folders/shared", new { });
        Assert.True(shared.Shared);
        Assert.Equal(shared.Id, (await a.PostJson<FolderDto>("/v1/folders/shared", new { })).Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.PatchJson($"/v1/folders/{shared.Id}", new FolderRequest("Shared", (await a.PostJson<FolderDto>("/v1/folders", new FolderRequest("Elsewhere", null))).Id))).StatusCode);
        var orks = await a.PostJson<FolderDto>("/v1/folders", new FolderRequest("Orks", shared.Id));
        var boyz = await a.PostJson<FolderDto>("/v1/folders", new FolderRequest("Boyz", orks.Id));
        var secret = await a.PostJson<FolderDto>("/v1/folders", new FolderRequest("Secret", null));
        var (glb1, glb2) = (TestGlb.Build((9, null, true)), TestGlb.Build((11, null, true)));
        var (boss, boy, hidden) = (await Upload(a, glb1, "Warboss"), await Upload(a, glb2, "Boy"), await Upload(a, TestGlb.Build((13, null, true)), "Hidden"));
        await a.PutAsync($"/v1/folders/{orks.Id}/assets/{boss.Id}", null);
        await a.PutAsync($"/v1/folders/{boyz.Id}/assets/{boy.Id}", null);
        await a.PutAsync($"/v1/folders/{secret.Id}/assets/{hidden.Id}", null);

        var view = await b.GetJson<SharedLibraryDto>($"/v1/shared/{user.ToUpperInvariant()}");
        Assert.Equal((user, "Sharer"), (view.Username, view.Name));
        Assert.Equal(new[] { shared.Id, orks.Id, boyz.Id }.Order(), view.Folders.Select(f => f.Id).Order());
        Assert.Equal(new[] { boss.Id, boy.Id }.Order(), view.Assets.Select(x => x.Id).Order());
        Assert.Equal(glb1, await Download(b, $"/v1/shared/{user}/assets/{boss.Id}/download"));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/shared/{user}/assets/{hidden.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/assets/{boss.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/v1/shared/no_such_user_here")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/v1/shared/a")).StatusCode);

        // A model into a folder of B's; then the Orks tree to B's top level.
        var into = await b.PostJson<FolderDto>("/v1/folders", new FolderRequest("Borrowed", null));
        var one = await b.PostJson<CopySharedResponse>($"/v1/shared/{user}/copy", new CopySharedRequest([boss.Id, hidden.Id], null, into.Id));
        Assert.Equal([hidden.Id], one.NotFound);
        var copy = Assert.Single(one.Assets);
        Assert.Contains(copy, (await b.GetJson<List<FolderDto>>("/v1/folders")).Single(f => f.Id == into.Id).AssetIds);
        var tree = await b.PostJson<CopySharedResponse>($"/v1/shared/{user}/copy", new CopySharedRequest(null, orks.Id, null));
        Assert.Equal(["Orks", "Boyz"], tree.Folders.Select(f => f.Name));
        Assert.Equal((null, tree.Folders[0].Id), (tree.Folders[0].ParentId, tree.Folders[1].ParentId));
        Assert.Equal(2, tree.Assets.Count);

        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/shared/{user}/copy", Json(new CopySharedRequest(null, secret.Id, null)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/shared/{user}/copy", Json(new CopySharedRequest([boss.Id], null, secret.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.PostAsync($"/v1/shared/{user}/copy", Json(new CopySharedRequest([boss.Id], null, null)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.PostAsync($"/v1/shared/{user}/copy", Json(new CopySharedRequest([boss.Id], orks.Id, null)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.PostAsync($"/v1/shared/{user}/copy", Json(new CopySharedRequest([hidden.Id], null, null)))).StatusCode);

        // A deletes the sources and the Shared folder: B's copies still download; B deletes them and the objects go.
        foreach (var x in new[] { boss, boy }) Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/assets/{x.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/folders/{shared.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/shared/{user}")).StatusCode);
        Assert.Equal(glb1, await Download(b, $"/v1/assets/{copy}/download"));
        foreach (var id in one.Assets.Concat(tree.Assets)) Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync($"/v1/assets/{id}")).StatusCode);
        Assert.Null(await _f.Blobs.Size(IBlobStore.Key(aId, boss.Hash)));
        Assert.Null(await _f.Blobs.Size(IBlobStore.Key(aId, boy.Hash)));
    }

    [Fact]
    public async Task A_copy_over_the_quota_writes_nothing_and_the_daily_cap_answers_429()
    {
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "Sharer"); var b = await _f.AuthenticatedClient(bId, "Copier");
        var user = Username("quota");
        await a.PutJson<AccountRow>("/v1/account/username", new UsernameRequest(user));
        var shared = await a.PostJson<FolderDto>("/v1/folders/shared", new { });
        var boss = await Upload(a, TestGlb.Build((15, null, true)), "Warboss");
        await a.PutAsync($"/v1/folders/{shared.Id}/assets/{boss.Id}", null);

        // B is one asset short of the quota's 500: seeding rows directly.
        lock (_f.Rows.Gate)
        {
            _f.Rows.Blobs[(bId, "filler")] = new InMemoryRows.BlobRow { Key = "filler", ContentType = "model/gltf-binary", Uploaded = true };
            for (var i = 0; i < UploadLimits.AccountAssets; i++)
                _f.Rows.Assets[_f.Rows.NextId()] = new InMemoryRows.AssetRow { Owner = bId, Hash = "filler", Name = "f", Kind = AssetKind.Miniature, ModelledBase = Base.Circle(1) };
        }
        var over = await b.PostAsync($"/v1/shared/{user}/copy", Json(new CopySharedRequest(null, shared.Id, null)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, over.StatusCode);
        Assert.Contains($"at most {UploadLimits.AccountAssets} assets", await over.Content.ReadAsStringAsync());
        Assert.Empty(await b.GetJson<List<FolderDto>>("/v1/folders"));
        lock (_f.Rows.Gate) foreach (var k in _f.Rows.Assets.Where(x => x.Value.Owner == bId).Select(x => x.Key).ToList()) _f.Rows.Assets.Remove(k);

        for (var i = 0; i < SharedCopyLimits.Default.PerDay; i++)
            await b.PostJson<CopySharedResponse>($"/v1/shared/{user}/copy", new CopySharedRequest([boss.Id], null, null));
        var capped = await b.PostAsync($"/v1/shared/{user}/copy", Json(new CopySharedRequest([boss.Id], null, null)));
        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        Assert.NotNull(capped.Headers.RetryAfter);
    }
}
