using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Detailed-log spec → What it shows: RollEntry.Worked and LineEntry.Worked over built records.</summary>
public class DetailedLogTests
{
    static readonly Guid Enemy = Guid.Parse("0198b7ac-2222-7000-8000-000000000002");
    static Guid Rifle => Samples.Army.Units[0].Models[0].Weapons[0].Id;
    static readonly RoundStarted Round1 = new(1, [Samples.Team1, Samples.Team2], []);
    static Die D(int raw, int modified, bool success) => new(raw, modified, success);

    static IReadOnlyList<LogEntry> Block(params StepCompleted[] steps)
    {
        var a = new EventApplier(Samples.Board, Samples.Game);
        EventPayload[] history = [Round1, new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Hold")), .. steps];
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        return Assert.IsType<ActivationBlock>(CombatLog.Build(a.Log, new Names(Samples.Game, a.Projection))[1]).Entries;
    }

    static string Unit => Samples.Army.Units[0].Name;
    static string Weapon => Samples.Army.Units[0].Models[0].Weapons[0].Name;

    [Fact]
    public void A_hit_reads_the_attack_the_printed_quality_what_unstoppable_took_away_each_die_and_the_hits()
    {
        var hit = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [D(2, 2, false), D(6, 6, true), D(4, 4, true)], null, new ThresholdBasis("Quality", 4, []),
            Ignored: [new IgnoredEffect("Bad Shot", "Unstoppable", -1)]);
        var roll = Assert.IsType<RollEntry>(Assert.Single(Block(new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], [], ["Unstoppable"]))));
        var worked = roll.Worked;
        Assert.StartsWith($"Attack: {Unit} (1 model) → ", worked[0]);
        Assert.StartsWith($"  {Weapon} ×1 (", worked[1]);
        Assert.Equal(
        [
            "Hit",
            "  Quality          4+   printed",
            "  Bad Shot         -1   ignored: Unstoppable",
            "  target 4+",
            "  rolled   2  6  4",
            "  hits     ·  ✓  ✓",
            "  rules: Unstoppable",
            "  = 2 hits",
        ], worked.Skip(2));
    }

    /// <summary>Extra attacks taken on an `extra_attacks` Ask complete roll-to-hit a second time in the frame: the attack is read once, on the pool.</summary>
    [Fact]
    public void A_second_roll_to_hit_in_the_frame_reads_no_second_attack()
    {
        var pool = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [D(6, 6, true), D(2, 2, false)]);
        var added = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [D(5, 5, true)]);
        var rolls = Block(new StepCompleted(Samples.Frame, "roll-to-hit", null, [pool], []), new StepCompleted(Samples.Frame, "roll-to-hit", null, [added], []))
            .OfType<RollEntry>().ToList();
        Assert.Equal(2, rolls.Count);
        Assert.StartsWith("Attack: ", rolls[0].Worked[0]);
        Assert.Null(rolls[1].Attack);
        Assert.Equal("Hit", rolls[1].Worked[0]);
    }

    [Fact]
    public void A_block_reads_cover_ap_the_final_values_and_the_six_that_passed_against_them()
    {
        var block = new RollRecord(Guid.CreateVersion7(), "defense", 5, [new RollModifier("cover", 1), new RollModifier("AP", -4, "Rending")],
            [D(6, 3, true), D(3, 0, false)], null, new ThresholdBasis("Defense", 5, [new ThresholdChange("Shaken", null, 1)], [new IgnoredEffect("Hero", "Curse", 0, 3)]),
            new CoverVerdict(true, 2, 3));
        var roll = Assert.IsType<RollEntry>(Assert.Single(Block(new StepCompleted(Samples.Frame, "roll-to-block", null, [block], []))));
        Assert.Equal(
        [
            "Block",
            "  Defense          5+   printed",
            "  Shaken           +1   → 6+",
            "  Hero             3+   ignored: Curse",
            "  cover            +1   2/3 models in cover",
            "  Rending AP(+4)   -4",
            "  target 5+, roll -3",
            "  rolled   6  3",
            "  final    3  0",
            "  blocks   ✓  ·",
            "  a 6 always succeeds, a 1 always fails (p.5)",
            "  = 1 blocked, 1 wound",
        ], roll.Worked);
    }

    [Fact]
    public void Cover_not_had_or_ignored_reads_its_count_and_a_fatigued_strike_its_raw_face()
    {
        var out_ = new RollRecord(Guid.CreateVersion7(), "defense", 4, [], [D(5, 5, true)], null, null, new CoverVerdict(false, 1, 4));
        var ignored = new RollRecord(Guid.CreateVersion7(), "defense", 4, [], [D(5, 5, true)], null, null, new CoverVerdict(true, 3, 4, "Blast"));
        var fatigued = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [D(5, 5, false), D(6, 6, true)], 6);
        var entries = Block(new StepCompleted(Samples.Frame, "roll-to-block", null, [out_, ignored], []), new StepCompleted(Samples.Frame, "roll-to-hit", null, [fatigued], [])).OfType<RollEntry>().ToList();
        Assert.Contains("  cover            --   no cover: 1/4 models in cover", entries[0].Worked);
        Assert.Contains("  cover            +1   ignored: Blast (3/4 models in cover)", entries[1].Worked);
        Assert.Contains("  target unmodified 6+", entries[2].Worked);
        Assert.DoesNotContain(entries[2].Worked, l => l.StartsWith("  final"));
    }

    [Fact]
    public void Eleven_dice_take_two_rows_and_a_forced_reroll_names_its_rule()
    {
        var roll = new RollRecord(Samples.Roll, "defense", 4, [], Enumerable.Range(0, 11).Select(i => D(i % 6 + 1, i % 6 + 1, i % 6 + 1 >= 4)).ToArray());
        var a = new EventApplier(Samples.Board, Samples.Game);
        EventPayload[] history = [Round1, new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Hold")),
            new StepCompleted(Samples.Frame, "roll-to-block", null, [roll], []), new DiceRerolled(Samples.Frame, Samples.Roll, [new DieReplacement(5, D(6, 6, true), D(2, 2, false), "Bane")])];
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        var entry = Assert.IsType<ActivationBlock>(CombatLog.Build(a.Log, new Names(Samples.Game, a.Projection))[1]).Entries.OfType<RollEntry>().Single();
        Assert.Equal("  rolled   1  2  3  4  5  2  1  2  3  4", entry.Worked[2]);
        Assert.Equal("  rolled   5", entry.Worked[4]);
        Assert.Contains("  rerolled die 6: 6 → 2 (Bane)", entry.Worked);
    }

    [Fact]
    public void A_step_names_a_rule_a_suppression_removed_and_a_wound_line_lists_each_model()
    {
        var model = Samples.Model;
        var entries = Block(new StepCompleted(Samples.Frame, "assign", null, [], [new WoundsSet(model, 2)]) { Ignored = [new IgnoredEffect("Regeneration", "Rending")] });
        var line = Assert.IsType<LineEntry>(Assert.Single(entries));
        Assert.Equal("1 model wounded", line.Text);
        Assert.Equal(["Regeneration ignored: Rending", $"{NameOf(model)} · 2 wounds"], line.Worked!);
    }

    [Fact]
    public void A_record_written_before_the_detailed_fields_still_reads()
    {
        var old = new RollRecord(Guid.CreateVersion7(), "morale", 4, [], [D(3, 3, false)]);
        var roll = Assert.IsType<RollEntry>(Assert.Single(Block(new StepCompleted(Samples.Frame, "test", null, [old], []))));
        Assert.Equal(["Morale", "  target 4+", "  rolled   3", "  passes   ·", "  = failed"], roll.Worked);
    }

    static string NameOf(Guid model)
    {
        var a = new EventApplier(Samples.Board, Samples.Game);
        return new Names(Samples.Game, a.Projection).Model(model);
    }
}
