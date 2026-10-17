using System.Net;
using Microsoft.Extensions.DependencyInjection;
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
        _f.Rows.SeedProperties("sha256:m", "GF", "grimdark-future");
        var (aId, bId, outsiderId) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "A"); var b = await _f.AuthenticatedClient(bId, "B"); var outsider = await _f.AuthenticatedClient(outsiderId, "O");
        var game = await a.PostJson<GameInfo>("/v1/games", new CreateGameRequest("M", "sha256:m", SchedulerShape.PlayerTurn, Enforcement.Silent, false));
        await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });

        async Task<AssetDto> Upload(HttpClient who, byte[] glb, AssetKind kind, string name)
        {
            var req = new CreateAssetRequest(name, kind, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, kind == AssetKind.Texture ? "image/png" : "model/gltf-binary", 1, new(0, 0, 0), kind == AssetKind.Miniature ? Tabletop.Geometry.Base.Circle(1.26) : null);
            var created = await who.PostJson<CreateAssetResponse>("/v1/assets", req);
            using var http = _f.Blobs.Client();
            await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new(req.ContentType) } });
            return (await who.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { })).Asset;
        }
        var aMesh = await Upload(a, TestGlb.Build((3, null, true)), AssetKind.Miniature, "a-mesh");
        var bMesh = await Upload(b, TestGlb.Build((6, null, true)), AssetKind.Miniature, "b-mesh");
        var texture = await Upload(b, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4], AssetKind.Texture, "board");
        var unconfirmed = await a.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("pending", AssetKind.Miniature, new string('a', 64), 10, "model/gltf-binary", 1, new(0, 0, 0), Tabletop.Geometry.Base.Circle(1)));

        var aArmy = await a.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        var aUnit = aArmy.Units[0];
        await a.PatchJson<Army>($"/v1/armies/{aArmy.Id}", new PatchArmyRequest(null, [new UnitPatch(aUnit.Id, aMesh.Id)], null));
        lock (_f.Rows.Gate)                 // the API refuses an unconfirmed asset; the row is written directly, as a slot whose asset lost its bytes
        {
            var row = _f.Rows.Armies[aArmy.Id];
            row.Army = row.Army with { Units = row.Army.Units.Select(un => un with { Models = un.Models.Select(m => m.Id == aUnit.Models[1].Id ? m with { AssetId = unconfirmed.Asset.Id } : m).ToList() }).ToList() };
        }
        var bArmy = await b.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        var bSnapshot = ArmySnapshot.Of(bArmy with { Units = [bArmy.Units[0] with { Models = [bArmy.Units[0].Models[0] with { AssetId = bMesh.Id }] }] }, Guid.CreateVersion7());

        // What the game server writes while the game is in lobby/setup: A's pick, B's frozen snapshot, the draft with B's texture.
        _f.Rows.PickArmy(game.Id, aId, aArmy.Id);
        _f.Rows.FreezeArmy(game.Id, bId, bSnapshot);
        _f.Rows.SeedGameTable(game.Id, new TableState(Guid.CreateVersion7(), null, new Tabletop.Geometry.TableSize(72, 48), VisualRef.OfAsset(texture.Id), [], [], []));

        await b.PatchJson<AssetDto>($"/v1/assets/{bMesh.Id}", new PatchAssetRequest(null, 2.5, new Tabletop.Geometry.Orientation(90, 0, 0), null));   // B's import corrections

        var manifest = await a.GetJson<Manifest>($"/v1/games/{game.Id}/assets");
        Assert.Equal(new HashSet<Guid> { aMesh.Id, bMesh.Id, texture.Id }, manifest.Assets.Select(m => m.AssetId).ToHashSet());   // the unconfirmed one is absent
        var bEntry = manifest.Assets.Single(m => m.AssetId == bMesh.Id);
        Assert.Equal((2.5, new Tabletop.Geometry.Orientation(90, 0, 0)), (bEntry.Scale, bEntry.Orientation));                  // A can draw B's mesh as B corrected it
        var aEntry = manifest.Assets.Single(m => m.AssetId == aMesh.Id);
        Assert.Equal((1.0, new Tabletop.Geometry.Orientation(0, 0, 0)), (aEntry.Scale, aEntry.Orientation));                   // untouched: the row's defaults
        using var http = _f.Blobs.Client();
        Assert.Equal(TestGlb.Build((6, null, true)), await http.GetByteArrayAsync(manifest.Assets.Single(m => m.AssetId == bMesh.Id).Url));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/v1/games/{game.Id}/assets")).StatusCode);
    }

    [Fact]
    public async Task Assets_owned_by_a_non_participant_are_never_presigned()
    {
        _f.Rows.SeedProperties("sha256:m2", "GF", "grimdark-future");
        var (hostId, strangerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var host = await _f.AuthenticatedClient(hostId, "H");
        await _f.AuthenticatedClient(strangerId, "S");
        var game = await host.PostJson<GameInfo>("/v1/games", new CreateGameRequest("M2", "sha256:m2", SchedulerShape.PlayerTurn, Enforcement.Silent, false));
        AssetDto Uploaded(Guid owner, string name)
        {
            var glb = TestGlb.Build((3 + name.Length * 3, null, true));
            var dto = _f.Services.GetRequiredService<IAssetRepository>().Create(owner, new CreateAssetRequest(name, AssetKind.Terrain,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, "model/gltf-binary", 1, new(0, 0, 0), null)).Result;
            _f.Services.GetRequiredService<IAssetRepository>().MarkUploaded(owner, dto.Hash).Wait();
            return dto;
        }
        var mine = Uploaded(hostId, "mine");
        var theirs = Uploaded(strangerId, "theirs-asset");
        // A table naming another account's asset id, as a setup edit could: only the participant's asset is presigned.
        TerrainPlacement At(Guid asset) => new(Guid.CreateVersion7(), VisualRef.OfAsset(asset), new Tabletop.Geometry.Point3(10, 10, 0), new Tabletop.Geometry.Yaw(0), 1, []);
        _f.Rows.SeedGameTable(game.Id, new TableState(Guid.CreateVersion7(), null, new Tabletop.Geometry.TableSize(72, 48), VisualRef.OfBuiltin("grass"), [At(mine.Id), At(theirs.Id)], [], []));
        var manifest = await host.GetJson<Manifest>($"/v1/games/{game.Id}/assets");
        Assert.Equal([mine.Id], manifest.Assets.Select(a => a.AssetId));
    }
}
