using System.Net;
using Dapper;
using Npgsql;
using Tabletop.Content;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Games;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class ManifestTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public ManifestTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task Participants_get_every_uploaded_asset_in_play_whoever_owns_it()
    {
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        await c.ExecuteAsync("insert into management.properties (content_hash, name, game_rules) values ('sha256:m', 'GF', 'grimdark-future') on conflict do nothing");
        var (aId, bId, outsiderId) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "A"); var b = await _f.AuthenticatedClient(bId, "B"); var outsider = await _f.AuthenticatedClient(outsiderId, "O");
        var game = await a.PostJson<GameInfo>("/v1/games", new CreateGameRequest("M", "sha256:m", SchedulerShape.PlayerTurn, Enforcement.Silent, false));
        await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });

        async Task<AssetDto> Upload(HttpClient who, byte[] glb, AssetKind kind, string name)
        {
            var req = new CreateAssetRequest(name, kind, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, kind == AssetKind.Texture ? "image/png" : "model/gltf-binary", 1, new(0, 0, 0), kind == AssetKind.Miniature ? Tabletop.Geometry.Base.Circle(1.26) : null);
            var created = await who.PostJson<CreateAssetResponse>("/v1/assets", req);
            using var http = new HttpClient();
            await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new(req.ContentType) } });
            return (await who.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { })).Asset;
        }
        var aMesh = await Upload(a, TestGlb.Build((3, null, true)), AssetKind.Miniature, "a-mesh");
        var bMesh = await Upload(b, TestGlb.Build((6, null, true)), AssetKind.Miniature, "b-mesh");
        var texture = await Upload(b, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4], AssetKind.Texture, "board");
        var unconfirmed = await a.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("pending", AssetKind.Miniature, new string('a', 64), 10, "model/gltf-binary", 1, new(0, 0, 0), Tabletop.Geometry.Base.Circle(1)));

        var aArmy = await a.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        var aUnit = aArmy.Units[0];
        await a.PatchJson<Army>($"/v1/armies/{aArmy.Id}", new PatchArmyRequest(null, [new UnitPatch(aUnit.Id, aMesh.Id)], [new SlotPatch(aUnit.Models[1].Id, unconfirmed.Asset.Id, false, null, null)]));
        var bArmy = await b.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        var bSnapshot = ArmySnapshot.Of(bArmy with { Units = [bArmy.Units[0] with { Models = [bArmy.Units[0].Models[0] with { AssetId = bMesh.Id }] }] }, Guid.CreateVersion7());

        // What the game server writes while the game is in lobby/setup: A's pick, B's frozen snapshot, the draft with B's texture.
        await c.ExecuteAsync("update management.participant set army_id = @army where game_id = @game and account_id = @acct", new { army = aArmy.Id, game = game.Id, acct = aId });
        await c.ExecuteAsync("update management.participant set army_snapshot = @snap::jsonb where game_id = @game and account_id = @acct", new { snap = ContentJson.Serialize(bSnapshot), game = game.Id, acct = bId });
        await c.ExecuteAsync("insert into management.table_state (id, game_id, width, length, texture_asset_id) values (@id, @game, 72, 48, @tex)", new { id = Guid.CreateVersion7(), game = game.Id, tex = texture.Id });

        await b.PatchJson<AssetDto>($"/v1/assets/{bMesh.Id}", new PatchAssetRequest(null, 2.5, new Tabletop.Geometry.Orientation(90, 0, 0), null));   // B's import corrections

        var manifest = await a.GetJson<Manifest>($"/v1/games/{game.Id}/assets");
        Assert.Equal(new HashSet<Guid> { aMesh.Id, bMesh.Id, texture.Id }, manifest.Assets.Select(m => m.AssetId).ToHashSet());   // the unconfirmed one is absent
        var bEntry = manifest.Assets.Single(m => m.AssetId == bMesh.Id);
        Assert.Equal((2.5, new Tabletop.Geometry.Orientation(90, 0, 0)), (bEntry.Scale, bEntry.Orientation));                  // A can draw B's mesh as B corrected it
        var aEntry = manifest.Assets.Single(m => m.AssetId == aMesh.Id);
        Assert.Equal((1.0, new Tabletop.Geometry.Orientation(0, 0, 0)), (aEntry.Scale, aEntry.Orientation));                   // untouched: the row's defaults
        using var http = new HttpClient();
        Assert.Equal(TestGlb.Build((6, null, true)), await http.GetByteArrayAsync(manifest.Assets.Single(m => m.AssetId == bMesh.Id).Url));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/v1/games/{game.Id}/assets")).StatusCode);
    }
}
