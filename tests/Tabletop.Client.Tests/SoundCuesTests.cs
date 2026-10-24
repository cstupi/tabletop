using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class SoundCuesTests
{
    static readonly Guid Squad = TableUx.Squad.Id, Orcs = TableUx.Orcs.Id;
    static readonly Guid A = TableUx.ModelOf(TableUx.Squad), B = TableUx.ModelOf(TableUx.Squad, 1), O = TableUx.ModelOf(TableUx.Orcs);
    static long _seq;
    static GameEvent Ev(EventPayload p) => new(++_seq, TableUx.P1, p, null);
    static GameEvent Start(Guid frame, string sequence, Guid actor, Guid target) => Ev(new SequenceStarted(frame, sequence, new SequenceContext(actor, [target], [], "Hold")));
    static GameEvent Step(Guid frame, params StateChange[] changes) => Ev(new StepCompleted(frame, "s", null, [], changes));
    static Location At(double x) => new OnTable(new Placement(new Point3(x, 10, 0), new Yaw(0)));
    static ModelRelocation Shelve(Guid model) => new(model, At(10), new OnShelf());
    static ModelRelocation Walk(Guid model) => new(model, At(10), At(14));

    readonly SoundCues _cues = new();
    readonly Projection _p = TableUx.After(TableUx.Deploy);
    IReadOnlyList<Cue> Of(bool silent, params GameEvent[] batch) => _cues.OnBatch(batch, _p, silent);
    IReadOnlyList<Cue> Of(params GameEvent[] batch) => Of(false, batch);

    [Fact]
    public void A_shooting_unit_plays_one_shot_however_many_weapon_groups_follow()
    {
        var shooting = Guid.CreateVersion7();
        Assert.Empty(Of(Start(shooting, "shooting", Squad, Orcs)));
        Assert.Equal([Cue.Shoot], Of(Start(Guid.CreateVersion7(), "attack-sequence", Squad, Orcs), Start(Guid.CreateVersion7(), "attack-sequence", Squad, Orcs)));
        Assert.Empty(Of(Start(Guid.CreateVersion7(), "attack-sequence", Squad, Orcs)));
    }

    [Fact]
    public void Each_striking_unit_of_a_melee_plays_one_clash()
    {
        Of(Start(Guid.CreateVersion7(), "melee", Squad, Orcs));
        Assert.Equal([Cue.Melee], Of(Start(Guid.CreateVersion7(), "attack-sequence", Squad, Orcs), Start(Guid.CreateVersion7(), "attack-sequence", Squad, Orcs)));
        Assert.Equal([Cue.Melee], Of(Start(Guid.CreateVersion7(), "attack-sequence", Orcs, Squad)));   // the strike back
        Assert.Empty(Of(Start(Guid.CreateVersion7(), "attack-sequence", Orcs, Squad)));
    }

    [Fact]
    public void An_attack_sequence_outside_shooting_and_melee_is_silent_and_a_closed_frame_stops_counting()
    {
        Assert.Empty(Of(Start(Guid.CreateVersion7(), "attack-sequence", Squad, Orcs)));
        var shooting = Guid.CreateVersion7();
        Of(Start(shooting, "shooting", Squad, Orcs), Ev(new SequenceFinished(shooting)));
        Assert.Empty(Of(Start(Guid.CreateVersion7(), "attack-sequence", Squad, Orcs)));
        var again = Guid.CreateVersion7();
        Of(Start(again, "shooting", Squad, Orcs), Ev(new SequenceAbandoned(again)));
        Assert.Empty(Of(Start(Guid.CreateVersion7(), "attack-sequence", Squad, Orcs)));
    }

    [Fact]
    public void A_move_is_any_model_landing_on_the_table_in_a_step_by_hand_or_from_the_shelf()
    {
        var frame = Guid.CreateVersion7();
        Assert.Equal([Cue.Move], Of(Step(frame, new ModelsMoved([Walk(A)]))));
        Assert.Equal([Cue.Move], Of(Ev(new ModelsMoved([Walk(A)]))));
        Assert.Equal([Cue.Move], Of(Ev(new ModelsMoved([new(A, new OnShelf(), At(14))]))));
        Assert.DoesNotContain(Cue.Move, Of(Step(frame, new ModelsMoved([Shelve(A)]))));
    }

    [Fact]
    public void Wounds_that_rise_or_a_casualty_play_one_thud_per_batch()
    {
        var frame = Guid.CreateVersion7();
        Assert.Equal([Cue.Wound], Of(Step(frame, new WoundsSet(A, 1), new WoundsSet(O, 1))));
        Assert.Equal([Cue.Wound], Of(Step(frame, new WoundsSet(B, 1)), Step(frame, new WoundsSet(A, 2))));
        Assert.Empty(Of(Step(frame, new WoundsSet(A, 2))));           // no more than it already had
        Assert.Empty(Of(Step(frame, new WoundsSet(A, 0))));           // healed
        Assert.Equal([Cue.Wound], Of(Step(frame, new ModelsMoved([Shelve(B)]))));
    }

    [Fact]
    public void A_batch_holds_each_cue_once_in_order_of_first_appearance()
    {
        var frame = Guid.CreateVersion7();
        Assert.Equal([Cue.Move, Cue.Wound], Of(Step(frame, new ModelsMoved([Walk(A)]), new WoundsSet(O, 1)), Step(frame, new ModelsMoved([Walk(B)]), new WoundsSet(O, 2))));
    }

    [Fact]
    public void A_silent_batch_or_an_undo_plays_nothing_and_rereads_the_totals()
    {
        var frame = Guid.CreateVersion7();
        Assert.Empty(Of(true, Step(frame, new WoundsSet(A, 1), new ModelsMoved([Walk(B)]))));
        var after = TableUx.After(TableUx.Deploy, new WoundsSet(A, 1));
        _cues.Seed(after);
        Assert.Empty(_cues.OnBatch([Step(frame, new WoundsSet(A, 1))], after, false));
        Assert.Empty(Of(Ev(new Undone(1)), Step(frame, new WoundsSet(B, 1), new ModelsMoved([Walk(A)]))));
    }

    [Fact]
    public void Frames_open_at_a_reseed_have_already_played_their_cue()
    {
        var mid = TableUx.After(TableUx.Deploy, new SequenceStarted(Guid.CreateVersion7(), "shooting", new SequenceContext(Squad, [Orcs], [], "Hold")));
        _cues.Seed(mid);
        Assert.Empty(_cues.OnBatch([Start(Guid.CreateVersion7(), "attack-sequence", Squad, Orcs)], mid, false));
    }
}
