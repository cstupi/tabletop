using Npgsql;
using Tabletop.GameServer.Persistence;
using Tabletop.Sim.Drivers;

namespace Tabletop.Sim.Suite;

/// <summary>
/// A socket run's rows. Every game a run seeds carries the run id in its name: "suite-run &lt;run&gt; &lt;game&gt;" while the run owns it
/// and will delete it, "suite &lt;run&gt; &lt;game&gt;" when --keep keeps it. A socket run holds a session advisory lock keyed on its run id
/// for its whole length (HoldAsync). The sweep deletes the suite-run games of every other run whose lock it can take: a live run
/// holds its lock, and a dead process's session released it, so only an interrupted run's games go. Kept games are never swept.
/// </summary>
public sealed class SuiteRows(NpgsqlDataSource db)
{
    private const string Transient = "suite-run", Kept = "suite";

    public static string GameName(Guid runId, string game, bool keep) => $"{(keep ? Kept : Transient)} {runId:N} {game}";

    /// <summary>The advisory lock key of a run: the run id's last eight bytes, the random part of a v7 guid.</summary>
    public static long LockKey(Guid runId) => BitConverter.ToInt64(runId.ToByteArray(), 8);

    /// <summary>
    /// Holds the run's advisory lock on a connection of its own until disposed, which unlocks it: a pooled connection's session outlives
    /// its dispose, so closing alone would not. A process that dies drops the connection, and the server releases the lock with it.
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

    /// <summary>
    /// Deletes the transient games of every other run that no live session holds the lock of, with their logs and the accounts nothing
    /// else uses. Returns the games' ids.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> SweepAsync(Guid runId, CancellationToken ct)
    {
        var byRun = new Dictionary<Guid, List<Guid>>();
        await using (var command = db.CreateCommand("select id, name from management.game where starts_with(name, $1)"))
        {
            command.Parameters.AddWithValue($"{Transient} ");
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var parts = reader.GetString(1).Split(' ', 3);
                // A name the suite did not write is not the suite's to delete.
                if (parts.Length < 3 || !Guid.TryParseExact(parts[1], "N", out var run) || run == runId) continue;
                (byRun.TryGetValue(run, out var list) ? list : byRun[run] = []).Add(reader.GetGuid(0));
            }
        }

        var swept = new List<Guid>();
        await using var probe = await db.OpenConnectionAsync(ct);
        foreach (var (run, games) in byRun)
        {
            if (!await ProbeAsync(probe, "select pg_try_advisory_lock($1)", run, ct)) continue;     // a live run
            try
            {
                foreach (var game in games) await DeleteGameAsync(game, await AccountsOfAsync(game, ct), ct);
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

    /// <summary>The game's log and rows (the tree cascades), then those of the accounts no game or participant still names.</summary>
    public async Task DeleteGameAsync(Guid gameId, IReadOnlyList<Guid> accounts, CancellationToken ct)
    {
        await new SocketRowSeeder(db, new PostgresGameRows(db)).DeleteAsync(gameId, ct);
        // An account a kept game names stays: deleting it would cascade into that game's participants.
        await using var command = db.CreateCommand(
            """
            delete from management.account a where a.id = any($1)
              and not exists (select 1 from management.game g where g.created_by = a.id)
              and not exists (select 1 from management.participant p where p.account_id = a.id)
            """);
        command.Parameters.AddWithValue(accounts.ToArray());
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<IReadOnlyList<Guid>> AccountsOfAsync(Guid gameId, CancellationToken ct)
    {
        var accounts = new List<Guid>();
        await using var command = db.CreateCommand(
            "select account_id from management.participant where game_id = $1 union select created_by from management.game where id = $1");
        command.Parameters.AddWithValue(gameId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) accounts.Add(reader.GetGuid(0));
        return accounts;
    }
}
