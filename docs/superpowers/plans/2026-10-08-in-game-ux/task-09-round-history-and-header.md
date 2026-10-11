# Task 9: Round history and header

**Wave:** 2 · **Depends on:** 1 · **Model:** sonnet

**Files:**
- Create: `client/src/Core/RoundHistory.cs`
- Create: `client/src/Core/HeaderView.cs`
- Test: `tests/Tabletop.Client.Tests/RoundHistoryTests.cs`, `tests/Tabletop.Client.Tests/HeaderViewTests.cs`

**Interfaces:**
- Consumes (Task 1): `SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context, IReadOnlyList<OutlineStep>? Outline = null)`,
  `OutlineStep(string Step, string Label)`, `Frame.Outline : IReadOnlyList<OutlineStep>` (empty when the event carried none).
  Existing: `EventApplier.EffectiveLog`, `Names`, `LogText.Describe`, `LogText.Answer`, `Projection.Ended`.
- Produces (README *Fixed interfaces → Client core*):

```csharp
public sealed record HistoryLine(string Text, IReadOnlyList<int> Dice, long Seq);
public static class RoundHistory { public static IReadOnlyList<HistoryLine> For(IReadOnlyList<GameEvent> effectiveLog, Projection p, Names n, Guid unitId); }
public sealed record OutlineItem(string Label, OutlineState State);   public enum OutlineState { Done, Current, Next }
public sealed record HeaderModel(int Round, string Turn, IReadOnlyList<(string Team, int Score)> Scores, string? ActiveUnit, string? Action,
    IReadOnlyList<OutlineItem> Outline, string? Waiting, string? Result);
public static class HeaderView { public static HeaderModel Of(Projection p, GameInfo game, Names n); }
```

Also public on `HeaderView`: `Frame? ActionFrame(Projection p)` (the stack frame whose `Context.Action` is set,
outermost first; Tasks 15 and 17), `IReadOnlyList<OutlineItem> Outline(Frame frame)`, and `string? Waiting(Projection p,
Names n)`, the one waiting line: the header's third line (Task 14) and the decision box's when nothing is mine (Task 16a).

Spec: *Header*, *Stat sheet → This round tab*, *Step outline*, *End of game*.

Wording settled here:

| Part | Text |
| --- | --- |
| Action line | `Activated: Advance`; a hand-recorded action with no frame reads the unit's `Actions`, `Seq` 0 |
| No action | `Not yet activated`, `Seq` 0, always the first line |
| Step line | `{outline label, else step name}: {answer}; {roll} {k} of {n} succeeded; {change}`, dice are the raw faces after rerolls and edits |
| Change | `LogText.Describe` of the change with a null actor, the leading `the game ` cut |
| Turn | the phase while round 0 (`deployment`), then `Team 1's turn` when a team is active, joined with ` · ` |
| Waiting | `Team 2: remove casualties` per unanswered team of each open Ask (question with `_` as spaces), joined with ` · `, ordered by Ask id |
| Result | `Game over · Team 1 wins`, `Game over · Draw` (several winners), `Game over · No result` (none) |

The outline marks every step up to the last one whose `StepCompleted` the action frame holds as `Done`, the one after it
`Current`, the rest `Next`. A nested frame (shooting inside Advance) completes nothing on the action frame, so the
step it runs under stays `Current`.

- [ ] **Step 1: Write the failing history tests**

Create `tests/Tabletop.Client.Tests/RoundHistoryTests.cs`:

```csharp
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
```

