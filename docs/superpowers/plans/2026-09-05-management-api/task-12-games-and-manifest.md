# Task 12: Properties, games, the public browser and the manifest

**Files:**
- Create: `server/management/Games/GameDtos.cs`, `server/management/Games/GameRepository.cs`, `server/management/Games/GameEndpoints.cs`, `server/management/Games/PropertiesEndpoint.cs`, `server/management/Games/ManifestEndpoint.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/GameTests.cs`, `tests/Tabletop.Management.Tests/ManifestTests.cs`

**Interfaces:**
- Consumes: `Tabletop.Protocol.GameInfo`, `Team`, `Participant`, `GameStatus`, `SchedulerShape`, `Enforcement`; `Tabletop.Content.ArmySnapshot`, `ContentJson`; `IBlobStore`; `LayoutRepository.ReadTree` is not needed — the manifest reads asset ids straight from the rows.
- Produces: `PropertiesSummary(string ContentHash, string Name, string GameRules)`; `CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps)`; `GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants)`; `ManifestEntry(Guid AssetId, Content.Assets.AssetKind Kind, string Hash, Uri Url, double Scale, Geometry.Orientation Orientation)` (scale and orientation are the owner's import corrections from the `asset` row — the client draws another participant's mesh from the manifest alone, since `GET /v1/assets/{id}` is owner-only); `Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt)`; `ManifestRow(Guid AssetId, Guid OwnerId, string Kind, string Hash, double Scale, double Pitch, double Yaw, double Roll)`; `GameRepository(Database)` with `ListProperties()`, `Create(creator, CreateGameRequest) : GameInfo?` (null: unknown hash), `List(Keyset) : IReadOnlyList<GameSummary>`, `Get(id) : GameInfo?`, `IsParticipant(id, accountId)`, `Join(id, accountId) : GameInfo?`, `Leave(id, accountId) : bool` (ends the game when the last participant leaves), `End(id) : bool`, `Delete(id) : bool` (game rows and `game.game_event`), `ManifestAssets(id) : IReadOnlyList<ManifestRow>`; endpoints `GET /v1/properties → PropertiesSummary[]`; `GET /v1/games?limit&after → Page<GameSummary>` (every status); `POST /v1/games → 201 GameInfo`; `GET /v1/games/{id} → GameInfo`; `POST /v1/games/{id}/join → GameInfo`; `POST /v1/games/{id}/leave → 204`; `POST /v1/games/{id}/end → 204`; `DELETE /v1/games/{id} → 204`; `GET /v1/games/{id}/assets → Manifest`.
- Rows management writes here: `game` insert (`status = 'lobby'`, `game_rules` copied from the chosen `properties` row, `rng_seed` generated here, `created_by`, `created_at`); `participant` insert (`team_id null`, `army_id null`, `army_snapshot null`) on create and join, delete on leave; `game.status = 'ended'` on `/end` and on the last leave; whole-game delete. Management never inserts `team`, never sets `participant.team_id/army_id/army_snapshot`, never writes a `table_state` with `game_id` — the game server does, while status is `lobby` or `setup`.
- **`rng_seed` is generated here, in `Create`, and never written again.** It is not in `CreateGameRequest`: the client does not choose a game's dice. The value is `Random.Shared.NextInt64()` — any `bigint` is valid and the seed is not a secret, since editing a roll is already a first-class command — and it goes into the same `insert` as the rest of the configuration, inside the same transaction, so a game row cannot exist without one. `management.game.rng_seed` is `bigint not null` with **no default** (contracts plan Task 15), so an insert that forgets it fails rather than quietly seeding every such game alike. `Get` reads it back into `GameInfo.RngSeed`, which is how it reaches the game server at hydration and the interpreter after that. No endpoint updates it: not `/join`, not `/leave`, not `/end`, and there is no configuration `PATCH` on a game — the game server rewrites the other configuration columns on `set_configuration` and leaves this one alone (simulation design → The seed is stored, so a run sets it).
- **`POST /v1/games/{id}/join` joins the game. It does not take a team.** The two are separate operations on separate paths, and conflating them is what has kept the question alive. Joining is this endpoint and it inserts a `participant` row with `team_id null`; it is allowed at any status, including `playing` and `ended`, and that is unchanged. Taking a team is a lobby edit on the game server's socket, allowed only while the game is `lobby` or `setup`, and there is no route for it here. So an account that joins a `playing` game gets a row and no team, which is the definition of a spectator, while a player who already had a slot rejoins it by reconnecting rather than by joining again. The canonical statement of the team model is game flow → *Teams are frozen at the flip, and a team is a slot*.
- `/end` and `DELETE` are the creator's alone, and remain so after the creator leaves (ruling 6); `/leave` still needs only a participant row. `GET /v1/games/{id}` needs only a login: games are public. The manifest is the one cross-owner read: for every participant it takes the slots' `asset_id` from the picked army (`participant.army_id`, before the snapshot) or from the frozen `army_snapshot` (after), plus the game's `table_state` texture, terrain `asset_id`s and marker `visual_asset_id`s, joins `asset`/`blob` where `uploaded`, and presigns one GET per asset for 60 minutes. Nothing is copied and no row is created.

