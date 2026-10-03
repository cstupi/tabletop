# Task 6: The run loop — ended, round-capped, stuck, or failed

**Files:**
- Create: `sim/Run/RunOutcome.cs`, `sim/Run/AskFingerprint.cs`, `sim/Run/HandPlacement.cs`, `sim/Run/RunLoop.cs`, `sim/Run/RunReport.cs`
- Modify: `sim/Scenarios/Expectation.cs` (its evaluation)
- Test: `tests/Tabletop.Sim.Tests/RunLoopTests.cs`

**Interfaces:**
- Consumes: `IGameDriver`, `SubmitResult`, `StepResult`, `DriverMode` (Task 4); `IAnswerSource`, `PolicyView`, `UnscriptedAskException`, `NoLegalAnswerException`, `AnsweredAsk` (Tasks 2–3); `FrameProjector`, `Recording`, `RecordingMeta`, `RecordedTeam` (Task 5); `Projection`, `OpenAsk`, `Ask`, `LegalSet`, `AskAnswer`, `AskQuestions`, `MoveModels`, `ModelRelocation`, `Location`, `OnTable`, `OnShelf`, `GameEvent`, `EventKinds`, `Command`, `WireJson` (Protocol — `Serialize<T>` on an enum, for the meta block's spelling only); `Placement`, `Point2`, `Point3`, `Yaw`, `Polygon`, `PlacedModel`, `ZoneStyles`, `Settling` (Geometry); `CompiledScenario`, `Scenario`, `Expectation` (Task 1).
- Produces: `enum StuckReason { NoAskNoEvents, AskReopenedUnchanged, SubmitRefused, NoLegalAnswer }`; `abstract record RunOutcome` with `Ended(int Round, IReadOnlyList<Guid> Winners)`, `RoundLimit(int Rounds)`, `Stuck(StuckReason Reason, string Detail)`, `Failed(string Detail)`, `Paused(Guid AskId, string Question)`, and `string Kind` (`ended`, `round_limit`, `stuck`, `failed`, `paused`); `sealed record AskFingerprint(string Question, string Teams, string Legal, bool AllowPass, string? WindowKind)` with `static AskFingerprint Of(Ask)`; `static class HandPlacement` with `IReadOnlyList<ModelRelocation> IntoZone(Projection, CompiledScenario, Guid teamId, Guid unitId, Chooser)` and `Chooser ChooserFor(ulong scenarioSeed)`; `sealed record RunSettings(int MaxSteps = 20_000, int FrameDumpOnFailure = 12, bool CheckInvariants = true, bool Record = true)`; `sealed record RunReport(string Scenario, ulong Seed, string Driver, RunOutcome Outcome, int Rounds, long LastSeq, int Frames, int AsksAnswered, double DurationMs, IReadOnlyList<string> Failures, string? RecordingPath)` with `bool Passed`; `sealed class RunLoop(IGameDriver driver, CompiledScenario scenario, IAnswerSource answers, Chooser chooser, RunSettings? settings = null)` with `Task<RunReport> RunAsync(CancellationToken ct)`, `FrameProjector Projector`, `Recording.Recording BuildRecording(RunReport)`, `IReadOnlyList<string> LastFrames(int n)`; `static class Expectations` with `IReadOnlyList<string> Check(IReadOnlyList<Expectation>, Projection, IReadOnlyList<GameEvent>, RunOutcome, CompiledScenario)`.

Spec: spec → *What a full game means* — the three-row table, "a run that reaches none of them is a bug the simulator exists to find"; "Stuck is the interesting one. A scheduler or interpreter that cannot make progress is a hang in production and a failed assertion here." Module prompt → "Detect stuck precisely: no open Ask and no events emitted, or an Ask answered that reopens unchanged. A stuck run is a failure with the last frames dumped."

**Termination, exactly.**

| End | Condition as this loop computes it |
| --- | --- |
| Ended | The game logged `game_ended`, with its winners. A wipe-out is not a win (p.6). |
| Round limit | `Projection.Scheduler.Round >= Scenario.RoundCap` and every open Ask is `AskQuestions.EndRound`. |
| Stuck: `NoAskNoEvents` | No Ask is open and no event of any kind was absorbed since the previous check, whether the step or an answer's drain returned it. |
| Stuck: `AskReopenedUnchanged` | The Ask answered at step *n* is closed and an Ask with the same `AskFingerprint` opens, with no event other than the three Ask events in between. **This check is defeatable and is not the only guard:** a single non-Ask event absorbed since the last answer clears the suspicion, so an engine that emits one cosmetic event per turn of the loop re-Asks forever without ever tripping it, and is caught only by `RunSettings.MaxSteps` (20 000), which is the backstop. |
| Stuck: `SubmitRefused` | An answer or a hand placement the driver refuses. Under `strict` that is a real bug in the policy or the engine, and the run stops rather than looping. |
| Stuck: `NoLegalAnswer` | `NoLegalAnswerException`: a legal set with no answer and no pass. |
| Failed | An `UnscriptedAskException`, a `RestTimeoutException` (the socket driver waited out `RestTimeout` for `AtRest`), a failed expectation, or a failed invariant. |

**A cap of *n* means *n* whole rounds.** The boundary condition is not a detail: `Round >= RoundCap` **and every open Ask is the round-boundary Ask** stops the run at the end of round *n*, where `Round > RoundCap` alone would let round *n* + 1 start and stop part-way through it. A cap of two therefore ends after the second round, not during the third. Two reasons for that reading. A cap is a maximum number of rounds, so exceeding it by a fraction of a round is a cap that does not hold; and stopping on the boundary Ask leaves the recording ending on a whole round, which is what a viewer's round scrubber and every `ReachedRound` expectation assume. The boundary Ask always arrives, so the loop cannot sail past the cap waiting for it: the scheduler cannot advance a round except through an answered `end_round` Ask, which is invariant 5, `TheRoundAdvancedOnlyThroughTheBoundaryAsk` (Task 7). This is the condition the design document records (`docs/design/simulation/design.md` → *The run loop and the four outcomes*); the table above and `RunLoop.DriveAsync` state it identically, and `A_cap_of_two_stops_at_the_end_of_the_second_round` pins it.

**Why the loop places models by hand.** Deployment's Ask is `deploy` over a `PieceSelection` of units — and it is the question name that says so, not the shape, which is what keeps a `PieceSelection` of model ids such as a Takedown's pick (p.14) out of this path; the scheduler spec's phase ends "when the last model of that unit has a placement", and moving models by hand is an ordinary `MoveModels` command, not an Ask (scheduler → The turn is an Ask: "moving models by hand never ends one"). So after a `deploy` answer the loop puts that unit's models on the table itself, inside the team's own deployment zone, using `ZoneStyles.Resolve` — geometry from a library, not a rules outcome the engine should have computed. Nothing checks whether the placement is *good*; placement is free under every enforcement setting (`architecture.md` → Rules enforcement: "free placement, with base overlap, terrain intersection, and broken coherency flagged rather than prevented").

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class RunLoopTests
{
    static async Task<RunReport> Run(string name, ulong seed = 1, Action<ScenarioBuilder>? tweak = null)
    {
        var scenario = ScenarioCatalog.Get(name).WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(seed))), HandPlacement.ChooserFor(seed));
        return await loop.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_skirmish_reaches_an_end_and_never_hangs()
    {
        var report = await Run("skirmish");
        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
        Assert.True(report.Rounds >= 1, $"only reached round {report.Rounds}");
        Assert.True(report.Frames > 20, $"only {report.Frames} frames");
        Assert.True(report.AsksAnswered > 10);
    }

    [Fact]
    public async Task Every_model_is_deployed_off_the_shelf_before_round_one()
    {
        var scenario = ScenarioCatalog.Get("skirmish");
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(AnswerScript.Empty, new AnswerPolicy(new Chooser(1))), HandPlacement.ChooserFor(1));
        await loop.RunAsync(CancellationToken.None);
        var placed = loop.Projector.Frames
            .First(f => f.Round >= 1 && f.Kind == EventKinds.Of(new RoundStarted(1, [], [])))
            .Models.Count(m => m.Where == "table");
        Assert.Equal(18, placed);
    }

    // Reproducibility is same-dice and same-answers, not same-ids: the seed is stored on the game and
    // the ids are ordinary v7 GUIDs, so nothing here may compare an id across two runs (Global
    // Constraints → Reproducibility). RunOutcome.Decided carries a team *id*, so the comparison is on
    // Kind plus the winner's ordinal.
    static async Task<(RunReport Report, int? WinnerOrdinal, IReadOnlyList<int> Dice)> RunTwice(string name, ulong seed)
    {
        var scenario = ScenarioCatalog.Get(name).WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(seed))), HandPlacement.ChooserFor(seed));
        var report = await loop.RunAsync(CancellationToken.None);
        var winner = report.Outcome is RunOutcome.Decided d
            ? compiled.Info.Teams.Single(t => t.Id == d.WinningTeamId).Ordinal
            : (int?)null;
        var dice = driver.Log
            .Select(e => e.Payload).OfType<StepCompleted>()
            .SelectMany(step => step.Rolls)
            .SelectMany(r => r.Dice.Select(x => x.Raw))
            .ToList();
        return (report, winner, dice);
    }

    [Fact]
    public async Task The_same_seed_produces_the_same_dice_the_same_answers_and_a_different_game_id()
    {
        var one = await RunTwice("skirmish", 12);
        var two = await RunTwice("skirmish", 12);
        Assert.Equal(one.Report.Outcome.Kind, two.Report.Outcome.Kind);
        Assert.Equal(one.WinnerOrdinal, two.WinnerOrdinal);
        Assert.Equal(one.Report.Rounds, two.Report.Rounds);
        Assert.Equal(one.Report.LastSeq, two.Report.LastSeq);
        Assert.Equal(one.Report.Frames, two.Report.Frames);
        Assert.Equal(one.Report.AsksAnswered, two.Report.AsksAnswered);
        Assert.NotEmpty(one.Dice);
        Assert.Equal(one.Dice, two.Dice);                                // the stored seed, doing its job
    }

    [Fact]
    public void The_scenario_seed_is_what_the_game_carries()
    {
        var compiled = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(12));
        Assert.Equal(12L, compiled.Info.RngSeed);
    }

    [Fact]
    public async Task Different_seeds_produce_different_runs()
    {
        var seeds = new[] { 1ul, 2ul, 3ul, 4ul, 5ul };
        var lengths = new List<long>();
        foreach (var seed in seeds) lengths.Add((await Run("skirmish", seed)).LastSeq);
        Assert.True(lengths.Distinct().Count() > 1, "five seeds produced identical logs");
    }

    [Fact]
    public async Task A_round_cap_terminates_a_stalemate()
    {
        var report = await Run("stalemate");
        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
        Assert.True(report.Rounds <= 3, $"the cap of 3 was exceeded: {report.Rounds}");
        if (report.Outcome is RunOutcome.RoundLimit limit) Assert.Equal(3, limit.Rounds);
    }

    // A cap of n means n whole rounds, so the loop stops on round n's boundary Ask and round n + 1
    // never starts. The two readings differ by exactly this frame, which is why it is asserted twice:
    // on the reported round and on the frames.
    [Fact]
    public async Task A_cap_of_two_stops_at_the_end_of_the_second_round()
    {
        var scenario = ScenarioCatalog.Get("stalemate").WithSeed(7) with { RoundCap = 2 };
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(7))), HandPlacement.ChooserFor(7));
        var report = await loop.RunAsync(CancellationToken.None);

        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
        Assert.DoesNotContain(loop.Projector.Frames, f => f.Round > 2);
        if (report.Outcome is RunOutcome.RoundLimit limit)
        {
            Assert.Equal(2, limit.Rounds);
            Assert.Equal(2, report.Rounds);
        }
    }

    [Fact]
    public async Task A_script_only_run_fails_on_the_first_unscripted_ask()
    {
        var scenario = new ScenarioBuilder("scripted", 1).Army("A", 1, 1).Army("B", 1, 1)
            .Scripted(AnswerScript.Empty, only: true).Build();
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, null), HandPlacement.ChooserFor(1));
        var report = await loop.RunAsync(CancellationToken.None);
        Assert.False(report.Passed);
        Assert.Equal("failed", report.Outcome.Kind);
        Assert.Contains("script-only", Assert.IsType<RunOutcome.Failed>(report.Outcome).Detail);
    }

    // The meta block is the viewer's input, so its enums carry the Protocol's spelling and not C#'s.
    [Fact]
    public async Task The_meta_block_spells_its_enums_the_way_the_wire_does()
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(3) with { RoundCap = 1 };
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, CancellationToken.None);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(3))), HandPlacement.ChooserFor(3));
        var report = await loop.RunAsync(CancellationToken.None);

        var meta = loop.BuildRecording(report).Meta;
        Assert.Equal("alternating-activation", meta.SchedulerShape);
        Assert.Equal("strict", meta.Enforcement);
        Assert.Equal(1, meta.RoundCap);
        Assert.Contains(meta.Outcome, (string[])["decided", "round_limit"]);
    }

    [Fact]
    public void An_ask_fingerprint_ignores_the_id_and_notices_the_legal_set()
    {
        var team = Guid.CreateVersion7();
        Ask ask(Guid id, int options) => new(id, AskQuestions.Activate, "p", [team],
            new Options(Enumerable.Range(0, options).Select(i => new AskOption($"o{i}", "", [])).ToList()), true, false, null, null);
        Assert.Equal(AskFingerprint.Of(ask(Guid.CreateVersion7(), 2)), AskFingerprint.Of(ask(Guid.CreateVersion7(), 2)));
        Assert.NotEqual(AskFingerprint.Of(ask(Guid.CreateVersion7(), 2)), AskFingerprint.Of(ask(Guid.CreateVersion7(), 3)));
    }

    [Fact]
    public void Hand_placement_puts_every_model_of_the_unit_inside_the_teams_zone()
    {
        var compiled = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));
        var projection = Projection.Initial(compiled.Board, compiled.Info);
        var team = compiled.TeamIds[0];
        var unit = compiled.Board.Armies[0].Snapshot.Units[0];
        var moves = HandPlacement.IntoZone(projection, compiled, team, unit.Id, new Chooser(5));
        Assert.Equal(unit.Models.Count, moves.Count);
        var shape = compiled.Board.Table.Zones.Single(z => z.TeamOrdinal == 1).Shape;
        foreach (var move in moves)
        {
            var to = Assert.IsType<OnTable>(move.To);
            Assert.IsType<OnShelf>(move.From);
            Assert.True(ZoneStyles.Contains(shape, compiled.Board.Table.Size, to.Placement.Position.Horizontal),
                $"{to.Placement.Position} is outside {shape}");
        }
    }
}
```
- [ ] **Step 2: Run; expect failure, then write `RunOutcome.cs` and `AskFingerprint.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter RunLoopTests`
Expected: `CS0246: ... 'RunLoop' could not be found`.

```csharp
using Tabletop.Protocol;

