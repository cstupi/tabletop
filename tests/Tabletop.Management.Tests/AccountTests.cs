using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
    }

    [Fact]
    public async Task Deletion_removes_content_blobs_every_game_played_in_and_the_stytch_user()
    {
        await using var db = new NpgsqlConnection(_f.Database.ConnectionString);
        await db.ExecuteAsync("insert into management.properties (content_hash, name, game_rules) values ('sha256:d', 'GF', 'grimdark-future') on conflict do nothing");
        var (meId, otherId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var me = await _f.AuthenticatedClient(meId, "Me"); var other = await _f.AuthenticatedClient(otherId, "Other");
        _f.Stytch.Users["me@example.com"] = new FakeStytch.User { Email = "me@example.com", ExternalId = meId.ToString() };

        var glb = TestGlb.Build((3, null, true));
        var created = await me.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("m", AssetKind.Miniature, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, "model/gltf-binary", 1, new(0, 0, 0), Tabletop.Geometry.Base.Circle(1.26)));
        using (var http = new HttpClient()) await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
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
        await db.ExecuteAsync("insert into game.game_event (game_id, seq, kind, payload) values (@id, 1, 'turn_started', '{}')", new { id = theirs.Id });

        Assert.Equal(HttpStatusCode.NoContent, (await me.DeleteAsync("/v1/account")).StatusCode);

        Assert.Equal(0, await db.ExecuteScalarAsync<int>("select count(*) from management.account where id = @meId", new { meId }));
        foreach (var table in new[] { "asset", "blob", "army", "folder", "table_state" })
            Assert.Equal(0, await db.ExecuteScalarAsync<int>($"select count(*) from management.{table} where owner_id = @meId", new { meId }));
        Assert.Null(await _f.Services.GetRequiredService<IBlobStore>().Size(IBlobStore.Key(meId, created.Asset.Hash)));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("select count(*) from management.game where id in (@a, @b, @c)", new { a = mine.Id, b = theirs.Id, c = left.Id }));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("select count(*) from game.game_event where game_id = @id", new { id = theirs.Id }));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("select count(*) from management.game where id = @id", new { id = untouched.Id }));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("select count(*) from management.account where id = @otherId", new { otherId }));
        Assert.Contains(meId.ToString(), _f.Stytch.Deleted);
        Assert.Equal(HttpStatusCode.Unauthorized, (await me.GetAsync("/v1/account")).StatusCode);
    }
}
