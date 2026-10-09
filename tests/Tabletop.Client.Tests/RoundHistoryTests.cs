using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class RoundHistoryTests
{
    static readonly GameInfo Game = Samples.Game;
    /// <summary>A unit of the other side: no unit this projection holds, which a SequenceContext may still name.</summary>
    static readonly Guid Enemy = Guid.Parse("0198b7ac-2222-7000-8000-000000000002");
    static readonly Guid Other = Guid.Parse("0198b7ac-3333-7000-8000-000000000003");
    static readonly Placement At0 = new(new Point3(0, 0, 0), new Yaw(0)), At5 = new(new Point3(3, 4, 0), new Yaw(0));

    static EventApplier Applier(params EventPayload[] history)
    {
        var a = new EventApplier(Samples.Board, Game);
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        return a;
    }

    static IReadOnlyList<HistoryLine> Lines(EventApplier a, Guid unit) =>
        RoundHistory.For(EventApplier.EffectiveLog(a.Log), a.Projection, new Names(Game, a.Projection), unit);

    [Fact]
    public void A_unit_not_yet_activated_says_so_above_what_was_done_to_it()
    {
        var a = Applier(new RoundStarted(1, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Enemy, [Samples.Unit], [], null)),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], [new WoundsSet(Samples.Model, 1)]));
        var lines = Lines(a, Samples.Unit);
        Assert.Equal(2, lines.Count);
        Assert.Equal((RoundHistory.NotYetActivated, 0L), (lines[0].Text, lines[0].Seq));
        Assert.Empty(lines[0].Dice);
        Assert.Equal("roll-to-hit: hit 1 of 2 succeeded; set Battle Brothers (Battle Brother) to 1 wound", lines[1].Text);
        Assert.Equal([6, 2], lines[1].Dice);
        Assert.Equal(3, lines[1].Seq);
    }

    [Fact]
    public void The_action_comes_first_and_a_step_reads_by_its_outline_label()
    {
        var a = Applier(new RoundStarted(1, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance"), [new OutlineStep("move", "Move")]),
            new StepCompleted(Samples.Frame, "move", new Destinations([new ModelRelocation(Samples.Model, new OnTable(At0), new OnTable(At5))]), [],
                [new ModelsMoved([new ModelRelocation(Samples.Model, new OnTable(At0), new OnTable(At5))])]),
            new StepCompleted(Samples.Frame, "internal-bookkeeping", null, [], []));
        var lines = Lines(a, Samples.Unit);
        Assert.Equal(["Activated: Advance", "Move: moved 1 model; moved Battle Brothers 5.0\""], lines.Select(l => l.Text));
        Assert.Equal([2L, 3L], lines.Select(l => l.Seq));      // a step with no answer, roll or change writes no line
    }

    [Fact]
    public void Only_this_round_and_only_frames_the_unit_is_in_count()
    {
        var a = Applier(new RoundStarted(1, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []),
            new SequenceFinished(Samples.Frame),
            new RoundStarted(2, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Other, "attack-sequence", new SequenceContext(Enemy, [Enemy], [], "Hold")),
            new StepCompleted(Other, "roll-to-hit", null, [Samples.Hit with { Id = Guid.CreateVersion7() }], []));
        Assert.Equal([RoundHistory.NotYetActivated], Lines(a, Samples.Unit).Select(l => l.Text));
    }

    [Fact]
    public void A_hand_recorded_action_with_no_frame_still_reads_as_activated()
    {
        var a = Applier(new RoundStarted(1, [Samples.Team1, Samples.Team2], []), new ActionRecorded(Samples.Unit, "Hold"));
        Assert.Equal(new[] { ("Activated: Hold", 0L) }, Lines(a, Samples.Unit).Select(l => (l.Text, l.Seq)));
    }

    [Fact]
    public void Edited_and_rerolled_dice_show_on_the_line_that_rolled_them()
    {
        var a = Applier(new RoundStarted(1, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [Enemy], [], null)),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []),
            new DiceEdited(Samples.Frame, Samples.Roll, [new DieReplacement(0, new Die(6, 6, true), new Die(1, 1, false))]),
            new DiceRerolled(Samples.Frame, Samples.Roll, [new DieReplacement(1, new Die(2, 2, false), new Die(5, 5, true))]));
        var line = Lines(a, Samples.Unit)[1];
        Assert.Equal("roll-to-hit: hit 1 of 2 succeeded", line.Text);
        Assert.Equal([1, 5], line.Dice);
    }

    [Fact]
    public void An_undone_step_is_not_in_the_effective_log_and_so_not_in_the_round()
    {
        var a = Applier(new RoundStarted(1, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Enemy, [Samples.Unit], [], null)),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []),
            new Undone(2));
        Assert.Equal([RoundHistory.NotYetActivated], Lines(a, Samples.Unit).Select(l => l.Text));
    }
}