namespace Tabletop.Sim.Run;

/// <summary>Why the engine stopped making progress. Every one of these is a bug the simulator exists to find.</summary>
public enum StuckReason { NoAskNoEvents, AskReopenedUnchanged, SubmitRefused, NoLegalAnswer }

/// <summary>
/// Five ends, as spec → What a full game means tabulates. Decided, RoundLimit and Stuck are the game
/// ending; Failed is an assertion breach — an unscripted Ask in a script-only run, a failed expectation or a
/// failed invariant — and Paused is the step-and-wait seam of Task 9.
/// </summary>
public abstract record RunOutcome
{
    public sealed record Decided(Guid WinningTeamId) : RunOutcome;
    public sealed record RoundLimit(int Rounds) : RunOutcome;
    public sealed record Stuck(StuckReason Reason, string Detail) : RunOutcome;
    public sealed record Failed(string Detail) : RunOutcome;
    /// <summary>StepAndWait: the loop stopped with this Ask open and answered nothing (Task 9).</summary>
    public sealed record Paused(Guid AskId, string Question) : RunOutcome;

    public string Kind => this switch
    {
        Decided => "decided",
        RoundLimit => "round_limit",
        Stuck => "stuck",
        Paused => "paused",
        _ => "failed",
    };
}
```

```csharp
using Tabletop.Protocol;