The events carry `Samples.P1` as actor; a change inside a step is described with a null actor, so its words never name
a participant.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter RoundHistoryTests`
Expected: compile errors: `RoundHistory` and `HistoryLine` do not exist.

- [ ] **Step 3: Write the history**

Create `client/src/Core/RoundHistory.cs`:

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One line of a unit's This round tab. Dice are the raw faces, after any reroll or edit; Seq is the event the line came from, 0 for a line no single event wrote.</summary>
public sealed record HistoryLine(string Text, IReadOnlyList<int> Dice, long Seq);

/// <summary>
/// A unit's round, from the effective log since the last round_started (in-game UX spec → Stat sheet, This round tab): first the
/// action it took or "Not yet activated", then one line per step_completed of every frame where it is the actor or a target, in log
/// order. A step with no answer, roll or change writes no line. Rules-free: it copies what the log says in LogText's words.
/// </summary>
public static class RoundHistory
{
    public const string NotYetActivated = "Not yet activated";

    public static IReadOnlyList<HistoryLine> For(IReadOnlyList<GameEvent> effectiveLog, Projection p, Names n, Guid unitId)
    {
        var start = 0;
        for (var i = effectiveLog.Count - 1; i >= 0; i--)
            if (effectiveLog[i].Payload is RoundStarted) { start = i + 1; break; }

        var frames = new Dictionary<Guid, SequenceStarted>();
        var steps = new List<StepLine>();
        var byRoll = new Dictionary<Guid, StepLine>();
        HistoryLine? action = null;
        for (var i = start; i < effectiveLog.Count; i++)
        {
            var e = effectiveLog[i];
            switch (e.Payload)
            {
                case SequenceStarted s:
                    frames[s.FrameId] = s;
                    if (action is null && s.Context.ActorUnitId == unitId && s.Context.Action is { } name)
                        action = new HistoryLine($"Activated: {name}", [], e.Seq);
                    break;
                case StepCompleted s when frames.TryGetValue(s.FrameId, out var frame) && Involves(frame.Context, unitId)
                                          && (s.Answer is not null || s.Rolls.Count > 0 || s.Changes.Count > 0):
                    var added = new StepLine(e.Seq, LabelOf(frame, s.Step), s.Answer, s.Rolls.ToList(), s.Changes);
                    steps.Add(added);
                    foreach (var r in s.Rolls) byRoll[r.Id] = added;
                    break;
                case DiceRerolled d when byRoll.TryGetValue(d.RollId, out var rerolled): rerolled.Replace(d.RollId, d.Replaced); break;
                case DiceEdited d when byRoll.TryGetValue(d.RollId, out var edited): edited.Replace(d.RollId, d.Replaced); break;
            }
        }

        var head = action
            ?? (p.Units.GetValueOrDefault(unitId)?.Actions is { Count: > 0 } recorded
                ? new HistoryLine($"Activated: {string.Join(", ", recorded.Order(StringComparer.Ordinal))}", [], 0)
                : new HistoryLine(NotYetActivated, [], 0));
        return [head, .. steps.Select(s => s.ToLine(n))];
    }

    static bool Involves(SequenceContext c, Guid unitId) => c.ActorUnitId == unitId || c.TargetUnitIds.Contains(unitId);

    /// <summary>The outline's label when the frame carried one for this step, else the step's own name.</summary>
    static string LabelOf(SequenceStarted frame, string step) => frame.Outline?.FirstOrDefault(o => o.Step == step)?.Label ?? step;

    sealed class StepLine(long seq, string label, AskAnswer? answer, List<RollRecord> rolls, IReadOnlyList<StateChange> changes)
    {
        public void Replace(Guid rollId, IReadOnlyList<DieReplacement> replaced)
        {
            var i = rolls.FindIndex(r => r.Id == rollId);
            var dice = rolls[i].Dice.ToList();
            foreach (var r in replaced)
                if (r.Index >= 0 && r.Index < dice.Count) dice[r.Index] = r.New;
            rolls[i] = rolls[i] with { Dice = dice };
        }

        public HistoryLine ToLine(Names n)
        {
            var parts = new List<string>();
            if (answer is not null) parts.Add(LogText.Answer(answer, n));
            parts.AddRange(rolls.Select(r => $"{r.Roll} {r.Dice.Count(d => d.Success)} of {r.Dice.Count} succeeded"));
            parts.AddRange(changes.Select(c => Change(c, seq, n)));
            return new HistoryLine($"{label}: {string.Join("; ", parts)}", rolls.SelectMany(r => r.Dice).Select(d => d.Raw).ToList(), seq);
        }
    }

    /// <summary>LogText's words for the change, without the actor: the engine applied it as part of the step.</summary>
    static string Change(StateChange c, long seq, Names n)
    {
        var text = LogText.Describe(new GameEvent(seq, null, c, null), n);
        var engine = n.Participant(null) + " ";
        return text.StartsWith(engine, StringComparison.Ordinal) ? text[engine.Length..] : text;
    }
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter RoundHistoryTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Write the failing header tests**

Create `tests/Tabletop.Client.Tests/HeaderViewTests.cs`:

```csharp
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
    public void Game_over_carries_the_result()
    {
        Assert.Equal("Game over · Team 1 wins", Of(After(new GameEnded("4 rounds have been played", [Samples.Team1]))).Result);
        Assert.Equal("Game over · Draw", Of(After(new GameEnded("4 rounds have been played", [Samples.Team1, Samples.Team2]))).Result);
        Assert.Equal("Game over · No result", Of(After(new GameEnded("abandoned", []))).Result);
    }
}
```

The Ask ids are fixed so the casualty Ask sorts before the end-round Ask.

- [ ] **Step 6: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter HeaderViewTests`
Expected: compile errors: `HeaderView`, `HeaderModel`, `OutlineState` do not exist.

