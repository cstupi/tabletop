# Task 5: The recording — the schema, the writer, and one frame per event

**Files:**
- Create: `sim/Recording/Recording.cs`, `sim/Recording/RecordingJson.cs`, `sim/Recording/LogLines.cs`, `sim/Recording/FrameProjector.cs`
- Test: `tests/Tabletop.Sim.Tests/RecordingTests.cs`

**Interfaces:**
- Consumes: `Projection`, `ModelInPlay`, `UnitInPlay`, `SchedulerPosition`, `OpenAsk`, `Ask`, `LegalSet`, `GameEvent`, `EventPayload` and every member, `EventKinds`, `Grant`, `RollRecord`, `Die`, `Location` (`OnTable`, `OnShelf`, `Inside`), `Board`, `GameInfo`, `EventApplier`, `AskAnswer` members (Protocol); `TableState`, `Marker`, `MarkerKind`, `TerrainPlacement`, `TerrainVolume`, `DeploymentZone`, `VisualRef` (Content); `Volume`, `VolumeKind`, `Footprint`, `FootprintKind`, `TerrainDeclarations`, `Base`, `BaseShape`, `Placement`, `Point3`, `Yaw`, `Polygon`, `ZoneStyles` (Geometry); `CompiledScenario` (Task 1).
- Produces: the record schema below (`Recording`, `RecordingMeta`, `RecordedTeam`, `RecordedBoard`, `RecordedTerrain`, `RecordedVolume`, `RecordedMarker`, `RecordedZone`, `RecordedPoint`, `RecordedModel`, `RecordedFrame`, `RecordedModelState`, `RecordedUnitState`, `RecordedMarkerState`, `RecordedAsk`, `RecordedAskOption`, `RecordedRoll`, `RecordedModifier`); `static class RecordingJson` with `const int SchemaVersion = 1`, `JsonSerializerOptions Options`, `Task WriteAsync(Recording, string path, CancellationToken)`, `Task<Recording> ReadAsync(string path, CancellationToken)`, `string Serialize(Recording)`, `Recording Deserialize(string)`; `static class LogLines` with `string Of(GameEvent, Projection after, IReadOnlyDictionary<Guid,int> teamOrdinals)`; `sealed class FrameProjector(CompiledScenario scenario)` with `RecordedBoard Board`, `RecordedFrame Add(GameEvent)`, `IReadOnlyList<RecordedFrame> Frames`, `Projection Projection`, `Recording Build(RecordingMeta meta)`.

