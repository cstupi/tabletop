using Npgsql;
using Tabletop.GameServer.Persistence;
using Tabletop.Sim.Drivers;

namespace Tabletop.Sim.Suite;

/// <summary>
/// A socket run's rows. Every game a run seeds carries the run id in its name: "suite-run &lt;run&gt; &lt;game&gt;" while the run owns it
/// and will delete it, "suite &lt;run&gt; &lt;game&gt;" when --keep keeps it. A run first sweeps every suite-run game of another run,
/// which only an interrupted run leaves behind; kept games are never swept.
/// </summary>
public sealed class SuiteRows(NpgsqlDataSource db)
{
    private const string Transient = "suite-run", Kept = "suite";

    public static string GameName(Guid runId, string game, bool keep) => $"{(keep ? Kept : Transient)} {runId:N} {game}";

    /// <summary>Deletes the games other runs left under the transient prefix, their logs and the accounts nothing else uses. Returns the games' ids.</summary>
    public async Task<IReadOnlyList<Guid>> SweepAsync(Guid runId, CancellationToken ct)
    {
        var games = new List<Guid>();
        await using (var command = db.CreateCommand("select id from management.game where starts_with(name, $1) and not starts_with(name, $2)"))
        {
            command.Parameters.AddWithValue($"{Transient} ");
            command.Parameters.AddWithValue($"{Transient} {runId:N} ");
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) games.Add(reader.GetGuid(0));
        }
        foreach (var game in games) await DeleteGameAsync(game, await AccountsOfAsync(game, ct), ct);
        return games;
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
