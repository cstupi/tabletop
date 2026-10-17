using Tabletop.Client.Core; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class LobbyViewSeatsTests
{
    static readonly Guid T1 = Guid.CreateVersion7(), T2 = Guid.CreateVersion7(), Me = Guid.CreateVersion7(), Ann = Guid.CreateVersion7(), Bob = Guid.CreateVersion7(), Acct = Guid.CreateVersion7();
    static GameInfo Game => new(Guid.CreateVersion7(), "g", GameStatus.Lobby, Acct, "grimdark-future", "h", SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 0,
        [new Team(T2, 2), new Team(T1, 1)],
        [new Participant(Me, Acct, "Clay", T1, Guid.CreateVersion7()), new Participant(Ann, Guid.CreateVersion7(), "Ann", null, null), new Participant(Bob, Guid.CreateVersion7(), "Bob", T1, null)]);

    [Fact]
    public void Every_participant_is_a_row_teamless_included_with_choices_no_team_then_teams_by_ordinal()
    {
        var rows = LobbyView.Players(Game);
        Assert.Equal(new[] { "Clay", "Ann", "Bob" }, rows.Select(r => r.Name));
        Assert.Equal(new[] { "Team 1", "No team", "Team 1" }, rows.Select(r => r.Seat));
        Assert.Equal(new[] { true, false, false }, rows.Select(r => r.HasArmy));
        Assert.Equal(new[] { "No team", "Team 1", "Team 2" }, LobbyView.SeatChoices(Game).Select(c => c.Label));
        Assert.Equal(new[] { 1, 0, 1 }, rows.Select(r => r.Selected));
    }

    [Fact]
    public void Picking_a_choice_places_that_participant()
    {
        Assert.Equal(new PickTeam(T2, Ann), LobbyView.Place(Game, Ann, 2));
        Assert.Equal(new PickTeam(null, Bob), LobbyView.Place(Game, Bob, 0));
    }
}