Spec: spec → *The recording is the artifact* — "A run writes one file: the board, then one frame per event, each frame carrying the model positions, wounds, conditions and the log line that event produced. It is a **recording of a projection, not a log of events**"; "Given frames, the viewer interpolates nothing and decides nothing; it draws what it is handed"; "The recording is derived and disposable. It is not a save format, it is not replayed into the engine, and the event log remains the only thing undo and hydration read." Spec → *Interactive debugging is a door* — "the recording carries the open Ask at every frame". Spec → *Still open*: "Whether a recording should carry the dice so a viewer can show a roll, or only its result" — settled: it carries the dice, in `RecordedRoll` (every die's raw and modified face, whether it succeeded, the threshold and the modifiers).

**Every frame carries whole state, not a delta.** Nothing in a viewer then has to accumulate, which is the point of handing it frames at all; and a scrub to frame 300 needs frame 300 only. A 500-frame game of 40 models is roughly 1.5 MB of JSON, which is a file a browser opens.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class RecordingTests
{
    static CompiledScenario Scenario() => ScenarioCompiler.Compile(ScenarioCatalog.Get("terrain"));

    [Fact]
    public void The_board_carries_the_table_the_terrain_the_zones_and_a_definition_per_model()
    {
        var projector = new FrameProjector(Scenario());
        var board = projector.Board;
        Assert.Equal(72, board.Width);
        Assert.Equal(48, board.Length);
        Assert.Equal(2, board.Terrain.Count);
        Assert.Contains("blocks_line_of_sight", board.Terrain[0].Volumes[0].Declarations);
        Assert.Equal(18, board.Models.Count);
        Assert.Equal(2, board.Models.Select(m => m.TeamId).Distinct().Count());
        Assert.All(board.Models, m => Assert.True(m.BaseA > 0));

        // The zones carry resolved rings, not just a shape id: resolving `frontline/1` is C# and the
        // twelve-inch inset is absolute, so a browser cannot recover it from the id and the table size.
        Assert.Equal(2, board.Zones.Count);
        Assert.Equal("frontline/1", board.Zones[0].Shape);
        Assert.Equal(1, board.Zones[0].TeamOrdinal);
        var ring = Assert.Single(board.Zones[0].Polygons);
        Assert.Equal(4, ring.Count);
        Assert.All(ring, p => Assert.InRange(p.X, 0, board.Width));
        Assert.All(ring, p => Assert.InRange(p.Y, 0, board.Length));
        Assert.Equal(12, ring.Max(p => p.Y) - ring.Min(p => p.Y));
    }

    [Fact]
    public void A_frame_carries_every_model_the_open_ask_and_the_log_line()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        var unit = c.Board.Armies[0].Snapshot.Units[0];
        var moved = new ModelsMoved([new ModelRelocation(unit.Models[0].Id, new OnShelf(),
            new OnTable(new Tabletop.Geometry.Placement(new Tabletop.Geometry.Point3(6, 6, 0), new Tabletop.Geometry.Yaw(0))))]);
        var frame = projector.Add(new GameEvent(1, null, moved, null));

        Assert.Equal(0, frame.Index);
        Assert.Equal(1, frame.Seq);
        Assert.Equal("models_moved", frame.Kind);
        Assert.Equal(18, frame.Models.Count);
        var placed = frame.Models.Single(m => m.Id == unit.Models[0].Id);
        Assert.Equal("table", placed.Where);
        Assert.Equal(6, placed.X);
        Assert.Empty(frame.OpenAsks);
        Assert.Empty(frame.Passed);
        Assert.Equal(0, frame.Scores.Values.Sum());
        Assert.Contains("moved", frame.Line);
    }

    [Fact]
    public void A_frame_carries_the_score_and_who_has_passed()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        projector.Add(new GameEvent(1, null, new RoundStarted(1, c.TeamIds, []), null));
        projector.Add(new GameEvent(2, null, new TurnStarted(c.TeamIds[0]), null));
        var scored = projector.Add(new GameEvent(3, null, new ScoreSet(c.TeamIds[0], 3), null));
        Assert.Equal(3, scored.Scores[c.TeamIds[0]]);
        Assert.Empty(scored.Passed);

        var passed = projector.Add(new GameEvent(4, null, new TurnEnded(c.TeamIds[0], true, []), null));
        Assert.Equal(c.TeamIds[0], Assert.Single(passed.Passed));
        Assert.Equal(3, passed.Scores[c.TeamIds[0]]);   // whole state, not a delta
    }

    [Fact]
    public void A_marker_placed_during_play_carries_its_own_definition()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        var marker = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Tabletop.Geometry.Point3(24, 12, 0),
            new Tabletop.Geometry.Yaw(0), 3, "Objective A", VisualRef.OfBuiltin("objective-ring"));
        var frame = projector.Add(new GameEvent(1, null, new MarkerPlaced(marker), null));

        // The board block knows only the setup markers, so the frame is what tells a viewer how to draw this one.
        Assert.DoesNotContain(marker.Id, projector.Board.Markers.Select(m => m.Id));
        var state = Assert.Single(frame.Markers);
        Assert.Equal(marker.Id, state.Id);
        Assert.Equal("objective", state.Kind);
        Assert.Equal(3, state.Diameter);
        Assert.Equal("Objective A", state.Label);
        Assert.Equal(24, state.X);
    }

    [Fact]
    public void An_open_ask_appears_on_every_frame_until_it_closes()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.Activate, "Activate", [c.TeamIds[0]],
            new Options([new AskOption("x", "X", [])]), true, false, null, null);
        Assert.Single(projector.Add(new GameEvent(1, null, new AskOpened(ask), null)).OpenAsks);
        var second = projector.Add(new GameEvent(2, null, new PhaseStarted("rounds"), null));
        Assert.Equal(ask.Id, Assert.Single(second.OpenAsks).Id);
        Assert.Equal("options", second.OpenAsks[0].LegalKind);
        Assert.Empty(projector.Add(new GameEvent(3, null, new AskClosed(ask.Id, []), null)).OpenAsks);
    }

    [Fact]
    public void The_dice_of_a_step_are_recorded_raw_modified_and_judged()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        var frameId = Guid.CreateVersion7();
        projector.Add(new GameEvent(1, null, new SequenceStarted(frameId, "attack", new SequenceContext(null, [], [], null)), null));
        var roll = new RollRecord(Guid.CreateVersion7(), "hit", 4, [new RollModifier("Good Shot", 1)],
            [new Die(3, 4, true), new Die(1, 2, false)]);
        var frame = projector.Add(new GameEvent(2, null,
            new StepCompleted(frameId, "hit-roll", null, [roll], []), null));
        var recorded = Assert.Single(frame.Rolls);
        Assert.Equal([3, 1], recorded.Raw);
        Assert.Equal([4, 2], recorded.Modified);
        Assert.Equal([true, false], recorded.Success);
        Assert.Equal(1, recorded.Net);
        Assert.Equal(4, recorded.Threshold);
    }

    [Fact]
    public void A_step_with_two_pools_records_both_and_the_line_names_each_threshold()
    {
        // A ruleset splits one roll wherever a step's dice do not share a threshold: OPR's roll-to-hit takes one pool per Quality
        // value the attacking models bring, since p.4 tests each attack against "the model's Quality value" and a joined hero
        // brings its own. The recording carries both, and the line has to say which is which.
        var projector = new FrameProjector(Scenario());
        var frameId = Guid.CreateVersion7();
        projector.Add(new GameEvent(1, null, new SequenceStarted(frameId, "attack", new SequenceContext(null, [], [], null)), null));
        var captain = new RollRecord(Guid.CreateVersion7(), "hit", 3, [], [new Die(4, 4, true)]);
        var squad = new RollRecord(Guid.CreateVersion7(), "hit", 5, [], [new Die(4, 4, false)]);
        var frame = projector.Add(new GameEvent(2, null,
            new StepCompleted(frameId, "roll-to-hit", null, [captain, squad], []), null));
        Assert.Equal([3, 5], frame.Rolls.Select(r => r.Threshold));
        Assert.Equal("roll-to-hit: hit 3+ 4!; hit 5+ 4", frame.Line);
    }

    [Fact]
    public async Task A_recording_round_trips_through_json()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        projector.Add(new GameEvent(1, null, new PhaseStarted("deployment"), null));
        projector.Add(new GameEvent(2, null, new RoundStarted(1, c.TeamIds, []), null));
        projector.Add(new GameEvent(3, null, new ScoreSet(c.TeamIds[0], 4), null));
        var teams = c.Info.Teams.Select(t => new RecordedTeam(t.Id, t.Ordinal, $"Team {t.Ordinal}", [])).ToList();
        var meta = new RecordingMeta("terrain", 1, "in-process", "grimdark-future", c.Properties.ContentHash,
            "alternating-activation", "strict", 8, teams, "round_limit", null, null, 1, 2, DateTimeOffset.UnixEpoch, 12.5);
        var recording = projector.Build(meta);

        var path = Path.Combine(Path.GetTempPath(), $"sim-{Guid.CreateVersion7():N}.json");
        try
        {
            await RecordingJson.WriteAsync(recording, path, CancellationToken.None);
            var read = await RecordingJson.ReadAsync(path, CancellationToken.None);
            Assert.Equal(RecordingJson.SchemaVersion, read.Version);
            Assert.Equal(RecordingJson.Serialize(recording), RecordingJson.Serialize(read));
            Assert.Equal(3, read.Frames.Count);
            Assert.Equal(4, read.Frames[2].Scores[c.TeamIds[0]]);
            Assert.Equal(teams.Count, read.Meta.Teams.Count);
            Assert.Equal("terrain", read.Meta.Scenario);
            Assert.Contains("\"schemaVersion\"", await File.ReadAllTextAsync(path, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_json_is_camel_case_and_omits_nulls()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        projector.Add(new GameEvent(1, null, new PhaseStarted("deployment"), null));
        var json = RecordingJson.Serialize(projector.Build(new RecordingMeta("terrain", 1, "in-process",
            "grimdark-future", "sha256:x", "alternating-activation", "strict", null, [], "stuck", "no ask, no events",
            null, 0, 1, DateTimeOffset.UnixEpoch, 1)));
        Assert.Contains("\"frames\"", json);
        Assert.DoesNotContain("\"Frames\"", json);
        Assert.Contains("\"polygons\"", json);            // the resolved zone rings, in the board block
        Assert.Contains("\"scores\"", json);              // present even with no points scored: a frame is whole state
        Assert.Contains("\"passed\"", json);
        Assert.DoesNotContain("roundCap", json);          // null, so omitted
        Assert.DoesNotContain("activeTeamId", json);      // no active team yet, so omitted
    }

    [Fact]
    public void An_undone_event_is_tolerated_on_a_projector_that_saw_nothing_before()
    {
        var projector = new FrameProjector(Scenario());
        var frame = projector.Add(new GameEvent(9, null, new Undone(5), null));
        Assert.Equal("undone", frame.Kind);
        Assert.Equal(18, frame.Models.Count);
    }

    [Fact]
    public void An_earlier_frames_scores_and_passed_are_copies_that_later_events_do_not_change()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        var team = c.TeamIds[0];
        projector.Add(new GameEvent(1, null, new RoundStarted(1, c.TeamIds, []), null));
        projector.Add(new GameEvent(2, null, new TurnStarted(team), null));
        var before = projector.Add(new GameEvent(3, null, new ScoreSet(team, 3), null));
        var passed = projector.Add(new GameEvent(4, null, new TurnEnded(team, true, []), null));
        projector.Add(new GameEvent(5, null, new ScoreSet(team, 7), null));
        projector.Add(new GameEvent(6, null, new RoundStarted(2, c.TeamIds, []), null));

        Assert.Equal(3, before.Scores[team]);
        Assert.Empty(before.Passed);
        Assert.Equal(team, Assert.Single(passed.Passed));
        Assert.Equal(3, passed.Scores[team]);
        Assert.Equal(3, projector.Frames[2].Scores[team]);
    }
}
```
- [ ] **Step 2: Run; expect failure, then write `Recording.cs`** — the schema, in full

Run: `dotnet test tests/Tabletop.Sim.Tests --filter RecordingTests`
Expected: `CS0246: ... 'FrameProjector' could not be found`.

```csharp
using System.Text.Json.Serialization;