namespace Tabletop.Sim.Run;

/// <summary>
/// What "the same Ask again" means. The id is server-minted and always different, so it is excluded; the
/// legal set is included, because an Ask that reopens with a *smaller* set is progress and not a hang.
/// </summary>
public sealed record AskFingerprint(string Question, string Teams, string Legal, bool AllowPass, string? WindowKind)
{
    public static AskFingerprint Of(Ask ask) => new(
        ask.Question,
        string.Join(",", ask.Teams.Select(t => t.ToString("N")).Order()),
        Describe(ask.Legal),
        ask.AllowPass,
        ask.WindowKind);

    private static string Describe(LegalSet legal) => legal switch
    {
        Options o => $"options:{string.Join(",", o.Choices.Select(c => c.Id).Order())}",
        PieceSelection p => $"pieces:{p.Min}-{p.Max}:{string.Join(",", p.Eligible.Select(i => i.ToString("N")).Order())}",
        Allocation a => $"allocation:{a.Amount}:{string.Join(",", a.Targets.Select(i => i.ToString("N")).Order())}",
        Assignment a => $"assignment:{a.MaxDistinctTargets}:{string.Join(",", a.Items.Select(i => i.Id).Order())}:{string.Join(",", a.Targets.Select(i => i.ToString("N")).Order())}",
        MoveRegion m => $"move:{m.Allowance}:{string.Join(",", m.ModelIds.Select(i => i.ToString("N")).Order())}",
        TokenSpend t => $"token_spend:{string.Join(",", t.MaxByModel.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value}"))}",
        Confirmation => "confirmation",
        _ => legal.GetType().Name,
    };
}
```

- [ ] **Step 3: Write `HandPlacement.cs`**

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Run;

/// <summary>
/// Deployment's Ask names a unit; putting its models on the table is an ordinary MoveModels command, not an
/// Ask (scheduler → The turn is an Ask: "moving models by hand never ends one"). This is where the simulator
/// does that. It computes no rules outcome: the zone comes from the deployment_zone rows and
/// Tabletop.Geometry resolves the shape, and nothing here judges whether a placement is good —
/// placement is free under every enforcement setting (architecture.md → Rules enforcement).
/// </summary>
public static class HandPlacement
{
    private const int Attempts = 40;
    private const int MaxColumns = 5;

    /// <summary>The chooser a run places its units with, derived from the scenario's seed: Program and every test that
    /// reproduces a console run use this, so one seed names one game through every entry point.</summary>
    public static Chooser ChooserFor(ulong scenarioSeed) => new(scenarioSeed ^ 0x5EED);

    public static IReadOnlyList<ModelRelocation> IntoZone(
        Projection projection, CompiledScenario scenario, Guid teamId, Guid unitId, Chooser chooser)
    {
        var unit = projection.Units[unitId];
        var table = scenario.Board.Table.Size;
        var ordinal = scenario.Info.Teams.Single(t => t.Id == teamId).Ordinal;
        var shape = scenario.Board.Table.Zones.FirstOrDefault(z => z.TeamOrdinal == ordinal)?.Shape;

        var shelved = unit.Definition.Models.Where(m => projection.Models[m.Id].Location is OnShelf).ToList();
        if (shelved.Count == 0) return [];

        // One grid for the whole unit: the column count and spacing come from the unit, not from a model's index,
        // so no two cells coincide. The spacing is the widest base plus half an inch.
        var cols = Math.Min(shelved.Count, MaxColumns);
        var widest = shelved.Max(m => Math.Max(m.Base.A, m.Base.B));
        var spacing = widest + 0.5;

        var offsets = shelved.Select((_, i) => new Point2(i % cols * spacing, i / cols * spacing)).ToList();
        var anchor = ChooseAnchor(shelved, offsets, shape, table, chooser);

        return shelved.Select((slot, i) => new ModelRelocation(slot.Id, new OnShelf(),
            new OnTable(new Placement(new Point3(anchor.X + offsets[i].X, anchor.Y + offsets[i].Y, 0), new Yaw(0))))).ToList();
    }

    /// <summary>A random anchor at which every base of the grid is fully inside the zone; the zone-centre-ish fallback otherwise.</summary>
    private static Point2 ChooseAnchor(
        IReadOnlyList<ModelSlot> models, IReadOnlyList<Point2> offsets, string? shape, TableSize table, Chooser chooser)
    {
        // Candidates are drawn from the zone's bounding box, so a thin zone is not missed 39 times in 40.
        var vertices = shape is null ? [] : ZoneStyles.Resolve(shape, table).SelectMany(p => p.Vertices).ToList();
        var (minX, maxX) = vertices.Count == 0 ? (0, table.Width) : (vertices.Min(v => v.X), vertices.Max(v => v.X));
        var (minY, maxY) = vertices.Count == 0 ? (0, table.Length) : (vertices.Min(v => v.Y), vertices.Max(v => v.Y));
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var candidate = new Point2(minX + chooser.Double() * (maxX - minX), minY + chooser.Double() * (maxY - minY));
            var placed = models.Select((m, i) => Placed(m, new Point2(candidate.X + offsets[i].X, candidate.Y + offsets[i].Y))).ToList();
            if (!placed.All(pm => Geometry.Table.ContainsBase(table, pm))) continue;
            if (shape is null || placed.All(pm => ZoneStyles.BaseFullyWithin(shape, table, pm))) return candidate;
        }
        return new Point2((minX + maxX) / 2, (minY + maxY) / 2);
    }

    private static PlacedModel Placed(ModelSlot slot, Point2 at) =>
        new(new ModelBody(slot.Base, slot.Height), new Placement(new Point3(at.X, at.Y, 0), new Yaw(0)));
}
```

