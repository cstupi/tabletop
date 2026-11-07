using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class ReplayBarViewTests
{
    static GameEvent E(long seq, EventPayload p, Guid? actor = null) => new(seq, actor, p, null);

    [Fact]
    public void The_buttons_go_back_by_round_turn_action_step_then_forward_by_step_action_turn_round()
    {
        Assert.Equal([ReplayUnit.Round, ReplayUnit.Turn, ReplayUnit.Action, ReplayUnit.Step], ReplayBarView.Back.Select(b => b.Unit));
        Assert.Equal([ReplayUnit.Step, ReplayUnit.Action, ReplayUnit.Turn, ReplayUnit.Round], ReplayBarView.Forward.Select(b => b.Unit));
        Assert.All(ReplayBarView.Back, b => Assert.True(b.Back));
        Assert.All(ReplayBarView.Forward, b => Assert.False(b.Back));
        Assert.Equal(["Round", "Turn", "Action", "Step"], ReplayBarView.Back.Select(b => b.Label));
        Assert.Equal(["step-back", "skip-back", "skip-back", "skip-back"], ReplayBarView.Back.Select(b => b.Icon).Reverse());
    }

    [Fact]
    public void A_tick_marks_each_round_start_along_the_log()
    {
        List<GameEvent> log =
        [
            E(1, new RoundStarted(1, [Samples.Team1], [])), E(2, new TurnStarted(Samples.Team1)), E(3, new ScoreSet(Samples.Team1, 1), Samples.P1),
            E(4, new ScoreSet(Samples.Team1, 2), Samples.P1), E(5, new RoundStarted(2, [Samples.Team1], [])), E(6, new TurnStarted(Samples.Team1)),
            E(7, new ScoreSet(Samples.Team1, 3), Samples.P1), E(8, new ScoreSet(Samples.Team1, 4), Samples.P1),
        ];
        Assert.Equal([new ReplayTick(1 / 8.0, 1), new ReplayTick(5 / 8.0, 2)], ReplayBarView.Ticks(log));
        Assert.Empty(ReplayBarView.Ticks([]));
    }

    [Fact]
    public void The_caption_is_the_latest_live_action_s_header_with_the_round_spelled_out()
    {
        var block = new ActivationBlock(5, E(5, new ScoreSet(Samples.Team1, 1)), false, "R2 · Team 1 · Red unit 1 · Charge → Blue unit 2", null, [], [], []);
        var undone = block with { Seq = 9, Undone = true, Header = "R2 · Team 1 · Red unit 3 · Hold" };
        Assert.Equal("Round 2 · Team 1 · Red unit 1 · Charge → Blue unit 2", ReplayBarView.Caption([block, undone], 2));
        Assert.Equal("Round 3", ReplayBarView.Caption([], 3));
        Assert.Equal("Before round 1", ReplayBarView.Caption([], 0));
    }
}
