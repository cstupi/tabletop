using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class ModelPicksTests
{
    static readonly Guid U1 = Guid.NewGuid(), U2 = Guid.NewGuid();
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid(), E = Guid.NewGuid();

    [Fact]
    public void Shift_click_adds_a_model_and_a_second_takes_it_out()
    {
        var picks = ModelPicks.None.Toggle(U1, A).Toggle(U1, B);
        Assert.Equal(U1, picks.UnitId);
        Assert.Equal([A, B], picks.ModelIds);
        picks = picks.Toggle(U1, A);
        Assert.Equal([B], picks.ModelIds);
        Assert.True(picks.Toggle(U1, B).IsEmpty);
        Assert.Null(picks.Toggle(U1, B).UnitId);
    }

    [Fact]
    public void A_pick_of_another_units_model_starts_over_with_that_unit()
    {
        var picks = ModelPicks.None.Toggle(U1, A).Toggle(U1, B).Toggle(U2, C);
        Assert.Equal(U2, picks.UnitId);
        Assert.Equal([C], picks.ModelIds);
    }

    [Fact]
    public void A_box_takes_the_models_whose_base_centre_is_inside_it_either_way_it_was_drawn()
    {
        var candidates = new[] { new PickCandidate(A, U1, 10, 10), new PickCandidate(B, U1, 20, 20), new PickCandidate(C, U1, 50, 50) };
        var hit = ModelPicks.InBox(candidates, new ScreenBox(25, 25, 5, 5));
        Assert.NotNull(hit);
        Assert.Equal(U1, hit.Value.UnitId);
        Assert.Equal([A, B], hit.Value.ModelIds);
        Assert.Null(ModelPicks.InBox(candidates, new ScreenBox(60, 60, 70, 70)));
    }

    [Fact]
    public void A_box_over_several_units_takes_the_unit_with_most_models_inside()
    {
        var candidates = new[] { new PickCandidate(A, U1, 1, 1), new PickCandidate(B, U2, 5, 5), new PickCandidate(C, U2, 6, 6), new PickCandidate(D, U1, 100, 100) };
        var hit = ModelPicks.InBox(candidates, new ScreenBox(0, 0, 10, 10))!.Value;
        Assert.Equal(U2, hit.UnitId);
        Assert.Equal([B, C], hit.ModelIds);
    }

    [Fact]
    public void On_a_tie_the_box_takes_the_unit_of_the_model_nearest_the_press()
    {
        var candidates = new[] { new PickCandidate(A, U1, 1, 1), new PickCandidate(B, U2, 9, 9) };
        Assert.Equal(U2, ModelPicks.InBox(candidates, new ScreenBox(10, 10, 0, 0))!.Value.UnitId);   // pressed at (10, 10)
        Assert.Equal(U1, ModelPicks.InBox(candidates, new ScreenBox(0, 0, 10, 10))!.Value.UnitId);   // pressed at (0, 0)
    }

    [Fact]
    public void A_box_adds_to_the_picks_of_its_unit_and_starts_over_for_another()
    {
        var picks = ModelPicks.None.Toggle(U1, A);
        var same = picks.Boxed([new PickCandidate(A, U1, 1, 1), new PickCandidate(B, U1, 2, 2)], new ScreenBox(0, 0, 3, 3));
        Assert.Equal([A, B], same.ModelIds);
        var other = picks.Boxed([new PickCandidate(C, U2, 1, 1)], new ScreenBox(0, 0, 3, 3));
        Assert.Equal(U2, other.UnitId);
        Assert.Equal([C], other.ModelIds);
        Assert.Same(picks, picks.Boxed([new PickCandidate(C, U2, 50, 50)], new ScreenBox(0, 0, 3, 3)));   // an empty box changes nothing
    }

    [Fact]
    public void A_drag_on_a_picked_model_holds_the_picks_in_the_units_order_and_keeps_them()
    {
        var picks = ModelPicks.None.Toggle(U1, C).Toggle(U1, A);
        IReadOnlyList<Guid> unit = [A, B, C, D];
        Assert.Equal([A, C], picks.Held(A, unit));
        Assert.Same(picks, picks.Pressed(A));
    }

    [Fact]
    public void A_drag_on_a_model_not_picked_holds_its_whole_unit_and_clears_the_pick()
    {
        var picks = ModelPicks.None.Toggle(U1, A);
        IReadOnlyList<Guid> unit = [A, B, C];
        Assert.Equal(unit, picks.Held(B, unit));
        Assert.True(picks.Pressed(B).IsEmpty);
        Assert.Equal(unit, ModelPicks.None.Held(B, unit));
    }

    [Fact]
    public void A_pick_of_a_model_no_longer_drawn_is_not_held()
    {
        var picks = ModelPicks.None.Toggle(U1, A).Toggle(U1, E);
        Assert.Equal([A], picks.Held(A, [A, B]));
    }

    [Fact]
    public void The_selection_names_the_picked_models_or_else_the_whole_unit()
    {
        Assert.Equal(new Selection(U1, []), ModelPicks.None.Selection(U1), SelectionComparer.Instance);
        var picks = ModelPicks.None.Toggle(U1, A);
        var sel = picks.Selection(U1);
        Assert.Equal(U1, sel.PieceId);
        Assert.Equal([A], sel.ModelIds);
        Assert.Equal(picks.ModelIds, ModelPicks.Of(sel).ModelIds);
        Assert.True(ModelPicks.Of(null).IsEmpty);
    }

    sealed class SelectionComparer : IEqualityComparer<Selection>
    {
        public static readonly SelectionComparer Instance = new();
        public bool Equals(Selection? x, Selection? y) => x?.PieceId == y?.PieceId && (x?.ModelIds ?? []).SequenceEqual(y?.ModelIds ?? []);
        public int GetHashCode(Selection s) => s.PieceId.GetHashCode();
    }
}
