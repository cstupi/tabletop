using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class WoundNumbersTests
{
    static readonly Guid Frame = Guid.CreateVersion7();
    static readonly Guid A = TableUx.ModelOf(TableUx.Squad), B = TableUx.ModelOf(TableUx.Squad, 1), C = TableUx.ModelOf(TableUx.Squad, 2);
    static EventPayload Shoot => new SequenceStarted(Frame, "deal-wounds", new SequenceContext(TableUx.Squad.Id, [], [], null));
    static GameEvent Step(long seq, params StateChange[] changes) => new(seq, TableUx.P1, new StepCompleted(Frame, "assign", null, [], changes), null);
    static ModelsMoved Removed(Guid model, double x) => new([new(model, new OnTable(new Placement(new Point3(x, 10, 0), new Yaw(0))), new OnShelf())]);

    static (EventApplier Applier, WoundNumbers Numbers) Live()
    {
        var a = TableUx.Applier(TableUx.Deploy, Shoot); var n = new WoundNumbers(); n.Seed(a.Projection);
        return (a, n);
    }

    static IReadOnlyDictionary<long, IReadOnlyList<WoundNumber>> Apply(EventApplier a, WoundNumbers n, GameEvent[] batch, bool catchingUp = false, IReadOnlyDictionary<Guid, int>? max = null)
    {
        a.ApplyAll(batch);
        return n.OnBatch(batch, a.Projection, catchingUp, max);
    }

    [Fact]
    public void Each_wound_on_a_model_left_standing_is_one_number_over_its_head_staggered()
    {
        var (a, n) = Live();
        var shown = Apply(a, n, [Step(3, new WoundsSet(A, 3))]);
        var numbers = shown[3];
        Assert.Equal(3, numbers.Count);
        Assert.All(numbers, w => Assert.Equal(A, w.ModelId));
        var slot = TableUx.Squad.Models[0];
        Assert.All(numbers, w => Assert.Equal(new Point3(10, 10, slot.Height), w.Head));
        Assert.Equal(new[] { 0, 0.15, 0.3 }, numbers.Select(w => Math.Round(w.Delay, 6)));
    }

    [Fact]
    public void Wounds_on_wire_are_totals_so_only_the_rise_counts()
    {
        var (a, n) = Live();
        Apply(a, n, [Step(3, new WoundsSet(A, 1))]);
        Assert.Equal(2, Apply(a, n, [Step(4, new WoundsSet(A, 3))])[4].Count);   // 1 → 3: two numbers
    }

    [Fact]
    public void A_model_destroyed_by_the_same_wounds_shows_nothing_and_its_unit_mates_still_show()
    {
        var (a, n) = Live();
        var shown = Apply(a, n, [Step(3, new WoundsSet(A, 2), new WoundsSet(B, 1), Removed(B, 12))]);
        Assert.Equal(new[] { A, A }, shown[3].Select(w => w.ModelId));
    }

    [Fact]
    public void A_model_at_its_max_wounds_shows_nothing_even_while_it_still_stands()
    {
        var (a, n) = Live();
        var shown = Apply(a, n, [Step(3, new WoundsSet(A, 3), new WoundsSet(C, 1))], max: new Dictionary<Guid, int> { [A] = 3, [C] = 3 });
        Assert.Equal(new[] { C }, shown[3].Select(w => w.ModelId));
    }

    [Fact]
    public void Healing_shows_nothing()
    {
        var (a, n) = Live();
        Apply(a, n, [Step(3, new WoundsSet(A, 2))]);
        Assert.Empty(Apply(a, n, [Step(4, new WoundsSet(A, 0))]));
    }

    [Fact]
    public void A_menu_wound_edit_shows_and_the_stagger_runs_on_across_a_batchs_events()
    {
        var (a, n) = Live();
        var shown = Apply(a, n, [Step(3, new WoundsSet(A, 1)), new GameEvent(4, TableUx.P1, new WoundsSet(A, 2), null)]);
        Assert.Equal(0, shown[3].Single().Delay);
        Assert.Equal(WoundNumbers.Stagger, shown[4].Single().Delay, 6);
    }

    [Fact]
    public void Catching_up_an_undo_and_an_empty_batch_show_nothing_and_reread_the_totals()
    {
        var (a, n) = Live();
        Assert.Empty(Apply(a, n, [Step(3, new WoundsSet(A, 1))], catchingUp: true));
        Assert.Single(Apply(a, n, [Step(4, new WoundsSet(A, 2))])[4]);                    // the catch-up's total is the base
        Assert.Empty(n.OnBatch([], a.Projection, false));                                  // a replay's jump
        Assert.Empty(n.OnBatch([new GameEvent(5, TableUx.P1, new Undone(3), null), Step(6, new WoundsSet(A, 3))], a.Projection, false));
    }

    [Fact]
    public void After_a_board_the_first_live_batch_counts_from_the_catch_up()
    {
        var n = new WoundNumbers(); n.OnBoard();
        var a = TableUx.Applier(); var history = TableUx.Events(TableUx.Deploy, Shoot, new StepCompleted(Frame, "assign", null, [], [new WoundsSet(A, 1)]));
        GameEvent[] page = [.. history]; a.ApplyAll(page);
        Assert.Empty(n.OnBatch(page, a.Projection, catchingUp: true));
        Assert.Single(Apply(a, n, [Step(4, new WoundsSet(A, 2))])[4]);
    }

    [Fact]
    public void A_number_rises_and_fades_out_over_its_seconds()
    {
        Assert.Equal((0, 1), WoundNumbers.Look(0));
        var (rise, alpha) = WoundNumbers.Look(WoundNumbers.Seconds);
        Assert.Equal(1, rise, 6); Assert.Equal(0, alpha, 6);
        var mid = WoundNumbers.Look(WoundNumbers.Seconds / 2);
        Assert.InRange(mid.Rise, 0.4, 0.9); Assert.InRange(mid.Alpha, 0.5, 1);
    }
}
