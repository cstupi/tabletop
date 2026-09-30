using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class SchedulerLogTests
{
    static readonly Guid AskA = Guid.CreateVersion7(), AskB = Guid.CreateVersion7();
    static Ask Activate(Guid id, Guid team) => new(id, AskQuestions.Activate, "Activate", [team], new Options([]), true, false, null, null);
    static Ask Framed(Guid id, Guid team) => new(id, AskQuestions.Confirm, "Confirm", [team], new Confirmation(), false, false, null, Samples.Frame);
    static Ask EndRound(Guid id, Guid[] teams) => new(id, AskQuestions.EndRound, "End the round", teams, new Confirmation(), false, false, null, null);

    static GameState State(params EventPayload[] history)
    {
        var applier = new EventApplier(Samples.Board, Samples.Game);
        applier.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, null, p, null)));
        return new GameState(Samples.Game, Samples.Board, applier, GrimdarkFutureRules.Instance);
    }

    [Fact]
    public void Slices_are_cut_at_the_last_round_phase_and_turn_events_of_the_effective_log()
    {
        var log = SchedulerLog.Of(State(
            new RoundStarted(1, [Samples.Team1, Samples.Team2], []), new PhaseStarted("rounds"),
            new TurnStarted(Samples.Team1), new TurnEnded(Samples.Team1, false, []),
            new TurnStarted(Samples.Team2), new AskOpened(Activate(AskA, Samples.Team2)),
            new Undone(5), new TurnStarted(Samples.Team2), new AskOpened(Activate(AskB, Samples.Team2))));
        Assert.Equal((1L, 2L, 8L, 2L, 8L), (log.RoundStartedAt, log.PhaseStartedAt, log.TurnStartedAt, log.RotationStartAt, log.CycleStartAt));
        Assert.Equal([9L], log.SinceTurn.Select(e => e.Seq));
        Assert.Equal([9L], log.SinceCycle.Select(e => e.Seq));
        Assert.Equal([3L, 4L, 5L, 8L, 9L], log.SinceRotation.Select(e => e.Seq));
        Assert.Equal(9, log.LastSeq);
        Assert.Equal(Samples.Team1, log.LastTurnEndedTeam(log.SinceRound));
        Assert.Equal(4, log.LastTurnEndedByTeam(log.SinceRound)[Samples.Team1]);
    }

    [Fact]
    public void Scheduler_asks_are_unframed_engine_questions_and_the_last_one_is_found_with_its_close()
    {
        Assert.True(SchedulerLog.IsSchedulerAsk(Activate(AskA, Samples.Team1)));
        Assert.False(SchedulerLog.IsSchedulerAsk(Framed(AskA, Samples.Team1)));
        Assert.False(SchedulerLog.IsSchedulerAsk(Activate(AskA, Samples.Team1) with { Question = "shoot_or_not" }));
        Assert.True(SchedulerLog.IsSchedulerAsk(EndRound(AskB, [Samples.Team1, Samples.Team2])));                        // the round boundary's own Ask (Task 5 emits it)
        Assert.True(SchedulerLog.IsTurnAsk(Activate(AskA, Samples.Team1)));
        Assert.False(SchedulerLog.IsTurnAsk(EndRound(AskB, [Samples.Team1])));                                           // a confirmation, not a turn: ReadAnswer is not asked about it (Task 4)
        Assert.False(SchedulerLog.IsTurnAsk(Framed(AskA, Samples.Team1)));
        var log = SchedulerLog.Of(State(new TurnStarted(Samples.Team1),
            new AskOpened(Activate(AskA, Samples.Team1)), new AskAnswered(AskA, Samples.Team1, new Passed(), false),
            new AskClosed(AskA, [new TeamAnswer(Samples.Team1, new Passed())]), new AskOpened(Framed(AskB, Samples.Team1))));
        var found = log.LastSchedulerAsk(log.SinceTurn);
        Assert.NotNull(found);
        Assert.Equal((AskA, 2L, 4L), (found.Value.Opened.Ask.Id, found.Value.OpenedAt, found.Value.ClosedAt));
        Assert.IsType<Passed>(found.Value.Closed!.Answers[0].Answer);
        var bare = SchedulerLog.Of(State(new TurnStarted(Samples.Team1)));
        Assert.Null(bare.LastSchedulerAsk(bare.SinceTurn));
    }

    [Fact]
    public void An_empty_log_has_no_anchors()
    {
        var log = SchedulerLog.Of(State());
        Assert.Equal((0L, 0L, 0L, 0L, 0L), (log.RoundStartedAt, log.PhaseStartedAt, log.TurnStartedAt, log.RotationStartAt, log.LastSeq));
        Assert.Empty(log.SinceRound);
    }
}
