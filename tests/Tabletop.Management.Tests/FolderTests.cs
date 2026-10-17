using System.Net;
using System.Net.Http.Json;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class FolderTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public FolderTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task Nest_move_and_file_an_asset_in_two_folders()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var orks = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Orks", null));
        var winter = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Winter", null));
        var boyz = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Boyz", orks.Id));
        var glb = TestGlb.Build((3, null, true));
        var asset = (await c.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("Boy", AssetKind.Miniature, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, "model/gltf-binary", 1, new(0, 0, 0), Tabletop.Geometry.Base.Circle(1.26)))).Asset;
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/v1/folders/{boyz.Id}/assets/{asset.Id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/v1/folders/{winter.Id}/assets/{asset.Id}", null)).StatusCode);
        var tree = await c.GetJson<List<FolderDto>>("/v1/folders");
        Assert.Equal(2, tree.Count(f => f.AssetIds.Contains(asset.Id)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchJson($"/v1/folders/{orks.Id}", new FolderRequest("Orks", boyz.Id))).StatusCode);   // cycle
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/folders/{orks.Id}")).StatusCode);
        tree = await c.GetJson<List<FolderDto>>("/v1/folders");
        Assert.Equal([winter.Id], tree.Select(f => f.Id));                       // Boyz cascaded with Orks
        Assert.True((await c.GetAsync($"/v1/assets/{asset.Id}")).IsSuccessStatusCode);  // the asset is untouched
    }

    [Fact]
    public async Task Folders_are_owner_only()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var f = await mine.PostJson<FolderDto>("/v1/folders", new FolderRequest("Mine", null));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.DeleteAsync($"/v1/folders/{f.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await theirs.PostAsJsonAsync("/v1/folders", new FolderRequest("Under yours", f.Id), Tabletop.Content.ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Patching_a_blank_name_is_rejected_and_leaves_the_name_unchanged()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var f = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Named", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchJson($"/v1/folders/{f.Id}", new FolderRequest("   ", null))).StatusCode);
        var tree = await c.GetJson<List<FolderDto>>("/v1/folders");
        Assert.Equal("Named", tree.Single(x => x.Id == f.Id).Name);
    }

    [Theory]
    [InlineData("a name of sixty-five characters, which is one more than allowed!!")]
    [InlineData("tab\there")]
    [InlineData("rlo‮evil")]
    public async Task Names_go_through_the_text_check_at_create_and_patch(string name)
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/folders", new FolderRequest(name, null), Tabletop.Content.ContentJson.Options)).StatusCode);
        var f = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Named", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchJson($"/v1/folders/{f.Id}", new FolderRequest(name, null))).StatusCode);
    }

    [Fact]
    public async Task An_account_holds_at_most_200_folders()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var repo = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IFolderRepository>(_f.Services);
        for (var i = 0; i < Tabletop.Management.Http.Limits.PerAccount; i++) await repo.Create(owner, new FolderRequest($"f{i}", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/folders", new FolderRequest("one more", null), Tabletop.Content.ContentJson.Options)).StatusCode);
    }
}
