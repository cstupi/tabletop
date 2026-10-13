using Tabletop.GameServer.Log;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class ReplayTests
{
    static GameEvent Ev(long seq, EventPayload p) => new(seq, Samples.P1, p, null);
    static readonly GameEvent[] Log =
    [
        Ev(1, new ScoreSet(Samples.Team1, 1)), Ev(2, new ScoreSet(Samples.Team1, 2)), Ev(3, new ScoreSet(Samples.Team1, 3)),
        Ev(4, new Undone(1)), Ev(5, new ScoreSet(Samples.Team2, 9)),
    ];

    [Theory]
    [InlineData(0, 0, 0)] [InlineData(1, 1, 0)] [InlineData(2, 2, 0)] [InlineData(3, 3, 0)] [InlineData(4, 1, 0)] [InlineData(5, 1, 9)]
    public void Replay_to_n_lands_on_the_settled_state_at_n(long seq, int team1, int team2)
    {
        var p = Replay.To(Samples.Board, Samples.Game, Log, seq);
        Assert.Equal((team1, team2), (p.Scores[Samples.Team1], p.Scores[Samples.Team2]));
    }

    [Fact]
    public void Undo_targets_are_zero_or_a_live_seq()
    {
        var applier = new EventApplier(Samples.Board, Samples.Game);
        applier.ApplyAll(Log);
        Assert.True(Replay.IsValidUndoTarget(applier, 0));
        Assert.True(Replay.IsValidUndoTarget(applier, 1));
        Assert.True(Replay.IsValidUndoTarget(applier, 5));
        Assert.False(Replay.IsValidUndoTarget(applier, 2));   // undone
        Assert.False(Replay.IsValidUndoTarget(applier, 4));   // the marker itself
        Assert.False(Replay.IsValidUndoTarget(applier, 6));   // the future
    }

    /// <summary>The answer that closes an Ask and the close are one command: between them the Ask is answered but open, and nothing reopens it.</summary>
    [Fact]
    public void The_seq_between_an_answer_and_its_close_is_not_a_target()
    {
        var answer = new Chosen("rush");
        var applier = new EventApplier(Samples.Board, Samples.Game);
        applier.ApplyAll([
            Ev(1, new AskOpened(Samples.Ask)), Ev(2, new AskAnswered(Samples.AskId, Samples.Team1, answer, false)),
            Ev(3, new AskClosed(Samples.AskId, [new TeamAnswer(Samples.Team1, answer)])), Ev(4, new ScoreSet(Samples.Team1, 1)),
        ]);
        Assert.True(Replay.IsValidUndoTarget(applier, 1));
        Assert.False(Replay.IsValidUndoTarget(applier, 2));
        Assert.True(Replay.IsValidUndoTarget(applier, 3));
    }
}
