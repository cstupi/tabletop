using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class GameOverViewTests
{
    static readonly GameInfo Game = Samples.Game;
    static readonly Ask Asked = new(Samples.AskId, AskQuestions.GameOver, "4 rounds have been played", [Samples.Team1, Samples.Team2],
        new Options([new AskOption(GameOverChoices.End, "End", []), new AskOption(GameOverChoices.Continue, "Continue", [])]), false, true, null, null);

    static Projection After(params EventPayload[] history)
    {
        var a = new EventApplier(Samples.Board, Game);
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        return a.Projection;
    }

    [Fact]
    public void Answer_is_the_chosen_option_for_the_game_over_ask_and_team()
    {
        var model = GameOverView.Of(After(new AskOpened(Asked)), Game, Samples.Team1);
        Assert.Equivalent(new AnswerAsk(Samples.AskId, new Chosen(GameOverChoices.End), Samples.Team1), GameOverView.Answer(model, GameOverChoices.End, Samples.Team1));
    }

    [Fact]
    public void Nothing_shows_while_the_game_runs_and_no_game_over_ask_is_open()
    {
        Assert.Equal(GameOverStage.None, GameOverView.Of(After(new RoundStarted(4, [], [])), Game, Samples.Team1).Stage);
        Assert.Equal(GameOverStage.None, GameOverView.Of(After(new AskOpened(Samples.Ask)), Game, Samples.Team1).Stage);
    }

    [Fact]
    public void The_game_over_ask_offers_its_choices_until_my_team_answers_then_waits()
    {
        var m = GameOverView.Of(After(new AskOpened(Asked)), Game, Samples.Team1);
        Assert.Equal((GameOverStage.Choose, Samples.AskId, "4 rounds have been played", (string?)null), (m.Stage, m.AskId, m.Reason, m.Note));
        Assert.Equal(["End", "Continue"], m.Choices.Select(c => c.Label));

        var answered = GameOverView.Of(After(new AskOpened(Asked), new AskAnswered(Samples.AskId, Samples.Team1, new Chosen(GameOverChoices.Continue), false)), Game, Samples.Team1);
        Assert.Equal((GameOverStage.Waiting, "Waiting for Team 2"), (answered.Stage, answered.Note));
        Assert.Equal([Samples.Team2], answered.WaitingFor!);
        Assert.Empty(answered.Choices);
        Assert.Equal(GameOverStage.Waiting, GameOverView.Of(After(new AskOpened(Asked)), Game, null).Stage);   // a watcher has nothing to answer
    }

    [Fact]
    public void Once_ended_it_shows_the_result_and_reason()
    {
        var m = GameOverView.Of(After(new AskOpened(Asked), new AskClosed(Samples.AskId, []), new GameEnded("4 rounds have been played. 2 to 1", [Samples.Team1])), Game, Samples.Team1);
        Assert.Equal((GameOverStage.Ended, (Guid?)null, "4 rounds have been played. 2 to 1"), (m.Stage, m.AskId, m.Reason));
        Assert.Equal("Team 1 wins", m.Result);
        Assert.Equal("Team 1 wins", m.Headline);   // no round played in this log
        Assert.Equal("Team 1 wins", (m with { Rounds = 4 }).Headline);   // the server's reason names the rounds: said once
        Assert.Equal("Waiting for the other player", GameOverView.WaitingText([], new Names(Game, null)));
        Assert.Empty(m.Choices);
    }
}
