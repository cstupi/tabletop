using Tabletop.Content;
using Tabletop.Content.Tests;

namespace Tabletop.Protocol.Tests;

public class GameProgressTests
{
    static readonly Guid T1 = Guid.CreateVersion7(), T2 = Guid.CreateVersion7(), Ann = Guid.CreateVersion7(), Bob = Guid.CreateVersion7();
    static readonly Participant P1 = new(Guid.CreateVersion7(), Ann, "Ann", T1, null), P2 = new(Guid.CreateVersion7(), Bob, "Bob", T2, null);
    static readonly GameInfo Game = new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, Ann, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 0, [new Team(T1, 1), new Team(T2, 2)], [P1, P2]);

    static Projection After(params EventPayload[] events)
    {
        var board = new Board(TableStateTests.Sample(), [new ArmyInPlay(P1.Id, ArmySnapshot.Of(ArmyTests.Sample(), Guid.CreateVersion7()))]);
        var applier = new EventApplier(board, Game);
        applier.ApplyAll(events.Select((e, i) => new GameEvent(i + 1, null, e, null)));
        return applier.Projection;
    }

    static Ask AskFor(Guid id, params Guid[] teams) => new(id, AskQuestions.Activate, "Activate", teams, Samples.LegalSets[0], true, false, null, null);

    [Fact]
    public void Before_the_first_round_nothing_is_set()
    {
        Assert.True(GameProgress.Of(After(), Game).SameAs(new GameProgress(0, null, [], null)));
    }

    [Fact]
    public void The_round_the_acting_team_and_the_accounts_an_open_ask_waits_on()
    {
        var ask = Guid.CreateVersion7();
        var p = GameProgress.Of(After(new RoundStarted(2, [T2, T1], []), new TurnStarted(T2), new AskOpened(AskFor(ask, T2))), Game);
        Assert.Equal(2, p.Round);
        Assert.Equal(2, p.ActingTeam);
        Assert.Equal([Bob], p.WaitingOn);
        Assert.Null(p.Outcome);
    }

    [Fact]
    public void A_team_that_answered_is_no_longer_waited_on()
    {
        var ask = Guid.CreateVersion7();
        var p = GameProgress.Of(After(new RoundStarted(1, [T1, T2], []), new AskOpened(AskFor(ask, T1, T2)), new AskAnswered(ask, T1, null, false)), Game);
        Assert.Equal([Bob], p.WaitingOn);
    }

    [Fact]
    public void An_ended_game_carries_the_winners_ordinals_and_waits_on_no_one()
    {
        Assert.Equal([2], GameProgress.Of(After(new RoundStarted(4, [T1, T2], []), new GameEnded("Team 2 holds more objectives", [T2])), Game).Outcome);
        var draw = GameProgress.Of(After(new GameEnded("Tied", [T2, T1])), Game);
        Assert.Equal([1, 2], draw.Outcome);
        Assert.Empty(draw.WaitingOn);
        Assert.Equal([], GameProgress.Of(After(new GameEnded("No result", [])), Game).Outcome);
    }

    [Fact]
    public void SameAs_compares_the_lists_by_item()
    {
        Assert.True(new GameProgress(1, 1, [Ann], [1]).SameAs(new GameProgress(1, 1, [Ann], [1])));
        Assert.False(new GameProgress(1, 1, [Ann], null).SameAs(new GameProgress(1, 1, [Bob], null)));
        Assert.False(new GameProgress(1, 1, [], null).SameAs(new GameProgress(1, 1, [], [])));
    }
}
