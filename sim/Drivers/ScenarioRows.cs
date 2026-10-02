using Tabletop.Content;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;

namespace Tabletop.Sim.Drivers;

/// <summary>
/// Management's rows, in memory, for exactly one scenario at status playing. The writes belong to lobby and
/// setup, which a scenario skips: it declares the setup result directly, and the setup phase is not logged
/// anyway (setup content → The setup phase is not logged).
///
/// This is also how an in-process run sets the RNG seed: ReadGameAsync hands back the scenario's own
/// GameInfo, whose RngSeed the compiler set from the scenario seed, so Hydrator reaches GameState.RngSeed
/// by the ordinary path. The socket driver has to write the rng_seed column instead (Task 8).
/// </summary>
public sealed class ScenarioRows(Scenarios.CompiledScenario scenario) : IGameRows
{
    public Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(gameId == scenario.Info.Id ? scenario.Info : null);

    public Task<Guid?> FindParticipantAsync(Guid gameId, Guid accountId, CancellationToken ct) =>
        Task.FromResult(scenario.Info.Participants.FirstOrDefault(p => p.AccountId == accountId)?.Id);

    public Task<TableState?> ReadDraftAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<TableState?>(gameId == scenario.Info.Id ? scenario.Snapshot : null);

    public Task<IReadOnlyList<ArmyInPlay>> ReadSnapshotsAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(scenario.Board.Armies);

    public Task<OwnedArmy?> ReadArmyAsync(Guid armyId, CancellationToken ct) => Task.FromResult<OwnedArmy?>(null);
    public Task<bool> PropertiesExistAsync(string hash, CancellationToken ct) =>
        Task.FromResult(hash == scenario.Properties.ContentHash);

    private static Task Unsupported(string member) =>
        throw new NotSupportedException($"a scenario is already playing; {member} belongs to lobby or setup");

    public Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct) => throw new NotSupportedException("a scenario declares its teams");
    public Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct) => Unsupported(nameof(SetParticipantTeamAsync));
    public Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct) => Unsupported(nameof(SetParticipantArmyAsync));
    public Task SetConfigurationAsync(Guid gameId, SetConfiguration configuration, CancellationToken ct) => Unsupported(nameof(SetConfigurationAsync));
    public Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct) => Unsupported(nameof(StartSetupAsync));
    public Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct) => Unsupported(nameof(WriteDraftAsync));
    public Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshots, CancellationToken ct) =>
        Unsupported(nameof(EndSetupAsync));
}
