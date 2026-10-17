using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

/// <summary>
/// The rows a socket run writes beside the game server's own: a scenario's management rows before a socket can name its game, a kept
/// game's spectator, the game's deletion, and a suite run's lock and sweep (SuiteRows names the games). SocketRowSeeder is the Postgres
/// one the console uses; tests hand in an in-memory one over the game server's fakes.
/// </summary>
public interface ISocketRows
{
    /// <summary>account, game, team and participant, then the setup result through IGameRows; the game ends up playing.</summary>
    Task SeedAsync(CompiledScenario scenario, SocketCredentials credentials, CancellationToken ct);

    /// <summary>The game's log and its rows; team, participant and the table_state tree go with the game. Accounts stay.</summary>
    Task DeleteAsync(Guid gameId, CancellationToken ct);

    /// <summary>A participant with no team and no army for the account: what the live feed's spectator socket needs. Idempotent on the account.</summary>
    Task SeedSpectatorAsync(Guid gameId, Guid accountId, string name, CancellationToken ct);

    /// <summary>Holds the run's lock until disposed. A run whose process dies loses its lock with it.</summary>
    Task<IAsyncDisposable> HoldAsync(Guid runId, CancellationToken ct);

    /// <summary>Deletes the transient games (SuiteRows.TransientRunOf) of every other run whose lock no one holds. Returns the games' ids.</summary>
    Task<IReadOnlyList<Guid>> SweepAsync(Guid runId, CancellationToken ct);
}
