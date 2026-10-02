# Task 10: The agreement test — one recording through both appliers

**Files:**
- Create: `tests/Tabletop.Viewer.Tests/FixtureWriter.cs`, `tests/Tabletop.Viewer.Tests/AgreementFixtureTests.cs`
- Create (generated once, checked in): `viewer/tests/fixtures/skirmish.recording.json`, `skirmish.welcome.json`, `skirmish.board.json`, `skirmish.events.json`, and the same four for `terrain`
- Create: `viewer/tests/normalise.ts`, `viewer/tests/fixture.ts`, `viewer/tests/agreement.test.ts`

**Interfaces:**
- Consumes: `Scenario`, `ScenarioCatalog`, `ScenarioCompiler`, `CompiledScenario` (simulation plan Task 1); `Chooser` (Task 1); `HandPlacement.ChooserFor`; `AnswerPolicy`, `ComposedAnswers` (Tasks 2–3); `InProcessDriver` (Task 4); `RunLoop`, `RunReport`, `RunSettings` (Task 6); `Recording`, `RecordingJson`, `FrameProjector` (Task 5); `WireJson`, `Welcome`, `BoardMessage`, `EventsMessage`, `GameEvent`, `MarkerMoved`, `MarkerRemoved`, `Point3`, `Yaw` (contracts plan Task 12); `EventApplier` and `ProjectionDesyncException` (contracts plan Task 14); `RepoPaths` (Task 2); `FrameProjector`, `Applier`, `ProjectionDesyncError`, `recordedBoardOf` (Task 9); `readRecording` (Task 3).
- Produces (C#): `sealed record Fixture(string Name, Recording Recording, IReadOnlyList<GameEvent> Log, GameInfo Game, Board Board, Guid RecorderParticipantId)`; `static class FixtureWriter` with `Task<Fixture> RunAsync(Scenario, CancellationToken)` and `Task WriteAsync(Fixture, string directory, CancellationToken)`; `const string[] FixtureNames = ["skirmish", "terrain"]`.
- Produces (TypeScript): in `normalise.ts` — `function normaliseFrame(frame: RecordedFrame): unknown`, `function normaliseBoard(board: RecordedBoard): unknown`; in `fixture.ts` — `const FIXTURES: readonly string[]`, `function loadFixture(name: string): { game: GameInfo; board: Board; events: readonly GameEvent[]; recording: Recording }` (Task 11's live-feed test loads the same four files, so this lives in its own module rather than inside a test file).

Spec: spec → *Two feeds* — "the applier is checked against the C# one by replaying a recording through both and comparing projections". Spec → *Testing* — "`viewer/` has Vitest over … the generated applier against a recording."

**How the comparison gets both sides.** The recording's frames *are* the C# `EventApplier`'s output, canonicalised by `FrameProjector` — so no separate C# projection dump is needed and nothing has to be exported for the test to read. The fixture is therefore the four frames a live socket delivers on connect (`welcome`, `board`, `events`) plus the recording the same run wrote. The TypeScript side is handed `welcome` and `board`, builds a `FrameProjector`, feeds it the events one at a time, and must reproduce `recording.board` and every `recording.frames[i]` field for field. That is a stronger check than comparing final projections: it compares 200-odd intermediate states, and it exercises the generated wire types, the ported applier, the ported log lines and the ported board projection in one pass. It is also, incidentally, an end-to-end test of the live feed's decoding, since those are the exact bytes the socket sends.

**The C# side replays the same files.** Each `Names` theory case also reads the fixture's `welcome`, `board` and `events` frames and feeds the events through `FrameProjector`, starting from the welcome's game and the board message. It asserts that the serialised board and every serialised frame equal `recording.json`'s. A change on the C# side that leaves the fixtures stale therefore fails `dotnet test`, and the TypeScript replay is not the only check on them. `FrameProjector` reads only `Board` and `Info` from its `CompiledScenario`, so the test builds one with nothing else set. The recording round trip compares serialised strings, because record equality over `IReadOnlyList` compares references.

**What frame comparison cannot prove, and the one pair that covers it.** Every frame matching says the two appliers agree on a *valid* log. It says nothing about an invalid one, and that is where two implementations of the same switch diverge most cheaply: the C# throws `ProjectionDesyncException` on an id the projection does not hold, while the natural TypeScript spelling of the same line spreads `undefined` into a fresh object and carries on. So one event — `marker_moved` naming a marker nothing placed — is applied on both sides and both must fault: `AgreementFixtureTests.The_csharp_applier_faults_on_an_id_the_projection_does_not_hold` in C#, *the two appliers fault the same way* in `agreement.test.ts`. A typed throw on one side against a quiet `undefined` on the other is not equivalence, and this plan will not claim it while the pair is missing.

**Fixtures are checked in and regenerated deliberately, not diffed.** Ids are `Guid.CreateVersion7()` and differ between runs (spec → *The seed is stored, so a run sets it*: "the ids differ between runs. Nothing asserts that two runs share an id, and nothing compares two recordings byte for byte"), so a fixture cannot be regenerated and compared to its predecessor. `TABLETOP_WRITE_FIXTURES=1` rewrites them; every other run reads what is checked in. `FixtureWriter` builds the answer policy as `sim/Program.cs` does: none when the scenario is `ScriptOnly`. It throws when the run ends `Stuck` or `Failed`, so a broken run is never written as a fixture. Both scenarios run under a two-round cap so the eight files stay a few megabytes: a recording is 5 to 6 MB for an eight-round game, so a two-round recording is about 1.5 MB, and an uncapped game would put tens of megabytes in the repo.

- [ ] **Step 1: Write `FixtureWriter.cs`**

```csharp
using Tabletop.Protocol;
using Tabletop.Sim;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Viewer.Tests;

/// <param name="Log">Raw, undone markers included — the viewer's applier drops undone ranges itself.</param>
public sealed record Fixture(
    string Name, Recording Recording, IReadOnlyList<GameEvent> Log, GameInfo Game, Board Board, Guid RecorderParticipantId);

/// <summary>
/// Writes the four frames a live socket delivers on connect, plus the recording the same run produced.
/// The viewer's agreement test replays the events and must reproduce the recording.
/// </summary>
public static class FixtureWriter
{
    public static readonly string[] FixtureNames = ["skirmish", "terrain"];

    public static async Task<Fixture> RunAsync(Scenario scenario, CancellationToken ct)
    {
        var compiled = ScenarioCompiler.Compile(scenario);
        // The policy is built as sim/Program.cs does.
        var answers = new ComposedAnswers(scenario.Script, scenario.ScriptOnly ? null : new AnswerPolicy(new Chooser(scenario.Seed)));
        await using var driver = await InProcessDriver.StartAsync(compiled, ct);
        var loop = new RunLoop(driver, compiled, answers, HandPlacement.ChooserFor(scenario.Seed), new RunSettings());
        var report = await loop.RunAsync(ct);
        if (report.Outcome is RunOutcome.Stuck or RunOutcome.Failed)
            throw new InvalidOperationException($"{scenario.Name}: the run ended {report.Outcome}; a broken run is not written as a fixture");
        return new Fixture(scenario.Name, loop.BuildRecording(report), loop.Log, driver.Info, driver.Board,
            compiled.RecorderParticipantId);
    }

    public static async Task WriteAsync(Fixture fixture, string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var name = fixture.Name;
        await RecordingJson.WriteAsync(fixture.Recording, Path.Combine(directory, $"{name}.recording.json"), ct);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.welcome.json"),
            WireJson.Serialize((SocketMessage)new Welcome(fixture.RecorderParticipantId, fixture.Game)), ct);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.board.json"),
            WireJson.Serialize((SocketMessage)new BoardMessage(fixture.Board)), ct);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.events.json"),
            WireJson.Serialize((SocketMessage)new EventsMessage(fixture.Log)), ct);
    }
}
```

- [ ] **Step 2: Write `AgreementFixtureTests.cs`**

```csharp
using System.Text.Json;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Viewer.Tests;

public class AgreementFixtureTests
{
    public static TheoryData<string> Names() => new(FixtureWriter.FixtureNames);

    [Theory]
    [MemberData(nameof(Names))]
    public async Task The_fixture_exists_round_trips_and_its_events_reproduce_its_frames(string name)
    {
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1")
        {
            var scenario = ScenarioCatalog.Get(name) with { RoundCap = 2 };
            await FixtureWriter.WriteAsync(
                await FixtureWriter.RunAsync(scenario, CancellationToken.None),
                RepoPaths.Fixtures, CancellationToken.None);
        }

        var path = Path.Combine(RepoPaths.Fixtures, $"{name}.recording.json");
        Assert.True(File.Exists(path), $"{path} is missing; run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests");
        var recording = await RecordingJson.ReadAsync(path, CancellationToken.None);
        Assert.Equal(RecordingJson.SchemaVersion, recording.Version);
        Assert.NotEmpty(recording.Frames);
        Assert.NotEmpty(recording.Board.Models);
        Assert.Equal(RecordingJson.Serialize(recording),
            RecordingJson.Serialize(RecordingJson.Deserialize(RecordingJson.Serialize(recording))));

        // The C# replay: the fixture's welcome, board and events through FrameProjector, as RunLoop.Absorb
        // feeds it, must reproduce the recorded board and frames. A change to the C# side that leaves the
        // fixtures stale fails here.
        var welcome = Assert.IsType<Welcome>(WireJson.Deserialize<SocketMessage>(
            await File.ReadAllTextAsync(Path.Combine(RepoPaths.Fixtures, $"{name}.welcome.json"))));
        var boardMessage = Assert.IsType<BoardMessage>(WireJson.Deserialize<SocketMessage>(
            await File.ReadAllTextAsync(Path.Combine(RepoPaths.Fixtures, $"{name}.board.json"))));
        var eventsMessage = Assert.IsType<EventsMessage>(WireJson.Deserialize<SocketMessage>(
            await File.ReadAllTextAsync(Path.Combine(RepoPaths.Fixtures, $"{name}.events.json"))));
        // FrameProjector reads only Board and Info from the scenario; the rest is the compiler's.
        var projector = new FrameProjector(new CompiledScenario(null!, welcome.Game, boardMessage.Board, null!,
            [], [], new Dictionary<Guid, Guid>(), null!, welcome.ParticipantId));
        foreach (var e in eventsMessage.Events) projector.Add(e);
        Assert.Equal(JsonSerializer.Serialize(recording.Board, RecordingJson.Options),
            JsonSerializer.Serialize(projector.Board, RecordingJson.Options));
        Assert.Equal(recording.Frames.Count, projector.Frames.Count);
        for (var i = 0; i < recording.Frames.Count; i++)
            Assert.Equal(JsonSerializer.Serialize(recording.Frames[i], RecordingJson.Options),
                JsonSerializer.Serialize(projector.Frames[i], RecordingJson.Options));

        // Every frame index is its position, and every frame names only board models — what read.ts enforces.
        var ids = recording.Board.Models.Select(m => m.Id).ToHashSet();
        for (var i = 0; i < recording.Frames.Count; i++)
        {
            Assert.Equal(i, recording.Frames[i].Index);
            Assert.All(recording.Frames[i].Models, m => Assert.Contains(m.Id, ids));
        }
    }

    [Fact]
    public void Both_fixtures_carry_the_four_files_the_viewer_reads()
    {
        foreach (var name in FixtureWriter.FixtureNames)
            foreach (var suffix in new[] { "recording", "welcome", "board", "events" })
                Assert.True(File.Exists(Path.Combine(RepoPaths.Fixtures, $"{name}.{suffix}.json")),
                    $"{name}.{suffix}.json is missing");
    }

    /// <summary>
    /// The C# half of one pair. The frame comparison proves the two appliers agree on a valid log; it
    /// cannot prove they agree on an invalid one, which is where equivalence is easiest to lose. C#
    /// throws <c>ProjectionDesyncException</c> and the TypeScript port
    /// throws <c>ProjectionDesyncError</c> on this same event — `viewer/tests/agreement.test.ts`,
    /// *the two appliers fault the same way*. One side faulting while the other returned undefined
    /// would not be equivalence, however many frames matched.
    /// </summary>
    [Fact]
    public void The_csharp_applier_faults_on_an_id_the_projection_does_not_hold()
    {
        var welcome = Assert.IsType<Welcome>(WireJson.Deserialize<SocketMessage>(
            File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "skirmish.welcome.json"))));
        var board = Assert.IsType<BoardMessage>(WireJson.Deserialize<SocketMessage>(
            File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "skirmish.board.json"))));
        var applier = new EventApplier(board.Board, welcome.Game);

        // A marker id nothing placed: the case ruling 2 names, a marker from setup rather than from an event.
        var unknown = Guid.CreateVersion7();
        Assert.Throws<ProjectionDesyncException>(
            () => applier.Apply(new GameEvent(1, null, new MarkerMoved(unknown, new Point3(5, 5, 0), new Yaw(90)), null)));

        // A store C# removes from rather than reads through does not fault, so the port must not either.
        applier.Apply(new GameEvent(2, null, new MarkerRemoved(unknown), null));
    }
}
```

- [ ] **Step 3: Generate the fixtures, then run the C# side**

```bash
cd "$(git rev-parse --show-toplevel)"
TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests --filter AgreementFixtureTests
dotnet test tests/Tabletop.Viewer.Tests
du -sh viewer/tests/fixtures
```
Expected: 4 tests passed on the second run (the `Names` theory is two cases, plus two facts, and each case includes the C# replay); the directory is under 4 MB. If it is larger, drop `RoundCap` to 1 for `terrain` and regenerate.

- [ ] **Step 4: Write `viewer/tests/normalise.ts`**

```ts
// viewer/tests/normalise.ts
// C# Dictionary enumeration order and JavaScript Map insertion order can diverge once an entry is removed
// and another added (a marker taken off the table, an Ask closed), and C#'s .Order() is culture-sensitive
// where JavaScript's .sort() is by code unit. Neither is a drift in the applier, so both sides are
// normalised before comparison and everything else is compared exactly.
import type { RecordedBoard, RecordedFrame } from '../src/lib/protocol/recording.generated';

const byId = <T extends { id: string }>(items: readonly T[]): readonly T[] =>
  [...items].sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
const sorted = (items: readonly string[]): readonly string[] => [...items].sort();

export function normaliseFrame(frame: RecordedFrame): unknown {
  return {
    ...frame,
    models: byId(frame.models).map((m) => ({ ...m, conditions: sorted(m.conditions) })),
    units: byId(frame.units).map((u) => ({ ...u, actions: sorted(u.actions), conditions: sorted(u.conditions) })),
    markers: byId(frame.markers),
    openAsks: byId(frame.openAsks).map((a) => ({ ...a, answered: sorted(a.answered) })),
    passed: sorted(frame.passed),
  };
}

// `polygons` is the one field of the recording the live feed cannot produce and is not asked to:
// ZoneStyles.Resolve is C#, deliberately not ported (Task 4), so recordedBoardOf writes []. Dropping
// it here keeps the comparison honest about what is actually ported. Everything else about a zone —
// its id, its shape id, its team ordinal — is compared exactly.
export function normaliseBoard(board: RecordedBoard): unknown {
  return {
    ...board,
    markers: byId(board.markers),
    zones: byId(board.zones).map(({ polygons: _polygons, ...rest }) => rest),
    models: byId(board.models),
  };
}
```

- [ ] **Step 5: Write `viewer/tests/fixture.ts` and `viewer/tests/agreement.test.ts`**

```ts
// viewer/tests/fixture.ts — the four frames a live socket delivers on connect, plus the recording of
// the same run. Written by tests/Tabletop.Viewer.Tests/FixtureWriter.cs and checked in.
import { readFileSync } from 'node:fs';
import { readRecording } from '../src/lib/recording/read';
import type { Board, GameEvent, GameInfo, SocketMessage } from '../src/lib/protocol/wire.generated';
import type { Recording } from '../src/lib/protocol/recording.generated';

export const FIXTURES = ['skirmish', 'terrain'] as const;

const read = (name: string): unknown => JSON.parse(readFileSync(`tests/fixtures/${name}.json`, 'utf8'));

export function loadFixture(name: string): {
  game: GameInfo; board: Board; events: readonly GameEvent[]; recording: Recording;
  frames: readonly SocketMessage[];
} {
  const welcome = read(`${name}.welcome`) as SocketMessage;
  const board = read(`${name}.board`) as SocketMessage;
  const events = read(`${name}.events`) as SocketMessage;
  if (welcome.kind !== 'welcome' || board.kind !== 'board' || events.kind !== 'events')
    throw new Error(`${name}: the fixture frames are not welcome, board and events`);
  return {
    game: welcome.game,
    board: board.board,
    events: events.events,
    recording: readRecording(read(`${name}.recording`)),
    frames: [welcome, board, events],
  };
}
```

```ts
// viewer/tests/agreement.test.ts
import { describe, expect, it } from 'vitest';
import { Applier, ProjectionDesyncError } from '../src/lib/live/apply';
import { FrameProjector, recordedBoardOf } from '../src/lib/live/project';
import type { GameEvent } from '../src/lib/protocol/wire.generated';
import { normaliseBoard, normaliseFrame } from './normalise';
import { FIXTURES, loadFixture } from './fixture';

describe.each(FIXTURES)('the TypeScript applier agrees with the C# one on %s', (name) => {
  const { game, board, events, recording } = loadFixture(name);

  it('reproduces the recorded board from the wire board', () => {
    expect(normaliseBoard(recordedBoardOf(board, game))).toEqual(normaliseBoard(recording.board));
    // normaliseBoard drops `polygons` because the browser has no ZoneStyles; check here that the C#
    // side really does resolve them, so the drop never quietly covers an empty field on both sides.
    for (const zone of recording.board.zones) {
      expect(zone.polygons.length, `${zone.shape} resolved to no rings`).toBeGreaterThan(0);
      for (const ring of zone.polygons) expect(ring.length).toBeGreaterThan(2);
    }
  });

  it('reproduces every recorded frame, field for field, from the raw event log', () => {
    const projector = new FrameProjector(board, game);
    const produced = events.map((event) => projector.add(event));
    expect(produced).toHaveLength(recording.frames.length);
    produced.forEach((frame, i) => {
      expect(normaliseFrame(frame), `frame ${i} (seq ${frame.seq}, ${frame.kind})`)
        .toEqual(normaliseFrame(recording.frames[i]!));
    });
  });

  it('replayed a real game, not a two-event stub', () => {
    expect(recording.frames.length).toBeGreaterThan(20);
    expect(new Set(recording.frames.map((f) => f.kind)).size).toBeGreaterThan(5);
    expect(recording.frames.some((f) => f.rolls.length > 0)).toBe(true);
    expect(recording.frames.some((f) => f.openAsks.length > 0)).toBe(true);
    expect(recording.frames.some((f) => f.models.some((m) => m.where === 'table'))).toBe(true);
  });
});

// The other half of the pair whose C# side is AgreementFixtureTests, "the C# applier faults on an id
// the projection does not hold". Agreement on 200 valid frames says nothing about the one input where
// the two implementations naturally diverge, so both faults are asserted on the same event.
describe('the two appliers fault the same way', () => {
  const { game, board } = loadFixture('skirmish');
  const UNKNOWN = '0198b7ac-0000-7000-8000-0000000000ff';

  it('throws ProjectionDesyncError where the C# throws ProjectionDesyncException', () => {
    const applier = new Applier(board, game);
    const moved = { seq: 1, actor: null, warning: null, payload: {
      kind: 'marker_moved', markerId: UNKNOWN, position: { x: 5, y: 5, z: 0 }, yaw: { degrees: 90 },
    } } as unknown as GameEvent;

    expect(() => applier.apply(moved)).toThrow(ProjectionDesyncError);
    // Not a typed throw against a silent success: nothing was written, so the projection did not
    // quietly diverge from the server's. `{ ...undefined, position }` would have left a marker here.
    expect(applier.projection.markers.size).toBe(0);

    // And the store the C# removes from rather than reads through faults on neither side.
    const removed = { seq: 2, actor: null, warning: null, payload: { kind: 'marker_removed', markerId: UNKNOWN } } as unknown as GameEvent;
    expect(() => new Applier(board, game).apply(removed)).not.toThrow();
  });
});
```

- [ ] **Step 6: Run the TypeScript side**

Run: `cd viewer && npm test`
Expected: 7 more tests passed — three per fixture, plus the one desync case that is not per fixture. A failure names the frame, its seq and its event kind, which localises a ported branch immediately.

- [ ] **Step 7: Commit**

```bash
cd "$(git rev-parse --show-toplevel)"
git add tests/Tabletop.Viewer.Tests viewer/tests/fixtures viewer/tests/normalise.ts viewer/tests/fixture.ts viewer/tests/agreement.test.ts
git commit -m "Replay a recording through both appliers and compare every frame"
```
