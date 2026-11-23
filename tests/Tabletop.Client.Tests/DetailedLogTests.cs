using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Detailed-log spec → What it shows: LogSheet's detailed paragraphs over built records.</summary>
public class DetailedLogTests
{
    static readonly Guid Enemy = Guid.Parse("0198b7ac-2222-7000-8000-000000000002");
    static Guid Rifle => Samples.Army.Units[0].Models[0].Weapons[0].Id;
    static readonly RoundStarted Round1 = new(1, [Samples.Team1, Samples.Team2], []);
    static Die D(int raw, int modified, bool success) => new(raw, modified, success);

    internal static IReadOnlyList<LogParagraph> Block(params EventPayload[] steps)
    {
        var a = new EventApplier(Samples.Board, Samples.Game);
        EventPayload[] history = [Round1, new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Hold")), .. steps];
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        return LogSheet.Of(CombatLog.Build(a.Log, new Names(Samples.Game, a.Projection)), true).Where(p => p.Depth == 1).ToList();
    }

    static string Unit => Samples.Army.Units[0].Name;
    static string Weapon => Samples.Army.Units[0].Models[0].Weapons[0].Name;
    static string EnemyName => new Names(Samples.Game, null).Unit(Enemy);

    [Fact]
    public void A_hit_reads_the_attack_its_weapons_what_unstoppable_took_away_its_faces_and_the_hits()
    {
        var hit = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [D(2, 2, false), D(6, 6, true), D(4, 4, true)], null, new ThresholdBasis("Quality", 4, []),
            Ignored: [new IgnoredEffect("Bad Shot", "Unstoppable", -1)]);
        var lines = Assert.Single(Block(new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], [], ["Unstoppable"]))).Lines;
        Assert.Equal($"{Unit} → {EnemyName} · {Weapon}", lines[0]);
        Assert.StartsWith($"  {Weapon} ×1 (", lines[1]);
        Assert.Equal(
        [
            "Hit roll · Quality 4+ · Bad Shot -1 ignored (Unstoppable) (3 dice)",
            " 1  2  3  4  5  6",
            " 0  1  0  1  0  1",
            "2 hits",
            "H: 2  W: 0",
            "Rules: Unstoppable",
            "",
        ], lines.Skip(2));
    }

    /// <summary>Extra attacks taken on an `extra_attacks` Ask complete roll-to-hit a second time in the frame: one attack, the weapons once, a table each.</summary>
    [Fact]
    public void A_second_roll_to_hit_in_the_frame_is_the_same_attack_with_a_table_of_its_own()
    {
        var pool = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [D(6, 6, true), D(2, 2, false)]);
        var added = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [D(5, 5, true)]);
        var lines = Assert.Single(Block(new StepCompleted(Samples.Frame, "roll-to-hit", null, [pool], []), new StepCompleted(Samples.Frame, "roll-to-hit", null, [added], []))).Lines;
        Assert.Single(lines, l => l.StartsWith($"  {Weapon}", StringComparison.Ordinal));
        Assert.Equal(["Hit roll · 4+ (2 dice)", "Hit roll · 4+ (1 die)"], lines.Where(l => l.StartsWith("Hit roll", StringComparison.Ordinal)));
        Assert.Contains("2 hits", lines);
    }

    /// <summary>3.6.0 p.13: tokens give "+1/-1 to the roll per token", so the detail adds them to the die and keeps the 4+.</summary>
    [Fact]
    public void A_casting_roll_adds_its_tokens_to_the_die_and_keeps_its_4_plus()
    {
        var cast = new RollRecord(Guid.CreateVersion7(), "casting", 4, [new RollModifier("tokens", 1)], [D(1, 2, false)]);
        var lines = Block(new StepCompleted(Samples.Frame, "roll", null, [cast], [])).SelectMany(p => p.Lines).ToList();
        Assert.Contains("Casting · needs 4+ · +1 token", lines);
        Assert.Contains("rolled 1 +1 token = 2", lines);
        Assert.DoesNotContain(lines, l => l.Contains("needs 3", StringComparison.Ordinal));
    }

    [Fact]
    public void A_block_lists_each_change_what_was_ignored_and_each_modifier_with_its_sign_over_its_table()
    {
        var block = new RollRecord(Guid.CreateVersion7(), "defense", 5, [new RollModifier("cover", 1), new RollModifier("AP", -4, "Rending")],
            [D(6, 3, true), D(3, 0, false)], null, new ThresholdBasis("Defense", 5, [new ThresholdChange("Shaken", null, 1)], [new IgnoredEffect("Hero", "Curse", 0, 3)]),
            new CoverVerdict(true, 2, 3));
        var lines = Assert.Single(Block(new StepCompleted(Samples.Frame, "roll-to-block", null, [block], []))).Lines;
        Assert.Equal(
        [
            "",
            "Defense 5+ · Shaken +1 · Hero 3+ ignored (Curse) · Cover +1 · Rending AP(4) -4 · needs 6+ (2 dice)",
            " 1  2  3  4  5  6",
            " 0  0  1  0  0  1",
            "a 6 always succeeds, a 1 always fails",
            "H: 2  W: 1",
            "",
        ], lines.SkipWhile(l => l.Length > 0));
    }

    [Fact]
    public void Cover_not_had_reads_nothing_cover_ignored_names_the_rule_and_a_fatigued_strike_its_raw_face()
    {
        var out_ = new RollRecord(Guid.CreateVersion7(), "defense", 4, [], [D(5, 5, true)], null, null, new CoverVerdict(false, 1, 4));
        var ignored = new RollRecord(Guid.CreateVersion7(), "defense", 4, [], [D(5, 5, true)], null, null, new CoverVerdict(true, 3, 4, "Blast"));
        var fatigued = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [D(5, 5, false), D(6, 6, true)], 6);
        var lines = Block(new StepCompleted(Samples.Frame, "roll-to-hit", null, [fatigued], []), new StepCompleted(Samples.Frame, "roll-to-block", null, [out_, ignored], []))
            .Single().Lines;
        Assert.Contains("Defense 4+ (1 die)", lines);
        Assert.Contains("Defense 4+ · Cover ignored (Blast) (1 die)", lines);
        Assert.Contains("Hit roll · unmodified 6+ (2 dice)", lines);
    }

    [Fact]
    public void A_forced_reroll_of_one_die_names_its_rule_and_the_table_keeps_the_faces_as_rolled()
    {
        var roll = new RollRecord(Samples.Roll, "defense", 4, [], Enumerable.Range(0, 11).Select(i => D(i % 6 + 1, i % 6 + 1, i % 6 + 1 >= 4)).ToArray());
        var lines = Block(new StepCompleted(Samples.Frame, "roll-to-block", null, [roll], []), new DiceRerolled(Samples.Frame, Samples.Roll, [new DieReplacement(5, D(6, 6, true), D(2, 2, false), "Bane")]))
            .Single().Lines.ToList();
        var at = lines.IndexOf("Defense 4+ (11 dice)");
        Assert.Equal([" 1  2  3  4  5  6", " 2  2  2  2  2  1", "Rerolled (Bane): 6 → 2"], lines[(at + 1)..(at + 4)]);
        Assert.Contains("H: 11  W: 7", lines);
    }

    [Fact]
    public void A_step_names_a_rule_a_suppression_removed_and_a_wound_line_lists_each_model()
    {
        var model = Samples.Model;
        var paragraph = Assert.Single(Block(new StepCompleted(Samples.Frame, "assign", null, [], [new WoundsSet(model, 2)]) { Ignored = [new IgnoredEffect("Regeneration", "Rending")] }));
        var line = Assert.IsType<LineEntry>(paragraph.Entry);
        Assert.Equal("1 model wounded", line.Text);
        Assert.Equal(["Regeneration ignored: Rending", $"{NameOf(model)} · 2 wounds", ""], paragraph.Lines);
    }

    [Fact]
    public void A_record_written_before_the_detailed_fields_still_reads()
    {
        var old = new RollRecord(Guid.CreateVersion7(), "morale", 4, [], [D(3, 3, false)]);
        var paragraph = Assert.Single(Block(new StepCompleted(Samples.Frame, "test", null, [old], [])));
        Assert.Equal(["Morale · 4+", "rolled 3", "failed", ""], paragraph.Lines);
    }

    static string NameOf(Guid model)
    {
        var a = new EventApplier(Samples.Board, Samples.Game);
        return new Names(Samples.Game, a.Projection).Model(model);
    }
}
