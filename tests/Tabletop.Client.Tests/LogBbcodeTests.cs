using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>The Godot log is one RichTextLabel: what its tags leave, and so what a selection across entries copies, is LogSheet's text.</summary>
public class LogBbcodeTests
{
    static readonly RoundStarted Round1 = new(1, [Samples.Team1, Samples.Team2], []);

    static IReadOnlyList<LogEntry> Entries()
    {
        var hit = new RollRecord(Guid.CreateVersion7(), "hit", 4, [new RollModifier("AP", -1)], [new Die(6, 6, true), new Die(2, 2, false)], null, new ThresholdBasis("Quality", 4, []));
        var a = new EventApplier(Samples.Board, Samples.Game);
        EventPayload[] history = [Round1, new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], "Hold")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], []), new SequenceFinished(Samples.Frame)];
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        return CombatLog.Build(a.Log, new Names(Samples.Game, a.Projection));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void The_text_without_its_tags_is_the_sheet_a_line_each(bool detailed)
    {
        var paragraphs = LogSheet.Of(Entries(), detailed);
        var bbcode = LogBbcode.Of(paragraphs, p => new ParagraphLook(true, "#ff000080", "#00ff0040"), out var lineOf);
        Assert.Equal(LogSheet.Text(paragraphs), LogBbcode.Plain(bbcode));
        Assert.Equal(paragraphs.Count, lineOf.Count);
        Assert.Equal(0, lineOf[0]);
        Assert.Equal(paragraphs[0].Lines.Count, lineOf[1]);
    }

    [Fact]
    public void A_bracket_is_written_so_it_draws_as_itself_and_each_line_links_to_its_paragraph()
    {
        var line = new LineEntry(1, new GameEvent(1, null, Round1, null), false, "Team [1] deployed");
        var bbcode = LogBbcode.Of(LogSheet.Of([line], false), _ => new ParagraphLook(), out _);
        Assert.Equal("[url=0]Team [lb]1] deployed[/url]", bbcode);
        Assert.Equal("Team [1] deployed", LogBbcode.Plain(bbcode));
    }
}
