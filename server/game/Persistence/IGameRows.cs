using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Persistence;

public sealed record OwnedArmy(Army Army, Guid OwnerId);

/// <summary>
/// Management's rows as this server sees them. Reads at hydration and end-setup; writes only while the game is lobby or setup
/// (whole-design review ruling 4). Nothing here is a join across the schema boundary: the log is IEventLog.
/// </summary>
public interface IGameRows
{
    Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct);
    Task<Guid?> FindParticipantAsync(Guid gameId, Guid accountId, CancellationToken ct);
    Task<TableState?> ReadDraftAsync(Guid gameId, CancellationToken ct);
    Task<IReadOnlyList<ArmyInPlay>> ReadSnapshotsAsync(Guid gameId, CancellationToken ct);
    Task<OwnedArmy?> ReadArmyAsync(Guid armyId, CancellationToken ct);
    Task<bool> PropertiesExistAsync(string hash, CancellationToken ct);

    Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct);
    Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct);
    Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct);
    Task SetConfigurationAsync(Guid gameId, SetConfiguration configuration, CancellationToken ct);
    /// <summary>Inserts the game's table_state tree and flips status to setup, in one transaction.</summary>
    Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct);
    /// <summary>Rewrites the whole tree: one write per drop (setup phase → The draft is the row).</summary>
    Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct);
    /// <summary>Rewrites the tree (off-slab terrain already removed), writes each participant's army_snapshot, flips status to playing; one transaction.</summary>
    Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshotsByParticipant, CancellationToken ct);
}
