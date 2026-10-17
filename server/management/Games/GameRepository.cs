using Dapper;
using Tabletop.Content;
using Tabletop.Management.Persistence;
using Tabletop.Protocol;

namespace Tabletop.Management.Games;

/// <summary>One catalog entry's player-facing text (spec: in-game UX → Rule text). Played is false on a generated text-only entry
/// (army-book rules spec → Played flag). Dapper maps by constructor parameter name.</summary>
public sealed record CatalogRuleText(string Name, string Kind, bool TakesParameter, string RuleText, bool Played);

public sealed class GameRepository(Database db) : IGameRepository
{
    private sealed record GameRow(Guid Id, string Name, string Status, string GameRules, string PropertiesHash, string SchedulerShape, string Enforcement, bool WaitsBetweenSteps, long RngSeed, Guid CreatedBy);
    private sealed record ParticipantRow(Guid Id, Guid AccountId, string Name, Guid? TeamId, Guid? ArmyId);
    // CreatedAt comes back as DateTime (Npgsql's CLR type for timestamptz), not DateTimeOffset: Dapper picks a
    // constructor by the reader's exact field type, so the row type must match it and GameSummary's DateTimeOffset
    // is built afterwards.
    private sealed record SummaryRow(Guid Id, string Name, string Status, Guid CreatedBy, DateTime CreatedAt, int Participants, Array ParticipantNames);   // Array: the reader reports text[] as System.Array
    private static readonly IReadOnlyDictionary<string, SchedulerShape> Shapes = new Dictionary<string, SchedulerShape> { ["player-turn"] = SchedulerShape.PlayerTurn, ["alternating-activation"] = SchedulerShape.AlternatingActivation };
    private static string Wire(SchedulerShape s) => Shapes.First(kv => kv.Value == s).Key;

    public async Task<IReadOnlyList<PropertiesSummary>> ListProperties()
    {
        await using var c = await db.Open();
        return (await c.QueryAsync<PropertiesSummary>("""
            select distinct on (game_rules, name) content_hash as ContentHash, name as Name, game_rules as GameRules
            from properties order by game_rules, name, seeded_at desc
            """)).ToList();
    }

    /// <summary>Every catalog entry of a pinned set, by name; null when no set has this hash.</summary>
    public async Task<IReadOnlyList<CatalogRuleText>?> RuleTexts(string hash)
    {
        await using var c = await db.Open();
        if (await c.ExecuteScalarAsync<int>("select count(*) from properties where content_hash = @hash", new { hash }) == 0) return null;
        return (await c.QueryAsync<CatalogRuleText>("""
            select name as Name, kind as Kind, takes_parameter as TakesParameter, rule_text as RuleText, played as Played
            from catalog_entry where properties_hash = @hash order by name
            """, new { hash })).ToList();
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
            (select count(*) from participant p where p.game_id = g.id)::int as Participants,
            array(select a.name from participant p join account a on a.id = p.account_id where p.game_id = g.id order by p.id) as ParticipantNames
            from game g where (@after::uuid is null or g.id < @after::uuid) order by g.id desc limit @limit", new { after = page.After, limit = page.Limit });
        return rows.Select(r => new GameSummary(r.Id, r.Name, Enum.Parse<GameStatus>(r.Status, true), r.CreatedBy, new DateTimeOffset(r.CreatedAt, TimeSpan.Zero), r.Participants, r.ParticipantNames.Cast<string>().ToList())).ToList();
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
