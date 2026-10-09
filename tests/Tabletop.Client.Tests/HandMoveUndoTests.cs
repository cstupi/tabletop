using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class HandMoveUndoTests
{
    static readonly Guid M = Guid.Parse("0198b7ac-0021-7000-8000-000000000021");

    [Fact]
    public void A_hand_move_arms_the_undo_to_the_event_before_it()
    {
        var u = new HandMoveUndo();
        Assert.Null(u.UndoTo);
        u.OnOwnMove(5, [M]);
        u.OnEvent(5);                     // the move itself
        Assert.Equal(4L, u.UndoTo);
        Assert.Equal([M], u.Models);
    }

    /// <summary>Review Focus 2: an undo to before the move would take any later event with it.</summary>
    [Fact]
    public void A_later_event_expires_the_undo()
    {
        var u = new HandMoveUndo();
        u.OnOwnMove(5, [M]);
        u.OnEvent(6);
        Assert.Null(u.UndoTo);
        Assert.Empty(u.Models);
        u.OnOwnMove(7, [M]);              // a fresh move arms it again
        Assert.Equal(6L, u.UndoTo);
    }
}
