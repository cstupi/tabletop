using Dapper;
using Npgsql;
using Tabletop.Content;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;
using Tabletop.Management.Games;
using Tabletop.Management.Identity;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Tests.Contracts;

/// <summary>The SQL repositories on a database of the class's own.</summary>
[Trait("Category", "E2E")]
public sealed class PostgresRepositoryContractTests : RepositoryContract, IClassFixture<TestDatabase>
{
    private readonly string _cs;
    private readonly Database _db;

    public PostgresRepositoryContractTests(TestDatabase database)
    {
        _cs = database.ConnectionString;
        _db = database.Data;
        Accounts = new AccountRepository(_db);
        Assets = new AssetRepository(_db);
        Shared = new SharedAssetRepository(_db);
        Folders = new FolderRepository(_db);
        Armies = new ArmyRepository(_db);
        Layouts = new LayoutRepository(_db);
        Games = new GameRepository(_db);
        Kinds = new CatalogKinds(_db);
    }

    protected override IAccountRepository Accounts { get; }
    protected override IAssetRepository Assets { get; }
    protected override ISharedAssetRepository Shared { get; }
    protected override IFolderRepository Folders { get; }
    protected override IArmyRepository Armies { get; }
    protected override ILayoutRepository Layouts { get; }
    protected override IGameRepository Games { get; }
    protected override ICatalogKinds Kinds { get; }

    private async Task Execute(string sql, object args)
    {
        await using var c = new NpgsqlConnection(_cs);
        await c.ExecuteAsync(sql, args);
    }

    protected override Task SeedProperties(string hash, string name, string gameRules, DateTimeOffset seededAt) =>
        Execute("insert into management.properties (content_hash, name, game_rules, seeded_at) values (@hash, @name, @gameRules, @seededAt)", new { hash, name, gameRules, seededAt });

    protected override Task SeedCatalogEntry(string hash, string name, string kind, bool takesParameter, string ruleText, bool played) =>
        Execute("insert into management.catalog_entry (properties_hash, name, kind, takes_parameter, aggregation, rule_text, played) values (@hash, @name, @kind, @takesParameter, @aggregation, @ruleText, @played)",
            new { hash, name, kind, takesParameter, aggregation = kind == "model" ? "all" : null, ruleText, played });

    protected override Task SeedEffect(string hash, string entryName, Guid id, int ordinal, string duration, Guid? parentId = null) =>
        Execute("insert into management.effect (id, properties_hash, entry_name, parent_effect_id, subject, ordinal, kind, duration) values (@id, @hash, @entryName, @parentId, 'self', @ordinal, 'add_hit', @duration)",
            new { id, hash, entryName, parentId, ordinal, duration });

    protected override Task SeedGameEvent(Guid gameId, long seq) =>
        Execute("insert into game.game_event (game_id, seq, kind, payload) values (@gameId, @seq, 'turn_started', '{}')", new { gameId, seq });

    protected override async Task<int> GameEventCount(Guid gameId)
    {
        await using var c = new NpgsqlConnection(_cs);
        return await c.ExecuteScalarAsync<int>("select count(*) from game.game_event where game_id = @gameId", new { gameId });
    }

    protected override Task PickArmy(Guid gameId, Guid accountId, Guid armyId) =>
        Execute("update management.participant set army_id = @armyId where game_id = @gameId and account_id = @accountId", new { gameId, accountId, armyId });

    protected override Task FreezeArmy(Guid gameId, Guid accountId, ArmySnapshot snapshot) =>
        Execute("update management.participant set army_snapshot = @snap::jsonb where game_id = @gameId and account_id = @accountId", new { gameId, accountId, snap = ContentJson.Serialize(snapshot) });

    protected override Task SeedGameTable(Guid gameId, Guid textureAssetId) =>
        Execute("insert into management.table_state (id, game_id, width, length, texture_asset_id) values (@id, @gameId, 72, 48, @textureAssetId)", new { id = Guid.CreateVersion7(), gameId, textureAssetId });

    protected override Task BackdateBlob(Guid ownerId, string hash, TimeSpan age) =>
        Execute("update management.blob set created_at = created_at - @age where owner_id = @ownerId and hash = @hash", new { ownerId, hash, age });
}