Spec: management API → The game manifest ("Authorised by being a participant. Returns presigned GET URLs for every mesh and texture in play, whoever owns them"), The rest of the surface (games; `POST /v1/games name, properties_hash, scheduler_shape, enforcement, waits_between_steps`; `GET /v1/properties`); accounts → A game is a lobby ("Games are public … anyone logged in may join"; "creates the game and the participant row that puts them in it — on no team until they pick one"), Ending is a flag ("A game ends when someone ends it explicitly, or when the last participant leaves"), Still open ("joinable at every status"); setup phase → Joining is allowed at any status, The lifecycle (`game.created_by`); whole-design review → Five rulings, 4 and 5; `schema.md` → GAME, TEAM, PARTICIPANT, "Deleting an account is application logic" (the same cross-schema delete, per game); simulation design → The seed is stored, so a run sets it, and `schema.md` → GAME (`game.rng_seed`, written once here).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Dapper;
using Npgsql;
using Tabletop.Management.Games;
using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class GameTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public GameTests(ManagementFactory f) => _f = f;
    const string Hash = "sha256:test-properties";

    async Task Seed()
    {
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        await c.ExecuteAsync("insert into management.properties (content_hash, name, game_rules) values (@Hash, 'Grimdark Future core', 'grimdark-future') on conflict do nothing", new { Hash });
    }
    static CreateGameRequest Friday() => new("Friday", Hash, SchedulerShape.AlternatingActivation, Enforcement.Warn, false);

    [Fact]
    public async Task Properties_lists_the_pinned_sets_and_create_copies_game_rules_from_the_chosen_one()
    {
        await Seed();
        var creator = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(creator, "Clay");
        Assert.Contains(await c.GetJson<List<PropertiesSummary>>("/v1/properties"), p => p == new PropertiesSummary(Hash, "Grimdark Future core", "grimdark-future"));
        var game = await c.PostJson<GameInfo>("/v1/games", Friday());
        Assert.Equal(("Friday", GameStatus.Lobby, creator, "grimdark-future", Hash, SchedulerShape.AlternatingActivation, Enforcement.Warn, false),
            (game.Name, game.Status, game.CreatedBy, game.GameRules, game.PropertiesHash, game.SchedulerShape, game.Enforcement, game.WaitsBetweenSteps));
        Assert.Empty(game.Teams);
        Assert.Equal([(creator, "Clay", (Guid?)null, (Guid?)null)], game.Participants.Select(p => (p.AccountId, p.Name, p.TeamId, p.ArmyId)));
        Assert.NotEqual(0L, game.RngSeed);                                                                      // generated at creation, not defaulted: a 0 seed would mean shared dice
        Assert.Equal(game.RngSeed, (await c.GetJson<GameInfo>($"/v1/games/{game.Id}")).RngSeed);                 // stored, and read back unchanged
        Assert.NotEqual(game.RngSeed, (await c.PostJson<GameInfo>("/v1/games", Friday())).RngSeed);              // per game, not per properties set
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/games", Friday() with { PropertiesHash = "sha256:nope" }, Tabletop.Content.ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Browser_is_public_join_is_open_at_any_status_and_the_last_leave_ends_the_game()
    {
        await Seed();
        var a = await _f.AuthenticatedClient(Guid.CreateVersion7(), "A");
        var b = await _f.AuthenticatedClient(Guid.CreateVersion7(), "B");
        var game = await a.PostJson<GameInfo>("/v1/games", Friday());
        Assert.Contains((await b.GetJson<Page<GameSummary>>("/v1/games?limit=200")).Items, g => g.Id == game.Id && g.Participants == 1);
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/v1/games/{game.Id}/end", null)).StatusCode);
        var joined = await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        Assert.Equal(GameStatus.Ended, joined.Status);
        Assert.Equal(2, joined.Participants.Count);
        Assert.Equal(2, (await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { })).Participants.Count);   // idempotent
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/v1/games/{game.Id}")).StatusCode);         // a participant, not the creator
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/games/{game.Id}/end", null)).StatusCode);  // same rule on both routes
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(GameStatus.Ended, (await a.GetJson<GameInfo>($"/v1/games/{game.Id}")).Status);
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        await c.ExecuteAsync("insert into game.game_event (game_id, seq, kind, payload) values (@id, 1, 'turn_started', '{}')", new { id = game.Id });
        var again = await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/games/{game.Id}")).StatusCode);         // the creator, who left, still may
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("select count(*) from game.game_event where game_id = @id", new { id = game.Id }));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/games/{game.Id}")).StatusCode);
    }

    [Fact]
    public async Task Two_concurrent_last_leaves_still_end_the_game()
    {
        await Seed();
        var a = await _f.AuthenticatedClient(Guid.CreateVersion7(), "A");
        var b = await _f.AuthenticatedClient(Guid.CreateVersion7(), "B");
        var game = await a.PostJson<GameInfo>("/v1/games", Friday());
        await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        await Task.WhenAll(a.PostAsync($"/v1/games/{game.Id}/leave", null), b.PostAsync($"/v1/games/{game.Id}/leave", null));
        Assert.Equal(GameStatus.Ended, (await a.GetJson<GameInfo>($"/v1/games/{game.Id}")).Status);
    }
}
```

```csharp
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
```

- [ ] **Step 2: Run; expect compile errors. Write `GameDtos.cs` and `GameRepository.cs`:**

```csharp
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Protocol;

