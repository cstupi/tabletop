using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Shared;
using Tabletop.Management.Http;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests;

/// <summary>Asset thumbnails (UX redesign, web): the uploader's browser renders a small image and puts it; the lists carry a presigned
/// URL to it; a copy shows its source's; deleting the last asset naming it deletes the object.</summary>
public class ThumbnailTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public ThumbnailTests(ManagementFactory f) => _f = f;

    /// <summary>A PNG's signature and IHDR: all the header check reads.</summary>
    internal static byte[] Png(int width = 256, int height = 256, int size = 64)
    {
        var b = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), (uint)height);
        return b;
    }

    private static ByteArrayContent Body(byte[] bytes, string type = "image/png") => new(bytes) { Headers = { ContentType = new MediaTypeHeaderValue(type) } };

    private async Task<AssetDto> Upload(HttpClient who, byte[] bytes, AssetKind kind = AssetKind.Miniature, string type = "model/gltf-binary")
    {
        var req = new CreateAssetRequest("Thing", kind, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), bytes.Length, type, 1,
            new Orientation(0, 0, 0), kind == AssetKind.Miniature ? Base.Circle(1.26) : null);
        var created = await who.PostJson<CreateAssetResponse>("/v1/assets", req);
        if (created.UploadUrl is not null)
            using (var http = _f.Blobs.Client()) await http.PutAsync(created.UploadUrl, new ByteArrayContent(bytes) { Headers = { ContentType = new(type) } });
        return (await who.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { })).Asset;
    }

    private async Task<byte[]> Fetch(Uri url) { using var http = _f.Blobs.Client(); return await http.GetByteArrayAsync(url); }

    [Fact]
    public async Task The_owner_puts_a_thumbnail_and_the_list_and_the_asset_carry_its_url()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var a = await Upload(c, TestGlb.Build((21, null, true)));
        var plain = await Upload(c, TestGlb.Build((22, null, true)));
        Assert.Null((await c.GetJson<Page<AssetDto>>("/v1/assets")).Items.Single(x => x.Id == a.Id).Thumbnail);

        var png = Png();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/v1/assets/{a.Id}/thumbnail", Body(png))).StatusCode);
        var listed = (await c.GetJson<Page<AssetDto>>("/v1/assets")).Items;
        Assert.Equal(png, await Fetch(listed.Single(x => x.Id == a.Id).Thumbnail!));
        Assert.Null(listed.Single(x => x.Id == plain.Id).Thumbnail);
        Assert.Equal(png, await Fetch((await c.GetJson<AssetDto>($"/v1/assets/{a.Id}")).Thumbnail!));
        Assert.DoesNotContain("thumbnailKey", await c.GetStringAsync("/v1/assets"), StringComparison.OrdinalIgnoreCase);

        var again = Png(128, 128);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/v1/assets/{a.Id}/thumbnail", Body(again))).StatusCode);
        Assert.Equal(again, await Fetch((await c.GetJson<AssetDto>($"/v1/assets/{a.Id}")).Thumbnail!));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/assets/{a.Id}")).StatusCode);
        Assert.Null(await _f.Blobs.Size(IBlobStore.ThumbnailKey(a.Id)));
    }

    [Fact]
    public async Task A_thumbnail_is_a_small_png_or_webp_of_an_uploaded_mesh_of_the_callers()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var a = await Upload(c, TestGlb.Build((23, null, true)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PutAsync($"/v1/assets/{a.Id}/thumbnail", Body("not an image at all"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PutAsync($"/v1/assets/{a.Id}/thumbnail", Body(Png(1024, 256)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PutAsync($"/v1/assets/{a.Id}/thumbnail", Body(Png(), "image/webp"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PutAsync($"/v1/assets/{a.Id}/thumbnail", Body(Png(size: ThumbnailLimits.Bytes + 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PutAsync($"/v1/assets/{a.Id}/thumbnail", Body(Png(), "image/jpeg"))).StatusCode);

        var stranger = await _f.AuthenticatedClient(Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsync($"/v1/assets/{a.Id}/thumbnail", Body(Png()))).StatusCode);

        var pending = (await c.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("Half", AssetKind.Miniature, new string('a', 64), 10, "model/gltf-binary", 1,
            new Orientation(0, 0, 0), Base.Circle(1)))).Asset;
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsync($"/v1/assets/{pending.Id}/thumbnail", Body(Png()))).StatusCode);
        var texture = await Upload(c, Png(64, 64), AssetKind.Texture, "image/png");
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsync($"/v1/assets/{texture.Id}/thumbnail", Body(Png()))).StatusCode);
        Assert.Null(await _f.Blobs.Size(IBlobStore.ThumbnailKey(a.Id)));
    }

    [Fact]
    public async Task A_shared_model_shows_its_thumbnail_and_the_copy_keeps_it_after_the_sharer_deletes()
    {
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "Sharer"); var b = await _f.AuthenticatedClient(bId, "Copier");
        var user = "thumbs" + Guid.NewGuid().ToString("N")[..8];
        await a.PutJson<AccountRow>("/v1/account/username", new UsernameRequest(user));
        var shared = await a.PostJson<FolderDto>("/v1/folders/shared", new { });
        var boss = await Upload(a, TestGlb.Build((24, null, true)));
        await a.PutAsync($"/v1/folders/{shared.Id}/assets/{boss.Id}", null);
        var png = Png();
        await a.PutAsync($"/v1/assets/{boss.Id}/thumbnail", Body(png));

        Assert.Equal(png, await Fetch((await b.GetJson<SharedLibraryDto>($"/v1/shared/{user}")).Assets.Single().Thumbnail!));
        var copy = (await b.PostJson<CopySharedResponse>($"/v1/shared/{user}/copy", new CopySharedRequest([boss.Id], null, null))).Assets.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/assets/{boss.Id}")).StatusCode);
        Assert.Equal(png, await Fetch((await b.GetJson<Page<AssetDto>>("/v1/assets")).Items.Single(x => x.Id == copy).Thumbnail!));
        Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync($"/v1/assets/{copy}")).StatusCode);
        Assert.Null(await _f.Blobs.Size(IBlobStore.ThumbnailKey(boss.Id)));
    }
}
