using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class LobbyViewTests
{
    static readonly Guid Me = Guid.CreateVersion7(), Other = Guid.CreateVersion7();

    static GameInfo Lobby(Guid? myTeam, Guid? otherTeam, Guid? myArmy = null) => Samples.Game with
    {
        Status = GameStatus.Lobby,
        Participants = [new Participant(Me, Guid.CreateVersion7(), "Clay", myTeam, myArmy), new Participant(Other, Guid.CreateVersion7(), "Ana", otherTeam, null)],
    };

    [Fact]
    public void An_empty_team_is_joined_and_a_held_one_names_its_holder_and_the_creator()
    {
        var teams = LobbyView.Teams(Lobby(null, Samples.Team2), Me, isCreator: false);
        Assert.Equal(new TeamChoice(Samples.Team1, "Join team 1", true, ""), teams[0]);
        Assert.Equal(new TeamChoice(Samples.Team2, "Team 2: Ana · ask the creator to add you", false,
            "Ana holds this team; the creator adds you from the Players list."), teams[1]);
    }

    [Fact]
    public void The_creator_may_join_a_held_team_and_my_own_team_is_left()
    {
        var teams = LobbyView.Teams(Lobby(Samples.Team1, Samples.Team2), Me, isCreator: true);
        Assert.Equal((true, "Leave team 1", (LobbyEdit)new LeaveTeam(Samples.Team1)), (teams[0].Enabled, teams[0].Text, teams[0].Edit));
        Assert.Equal((true, "Join team 2", (LobbyEdit)new PickTeam(Samples.Team2)), (teams[1].Enabled, teams[1].Text, teams[1].Edit));
        Assert.Equal("Held by Ana; as the creator you may join it. You keep your other team and play both sides.", teams[1].Why);
    }

    /// <summary>Self-play: one account on both teams, a row per team, an army picker per team.</summary>
    [Fact]
    public void One_account_on_both_teams_leaves_either_and_picks_an_army_for_each()
    {
        var account = Guid.CreateVersion7(); var second = Guid.CreateVersion7(); var army = Guid.CreateVersion7();
        var game = Samples.Game with { Status = GameStatus.Lobby, Participants =
            [new Participant(Me, account, "Clay", Samples.Team1, army), new Participant(second, account, "Clay", Samples.Team2, null)] };
        Assert.Equal(["Leave team 1", "Leave team 2"], LobbyView.Teams(game, Me, isCreator: true).Select(t => t.Text));
        Assert.Equal("You are on Team 1 and Team 2. Pick your army below. You play every side you hold.", LobbyView.Seat(game, Me));
        var armies = LobbyView.Armies(game, Me);
        Assert.Equal([("Your army for Team 1", (Guid?)army), ("Your army for Team 2", null)], armies.Select(a => (a.Label, a.ArmyId)));
        Assert.Equal(new PickArmy(army, Samples.Team2), armies[1].Pick(army));
        Assert.Equal([new ArmySeat(null, "Your army", null)], LobbyView.Armies(Lobby(null, null), Me));
    }

    [Fact]
    public void The_seat_line_says_where_I_sit_and_what_is_missing()
    {
        Assert.Equal("You have no team: join one, or stay without one to watch once play starts.", LobbyView.Seat(Lobby(null, null), Me));
        Assert.Equal("You are on Team 1. Pick your army below.", LobbyView.Seat(Lobby(Samples.Team1, null), Me));
        Assert.Equal("You are on Team 1.", LobbyView.Seat(Lobby(Samples.Team1, null, Guid.CreateVersion7()), Me));
        Assert.Equal("You are watching this game.", LobbyView.Seat(Lobby(null, null), Guid.Empty));
    }

    [Fact]
    public void Everyone_but_the_creator_is_told_what_they_wait_for()
    {
        Assert.Null(LobbyView.WaitingForCreator(Lobby(null, null), isCreator: true));
        Assert.Equal("Waiting for the creator to start setup.", LobbyView.WaitingForCreator(Lobby(null, null), isCreator: false));
        Assert.Equal("Anyone may edit the table. Waiting for the creator to end setup.", LobbyView.WaitingForCreator(Lobby(null, null) with { Status = GameStatus.Setup }, isCreator: false));
    }

    [Fact]
    public void End_setup_counts_the_pieces_off_the_table_in_table_words()
    {
        var table = Tabletop.Content.Tests.TableStateTests.Sample();
        var piece = table.Terrain[0];
        Tabletop.Content.TableState With(params (double X, double Y)[] at) =>
            table with { Terrain = at.Select(p => piece with { Id = Guid.CreateVersion7(), Position = new Tabletop.Geometry.Point3(p.X, p.Y, 0) }).ToList() };
        Assert.Equal("End setup", LobbyView.EndSetupText(null));
        Assert.Equal("End setup", LobbyView.EndSetupText(With((72, 48), (10, 10))));      // the edge is on the table
        Assert.Equal("End setup (1 piece off the table will be removed)", LobbyView.EndSetupText(With((76, 4), (10, 10))));
        Assert.Equal("End setup (3 pieces off the table will be removed)", LobbyView.EndSetupText(With((76, 4), (-1, 4), (10, 49))));
    }

    [Fact]
    public void A_team_card_lists_each_player_with_their_army_or_says_it_has_none()
    {
        var game = Lobby(Samples.Team1, Samples.Team1, myArmy: Guid.CreateVersion7());
        game = game with { Participants = [game.Participants[0] with { ArmyName = "Iron Legion" }, game.Participants[1]] };
        Assert.Equal("Clay ⚔ Iron Legion\nAna · no army yet", LobbyView.Members(game, Samples.Team1));
        Assert.Equal("No players yet", LobbyView.Members(game, Samples.Team2));
    }

    /// <summary>Rulebook p.50 Team Games: two players on a side, each with their own army picker.</summary>
    [Fact]
    public void Two_players_on_one_team_each_pick_their_own_army()
    {
        var game = Lobby(Samples.Team1, Samples.Team1);
        Assert.Equal([new ArmySeat(Samples.Team1, "Your army", null)], LobbyView.Armies(game, Me));
        Assert.Equal([new ArmySeat(Samples.Team1, "Your army", null)], LobbyView.Armies(game, Other));
        Assert.Equal("You are on Team 1. Pick your army below. Teammates: Ana.",
            LobbyView.Seat(game, Me));
    }
}
