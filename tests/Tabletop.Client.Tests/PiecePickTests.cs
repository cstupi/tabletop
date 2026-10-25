using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class PiecePickTests
{
    static readonly Guid A = TableUx.ModelOf(TableUx.Squad), B = TableUx.ModelOf(TableUx.Squad, 1), C = TableUx.ModelOf(TableUx.Squad, 2);
    static Names NamesOf() => new(TableUx.Game, TableUx.After(TableUx.Deploy));

    [Fact]
    public void Toggling_adds_then_removes_in_order()
    {
        var pick = new PiecePick(new PieceSelection([A, B, C], 1, 2), [A, B, C]);
        Assert.Equal(PickChange.Added, pick.Toggle(B)); Assert.Equal(PickChange.Added, pick.Toggle(A));
        Assert.Equal([B, A], pick.Picked);
        Assert.Equal(PickChange.Unchanged, pick.Toggle(C));          // past Max
        Assert.Equal(PickChange.Removed, pick.Toggle(B));
        Assert.Equivalent(new SelectedPieces([A]), pick.Answer());
    }

    [Fact]
    public void Not_eligible_is_unchanged()
        => Assert.Equal(PickChange.Unchanged, new PiecePick(new PieceSelection([A, B], 1, 1), [A]).Toggle(B));

    [Fact]
    public void One_of_one_answers_at_once()
    {
        var pick = new PiecePick(new PieceSelection([A, B], 1, 1), [A, B]);
        Assert.False(pick.AnswersAtOnce);
        pick.Toggle(A);
        Assert.True(pick.AnswersAtOnce);
    }

    [Fact]
    public void Done_waits_for_the_minimum()
    {
        var pick = new PiecePick(new PieceSelection([A, B, C], 2, 3), [A, B, C]);
        pick.Toggle(A);
        Assert.False(pick.Ready); Assert.Null(pick.Answer()); Assert.Equal("1 of 2–3", pick.Count(NamesOf()));
        pick.Toggle(B);
        Assert.True(pick.Ready); Assert.Equivalent(new SelectedPieces([A, B]), pick.Answer());
    }

    [Fact]
    public void One_pick_at_max_one_counts_as_its_name()
    {
        var names = NamesOf();
        var pick = new PiecePick(new PieceSelection([A, B], 1, 1), [A, B]);
        pick.Toggle(B);
        Assert.Equal(names.Piece(B), pick.Count(names));
    }
}
