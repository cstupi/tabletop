using System.Net;
using System.Net.Http.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;

namespace Tabletop.Management.Tests;

/// <summary>M17: one stranger, every owner-scoped route not already covered by another test class's
/// cross-owner case, each 404. The SQL behind every one of these is owner-scoped already; this is the
/// regression guard.</summary>
public class OwnerOnlyTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public OwnerOnlyTests(ManagementFactory f) => _f = f;

    public enum Route { AssetDelete, AssetConfirm, AssetDownload, AssetVolumesGet, AssetVolumesPut, LayoutPatch, LayoutDelete, ArmyPatch, FolderAssetPut, FolderAssetDelete }

    [Theory]
    [InlineData(Route.AssetDelete)]
    [InlineData(Route.AssetConfirm)]
    [InlineData(Route.AssetDownload)]
    [InlineData(Route.AssetVolumesGet)]
    [InlineData(Route.AssetVolumesPut)]
    [InlineData(Route.LayoutPatch)]
    [InlineData(Route.LayoutDelete)]
    [InlineData(Route.ArmyPatch)]
    [InlineData(Route.FolderAssetPut)]
    [InlineData(Route.FolderAssetDelete)]
    public async Task A_stranger_gets_404(Route route)
    {
        var owner = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var stranger = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var response = await Request(route, owner, stranger);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<CreateAssetResponse> CreateAsset(HttpClient owner, string name = "A")
    {
        var glb = TestGlb.Build((3, null, true));
        return (await owner.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest(name, AssetKind.Miniature,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, "model/gltf-binary", 1, new Orientation(0, 0, 0), Base.Circle(1.26))))!;
    }

    private static Task<HttpResponseMessage> Request(Route route, HttpClient owner, HttpClient stranger) => route switch
    {
        Route.AssetDelete => AssetRoute(owner, stranger, (s, id) => s.DeleteAsync($"/v1/assets/{id}")),
        Route.AssetConfirm => AssetRoute(owner, stranger, (s, id) => s.PostAsync($"/v1/assets/{id}/confirm", null)),
        Route.AssetDownload => AssetRoute(owner, stranger, (s, id) => s.GetAsync($"/v1/assets/{id}/download")),
        Route.AssetVolumesGet => AssetRoute(owner, stranger, (s, id) => s.GetAsync($"/v1/assets/{id}/volumes")),
        Route.AssetVolumesPut => AssetRoute(owner, stranger, (s, id) => s.PutAsJsonAsync($"/v1/assets/{id}/volumes", Array.Empty<Volume>(), ContentJson.Options)),
        Route.LayoutPatch => LayoutRoute(owner, stranger, (s, id) => s.PatchAsJsonAsync($"/v1/layouts/{id}", new PatchLayoutRequest("Renamed", null), ContentJson.Options)),
        Route.LayoutDelete => LayoutRoute(owner, stranger, (s, id) => s.DeleteAsync($"/v1/layouts/{id}")),
        Route.ArmyPatch => ArmyRoute(owner, stranger),
        Route.FolderAssetPut => FolderAssetRoute(owner, stranger, (s, folderId, assetId) => s.PutAsync($"/v1/folders/{folderId}/assets/{assetId}", null)),
        Route.FolderAssetDelete => FolderAssetRoute(owner, stranger, (s, folderId, assetId) => s.DeleteAsync($"/v1/folders/{folderId}/assets/{assetId}")),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private static async Task<HttpResponseMessage> AssetRoute(HttpClient owner, HttpClient stranger, Func<HttpClient, Guid, Task<HttpResponseMessage>> act) =>
        await act(stranger, (await CreateAsset(owner)).Asset.Id);

    private static async Task<HttpResponseMessage> LayoutRoute(HttpClient owner, HttpClient stranger, Func<HttpClient, Guid, Task<HttpResponseMessage>> act) =>
        await act(stranger, (await owner.PostJson<TableState>("/v1/layouts", LayoutTests.Sample())).Id);

    private static async Task<HttpResponseMessage> ArmyRoute(HttpClient owner, HttpClient stranger)
    {
        var army = await owner.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        return await stranger.PatchAsJsonAsync($"/v1/armies/{army.Id}", new PatchArmyRequest("Renamed", null, null), ContentJson.Options);
    }

    private static async Task<HttpResponseMessage> FolderAssetRoute(HttpClient owner, HttpClient stranger, Func<HttpClient, Guid, Guid, Task<HttpResponseMessage>> act)
    {
        var folder = await owner.PostJson<FolderDto>("/v1/folders", new FolderRequest("F", null));
        var asset = await CreateAsset(owner);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync($"/v1/folders/{folder.Id}/assets/{asset.Asset.Id}", null)).StatusCode);
        return await act(stranger, folder.Id, asset.Asset.Id);
    }
}