namespace Tabletop.Management.Games;

public sealed record PropertiesSummary(string ContentHash, string Name, string GameRules);
public sealed record CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps);
public sealed record GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants);
/// <summary>One asset in play. Scale and Orientation are the owner's import corrections: the client cannot read another participant's asset row, so they ride here.</summary>
public sealed record ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation);
public sealed record Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt);
/// <summary>What the manifest query returns per asset, before presigning.</summary>
public sealed record ManifestRow(Guid AssetId, Guid OwnerId, string Kind, string Hash, double Scale, double Pitch, double Yaw, double Roll);
```

```csharp
using Dapper;
using Tabletop.Content;
using Tabletop.Management.Persistence;
using Tabletop.Protocol;

namespace Tabletop.Management.Games;

public sealed class GameRepository(Database db)
{
    private sealed record GameRow(Guid Id, string Name, string Status, string GameRules, string PropertiesHash, string SchedulerShape, string Enforcement, bool WaitsBetweenSteps, long RngSeed, Guid CreatedBy);
    private sealed record ParticipantRow(Guid Id, Guid AccountId, string Name, Guid? TeamId, Guid? ArmyId);
    // CreatedAt comes back as DateTime (Npgsql's CLR type for timestamptz), not DateTimeOffset: Dapper picks a
    // constructor by the reader's exact field type, so the row type must match it and GameSummary's DateTimeOffset
    // is built afterwards.
    private sealed record SummaryRow(Guid Id, string Name, string Status, Guid CreatedBy, DateTime CreatedAt, int Participants);
    private static readonly IReadOnlyDictionary<string, SchedulerShape> Shapes = new Dictionary<string, SchedulerShape> { ["player-turn"] = SchedulerShape.PlayerTurn, ["alternating-activation"] = SchedulerShape.AlternatingActivation };
    private static string Wire(SchedulerShape s) => Shapes.First(kv => kv.Value == s).Key;

