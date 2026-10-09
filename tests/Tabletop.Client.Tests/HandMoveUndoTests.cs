using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class HandMoveUndoTests
{
    static readonly Guid M = Guid.Parse("0198b7ac-0021-7000-8000-000000000021");

    static GameEvent Ev(long seq, EventPayload? payload = null) => new(seq, null, payload ?? new PhaseStarted("x"), null);
    static List<GameEvent> Log(int count) => Enumerable.Range(1, count).Select(i => Ev(i)).ToList();

    [Fact]
    public void A_hand_move_arms_the_undo_to_the_event_before_it()
    {
        var u = new HandMoveUndo();
        Assert.Null(u.UndoTo);
        u.OnOwnMove(Log(5), 5, [M]);
        u.OnEvent(5);                     // the move itself
        Assert.Equal(4L, u.UndoTo);
        Assert.Equal([M], u.Models);
    }

    /// <summary>Review Focus 2: an undo to before the move would take any later event with it.</summary>
    [Fact]
    public void A_later_event_expires_the_undo()
    {
        var u = new HandMoveUndo();
        u.OnOwnMove(Log(5), 5, [M]);
        u.OnEvent(6);
        Assert.Null(u.UndoTo);
        Assert.Empty(u.Models);
        u.OnOwnMove(Log(7), 7, [M]);      // a fresh move arms it again
        Assert.Equal(6L, u.UndoTo);
    }

    /// <summary>Drag, undo, drag, undo: the event before the second move is the first undo's marker, which the server refuses as a target.</summary>
    [Fact]
    public void After_an_undo_the_target_is_the_last_live_event_below_the_move()
    {
        var log = new List<GameEvent> { Ev(1), Ev(2), Ev(3), Ev(4, new Undone(2)), Ev(5) };
        var u = new HandMoveUndo();
        u.OnOwnMove(log, 5, [M]);
        Assert.Equal(2L, u.UndoTo);
        log.Add(Ev(6, new Undone(2))); log.Add(Ev(7));
        u.OnEvent(6);
        u.OnOwnMove(log, 7, [M]);
        Assert.Equal(2L, u.UndoTo);
    }

    /// <summary>"Undo to here" from the log before the move: the marker and everything it undid are skipped.</summary>
    [Fact]
    public void After_an_undo_to_here_the_target_skips_the_undone_range()
    {
        var log = new List<GameEvent> { Ev(1), Ev(2), Ev(3), Ev(4), Ev(5, new Undone(1)), Ev(6) };
        var u = new HandMoveUndo();
        u.OnOwnMove(log, 6, [M]);
        Assert.Equal(1L, u.UndoTo);
    }

    [Fact]
    public void A_first_live_move_undoes_to_zero()
    {
        var log = new List<GameEvent> { Ev(1), Ev(2, new Undone(0)), Ev(3) };
        var u = new HandMoveUndo();
        u.OnOwnMove(log, 3, [M]);
        Assert.Equal(0L, u.UndoTo);
    }
}
