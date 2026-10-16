using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class CombatLogTests
{
    static readonly Guid Enemy = Guid.Parse("0198b7ac-2222-7000-8000-000000000002");
    static Guid Rifle => Samples.Army.Units[0].Models[0].Weapons[0].Id;
    static readonly RoundStarted Round1 = new(1, [Samples.Team1, Samples.Team2], []);

    static (IReadOnlyList<LogEntry> Entries, IReadOnlyList<GameEvent> Log, Names Names) Build(params EventPayload[] history) => BuildOn(Samples.Board, history);

    static (IReadOnlyList<LogEntry> Entries, IReadOnlyList<GameEvent> Log, Names Names) BuildOn(Board board, params EventPayload[] history)
    {
        var a = new EventApplier(board, Samples.Game);
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        var n = new Names(Samples.Game, a.Projection);
        return (CombatLog.Build(a.Log, n), a.Log, n);
    }

    /// <summary>The one roll a single step of a shooting frame records, as the log builds it.</summary>
    static RollEntry Line(RollRecord roll)
    {
        var (entries, _, _) = Build(Round1, new SequenceStarted(Samples.Frame, "shooting", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Hold")),
            new StepCompleted(Samples.Frame, "roll", null, [roll], []));
        return Assert.IsType<ActivationBlock>(entries[1]).Entries.OfType<RollEntry>().Single();
    }

    static readonly ThresholdBasis Defense5 = new("Defense", 5, []), Quality3 = new("Quality", 3, []), Quality4 = new("Quality", 4, []);
    static Die D(int raw, int modified, bool success, params string[] triggered) => new(raw, modified, success, triggered.Length == 0 ? null : triggered);
    static RollRecord Roll(string roll, int threshold, RollModifier[] modifiers, Die[] dice, ThresholdBasis? basis = null, CoverVerdict? cover = null) =>
        new(Guid.CreateVersion7(), roll, threshold, modifiers, dice, null, basis, cover);

    [Fact]
    public void A_block_in_cover_folds_cover_and_ap_into_what_it_needs_and_shows_the_raw_faces()
    {
        var r = Line(Roll("defense", 5, [new RollModifier("cover", 1), new RollModifier("AP", -1)], [D(6, 6, true), D(2, 2, false), D(1, 1, false)], Defense5, new CoverVerdict(true, 4, 6)));
        Assert.Equal("Block · Defense 5+ (cover +1, AP -1) → needs 5 · 3 dice: ⚅ ⚁ ⚀ · 1 blocked, 2 wounds", r.Collapsed(CombatLog.Pip));
        Assert.Contains("In cover: 4 of 6 models", r.Details);
    }

    [Fact]
    public void A_block_out_of_cover_says_how_many_models_were_in_cover()
    {
        var r = Line(Roll("defense", 5, [new RollModifier("AP", -1)], [D(1, 0, false)], Defense5, new CoverVerdict(false, 2, 6)));
        Assert.Equal("Block · Defense 5+ (no cover: 2/6 models, AP -1) → needs 6 · ⚀ · 0 blocked, 1 wound", r.Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void A_block_whose_cover_a_rule_ignored_names_the_rule()
    {
        var r = Line(Roll("defense", 5, [], [D(4, 4, false)], Defense5, new CoverVerdict(true, 1, 1, "Blast")));
        Assert.Equal("Block · Defense 5+ (cover ignored (Blast)) · ⚃ · 0 blocked, 1 wound", r.Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void A_granted_modifier_carries_the_rule_that_granted_it_and_past_six_needs_six()
    {
        var r = Line(Roll("defense", 5, [new RollModifier("AP", -1), new RollModifier("AP", -4, "Rending")], [D(6, 1, true)], Defense5, new CoverVerdict(false, 0, 3)));
        Assert.Equal("Block · Defense 5+ (no cover: 0/3 models, AP -1, Rending AP -4) → needs 6 · ⚅ · 1 blocked, 0 wounds", r.Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void An_old_record_without_basis_or_verdict_folds_its_modifiers_into_its_threshold()
    {
        var r = Line(Roll("defense", 5, [new RollModifier("AP", -1)], [D(1, 0, false)]));
        Assert.Equal("Block · 5+ (AP -1) → needs 6 · ⚀ · 0 blocked, 1 wound", r.Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void A_hit_counts_the_dice_that_set_off_a_rule_by_the_rule()
    {
        var r = Line(Roll("hit", 3, [], [D(2, 2, false), D(6, 6, true, "Rending"), D(1, 1, false), D(6, 6, true, "Rending"), D(6, 6, true, "Rending"), D(1, 1, false), D(5, 5, true), D(1, 1, false)], Quality3));
        Assert.Equal("Hit · Quality 3+ · 8 dice: ⚁ ⚅ ⚀ ⚅ ⚅ ⚀ ⚄ ⚀ · 4 hits (3 sixes: Rending)", r.Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void A_hits_extra_hits_add_to_its_total()
    {
        var r = Line(Roll("hit", 4, [], [D(6, 6, true, "Furious +1 hit"), D(6, 6, true, "Furious +1 hit")], Quality4));
        Assert.Equal("Hit · Quality 4+ · 2 dice: ⚅ ⚅ · 4 hits (2 sixes: Furious +2 hits)", r.Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void A_threshold_basis_change_is_named_and_below_two_needs_two()
    {
        var basis = new ThresholdBasis("Quality", 5, [new ThresholdChange("Reliable", 2, 0)]);
        var r = Line(Roll("hit", 2, [new RollModifier("Spell", 1)], [D(1, 2, false)], basis));
        Assert.Equal("Hit · Quality 5+ (Reliable 2+, Spell +1) → needs 2 · ⚀ · 0 hits", r.Collapsed(CombatLog.Pip));
        Assert.Equal(["Heavy Rifle", "Quality 5+", "Reliable: 2+", "Spell +1", "Target 2+"], r.Details);
    }

    [Fact]
    public void A_single_die_test_reads_passed_or_failed_and_a_raw_face_roll_says_unmodified()
    {
        Assert.Equal("Morale · Quality 4+ · ⚀ · failed", Line(Roll("morale", 4, [], [D(1, 1, false)], Quality4)).Collapsed(CombatLog.Pip));
        var fatigued = new RollRecord(Guid.CreateVersion7(), "hit", 6, [], [D(6, 6, true)], 6);
        Assert.Equal("Hit · 6+ unmodified · ⚅ · 1 hit", Line(fatigued).Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void Rerolls_and_edits_stay_on_the_rolls_line_and_a_players_modifier_is_its_own_line()
    {
        var other = Samples.Hit with { Id = Guid.CreateVersion7() };
        var (entries, _, _) = Build(new SequenceStarted(Samples.Frame, "x", new SequenceContext(Samples.Unit, [], [], null)),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit, other], []),
            new DiceEdited(Samples.Frame, Samples.Roll, [new DieReplacement(0, new Die(6, 6, true), new Die(1, 1, false))]),
            new DiceRerolled(Samples.Frame, Samples.Roll, [new DieReplacement(1, new Die(2, 2, false), new Die(5, 5, true))]),
            new ModifierAdded(Samples.Frame, "hit", new RollModifier("Clay", 1)));
        var rolls = entries.OfType<RollEntry>().ToList();
        Assert.Equal("Hit · 4+ (AP -1) → needs 5 · 2 dice: ⚀ ⚄ · 1 hit · set by hand ⚅ → ⚀ · rerolled ⚁ → ⚄", rolls[0].Collapsed(CombatLog.Pip));
        Assert.Empty(rolls[1].Amendments);                                  // the other pool of the same step
        Assert.Equal([6, 2], rolls[1].Dice.Select(d => d.Raw));
        Assert.Equal(["Clay added +1 to the hit roll"], entries.OfType<LineEntry>().Select(l => l.Text));
    }

    [Fact]
    public void A_roll_with_no_target_number_shows_its_faces_without_a_result()
    {
        var roll = new RollRecord(Samples.Roll, "roll-off", null, [new RollModifier("bonus", 1)], [new Die(4, 5, false), new Die(2, 2, false)]);
        var (entries, _, _) = Build(Round1, new SequenceStarted(Samples.Frame, "x", new SequenceContext(Samples.Unit, [], [], "Hold")),
            new StepCompleted(Samples.Frame, "roll", null, [roll], []), new SequenceFinished(Samples.Frame));
        var block = Assert.IsType<ActivationBlock>(entries[1]);
        var r = block.Entries.OfType<RollEntry>().Single();
        Assert.Equal(["4 → 5", "2"], r.Dice.Select(d => d.Text));
        Assert.Equal("", r.Result);
        Assert.Equal("Roll off · 2 dice: ⚃ ⚁", r.Collapsed(CombatLog.Pip));
        Assert.Equal("Roll off", block.Chain);
        Assert.False(r.Judged);
    }

    [Fact]
    public void Questions_answers_closes_empty_steps_and_frame_bookkeeping_are_not_drawn()
    {
        var nested = Guid.CreateVersion7();
        var ask = new Ask(Samples.AskId, "q", "Pick", [Samples.Team1], new Confirmation(), false, false, null, Samples.Frame);
        var (entries, _, _) = Build(Round1, new TurnStarted(Samples.Team1),
            new SequenceStarted(Samples.Frame, "charge", new SequenceContext(Samples.Unit, [Enemy], [], "Charge")),
            new AskOpened(ask), new AskAnswered(Samples.AskId, Samples.Team1, new Confirmed(), false, null), new AskClosed(Samples.AskId, []),
            new StepCompleted(Samples.Frame, "charge-target", null, [], []),
            new SequenceStarted(nested, "melee", new SequenceContext(Samples.Unit, [Enemy], [], null)),
            new SequenceFinished(nested), new SequenceAbandoned(Samples.Frame), new TurnEnded(Samples.Team1, false, []));
        Assert.Equal(["Round 1"], entries.OfType<LineEntry>().Select(l => l.Text));
        Assert.Empty(Assert.IsType<ActivationBlock>(entries[1]).Entries);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void A_header_names_the_round_team_unit_action_targets_and_the_farthest_move()
    {
        var to = new OnTable(Samples.At(12, 19.4));
        var (entries, _, _) = Build(Round1, new ModelsMoved([Samples.Deploy]), new RoundStarted(2, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Samples.Frame, "charge", new SequenceContext(Samples.Unit, [Enemy], [], "Charge")),
            new StepCompleted(Samples.Frame, "move", null, [], [new ModelsMoved([new ModelRelocation(Samples.Model, Samples.Deploy.To, to)])]),
            new SequenceFinished(Samples.Frame));
        var block = entries.OfType<ActivationBlock>().Single();
        Assert.Equal($"R2 · Team 1 · Battle Brothers · Charge → {Names.Short(Enemy)} · moved 7.5\"", block.Header);
        Assert.Empty(block.Entries);                                        // the move is in the header
    }

    [Fact]
    public void A_unit_sent_to_the_shelf_unwounded_after_a_failed_morale_test_fled()
    {
        var morale = Roll("morale", 4, [], [D(1, 1, false)], Quality4);
        var (entries, _, _) = Build(Round1, new ModelsMoved([Samples.Deploy]),
            new SequenceStarted(Samples.Frame, "charge", new SequenceContext(Samples.Unit, [Enemy], [], "Charge")),
            new StepCompleted(Samples.Frame, "test", null, [morale], []),
            new StepCompleted(Samples.Frame, "consequence", null, [], [new ModelsMoved([new ModelRelocation(Samples.Model, Samples.Deploy.To, new OnShelf())])]),
            new SequenceFinished(Samples.Frame));
        var block = entries.OfType<ActivationBlock>().Single();
        Assert.Equal(["Morale · Quality 4+ · ⚀ · failed", "Battle Brothers Fled!"],
            block.Entries.Select(e => e is RollEntry r ? r.Collapsed(CombatLog.Pip) : ((LineEntry)e).Text));
        Assert.Equal("Morale failed → Battle Brothers Fled!", block.Chain);
    }

    [Fact]
    public void An_undo_reads_undone_to_the_header_it_took_back_and_a_menu_edit_is_by_hand()
    {
        var (entries, _, _) = Build(Round1,
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []),
            new SequenceFinished(Samples.Frame),
            new Undone(1), new WoundsSet(Samples.Model, 1));
        var lines = entries.OfType<LineEntry>().Select(l => l.Text).ToList();
        Assert.Contains("Undone to: R1 · Team 1 · Battle Brothers · Advance", lines);
        Assert.Contains(lines, l => l.StartsWith("Clay set ") && l.EndsWith(" to 1 wound · by hand"));
    }

    [Fact]
    public void An_action_frame_groups_its_entries_under_one_header_with_a_chain_summary()
    {
        var nested = Guid.CreateVersion7();
        var (entries, _, _) = Build(new RoundStarted(2, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Advance")),
            new SequenceStarted(nested, "attack-sequence", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Advance")),
            new StepCompleted(nested, "roll-to-hit", null, [Samples.Hit], []),
            new StepCompleted(nested, "remove", null, [], [new WoundsSet(Samples.Model, 1), new ConditionSet(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", true)]),
            new SequenceFinished(nested), new SequenceFinished(Samples.Frame), new TurnEnded(Samples.Team1, false, []));
        Assert.Equal(2, entries.Count);
        var block = Assert.IsType<ActivationBlock>(entries[1]);
        Assert.Equal($"R2 · Team 1 · Battle Brothers · Advance → {Names.Short(Enemy)}", block.Header);
        Assert.Equal("Hit 1 → 1 model wounded → Shaken", block.Chain);
    }

    [Fact]
    public void An_undone_activation_closes_struck_through_and_an_undo_inside_one_keeps_it_open()
    {
        var second = Guid.CreateVersion7();
        var (entries, log, n) = Build(Round1,
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []),
            new Undone(1),                                                                              // before the block: closes it
            new SequenceStarted(second, "hold", new SequenceContext(Samples.Unit, [], [], "Hold")),
            new StepCompleted(second, "roll-to-hit", null, [Samples.Hit with { Id = Guid.CreateVersion7() }], []),
            new Undone(5),                                                                              // inside the block: stays open
            new SequenceFinished(second));
        var blocks = entries.OfType<ActivationBlock>().ToList();
        Assert.Equal([true, false], blocks.Select(b => b.Undone));
        Assert.All(blocks[0].Entries, e => Assert.True(e.Undone));
        Assert.Equal(("", ""), (blocks[0].Chain, blocks[1].Chain));                                     // an undone roll is not in a chain
        Assert.True(blocks[1].Entries.OfType<RollEntry>().Single().Undone);
        Assert.Contains(blocks[1].Entries, e => e is LineEntry { Event.Payload: Undone });
        Assert.Contains(entries, e => e is LineEntry { Event.Payload: Undone });
        Assert.Null(CombatLog.Latest(log, n));
    }

    [Fact]
    public void Filters_keep_rolls_my_units_or_the_carded_unit()
    {
        var mine = Guid.CreateVersion7(); var theirs = Guid.CreateVersion7();
        var (entries, _, n) = Build(Round1,
            new SequenceStarted(mine, "hold", new SequenceContext(Samples.Unit, [], [], "Hold")), new SequenceFinished(mine),
            new SequenceStarted(theirs, "advance", new SequenceContext(Enemy, [], [], "Advance")),
            new StepCompleted(theirs, "roll-to-hit", null, [Samples.Hit], []), new SequenceFinished(theirs));
        bool Mine(Guid u) => n.TeamOfUnit(u) == Samples.Team1;
        Guid ActorOf(LogEntry e) => ((ActivationBlock)e).Actor!.Value;
        Assert.Equal([Samples.Unit], CombatLog.Filter(entries, LogFilter.Mine, Mine, null).Select(ActorOf));
        Assert.All(Assert.IsType<ActivationBlock>(Assert.Single(CombatLog.Filter(entries, LogFilter.Rolls, Mine, null))).Entries, e => Assert.IsType<RollEntry>(e));
        Assert.Equal([Enemy], CombatLog.Filter(entries, LogFilter.Unit, Mine, Enemy).Select(ActorOf));
        Assert.Empty(CombatLog.Filter(entries, LogFilter.Unit, Mine, null));
        Assert.Same(entries, CombatLog.Filter(entries, LogFilter.All, Mine, null));
    }

    [Fact]
    public void A_model_wounded_then_removed_in_one_step_reads_removed_not_wounded()
    {
        var unit = Samples.Army.Units[0];
        var second = unit.Models[0] with { Id = Guid.CreateVersion7(), Ordinal = 2 };
        var army = Samples.Board.Armies[0].Snapshot with { Units = [unit with { Models = [unit.Models[0], second] }] };
        var board = Samples.Board with { Armies = [new ArmyInPlay(Samples.P1, army)] };
        var shelved = new ModelRelocation(Samples.Model, Samples.Deploy.To, new OnShelf());
        var (entries, _, _) = BuildOn(board, Round1, new ModelsMoved([Samples.Deploy]),
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [Enemy], [], "Advance")),
            new StepCompleted(Samples.Frame, "remove", null, [], [new WoundsSet(Samples.Model, 3), new ModelsMoved([shelved]), new WoundsSet(second.Id, 1)]),
            new SequenceFinished(Samples.Frame));
        Assert.Equal("1 model wounded → 1 model removed", entries.OfType<ActivationBlock>().Single().Chain);
    }

    [Fact]
    public void An_undo_into_a_finished_activation_reopens_it_until_its_live_finish()
    {
        var (entries, _, _) = Build(Round1,
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []),
            new SequenceFinished(Samples.Frame),
            new Undone(3),                                                                              // into the block: its finish dies
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit with { Id = Guid.CreateVersion7() }], []),
            new SequenceFinished(Samples.Frame));
        Assert.Equal(2, entries.Count);
        var block = Assert.IsType<ActivationBlock>(entries[1]);
        Assert.False(block.Undone);
        Assert.Equal([3L, 5, 6], block.Entries.Select(e => e.Seq));
        Assert.Equal("Hit 1 → Hit 1", block.Chain);
    }

    [Fact]
    public void A_block_is_found_by_the_targets_of_its_nested_frames()
    {
        var nested = Guid.CreateVersion7();
        var (entries, _, _) = Build(Round1,
            new SequenceStarted(Samples.Frame, "shooting", new SequenceContext(Samples.Unit, [], [], "Hold")),
            new SequenceStarted(nested, "attack-sequence", new SequenceContext(Samples.Unit, [Enemy], [Rifle], null)),
            new StepCompleted(nested, "roll-to-hit", null, [Samples.Hit], []),
            new SequenceFinished(nested), new SequenceFinished(Samples.Frame));
        var block = Assert.IsType<ActivationBlock>(Assert.Single(CombatLog.Filter(entries, LogFilter.Unit, _ => false, Enemy)));
        Assert.Equal([Enemy], block.Targets);
        Assert.Single(CombatLog.Filter(entries, LogFilter.Mine, u => u == Enemy, null));                // the attacked unit's side
    }

    [Fact]
    public void An_undone_block_closes_at_its_own_finish_or_abandonment()
    {
        var second = Guid.CreateVersion7(); var third = Guid.CreateVersion7();
        var (entries, _, _) = Build(Round1,
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []),
            new SequenceFinished(Samples.Frame),
            new SequenceStarted(second, "hold", new SequenceContext(Enemy, [], [], "Hold")),
            new StepCompleted(second, "roll-to-hit", null, [Samples.Hit with { Id = Guid.CreateVersion7() }], []),
            new SequenceAbandoned(second),
            new SequenceStarted(third, "hold", new SequenceContext(Samples.Unit, [], [], "Hold")),
            new SequenceFinished(third),
            new Undone(1));
        var blocks = entries.OfType<ActivationBlock>().ToList();
        Assert.Equal([2L, 5, 8], blocks.Select(b => b.Seq));
        Assert.All(blocks, b => Assert.True(b.Undone));
        Assert.Equal([Samples.Unit, Enemy, Samples.Unit], blocks.Select(b => b.Actor!.Value));
        Assert.Single(blocks[0].Entries.OfType<RollEntry>());
        Assert.Single(blocks[1].Entries.OfType<RollEntry>());
    }
}
