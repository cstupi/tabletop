using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class LogSheetTests
{
    static readonly Guid Enemy = Guid.Parse("0198b7ac-2222-7000-8000-000000000002");
    static Guid Rifle => Samples.Army.Units[0].Models[0].Weapons[0].Id;
    static readonly RoundStarted Round1 = new(1, [Samples.Team1, Samples.Team2], []);
    static Guid F(int n) => Guid.Parse($"01a1e0aa-0000-7000-8000-{n:D12}");
    static Die D(int raw, bool success, params string[] triggered) => new(raw, raw, success, triggered.Length == 0 ? null : triggered);
    static RollRecord Roll(string roll, int threshold, RollModifier[] modifiers, Die[] dice, ThresholdBasis? basis = null, CoverVerdict? cover = null) =>
        new(Guid.CreateVersion7(), roll, threshold, modifiers, dice, null, basis, cover);

    static IReadOnlyList<LogEntry> Build(params EventPayload[] history)
    {
        var a = new EventApplier(Samples.Board, Samples.Game);
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        return CombatLog.Build(a.Log, new Names(Samples.Game, a.Projection));
    }

    static string Unit => Samples.Army.Units[0].Name;

    /// <summary>A Hold with one shooting attack: a hit roll of eight dice (two sixes set off Relentless), then a block in cover with AP(1),
    /// then a model wounded.</summary>
    static EventPayload[] Shooting(RollRecord hit, RollRecord block, StateChange[]? changes = null)
    {
        var attack = new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Hold");
        return
        [
            Round1,
            new SequenceStarted(F(1), "hold", new SequenceContext(Samples.Unit, [], [], "Hold")),
            new SequenceStarted(F(2), "attack-sequence", attack),
            new StepCompleted(F(2), "roll-to-hit", null, [hit], [], ["Relentless"]),
            new SequenceStarted(F(3), "resolve-hits", attack),
            new StepCompleted(F(3), "roll-to-block", null, [block], []),
            new SequenceStarted(F(4), "deal-wounds", attack),
            new StepCompleted(F(4), "assign", null, [], changes ?? []),
            new SequenceFinished(F(4)), new SequenceFinished(F(3)), new SequenceFinished(F(2)), new SequenceFinished(F(1)),
        ];
    }

    static readonly RollRecord Hit = Roll("hit", 4, [], [D(1, false), D(2, false), D(3, false), D(4, true), D(5, true), D(6, true, "Relentless +1 hit"), D(6, true, "Relentless +1 hit"), D(4, true)],
        new ThresholdBasis("Quality", 4, []));
    static readonly RollRecord Block = Roll("defense", 5, [new RollModifier("AP", -1), new RollModifier("cover", 1)],
        [D(1, false), D(2, false), D(5, true), D(6, true), D(3, false), D(4, false), D(5, true)], new ThresholdBasis("Defense", 5, []), new CoverVerdict(true, 3, 4));

    [Fact]
    public void An_attack_is_one_summary_line_of_hits_wounds_and_cover()
    {
        var lines = LogSheet.Text(Build(Shooting(Hit, Block)), false).Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("Round 1", lines[0]);
        Assert.StartsWith($"R1 · Team 1 · {Unit} · Hold", lines[1]);
        Assert.Equal($"  {Unit} → {new Names(Samples.Game, null).Unit(Enemy)}  H: 7  W: 4  Cover: Y", lines[2]);
    }

    [Fact]
    public void A_detailed_attack_tables_the_faces_names_what_the_faces_set_off_and_lists_the_block_modifiers_with_their_signs()
    {
        var lines = LogSheet.Of(Build(Shooting(Hit, Block)), true).Single(p => p.Entries.Count == 2).Lines;
        Assert.Equal(
        [
            $"{Unit} → {new Names(Samples.Game, null).Unit(Enemy)} · {Samples.Army.Units[0].Models[0].Weapons[0].Name}",
            "Hit roll · Quality 4+ (8 dice)",
            " 1  2  3  4  5  6",
            " 1  1  1  2  1  2",
            "Relentless (6s: +1 hit)",
            " 1  2  3  4  5  6",
            " 0  0  0  0  0  2",
            "5 hits · 2 bonus hits",
            "",
            "Defense 5+ · AP(1) -1 · Cover +1 · needs 5+ (7 dice)",
            " 1  2  3  4  5  6",
            " 1  1  1  1  2  1",
            "H: 7  W: 4",
            "Rules: Relentless",
            "",
        ], lines.Where(l => !l.StartsWith("  ", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_table_widens_its_columns_for_a_count_past_99() =>
        Assert.Equal(["  1   2   3   4   5   6", "100   0   0   0   0   1"], LogSheet.Table([.. Enumerable.Repeat(1, 100), 6]));

    [Fact]
    public void A_multiplied_attack_counts_the_blocked_dice_as_its_hits_and_the_rest_as_bonus()
    {
        var hit = Roll("hit", 4, [], [D(5, true), D(2, false)], new ThresholdBasis("Quality", 4, []));
        var block = Roll("defense", 5, [], [D(1, false), D(2, false), D(6, true)], new ThresholdBasis("Defense", 5, []), new CoverVerdict(false, 0, 5));
        var events = Shooting(hit, block);
        events[3] = ((StepCompleted)events[3]) with { Values = new Dictionary<string, int> { ["hit_multiplier"] = 3 } };
        var paragraph = LogSheet.Of(Build(events), true).Single(p => p.Entries.Count == 2);
        Assert.Contains("Hits ×3", paragraph.Lines);
        Assert.Contains("1 hit · 2 bonus hits", paragraph.Lines);
        Assert.Contains("Defense 5+ · No cover (0/5) (3 dice)", paragraph.Lines);
        Assert.Contains("H: 3  W: 2", paragraph.Lines);
    }

    [Fact]
    public void Wounds_a_roll_per_wound_ignores_come_off_the_attack_and_the_roll_reads_under_it()
    {
        var regen = Roll("Regeneration", 5, [], [D(6, true), D(2, false), D(3, false), D(1, false)]);
        var events = Shooting(Hit, Block);
        events[7] = new StepCompleted(F(4), "assign", null, [regen], []);
        var summary = LogSheet.Of(Build(events), false).Single(p => p.Entries.Count == 3).Lines;
        Assert.Equal($"{Unit} → {new Names(Samples.Game, null).Unit(Enemy)}  H: 7  W: 3  Cover: Y", summary[0]);
        Assert.Equal("Regeneration 5+: rolled 6 2 3 1, 1 ignored, 3 wounds stand", summary[1]);
        var detailed = LogSheet.Of(Build(events), true).Single(p => p.Entries.Count == 3).Lines;
        Assert.Contains("Regeneration · 5+ (4 wounds)", detailed);
        Assert.Contains("1 ignored", detailed);
    }

    [Fact]
    public void A_reroll_gets_its_own_table_of_the_new_faces_and_the_first_table_keeps_the_faces_as_rolled()
    {
        var events = Shooting(Hit, Block).ToList();
        events.Insert(6, new DiceRerolled(F(3), Block.Id, [new DieReplacement(2, D(5, true), D(1, false), "Bane"), new DieReplacement(3, D(6, true), D(2, false), "Bane")]));
        var lines = LogSheet.Of(Build([.. events]), true).Single(p => p.Entries.Count == 2).Lines.ToList();
        var at = lines.IndexOf("Rerolled (Bane) (2 dice)");
        Assert.Equal([" 1  2  3  4  5  6", " 1  1  0  0  0  0"], lines[(at + 1)..(at + 3)]);
        Assert.Equal(" 1  1  1  1  2  1", lines[at - 1]);
        Assert.Contains("H: 7  W: 6", lines);
    }

    [Fact]
    public void A_test_names_its_unit_when_it_is_not_the_acting_one_and_reads_its_die_and_result()
    {
        var morale = Roll("morale", 4, [], [D(3, false)], new ThresholdBasis("Quality", 4, []));
        var text = LogSheet.Text(Build(
        [
            Round1,
            new SequenceStarted(F(1), "hold", new SequenceContext(Samples.Unit, [], [], "Hold")),
            new SequenceStarted(F(5), "morale-test", new SequenceContext(Enemy, [], [], null)),
            new StepCompleted(F(5), "test", null, [morale], []),
            new SequenceFinished(F(5)), new SequenceFinished(F(1)),
        ]), false);
        Assert.EndsWith($"  {new Names(Samples.Game, null).Unit(Enemy)} · Morale Quality 4+: rolled 3, failed", text);
    }

    [Fact]
    public void Paragraphs_indent_two_spaces_a_depth_and_the_text_is_one_line_each()
    {
        var paragraphs = LogSheet.Of(Build(Shooting(Hit, Block)), false);
        Assert.Equal([0, 0, 1], paragraphs.Select(p => p.Depth));
        Assert.Equal(string.Join("\n", paragraphs.SelectMany(p => p.Indented)), LogSheet.Text(paragraphs));
    }

    [Fact]
    public void Modifiers_read_with_the_sign_the_roll_applies()
    {
        Assert.Equal("AP(1) -1", LogSheet.Modifier(new RollModifier("AP", -1)));
        Assert.Equal("Rending AP(4) -4", LogSheet.Modifier(new RollModifier("AP", -4, "Rending")));
        Assert.Equal("Cover +1", LogSheet.Modifier(new RollModifier("cover", 1)));
        Assert.Equal("Stealth -1", LogSheet.Modifier(new RollModifier("Stealth", -1)));
    }
}