namespace Tabletop.Sim.Recording;

// A RECORDING OF A PROJECTION, NOT A LOG OF EVENTS (spec → The recording is the artifact).
// Every frame carries whole state, so a viewer interpolates nothing and decides nothing: it draws what it
// is handed. The recording is derived and disposable — it is not a save format, it is never replayed into
// the engine, and the event log in Postgres remains the only thing undo and hydration read. A reader who
// wants what happened rather than what it looked like wants game.game_event, not this.

/// <summary>One run. `schemaVersion` is RecordingJson.SchemaVersion; a reader that does not know it should refuse the file.</summary>
public sealed record Recording([property: JsonPropertyName("schemaVersion")] int Version, RecordingMeta Meta, RecordedBoard Board, IReadOnlyList<RecordedFrame> Frames);

/// <param name="Outcome">
/// `ended`, `round_limit`, `stuck` or `failed` for a finished run; `live` for a feed that is still
/// running, which viewer/'s live feed sets when it manufactures a RecordingMeta from GameInfo.
/// sim/ writes only the first four.
/// </param>
public sealed record RecordingMeta(
    string Scenario,
    ulong Seed,
    string Driver,
    string GameRules,
    string PropertiesHash,
    string SchedulerShape,
    string Enforcement,
    int? RoundCap,
    IReadOnlyList<RecordedTeam> Teams,
    string Outcome,
    string? OutcomeDetail,
    Guid? WinningTeamId,
    int Rounds,
    long LastSeq,
    DateTimeOffset StartedAt,
    double DurationMs);