- [ ] **Step 7: Write the header**

Create `client/src/Core/HeaderView.cs`:

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum OutlineState { Done, Current, Next }
public sealed record OutlineItem(string Label, OutlineState State);

/// <summary>
/// The header's three lines (in-game UX spec → Header): round, turn and scores; the active unit, its action and the step strip; the
/// waiting line. Result is set once the log has ended the game.
/// </summary>
public sealed record HeaderModel(int Round, string Turn, IReadOnlyList<(string Team, int Score)> Scores, string? ActiveUnit, string? Action,
    IReadOnlyList<OutlineItem> Outline, string? Waiting, string? Result);

public static class HeaderView
{
    public static HeaderModel Of(Projection p, GameInfo game, Names n)
    {
        var s = p.Scheduler;
        var turn = string.Join(" · ", new[] { s.Round == 0 ? s.Phase : null, s.ActiveTeamId is { } t ? $"{n.Team(t)}'s turn" : null }.OfType<string>());
        var scores = game.Teams.OrderBy(t => t.Ordinal).Select(t => (n.Team(t.Id), p.Scores.GetValueOrDefault(t.Id))).ToList();
        var frame = ActionFrame(p);
        return new HeaderModel(s.Round, turn, scores,
            frame?.Context.ActorUnitId is { } unit ? n.Unit(unit) : null, frame?.Context.Action,
            frame is null ? [] : Outline(frame), Waiting(p, n), Result(p.Ended, n));
    }

    /// <summary>The activation's top frame: the outermost one whose context names an action. Nested frames run under it.</summary>
    public static Frame? ActionFrame(Projection p) => p.Stack.FirstOrDefault(f => f.Context.Action is not null);

    /// <summary>
    /// Every outline step up to the last one the frame has completed is done, the next is current, the rest are next. A frame
    /// pushed by a step completes nothing on this one, so the step it runs under stays current. No outline, no strip.
    /// </summary>
    public static IReadOnlyList<OutlineItem> Outline(Frame frame)
    {
        var completed = frame.Steps.Select(x => x.Step).ToHashSet(StringComparer.Ordinal);
        var last = -1;
        for (var i = 0; i < frame.Outline.Count; i++)
            if (completed.Contains(frame.Outline[i].Step)) last = i;
        return frame.Outline.Select((o, i) => new OutlineItem(o.Label,
            i <= last ? OutlineState.Done : i == last + 1 ? OutlineState.Current : OutlineState.Next)).ToList();
    }

    /// <summary>Who the game waits on and for what: one entry per team that has not answered an open Ask, in Ask order.</summary>
    public static string? Waiting(Projection p, Names n)
    {
        var parts = p.OpenAsks.Values.OrderBy(a => a.Ask.Id)
            .SelectMany(a => a.Ask.Teams.Where(t => !a.Answers.ContainsKey(t)).Select(t => $"{n.Team(t)}: {a.Ask.Question.Replace('_', ' ')}"))
            .Distinct().ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    static string? Result(GameEnded? ended, Names n) => ended is null ? null : "Game over · " + (ended.Winners.Count switch
    {
        0 => "No result",
        1 => $"{n.Team(ended.Winners[0])} wins",
        _ => "Draw",
    });
}
```

- [ ] **Step 8: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter "HeaderViewTests|RoundHistoryTests"`
Expected: PASS, 14 tests.

- [ ] **Step 9: Check the client**

Run: `dotnet test tests/Tabletop.Client.Tests && dotnet build client/Tabletop.csproj && ./scripts/check-client`
Expected: every test passes, the build succeeds, `check-client` prints nothing.

- [ ] **Step 10: Commit**

```bash
git add client/src/Core/RoundHistory.cs client/src/Core/HeaderView.cs \
  tests/Tabletop.Client.Tests/RoundHistoryTests.cs tests/Tabletop.Client.Tests/HeaderViewTests.cs
git commit -m "Derive a unit's round and the header from the projection and the log" \
  -m "Co-Authored-By: Claude Sonnet <noreply@anthropic.com>"
```
