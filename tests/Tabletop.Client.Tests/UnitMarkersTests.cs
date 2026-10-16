using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class UnitMarkersTests
{
    static readonly Guid Frame = Guid.CreateVersion7();
    static readonly Guid A = TableUx.ModelOf(TableUx.Squad), B = TableUx.ModelOf(TableUx.Squad, 1);
    static EventPayload Shoot => new SequenceStarted(Frame, "s", new SequenceContext(TableUx.Squad.Id, [], [], "Hold"));
    static GameEvent Step(long seq, params StateChange[] changes) => new(seq, TableUx.P1, new StepCompleted(Frame, "s", null, [], changes), null);

    [Fact]
    public void Icons_sit_over_the_centroid_above_the_tallest_model()
    {
        var p = TableUx.After(TableUx.Deploy, new ActionRecorded(TableUx.Squad.Id, "Advance"), new ConditionSet(new Subject(SubjectKind.Model, A), "shaken", true), new WoundsSet(B, 1));
        var m = UnitMarkers.Of(p).Single();                 // Orcs carry nothing; Reserve is off the table
        Assert.Equal((TableUx.Squad.Id, new Point3(12, 10, 1.5)), (m.UnitId, m.Anchor));
        Assert.Equal(new MarkerIcon[] { new("activated", "✓"), new("shaken", "Shaken") }, m.Icons);                // wounds are per model (ModelMarks)
        Assert.Empty(UnitMarkers.Of(TableUx.After(TableUx.Deploy, new WoundsSet(B, 1))));
    }

    [Fact]
    public void A_step_floats_models_removed_wounds_taken_and_conditions_set_per_unit()
    {
        var a = TableUx.Applier(TableUx.Deploy, Shoot); var f = new FloatingResults(); f.Seed(a.Projection);
        GameEvent[] first = [Step(3, new WoundsSet(A, 1), new WoundsSet(B, 1), new ModelsMoved([new(B, new OnTable(new Placement(new Point3(12, 10, 0), new Yaw(0))), new OnShelf())]),
            new ConditionSet(new Subject(SubjectKind.Unit, TableUx.Squad.Id), "shaken", true))];
        a.ApplyAll(first);
        Assert.Equal(new[] { "-1 model", "-1 wound", "Shaken" }, f.OnBatch(first, a.Projection, catchingUp: false)[3].Select(r => r.Text));   // B's wound is its removal
        GameEvent[] second = [Step(4, new WoundsSet(A, 2))]; a.ApplyAll(second);
        Assert.Equal("-1 wound", f.OnBatch(second, a.Projection, catchingUp: false)[4].Single().Text);                                         // the total went 1 → 2
    }

    /// <summary>Review Focus 3: the join's catch-up arrives in several pages, all silent until Session stops catching up.</summary>
    [Fact]
    public void The_catch_up_batch_after_a_board_floats_nothing()
    {
        var f = new FloatingResults(); f.OnBoard();
        var a = TableUx.Applier(); var history = TableUx.Events(TableUx.Deploy, Shoot, new StepCompleted(Frame, "s", null, [], [new WoundsSet(A, 1)]));
        GameEvent[] page1 = [.. history.Take(2)], page2 = [history[2]];
        a.ApplyAll(page1); Assert.Empty(f.OnBatch(page1, a.Projection, catchingUp: true));
        a.ApplyAll(page2); Assert.Empty(f.OnBatch(page2, a.Projection, catchingUp: true));
        GameEvent[] next = [Step(4, new WoundsSet(A, 2))]; a.ApplyAll(next);
        Assert.Equal("-1 wound", f.OnBatch(next, a.Projection, catchingUp: false)[4].Single().Text);
    }
}