The `Z` is `0` and the server settles it: the interpreter plan's move step calls `Settling.SurfaceHeightAt`, and hand moves are "copied as sent, including the client's `Z`" (game server skeleton plan → Interfaces needed 3) — so a model deployed onto a hill sits at `0` until it next moves, which is a cosmetic wrong that the simulator does not exist to fix. Named here so nobody mistakes it for a bug in geometry.

A unit's models are laid on one fixed-column grid, at most five columns wide and spaced by the widest base plus half an inch, so no two cells coincide. The anchor is drawn from the zone's bounding box until every base of the grid is fully inside the zone, with the zone's centre as the fallback. `HandPlacement.ChooserFor(scenarioSeed)` derives the placement chooser from the scenario's seed, and `Program` and every test that reproduces a console run use it.

- [ ] **Step 4: Write `RunReport.cs` and the expectation evaluation**

```csharp
namespace Tabletop.Sim.Run;

/// <param name="MaxSteps">
/// A hard stop, so a bug in this loop cannot spin forever — and the only guard against an engine that makes
/// no real progress while emitting one non-Ask event per pass, which clears the AskReopenedUnchanged check.
/// </param>
/// <param name="Record">
/// False builds no frames: a loop attached to a game already past its first event (the redo after an Undo) has
/// no projector state for the events that refer back to the kept prefix. Pair it with CheckInvariants: false, since
/// the invariants replay the loop's own log from its first event.
/// </param>
public sealed record RunSettings(int MaxSteps = 20_000, int FrameDumpOnFailure = 12, bool CheckInvariants = true, bool Record = true);

/// <param name="RecordingPath">
/// Where the recording was written, or null. <b><c>RunLoop</c> never sets this and always leaves it null.</b>
/// The loop builds a <c>Recording</c> on demand through <c>BuildRecording</c> but writes no file and knows no
/// output directory; whoever writes the file owns the path, which is <c>Program.PlayAsync</c> (Task 10)
/// returning <c>report with { RecordingPath = path }</c> after <c>RecordingJson.WriteAsync</c>. A caller that
/// drives <c>RunLoop</c> itself — a full-game test, say — gets null here and that is correct, because nothing
/// was written.
/// </param>
public sealed record RunReport(
    string Scenario, ulong Seed, string Driver, RunOutcome Outcome,
    int Rounds, long LastSeq, int Frames, int AsksAnswered, double DurationMs,
    IReadOnlyList<string> Failures, string? RecordingPath)
{
    public bool Passed => Failures.Count == 0 && Outcome is RunOutcome.Decided or RunOutcome.RoundLimit or RunOutcome.Paused;
}
```

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Run;

