using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class HeaderViewTests
{
    static readonly GameInfo Game = Samples.Game;
    static readonly Guid Nested = Guid.Parse("0198b7ac-4444-7000-8000-000000000004");
    static readonly OutlineStep[] AdvanceOutline = [new("move", "Move"), new("shoot", "Shoot"), new("end", "End")];

    static Projection After(params EventPayload[] history)
    {
        var a = new EventApplier(Samples.Board, Game);
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        return a.Projection;
    }

    static HeaderModel Of(Projection p) => HeaderView.Of(p, Game, new Names(Game, p));

    [Fact]
    public void Round_turn_and_every_team_s_score()
    {
        var h = Of(After(new RoundStarted(2, [Samples.Team1, Samples.Team2], []), new TurnStarted(Samples.Team1), new ScoreSet(Samples.Team1, 3)));
        Assert.Equal((2, "Team 1's turn"), (h.Round, h.Turn));
        Assert.Equal(new[] { ("Team 1", 3), ("Team 2", 0) }, h.Scores);
        Assert.Null(h.ActiveUnit); Assert.Null(h.Action); Assert.Empty(h.Outline); Assert.Null(h.Waiting); Assert.Null(h.Result);
    }

    [Fact]
    public void Before_the_first_round_the_phase_leads_the_turn()
    {
        var h = Of(After(new RoundStarted(0, [Samples.Team1, Samples.Team2], []), new PhaseStarted("deployment"), new TurnStarted(Samples.Team2)));
        Assert.Equal("deployment · Team 2's turn", h.Turn);
        Assert.Equal("deployment", Of(After(new RoundStarted(0, [Samples.Team1], []), new PhaseStarted("deployment"))).Turn);
    }

    [Fact]
    public void The_outline_ticks_completed_steps_and_a_nested_frame_lights_the_step_it_runs_under()
    {
        var p = After(new RoundStarted(1, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance"), AdvanceOutline),
            new StepCompleted(Samples.Frame, "move", null, [], []),
            new SequenceStarted(Nested, "shooting", new SequenceContext(Samples.Unit, [], [], null)));
        var h = Of(p);
        Assert.Equal(("Battle Brothers", "Advance"), (h.ActiveUnit, h.Action));
        Assert.Equal(new[] { ("Move", OutlineState.Done), ("Shoot", OutlineState.Current), ("End", OutlineState.Next) },
            h.Outline.Select(o => (o.Label, o.State)));
        Assert.Same(p.Stack[0], HeaderView.ActionFrame(p));
    }

    [Fact]
    public void A_step_outside_the_outline_moves_nothing_and_the_last_step_done_leaves_no_current()
    {
        var started = new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance"), AdvanceOutline);
        var internalOnly = Of(After(started, new StepCompleted(Samples.Frame, "bookkeeping", null, [], [])));
        Assert.Equal(new[] { OutlineState.Current, OutlineState.Next, OutlineState.Next }, internalOnly.Outline.Select(o => o.State));
        var allDone = Of(After(started, new StepCompleted(Samples.Frame, "end", null, [], [])));
        Assert.All(allDone.Outline, o => Assert.Equal(OutlineState.Done, o.State));
    }

    /// <summary>Review Focus 3: a log written before the outline existed shows the action and no strip.</summary>
    [Fact]
    public void No_outline_gives_no_strip()
    {
        var h = Of(After(new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")),
            new StepCompleted(Samples.Frame, "move", null, [], [])));
        Assert.Equal(("Battle Brothers", "Advance"), (h.ActiveUnit, h.Action));
        Assert.Empty(h.Outline);
    }

    [Fact]
    public void A_frame_with_no_action_is_not_the_action_frame()
    {
        var h = Of(After(new SequenceStarted(Samples.Frame, "roll-off", new SequenceContext(null, [], [], null))));
        Assert.Null(h.ActiveUnit); Assert.Null(h.Action); Assert.Empty(h.Outline);
    }

    [Fact]
    public void The_waiting_line_names_each_team_not_yet_answered_and_what_for()
    {
        var casualties = new Ask(Guid.Parse("0198b7ac-5555-7000-8000-000000000005"), "remove_casualties", "Assign 2 wound(s)", [Samples.Team2], new Allocation([Samples.Model], 2), false, false, null, null);
        var endRound = new Ask(Guid.Parse("0198b7ac-6666-7000-8000-000000000006"), AskQuestions.EndRound, "End the round", [Samples.Team1, Samples.Team2], new Confirmation(), false, false, null, null);
        Assert.Equal("Team 2: remove casualties", Of(After(new AskOpened(casualties))).Waiting);
        var both = After(new AskOpened(casualties), new AskOpened(endRound), new AskAnswered(endRound.Id, Samples.Team1, new Confirmed(), false));
        Assert.Equal("Team 2: remove casualties · Team 2: end round", Of(both).Waiting);
    }

    [Fact]
    public void The_waiting_line_says_you_for_my_team_and_puts_me_first()
    {
        var casualties = new Ask(Guid.Parse("0198b7ac-5555-7000-8000-000000000005"), "remove_casualties", "Assign 2 wound(s)", [Samples.Team1], new Allocation([Samples.Model], 2), false, false, null, null);
        var endRound = new Ask(Guid.Parse("0198b7ac-4444-7000-8000-000000000004"), AskQuestions.EndRound, "End the round", [Samples.Team2], new Confirmation(), false, false, null, null);
        var p = After(new AskOpened(endRound), new AskOpened(casualties));
        Assert.Equal("Your move: remove casualties · Team 2: end round", HeaderView.Waiting(p, new Names(Game, p), Samples.Team1));
        Assert.Equal("Team 2: end round · Team 1: remove casualties", HeaderView.Waiting(p, new Names(Game, p), (Guid?)null));
    }

    [Fact]
    public void A_watcher_is_told_they_watch() =>
        Assert.Equal("Watching: you have no team in this game", HeaderView.Watching);

    [Fact]
    public void Game_over_carries_the_result()
    {
        Assert.Equal("Game over · Team 1 wins", Of(After(new GameEnded("4 rounds have been played", [Samples.Team1]))).Result);
        Assert.Equal("Game over · Draw", Of(After(new GameEnded("4 rounds have been played", [Samples.Team1, Samples.Team2]))).Result);
        Assert.Equal("Game over · No result", Of(After(new GameEnded("abandoned", []))).Result);
    }

    [Fact]
    public void The_first_line_drops_the_separator_when_there_is_no_turn()
    {
        Assert.Equal("Round 2 · Team 1's turn", Of(After(new RoundStarted(2, [Samples.Team1, Samples.Team2], []), new TurnStarted(Samples.Team1))).TopLine);
        Assert.Equal("Round 4", Of(After(new RoundStarted(4, [Samples.Team1, Samples.Team2], []))).TopLine);
    }
}
