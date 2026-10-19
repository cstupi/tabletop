using Tabletop.Content;
using Tabletop.Content.Tests;

namespace Tabletop.Protocol.Tests;

public class WaitingForPlayersTests
{
    static readonly Guid T1 = Guid.CreateVersion7(), T2 = Guid.CreateVersion7(), Account = Guid.CreateVersion7();

    static GameInfo Game(params Participant[] participants) => new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, Account, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 0, [new Team(T1, 1), new Team(T2, 2)], participants);
    static Board Board(params Guid[] withArmies) => new(TableStateTests.Sample(), withArmies.Select(p => new ArmyInPlay(p, ArmySnapshot.Of(ArmyTests.Sample(), Guid.CreateVersion7()))).ToList());

    [Fact]
    public void A_team_with_no_participant_waits_for_a_player_and_one_with_no_army_waits_for_its_army()
    {
        var p1 = new Participant(Guid.CreateVersion7(), Account, "Ann", T1, null);
        var waiting = WaitingForPlayers.Teams(Game(p1), Board(p1.Id));
        Assert.Equal("Waiting for a player on Team 2", Assert.Single(waiting).Text);

        var p2 = new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), "Bob", T2, null);
        Assert.Equal("Team 2 has no army", Assert.Single(WaitingForPlayers.Teams(Game(p1, p2), Board(p1.Id))).Text);
        Assert.Empty(WaitingForPlayers.Teams(Game(p1, p2), Board(p1.Id, p2.Id)));
    }

    /// <summary>Owner ruling: "You need an army on your team." A seat with no army commands the team's units, so one army on the team is enough.</summary>
    [Fact]
    public void A_team_of_two_players_waits_only_until_one_of_them_has_an_army()
    {
        var ann = new Participant(Guid.CreateVersion7(), Account, "Ann", T1, null);
        var cid = new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), "Cid", T1, null);
        var bob = new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), "Bob", T2, null);
        Assert.Equal("Team 1 has no army", Assert.Single(WaitingForPlayers.Teams(Game(ann, cid, bob), Board(bob.Id))).Text);
        Assert.Empty(WaitingForPlayers.Teams(Game(ann, cid, bob), Board(ann.Id, bob.Id)));
        Assert.Empty(WaitingForPlayers.Teams(Game(ann, cid, bob), Board(cid.Id, bob.Id)));
        Assert.Empty(WaitingForPlayers.Teams(Game(ann, cid, bob), Board(ann.Id, cid.Id, bob.Id)));
    }

    [Fact]
    public void One_account_holding_both_teams_with_an_army_each_waits_for_nothing()
    {
        var a = new Participant(Guid.CreateVersion7(), Account, "Ann", T1, null);
        var b = new Participant(Guid.CreateVersion7(), Account, "Ann", T2, null);
        Assert.Empty(WaitingForPlayers.Teams(Game(a, b), Board(a.Id, b.Id)));
    }

    [Fact]
    public void The_wait_holds_only_while_playing_with_an_empty_log()
    {
        var p1 = new Participant(Guid.CreateVersion7(), Account, "Ann", T1, null);
        Assert.True(WaitingForPlayers.Holds(Game(p1), Board(p1.Id), 0));
        Assert.False(WaitingForPlayers.Holds(Game(p1), Board(p1.Id), 1));
        Assert.False(WaitingForPlayers.Holds(Game(p1) with { Status = GameStatus.Setup }, Board(p1.Id), 0));
        Assert.False(WaitingForPlayers.Holds(Game(p1), null, 0));
    }
}
