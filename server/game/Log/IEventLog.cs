using Tabletop.Protocol;

namespace Tabletop.GameServer.Log;

/// <summary>The game schema's one table. Append is atomic per call: all of the events land or none do. The session appends one command's
/// events (its own batch and every Advance batch after it) in one call. Seq is assigned by the session and enforced by the primary key.</summary>
public interface IEventLog
{
    Task AppendAsync(Guid gameId, IReadOnlyList<GameEvent> events, CancellationToken ct);
    Task<IReadOnlyList<GameEvent>> ReadAsync(Guid gameId, long afterSeq, CancellationToken ct);
    Task<long> LastSeqAsync(Guid gameId, CancellationToken ct);
}

public sealed class DuplicateSequenceException(Guid gameId, long seq) : Exception($"Game {gameId} already has an event at seq {seq}.")
{
    public Guid GameId { get; } = gameId;
    public long Seq { get; } = seq;
}
