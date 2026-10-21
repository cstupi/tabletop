using Tabletop.Protocol;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Lifecycle;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests.Lifecycle;

public class LifecycleScenarioTests
{
    /// <summary>A game as the server sends it: two seats, the recorder first among the participants (the rows' own order), not last.</summary>
    static (GameInfo Info, Board Board, Guid Recorder) Served(bool selfPlay = false)
    {
        var builder = new ScenarioBuilder("served", 1).Army("Red", 2, 2).Army("Blue", 1, 3);
        var compiled = ScenarioCompiler.Compile((selfPlay ? builder.SelfPlay() : builder).Build());
        var recorder = compiled.Info.Participants[^1];
        var info = compiled.Info with { Participants = [recorder, .. compiled.Info.Participants.SkipLast(1)] };
        return (info, compiled.Board, recorder.Id);
    }

    static readonly Dictionary<Guid, IReadOnlyList<int>> None = [];

    [Fact]
    public void The_seats_follow_the_boards_armies_and_the_recorder_goes_last()
    {
        var (info, board, recorder) = Served();
        var compiled = LifecycleScenario.From("two-players", 1, info, board, recorder, selfPlay: false, None, roundCap: 5);
        Assert.Equal(board.Armies.Select(a => a.ParticipantId), compiled.ParticipantIds);
        Assert.Equal(recorder, compiled.Info.Participants[^1].Id);
        Assert.Equal(recorder, compiled.RecorderParticipantId);
        Assert.Equal([.. board.Armies.Select(a => a.ParticipantId), recorder], compiled.Info.Participants.Select(p => p.Id));
        Assert.Equal(5, compiled.Scenario.RoundCap);
        Assert.Equal([1, 2], compiled.Scenario.Armies.Select(a => a.Team));
        Assert.Contains(compiled.Scenario.Expectations, e => e is Expectation.Outcome("ended"));
    }

    [Fact]
    public void Set_aside_units_go_to_their_seats_army()
    {
        var (info, board, recorder) = Served();
        var blue = board.Armies[1].ParticipantId;
        var compiled = LifecycleScenario.From("ambusher", 1, info, board, recorder, false, new Dictionary<Guid, IReadOnlyList<int>> { [blue] = [1] }, 5);
        Assert.Empty(compiled.Scenario.Armies[0].SetAside);
        Assert.Equal([1], compiled.Scenario.Armies[1].SetAside);
    }

    [Fact]
    public void Self_play_connects_the_accounts_lowest_row()
    {
        var (info, board, recorder) = Served(selfPlay: true);
        var compiled = LifecycleScenario.From("self-play", 1, info, board, recorder, selfPlay: true, None, 5);
        var account = compiled.Info.Participants[0].AccountId;
        Assert.Equal([compiled.Info.Participants.Where(p => p.AccountId == account).Min(p => p.Id)], compiled.Connected);

        var reversed = board with { Armies = board.Armies.Reverse().ToList() };
        Assert.Throws<InvalidOperationException>(() => LifecycleScenario.From("self-play", 1, info, reversed, recorder, true, None, 5));
    }

    [Fact]
    public void A_game_on_another_set_or_a_recorder_with_a_team_is_refused()
    {
        var (info, board, recorder) = Served();
        Assert.Throws<InvalidOperationException>(() => LifecycleScenario.From("x", 1, info with { PropertiesHash = "sha256:old" }, board, recorder, false, None, 5));
        Assert.Throws<InvalidOperationException>(() => LifecycleScenario.From("x", 1, info, board, board.Armies[0].ParticipantId, false, None, 5));
    }

    [Fact]
    public void Credentials_follow_the_participants_by_account()
    {
        var (info, board, recorder) = Served();
        var compiled = LifecycleScenario.From("two-players", 1, info, board, recorder, false, None, 5);
        var sessions = compiled.Info.Participants.ToDictionary(p => p.AccountId, p => new MintedSession($"jwt-{p.Name}", p.AccountId));
        var refreshed = new List<Guid>();
        var credentials = LifecycleScenario.Credentials(compiled, sessions, (account, _) =>
        {
            refreshed.Add(account);
            return Task.FromResult(sessions[account]);
        });
        Assert.Equal(compiled.Info.Participants.Select(p => p.AccountId), credentials.Sessions.Select(s => s.AccountId));
        credentials.Refresh!(2, CancellationToken.None);
        Assert.Equal([compiled.Info.Participants[2].AccountId], refreshed);
    }
}
