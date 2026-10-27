using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class SoundCuesTests
{
    static readonly Guid Squad = TableUx.Squad.Id, Orcs = TableUx.Orcs.Id;
    static readonly Guid A = TableUx.ModelOf(TableUx.Squad), B = TableUx.ModelOf(TableUx.Squad, 1), O = TableUx.ModelOf(TableUx.Orcs);
    static readonly Guid Rifle1 = TableUx.Squad.Models[0].Weapons[0].Id, Rifle2 = TableUx.Squad.Models[1].Weapons[0].Id, Pistol = TableUx.Squad.Models[2].Weapons[0].Id;
    static long _seq;
    static GameEvent Ev(EventPayload p) => new(++_seq, TableUx.P1, p, null);
    static GameEvent Start(Guid frame, string sequence, Guid actor, params Guid[] targets) => Ev(new SequenceStarted(frame, sequence, new SequenceContext(actor, targets, [], "Hold")));
    static GameEvent Group(Guid frame, Guid weapon) => Ev(new SequenceStarted(frame, "attack-sequence", new SequenceContext(Squad, [Orcs], [weapon], "Hold")));
    static GameEvent Step(Guid frame, params StateChange[] changes) => Ev(new StepCompleted(frame, "s", null, [], changes));
    static GameEvent Rolled(Guid frame, string roll, params bool[] successes) =>
        Ev(new StepCompleted(frame, roll, null, [new RollRecord(Guid.CreateVersion7(), roll, 4, [], successes.Select(s => new Die(s ? 5 : 2, s ? 5 : 2, s)).ToList())], []));
    /// <summary>n dice, the first `hits` of them hits.</summary>
    static GameEvent Hits(Guid frame, int n, int hits = 1) => Rolled(frame, "hit", Enumerable.Range(0, n).Select(i => i < hits).ToArray());
    static GameEvent Picked(Guid frame, params Guid[] pieces) => Ev(new StepCompleted(frame, "targets", new SelectedPieces(pieces), [], []));
    static Location At(double x) => new OnTable(new Placement(new Point3(x, 10, 0), new Yaw(0)));
    static ModelRelocation Shelve(Guid model) => new(model, At(10), new OnShelf());
    static ModelRelocation Walk(Guid model) => new(model, At(10), At(14));
    static Guid Id() => Guid.CreateVersion7();

    readonly SoundCues _cues = new();
    readonly Projection _p = TableUx.After(TableUx.Deploy);
    Names N => new(TableUx.Game, _p);
    IReadOnlyList<CueAt> Played(bool silent, params GameEvent[] batch) => _cues.OnBatch(batch, _p, silent, N);
    IReadOnlyList<CueAt> Played(params GameEvent[] batch) => Played(false, batch);
    IReadOnlyList<Cue> Of(bool silent, params GameEvent[] batch) => Played(silent, batch).Select(c => c.Cue).ToList();
    IReadOnlyList<Cue> Of(params GameEvent[] batch) => Of(false, batch);
    static IReadOnlyList<Cue> Squash(IEnumerable<CueAt> cues) => cues.Select(c => c.Cue).Aggregate(new List<Cue>(), (l, c) => { if (l.Count == 0 || l[^1] != c) l.Add(c); return l; });

    /// <summary>A shooting frame with one weapon group open.</summary>
    Guid Shooting(Guid weapon)
    {
        var attack = Id();
        Of(Start(Id(), "shooting", Squad), Group(attack, weapon));
        return attack;
    }

    Guid Melee(Guid striker, Guid target)
    {
        var attack = Id();
        Of(Start(Id(), "melee", striker, target), Start(attack, "attack-sequence", striker, target));
        return attack;
    }

    [Fact]
    public void A_shooting_frame_and_its_groups_starting_are_silent_until_their_dice()
    {
        Assert.Empty(Of(Start(Id(), "shooting", Squad), Group(Id(), Rifle1)));
    }

    [Fact]
    public void A_single_shot_is_one_report_then_its_hit_after_the_dice_land()
    {
        var played = Played(Hits(Shooting(Rifle1), 1));
        Assert.Equal([Cue.Shoot, Cue.Impact], played.Select(c => c.Cue));
        Assert.Equal(0, played[0].Delay);
        Assert.True(played[0].Volley);
        Assert.Equal(SoundCues.Gap(Cue.Shoot), played[1].Delay, 6);
    }

    [Fact]
    public void Ten_shots_are_a_volley_of_ten_staggered_reports_and_the_result_follows_the_last()
    {
        var played = Played(Hits(Shooting(Rifle1), 10, 0));
        var shots = played.Where(c => c.Cue == Cue.Shoot).ToList();
        Assert.Equal(10, shots.Count);
        Assert.All(shots, s => Assert.True(s.Volley));
        Assert.Equal(0, shots[0].Delay);
        foreach (var (a, b) in shots.Zip(shots.Skip(1))) Assert.InRange(b.Delay - a.Delay, 0.07, 0.12);
        Assert.Equal(Cue.Miss, played[^1].Cue);
        Assert.Equal(shots[^1].Delay + SoundCues.Gap(Cue.Shoot), played[^1].Delay, 6);
    }

    [Fact]
    public void A_volley_varies_each_shot_a_little_in_time_pitch_and_level()
    {
        var shots = Played(Hits(Shooting(Rifle1), 12)).Where(c => c.Volley).ToList();
        Assert.All(shots, s => { Assert.InRange(s.Pitch, 1 - SoundCues.PitchSpread, 1 + SoundCues.PitchSpread); Assert.InRange(s.VolumeDb, -SoundCues.VolumeSpreadDb, 0); });
        Assert.True(shots.Select(s => s.Pitch).Distinct().Count() > 6);
        Assert.True(shots.Select(s => s.VolumeDb).Distinct().Count() > 6);
        var gaps = shots.Zip(shots.Skip(1), (a, b) => Math.Round(b.Delay - a.Delay, 6)).Distinct().Count();
        Assert.True(gaps > 4, "the spacing is jittered, not machine-even");
    }

    [Fact]
    public void A_volley_is_the_same_every_time_its_frame_is_played()
    {
        var shooting = Id(); var attack = Id();
        GameEvent[] Batch() => [Start(shooting, "shooting", Squad), Group(attack, Rifle1), Hits(attack, 8)];
        var once = new SoundCues().OnBatch(Batch(), _p, false, N); var twice = new SoundCues().OnBatch(Batch(), _p, false, N);
        Assert.Equal(once, twice);
        var other = Played(Hits(Shooting(Rifle1), 8));
        Assert.NotEqual(once.Select(c => c.Pitch), other.Select(c => c.Pitch));
    }

    [Fact]
    public void A_volley_keeps_its_natural_spacing_up_to_five_seconds_then_compresses_then_caps_the_sounds()
    {
        Assert.Equal(5, SoundCues.MaxVolleySeconds);
        double Span(int dice)
        {
            var attack = Id();
            var cues = new SoundCues(); cues.OnBatch([Start(Id(), "shooting", Squad), Group(attack, Rifle1)], _p, false, N);
            var s = cues.OnBatch([Hits(attack, dice)], _p, false, N).Where(c => c.Volley).ToList();
            return s[^1].Delay - s[0].Delay;
        }
        Assert.Equal(SoundCues.VolleyInterval, SoundCues.Interval(10));
        Assert.Equal(SoundCues.VolleyInterval, SoundCues.Interval(53));                  // 52 × 95 ms = 4.94 s
        Assert.Equal(5.0 / 59, SoundCues.Interval(60), 9);                                // compressed to fit
        Assert.InRange(SoundCues.Interval(SoundCues.MaxVolleySounds), SoundCues.MinVolleyInterval, SoundCues.MinVolleyInterval + 0.001);   // the floor, where the count caps
        Assert.Equal(SoundCues.MinVolleyInterval, SoundCues.Interval(1000));
        Assert.InRange(SoundCues.MinVolleyInterval, 0.025, 0.03);
        Assert.InRange(SoundCues.VolleyInterval, 0.07, 0.12);
        Assert.InRange(Span(10), 9 * 0.095 - 0.012, 9 * 0.095);
        Assert.InRange(Span(60), 4.9, 5.0);
        var attack = Shooting(Rifle1);
        var huge = Played(Hits(attack, 500)).Where(c => c.Volley).ToList();
        Assert.Equal(SoundCues.MaxVolleySounds, huge.Count);
        Assert.True(huge[^1].Delay - huge[0].Delay <= SoundCues.MaxVolleySeconds);
        Assert.All(huge.Zip(huge.Skip(1)), p => Assert.True(p.Second.Delay > p.First.Delay, "the shots keep their order"));
    }

    [Fact]
    public void A_weapon_sounds_the_same_whichever_frame_fires_it()
    {
        Assert.Equal(SoundCues.VariantOf("Rifle"), SoundCues.VariantOf("Rifle"));
        Assert.Equal([0, 1, 2], Enumerable.Range(0, 40).Select(i => SoundCues.VariantOf($"Weapon {i}")).Distinct().Order());
        var v1 = Played(Hits(Shooting(Rifle1), 3)).First().Variant;
        var v2 = Played(Hits(Shooting(Rifle2), 3)).First().Variant;   // another model's rifle
        Assert.Equal(SoundCues.VariantOf("Rifle"), v1);
        Assert.Equal(v1, v2);
        Assert.Equal(SoundCues.VariantOf("Pistol"), Played(Hits(Shooting(Pistol), 1)).First().Variant);
    }

    [Fact]
    public void Weapon_groups_sound_one_after_another_in_the_dice_trays_order_each_with_its_result()
    {
        var shooting = Id(); var a1 = Id(); var a2 = Id(); var d1 = Id();
        var played = Played(Start(shooting, "shooting", Squad),
            Group(a1, Rifle1), Hits(a1, 4, 2), Start(d1, "deal-wounds", Squad, Orcs), Rolled(d1, "defense", true, true), Ev(new SequenceFinished(d1)), Ev(new SequenceFinished(a1)),
            Group(a2, Pistol), Hits(a2, 2, 0));
        Assert.Equal([Cue.Shoot, Cue.Impact, Cue.Block, Cue.Shoot, Cue.Miss], Squash(played));
        Assert.Equal(6, played.Count(c => c.Cue == Cue.Shoot));
        Assert.All(played.Zip(played.Skip(1)), p => Assert.True(p.Second.Delay > p.First.Delay));
    }

    [Fact]
    public void Two_groups_whose_dice_come_together_play_back_to_back_with_a_short_gap()
    {
        var a1 = Id(); var a2 = Id();
        var played = Played(Start(Id(), "shooting", Squad), Group(a1, Rifle1), Hits(a1, 3, 0), Group(a2, Pistol), Hits(a2, 3, 0));
        Assert.Equal([Cue.Shoot, Cue.Miss, Cue.Shoot, Cue.Miss], Squash(played));
    }

    [Fact]
    public void A_shooting_hit_roll_plays_an_impact_when_any_die_hits_and_a_miss_when_none_does()
    {
        var attack = Shooting(Rifle1);
        Assert.Equal(Cue.Impact, Of(Rolled(attack, "hit", false, true, false))[^1]);
        Assert.Equal(Cue.Miss, Of(Rolled(attack, "hit", false, false))[^1]);
    }

    [Fact]
    public void A_block_roll_plays_the_clang_when_every_hit_is_blocked_and_the_wound_when_any_gets_through()
    {
        var attack = Shooting(Rifle1); var deal = Id();
        Of(Start(deal, "deal-wounds", Squad, Orcs));
        Assert.Equal([Cue.Block], Of(Rolled(deal, "defense", true, true)));
        Assert.Equal([Cue.Wound], Of(Rolled(deal, "defense", true, false), Step(deal, new WoundsSet(O, 1))));
        Assert.Empty(Of(Rolled(attack, "defense")));                      // no dice, no hits to block
    }

    [Fact]
    public void A_shot_sounds_in_order_volley_then_hit_then_wound_each_after_the_dice_land()
    {
        var shooting = Id(); var attack = Id(); var deal = Id();
        var played = Played(Start(shooting, "shooting", Squad), Group(attack, Rifle1), Hits(attack, 2, 2),
            Start(deal, "deal-wounds", Squad, Orcs), Rolled(deal, "defense", false, true), Step(deal, new WoundsSet(O, 1)));
        Assert.Equal([Cue.Shoot, Cue.Impact, Cue.Wound], Squash(played));
        var impact = played.Single(c => c.Cue == Cue.Impact); var wound = played.Single(c => c.Cue == Cue.Wound);
        Assert.Equal(played.Last(c => c.Cue == Cue.Shoot).Delay + SoundCues.Gap(Cue.Shoot), impact.Delay, 6);
        Assert.Equal(impact.Delay + SoundCues.Gap(Cue.Impact), wound.Delay, 6);
    }

    [Fact]
    public void A_melee_clash_plays_a_strike_per_hit_and_a_whiff_per_miss_in_die_order_then_the_wound()
    {
        var attack = Melee(Squad, Orcs);
        var played = Played(Rolled(attack, "hit", true, false, true, true, false), Rolled(attack, "defense", false));
        Assert.Equal([Cue.Melee, Cue.Whiff, Cue.Melee, Cue.Melee, Cue.Whiff, Cue.Wound], played.Select(c => c.Cue));
        var dice = played.Take(5).ToList();
        Assert.All(dice, d => Assert.True(d.Volley));
        foreach (var (a, b) in dice.Zip(dice.Skip(1))) Assert.InRange(b.Delay - a.Delay, 0.07, 0.12);
        Assert.Equal(dice[^1].Delay + SoundCues.Gap(Cue.Whiff), played[^1].Delay, 6);
        Assert.Equal([Cue.Melee, Cue.Block], Squash(Played(Rolled(attack, "hit", true), Rolled(attack, "defense", true))));
    }

    [Fact]
    public void Twenty_melee_dice_stagger_and_a_huge_melee_caps_like_a_volley()
    {
        var attack = Melee(Squad, Orcs);
        var twenty = Played(Hits(attack, 20, 10));
        Assert.Equal(10, twenty.Count(c => c.Cue == Cue.Melee)); Assert.Equal(10, twenty.Count(c => c.Cue == Cue.Whiff));
        Assert.Equal(20, twenty.Select(c => c.Delay).Distinct().Count());
        var huge = Played(Hits(attack, 400, 200));
        Assert.Equal(SoundCues.MaxVolleySounds, huge.Count);
        Assert.InRange(huge.Count(c => c.Cue == Cue.Melee), SoundCues.MaxVolleySounds / 2 - 1, SoundCues.MaxVolleySounds / 2 + 1);   // spread over the dice
        Assert.True(huge[^1].Delay - huge[0].Delay <= SoundCues.MaxVolleySeconds);
    }

    [Fact]
    public void The_strike_back_clashes_in_its_own_turn()
    {
        var melee = Id(); var strike = Id(); var back = Id();
        var played = Played(Start(melee, "melee", Squad, Orcs), Start(strike, "attack-sequence", Squad, Orcs), Hits(strike, 3, 3), Ev(new SequenceFinished(strike)),
            Start(back, "attack-sequence", Orcs, Squad), Hits(back, 2, 0));
        Assert.Equal([Cue.Melee, Cue.Whiff], Squash(played));
        Assert.Equal(played[2].Delay + SoundCues.GroupGap, played[3].Delay, 6);
    }

    [Fact]
    public void A_hit_roll_outside_shooting_and_melee_is_silent_and_a_closed_frame_stops_counting()
    {
        var hits = Id();
        Assert.Empty(Of(Start(hits, "spell-hits", Squad, Orcs), Rolled(hits, "hit", true, true)));
        var shooting = Id(); var attack = Id();
        Of(Start(shooting, "shooting", Squad), Ev(new SequenceFinished(shooting)));
        Assert.Empty(Of(Start(attack, "attack-sequence", Squad, Orcs), Hits(attack, 2)));
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
    public void A_move_then_a_volley_waits_for_the_move()
    {
        var a = Id();
        var played = Played(Step(Id(), new ModelsMoved([Walk(A)])), Start(Id(), "shooting", Squad), Group(a, Rifle1), Hits(a, 1));
        Assert.Equal([Cue.Move, Cue.Shoot, Cue.Impact], played.Select(c => c.Cue));
        Assert.Equal(SoundCues.Gap(Cue.Move), played[1].Delay, 6);
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
        Assert.Empty(_cues.OnBatch([Step(frame, new WoundsSet(A, 1))], after, false, N));
        Assert.Empty(Of(Ev(new Undone(1)), Step(frame, new WoundsSet(B, 1), new ModelsMoved([Walk(A)]))));
    }

    [Fact]
    public void An_empty_batch_after_a_new_board_rereads_the_totals_and_frames()
    {
        // A replay's jump: the board redraws at the new position, then an empty batch; the next step played forward reads from there.
        var shooting = Id(); var attack = Id();
        var at = TableUx.After(TableUx.Deploy, new WoundsSet(A, 1), new SequenceStarted(shooting, "shooting", new SequenceContext(Squad, [], [], "Hold")),
            new SequenceStarted(attack, "attack-sequence", new SequenceContext(Squad, [Orcs], [Rifle1], "Hold")));
        _cues.OnBoard();
        Assert.Empty(_cues.OnBatch([], at, false, new Names(TableUx.Game, at)));
        Assert.Empty(_cues.OnBatch([Step(attack, new WoundsSet(A, 1))], at, false, new Names(TableUx.Game, at)));
        Assert.Equal([Cue.Shoot, Cue.Miss], _cues.OnBatch([Rolled(attack, "hit", false)], at, false, new Names(TableUx.Game, at)).Select(c => c.Cue));
    }

    [Fact]
    public void Frames_open_at_a_reseed_still_read_their_dice()
    {
        var shooting = Id(); var attack = Id();
        var mid = TableUx.After(TableUx.Deploy, new SequenceStarted(shooting, "shooting", new SequenceContext(Squad, [], [], "Hold")),
            new SequenceStarted(attack, "attack-sequence", new SequenceContext(Squad, [Orcs], [Rifle1], "Hold")));
        _cues.Seed(mid);
        Assert.Equal([Cue.Shoot, Cue.Miss], _cues.OnBatch([Rolled(attack, "hit", false)], mid, false, new Names(TableUx.Game, mid)).Select(c => c.Cue));
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
