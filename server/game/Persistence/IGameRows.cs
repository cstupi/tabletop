using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Persistence;

public sealed record OwnedArmy(Army Army, Guid OwnerId);

/// <summary>
/// Management's rows as this server sees them. Reads at hydration and end-setup; writes while the game is lobby or setup, a participant's team and army
/// while deployment waits for players (WaitingForPlayers), and `game.status` once more: `playing → ended`
/// when this server's own log ends the game (enforcement spec → The row and the broadcast). Nothing here is a join across the schema boundary: the log is IEventLog.
/// </summary>
public interface IGameRows
{
    Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct);
    Task<Guid?> FindParticipantAsync(Guid gameId, Guid accountId, CancellationToken ct);
    Task<TableState?> ReadDraftAsync(Guid gameId, CancellationToken ct);
    Task<IReadOnlyList<ArmyInPlay>> ReadSnapshotsAsync(Guid gameId, CancellationToken ct);
    Task<OwnedArmy?> ReadArmyAsync(Guid armyId, CancellationToken ct);
    Task<bool> PropertiesExistAsync(string hash, CancellationToken ct);
    /// <summary>Each named asset that exists, with its owner's account id. A setup edit may reference only assets a participant owns.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> AssetOwnersAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken ct);

    /// <summary>playing → ended, guarded on playing, so management's own end and a second call change nothing.</summary>
    Task EndGameAsync(Guid gameId, CancellationToken ct);

    Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct);
    Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct);
    Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct);
    /// <summary>One more participant row for an account already in the game, on teamId: an account holds one row per team it plays (self-play).</summary>
    Task AddSeatAsync(Guid participantId, Guid gameId, Guid accountId, Guid teamId, CancellationToken ct);
    /// <summary>Deletes one of an account's participant rows; the session calls it only while the account keeps another.</summary>
    Task RemoveSeatAsync(Guid participantId, CancellationToken ct);
    /// <summary>While deployment waits for players: the army a participant brings after the flip, its army_id and army_snapshot in one write,
    /// guarded on playing and on no snapshot yet.</summary>
    Task JoinArmyAsync(Guid gameId, Guid participantId, Guid armyId, ArmySnapshot snapshot, CancellationToken ct);
    Task SetConfigurationAsync(Guid gameId, SetConfiguration configuration, CancellationToken ct);
    /// <summary>Inserts the game's table_state tree and flips status to setup, in one transaction.</summary>
    Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct);
    /// <summary>Rewrites the whole tree: one write per drop (setup phase → The draft is the row).</summary>
    Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct);
    /// <summary>Rewrites the tree (off-slab terrain already removed), writes each participant's army_snapshot, flips status to playing; one transaction.</summary>
    Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshotsByParticipant, CancellationToken ct);
}
