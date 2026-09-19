using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Persistence;

/// <summary>Stub bodies for the writes; Tasks 10-12 replace them.</summary>
public sealed partial class PostgresGameRows
{
    public Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct) => throw new NotImplementedException();
    public Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct) => throw new NotImplementedException();
    public Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct) => throw new NotImplementedException();
    public Task SetConfigurationAsync(Guid gameId, SetConfiguration configuration, CancellationToken ct) => throw new NotImplementedException();
    public Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct) => throw new NotImplementedException();
    public Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct) => throw new NotImplementedException();
    public Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshotsByParticipant, CancellationToken ct) => throw new NotImplementedException();
}
