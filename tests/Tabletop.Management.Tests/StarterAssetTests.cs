using System.Net.Http.Json;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests;

public class StarterAssetTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public StarterAssetTests(ManagementFactory f) => _f = f;

    private async Task<HttpClient> Login(string email, bool signup)
    {
        var c = _f.CreateClient();
        var session = await c.PostJson<SessionResponse>(signup ? "/v1/auth/password/signup" : "/v1/auth/password", new { email, password = "long enough password" });
        c.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionJwt);
        return c;
    }

    private static async Task<IReadOnlyList<AssetDto>> Assets(HttpClient c) => (await c.GetJson<Page<AssetDto>>("/v1/assets?limit=100")).Items;

    [Fact]
    public async Task Signup_seeds_the_starter_miniatures_uploaded_and_in_a_starter_folder()
    {
        var c = await Login("starter@example.com", signup: true);
        var assets = await Assets(c);
        Assert.Equal(StarterMeshes.All.Select(m => m.Name), assets.Select(a => a.Name));
        Assert.All(assets, a =>
        {
            Assert.Equal(AssetKind.Miniature, a.Kind);
            Assert.True(a.Uploaded);
            Assert.Equal(1.0, a.Scale);
            Assert.Equal(new Orientation(0, 0, 0), a.Orientation);
        });
        Assert.Equal(StarterMeshes.All.Select(m => m.ModelledBase), assets.Select(a => a.ModelledBase!.Value));

        var folder = Assert.Single(await c.GetJson<List<FolderDto>>("/v1/folders"));
        Assert.Equal(("Starter", (Guid?)null), (folder.Name, folder.ParentId));
        Assert.Equal(assets.Select(a => a.Id).Order(), folder.AssetIds.Order());

        var dl = await c.GetJson<DownloadResponse>($"/v1/assets/{assets[1].Id}/download");
        using var http = _f.Blobs.Client();
        Assert.Equal(StarterMeshes.All[1].Glb, await http.GetByteArrayAsync(dl.Url));
    }

    [Fact]
    public async Task A_later_login_seeds_nothing_and_a_deleted_starter_stays_deleted()
    {
        var c = await Login("again@example.com", signup: true);
        var first = await Assets(c);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/assets/{first[0].Id}")).StatusCode);

        var again = await Login("again@example.com", signup: false);
        Assert.Equal(first.Skip(1).Select(a => a.Id), (await Assets(again)).Select(a => a.Id));
        Assert.Single(await again.GetJson<List<FolderDto>>("/v1/folders"));
    }

    [Fact]
    public async Task An_account_that_predates_the_starter_set_gets_it_on_its_next_login()
    {
        await Login("older@example.com", signup: true);
        var accountId = Guid.Parse(_f.Stytch.Users["older@example.com"].ExternalId!);
        lock (_f.Rows.Gate)
        {
            foreach (var id in _f.Rows.Assets.Where(a => a.Value.Owner == accountId).Select(a => a.Key).ToList()) _f.Rows.RemoveAsset(id);
            foreach (var id in _f.Rows.Folders.Where(f => f.Value.Owner == accountId).Select(f => f.Key).ToList()) _f.Rows.RemoveFolder(id);
            _f.Rows.Accounts[accountId].StarterSeeded = false;
        }

        var c = await Login("older@example.com", signup: false);
        Assert.Equal(StarterMeshes.All.Count, (await Assets(c)).Count);
    }
}