public sealed record RecordedTeam(Guid Id, int Ordinal, string Name, IReadOnlyList<string> Participants);

/// <summary>The initial state: the slab, what stands on it, and one definition per model. Positions are per frame.</summary>
public sealed record RecordedBoard(
    double Width,
    double Length,
    string Texture,
    IReadOnlyList<RecordedTerrain> Terrain,
    IReadOnlyList<RecordedMarker> Markers,
    IReadOnlyList<RecordedZone> Zones,
    IReadOnlyList<RecordedModel> Models);

public sealed record RecordedTerrain(
    Guid Id, string Mesh, double X, double Y, double Z, double Yaw, double Scale, IReadOnlyList<RecordedVolume> Volumes);

/// <param name="Kind">`solid` or `area`.</param>
/// <param name="Footprint">`circle` (A = radius) or `rect` (A = width, B = length).</param>
/// <param name="Declarations">The closed six, snake_case: impassable, blocks_line_of_sight, see_in_and_out, cover, difficult, dangerous.</param>
public sealed record RecordedVolume(
    Guid Id, string Kind, string Footprint, double A, double B,
    double OffsetX, double OffsetY, double Yaw, double Bottom, double Top,
    IReadOnlyList<string> Declarations, string? Notes);

/// <param name="Kind">`objective` or `token`.</param>
public sealed record RecordedMarker(Guid Id, string Kind, double Diameter, string? Label);

/// <param name="Shape">The shape id, as the table state holds it — `frontline/1`.</param>
/// <param name="Polygons">
/// The shape already resolved: one ring per polygon, each a list of `{x, y}` in table inches. Recorded
/// rather than left to the reader because resolving a shape id is `Tabletop.Geometry.ZoneStyles`, which is
/// C#, and the inset is absolute (OPR's frontline is twelve inches) rather than a fraction of the table,
/// so it cannot be recovered from the id and the size. It costs a handful of points once per zone.
/// </param>
public sealed record RecordedZone(
    Guid Id, string Shape, int TeamOrdinal, IReadOnlyList<IReadOnlyList<RecordedPoint>> Polygons);

/// <summary>A ring vertex in table inches. Sim-owned so the JSON is exactly {x, y}; Point2 also serialises its Length.</summary>
public sealed record RecordedPoint(double X, double Y);

/// <param name="BaseShape">`circle`, `oval` or `rect`.</param>
/// <param name="BaseA">A circle's diameter, an oval's or rect's length — all in inches.</param>
/// <param name="BaseB">An oval's width; equal to BaseA otherwise.</param>
public sealed record RecordedModel(
    Guid Id, Guid UnitId, string UnitName, string Name, int Ordinal,
    Guid TeamId, int TeamOrdinal, string BaseShape, double BaseA, double BaseB, double Height);