namespace Tabletop.Sim.Scenarios;

/// <summary>A scenario's own assertions, checked at the end of a run beside the universal invariants.</summary>
public static class Expectations
{
    public static IReadOnlyList<string> Check(
        IReadOnlyList<Expectation> expectations, Projection projection,
        IReadOnlyList<GameEvent> log, RunOutcome outcome, CompiledScenario scenario)
    {
        var failures = new List<string>();
        foreach (var expectation in expectations)
        {
            switch (expectation)
            {
                case Expectation.EventOfKind(var kind, var atLeast):
                    var count = log.Count(e => EventKinds.Of(e.Payload) == kind);
                    if (count < atLeast) failures.Add($"expected at least {atLeast} '{kind}' events, saw {count}");
                    break;
                case Expectation.NoEventOfKind(var kind) when log.Any(e => EventKinds.Of(e.Payload) == kind):
                    failures.Add($"expected no '{kind}' event, saw one at seq {log.First(e => EventKinds.Of(e.Payload) == kind).Seq}");
                    break;
                case Expectation.ReachedRound(var round) when projection.Scheduler.Round < round:
                    failures.Add($"expected round {round}, reached {projection.Scheduler.Round}");
                    break;
                case Expectation.TeamWiped(var ordinal) when ModelsOnTable(projection, scenario, ordinal) > 0:
                    failures.Add($"expected team {ordinal} wiped, it has {ModelsOnTable(projection, scenario, ordinal)} models in play");
                    break;
                case Expectation.ModelsRemaining(var ordinal, var atLeast) when ModelsOnTable(projection, scenario, ordinal) < atLeast:
                    failures.Add($"expected team {ordinal} to keep {atLeast} models, it has {ModelsOnTable(projection, scenario, ordinal)}");
                    break;
                case Expectation.AskAsked(var question, var atLeast):
                    var asked = log.Count(e => e.Payload is AskOpened o && o.Ask.Question == question);
                    if (asked < atLeast) failures.Add($"expected at least {atLeast} '{question}' asks, saw {asked}");
                    break;
                case Expectation.Outcome(var kind) when outcome.Kind != kind:
                    failures.Add($"expected outcome '{kind}', got '{outcome.Kind}': {outcome}");
                    break;
            }
        }
        return failures;
    }

