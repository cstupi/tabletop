using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Game-flow spec → One account, several teams; → Waiting for players.</summary>
public class SeatsTests
{
    static readonly Guid Account = Guid.CreateVersion7(), Second = Guid.CreateVersion7();
    static GameInfo BothSides => Samples.Game with { Participants =
        [new Participant(Samples.P1, Account, "Clay", Samples.Team1, null), new Participant(Second, Account, "Clay", Samples.Team2, null)] };

    static Projection WithAsk(GameInfo game, params (Guid Team, bool Answered)[] teams)
    {
        var applier = new EventApplier(Samples.Board, game);
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.Confirm, "Continue", teams.Select(t => t.Team).ToList(), new Confirmation(), false, true, null, null);
        applier.Apply(new GameEvent(1, null, new AskOpened(ask), null));
        var seq = 2L;
        foreach (var (team, answered) in teams) if (answered) applier.Apply(new GameEvent(seq++, null, new AskAnswered(ask.Id, team, new Confirmed(), false), null));
        return applier.Projection;
    }

    [Fact]
    public void An_account_on_both_teams_holds_both_and_acts_for_the_team_an_ask_waits_on()
    {
        var game = BothSides;
        Assert.Equal([Samples.Team1, Samples.Team2], Seats.Teams(game, Second));            // either row names the account
        Assert.Equal(Samples.Team1, Seats.Acting(WithAsk(game, (Samples.Team1, false), (Samples.Team2, false)), [Samples.Team1, Samples.Team2]));
        Assert.Equal(Samples.Team2, Seats.Acting(WithAsk(game, (Samples.Team1, true), (Samples.Team2, false)), [Samples.Team1, Samples.Team2]));
        Assert.Equal(Samples.Team2, Seats.Acting(WithAsk(game, (Samples.Team2, false)), [Samples.Team1, Samples.Team2]));
        Assert.Equal("Playing as Team 2", Seats.PlayingAs(game, [Samples.Team1, Samples.Team2], Samples.Team2));
        Assert.Null(Seats.PlayingAs(game, [Samples.Team1], Samples.Team1));                  // one team needs no label
        Assert.Null(Seats.Acting(null, []));
    }

    [Fact]
    public void The_header_names_each_of_my_teams_whose_move_it_is()
    {
        var game = BothSides; var p = WithAsk(game, (Samples.Team1, false), (Samples.Team2, false));
        Assert.Equal("Your move (Team 1): confirm · Your move (Team 2): confirm", HeaderView.Waiting(p, new Names(game, p), [Samples.Team1, Samples.Team2]));
    }

    [Fact]
    public void While_deployment_waits_everyone_reads_why_and_a_participant_may_join_the_empty_team()
    {
        var game = Samples.Game;                                                              // Clay on team 1, no one on team 2
        var waiting = WaitingForPlayers.Teams(game, Samples.Board);
        var lines = WaitingView.Of(game, waiting, Samples.P1);
        var line = Assert.Single(lines);
        Assert.Equal("Waiting for a player on Team 2", line.Text);
        Assert.Equal(new PickTeam(Samples.Team2), line.Join!.Edit);
        Assert.Null(line.Army);
        Assert.Null(Assert.Single(WaitingView.Of(game, waiting, Guid.Empty)).Join);          // an admin watcher is told, and offered nothing
        Assert.Equal("Deployment waits: Waiting for a player on Team 2", HeaderView.WaitingForPlayers(waiting));

        var mine = game with { Participants = [.. game.Participants, new Participant(Second, game.Participants[0].AccountId, "Clay", Samples.Team2, null)] };
        var army = Assert.Single(WaitingView.Of(mine, WaitingForPlayers.Teams(mine, Samples.Board), Samples.P1)).Army!;
        Assert.Equal(new PickArmy(Samples.Army.Id, Samples.Team2), army.Pick(Samples.Army.Id));
    }
}
