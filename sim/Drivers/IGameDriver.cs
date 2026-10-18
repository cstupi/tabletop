using Tabletop.Protocol;

namespace Tabletop.Sim.Drivers;

public abstract record SubmitResult
{
    public sealed record Accepted(string? Warning) : SubmitResult;
    public sealed record Rejected(RefusalReason Reason, string Detail) : SubmitResult;
}

/// <summary>What one step produced. Quiescent means the server rested without emitting anything.</summary>
public sealed record StepResult(IReadOnlyList<GameEvent> Events, IReadOnlyList<OpenAsk> OpenAsks, bool Quiescent);

/// <summary>Run answers every Ask; StepAndWait stops with one open and answers nothing (spec → Interactive debugging is a door).</summary>
public enum DriverMode { Run, StepAndWait }

/// <summary>
/// The server did not say it was at rest inside the driver's timeout. A hung server or a dead
/// connection, never a stuck game: RunLoop turns it into RunOutcome.Failed.
/// </summary>
public sealed class RestTimeoutException(TimeSpan timeout, long lastSeq, Exception? cause = null)
    : Exception($"the server did not reach rest within {timeout.TotalSeconds:0.#}s; last applied seq {lastSeq}"
        + (cause is null ? "" : $" (the socket failed: {cause.GetType().Name}: {cause.Message})"), cause)
{
    public TimeSpan Timeout { get; } = timeout;
    public long LastSeq { get; } = lastSeq;
}

/// <summary>A driver's own stream broke an invariant (a lost, reordered or duplicated frame). RunLoop turns it into RunOutcome.Failed.</summary>
public sealed class DriverFaultException(string message) : Exception(message);

/// <summary>
/// One game, played from outside. Two implementations: in process and over the socket, and the same
/// scenario must pass against either (spec → Two drivers behind one interface).
/// </summary>
public interface IGameDriver : IAsyncDisposable
{
    string Name { get; }
    Guid GameId { get; }
    GameInfo Info { get; }
    Board Board { get; }
    IReadOnlyList<Guid> TeamIds { get; }
    Guid ParticipantFor(Guid teamId);
    DriverMode Mode { get; set; }

    /// <summary>The scenario's board and armies become a playing game, and the driver is connected to it.</summary>
    Task StartAsync(CancellationToken ct);

    Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct);
    Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct);

    /// <summary>Every event that arrived since the last drain, in sequence order, applied to the driver's own projection.</summary>
    Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct);

    Task<Projection> ProjectionAsync(CancellationToken ct);

    /// <summary>Let the server rest, then report what it produced.</summary>
    Task<StepResult> StepAsync(CancellationToken ct);

    /// <summary>Names the team answering, so an account holding several teams (self-play) answers for the one meant. `seat` is the participant who
    /// answers (a team game: the seat whose unit it is); null is the team's first.</summary>
    Task<SubmitResult> AnswerAsync(Guid teamId, Guid askId, AskAnswer answer, CancellationToken ct, Guid? seat = null) =>
        SubmitAsync(seat ?? ParticipantFor(teamId), new AnswerAsk(askId, answer, teamId), ct);
}
