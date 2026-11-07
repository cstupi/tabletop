using Tabletop.Content;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

public sealed record SeededParticipant(Guid ParticipantId, Guid AccountId, Guid? TeamId);
public sealed record SeededGame(Guid GameId, Guid CreatorAccountId, IReadOnlyList<Guid> TeamIds, IReadOnlyList<SeededParticipant> Participants, string PropertiesHash, long RngSeed);

/// <summary>
/// Management's rows as a test seeds them: InMemorySeeder writes the in-memory stores (the unit suite), PostgresFixture the Docker
/// Postgres (the E2E suite). Rows is the IGameRows over the same rows, so a contract test reads back what it seeded.
/// </summary>
public interface IRowSeeder
{
    /// <summary>The dice seed a seeded game gets: a fixed value keeps seeded games' dice identical run to run.</summary>
    public const long DefaultRngSeed = 8675309;

    IGameRows Rows { get; }
    Task<Guid> SeedAccountAsync(string name);
    /// <summary>A bare properties row (no catalog): name "GF test", game rules grimdark-future. Returns its hash.</summary>
    Task<string> SeedPropertiesAsync();
    Task<SeededGame> SeedGameAsync(GameStatus status, int teams = 2, int participantsPerTeam = 1, int spectators = 0, Enforcement enforcement = Enforcement.Warn, long rngSeed = DefaultRngSeed);
    Task<SeededParticipant> SeedParticipantAsync(Guid gameId, Guid accountId, Guid? teamId);
    /// <summary>ArmyTests.Sample() (or the army given) under owner; returns exactly what it stored.</summary>
    Task<Army> SeedArmyAsync(Guid ownerId, Army? given = null);
    /// <summary>A game's table_state row, for a game seeded at setup, playing or ended that never went through StartSetup.</summary>
    Task<Guid> SeedTableStateAsync(Guid gameId, double width = 72, double length = 48);
    /// <summary>A saved layout: TableStateTests.Sample() named "Saved layout", owned by an account rather than a game.</summary>
    Task<TableState> SeedLayoutAsync(Guid ownerId);
    /// <summary>The game row's progress columns as last written; null before IGameRows.WriteProgressAsync.</summary>
    Task<GameProgress?> ProgressOfAsync(Guid gameId);
}