    public async Task<IReadOnlyList<PropertiesSummary>> ListProperties()
    {
        await using var c = await db.Open();
        return (await c.QueryAsync<PropertiesSummary>("select content_hash as ContentHash, name as Name, game_rules as GameRules from properties order by game_rules, name")).ToList();
    }

    public async Task<GameInfo?> Create(Guid creator, CreateGameRequest r)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var gameRules = await c.ExecuteScalarAsync<string?>("select game_rules from properties where content_hash = @hash", new { hash = r.PropertiesHash }, tx);
        if (gameRules is null) return null;
        var id = Guid.CreateVersion7();
        // The game's dice seed, generated once here and never written again. The column has no default, so this is the only place it can come from.
        var rngSeed = Random.Shared.NextInt64();
        await c.ExecuteAsync(@"insert into game (id, name, status, game_rules, properties_hash, scheduler_shape, enforcement, waits_between_steps, rng_seed, created_by)
            values (@id, @name, 'lobby', @gameRules, @hash, @shape, @enforcement, @waits, @rngSeed, @creator)",
            new { id, name = r.Name, gameRules, hash = r.PropertiesHash, shape = Wire(r.SchedulerShape), enforcement = r.Enforcement.ToString().ToLowerInvariant(), waits = r.WaitsBetweenSteps, rngSeed, creator }, tx);
        await c.ExecuteAsync("insert into participant (id, game_id, account_id) values (@pid, @id, @creator)", new { pid = Guid.CreateVersion7(), id, creator }, tx);
        await tx.CommitAsync();
        return await Get(id);
    }

    public async Task<IReadOnlyList<GameSummary>> List(Http.Keyset page)
    {
        await using var c = await db.Open();
        // A named record, not a ValueTuple: Dapper maps to constructor parameters by column name and cannot map tuple element names.
        var rows = await c.QueryAsync<SummaryRow>(@"select g.id as Id, g.name as Name, g.status as Status, g.created_by as CreatedBy, g.created_at as CreatedAt,
            (select count(*) from participant p where p.game_id = g.id)::int as Participants
            from game g where (@after::uuid is null or g.id < @after::uuid) order by g.id desc limit @limit", new { after = page.After, limit = page.Limit });
        return rows.Select(r => new GameSummary(r.Id, r.Name, Enum.Parse<GameStatus>(r.Status, true), r.CreatedBy, new DateTimeOffset(r.CreatedAt, TimeSpan.Zero), r.Participants)).ToList();
    }

    public async Task<GameInfo?> Get(Guid id)
    {
        await using var c = await db.Open();
        var g = await c.QuerySingleOrDefaultAsync<GameRow>(@"select id as Id, name as Name, status as Status, game_rules as GameRules, properties_hash as PropertiesHash, scheduler_shape as SchedulerShape,
            enforcement as Enforcement, waits_between_steps as WaitsBetweenSteps, rng_seed as RngSeed, created_by as CreatedBy from game where id = @id", new { id });
        if (g is null) return null;
        var teams = (await c.QueryAsync<Team>("select id as Id, ordinal as Ordinal from team where game_id = @id order by ordinal", new { id })).ToList();
        var participants = (await c.QueryAsync<ParticipantRow>(@"select p.id as Id, p.account_id as AccountId, a.name as Name, p.team_id as TeamId, p.army_id as ArmyId
            from participant p join account a on a.id = p.account_id where p.game_id = @id order by p.id", new { id })).Select(p => new Participant(p.Id, p.AccountId, p.Name, p.TeamId, p.ArmyId)).ToList();
        return new GameInfo(g.Id, g.Name, Enum.Parse<GameStatus>(g.Status, true), g.CreatedBy, g.GameRules, g.PropertiesHash, Shapes[g.SchedulerShape], Enum.Parse<Enforcement>(g.Enforcement, true), g.WaitsBetweenSteps, g.RngSeed, teams, participants);
    }

    public async Task<bool> IsParticipant(Guid id, Guid accountId)
    {
        await using var c = await db.Open();
        return await c.ExecuteScalarAsync<bool>("select exists(select 1 from participant where game_id = @id and account_id = @accountId)", new { id, accountId });
    }

    /// <summary>Ending and deleting are the creator's, and stay the creator's after they leave: created_by has no on-delete action and outlives the participant row.</summary>
    public async Task<bool> IsCreator(Guid id, Guid accountId)
    {
        await using var c = await db.Open();
        return await c.ExecuteScalarAsync<bool>("select exists(select 1 from game where id = @id and created_by = @accountId)", new { id, accountId });
    }

    /// <summary>Joining the game is a participant row with no team, at any status; joining twice is the same row (unique (game_id, account_id)).</summary>
    /// <remarks>Taking a team is a game-server lobby edit before play, not this. See the game-flow spec, "Teams are frozen at the flip, and a team is a slot".</remarks>
    public async Task<GameInfo?> Join(Guid id, Guid accountId)
    {
        await using var c = await db.Open();
        await c.ExecuteAsync("insert into participant (id, game_id, account_id) select @pid, @id, @accountId where exists (select 1 from game where id = @id) on conflict (game_id, account_id) do nothing",
            new { pid = Guid.CreateVersion7(), id, accountId });
        return await Get(id);
    }

    /// <summary>
    /// Locks the game row first so two concurrent last-leaves can't both see the other's not-yet-committed
    /// delete under READ COMMITTED and skip ending the game. The lock serializes them: the second leave's
    /// existence check runs only after the first has committed its delete.
    /// </summary>
    public async Task<bool> Leave(Guid id, Guid accountId)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        if (await c.ExecuteScalarAsync<int?>("select 1 from game where id = @id for update", new { id }, tx) is null) return false;
        if (await c.ExecuteAsync("delete from participant where game_id = @id and account_id = @accountId", new { id, accountId }, tx) == 0) return false;
        await c.ExecuteAsync("update game set status = 'ended' where id = @id and not exists (select 1 from participant where game_id = @id)", new { id }, tx);
        await tx.CommitAsync();
        return true;
    }

    public async Task<bool> End(Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("update game set status = 'ended' where id = @id", new { id }) == 1;
    }

    /// <summary>The game's rows cascade in the management schema; the log is a separate delete because no FK crosses the schema boundary.</summary>
    public async Task<bool> Delete(Guid id)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("delete from game.game_event where game_id = @id", new { id }, tx);
        var n = await c.ExecuteAsync("delete from game where id = @id", new { id }, tx);
        await tx.CommitAsync();
        return n == 1;
    }

    public async Task<IReadOnlyList<ManifestRow>> ManifestAssets(Guid id)
    {
        await using var c = await db.Open();
        var ids = new HashSet<Guid>();
        ids.UnionWith(await c.QueryAsync<Guid>(@"select s.asset_id from participant p join unit u on u.army_id = p.army_id join model_slot s on s.unit_id = u.id
            where p.game_id = @id and p.army_snapshot is null and s.asset_id is not null", new { id }));
        foreach (var json in await c.QueryAsync<string>("select army_snapshot::text from participant where game_id = @id and army_snapshot is not null", new { id }))
            ids.UnionWith(ContentJson.Deserialize<ArmySnapshot>(json).Units.SelectMany(u => u.Models).Where(m => m.AssetId is not null).Select(m => m.AssetId!.Value));
        ids.UnionWith(await c.QueryAsync<Guid>(@"select texture_asset_id from table_state where game_id = @id and texture_asset_id is not null
            union select tp.asset_id from terrain_placement tp join table_state t on t.id = tp.table_state_id where t.game_id = @id and tp.asset_id is not null
            union select m.visual_asset_id from marker m join table_state t on t.id = m.table_state_id where t.game_id = @id and m.visual_asset_id is not null", new { id }));
        if (ids.Count == 0) return [];
        return (await c.QueryAsync<ManifestRow>(@"select a.id as AssetId, a.owner_id as OwnerId, a.kind as Kind, a.blob_hash as Hash, a.scale::float8 as Scale,
            a.orientation_pitch::float8 as Pitch, a.orientation_yaw::float8 as Yaw, a.orientation_roll::float8 as Roll
            from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
            where a.id = any(@ids) and b.uploaded", new { ids = ids.ToArray() })).ToList();
    }
}
```

- [ ] **Step 3: Write `PropertiesEndpoint.cs`, `GameEndpoints.cs`, `ManifestEndpoint.cs`**

```csharp
namespace Tabletop.Management.Games;

public static class PropertiesEndpoint
{
    public static void MapProperties(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/properties", async (GameRepository games) => Results.Ok(await games.ListProperties())).RequireAuthorization();
}
```

```csharp
using System.Security.Claims;
using Tabletop.Management.Http;

namespace Tabletop.Management.Games;

public static class GameEndpoints
{
    public static void MapGames(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/games").RequireAuthorization();
        g.MapGet("", async (GameRepository games, int? limit, Guid? after) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<GameSummary>.Of(await games.List(page), page.Limit, x => x.Id));
        });
        g.MapPost("", async (ClaimsPrincipal u, CreateGameRequest r, GameRepository games) =>
            string.IsNullOrWhiteSpace(r.Name) ? Problems.Unprocessable("name is required")
            : await games.Create(CurrentAccount.Id(u), r) is { } game ? Results.Created($"/v1/games/{game.Id}", game) : Problems.Unprocessable($"No Properties set with hash {r.PropertiesHash}. GET /v1/properties lists the pinned sets."));
        g.MapGet("/{id:guid}", async (Guid id, GameRepository games) => await games.Get(id) is { } game ? Results.Ok(game) : Problems.NotFound());
        g.MapPost("/{id:guid}/join", async (ClaimsPrincipal u, Guid id, GameRepository games) => await games.Join(id, CurrentAccount.Id(u)) is { } game ? Results.Ok(game) : Problems.NotFound());
        g.MapPost("/{id:guid}/leave", async (ClaimsPrincipal u, Guid id, GameRepository games) => await games.Leave(id, CurrentAccount.Id(u)) ? Results.NoContent() : Problems.NotFound());
        g.MapPost("/{id:guid}/end", async (ClaimsPrincipal u, Guid id, GameRepository games) =>
            await games.IsCreator(id, CurrentAccount.Id(u)) && await games.End(id) ? Results.NoContent() : Problems.NotFound());
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, GameRepository games) =>
            await games.IsCreator(id, CurrentAccount.Id(u)) && await games.Delete(id) ? Results.NoContent() : Problems.NotFound());
    }
}
```

```csharp
using System.Security.Claims;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Games;

public static class ManifestEndpoint
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(60);

    public static void MapManifest(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/games/{id:guid}/assets", async (ClaimsPrincipal u, Guid id, GameRepository games, IBlobStore blobs) =>
        {
            if (!await games.IsParticipant(id, CurrentAccount.Id(u))) return Problems.NotFound();
            var entries = new List<ManifestEntry>();
            foreach (var r in await games.ManifestAssets(id))
                entries.Add(new ManifestEntry(r.AssetId, AssetRules.Kind(r.Kind), r.Hash, await blobs.PresignDownload(IBlobStore.Key(r.OwnerId, r.Hash), Ttl), r.Scale, new Tabletop.Geometry.Orientation(r.Pitch, r.Yaw, r.Roll)));
            return Results.Ok(new Manifest(entries, DateTimeOffset.UtcNow.Add(Ttl)));
        }).RequireAuthorization();
}
```

- [ ] **Step 4: Wire `Program.cs`** (`AddSingleton<GameRepository>()`, `app.MapProperties(); app.MapGames(); app.MapManifest();`); run `dotnet test`; expect at least 61 passed. Commit: `Add games, the public browser, join and leave, and the asset manifest`.
