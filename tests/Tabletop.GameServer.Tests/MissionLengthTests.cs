using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>GF p.7: "After 4 rounds have been played the game ends". silent lets round 5 start, warn asks every team End or Continue, strict ends the game.</summary>
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
        Assert.StartsWith(GrimdarkFutureRules.MissionLengthDetail(4), end.Reason);
        Assert.Null(h.Log[^1].Actor);
        Assert.Equal(4, h.P.Scheduler.Round);
        Assert.Same(end, h.P.Ended);
        Assert.Empty(h.P.OpenAsks);
        Assert.DoesNotContain(h.Log, e => e.Payload is AskOpened { Ask.Question: AskQuestions.GameOver });
        Assert.Empty(h.Run());                                         // the judge does not fire again, and nothing else advances
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    static void GameOver(SchedulerHarness h, int team, string choice) => h.Answer(team, new Chosen(choice));

    [Fact]
    public void Warn_asks_every_team_end_or_continue_when_round_four_is_ended()
    {
        var h = Game(Enforcement.Warn);
        for (var r = 0; r < 4; r++) PlayRound(h);
        var ask = h.Open;
        Assert.Equal(AskQuestions.GameOver, ask.Question);
        Assert.Equal(h.Teams, ask.Teams);
        Assert.True(ask.Sealed);
        Assert.Null(ask.FrameId);
        Assert.StartsWith(GrimdarkFutureRules.MissionLengthDetail(4), ask.Prompt);
        Assert.Equal([GameOverChoices.End, GameOverChoices.Continue], ((Options)ask.Legal).Choices.Select(c => c.Id));
        Assert.Equal(4, h.P.Scheduler.Round);
        Assert.Null(h.P.Ended);
        Assert.Empty(h.Run());                                         // waits for the answers
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Warn_ends_the_game_at_one_teams_end_with_the_strict_result()
    {
        var h = Game(Enforcement.Warn);
        for (var r = 0; r < 4; r++) PlayRound(h);
        GameOver(h, 1, GameOverChoices.End);
        var end = Assert.IsType<GameEnded>(h.Log[^1].Payload);
        Assert.Equal($"{GrimdarkFutureRules.MissionLengthDetail(4)}. {GfObjectives.Standing(h.State)}", end.Reason);     // the strict breach's detail and winners
        Assert.Equal(GfObjectives.Winners(h.State), end.Winners);
        Assert.Empty(h.P.OpenAsks);
        Assert.Equal(4, h.P.Scheduler.Round);
        Assert.DoesNotContain(h.Log, e => e.Payload is RoundStarted { Round: 5 });
        Assert.Empty(h.Run());
    }

    [Fact]
    public void Warn_ends_the_game_when_one_continues_and_the_other_ends()
    {
        var h = Game(Enforcement.Warn);
        for (var r = 0; r < 4; r++) PlayRound(h);
        GameOver(h, 0, GameOverChoices.Continue);
        Assert.Null(h.P.Ended);
        Assert.Equal(AskQuestions.GameOver, h.OpenAsk(1)!.Question);
        GameOver(h, 1, GameOverChoices.End);
        Assert.IsType<GameEnded>(h.Log[^1].Payload);
    }

    [Fact]
    public void Warn_continues_into_round_five_when_every_team_continues_and_asks_again_at_its_end()
    {
        var h = Game(Enforcement.Warn);
        for (var r = 0; r < 4; r++) PlayRound(h);
        GameOver(h, 0, GameOverChoices.Continue);
        GameOver(h, 1, GameOverChoices.Continue);
        var fifth = LastOf<RoundStarted>(h);
        Assert.Equal(5, ((RoundStarted)fifth.Payload).Round);
        Assert.StartsWith(GrimdarkFutureRules.MissionLengthDetail(4), fifth.Warning);
        Assert.Null(h.P.Ended);
        PlayRound(h);
        Assert.Equal(AskQuestions.GameOver, h.Open.Question);
        Assert.StartsWith(GrimdarkFutureRules.MissionLengthDetail(5), h.Open.Prompt);
        GameOver(h, 0, GameOverChoices.End);
        Assert.StartsWith(GrimdarkFutureRules.MissionLengthDetail(5), Assert.IsType<GameEnded>(h.Log[^1].Payload).Reason);
    }

    [Fact]
    public void A_menu_round_closes_an_open_game_over_ask()
    {
        var h = Game(Enforcement.Warn);
        for (var r = 0; r < 4; r++) PlayRound(h);
        h.Dispatch(new SetRound(3), team: 0);
        Assert.DoesNotContain(h.P.OpenAsks.Values, a => a.Ask.Question == AskQuestions.GameOver);
        Assert.Equal((3, AskQuestions.Activate), (h.P.Scheduler.Round, h.Open.Question));
    }

    [Fact]
    public void Silent_starts_round_five_with_nothing_said()
    {
        var h = Game(Enforcement.Silent);
        for (var r = 0; r < 4; r++) PlayRound(h);
        Assert.Equal(5, h.P.Scheduler.Round);
        Assert.All(h.Log, e => Assert.Null(e.Warning));
        Assert.DoesNotContain(h.Log, e => e.Payload is GameEnded);
        Assert.DoesNotContain(h.Log, e => e.Payload is AskOpened { Ask.Question: AskQuestions.GameOver });
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
        Assert.StartsWith(GrimdarkFutureRules.MissionLengthDetail(5), Assert.IsType<GameEnded>(h.Log[^1].Payload).Reason);
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
        Assert.Equal(GrimdarkFutureRules.MissionLength, breach!.Rule);
        Assert.StartsWith(GrimdarkFutureRules.MissionLengthDetail(4), breach.Detail);
        Assert.NotNull(new WithSequences(gf).JudgeAdvance(h.State, TestProperties.Core, [new RoundStarted(5, [], [])]));
    }
}
