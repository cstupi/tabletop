using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>The log panel's Copy: the rows as drawn, as plain text.</summary>
public class LogCopyTests
{
    static readonly IReadOnlyList<LogEntry> Entries = CombatLog.Build(CombatLogFixtureTests.Log(), new Names(CombatLogFixtureTests.Game, null));
    static ActivationBlock Block => Entries.OfType<ActivationBlock>().First(b => !b.Undone && b.Entries.OfType<RollEntry>().Any());
    static RollEntry Roll => Block.Entries.OfType<RollEntry>().First();
    static LineEntry Line(string text, bool undone = false, IReadOnlyList<string>? worked = null) =>
        new(7, new GameEvent(7, Samples.P1, new RoundStarted(1, [Samples.Team1, Samples.Team2], []), null), undone, text, worked);

    static string Lines(params string[] lines) => string.Join("\n", lines);

    /// <summary>An activation's own Copy: its header and every entry, every roll open, whatever the panel shows folded.</summary>
    [Fact]
    public void An_activation_copies_whole_with_every_roll_open()
    {
        var b = Block;
        Assert.Equal(LogCopy.Text([b], detailed: true, _ => true), LogCopy.Activation(b, detailed: true));
        var plain = LogCopy.Activation(b, detailed: false).Split('\n');
        Assert.Equal(b.Header, plain[0]);
        foreach (var r in b.Entries.OfType<RollEntry>())
            Assert.Contains(plain, l => l == $"    {r.Dice[0].Text}");
    }

    [Fact]
    public void A_folded_block_is_its_header_and_chain()
    {
        var b = Block;
        Assert.Equal($"{b.Header}  ·  {b.Chain}", LogCopy.Text([b], detailed: false, _ => false));
    }

    [Fact]
    public void An_open_block_indents_its_entries_and_a_closed_roll_is_its_collapsed_line()
    {
        var b = Block with { Entries = [Roll] };
        Assert.Equal(Lines(b.Header, $"  {Roll.Collapsed(CombatLog.Pip)}"), LogCopy.Text([b], detailed: false, e => e is ActivationBlock));
    }

    [Fact]
    public void An_open_roll_adds_its_details_dice_and_amendments()
    {
        var r = Roll;
        var expected = new[] { r.Collapsed(CombatLog.Pip) }.Concat(r.Details.Concat(r.Dice.Select(d => d.Text)).Concat(r.Amendments.Select(a => a.Text)).Select(s => $"  {s}"));
        Assert.Equal(Lines([.. expected]), LogCopy.Text([r], detailed: false, _ => true));
    }

    [Fact]
    public void Detailed_draws_a_roll_and_a_named_line_as_their_worked_lines()
    {
        var r = Roll;
        Assert.Equal(Lines([.. r.Worked, "a", "b"]), LogCopy.Text([r, Line("2 models wounded", worked: ["a", "b"])], detailed: true, _ => true));
        Assert.Equal("2 models wounded", LogCopy.Text([Line("2 models wounded", worked: ["a", "b"])], detailed: false, _ => true));
    }

    [Fact]
    public void An_undone_row_says_so()
    {
        Assert.Equal(Lines("Round 1", "Round 2 (undone)"), LogCopy.Text([Line("Round 1"), Line("Round 2", undone: true)], detailed: false, _ => false));
    }
}
