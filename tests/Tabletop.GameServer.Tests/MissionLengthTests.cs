using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>GF p.7: "After 4 rounds have been played the game ends". silent lets round 5 start, warn marks it, strict ends the game.</summary>
public class MissionLengthTests
{
    static SchedulerHarness Game(Enforcement enforcement)
    {
        var rules = FakeRules.Gf();
        rules.JudgeAdvanceHook = GrimdarkFutureRules.Instance.JudgeAdvance;
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)], enforcement: enforcement, rules: rules);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        return h;
    }

    /// <summary>Every open activation answered with its first option, then the boundary question confirmed.</summary>
    static void PlayRound(SchedulerHarness h)
    {
        for (var i = 0; i < 20 && h.Open.Question != AskQuestions.EndRound; i++)
            h.Answer(h.Teams.ToList().IndexOf(h.Open.Teams[0]), new Chosen(((Options)h.Open.Legal).Choices[0].Id));
        h.EndRound();
    }

    static GameEvent LastOf<T>(SchedulerHarness h) where T : EventPayload => h.Log.Last(e => e.Payload is T);

    [Fact]
    public void Strict_ends_the_game_when_round_four_is_ended()
    {
        var h = Game(Enforcement.Strict);
        for (var r = 0; r < 3; r++) PlayRound(h);
        Assert.Equal(4, h.P.Scheduler.Round);
        Assert.Null(h.P.Ended);
        PlayRound(h);
        var end = Assert.IsType<GameEnded>(h.Log[^1].Payload);
        Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(4), end.Reason);
        Assert.Equal("4 rounds have been played: the game ends after 4 (p.7)", end.Reason);
        Assert.Null(h.Log[^1].Actor);
        Assert.Equal(4, h.P.Scheduler.Round);
        Assert.Same(end, h.P.Ended);
        Assert.Empty(h.P.OpenAsks);
        Assert.Empty(h.Run());                                         // the judge does not fire again, and nothing else advances
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Warn_marks_round_five_and_every_round_after()
    {
        var h = Game(Enforcement.Warn);
        for (var r = 0; r < 4; r++) PlayRound(h);
        var fifth = LastOf<RoundStarted>(h);
        Assert.Equal(5, ((RoundStarted)fifth.Payload).Round);
        Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(4), fifth.Warning);
        Assert.Null(h.P.Ended);
        Assert.Equal(1, h.Log.Count(e => e.Warning is not null));      // once, not on every Advance
        PlayRound(h);
        Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(5), LastOf<RoundStarted>(h).Warning);
        Assert.DoesNotContain(h.Log, e => e.Payload is GameEnded);
    }

    [Fact]
    public void Silent_starts_round_five_with_nothing_said()
    {
        var h = Game(Enforcement.Silent);
        for (var r = 0; r < 4; r++) PlayRound(h);
        Assert.Equal(5, h.P.Scheduler.Round);
        Assert.All(h.Log, e => Assert.Null(e.Warning));
        Assert.DoesNotContain(h.Log, e => e.Payload is GameEnded);
    }

    [Fact]
    public void A_menu_round_after_a_strict_end_reopens_play_until_the_next_boundary()
    {
        var h = Game(Enforcement.Strict);
        for (var r = 0; r < 4; r++) PlayRound(h);
        h.Dispatch(new SetRound(5), team: 0);
        Assert.Null(h.P.Ended);
        Assert.Equal((5, AskQuestions.Activate), (h.P.Scheduler.Round, h.Open.Question));
        PlayRound(h);
        Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(5), Assert.IsType<GameEnded>(h.Log[^1].Payload).Reason);
        Assert.Equal(5, h.P.Scheduler.Round);
    }

    [Fact]
    public void Undoing_only_the_end_ends_the_game_again_at_once()
    {
        var h = Game(Enforcement.Strict);
        for (var r = 0; r < 4; r++) PlayRound(h);
        var endSeq = LastOf<GameEnded>(h).Seq;
        h.Dispatch(new Undo(endSeq - 1), team: 0);
        Assert.IsType<GameEnded>(h.Log[^1].Payload);
        Assert.NotNull(h.P.Ended);
        Assert.Equal(2, h.Log.Count(e => e.Payload is GameEnded));
    }

    [Fact]
    public void The_judge_reads_only_a_round_past_the_fourth_and_survives_a_wrapper()
    {
        var h = Game(Enforcement.Strict);
        var gf = GrimdarkFutureRules.Instance;
        Assert.Null(gf.JudgeAdvance(h.State, TestProperties.Core, [new RoundStarted(4, [], [])]));
        Assert.Null(gf.JudgeAdvance(h.State, TestProperties.Core, [new TurnStarted(h.Teams[0])]));
        var breach = gf.JudgeAdvance(h.State, TestProperties.Core, [new RoundStarted(5, [], [])]);
        Assert.Equal((GrimdarkFutureRules.MissionLength, GrimdarkFutureRules.MissionLengthDetail(4)), (breach!.Rule, breach.Detail));
        Assert.NotNull(new WithSequences(gf).JudgeAdvance(h.State, TestProperties.Core, [new RoundStarted(5, [], [])]));
    }
}
