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
}
