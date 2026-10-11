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

    [Fact]
    public void A_roll_reads_in_its_frames_words_with_its_basis_modifiers_dice_and_rules()
    {
        var basis = new ThresholdBasis("Quality", 5, [new ThresholdChange("Reliable", 2, 0), new ThresholdChange("Steady", null, 1)]);
        var hit = new RollRecord(Samples.Roll, "hit", 3, [new RollModifier("cover", 1), new RollModifier("player", 1)], [new Die(3, 5, true), new Die(1, 3, false), new Die(6, 8, true)], null, basis);
        var (entries, log, n) = Build(Round1, new SequenceStarted(Samples.Frame, "shooting", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Hold")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], [], ["Reliable"]));
        var roll = Assert.Single(Assert.IsType<ActivationBlock>(entries[1]).Entries.OfType<RollEntry>());
        Assert.Equal($"Battle Brothers → {Names.Short(Enemy)} · Heavy Rifle · Hit 3+ · ⚂ ⚀ ⚅ · 2 hits", roll.Collapsed(CombatLog.Pip));
        Assert.Equal(["Quality 5+", "Reliable: 2+", "Steady: +1", "Target 3+"], roll.Basis);
        Assert.Equal(["+1 cover", "+1 player"], roll.Modifiers);
        Assert.Equal(["3 → 5 pass", "1 → 3 fail", "6 → 8 pass"], roll.Dice.Select(d => d.Text));
        Assert.Equal(["Reliable"], roll.Applied);
        Assert.Equal(Samples.Roll, CombatLog.Latest(log, n)!.Record.Id);
    }

    [Fact]
    public void A_raw_face_roll_is_marked_and_a_roll_without_a_basis_shows_its_threshold_alone()
    {
        var (entries, _, _) = Build(new SequenceStarted(Samples.Frame, "melee", new SequenceContext(Samples.Unit, [], [], null)),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [new RollRecord(Samples.Roll, "hit", 6, [], [new Die(6, 6, true)], 6)], []));
        var roll = entries.OfType<RollEntry>().Single();                   // no action: a roll between blocks
        Assert.Equal(["Target 6+"], roll.Basis);
        Assert.Equal("6 pass (raw face)", roll.Dice[0].Text);
        Assert.Equal("Battle Brothers · Hit 6+ · ⚅ · passed", roll.Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void Rerolls_and_edits_attach_to_the_roll_they_name_and_a_players_modifier_is_its_own_line()
    {
        var other = Samples.Hit with { Id = Guid.CreateVersion7() };
        var (entries, _, _) = Build(new SequenceStarted(Samples.Frame, "x", new SequenceContext(Samples.Unit, [], [], null)),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit, other], []),
            new DiceEdited(Samples.Frame, Samples.Roll, [new DieReplacement(0, new Die(6, 6, true), new Die(1, 1, false))]),
            new DiceRerolled(Samples.Frame, Samples.Roll, [new DieReplacement(1, new Die(2, 2, false), new Die(5, 5, true))]),
            new ModifierAdded(Samples.Frame, "hit", new RollModifier("Clay", 1)));
        var rolls = entries.OfType<RollEntry>().ToList();
        Assert.Equal(["Set by hand: replaced 6", "Rerolled: replaced 2"], rolls[0].Amendments.Select(a => a.Text));
        Assert.Equal([1, 5], rolls[0].Dice.Select(d => d.Raw));
        Assert.Empty(rolls[1].Amendments);                                  // the other pool of the same step
        Assert.Equal([6, 2], rolls[1].Dice.Select(d => d.Raw));
        Assert.Equal(["Clay began x with Battle Brothers", "Clay added +1 to the hit roll"], entries.OfType<LineEntry>().Select(l => l.Text));
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
        Assert.Equal(3, entries.Count);
        var block = Assert.IsType<ActivationBlock>(entries[1]);
        Assert.Equal("Round 2 · Team 1 · Battle Brothers · Advance", block.Header);
        Assert.Equal("Hit 1 → 1 model wounded → Shaken", block.Chain);
        Assert.IsType<LineEntry>(entries[2]);
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
        Assert.Contains(blocks[1].Entries, e => e.Event.Payload is SequenceFinished);
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
        Assert.Equal([3L, 4, 5, 6, 7], block.Entries.Select(e => e.Seq));
        Assert.Equal([false, true, false, false, false], block.Entries.Select(e => e.Undone));
        Assert.Equal("Hit 1 → Hit 1", block.Chain);
    }
}
