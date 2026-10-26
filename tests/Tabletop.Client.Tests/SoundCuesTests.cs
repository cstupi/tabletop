using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class SoundCuesTests
{
    static readonly Guid Squad = TableUx.Squad.Id, Orcs = TableUx.Orcs.Id;
    static readonly Guid A = TableUx.ModelOf(TableUx.Squad), B = TableUx.ModelOf(TableUx.Squad, 1), O = TableUx.ModelOf(TableUx.Orcs);
    static long _seq;
    static GameEvent Ev(EventPayload p) => new(++_seq, TableUx.P1, p, null);
    static GameEvent Start(Guid frame, string sequence, Guid actor, params Guid[] targets) => Ev(new SequenceStarted(frame, sequence, new SequenceContext(actor, targets, [], "Hold")));
    static GameEvent Step(Guid frame, params StateChange[] changes) => Ev(new StepCompleted(frame, "s", null, [], changes));
    static GameEvent Rolled(Guid frame, string roll, params bool[] successes) =>
        Ev(new StepCompleted(frame, roll, null, [new RollRecord(Guid.CreateVersion7(), roll, 4, [], successes.Select(s => new Die(s ? 5 : 2, s ? 5 : 2, s)).ToList())], []));
    static GameEvent Picked(Guid frame, params Guid[] pieces) => Ev(new StepCompleted(frame, "targets", new SelectedPieces(pieces), [], []));
    static Location At(double x) => new OnTable(new Placement(new Point3(x, 10, 0), new Yaw(0)));
    static ModelRelocation Shelve(Guid model) => new(model, At(10), new OnShelf());
    static ModelRelocation Walk(Guid model) => new(model, At(10), At(14));
    static Guid Id() => Guid.CreateVersion7();

    readonly SoundCues _cues = new();
    readonly Projection _p = TableUx.After(TableUx.Deploy);
    IReadOnlyList<CueAt> Played(bool silent, params GameEvent[] batch) => _cues.OnBatch(batch, _p, silent, TeamOf);
    IReadOnlyList<Cue> Of(bool silent, params GameEvent[] batch) => Played(silent, batch).Select(c => c.Cue).ToList();
    IReadOnlyList<Cue> Of(params GameEvent[] batch) => Of(false, batch);
    static Guid? TeamOf(Guid unit) => unit == Squad ? TableUx.T1 : unit == Orcs ? TableUx.T2 : null;

    /// <summary>A shooting frame with its first attack sequence open: the shot has played.</summary>
    Guid Shooting(Guid shooter, Guid target)
    {
        var attack = Id();
        Of(Start(Id(), "shooting", shooter), Start(attack, "attack-sequence", shooter, target));
        return attack;
    }

    Guid Melee(Guid striker, Guid target)
    {
        var attack = Id();
        Of(Start(Id(), "melee", striker, target), Start(attack, "attack-sequence", striker, target));
        return attack;
    }

    [Fact]
    public void A_shooting_unit_plays_one_shot_however_many_weapon_groups_follow()
    {
        var shooting = Id();
        Assert.Empty(Of(Start(shooting, "shooting", Squad)));
        Assert.Equal([Cue.Shoot], Of(Start(Id(), "attack-sequence", Squad, Orcs), Start(Id(), "attack-sequence", Squad, Orcs)));
        Assert.Empty(Of(Start(Id(), "attack-sequence", Squad, Orcs)));
    }

    [Fact]
    public void A_shot_picks_one_of_three_variants_by_its_shooting_frame()
    {
        int VariantOf(Guid frame) => new SoundCues().OnBatch([Start(frame, "shooting", Squad), Start(Id(), "attack-sequence", Squad, Orcs)], _p, false, TeamOf).Single().Variant;
        var frames = Enumerable.Range(0, 60).Select(_ => Id()).ToList();
        var variants = frames.Select(VariantOf).ToList();
        Assert.All(variants, v => Assert.InRange(v, 0, SoundCues.Variants(Cue.Shoot) - 1));
        Assert.Equal(3, variants.Distinct().Count());
        Assert.Equal(variants, frames.Select(VariantOf));                 // the same frame always sounds the same
        Assert.Equal(3, SoundCues.Variants(Cue.Shoot));
        Assert.Equal(1, SoundCues.Variants(Cue.Wound));
    }

    [Fact]
    public void A_shooting_hit_roll_plays_an_impact_when_any_die_hits_and_a_miss_when_none_does()
    {
        var attack = Shooting(Squad, Orcs);
        Assert.Equal([Cue.Impact], Of(Rolled(attack, "hit", false, true, false)));
        Assert.Equal([Cue.Miss], Of(Rolled(attack, "hit", false, false)));
    }

    [Fact]
    public void A_block_roll_plays_the_clang_when_every_hit_is_blocked_and_the_wound_when_any_gets_through()
    {
        var attack = Shooting(Squad, Orcs); var deal = Id();
        Of(Start(deal, "deal-wounds", Squad, Orcs));
        Assert.Equal([Cue.Block], Of(Rolled(deal, "defense", true, true)));
        Assert.Equal([Cue.Wound], Of(Rolled(deal, "defense", true, false), Step(deal, new WoundsSet(O, 1))));
        Assert.Empty(Of(Rolled(attack, "defense")));                      // no dice, no hits to block
    }

    [Fact]
    public void A_shot_sounds_in_order_fire_then_hit_then_wound_each_after_the_dice_land()
    {
        var shooting = Id(); var attack = Id(); var deal = Id();
        var played = Played(false, Start(shooting, "shooting", Squad), Start(attack, "attack-sequence", Squad, Orcs), Rolled(attack, "hit", true, true),
            Start(deal, "deal-wounds", Squad, Orcs), Rolled(deal, "defense", false, true), Step(deal, new WoundsSet(O, 1)));
        Assert.Equal([Cue.Shoot, Cue.Impact, Cue.Wound], played.Select(c => c.Cue));
        Assert.Equal(0, played[0].Delay);
        Assert.Equal(played[0].Delay + SoundCues.Gap(Cue.Shoot), played[1].Delay, 6);
        Assert.Equal(played[1].Delay + SoundCues.Gap(Cue.Impact), played[2].Delay, 6);
        Assert.All(played.Skip(1).Zip(played), p => Assert.True(p.First.Delay - p.Second.Delay >= 0.4, "each follows the dice tray's 0.4 s tumble"));
    }

    [Fact]
    public void A_batch_caps_each_cue_at_one_however_many_weapon_groups_roll()
    {
        var shooting = Id(); var a1 = Id(); var a2 = Id();
        Assert.Equal([Cue.Shoot, Cue.Impact, Cue.Block, Cue.Wound], Of(Start(shooting, "shooting", Squad),
            Start(a1, "attack-sequence", Squad, Orcs), Rolled(a1, "hit", true), Rolled(a1, "defense", true),
            Start(a2, "attack-sequence", Squad, Orcs), Rolled(a2, "hit", true), Rolled(a2, "defense", false), Step(a2, new WoundsSet(O, 1))));
    }

    [Fact]
    public void A_melee_plays_its_strike_when_the_hit_roll_lands_a_whiff_when_it_misses_and_then_the_wound()
    {
        var attack = Melee(Squad, Orcs);
        Assert.Equal([Cue.Melee, Cue.Wound], Of(Rolled(attack, "hit", true), Rolled(attack, "defense", false)));
        Assert.Equal([Cue.Whiff], Of(Rolled(attack, "hit", false, false)));
        Assert.Equal([Cue.Melee, Cue.Block], Of(Rolled(attack, "hit", true), Rolled(attack, "defense", true)));
    }

    [Fact]
    public void A_melee_attack_starting_is_silent_until_its_dice()
    {
        Assert.Empty(Of(Start(Id(), "melee", Squad, Orcs), Start(Id(), "attack-sequence", Squad, Orcs)));
    }

    [Fact]
    public void A_hit_roll_outside_shooting_and_melee_is_silent_and_a_closed_frame_stops_counting()
    {
        var hits = Id();
        Assert.Empty(Of(Start(hits, "spell-hits", Squad, Orcs), Rolled(hits, "hit", true, true)));
        Assert.Empty(Of(Start(Id(), "attack-sequence", Squad, Orcs)));
        var shooting = Id();
        Of(Start(shooting, "shooting", Squad), Ev(new SequenceFinished(shooting)));
        Assert.Empty(Of(Start(Id(), "attack-sequence", Squad, Orcs)));
        var again = Id();
        Of(Start(again, "shooting", Squad), Ev(new SequenceAbandoned(again)));
        Assert.Empty(Of(Start(Id(), "attack-sequence", Squad, Orcs)));
    }

    [Fact]
    public void A_spell_on_a_friendly_unit_plays_the_friendly_cast_and_on_an_enemy_the_enemy_cast()
    {
        var cast = Id();
        Assert.Empty(Of(Start(cast, "casting", Squad), Rolled(cast, "casting", true)));
        Assert.Equal([Cue.CastFriendly], Of(Picked(cast, Squad)));
        var hex = Id();
        Of(Start(hex, "casting", Squad), Rolled(hex, "casting", true));
        Assert.Equal([Cue.CastEnemy], Of(Picked(hex, O)));                 // a model-grain spell names the model
    }

    [Fact]
    public void A_spells_hits_follow_its_cast_with_the_block_or_the_wound()
    {
        var cast = Id(); var hits = Id(); var deal = Id();
        Of(Start(cast, "casting", Squad), Rolled(cast, "casting", true));
        Assert.Equal([Cue.CastEnemy, Cue.Wound], Of(Picked(cast, Orcs), Start(hits, "spell-hits", Squad, Orcs), Rolled(hits, "hit", true),
            Start(deal, "deal-wounds", Squad, Orcs), Rolled(deal, "defense", false), Step(deal, new WoundsSet(O, 1))));
    }

    [Fact]
    public void A_failed_cast_fizzles()
    {
        var cast = Id();
        Assert.Equal([Cue.Fizzle], Of(Start(cast, "casting", Squad), Rolled(cast, "casting", false)));
    }

    [Fact]
    public void A_target_answer_outside_a_casting_frame_is_silent()
    {
        var pick = Id();
        Assert.Empty(Of(Start(pick, "pick-rule", Squad), Picked(pick, Orcs)));
    }

    [Fact]
    public void A_move_is_any_model_landing_on_the_table_in_a_step_by_hand_or_from_the_shelf()
    {
        var frame = Id();
        Assert.Equal([Cue.Move], Of(Step(frame, new ModelsMoved([Walk(A)]))));
        Assert.Equal([Cue.Move], Of(Ev(new ModelsMoved([Walk(A)]))));
        Assert.Equal([Cue.Move], Of(Ev(new ModelsMoved([new(A, new OnShelf(), At(14))]))));
        Assert.DoesNotContain(Cue.Move, Of(Step(frame, new ModelsMoved([Shelve(A)]))));
    }

    [Fact]
    public void Wounds_that_rise_or_a_casualty_play_one_wound_per_batch()
    {
        var frame = Id();
        Assert.Equal([Cue.Wound], Of(Step(frame, new WoundsSet(A, 1), new WoundsSet(O, 1))));
        Assert.Equal([Cue.Wound], Of(Step(frame, new WoundsSet(B, 1)), Step(frame, new WoundsSet(A, 2))));
        Assert.Empty(Of(Step(frame, new WoundsSet(A, 2))));           // no more than it already had
        Assert.Empty(Of(Step(frame, new WoundsSet(A, 0))));           // healed
        Assert.Equal([Cue.Wound], Of(Step(frame, new ModelsMoved([Shelve(B)]))));
    }

    [Fact]
    public void A_batch_holds_each_cue_once_in_order_of_first_appearance()
    {
        var frame = Id();
        Assert.Equal([Cue.Move, Cue.Wound], Of(Step(frame, new ModelsMoved([Walk(A)]), new WoundsSet(O, 1)), Step(frame, new ModelsMoved([Walk(B)]), new WoundsSet(O, 2))));
    }

    [Fact]
    public void A_silent_batch_or_an_undo_plays_nothing_and_rereads_the_totals()
    {
        var frame = Id();
        Assert.Empty(Of(true, Step(frame, new WoundsSet(A, 1), new ModelsMoved([Walk(B)]))));
        var after = TableUx.After(TableUx.Deploy, new WoundsSet(A, 1));
        _cues.Seed(after);
        Assert.Empty(_cues.OnBatch([Step(frame, new WoundsSet(A, 1))], after, false, TeamOf));
        Assert.Empty(Of(Ev(new Undone(1)), Step(frame, new WoundsSet(B, 1), new ModelsMoved([Walk(A)]))));
    }

    [Fact]
    public void Frames_open_at_a_reseed_have_already_played_their_shot_and_still_read_their_dice()
    {
        var shooting = Id(); var attack = Id();
        var mid = TableUx.After(TableUx.Deploy, new SequenceStarted(shooting, "shooting", new SequenceContext(Squad, [], [], "Hold")),
            new SequenceStarted(attack, "attack-sequence", new SequenceContext(Squad, [Orcs], [], "Hold")));
        _cues.Seed(mid);
        Assert.Empty(_cues.OnBatch([Start(Id(), "attack-sequence", Squad, Orcs)], mid, false, TeamOf).Select(c => c.Cue));
        Assert.Equal([Cue.Miss], _cues.OnBatch([Rolled(attack, "hit", false)], mid, false, TeamOf).Select(c => c.Cue));
    }

    [Fact]
    public void Every_cue_names_its_files()
    {
        Assert.Equal("shoot-1", SoundCues.File(Cue.Shoot, 0));
        Assert.Equal("shoot-3", SoundCues.File(Cue.Shoot, 2));
        Assert.Equal("cast-friendly", SoundCues.File(Cue.CastFriendly, 0));
        Assert.Equal("wound", SoundCues.File(Cue.Wound, 0));
        Assert.Equal(13, Enum.GetValues<Cue>().Sum(SoundCues.Variants));
    }
}
