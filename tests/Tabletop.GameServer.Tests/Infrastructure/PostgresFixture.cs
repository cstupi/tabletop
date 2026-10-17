using Npgsql;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;

/// <summary>
/// The E2E suite's seeder: talks to the Docker Postgres from docker-compose.yml. Every row it seeds is deleted on dispose.
/// Implements xunit's own IAsyncLifetime, not the BCL IAsyncDisposable: xUnit.net v2 only awaits the
/// former for a collection fixture's teardown, so IAsyncDisposable alone is never invoked and every
/// seeded row would otherwise leak into the shared database.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime, IRowSeeder
{
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=tabletop;Username=tabletop;Password=tabletop";
    public NpgsqlDataSource Db { get; } = NpgsqlDataSource.Create(ConnectionString);
    public IGameRows Rows => new PostgresGameRows(Db);
    private readonly List<Guid> _games = new(), _accounts = new();
    private readonly List<string> _properties = new();

    public async Task<Guid> SeedAccountAsync(string name)
    {
        var id = Guid.CreateVersion7();
        await using var cmd = Db.CreateCommand("insert into management.account (id, name) values ($1, $2)");
        cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(name);
        await cmd.ExecuteNonQueryAsync();
        _accounts.Add(id);
        return id;
    }

    public async Task<string> SeedPropertiesAsync()
    {
        // [24..32], not [..8]: a v7 guid's leading hex digits are its millisecond timestamp, identical across
        // calls made within the same test run, so a leading slice collides on properties_pkey; the tail is random.
        var hash = "sha256:test-" + Guid.CreateVersion7().ToString("N")[24..32];
        await using var cmd = Db.CreateCommand("insert into management.properties (content_hash, name, game_rules) values ($1, 'GF test', 'grimdark-future')");
        cmd.Parameters.AddWithValue(hash);
        await cmd.ExecuteNonQueryAsync();
        _properties.Add(hash);
        return hash;
    }

    public async Task<SeededGame> SeedGameAsync(GameStatus status, int teams = 2, int participantsPerTeam = 1, int spectators = 0, Enforcement enforcement = Enforcement.Warn, long rngSeed = IRowSeeder.DefaultRngSeed)
    {
        var hash = await SeedPropertiesAsync();
        var creator = await SeedAccountAsync("Creator");
        var gameId = Guid.CreateVersion7();
        await using (var cmd = Db.CreateCommand(
            "insert into management.game (id, name, status, game_rules, properties_hash, scheduler_shape, enforcement, waits_between_steps, rng_seed, created_by) " +
            "values ($1, 'Friday', $2, 'grimdark-future', $3, 'alternating-activation', $4, false, $5, $6)"))
        {
            cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(RowMapping.EnumText(status));
            cmd.Parameters.AddWithValue(hash); cmd.Parameters.AddWithValue(RowMapping.EnumText(enforcement)); cmd.Parameters.AddWithValue(rngSeed); cmd.Parameters.AddWithValue(creator);
            await cmd.ExecuteNonQueryAsync();
        }
        _games.Add(gameId);
        var teamIds = new List<Guid>(); var participants = new List<SeededParticipant>();
        for (var t = 1; t <= teams; t++)
        {
            var teamId = Guid.CreateVersion7(); teamIds.Add(teamId);
            await using var cmd = Db.CreateCommand("insert into management.team (id, game_id, ordinal) values ($1, $2, $3)");
            cmd.Parameters.AddWithValue(teamId); cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(t);
            await cmd.ExecuteNonQueryAsync();
            for (var p = 0; p < participantsPerTeam; p++)
                participants.Add(await SeedParticipantAsync(gameId, t == 1 && p == 0 ? creator : await SeedAccountAsync($"Player {t}.{p}"), teamId));
        }
        for (var s = 0; s < spectators; s++) participants.Add(await SeedParticipantAsync(gameId, await SeedAccountAsync($"Watcher {s}"), null));
        return new SeededGame(gameId, creator, teamIds, participants, hash, rngSeed);
    }

    public async Task<SeededParticipant> SeedParticipantAsync(Guid gameId, Guid accountId, Guid? teamId)
    {
        var id = Guid.CreateVersion7();
        await using var cmd = Db.CreateCommand("insert into management.participant (id, game_id, team_id, account_id) values ($1, $2, $3, $4)");
        cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(gameId);
        cmd.Parameters.AddWithValue((object?)teamId ?? DBNull.Value); cmd.Parameters.AddWithValue(accountId);
        await cmd.ExecuteNonQueryAsync();
        return new SeededParticipant(id, accountId, teamId);
    }

    /// <summary>Inserts ArmyTests.Sample() under owner and returns exactly what it inserted (the sample makes fresh ids per call).</summary>
    public async Task<Tabletop.Content.Army> SeedArmyAsync(Guid ownerId, Tabletop.Content.Army? given = null)
    {
        var army = given ?? Tabletop.Content.Tests.ArmyTests.Sample();
        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        async Task Run(string sql, params object?[] args)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            foreach (var a in args) cmd.Parameters.AddWithValue(a ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        await Run("insert into management.army (id, owner_id, name, game_system, source) values ($1, $2, $3, $4, $5)", army.Id, ownerId, army.Name, army.GameSystem, army.Source);
        foreach (var u in army.Units)
        {
            await Run("insert into management.unit (id, army_id, ordinal, name, reserve_property, reserve_of) values ($1, $2, $3, $4, $5, $6)",
                u.Id, army.Id, u.Ordinal, u.Name, u.Reserve?.Property, u.Reserve?.Of);
            foreach (var (k, v) in u.Stats) await Run("insert into management.unit_stat values ($1, $2, $3)", u.Id, k, v);
            foreach (var p in u.Properties) await Run("insert into management.unit_property values ($1, $2, $3)", u.Id, p.Name, p.Parameter);
            foreach (var s in u.Models)
            {
                await Run("insert into management.model_slot (id, unit_id, ordinal, name, base_shape, base_a, base_b, height, asset_id, army_book) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)",
                    s.Id, u.Id, s.Ordinal, s.Name, RowMapping.EnumText(s.Base.Shape), s.Base.A, s.Base.B, s.Height, s.AssetId, s.ArmyBook);
                foreach (var (k, v) in s.Stats) await Run("insert into management.slot_stat values ($1, $2, $3)", s.Id, k, v);
                foreach (var p in s.Properties) await Run("insert into management.slot_property values ($1, $2, $3)", s.Id, p.Name, p.Parameter);
                foreach (var w in s.Weapons)
                {
                    await Run("insert into management.weapon (id, slot_id, name) values ($1, $2, $3)", w.Id, s.Id, w.Name);
                    foreach (var (k, v) in w.Stats) await Run("insert into management.weapon_stat values ($1, $2, $3)", w.Id, k, v);
                    foreach (var p in w.Properties) await Run("insert into management.weapon_property values ($1, $2, $3)", w.Id, p.Name, p.Parameter);
                }
            }
        }
        await tx.CommitAsync();
        return army;
    }

    // A table_state row for a game seeded at setup/playing/ended, when the test does not go through StartSetup.
    public async Task<Guid> SeedTableStateAsync(Guid gameId, double width = 72, double length = 48)
    {
        var id = Guid.CreateVersion7();
        await using var cmd = Db.CreateCommand("insert into management.table_state (id, game_id, width, length, texture_builtin) values ($1, $2, $3, $4, 'grass')");
        cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(width); cmd.Parameters.AddWithValue(length);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>A saved layout: a table_state owned by an account rather than a game (owner_id set, game_id null), with its
    /// terrain, volumes, markers and zones as rows. Returns exactly what it inserted, so a replace_draft built from it carries
    /// the layout rows' own primary keys. Removed with its owner account, which the table cascades from.</summary>
    public async Task<Tabletop.Content.TableState> SeedLayoutAsync(Guid ownerId)
    {
        var layout = Tabletop.Content.Tests.TableStateTests.Sample() with { Name = "Saved layout" };
        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        async Task Run(string sql, params object?[] args)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            foreach (var a in args) cmd.Parameters.AddWithValue(a ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        await Run("insert into management.table_state (id, owner_id, name, width, length, texture_builtin, texture_asset_id) values ($1, $2, $3, $4, $5, $6, $7)",
            layout.Id, ownerId, layout.Name, layout.Size.Width, layout.Size.Length, layout.Texture.Builtin, layout.Texture.AssetId);
        foreach (var t in layout.Terrain)
        {
            await Run("insert into management.terrain_placement (id, table_state_id, builtin, asset_id, pos_x, pos_y, pos_z, yaw, scale) values ($1, $2, $3, $4, $5, $6, $7, $8, $9)",
                t.Id, layout.Id, t.Mesh.Builtin, t.Mesh.AssetId, t.Position.X, t.Position.Y, t.Position.Z, t.Yaw.Degrees, t.Scale);
            foreach (var v in t.Volumes)
                await Run($"insert into management.terrain_volume (id, terrain_placement_id, {RowMapping.VolumeColumns}) values ($1, $2, {string.Join(", ", Enumerable.Range(3, 16).Select(i => "$" + i))})",
                    [v.Id, t.Id, .. RowMapping.VolumeValues(v.Volume)]);
        }
        foreach (var m in layout.Markers)
            await Run("insert into management.marker (id, table_state_id, kind, pos_x, pos_y, pos_z, yaw, diameter, label, visual_builtin, visual_asset_id) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)",
                m.Id, layout.Id, RowMapping.EnumText(m.Kind), m.Position.X, m.Position.Y, m.Position.Z, m.Yaw.Degrees, m.Diameter, m.Label, m.Visual.Builtin, m.Visual.AssetId);
        foreach (var z in layout.Zones)
            await Run("insert into management.deployment_zone (id, table_state_id, shape, team_ordinal) values ($1, $2, $3, $4)", z.Id, layout.Id, z.Shape, z.TeamOrdinal);
        await tx.CommitAsync();
        return layout;
    }

    /// <summary>
    /// Deletes a properties set seeded outside the fixture (SeedAsync writes it directly, not through SeedPropertiesAsync).
    /// `effect` goes first: its target_roll/target_stat/target_entry/target_action foreign keys are plain (not
    /// on delete cascade), and properties' own cascade to action/roll/stat_definition/catalog_entry races the
    /// catalog_entry-to-effect cascade within one statement, so deleting `properties` directly can fail with
    /// "violates foreign key constraint effect_properties_hash_target_action_fkey" depending on trigger order.
    /// Deleting `effect` (which cascades to effect_condition and condition_value) first removes that race; the
    /// remaining six tables then cascade cleanly from `properties`.
    /// </summary>
    public async Task DeletePropertiesAsync(string hash)
    {
        await Exec("delete from management.effect where properties_hash = $1", hash);
        await Exec("delete from management.properties where content_hash = $1", hash);
    }

    public async Task<int> CountAsync(string sql, params object[] args)
    {
        await using var cmd = Db.CreateCommand(sql);
        foreach (var a in args) cmd.Parameters.AddWithValue(a);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var g in _games) { await Exec("delete from game.game_event where game_id = $1", g); await Exec("delete from management.game where id = $1", g); }
        foreach (var a in _accounts) await Exec("delete from management.account where id = $1", a);
        foreach (var p in _properties) await Exec("delete from management.properties where content_hash = $1", p);
        await Db.DisposeAsync();
    }

    private async Task Exec(string sql, object arg) { await using var cmd = Db.CreateCommand(sql); cmd.Parameters.AddWithValue(arg); await cmd.ExecuteNonQueryAsync(); }
}
