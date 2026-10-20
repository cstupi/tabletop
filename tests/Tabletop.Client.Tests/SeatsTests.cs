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
    public void Between_turns_an_account_on_both_teams_stays_with_the_team_that_last_had_the_turn()
    {
        var applier = new EventApplier(Samples.Board, BothSides);
        applier.ApplyAll([new GameEvent(1, null, new TurnStarted(Samples.Team2), null), new GameEvent(2, null, new TurnEnded(Samples.Team2, false, []), null)]);
        Assert.Equal(Samples.Team2, Seats.Acting(applier.Projection, [Samples.Team1, Samples.Team2], applier.Log));
        Assert.Equal(Samples.Team1, Seats.Acting(applier.Projection, [Samples.Team1, Samples.Team2]));   // no log: my first team
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

    /// <summary>Owner ruling: "You need an army on your team." Cid joins Clay's team with no army: Clay's army is the team's, so Cid is not
    /// prompted; on a team with no army any of its players is offered the picker.</summary>
    [Fact]
    public void Only_a_team_with_no_army_is_offered_the_army_picker()
    {
        var cid = Guid.CreateVersion7();
        var game = Samples.Game with { Participants = [.. Samples.Game.Participants, new Participant(cid, Guid.CreateVersion7(), "Cid", Samples.Team1, null),
            new Participant(Second, Guid.CreateVersion7(), "Bob", Samples.Team2, null)] };
        var bobs = new ArmyInPlay(Second, ArmySnapshot.Of(ArmyTests.Sample(), Guid.CreateVersion7()));
        var armed = Samples.Board with { Armies = [.. Samples.Board.Armies, bobs] };
        Assert.Empty(WaitingView.Of(game, WaitingForPlayers.Teams(game, armed), cid));

        var unarmed = Samples.Board with { Armies = [bobs] };
        var line = Assert.Single(WaitingView.Of(game, WaitingForPlayers.Teams(game, unarmed), cid));
        Assert.Equal("Team 1 has no army", line.Text);
        Assert.NotNull(line.Army);
    }

    /// <summary>Clay picks his army while Bob's team still waits: the game info naming the army arrives before the board that holds it. The panel
    /// keeps its controls (an open dropdown stays open) until the lines change, and rebuilds when the board takes Team 1 off the list.</summary>
    [Fact]
    public void The_waiting_panel_rebuilds_only_when_its_lines_change()
    {
        var game = Samples.Game with { Participants = [.. Samples.Game.Participants, new Participant(Second, Guid.CreateVersion7(), "Bob", Samples.Team2, null)] };
        var empty = Samples.Board with { Armies = [] };
        var shown = WaitingView.Of(game, WaitingForPlayers.Teams(game, empty), Samples.P1);
        Assert.Equal(["Team 1 has no army", "Team 2 has no army"], shown.Select(l => l.Text));
        Assert.True(WaitingView.Changed(null, shown));                                          // a new panel builds
        Assert.False(WaitingView.Changed(shown, WaitingView.Of(game, WaitingForPlayers.Teams(game, empty), Samples.P1)));

        var picked = game with { Participants = [game.Participants[0] with { ArmyId = Samples.Army.Id }, game.Participants[1]] };
        Assert.False(WaitingView.Changed(shown, WaitingView.Of(picked, WaitingForPlayers.Teams(picked, empty), Samples.P1)));

        var now = WaitingView.Of(picked, WaitingForPlayers.Teams(picked, Samples.Board), Samples.P1);
        Assert.Equal("Team 2 has no army", Assert.Single(now).Text);
        Assert.True(WaitingView.Changed(shown, now));
    }
}
