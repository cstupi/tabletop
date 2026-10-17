using Npgsql;
using Tabletop.Content;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Drivers;

public sealed class PropertiesMissingException(string hash)
    : Exception($"no properties row for {hash}; start the game server once so it seeds its embedded documents");

/// <summary>
/// ISocketRows over Postgres. account, game, team and participant are management's writes and are raw SQL here; the table_state tree
/// and the army snapshots go through IGameRows, which owns them (game server skeleton plan → Interfaces needed 1). A run's lock is a
/// session advisory lock keyed on its run id (LockKey), held on a connection of its own.
/// </summary>
public sealed class SocketRowSeeder(NpgsqlDataSource db, IGameRows rows) : ISocketRows
{
    public SocketRowSeeder(NpgsqlDataSource db) : this(db, new PostgresGameRows(db)) { }

    public async Task SeedAsync(CompiledScenario scenario, SocketCredentials credentials, CancellationToken ct)
    {
        if (!await rows.PropertiesExistAsync(scenario.Properties.ContentHash, ct))
            throw new PropertiesMissingException(scenario.Properties.ContentHash);

        var accounts = scenario.Info.Participants.Select((_, i) => credentials.AccountOf(i)).ToList();
        var info = scenario.Info;

        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        for (var i = 0; i < accounts.Count; i++)
            await Execute(connection, transaction, ct,
                "insert into management.account (id, name) values ($1, $2) on conflict (id) do nothing",
                accounts[i], info.Participants[i].Name);

        // rng_seed is how a socket run sets the seed: the driver writes a row rather than constructing a
        // GameInfo, so the scenario's seed goes in the insert and the game server reads it back at
        // hydration with the rest of the game's row.
        await Execute(connection, transaction, ct,
            """
            insert into management.game (id, name, status, game_rules, properties_hash, scheduler_shape, enforcement, waits_between_steps, created_by, rng_seed)
            values ($1, $2, 'lobby', $3, $4, $5, $6, $7, $8, $9)
            """,
            info.Id, info.Name, info.GameRules, info.PropertiesHash,
            Wire(info.SchedulerShape), Wire(info.Enforcement), info.WaitsBetweenSteps, accounts[0], info.RngSeed);

        foreach (var team in info.Teams)
            await Execute(connection, transaction, ct,
                "insert into management.team (id, game_id, ordinal) values ($1, $2, $3)", team.Id, info.Id, team.Ordinal);

        for (var i = 0; i < info.Participants.Count; i++)
            await Execute(connection, transaction, ct,
                "insert into management.participant (id, game_id, team_id, account_id, army_id, army_snapshot) values ($1, $2, $3, $4, null, null)",
                info.Participants[i].Id, info.Id, info.Participants[i].TeamId, accounts[i]);

        await transaction.CommitAsync(ct);

        // The setup result, written by the code that owns the tree; end-setup flips the game to playing.
        await rows.StartSetupAsync(info.Id, scenario.Snapshot, ct);
        await rows.EndSetupAsync(info.Id, scenario.Snapshot,
            scenario.Board.Armies.ToDictionary(a => a.ParticipantId, a => (ArmySnapshot?)a.Snapshot), ct);
    }

    public async Task DeleteAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await Execute(connection, null, ct, "delete from game.game_event where game_id = $1", gameId);
        await Execute(connection, null, ct, "delete from management.game where id = $1", gameId);   // the tree cascades
    }

    public async Task SeedSpectatorAsync(Guid gameId, Guid accountId, string name, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await Execute(connection, transaction, ct, "insert into management.account (id, name) values ($1, $2) on conflict (id) do nothing", accountId, name);
        await Execute(connection, transaction, ct,
            "insert into management.participant (id, game_id, team_id, account_id, army_id, army_snapshot) values ($1, $2, null, $3, null, null)",
            Guid.CreateVersion7(), gameId, accountId);
        await transaction.CommitAsync(ct);
    }

    /// <summary>The advisory lock key of a run: the run id's last eight bytes, the random part of a v7 guid.</summary>
    public static long LockKey(Guid runId) => BitConverter.ToInt64(runId.ToByteArray(), 8);

    /// <summary>
    /// A pooled connection's session outlives its dispose, so the lock is unlocked explicitly before the connection goes. A process that
    /// dies drops the connection, and the server releases the lock with it.
    /// </summary>
    public async Task<IAsyncDisposable> HoldAsync(Guid runId, CancellationToken ct)
    {
        var connection = await db.OpenConnectionAsync(ct);
        try
        {
            await ProbeAsync(connection, "select pg_advisory_lock($1) is null", runId, ct);
            return new RunLock(connection, runId);
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private sealed class RunLock(NpgsqlConnection connection, Guid runId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await ProbeAsync(connection, "select pg_advisory_unlock($1)", runId, CancellationToken.None); }
            finally { await connection.DisposeAsync(); }
        }
    }

    /// <summary>A live run holds its lock, and a dead process's session released it, so only an interrupted run's games go.</summary>
    public async Task<IReadOnlyList<Guid>> SweepAsync(Guid runId, CancellationToken ct)
    {
        var byRun = new Dictionary<Guid, List<Guid>>();
        await using (var command = db.CreateCommand("select id, name from management.game where starts_with(name, $1)"))
        {
            command.Parameters.AddWithValue(SuiteRows.TransientPrefix);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (SuiteRows.TransientRunOf(reader.GetString(1)) is { } run && run != runId)
                    (byRun.TryGetValue(run, out var list) ? list : byRun[run] = []).Add(reader.GetGuid(0));
        }

        var swept = new List<Guid>();
        await using var probe = await db.OpenConnectionAsync(ct);
        foreach (var (run, games) in byRun)
        {
            if (!await ProbeAsync(probe, "select pg_try_advisory_lock($1)", run, ct)) continue;     // a live run
            try
            {
                foreach (var game in games) await DeleteAsync(game, ct);
                swept.AddRange(games);
            }
            finally { await ProbeAsync(probe, "select pg_advisory_unlock($1)", run, CancellationToken.None); }
        }
        return swept;
    }

    private static async Task<bool> ProbeAsync(NpgsqlConnection connection, string sql, Guid run, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(LockKey(run));
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static string Wire<T>(T value) where T : struct, Enum => WireJson.Serialize(value).Trim('"');

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct,
        string sql, params object?[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
