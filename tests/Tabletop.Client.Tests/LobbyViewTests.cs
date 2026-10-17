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
        Assert.Equal(new TeamChoice(Samples.Team2, "Team 2: Ana", false, "Ana holds this team: ask the creator to place you on it."), teams[1]);
    }

    [Fact]
    public void The_creator_may_join_a_held_team_and_my_own_team_is_not_offered()
    {
        var teams = LobbyView.Teams(Lobby(Samples.Team1, Samples.Team2), Me, isCreator: true);
        Assert.Equal((false, "On team 1"), (teams[0].Enabled, teams[0].Text));
        Assert.Equal((true, "Join team 2"), (teams[1].Enabled, teams[1].Text));
        Assert.Equal("Held by Ana; as the creator you may join it.", teams[1].Why);
    }

    [Fact]
    public void The_seat_line_says_where_I_sit_and_what_is_missing()
    {
        Assert.Equal("You have no team: join one, or stay without one to watch once play starts.", LobbyView.Seat(Lobby(null, null), Me));
        Assert.Equal("You are on Team 1. Pick an army below.", LobbyView.Seat(Lobby(Samples.Team1, null), Me));
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
}