    /// <summary>Models in play: <c>Location is not OnShelf</c>, the engine's own test (Scheduler/TeamUnits.cs).
    /// Casualties and set-aside units are on the shelf; a model inside a building or transport still counts.</summary>
    public static int ModelsOnTable(Projection projection, CompiledScenario scenario, int teamOrdinal) =>
        ModelsOnTable(projection, scenario, scenario.Info.Teams.Single(t => t.Ordinal == teamOrdinal).Id);

    public static int ModelsOnTable(Projection projection, CompiledScenario scenario, Guid teamId) =>
        projection.Models.Values.Count(m =>
            m.Location is not OnShelf && scenario.TeamOfParticipant.GetValueOrDefault(projection.Units[m.UnitId].ParticipantId) == teamId);
}
```

- [ ] **Step 5: Write `RunLoop.cs`**

```csharp
using System.Diagnostics;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Run;

/// <summary>
/// Drive a game to one of the three ends spec → What a full game means lists, and fail loudly on the two
/// further things that can happen: no progress at all, and an assertion that does not hold. Paused is the
/// fifth and is not an end. Every answer comes from the answer source; nothing here reads a rule.
/// </summary>
public sealed class RunLoop(
    IGameDriver driver, CompiledScenario scenario, IAnswerSource answers, Chooser chooser, RunSettings? settings = null)
{
    private static readonly string[] DeployQuestions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher];

    private readonly RunSettings _settings = settings ?? new RunSettings();
    private readonly List<GameEvent> _log = [];
    private AskFingerprint? _lastAnswered;
    private int _sinceLastAnswer;       // non-Ask events absorbed since the last accepted answer, wherever they were drained
    private int _absorbedThisCycle;     // every event absorbed since the last step's check

    public FrameProjector Projector { get; } = new(scenario);

    public async Task<RunReport> RunAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var failures = new List<string>();
        RunOutcome outcome;
        try
        {
            outcome = await DriveAsync(ct);
        }
        catch (UnscriptedAskException ex) { outcome = new RunOutcome.Failed(ex.Message); }
        // A hung server or a dead socket, not a stuck game: the engine may have been making progress.
        catch (RestTimeoutException ex) { outcome = new RunOutcome.Failed(ex.Message); }
        catch (NoLegalAnswerException ex) { outcome = new RunOutcome.Stuck(StuckReason.NoLegalAnswer, ex.Message); }

        var projection = await driver.ProjectionAsync(ct);
        failures.AddRange(Expectations.Check(scenario.Scenario.Expectations, projection, _log, outcome, scenario));
        if (outcome is RunOutcome.Stuck or RunOutcome.Failed)
            failures.Add($"{outcome.Kind}: {outcome}\nlast frames:\n{string.Join("\n", LastFrames(_settings.FrameDumpOnFailure))}");

        // RecordingPath stays null: this loop writes no file. Program.PlayAsync fills it in after
        // RecordingJson.WriteAsync, and it is the only thing that does — see RunReport's own comment.
        return new RunReport(scenario.Scenario.Name, scenario.Scenario.Seed, driver.Name, outcome,
            projection.Scheduler.Round, projection.LastSeq, Projector.Frames.Count,
            answers.History.Count, clock.Elapsed.TotalMilliseconds, failures, RecordingPath: null);
    }

    private async Task<RunOutcome> DriveAsync(CancellationToken ct)
    {
        Absorb(await driver.DrainEventsAsync(ct));
        for (var step = 0; step < _settings.MaxSteps; step++)
        {
            var result = await driver.StepAsync(ct);
            Absorb(result.Events);
            var produced = _absorbedThisCycle;
            _absorbedThisCycle = 0;

            var projection = await driver.ProjectionAsync(ct);
            if (Decided(projection) is { } winner) return new RunOutcome.Decided(winner);

            var open = result.OpenAsks.Where(a => a.Ask.Teams.Any(t => !a.Answers.ContainsKey(t))).ToList();
            if (open.Count == 0)
            {
                // The events an answer produced were drained when it was submitted, so the step itself may
                // return none; "nothing emitted" counts every event absorbed since the previous check.
                if (produced == 0)
                    return new RunOutcome.Stuck(StuckReason.NoAskNoEvents, $"no ask is open and nothing was emitted at seq {projection.LastSeq}");
                continue;
            }

            // n whole rounds, ending on round n's boundary Ask rather than part-way through round n + 1.
            // The boundary Ask always comes: the scheduler cannot advance a round without one (invariant 5).
            if (projection.Scheduler.Round >= scenario.Scenario.RoundCap &&
                open.All(a => a.Ask.Question == AskQuestions.EndRound))
                return new RunOutcome.RoundLimit(projection.Scheduler.Round);

            if (driver.Mode == DriverMode.StepAndWait) return new RunOutcome.Paused(open[0].Ask.Id, open[0].Ask.Question);

            if (Reopened(open[0].Ask) is { } stuck) return stuck;

            if (await AnswerAsync(open[0], projection, ct) is { } refusal) return refusal;
        }
        return new RunOutcome.Stuck(StuckReason.NoAskNoEvents, $"the loop ran {_settings.MaxSteps} steps without an end");
    }

    /// <summary>An unsealed Ask is answered once, by the first listed team that has not answered; a sealed Ask by every addressed team.</summary>
    private async Task<RunOutcome?> AnswerAsync(OpenAsk open, Projection projection, CancellationToken ct)
    {
        // The whole state: an answer may read anything the engine exposes. AnswerPolicy, the
        // default, reads only the geometry; a named state-reading IAnswerSource would read the rest.
        var view = new PolicyView(projection, driver.Board, volumes: null, driver.Info, scenario.Properties);
        foreach (var teamId in open.Ask.Teams.Where(t => !open.Answers.ContainsKey(t)))
        {
            var answer = answers.Answer(open.Ask, view);
            var result = await driver.AnswerAsync(teamId, open.Ask.Id, answer, ct);
            if (result is SubmitResult.Rejected rejected)
                return new RunOutcome.Stuck(StuckReason.SubmitRefused,
                    $"'{open.Ask.Question}' answered with {answer.GetType().Name}: {rejected.Reason} {rejected.Detail}");
            _lastAnswered = AskFingerprint.Of(open.Ask);
            _sinceLastAnswer = 0;                   // reset before the drain: Absorb counts what the answer produced
            Absorb(await driver.DrainEventsAsync(ct));

            if (DeployQuestions.Contains(open.Ask.Question) && answer is SelectedPieces selected)
                foreach (var unitId in selected.PieceIds)
                    if (await PlaceAsync(teamId, unitId, ct) is { } refusal) return refusal;

            if (open.Ask.Sealed) continue;      // a sealed Ask wants every addressed team's answer before it closes
            break;
        }
        return null;
    }

    private async Task<RunOutcome?> PlaceAsync(Guid teamId, Guid unitId, CancellationToken ct)
    {
        var projection = await driver.ProjectionAsync(ct);
        if (!projection.Units.ContainsKey(unitId)) return null;
        var moves = HandPlacement.IntoZone(projection, scenario, teamId, unitId, chooser);
        if (moves.Count == 0) return null;
        var result = await driver.SubmitAsync(driver.ParticipantFor(teamId), new MoveModels(moves), ct);
        Absorb(await driver.DrainEventsAsync(ct));
        return result is SubmitResult.Rejected rejected
            ? new RunOutcome.Stuck(StuckReason.SubmitRefused, $"hand placement of unit {unitId}: {rejected.Reason} {rejected.Detail}")
            : null;
    }

    /// <summary>
    /// An Ask answered and reopening unchanged, with nothing but the three Ask events between. One non-Ask
    /// event since the last answer clears the suspicion, so this catches a tight re-Ask loop and nothing
    /// slower: an engine emitting one cosmetic event per pass re-Asks forever and is caught by
    /// RunSettings.MaxSteps instead, which is the backstop and not a formality.
    /// </summary>
    private RunOutcome? Reopened(Ask ask)
    {
        if (_lastAnswered is null || _sinceLastAnswer > 0) return null;
        return AskFingerprint.Of(ask) == _lastAnswered
            ? new RunOutcome.Stuck(StuckReason.AskReopenedUnchanged, $"'{ask.Question}' reopened unchanged after being answered")
            : null;
    }

    /// <summary>One team with models in play and at least one other with none (spec → What a full game means).</summary>
    private Guid? Decided(Projection projection)
    {
        if (projection.Scheduler.Round < 1) return null;                       // deployment: the shelf is not a wipe
        var standing = scenario.TeamIds.Where(t => Expectations.ModelsOnTable(projection, scenario, t) > 0).ToList();
        return standing.Count == 1 && scenario.TeamIds.Count > 1 ? standing[0] : null;
    }

    private void Absorb(IReadOnlyList<GameEvent> events)
    {
        foreach (var e in events)
        {
            _log.Add(e);
            if (_settings.Record) Projector.Add(e);
            _absorbedThisCycle++;
            if (e.Payload is not (AskOpened or AskAnswered or AskClosed)) _sinceLastAnswer++;
        }
    }

    public IReadOnlyList<string> LastFrames(int n) =>
        Projector.Frames.TakeLast(n).Select(f => $"  [{f.Index}] seq {f.Seq} {f.Kind}: {f.Line}").ToList();

    public Recording.Recording BuildRecording(RunReport report) => Projector.Build(new RecordingMeta(
        report.Scenario, report.Seed, report.Driver, scenario.Info.GameRules, scenario.Info.PropertiesHash,
        Wire(scenario.Info.SchedulerShape), Wire(scenario.Info.Enforcement),
        scenario.Scenario.RoundCap,
        scenario.Info.Teams.Select(t => new RecordedTeam(t.Id, t.Ordinal,
            scenario.Scenario.Armies.ElementAtOrDefault(t.Ordinal - 1)?.Name ?? $"Team {t.Ordinal}",
            scenario.Info.Participants.Where(p => p.TeamId == t.Id).Select(p => p.Name).ToList())).ToList(),
        report.Outcome.Kind,
        report.Outcome switch { RunOutcome.Stuck s => s.Detail, RunOutcome.Failed f => f.Detail, _ => null },
        report.Outcome is RunOutcome.Decided d ? d.WinningTeamId : null,
        report.Rounds, report.LastSeq, DateTimeOffset.UtcNow, report.DurationMs));

    /// <summary>The Protocol's own spelling of an enum, so the meta block carries one convention and not two.</summary>
    private static string Wire<T>(T value) where T : struct, Enum => WireJson.Serialize(value).Trim('"');
}
```

**Every enum in the recording is written in the Protocol's spelling.** `SchedulerShape.ToString()` gives `AlternatingActivation`, and a viewer reading the meta block must see `alternating-activation`, so `RunLoop` takes the spelling from `WireJson` itself rather than inventing one — the same `Wire<T>` helper `SocketRowSeeder` uses in Task 8, and the reason `WireJson.Serialize<T>(T)` accepting an enum is on the *Interfaces needed* list. `Enforcement` goes through the same helper: `Silent`/`Warn`/`Strict` happen to spell the same under `.ToLowerInvariant()` as under `WireJson`'s `JsonNamingPolicy.SnakeCaseLower`, so this is not a bug being fixed but a coincidence being removed, and a multi-word member added to `Enforcement` later cannot start the two spellings drifting apart again. `RunLoop.cs` therefore needs `using Tabletop.Protocol;`, which it already has.

The rest of the recording's enums were checked against the contracts plan and are correct as written, because every one of them is a single word and `SnakeCaseLower` leaves a single word alone: `MarkerKind { Objective, Token }` in `MarkerState` and `BuildBoard`, `BaseShape { Circle, Oval, Square }` in `BuildBoard`, and `VolumeKind { Solid, Area }` with `FootprintKind { Circle, Rect }` in `VolumeOf` (Task 5 Step 5). `TerrainDeclarations` is a `[Flags]` enum with multi-word members and is already spelled by hand into `blocks_line_of_sight` and the rest, matching `SnakeCaseLower`. `RecordingMeta.Outcome` is `RunOutcome.Kind`, a string this plan owns, not an enum. `SchedulerShape` was the only member whose two spellings differ, and it was the one that was wrong.

- [ ] **Step 6: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter RunLoopTests
git add sim/Run sim/Scenarios tests/Tabletop.Sim.Tests
git commit -m "Drive a game to decided, capped or stuck, and dump the last frames on a failure"
```
