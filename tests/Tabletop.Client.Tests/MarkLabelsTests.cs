using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>The labels over the table: a short noun phrase per reason (the full sentence is the decision box's), stacked per model and kept
/// apart from each other and off the Hud.</summary>
public class MarkLabelsTests
{
    [Theory]
    [InlineData("overlaps a base", "On a base")]
    [InlineData("Flayer Cavalry 1 would be placed on another model's base: models may never be placed in physically impossible locations", "On a base")]
    [InlineData("path crosses a model", "Through a model")]
    [InlineData("path crosses Orc Warriors", "Through a unit")]
    [InlineData("A 1 would move through F: models may never move through other models or units", "Through a unit")]
    [InlineData("passes within 1\" of Orc Warriors", "Too close 1\"")]
    [InlineData("A 1 would pass within 1\" of F: models may never be within 1\" of models from other units", "Too close 1\"")]
    [InlineData("Flayer Cavalry would move through another model: models may never move through other models or units", "Through a model")]
    [InlineData("ends in impassable terrain", "In impassable terrain")]
    [InlineData("Orc 2 would end its move in impassable terrain", "In impassable terrain")]
    [InlineData("Orc 2 would move through impassable terrain", "Through impassable terrain")]
    [InlineData("within 1\" of Orc Warriors", "Too close 1\"")]
    [InlineData("placed within 9\" of Orc Warriors", "Too close 9\"")]
    [InlineData("Orc 2 would end within 1\" of Specialists: models must stay 1\" away", "Too close 1\"")]
    [InlineData("Flayer Cavalry would end out of coherency: all models in a unit must stay within 1\" of at least one other model and within 9\" of all other models.", "Out of coherency")]
    [InlineData("Orc 2 would leave the table: models may never move outside of the battlefield", "Off the table")]
    [InlineData("Orc 2 would be placed outside its deployment zone: units deploy fully within it", "Outside its zone")]
    [InlineData("Scout 1 would be placed beyond 12\" of its zone", "Beyond 12\"")]
    [InlineData("Dangerous terrain test", "Dangerous terrain test")]
    [InlineData("something the client has no word for", "Against the rules")]
    public void A_reason_reads_as_a_short_noun_phrase_over_the_table(string reason, string words) => Assert.Equal(words, MarkWords.Short(reason));

    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7();
    static readonly HudRect View = new(0, 0, 1280, 720);

    [Fact]
    public void A_models_labels_stack_a_step_apart_over_it()
    {
        var placed = MarkLabels.Place([new(A, (400, 300), 120, 20), new(A, (400, 300), 160, 20)], View);
        Assert.Equal(2, placed.Count);
        var (first, second) = (placed[0], placed[1]);
        Assert.Equal(MarkLabels.Gap, second.Y - first.Bottom, 3);                          // stacked downward, XS apart
        Assert.Equal(400, first.X + first.W / 2, 3); Assert.Equal(400, second.X + second.W / 2, 3);
        Assert.True(second.Bottom <= 300);                                                  // the stack stands over its anchor
    }

    [Fact]
    public void Labels_of_two_models_never_overlap()
    {
        var placed = MarkLabels.Place([new(A, (400, 300), 120, 20), new(B, (410, 305), 160, 20), new(B, (410, 305), 100, 20)], View);
        for (var i = 0; i < placed.Count; i++)
            for (var j = i + 1; j < placed.Count; j++)
                Assert.False(Overlap(placed[i], placed[j]), $"{placed[i]} meets {placed[j]}");
    }

    [Fact]
    public void A_label_keeps_off_the_huds_panels()
    {
        var panel = new HudRect(300, 200, 300, 120);
        var placed = MarkLabels.Place([new(A, (400, 300), 120, 20), new(A, (400, 300), 120, 20)], View, [panel]);
        Assert.All(placed, r => Assert.False(Overlap(r, panel), $"{r} under the panel"));
        Assert.Equal(MarkLabels.Gap, placed[1].Y - placed[0].Bottom, 3);
    }

    [Fact]
    public void Gap_labels_and_mark_labels_share_one_placement_off_the_panels_and_the_bar_over_the_unit()
    {
        // Two coherency gaps whose lines' middles nearly meet, the moved model's "Out of coherency" over them, the bar over the unit on the mark.
        var bar = new HudRect(360, 250, 92, 42);
        var panel = new HudRect(0, 0, 200, 720);
        var (marks, gaps) = MarkLabels.Place([new(A, (405, 300), 150, 22)], [new((400, 320), 160, 20), new((402, 322), 160, 20)], View, [panel], bar);
        Assert.Single(marks); Assert.Equal(2, gaps.Count);
        var all = marks.Concat(gaps).ToList();
        for (var i = 0; i < all.Count; i++)
        {
            Assert.False(Overlap(all[i], bar), $"{all[i]} under the bar");
            Assert.False(Overlap(all[i], panel), $"{all[i]} under the panel");
            for (var j = i + 1; j < all.Count; j++) Assert.False(Overlap(all[i], all[j]), $"{all[i]} meets {all[j]}");
        }
    }

    [Fact]
    public void A_gap_label_hangs_a_step_over_its_lines_middle_when_nothing_is_in_the_way()
    {
        var (_, gaps) = MarkLabels.Place([], [new((400, 320), 160, 20)], View, [], null);
        Assert.Equal(new HudRect(320, 320 - MarkLabels.Gap - 20, 160, 20), gaps[0]);
    }

    static bool Overlap(HudRect a, HudRect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}