/// <param name="Index">Zero-based position in Frames; the timeline's coordinate.</param>
/// <param name="Seq">The event log sequence this frame is the state after.</param>
/// <param name="Kind">The event kind, snake_case, exactly as EventKinds spells it.</param>
/// <param name="Line">One human line, generated by LogLines from the names the event already carries.</param>
/// <param name="Scores">Projection.Scores, whole: every team's score as of this frame, not a delta.</param>
/// <param name="Passed">Projection.Scheduler.Passed: the teams that have passed and will not re-enter the round.</param>
public sealed record RecordedFrame(
    int Index,
    long Seq,
    string Kind,
    Guid? Actor,
    string? Warning,
    int Round,
    string? Phase,
    Guid? ActiveTeamId,
    string Line,
    IReadOnlyList<RecordedModelState> Models,
    IReadOnlyList<RecordedUnitState> Units,
    IReadOnlyList<RecordedMarkerState> Markers,
    IReadOnlyList<RecordedAsk> OpenAsks,
    IReadOnlyList<RecordedRoll> Rolls,
    IReadOnlyDictionary<Guid, int> Scores,
    IReadOnlyList<Guid> Passed);

/// <param name="Where">`table`, `shelf` or `inside` — the three places a model can be (game flow → The shelf, Inside a container).</param>
/// <param name="ContainerId">Set when Where is `inside`; a model inside a container has no position.</param>
public sealed record RecordedModelState(
    Guid Id, string Where, double? X, double? Y, double? Z, double? Yaw, Guid? ContainerId,
    int Wounds, int SpellTokens, IReadOnlyList<string> Conditions);

public sealed record RecordedUnitState(
    Guid Id, IReadOnlyList<string> Actions, IReadOnlyList<string> Conditions,
    IReadOnlyList<string> Grants, int WoundsThisActivation);

/// <param name="Kind">`objective` or `token`.</param>
/// <param name="Diameter">Inches. Carried per frame, not looked up in Board.Markers: a marker placed during
/// play is in no board block, and a frame a reader has to join against something that may not be there is
/// not whole state.</param>
public sealed record RecordedMarkerState(
    Guid Id, string Kind, double Diameter, string? Label, double X, double Y, double Z, double Yaw);

/// <param name="LegalKind">The legal set's wire kind: options, pieces, allocation, assignment, move_region, token_spend, confirmation.</param>
/// <param name="Answered">The teams that have already answered — a sealed Ask's answers stay hidden until it closes.</param>
/// <param name="Options">Present for an options or assignment set: the choice ids, their labels and the pieces they name.</param>
/// <param name="Pieces">The pieces the set names: eligible, targets, models, or a token spend's spender models (p.13).</param>
/// <param name="Allowance">A move region's allowance in inches, or a token spend's total maximum. The spend's per-model
/// maxima are not carried: every frame already holds each model's SpellTokens.</param>
public sealed record RecordedAsk(
    Guid Id, string Question, string Prompt, IReadOnlyList<Guid> Teams,
    bool AllowPass, bool Sealed, string? WindowKind, Guid? FrameId,
    string LegalKind, IReadOnlyList<Guid> Answered,
    IReadOnlyList<RecordedAskOption> Options, IReadOnlyList<Guid> Pieces, double? Allowance);

public sealed record RecordedAskOption(string Id, string Label, IReadOnlyList<Guid> Pieces);

/// <param name="Net">The summed, capped modifier the engine applied — Modified minus Raw, per die.</param>
public sealed record RecordedRoll(
    Guid Id, string Roll, int? Threshold, int Net,
    IReadOnlyList<int> Raw, IReadOnlyList<int> Modified, IReadOnlyList<bool> Success,
    IReadOnlyList<RecordedModifier> Modifiers);

public sealed record RecordedModifier(string Source, int Amount);
```

- [ ] **Step 3: Write `RecordingJson.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Sim.Recording;

/// <summary>
/// The recording's own JSON. Not WireJson: this is not a wire format, and it carries no type discriminator
/// because every shape here is closed at compile time and named by a string field instead.
/// </summary>
public static class RecordingJson
{
    public const int SchemaVersion = 1;

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Serialize(Recording recording) => JsonSerializer.Serialize(recording, Options);
    public static Recording Deserialize(string json) =>
        JsonSerializer.Deserialize<Recording>(json, Options) ?? throw new InvalidDataException("empty recording");

    public static async Task WriteAsync(Recording recording, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var file = File.Create(path);
        await JsonSerializer.SerializeAsync(file, recording, Options, ct);
    }

    public static async Task<Recording> ReadAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Recording>(file, Options, ct)
            ?? throw new InvalidDataException($"{path} is not a recording");
    }
}
```

`Recording.Version` serialises as `schemaVersion`, because the record declares `[property: JsonPropertyName("schemaVersion")] int Version`, and the test asserts that name appears in the file. A reader checks it first and refuses anything it does not know.

`RecordedFrame.Scores` is the schema's only dictionary and its keys are `Guid`s. `DictionaryKeyPolicy` is left unset, so `System.Text.Json` writes each key as the canonical hyphenated form and a team id in `scores` is spelled exactly as a team id anywhere else in the file. `PropertyNamingPolicy` does not touch dictionary keys.
- [ ] **Step 4: Write `LogLines.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Sim.Recording;

