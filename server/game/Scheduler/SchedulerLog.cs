using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>The effective log (undone ranges removed) cut at the scheduler's anchors. Built afresh every call; nothing is cached across calls.</summary>
public sealed class SchedulerLog
{
    static readonly HashSet<string> TurnQuestions = [AskQuestions.Activate, AskQuestions.ActOrEndPhase, AskQuestions.Deploy, AskQuestions.DeployScouts];
    static readonly HashSet<string> Questions = [..TurnQuestions, AskQuestions.DeployAmbusher, AskQuestions.EndRound];

    private SchedulerLog(IReadOnlyList<GameEvent> effective) { Effective = effective; }

    public static SchedulerLog Of(GameState state) => new(EventApplier.EffectiveLog(state.Applier.Log));

    public IReadOnlyList<GameEvent> Effective { get; }
    public long LastSeq => Effective.Count == 0 ? 0 : Effective[^1].Seq;
    public GameEvent? Last(Func<EventPayload, bool> match) => Effective.LastOrDefault(e => match(e.Payload));
    public IReadOnlyList<GameEvent> After(long seq) => Effective.Where(e => e.Seq > seq).ToList();

    public long RoundStartedAt => Last(p => p is RoundStarted)?.Seq ?? 0;
    public long PhaseStartedAt => Last(p => p is PhaseStarted)?.Seq ?? 0;
    public long TurnStartedAt => Last(p => p is TurnStarted)?.Seq ?? 0;
    /// <summary>Where the rotation over teams restarts from Order[0]: a round or a phase start, whichever is later.</summary>
    public long RotationStartAt => Math.Max(RoundStartedAt, PhaseStartedAt);
    public IReadOnlyList<GameEvent> SinceRound => After(RoundStartedAt);
    public IReadOnlyList<GameEvent> SinceRotation => After(RotationStartAt);
    public IReadOnlyList<GameEvent> SinceTurn => After(TurnStartedAt);
    /// <summary>Where the current activation cycle starts: the turn, or a phase started inside it (player-turn phases).</summary>
    public long CycleStartAt => Math.Max(TurnStartedAt, PhaseStartedAt);
    public IReadOnlyList<GameEvent> SinceCycle => After(CycleStartAt);

    /// <summary>An Ask the scheduler issued: unframed, and one of the engine questions it owns.</summary>
    public static bool IsSchedulerAsk(Ask ask) => ask.FrameId is null && Questions.Contains(ask.Question);

    /// <summary>A scheduler Ask whose answer TurnCycle reads through IGameRules.ReadAnswer: the activate and deploy questions. The Ambusher Ask is read by RoundBoundaryStep and the boundary Ask is a confirmation, so neither is one.</summary>
    public static bool IsTurnAsk(Ask ask) => ask.FrameId is null && TurnQuestions.Contains(ask.Question);

    public (AskOpened Opened, long OpenedAt, AskClosed? Closed, long ClosedAt)? LastSchedulerAsk(IReadOnlyList<GameEvent> slice)
    {
        var opened = slice.LastOrDefault(e => e.Payload is AskOpened o && IsSchedulerAsk(o.Ask));
        if (opened is null) return null;
        var ask = (AskOpened)opened.Payload;
        var closed = slice.FirstOrDefault(e => e.Seq > opened.Seq && e.Payload is AskClosed c && c.AskId == ask.Ask.Id);
        return (ask, opened.Seq, closed?.Payload as AskClosed, closed?.Seq ?? 0);
    }

    public IReadOnlyDictionary<Guid, long> LastTurnEndedByTeam(IReadOnlyList<GameEvent> slice) =>
        slice.Where(e => e.Payload is TurnEnded).GroupBy(e => ((TurnEnded)e.Payload).TeamId).ToDictionary(g => g.Key, g => g.Max(e => e.Seq));

    public Guid? LastTurnEndedTeam(IReadOnlyList<GameEvent> slice) => (slice.LastOrDefault(e => e.Payload is TurnEnded)?.Payload as TurnEnded)?.TeamId;
}
