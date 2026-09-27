# Task 13: The account — rename, and total deletion

**Files:**
- Create: `server/management/Identity/AccountDeleter.cs`
- Modify: `server/management/Identity/AccountEndpoints.cs`, `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/AccountTests.cs`

**Interfaces:**
- Consumes: `AccountRepository`, `GameRepository.Delete`, `IBlobStore.Delete`, `StytchClient.DeleteUser`.
- Produces: `RenameRequest(string Name)`; `AccountDeleter(Database, GameRepository, IBlobStore, StytchClient).Delete(Guid accountId) : Task<bool>`; `PATCH /v1/account {name} → AccountRow`; `DELETE /v1/account → 204`.
- Order of deletion, and why: (1) read the account's blob keys; (2) read every game the account touches — the `game_id`s it participates in **and** the `game.created_by` rows it created — and delete each one; its rows cascade in the management schema and `game.game_event` is deleted explicitly, because no FK crosses the schema boundary; (3) delete the account row, cascading armies, assets, blobs, folders and layouts; (4) delete the S3 objects; (5) delete the Stytch user by `external_id`. Database first so that a Stytch failure leaves no orphaned content; a Stytch `404` is ignored; another Stytch failure answers `502` after the content is already gone, and logging in again recreates an empty account with the same id (`Sessions.Complete` inserts on conflict).
- **`created_by` is why the game set is a union, not just the participant rows.** `game.created_by` is `not null references account(id)` with no `on delete` action (contracts plan, `001_schema.sql`), and `/leave` deletes the participant row while the game survives — so an account that created a game and then left is still named by `created_by`, and `delete from account` would fail on that FK. `schema.md` → A game records its creator asserts *"Account deletion already destroys every game the account played in, so a deleted creator leaves no orphan"*; the union is what makes that true.

Spec: accounts → Deletion is total ("destroys its armies, assets, blobs, folders and layouts, and deletes every game it participated in — the game record and its event log — and then the account row"; "The collateral is real and accepted"); `schema.md` → Deleting an account is application logic ("a deliberate multi-step operation in the delete path rather than a constraint or a trigger"); management API → The rest of the surface (`PATCH /v1/account name`, `DELETE /v1/account the full cascade`).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Dapper;
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
```

- [ ] **Step 2: Run; expect compile errors. Write `AccountDeleter.cs`:**

```csharp
using Dapper;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Games;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Identity;

/// <summary>Deletion is total. Cascades handle what hangs below the account; games hang above it and are deleted one by one, log included.</summary>
public sealed class AccountDeleter(Database db, GameRepository games, IBlobStore blobs, StytchClient stytch, ILogger<AccountDeleter> log)
{
    public async Task<bool> Delete(Guid accountId)
    {
        IReadOnlyList<string> keys; IReadOnlyList<Guid> gameIds;
        await using (var c = await db.Open())
        {
            if (!await c.ExecuteScalarAsync<bool>("select exists(select 1 from account where id = @accountId)", new { accountId })) return false;
            keys = (await c.QueryAsync<string>("select s3_key from blob where owner_id = @accountId", new { accountId })).ToList();
            // Games it plays in, plus games it created and has since left: game.created_by has no on-delete action, so a game it created must go too.
            gameIds = (await c.QueryAsync<Guid>(@"select game_id from participant where account_id = @accountId
                                                 union select id from game where created_by = @accountId", new { accountId })).ToList();
        }
        foreach (var gameId in gameIds) await games.Delete(gameId);
        await using (var c = await db.Open())
            await c.ExecuteAsync("delete from account where id = @accountId", new { accountId });
        if (keys.Count > 0) await blobs.Delete(keys);
        try { await stytch.DeleteUser(accountId.ToString()); }
        catch (StytchException e) when (e.Status == 404) { log.LogWarning("Stytch had no user for account {AccountId}", accountId); }
        return true;
    }
}
```

- [ ] **Step 3: Extend `AccountEndpoints.cs`**

```csharp
public sealed record RenameRequest(string Name);

// inside MapAccount, after the GET:
group.MapPatch("", async (ClaimsPrincipal user, RenameRequest r, AccountRepository accounts) =>
{
    if (string.IsNullOrWhiteSpace(r.Name)) return Problems.Unprocessable("name is required");
    var id = CurrentAccount.Id(user);
    return await accounts.Rename(id, r.Name.Trim()) ? Results.Ok(await accounts.Get(id)) : Results.Unauthorized();
});
group.MapDelete("", async (ClaimsPrincipal user, AccountDeleter deleter) =>
{
    try { return await deleter.Delete(CurrentAccount.Id(user)) ? Results.NoContent() : Results.Unauthorized(); }
    catch (StytchException e) { return Sessions.Problem(e); }
});
```

- [ ] **Step 4: Wire `Program.cs`** (`AddSingleton<AccountDeleter>()`); run `dotnet test`; expect at least 64 passed. Commit: `Rename the account, and delete it with everything it owns and every game it played in`.
