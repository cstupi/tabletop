using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.Content;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;
using Tabletop.Management.Games;
using Tabletop.Management.Identity;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class AccountTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public AccountTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task Rename()
    {
        var id = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(id, "Old");
        Assert.Equal(new AccountRow(id, "New"), await c.PatchJson<AccountRow>("/v1/account", new RenameRequest("New")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchAsJsonAsync("/v1/account", new RenameRequest("  "), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchAsJsonAsync("/v1/account", new RenameRequest(new string('n', 65)), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchAsJsonAsync("/v1/account", new RenameRequest("ev\u202Eil"), ContentJson.Options)).StatusCode);
        Assert.Equal("New", (await c.GetJson<AccountRow>("/v1/account")).Name);
    }

    [Fact]
    public async Task Deletion_removes_content_blobs_the_games_it_hosted_and_the_stytch_user_and_leaves_the_games_it_joined()
    {
        var rows = _f.Rows;
        rows.SeedProperties("sha256:d", "GF", "grimdark-future");
        var (meId, otherId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var me = await _f.AuthenticatedClient(meId, "Me"); var other = await _f.AuthenticatedClient(otherId, "Other");
        _f.Stytch.Users["me@example.com"] = new FakeStytch.User { Email = "me@example.com", ExternalId = meId.ToString() };

        var glb = TestGlb.Build((3, null, true));
        var created = await me.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("m", AssetKind.Miniature, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, "model/gltf-binary", 1, new(0, 0, 0), Tabletop.Geometry.Base.Circle(1.26)));
        using (var http = _f.Blobs.Client()) await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        await me.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { });
        await me.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        await me.PostJson<FolderDto>("/v1/folders", new FolderRequest("F", null));
        await me.PostJson<TableState>("/v1/layouts", LayoutTests.Sample());
        var mine = await me.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Mine", "sha256:d", SchedulerShape.PlayerTurn, Enforcement.Warn, false));
        var left = await me.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Left", "sha256:d", SchedulerShape.PlayerTurn, Enforcement.Warn, false));
        await other.PostJson<GameInfo>($"/v1/games/{left.Id}/join", new { });                        // so the game outlives my leaving
        Assert.Equal(HttpStatusCode.NoContent, (await me.PostAsync($"/v1/games/{left.Id}/leave", null)).StatusCode);   // created_by me, no participant row of mine
        var theirs = await other.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Theirs", "sha256:d", SchedulerShape.PlayerTurn, Enforcement.Warn, false));
        await me.PostJson<GameInfo>($"/v1/games/{theirs.Id}/join", new { });
        var untouched = await other.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Untouched", "sha256:d", SchedulerShape.PlayerTurn, Enforcement.Warn, false));
        var abandoned = await other.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Abandoned", "sha256:d", SchedulerShape.PlayerTurn, Enforcement.Warn, false));
        await me.PostJson<GameInfo>($"/v1/games/{abandoned.Id}/join", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsync($"/v1/games/{abandoned.Id}/leave", null)).StatusCode);   // I am its last participant
        rows.SeedGameEvent(mine.Id, 1);
        rows.SeedGameEvent(theirs.Id, 1);

        Assert.Equal(HttpStatusCode.NoContent, (await me.DeleteAsync("/v1/account")).StatusCode);

        lock (rows.Gate)
        {
            Assert.False(rows.Accounts.ContainsKey(meId));
            Assert.DoesNotContain(rows.Assets.Values, a => a.Owner == meId);
            Assert.DoesNotContain(rows.Blobs.Keys, b => b.Owner == meId);
            Assert.DoesNotContain(rows.Armies.Values, a => a.Owner == meId);
            Assert.DoesNotContain(rows.Folders.Values, f => f.Owner == meId);
            Assert.DoesNotContain(rows.Layouts.Values, l => l.Owner == meId);
            Assert.Empty(rows.Games.Keys.Intersect([mine.Id, left.Id]));
            Assert.DoesNotContain(rows.GameEvents, e => e.GameId == mine.Id);
            Assert.Contains(theirs.Id, rows.Games.Keys);
            Assert.Contains(rows.GameEvents, e => e.GameId == theirs.Id);
            Assert.Equal([otherId], rows.Participants.Where(p => p.GameId == theirs.Id).Select(p => p.AccountId));
            Assert.Equal(GameStatus.Lobby, rows.Games[theirs.Id].Status);
            Assert.DoesNotContain(rows.Participants, p => p.AccountId == meId);
            Assert.Equal(GameStatus.Ended, rows.Games[abandoned.Id].Status);   // the last leave ends it, as /leave does
            Assert.Contains(untouched.Id, rows.Games.Keys);
            Assert.True(rows.Accounts.ContainsKey(otherId));
        }
        Assert.Null(await _f.Blobs.Size(IBlobStore.Key(meId, created.Asset.Hash)));
        Assert.Contains(meId.ToString(), _f.Stytch.Deleted);
        var unauth = await me.GetAsync("/v1/account");
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);
        Assert.Equal("application/problem+json", unauth.Content.Headers.ContentType?.MediaType);
    }
}
