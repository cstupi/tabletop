using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Offers;
using Tabletop.Management.Games;
using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

/// <summary>Shared models (spec 2026-10-25): A offers to B, C sees nothing, B accepts and plays the copy; A's withdraw, delete and account
/// delete leave it working; a declined offer leaves nothing.</summary>
public class OfferTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public OfferTests(ManagementFactory f) => _f = f;

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

    private async Task<GameInfo> PlayTogether(HttpClient creator, params HttpClient[] others)
    {
        _f.Rows.SeedProperties("sha256:offers-" + Guid.NewGuid().ToString("N"), "GF", "grimdark-future");
        var hash = _f.Rows.Properties.Keys.Last(k => k.StartsWith("sha256:offers-"));
        var game = await creator.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Friday", hash, SchedulerShape.PlayerTurn, Enforcement.Silent, false));
        foreach (var o in others) await o.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        return game;
    }

    [Fact]
    public async Task A_offers_to_B_C_sees_nothing_B_accepts_and_plays_it_and_the_copy_outlives_the_source()
    {
        var (aId, bId, cId) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "Sharer"); var b = await _f.AuthenticatedClient(bId, "Recipient"); var c = await _f.AuthenticatedClient(cId, "Outsider");
        var game = await PlayTogether(a, b);
        var glb = TestGlb.Build((9, null, true));
        var mesh = await Upload(a, glb, "Dread");

        var players = await a.GetJson<List<PlayerDto>>("/v1/offers/players");
        Assert.Equal([(bId, "Recipient")], players.Select(p => (p.AccountId, p.Name)));
        Assert.Equal(["Friday"], players[0].Games);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync("/v1/offers", JsonContent(new CreateOffersRequest([cId], [mesh.Id], null)))).StatusCode);

        var made = await a.PostJson<CreateOffersResponse>("/v1/offers", new CreateOffersRequest([bId], [mesh.Id], null));
        var offer = Assert.Single(made.Offers);
        Assert.Equal((OfferState.Open, "Recipient"), (offer.State, offer.RecipientName));

        // C: nothing to see, preview, download, accept, decline or withdraw.
        Assert.Equal(0, (await c.GetJson<OfferCountDto>("/v1/offers/received/count")).Open);
        Assert.Empty((await c.GetJson<Page<OfferDto>>("/v1/offers/received")).Items);
        foreach (var (method, url) in new[] { ("GET", $"/v1/offers/{offer.Id}"), ("GET", $"/v1/offers/{offer.Id}/assets/{mesh.Id}/download"), ("GET", $"/v1/assets/{mesh.Id}"),
                     ("POST", $"/v1/offers/{offer.Id}/accept"), ("POST", $"/v1/offers/{offer.Id}/decline"), ("POST", $"/v1/offers/{offer.Id}/withdraw") })
        {
            var response = method == "GET" ? await c.GetAsync(url) : await c.PostAsync(url, JsonContent(new { block = false, folderId = (Guid?)null }));
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {url}: {response.StatusCode}");
        }
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/v1/offers/{offer.Id}/assets/{mesh.Id}/download")).StatusCode);   // the sharer uses their own download

        // B: the count, the list, the preview.
        Assert.Equal(1, (await b.GetJson<OfferCountDto>("/v1/offers/received/count")).Open);
        var received = Assert.Single((await b.GetJson<Page<OfferDto>>("/v1/offers/received")).Items);
        Assert.Equal(("Sharer", "Dread", glb.LongLength), (received.SharerName, received.Assets.Single().Name, received.Assets.Single().ByteSize));
        Assert.Equal(glb, await Download(b, $"/v1/offers/{offer.Id}/assets/{mesh.Id}/download"));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/assets/{mesh.Id}/download")).StatusCode);

        var folder = await b.PostJson<FolderDto>("/v1/folders", new FolderRequest("Borrowed", null));
        var accepted = await b.PostJson<AcceptOfferResponse>($"/v1/offers/{offer.Id}/accept", new AcceptOfferRequest(folder.Id));
        var copy = Assert.Single(accepted.Copied);
        Assert.Equal(("Dread", 1.5, new Orientation(90, 0, 0), true), (copy.Name, copy.Scale, copy.Orientation, copy.Uploaded));
        Assert.Contains(copy.Id, (await b.GetJson<List<FolderDto>>("/v1/folders")).Single(f => f.Id == folder.Id).AssetIds);
        Assert.Equal(glb, await Download(b, $"/v1/assets/{copy.Id}/download"));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/offers/{offer.Id}/assets/{mesh.Id}/download")).StatusCode);   // closed
        Assert.Equal(OfferState.Accepted, (await a.GetJson<Page<OfferDto>>("/v1/offers/sent")).Items.Single().State);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/v1/offers/{offer.Id}/withdraw", JsonContent(new { }))).StatusCode);

        // B's army takes the copy; A's original is still not B's to assign.
        var army = await b.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.PatchJson($"/v1/armies/{army.Id}", new PatchArmyRequest(null, [new UnitPatch(army.Units[0].Id, mesh.Id)], null))).StatusCode);
        await b.PatchJson<Army>($"/v1/armies/{army.Id}", new PatchArmyRequest(null, [new UnitPatch(army.Units[0].Id, copy.Id)], null));
        _f.Rows.PickArmy(game.Id, bId, army.Id);
        var entry = (await a.GetJson<Manifest>($"/v1/games/{game.Id}/assets")).Assets.Single(m => m.AssetId == copy.Id);
        using (var http = _f.Blobs.Client()) Assert.Equal(glb, await http.GetByteArrayAsync(entry.Url));

        // A deletes the source: the copy still downloads. B deletes the copy: nothing names the object and it goes.
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/assets/{mesh.Id}")).StatusCode);
        Assert.Equal(glb, await Download(b, $"/v1/assets/{copy.Id}/download"));
        Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync($"/v1/assets/{copy.Id}")).StatusCode);
        Assert.Null(await _f.Blobs.Size(IBlobStore.Key(aId, mesh.Hash)));
    }

    [Fact]
    public async Task A_declined_or_withdrawn_offer_leaves_nothing_and_block_hides_the_sharer()
    {
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "Sharer"); var b = await _f.AuthenticatedClient(bId, "Recipient");
        await PlayTogether(b, a);
        var mesh = await Upload(a, TestGlb.Build((12, null, true)), "Unwanted");
        var folder = await a.PostJson<FolderDto>("/v1/folders", new FolderRequest("Lot", null));
        await a.PutAsync($"/v1/folders/{folder.Id}/assets/{mesh.Id}", null);

        var first = (await a.PostJson<CreateOffersResponse>("/v1/offers", new CreateOffersRequest([bId], null, folder.Id))).Offers.Single();
        Assert.Equal("Lot", first.Folder);
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsync($"/v1/offers/{first.Id}/decline", JsonContent(new DeclineOfferRequest(false)))).StatusCode);
        Assert.Empty((await b.GetJson<Page<AssetDto>>("/v1/assets")).Items.Where(x => x.Hash == mesh.Hash));
        Assert.Equal(OfferState.Declined, (await a.GetJson<OfferDto>($"/v1/offers/{first.Id}")).State);

        var second = (await a.PostJson<CreateOffersResponse>("/v1/offers", new CreateOffersRequest([bId], [mesh.Id], null))).Offers.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/v1/offers/{second.Id}/withdraw", JsonContent(new { }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/offers/{second.Id}/accept", JsonContent(new AcceptOfferRequest(null)))).StatusCode);

        var third = (await a.PostJson<CreateOffersResponse>("/v1/offers", new CreateOffersRequest([bId], [mesh.Id], null))).Offers.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsync($"/v1/offers/{third.Id}/decline", JsonContent(new DeclineOfferRequest(true)))).StatusCode);
        Assert.Equal([aId], (await b.GetJson<List<BlockedDto>>("/v1/offers/blocked")).Select(x => x.AccountId));
        var hidden = (await a.PostJson<CreateOffersResponse>("/v1/offers", new CreateOffersRequest([bId], [mesh.Id], null))).Offers.Single();
        Assert.Equal(OfferState.Open, hidden.State);
        Assert.Equal(0, (await b.GetJson<OfferCountDto>("/v1/offers/received/count")).Open);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/offers/{hidden.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync($"/v1/offers/blocked/{aId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/v1/offers/blocked/{aId}")).StatusCode);
        Assert.Equal(1, (await b.GetJson<OfferCountDto>("/v1/offers/received/count")).Open);
        Assert.Empty((await b.GetJson<Page<AssetDto>>("/v1/assets")).Items.Where(x => x.Hash == mesh.Hash));
    }

    [Fact]
    public async Task A_copy_survives_the_sharers_account_delete()
    {
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "Leaver"); var b = await _f.AuthenticatedClient(bId, "Keeper");
        await PlayTogether(b, a);
        var glb = TestGlb.Build((15, null, true));
        var mesh = await Upload(a, glb, "Gone soon");
        var offer = (await a.PostJson<CreateOffersResponse>("/v1/offers", new CreateOffersRequest([bId], [mesh.Id], null))).Offers.Single();
        var copy = (await b.PostJson<AcceptOfferResponse>($"/v1/offers/{offer.Id}/accept", new AcceptOfferRequest(null))).Copied.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/v1/account")).StatusCode);
        Assert.Equal(glb, await Download(b, $"/v1/assets/{copy.Id}/download"));
    }

    [Fact]
    public async Task Bad_requests()
    {
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "A"); var b = await _f.AuthenticatedClient(bId, "B");
        await PlayTogether(a, b);
        var mesh = await Upload(a, TestGlb.Build((21, null, true)), "Mesh");
        var theirs = await a.PostJson<FolderDto>("/v1/folders", new FolderRequest("Mine", null));

        async Task<HttpStatusCode> Offer(HttpClient who, object body) => (await who.PostAsync("/v1/offers", JsonContent(body))).StatusCode;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Offer(a, new CreateOffersRequest([], [mesh.Id], null)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Offer(a, new CreateOffersRequest(Enumerable.Range(0, 21).Select(_ => Guid.CreateVersion7()).ToList(), [mesh.Id], null)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Offer(a, new CreateOffersRequest([bId], null, null)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Offer(a, new CreateOffersRequest([bId], [mesh.Id], theirs.Id)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Offer(a, new CreateOffersRequest([bId], [], null)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Offer(b, new CreateOffersRequest([aId], [mesh.Id], null)));     // not B's
        Assert.Equal(HttpStatusCode.NotFound, await Offer(b, new CreateOffersRequest([aId], null, theirs.Id)));
        Assert.Equal(HttpStatusCode.NotFound, await Offer(a, new CreateOffersRequest([aId], [mesh.Id], null)));             // not to oneself

        var offer = (await a.PostJson<CreateOffersResponse>("/v1/offers", new CreateOffersRequest([bId], [mesh.Id], null))).Offers.Single();
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/offers/{offer.Id}/accept", JsonContent(new AcceptOfferRequest(theirs.Id)))).StatusCode);
        await b.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("Not a texture", AssetKind.Texture, mesh.Hash, 10, "image/png", 1, new Orientation(0, 0, 0), null));
        Assert.Equal(HttpStatusCode.Conflict, (await b.PostAsync($"/v1/offers/{offer.Id}/accept", JsonContent(new AcceptOfferRequest(null)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/v1/shared-assets")).StatusCode);                          // the everyone listing is gone
        Assert.False((await a.PostAsync("/v1/assets/share", JsonContent(new BulkAssetsRequest([mesh.Id])))).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/v1/offers/received")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/v1/offers/players")).StatusCode);
    }

    [Fact]
    public async Task The_daily_cap_is_429_with_retry_after()
    {
        using var host = new ManagementFactory();
        var (aId, bId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        using var factory = host.WithWebHostBuilder(w => w.ConfigureServices(s =>
            s.Replace(ServiceDescriptor.Singleton(new OfferLimits(PerDay: 1, OpenPerRecipient: 10)))));
        var accounts = factory.Services.GetRequiredService<Tabletop.Management.Identity.IAccountRepository>();
        await accounts.Insert(aId, "A"); await accounts.Insert(bId, "B");
        HttpClient Client(Guid id) { var c = factory.CreateClient(); c.DefaultRequestHeaders.Authorization = new("Bearer", TestKeys.Mint(id)); return c; }
        var (a, b) = (Client(aId), Client(bId));
        host.Rows.SeedProperties("sha256:offers-cap", "GF", "grimdark-future");
        var game = await a.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Cap", "sha256:offers-cap", SchedulerShape.PlayerTurn, Enforcement.Silent, false));
        await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData([1, 2, 3]));
        var created = await a.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("x", AssetKind.Miniature, hash, 3, "model/gltf-binary", 1, new Orientation(0, 0, 0), Base.Circle(1)));
        lock (host.Rows.Gate) host.Rows.Blobs[(aId, hash)].Uploaded = true;

        await a.PostJson<CreateOffersResponse>("/v1/offers", new CreateOffersRequest([bId], [created.Asset.Id], null));
        var refused = await a.PostAsync("/v1/offers", JsonContent(new CreateOffersRequest([bId], [created.Asset.Id], null)));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.InRange(int.Parse(refused.Headers.GetValues("Retry-After").Single()), 23 * 3600, 24 * 3600);
    }

    private static HttpContent JsonContent(object body) => System.Net.Http.Json.JsonContent.Create(body, options: ContentJson.Options);
}