/// <summary>
/// One line per event, from the event and the projection after it. It reads the projection freely, and it
/// interprets nothing: every name it prints — a sequence, a step, an action, a condition, a question — is a
/// string the event already carries, and nothing here decides what any of them mean.
/// </summary>
public static class LogLines
{
    public static string Of(GameEvent e, Projection after, IReadOnlyDictionary<Guid, int> ordinals) => e.Payload switch
    {
        RoundStarted r => $"Round {r.Round} started",
        TurnStarted t => $"{Team(ordinals, t.TeamId)}'s turn",
        TurnEnded t => t.Passed ? $"{Team(ordinals, t.TeamId)} passed" : $"{Team(ordinals, t.TeamId)}'s turn ended",
        PhaseStarted p => $"Phase: {p.Phase}",
        SequenceStarted s => $"{s.Sequence} started{Actor(after, s.Context.ActorUnitId)}",
        StepCompleted s => $"{s.Step}{Dice(s.Rolls)}",
        SequenceFinished => "sequence finished",
        SequenceAbandoned => "sequence abandoned",
        AskOpened a => $"Asked {string.Join(", ", a.Ask.Teams.Select(t => Team(ordinals, t)))}: {a.Ask.Prompt}",
        AskAnswered a => $"{Team(ordinals, a.TeamId)} answered {Answer(a.Answer)}{(a.OutsideLegalSet ? " (outside the legal set)" : "")}",
        AskClosed => "ask closed",
        DiceRerolled d => $"rerolled {d.Replaced.Count} dice",
        DiceEdited d => $"edited {d.Replaced.Count} dice",
        ModifierAdded m => $"{m.Modifier.Source} gives {m.Modifier.Amount:+#;-#;0} to {m.Roll}",
        Undone u => $"undone to {u.ToSeq}",
        ModelsMoved m => $"{m.Moves.Count} model(s) moved",
        WoundsSet w => $"{Model(after, w.ModelId)} at {w.Wounds} wound(s)",
        ConditionSet c => $"{Subject(after, c.Subject)} {(c.Present ? "gains" : "loses")} {c.Condition}",
        ActionRecorded a => $"{Unit(after, a.UnitId)} did {a.Action}",
        ActionRecordCleared a => $"{Unit(after, a.UnitId)}'s action record cleared",
        GrantAdded g => $"{Unit(after, g.UnitId)} gains {g.Grant.EntryName}",
        GrantRemoved => "a grant was removed",
        SpellTokensSet s => $"{Model(after, s.ModelId)} holds {s.Tokens} token(s)",
        WoundsThisActivationSet w => $"{Unit(after, w.UnitId)} has taken {w.Wounds} wound(s) this activation",
        ScoreSet s => $"{Team(ordinals, s.TeamId)} scores {s.Score}",
        MarkerPlaced m => $"marker placed{(m.Marker.Label is { } l ? $": {l}" : "")}",
        MarkerMoved => "marker moved",
        MarkerRemoved => "marker removed",
        _ => EventKinds.Of(e.Payload),
    };

    private static string Team(IReadOnlyDictionary<Guid, int> ordinals, Guid teamId) =>
        ordinals.TryGetValue(teamId, out var ordinal) ? $"Team {ordinal}" : teamId.ToString("N")[..8];

    private static string Unit(Projection p, Guid unitId) =>
        p.Units.TryGetValue(unitId, out var u) ? u.Definition.Name : unitId.ToString("N")[..8];

    private static string Model(Projection p, Guid modelId) =>
        p.Models.TryGetValue(modelId, out var m) ? m.Slot.Name : modelId.ToString("N")[..8];

    private static string Subject(Projection p, Subject s) =>
        s.Kind == SubjectKind.Unit ? Unit(p, s.Id) : Model(p, s.Id);

    private static string Actor(Projection p, Guid? unitId) => unitId is { } id ? $" for {Unit(p, id)}" : "";

    /// <summary>
    /// Every roll on the step, not the first: a ruleset splits one roll wherever a step's dice do not share a threshold, and OPR's
    /// roll-to-hit takes one pool per Quality value the attacking models bring (p.4, and a joined hero brings its own). The
    /// threshold is printed because it is the only thing that tells two pools of the same roll apart.
    /// </summary>
    private static string Dice(IReadOnlyList<RollRecord> rolls) =>
        rolls.Count == 0 ? "" : ": " + string.Join("; ", rolls.Select(r =>
            $"{r.Roll}{(r.Threshold is { } t ? $" {t}+" : "")} {string.Join(",", r.Dice.Select(d => d.Success ? $"{d.Modified}!" : $"{d.Modified}"))}"));

    private static string Answer(AskAnswer? answer) => answer switch
    {
        null => "nothing",
        Chosen c => c.OptionId,
        SelectedPieces s => $"{s.PieceIds.Count} piece(s)",
        Allocated a => $"{a.Counts.Values.Sum()} allocated",
        Assigned a => $"{a.Targets.Count} assignment(s)",
        Destinations d => $"{d.Moves.Count} destination(s)",
        TokensSpent t => $"{t.Counts.Values.Sum()} token(s) spent",
        Confirmed => "yes",
        Passed => "a pass",
        _ => answer.GetType().Name,
    };
}
```

`Projection` has no team-ordinal lookup — the ordinals live on `GameInfo.Teams` — so the ordinals are passed in as a dictionary that `FrameProjector` builds once from `CompiledScenario.Info.Teams`. That is why `Of` takes three arguments and not two.

- [ ] **Step 5: Write `FrameProjector.cs`**

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Recording;

/// <summary>
/// The second applier. It is a viewer, exactly like the client: it applies the events the driver drained and
/// writes down what the table looked like afterwards. It never decides anything and never talks to the engine.
/// </summary>
public sealed class FrameProjector(CompiledScenario scenario)
{
    private readonly EventApplier _applier = new(scenario.Board, scenario.Info);
    private readonly List<RecordedFrame> _frames = [];
    private readonly IReadOnlyDictionary<Guid, int> _ordinals = scenario.Info.Teams.ToDictionary(t => t.Id, t => t.Ordinal);

    public RecordedBoard Board { get; } = BuildBoard(scenario);
    public IReadOnlyList<RecordedFrame> Frames => _frames;
    public Projection Projection => _applier.Projection;

    public RecordedFrame Add(GameEvent e)
    {
        _applier.Apply(e);
        var p = _applier.Projection;
        var rolls = e.Payload is StepCompleted step ? step.Rolls.Select(RollOf).ToList() : [];
        var frame = new RecordedFrame(
            _frames.Count, e.Seq, EventKinds.Of(e.Payload), e.Actor, e.Warning,
            p.Scheduler.Round, p.Scheduler.Phase, p.Scheduler.ActiveTeamId,
            LogLines.Of(e, p, _ordinals),
            p.Models.Values.Select(ModelState).ToList(),
            p.Units.Values.Select(UnitState).ToList(),
            p.Markers.Values.Select(MarkerState).ToList(),
            p.OpenAsks.Values.Select(AskOf).ToList(),
            rolls,
            // Copied, not aliased: the applier keeps mutating these and every frame is kept for the run.
            p.Scores.ToDictionary(kv => kv.Key, kv => kv.Value),
            p.Scheduler.Passed.ToList());
        _frames.Add(frame);
        return frame;
    }

    public Recording Build(RecordingMeta meta) => new(RecordingJson.SchemaVersion, meta, Board, _frames);

    private static RecordedModelState ModelState(ModelInPlay m) => m.Location switch
    {
        OnTable t => new RecordedModelState(m.Id, "table", t.Placement.Position.X, t.Placement.Position.Y,
            t.Placement.Position.Z, t.Placement.Yaw.Degrees, null, m.Wounds, m.SpellTokens, m.Conditions.Order().ToList()),
        Inside i => new RecordedModelState(m.Id, "inside", null, null, null, null, i.ContainerId,
            m.Wounds, m.SpellTokens, m.Conditions.Order().ToList()),
        _ => new RecordedModelState(m.Id, "shelf", null, null, null, null, null,
            m.Wounds, m.SpellTokens, m.Conditions.Order().ToList()),
    };

    // Kind, diameter and label ride along per frame, because a marker placed during play is in no board block.
    private static RecordedMarkerState MarkerState(Marker m) => new(
        m.Id, m.Kind.ToString().ToLowerInvariant(), m.Diameter, m.Label,
        m.Position.X, m.Position.Y, m.Position.Z, m.Yaw.Degrees);

    private static RecordedUnitState UnitState(UnitInPlay u) => new(
        u.Id, u.Actions.Order().ToList(), u.Conditions.Order().ToList(),
        u.Grants.Select(g => g.EntryName).ToList(), u.WoundsThisActivation);

    private static RecordedAsk AskOf(OpenAsk open)
    {
        var ask = open.Ask;
        var (kind, options, pieces, allowance) = ask.Legal switch
        {
            Options o => ("options", o.Choices.Select(OptionOf).ToList(), (IReadOnlyList<Guid>)[], (double?)null),
            PieceSelection p => ("pieces", [], p.Eligible, null),
            Allocation a => ("allocation", [], a.Targets, null),
            Assignment a => ("assignment", a.Items.Select(OptionOf).ToList(), a.Targets, null),
            MoveRegion m => ("move_region", [], m.ModelIds, m.Allowance),
            TokenSpend t => ("token_spend", [], t.MaxByModel.Keys.ToList(), (double)t.MaxByModel.Values.Sum()),
            Confirmation => ("confirmation", [], [], null),
            _ => (ask.Legal.GetType().Name, [], [], null),
        };
        return new RecordedAsk(ask.Id, ask.Question, ask.Prompt, ask.Teams, ask.AllowPass, ask.Sealed,
            ask.WindowKind, ask.FrameId, kind, open.Answers.Keys.ToList(), options, pieces, allowance);
    }

    private static RecordedAskOption OptionOf(AskOption o) => new(o.Id, o.Label, o.Pieces);

    private static RecordedRoll RollOf(RollRecord r) => new(
        r.Id, r.Roll, r.Threshold, r.Dice.Count == 0 ? 0 : r.Dice[0].Modified - r.Dice[0].Raw,
        r.Dice.Select(d => d.Raw).ToList(), r.Dice.Select(d => d.Modified).ToList(), r.Dice.Select(d => d.Success).ToList(),
        r.Modifiers.Select(m => new RecordedModifier(m.Source, m.Amount)).ToList());

    private static RecordedBoard BuildBoard(CompiledScenario s)
    {
        var table = s.Board.Table;
        var teamOf = s.Info.Participants.Where(p => p.TeamId is not null).ToDictionary(p => p.Id, p => p.TeamId!.Value);
        var ordinalOf = s.Info.Teams.ToDictionary(t => t.Id, t => t.Ordinal);
        var models = s.Board.Armies.SelectMany(a => a.Snapshot.Units.SelectMany(u => u.Models.Select(m =>
            new RecordedModel(m.Id, u.Id, u.Name, m.Name, m.Ordinal, teamOf[a.ParticipantId], ordinalOf[teamOf[a.ParticipantId]],
                m.Base.Shape.ToString().ToLowerInvariant(), m.Base.A, m.Base.B, m.Height)))).ToList();

        return new RecordedBoard(
            table.Size.Width, table.Size.Length, VisualName(table.Texture),
            table.Terrain.Select(t => new RecordedTerrain(t.Id, VisualName(t.Mesh),
                t.Position.X, t.Position.Y, t.Position.Z, t.Yaw.Degrees, t.Scale,
                t.Volumes.Select(v => VolumeOf(v.Id, v.Volume)).ToList())).ToList(),
            table.Markers.Select(m => new RecordedMarker(m.Id, m.Kind.ToString().ToLowerInvariant(), m.Diameter, m.Label)).ToList(),
            table.Zones.Select(z => new RecordedZone(z.Id, z.Shape, z.TeamOrdinal, RingsOf(z.Shape, table.Size))).ToList(),
            models);
    }

    private static IReadOnlyList<IReadOnlyList<RecordedPoint>> RingsOf(string shape, TableSize size)
    {
        try
        {
            return ZoneStyles.Resolve(shape, size)
                .Select(g => (IReadOnlyList<RecordedPoint>)g.Vertices.Select(v => new RecordedPoint(v.X, v.Y)).ToList()).ToList();
        }
        catch (ArgumentException) { return []; }
    }

    private static string VisualName(VisualRef visual) => visual.Builtin ?? visual.AssetId!.Value.ToString();

    private static RecordedVolume VolumeOf(Guid id, Volume v) => new(
        id, v.Kind.ToString().ToLowerInvariant(), v.Footprint.Kind.ToString().ToLowerInvariant(),
        v.Footprint.A, v.Footprint.B, v.Offset.X, v.Offset.Y, v.Yaw.Degrees, v.Bottom, v.Top,
        Declarations(v.Declarations), v.Notes);

    private static IReadOnlyList<string> Declarations(TerrainDeclarations d) =>
        Enum.GetValues<TerrainDeclarations>()
            .Where(f => f != TerrainDeclarations.None && d.HasFlag(f))
            .Select(f => string.Concat(f.ToString().Select((ch, i) => char.IsUpper(ch) && i > 0 ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString())))
            .ToList();
}
```

The markers' positions live on `RecordedMarkerState` frame by frame like a model's, and so do the kind, the diameter and the label — a marker placed during play appears in the first frame after its `marker_placed` and is in no board block, so the frame has to be enough on its own. `RecordedBoard.Markers` stays as the *setup* markers, which is what the board block describes; where both name the same marker they agree, and the frame is what a viewer draws from.

`ZoneStyles.Resolve` answers in the corner frame the recording already uses, so nothing converts. It throws `ArgumentException` on an unknown or malformed shape id, and `RingsOf` catches it, so `polygons` is `[]` and a viewer draws no outline while still showing the id — the same fallback as a client that does not know the style (client design → *An unknown zone shape id*).

- [ ] **Step 6: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter RecordingTests
git add sim/Recording tests/Tabletop.Sim.Tests
git commit -m "Record a projection frame per event, and its JSON schema"
```
