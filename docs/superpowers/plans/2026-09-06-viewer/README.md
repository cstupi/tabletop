# Developer Viewer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `viewer/` — a static Svelte page that draws a `sim/` recording top-down in inches, with a timeline and a log beside the board, plus a live feed that attaches to a running game server as a spectator with no team and manufactures the same frames in the browser.

**Architecture:** One input type: the recording schema `sim/Recording/Recording.cs` defines (`Recording`, `RecordedBoard`, `RecordedFrame`, …), whose frames carry whole state rather than deltas. The recording feed reads that file directly. The live feed reads `Tabletop.Protocol` off a WebSocket and *manufactures* the same `RecordedBoard` and `RecordedFrame[]` in TypeScript, so the renderer, the timeline and the panels have exactly one shape to draw and neither feed is privileged. Both TypeScript type sets are **generated from the C# by reflecting over `System.Text.Json`'s own contract metadata** and checked in, with a golden-file xUnit test as the drift guard. The board is a single 2D `<canvas>`.

**Tech Stack:** Node 24 (`~/.local/share/mise`), npm; `svelte` 5.57.0, `@sveltejs/vite-plugin-svelte` 7.3.0, `vite` 8.2.2, `vitest` 5.0.0, `typescript` 5.9.3, `svelte-check` 4.7.6. No SvelteKit, no three.js, no WebGL, no rendering library. `tests/Tabletop.Viewer.Tests` is a `net10.0` xUnit project.

**Spec:** `docs/superpowers/specs/2026-09-06-simulation-design.md` (primary — *The recording is the artifact*, *`viewer/` is a developer web app, separate from `web/`*, *Two feeds*, *Interactive debugging is a door, not a room*, *Testing*). Plans obeyed: `docs/superpowers/plans/2026-09-06-simulation/README.md` (**the recording schema, consumed verbatim**, and `RecordingJson.SchemaVersion`), `2026-09-05-contracts-and-local-dev.md` (`SocketMessage`, `EventPayload`, `StateChange`, `Ask`, `LegalSet`, `Projection`, `EventApplier`, `WireJson`, `ContentJson`), `2026-09-05-game-server-skeleton.md` (*Interfaces needed → Client shell plan*: the connect sequence, frame order and rejection cases), `2026-09-05-geometry.md` (the table frame, `Bodies.BaseOutline`, `WorldVolumes.WorldOutline`, `Point2.Rotate`), `2026-09-05-web-app/README.md` (**its Svelte, Vite and Vitest conventions and versions only** — `viewer/` shares no code and no build with `web/` and imports nothing from it), `2026-09-05-management-api/README.md` (only `POST /v1/auth/password` and `POST /v1/auth/session/refresh`, for the live feed's session JWT).

**Requires landed:** the simulation plan (the recording schema, consumed verbatim, and `RecordingJson.SchemaVersion`) and everything it requires; the contracts plan for `Tabletop.Protocol`. Task 12's `docs/architecture.md` edit extends a paragraph already in the document and needs no other plan.

## Global Constraints

- **`viewer/` is a deliverable that ships to developers** (spec → *`sim/` is a fourth deliverable*): "Neither is in the client's export, neither is referenced by a server, and a release that omits both is complete."
- **The recording schema is consumed, never redefined.** `schemaVersion` is the integer `1` (`RecordingJson.SchemaVersion`); a file carrying any other value is refused rather than mis-drawn. Every field the viewer reads is a field the simulation plan's `Tabletop.Sim.Recording` records already declare. Fields the viewer needs and the schema does not have are listed under *Interfaces needed from other modules* and are **not** invented locally. The schema declares `RecordedZone.Polygons`, `RecordedMarkerState`'s `Kind`, `Diameter` and `Label`, and `RecordedFrame`'s `Scores` and `Passed`, so nothing in this plan reads a field through a cast or with a fallback.
- **Frames are whole state, not deltas** (spec → *The recording is the artifact*): "Given frames, the viewer interpolates nothing and decides nothing; it draws what it is handed." A scrub to frame 300 reads frame 300 and nothing else. No accumulation, no interpolation, no tweening.
- **What is not built.** No command, no Ask answer, no editing, no pausing a simulation, no interactive control of any kind, and no auth beyond a spectator session JWT. The viewer sends exactly one frame ever — `Connect` — and after that it only receives. The seam is left, per spec → *Interactive debugging is a door, not a room*: `Feed` has a `control` member that is `null` in both implementations, and the open Ask is displayed at every frame. Building on that seam "makes the viewer a second thin client, which is a larger surface than a renderer" and is out of scope.
- **Top-down and schematic. No 3D, no mesh, ever** (spec → *`viewer/` is a developer web app*): "a model is its base and its facing." `z` is read only to label a model that is standing on something.
- One world unit = one inch. The table frame is the corner frame: origin at a table corner, `0 ≤ x ≤ width`, `0 ≤ y ≤ length`, `z` up; yaw 0 faces `+y` and positive yaw turns counter-clockwise seen from above (contracts plan → *Interfaces → Produces → `Tabletop.Geometry`*; geometry plan → *Coordinate frame*). Canvas `y` grows downward, so the view flips `y` once, in `transform.ts`, and nowhere else.
- **The renderer has one input shape.** `RecordedBoard` + `RecordedFrame`. The live feed's job is to produce them; nothing downstream of `Feed` knows which feed it is attached to.
- **The TypeScript protocol types are generated, not written.** Spec → *Two feeds*: "the TypeScript types are generated from the C# records rather than hand-written, and the applier is checked against the C# one by replaying a recording through both and comparing projections. A hand-maintained second applier was rejected." Generated output is checked in; `tests/Tabletop.Viewer.Tests` regenerates and diffs, so `dotnet test` fails on drift and `npm run build` needs no .NET SDK. The generated surface is only what the live feed reads — no `Command`, no `Edit`, no `Presence`, no `Ping`, no `AtRest`.
- **An unrecognised wire kind is ignored, never a fault.** `SocketMessage` grows: it is thirteen kinds, `at_rest` among them. The live feed reads seven of them, tests membership against one `HANDLED` set, and returns from its `switch`'s `default` arm. A kind the viewer does not act on costs it one `Set.has` and nothing else, so the next kind the protocol grows needs no edit here.
- **The applier is checked against the C# one by whole-frame comparison.** The recording's frames *are* the C# `EventApplier`'s output, canonicalised; so replaying the same event log through the TypeScript applier must reproduce them field for field. The comparison normalises collection order (sort by id, sort string arrays) on both sides and deep-compares everything else, including `line`.
- **Nothing is imported from `web/`** and nothing under `viewer/` is referenced by `Tabletop.sln`. `viewer/` has its own `package.json` and its own lockfile.
- TypeScript strict, `noUncheckedIndexedAccess`. Vitest covers every module under `src/lib/**` with logic — the recording reader, the frame-to-geometry projection, the timeline reducer, the Ask description, and the ported applier (spec → *Testing*). Svelte components are checked by looking at them.
- Ports: viewer dev server `5174` (`web/` owns `5173`), proxying `/v1` to management on `5100`; the game server socket is `ws://localhost:5200/ws`.
- Commit after every task, one imperative line, with the `Co-Authored-By` trailer naming the model.
- The last task edits `CLAUDE.md` and `docs/architecture.md`. **The simulation plan (`2026-09-06-simulation/README.md`, Task 11) edits both files first and already writes `viewer/`'s one line into each**; this plan's Task 12 is written against the text as that task leaves it, and says so at every edit.

---

## File Structure

```
viewer/
  package.json  package-lock.json  vite.config.ts  tsconfig.json  svelte.config.js  .npmrc  .gitignore
  index.html                                   the whole page
  src/main.ts                                  mounts App
  src/app.css                                  the two-pane layout and the dark scheme
  src/App.svelte                               shell: source picker, canvas, side panel, timeline
  src/lib/protocol/recording.generated.ts      GENERATED — the recording schema, from sim/Recording/Recording.cs
  src/lib/protocol/wire.generated.ts           GENERATED — the Protocol subset the live feed reads
  src/lib/recording/read.ts                    parse, gate on schemaVersion, validate structure
  src/lib/recording/source.ts                  load from a File, from a URL, from ?recording=
  src/lib/board/transform.ts                   inches <-> pixels: fit, zoom, pan, the single y flip
  src/lib/board/palette.ts                     colour by team ordinal, by declaration, by marker kind
  src/lib/board/frame.ts                       (RecordedBoard, RecordedFrame) -> Scene, pure
  src/lib/board/draw.ts                        Scene -> canvas
  src/lib/board/pick.ts                        canvas point -> the model or terrain under it
  src/lib/timeline/timeline.ts                 the reducer: play, pause, step, scrub, jump
  src/lib/asks/describe.ts                     RecordedAsk -> a heading, an addressee line, option lines
  src/lib/feed/feed.ts                         the Feed interface and the control-channel seam
  src/lib/feed/recordingFeed.ts                a whole recording, already framed
  src/lib/feed/liveFeed.ts                     the socket, the connect sequence, frames as they arrive
  src/lib/live/apply.ts                        EventApplier, ported
  src/lib/live/project.ts                      FrameProjector, ported
  src/lib/live/logLines.ts                     LogLines, ported
  src/lib/live/management.ts                   POST /v1/auth/password, POST /v1/auth/session/refresh
  src/lib/components/Board.svelte              the canvas, its resize observer and its pointer handling
  src/lib/components/Timeline.svelte           play, pause, step, scrub
  src/lib/components/LogPanel.svelte           one row per frame; clicking a row jumps
  src/lib/components/AskPanel.svelte           the open Ask and who it addresses
  src/lib/components/RollPanel.svelte          the frame's dice, raw and modified
  src/lib/components/SourcePicker.svelte       file, URL, or connect to a live game
  src/lib/components/Scoreboard.svelte         the round, each team's score, the active and passed teams
  tests/smoke.test.ts  tests/read.test.ts  tests/frame.test.ts  tests/transform.test.ts
  tests/timeline.test.ts  tests/describe.test.ts  tests/pick.test.ts  tests/feed.test.ts
  tests/apply.test.ts  tests/agreement.test.ts  tests/live.test.ts
  tests/normalise.ts  tests/fixture.ts             helpers, not test files
  tests/fixtures/                              written by tests/Tabletop.Viewer.Tests, checked in

tests/Tabletop.Viewer.Tests/
  Tabletop.Viewer.Tests.csproj                 references libs/Tabletop.Protocol and sim/Tabletop.Sim
  RepoPaths.cs                                 finds the repo root from the test assembly
  TypeScriptEmitter.cs                         JsonTypeInfo -> TypeScript, one file per root set
  GeneratedTypesTests.cs                       the golden-file guard; writes under TABLETOP_WRITE_GENERATED=1
  FixtureWriter.cs                             a run -> recording.json + events.json + welcome.json + board.json
  AgreementFixtureTests.cs                     writes viewer/tests/fixtures, asserts it round-trips in C#
```

Two things the structure locks in. **`src/lib/live/` is the only place that knows `Tabletop.Protocol` exists** — everything else speaks the recording schema, which is why the renderer cannot drift between feeds. And **the emitter lives in a test project, not in `viewer/`**: `viewer/` stays a pure npm project with no .NET dependency, no new top-level directory is created, and the drift guard runs inside the `dotnet test` every plan already ends with. That is this plan's answer to the spec's open item *"Whether generating TypeScript types from the C# records is worth a build step, or whether a checked-in generated file is enough"* — checked in, guarded by a test rather than a build step.

## Tasks

Each task is its own file, in build order.

1. [The project — Vite, Svelte 5, Vitest, and the dev proxy](task-01-the-project-vite-svelte-5-vitest.md)
2. [The TypeScript types, generated from the C# and checked in](task-02-the-typescript-types-generated-from-the.md)
3. [The recording reader — the schema gate and the structural checks](task-03-the-recording-reader-the-schema-gate.md)
4. [The frame-to-geometry projection — inches to shapes, and the view transform](task-04-the-frame-to-geometry-projection-inches.md)
5. [The board — a 2D canvas, and picking what is under the pointer](task-05-the-board-a-2d-canvas-and.md)
6. [The timeline — play, pause, step, scrub, and jump from a log line](task-06-the-timeline-play-pause-step-scrub.md)
7. [The open Ask, who it addresses, and the frame's dice](task-07-the-open-ask-who-it-addresses.md)
8. [The feed abstraction, the recording feed, and the whole page](task-08-the-feed-abstraction-the-recording-feed.md)
9. [The event applier, the frame projector and the log lines, ported to TypeScript](task-09-the-event-applier-the-frame-projector.md)
10. [The agreement test — one recording through both appliers](task-10-the-agreement-test-one-recording-through.md)
11. [The live feed — a spectator socket to the game server](task-11-the-live-feed-a-spectator-socket.md)
12. [`CLAUDE.md` and `docs/architecture.md`](task-12-claude-md-and-docs-architecture-md.md)

---

## The recording schema this plan consumes

The schema is `sim/Recording/Recording.cs`; the C# type is `Tabletop.Sim.Recording.Recording`, written qualified from any other namespace under `Tabletop.Sim`. **Nothing here is this plan's to change.** `IRL<T>` is `IReadOnlyList<T>` and `IRD<K,V>` is `IReadOnlyDictionary<K,V>`; `schemaVersion` is `RecordingJson.SchemaVersion`, currently `1`. Every field is camelCase on the wire, nulls omitted. Member order below is the C# records' positional order, which is also the order the generated TypeScript declares.

| Type | Members |
| --- | --- |
| `Recording` | `int Version` (serialised `schemaVersion`), `RecordingMeta Meta`, `RecordedBoard Board`, `IRL<RecordedFrame> Frames` |
| `RecordingMeta` | `string Scenario`, `ulong Seed`, `string Driver`, `string GameRules`, `string PropertiesHash`, `string SchedulerShape`, `string Enforcement`, `int? RoundCap`, `IRL<RecordedTeam> Teams`, `string Outcome` (`decided`, `round_limit`, `stuck`, `failed` for a finished run; `live` for a feed still running, which only the live feed sets), `string? OutcomeDetail` (set for `stuck` and `failed`), `Guid? WinningTeamId`, `int Rounds`, `long LastSeq`, `DateTimeOffset StartedAt`, `double DurationMs` |
| `RecordedTeam` | `Guid Id`, `int Ordinal`, `string Name`, `IRL<string> Participants` |
| `RecordedBoard` | `double Width`, `double Length`, `string Texture`, `IRL<RecordedTerrain> Terrain`, `IRL<RecordedMarker> Markers`, `IRL<RecordedZone> Zones`, `IRL<RecordedModel> Models` |
| `RecordedTerrain` | `Guid Id`, `string Mesh`, `double X`, `double Y`, `double Z`, `double Yaw`, `double Scale`, `IRL<RecordedVolume> Volumes` |
| `RecordedVolume` | `Guid Id`, `string Kind` (`solid`/`area`), `string Footprint` (`circle`: A = radius; `rect`: A = width, B = length), `double A`, `double B`, `double OffsetX`, `double OffsetY`, `double Yaw`, `double Bottom`, `double Top`, `IRL<string> Declarations` (the closed six, snake_case), `string? Notes`. **Warning: `A` is a *radius* on a `circle` footprint, which is why `volumeOutline` does not halve it — the opposite of `RecordedModel.BaseA`.** |
| `RecordedMarker` | `Guid Id`, `string Kind` (`objective`/`token`), `double Diameter`, `string? Label` |
| `RecordedZone` | `Guid Id`, `string Shape`, `int TeamOrdinal`, `IRL<IRL<RecordedPoint>> Polygons` — the shape id already resolved by `ZoneStyles.Resolve`, one ring per polygon, `{x, y}` in table inches and in the corner frame; `[]` for a shape id the resolver does not know |
| `RecordedPoint` | `double X`, `double Y` — a sim-owned type, so the JSON is exactly `{x, y}`; the schema names no Geometry type |
| `RecordedModel` | `Guid Id`, `Guid UnitId`, `string UnitName`, `string Name`, `int Ordinal`, `Guid TeamId`, `int TeamOrdinal`, `string BaseShape` (`circle`/`oval`/`rect`), `double BaseA`, `double BaseB`, `double Height`. **Warning: `BaseA` and `BaseB` are *diameters*, which is why `baseOutline` halves them — the opposite of `RecordedVolume.A`.** |
| `RecordedFrame` | `int Index`, `long Seq`, `string Kind`, `Guid? Actor`, `string? Warning`, `int Round`, `string? Phase`, `Guid? ActiveTeamId`, `string Line`, `IRL<RecordedModelState> Models`, `IRL<RecordedUnitState> Units`, `IRL<RecordedMarkerState> Markers`, `IRL<RecordedAsk> OpenAsks`, `IRL<RecordedRoll> Rolls`, `IRD<Guid,int> Scores`, `IRL<Guid> Passed` — the last two from `Projection.Scores` and `Projection.Scheduler.Passed`, whole rather than deltas like everything else on a frame |
| `RecordedModelState` | `Guid Id`, `string Where` (`table`/`shelf`/`inside`), `double? X`, `double? Y`, `double? Z`, `double? Yaw`, `Guid? ContainerId`, `int Wounds`, `int SpellTokens`, `IRL<string> Conditions` |
| `RecordedUnitState` | `Guid Id`, `IRL<string> Actions`, `IRL<string> Conditions`, `IRL<string> Grants`, `int WoundsThisActivation` |
| `RecordedMarkerState` | `Guid Id`, `string Kind`, `double Diameter`, `string? Label`, `double X`, `double Y`, `double Z`, `double Yaw` — in that order; a marker's definition rides along per frame, because one placed during play is in no board block |
| `RecordedAsk` | `Guid Id`, `string Question`, `string Prompt`, `IRL<Guid> Teams`, `bool AllowPass`, `bool Sealed`, `string? WindowKind`, `Guid? FrameId`, `string LegalKind` (`options`, `pieces`, `allocation`, `assignment`, `move_region`, `token_spend`, `confirmation`), `IRL<Guid> Answered`, `IRL<RecordedAskOption> Options`, `IRL<Guid> Pieces` (a `token_spend`'s spender models among them), `double? Allowance` (a `token_spend`'s total maximum among them) |
| `RecordedAskOption` | `string Id`, `string Label`, `IRL<Guid> Pieces` |
| `RecordedRoll` | `Guid Id`, `string Roll`, `int? Threshold`, `int Net`, `IRL<int> Raw`, `IRL<int> Modified`, `IRL<bool> Success`, `IRL<RecordedModifier> Modifiers` |
| `RecordedModifier` | `string Source`, `int Amount` |

Frames are whole state, never deltas. `RecordedBoard` and every `Recorded*` type carries the fields above and no others. `Scores` is the schema's only dictionary; its `Guid` keys serialise as canonical strings because neither option set sets a dictionary key policy, so `frame.scores[team.id]` matches `RecordedTeam.Id` verbatim. The viewer reads nothing the schema does not declare and no field through a cast; *Interfaces needed from other modules*, items 1–3, say how `RecordedZone.Polygons`, `Kind`/`Diameter`/`Label` on `RecordedMarkerState`, and `Scores` and `Passed` on `RecordedFrame` are read.

---

## Interfaces → Produces

Names later work copies verbatim. `RO<T>` abbreviates `readonly T[]`.

### `viewer/src/lib/protocol/` — generated, checked in

| Module | Exports |
| --- | --- |
| `recording.generated.ts` | `SCHEMA_VERSION`, and one `interface` per recording type above (`Recording`, `RecordingMeta`, `RecordedTeam`, `RecordedBoard`, `RecordedTerrain`, `RecordedVolume`, `RecordedMarker`, `RecordedZone`, `RecordedPoint`, `RecordedModel`, `RecordedFrame`, `RecordedModelState`, `RecordedUnitState`, `RecordedMarkerState`, `RecordedAsk`, `RecordedAskOption`, `RecordedRoll`, `RecordedModifier`), declared in name order with each interface's members in the C# positional order |
| `wire.generated.ts` | `SocketMessage = Connect \| Welcome \| GameInfoMessage \| BoardMessage \| EventsMessage \| Refused \| Rejected`, and the transitive closure of those seven: `GameInfo`, `Team`, `Participant`, `Board`, `ArmyInPlay`, `ArmySnapshot`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`, `TableState`, `TerrainPlacement`, `TerrainVolume`, `Volume`, `Footprint`, `Base`, `Marker`, `DeploymentZone`, `VisualRef`, `Point2`, `Point3`, `Yaw`, `TableSize`, `Placement`, `GameEvent`, `EventPayload` (every kind), `StateChange` (13 kinds), `Location` (3), `Ask`, `AskOption`, `LegalSet` (7), `AskAnswer` (8), `MoveContainment`, `ModelRelocation`, `Subject`, `Grant`, `Die`, `RollModifier`, `RollRecord`, `SequenceContext`, `TeamAnswer`, `DieReplacement`, and the enums. **No `Command`, `Edit`, `Presence`, `Ping`, `PresenceEnded`, `AtRest`, `Cursor`, `Selection`, `PresencePlacement` or `Measurement`.** The wire has thirteen `SocketMessage` kinds; this module declares seven, and `liveFeed.ts`'s `HANDLED` set is what decides which arrive. |

### `tests/Tabletop.Viewer.Tests`

| Type | Members |
| --- | --- |
| `static class RepoPaths` | `string Root`, `string Viewer`, `string Generated`, `string Fixtures` |
| `sealed record EmitRequest(string Module, JsonSerializerOptions Options, IReadOnlyList<Type> Roots, IReadOnlyDictionary<Type, IReadOnlyList<Type>> PrunedUnions, IReadOnlyDictionary<Type, string> Rename)` | |
| `static class TypeScriptEmitter` | `string Emit(EmitRequest)` |
| `sealed record Fixture(string Name, Recording Recording, IReadOnlyList<GameEvent> Log, GameInfo Game, Board Board, Guid RecorderParticipantId)` | |
| `static class FixtureWriter` | `string[] FixtureNames`, `Task<Fixture> RunAsync(Scenario, CancellationToken)`, `Task WriteAsync(Fixture, string directory, CancellationToken)` |
| `GeneratedTypesTests`, `AgreementFixtureTests` | the two golden guards; `TABLETOP_WRITE_GENERATED=1` and `TABLETOP_WRITE_FIXTURES=1` rewrite |

### `viewer/src/lib/`

| Module | Exports |
| --- | --- |
| `recording/read.ts` | `class RecordingError`, `readRecording(unknown): Recording`, `parseRecording(string): Recording`, `teamOrdinals(Recording)`, `teamNames(Recording)` |
| `recording/source.ts` | `type RecordingSource`, `fromFile(File)`, `fromUrl(string)`, `recordingUrlFrom(string): string \| null` |
| `board/transform.ts` | `type Point`, `type Table`, `type Viewport`, `type View`, `MARGIN`, `fit`, `toScreen`, `toTable`, `zoomAt`, `panBy`, `gridStep`, `rotate` |
| `board/palette.ts` | `TEAM_COLOURS`, `teamColour(number)`, `DECLARATIONS`, `declarationColour`, `terrainStroke`, `markerColour` |
| `board/frame.ts` | `type Shape`, `OVAL_VERTICES`, `type ModelShape`, `type TerrainShape`, `type MarkerShape`, `type ZoneShape`, `type OffTable`, `type Scene`, `sceneOf(RecordedBoard, RecordedFrame): Scene`, `baseOutline`, `volumeOutline` |
| `board/pick.ts` | `type Hit`, `contains(Shape, Point)`, `pickAt(Scene, Point): Hit \| null` |
| `board/draw.ts` | `type DrawOptions`, `draw(ctx, Scene, View, Viewport, DrawOptions)` |
| `timeline/timeline.ts` | `type Timeline`, `EMPTY`, `type TimelineAction`, `reduce`, `indexOfSeq`, `frameOf` |
| `asks/describe.ts` | `type Addressee`, `type AskView`, `describeAsk`, `describeLegal`, `humanise` |
| `feed/feed.ts` | `type FeedStatus`, `type FeedState`, `IDLE`, `type ControlChannel`, `interface Feed`, `class Emitter`, `detachFeed(Feed \| null, (() => void) \| null)`, `attachFeed(Feed \| null, (() => void) \| null, Feed, listener): () => void` |
| `feed/recordingFeed.ts` | `recordingFeed(RecordingSource): Feed` |
| `feed/liveFeed.ts` | `CLIENT_VERSION`, `type SocketLike`, `type LiveOptions`, `liveFeed(LiveOptions, open?): Feed`, `isServerFrame` |
| `live/apply.ts` | `type UnitInPlay`, `type ModelInPlay`, `type SchedulerPosition`, `type OpenAsk`, `type Projection`, `class ProjectionDesyncError` (`kind`, `unknownId`), `initialProjection`, `applyChange`, `applyTo`, `effectiveLog`, `class Applier` |
| `live/logLines.ts` | `lineOf(GameEvent, Projection, ReadonlyMap<string,number>): string`, `shortId` |
| `live/project.ts` | `declarationsOf(string)`, `recordedBoardOf(Board, GameInfo): RecordedBoard`, `class FrameProjector` |
| `live/management.ts` | `class ManagementError`, `type Session`, `logIn(email, password)`, `refreshSession(jwt)` |
| `components/` | `Board.svelte`, `Timeline.svelte`, `LogPanel.svelte`, `AskPanel.svelte`, `RollPanel.svelte`, `SourcePicker.svelte`, `Scoreboard.svelte` |

### What is deliberately not built

No command, no Ask answer, no editing of anything, no pausing or resuming a run, no interactive control, and no auth beyond one spectator session JWT. The viewer sends exactly one frame in its whole life — `Connect` — and `Feed.control` is declared and `null` in both implementations. There is no 3D view, no mesh loading, no WebGL, no drawing library, no router, no SvelteKit, no service worker and no persistence: reloading the page loses the loaded recording, which is correct for a file the developer has on disk.

---
## Interfaces needed from other modules

Everything else is consumed by its exact name. Each item below names the exact signature assumed and what the plan does until it lands. Items 1, 2 and 3 record the shape of the three recording fields the viewer depends on most and how each task reads them.

1. **Simulation plan, `RecordedZone.Polygons`.** `IReadOnlyList<IReadOnlyList<RecordedPoint>> Polygons`, wire name `polygons`, an array of rings of `{x, y}` in table inches, filled in `FrameProjector.BuildBoard` by `ZoneStyles.Resolve(z.Shape, table.Size)` and flattened to each `Polygon.Vertices` (simulation plan → *Interfaces → Produces → `Tabletop.Sim.Recording`*). The rings are already in the corner frame, so nothing here converts. The reason the recording carries them: resolving `frontline/1` is `Tabletop.Geometry.ZoneStyles`, which is C#, and OPR's frontline is an absolute twelve-inch inset rather than a fraction of the table, so it cannot be recovered from the id and the size, and porting `ZoneStyles` would be a second geometry implementation in the browser. Task 4's `sceneOf` reads `zone.polygons` directly, with no cast and no `?? []`, and its test pins the resolved ring. One branch exists — an **empty** ring list, which `ZoneStyles.Resolve` answers for a shape id it does not know and which Task 9's `recordedBoardOf` writes unconditionally, since the live feed has no resolver. An empty list draws no outline and is not an error; Task 4's test pins that too, and Task 10's `normaliseBoard` drops `polygons` before comparing the two boards for exactly this reason.
2. **Simulation plan, `RecordedMarkerState`'s `Kind`, `Diameter` and `Label`.** The fields sit on the state, positionally `Id, Kind, Diameter, Label, X, Y, Z, Yaw`, rather than in a board block covering every marker, because the live feed receives the `board` frame before any event and so cannot know a marker not yet placed. `RecordedBoard.Markers` is the *setup* markers; where both name one marker they agree. `sceneOf` builds every marker from `frame.markers` alone and never consults `board.markers` (Task 4), and Task 5's `draw.ts` draws every marker's ring the same way; `MarkerShape` carries no `known` flag.
3. **Simulation plan, `RecordedFrame`'s `Scores` and `Passed`.** `IReadOnlyDictionary<Guid, int> Scores` from `Projection.Scores` and `IReadOnlyList<Guid> Passed` from `Projection.Scheduler.Passed`, both copied rather than aliased and both whole rather than deltas. The viewer shows both in `Scoreboard.svelte` in the header rather than in a tab: the score is the number a person watching wants continuously, and the passed teams are a scheduler fact that belongs beside the round and the active team (Task 8). Task 9's ported `FrameProjector.add` writes both so the live feed produces the same frames, and Task 10's agreement test compares them field for field, sorting `passed` for the same reason it sorts `answered`.
4. **Simulation plan, public surface** — `RunLoop.BuildRecording(RunReport)`, `RunLoop.Log`, `InProcessDriver.Info`, `InProcessDriver.Board`, `CompiledScenario.RecorderParticipantId`, `ScenarioCatalog.Get(string)`, `ScenarioCompiler.Compile(Scenario)`, `Scenario` as a `record` supporting `with { RoundCap = 2 }`, `Chooser(ulong)`, `HandPlacement.ChooserFor(ulong)` (the one placement-chooser derivation, `RunLoop`'s fourth constructor argument), `AnswerPolicy(Chooser, PolicyOptions?)`, `ComposedAnswers(AnswerScript, AnswerPolicy?)`, `RunSettings()` (whose four members are `MaxSteps`, `FrameDumpOnFailure`, `CheckInvariants` and `Record`; the default records), `RecordingJson.{Options, SchemaVersion, Serialize, Deserialize, WriteAsync, ReadAsync}` and every `Recorded*` record must all be `public` — `tests/Tabletop.Viewer.Tests` references `sim/Tabletop.Sim.csproj` and constructs a run to write its fixtures. `sim/` never references this test project, so there is no cycle.
5. **Contracts plan, `Tabletop.Protocol`** — `WireJson.Options` must be `public` (it is), and no reachable pair of types in the `SocketMessage` closure may share a `Name`, because the emitter writes one TypeScript declaration per name. The one candidate is `Placement`: the contracts plan declares `readonly record struct Placement` in `Tabletop.Geometry`, and the simulation plan's Task 5 test writes `new Tabletop.Protocol.Placement(...)`. If both exist, the emitter throws `Two types are both called Placement` and the fix is one entry in `EmitRequest.Rename`; if only the Geometry one exists, nothing is needed. Assumed: only the Geometry one, and the simulation plan's qualifier is a slip.
6. **Contracts plan, `Tabletop.Protocol.ProtocolVersion.Current`** — the viewer hard-codes `CLIENT_VERSION = 1` rather than reading it, because `Connect.ClientVersion` is an `int` on the wire and generating a constant for one number is not worth a second emitter mode. If `ProtocolVersion.Current` ever moves past 1, `viewer/src/lib/feed/liveFeed.ts` must be edited or the server answers `rejected: upgrade_required`. Assumed acceptable for a developer tool; a generated constant is the alternative. **The thirteenth `SocketMessage` kind, `at_rest`, does not move it:** the protocol version is `1`, because adding a kind is additive and a client that ignores the kind stays compatible, so `CLIENT_VERSION = 1` holds. Only the *number* is untyped by the wire: the constant is declared `Connect['clientVersion']` and the frame is built as a `Connect` from `wire.generated.ts`, so the shape of the one message the viewer sends is checked against the C# record like every message it reads.
7. **Management plan** — `POST /v1/auth/password` and `POST /v1/auth/session/refresh` both answer a body with a `sessionJwt` string property. The viewer reads that one property and ignores the rest of `SessionResponse`, so its `Account` shape is not depended on. `TABLETOP_WEB_URL` needs no companion for the viewer: development goes through Vite's `/v1` proxy, so it is same-origin and CORS never applies. A viewer served from another origin in future would need its origin added to management's CORS policy.
8. **Contracts plan, `AtRest` and `ProjectionDesyncException`.** `AtRest(long Seq)`, wire kind `at_rest`, server to client, is `SocketMessage`'s thirteenth kind. The viewer prunes it out of `wire.generated.ts` and ignores it on the socket, so all it needs is for the kind to exist: the pruned member list in `GeneratedTypesTests.WireModule` names the seven it keeps, and a kind it does not name cannot break it. If `at_rest` has a different name or is absent, nothing in this plan moves. `ProjectionDesyncException`, thrown by `EventApplier` and carrying the event kind and the unknown id, is different: Task 9 ports it as `ProjectionDesyncError` and Task 10's `The_csharp_applier_faults_on_an_id_the_projection_does_not_hold` names the C# type in an `Assert.Throws`, so a different type name is one edit there, and the applier *not* throwing would make the port wrong rather than the test wrong.

## Rulings

1. **No headless browser test; the rendering is checked by looking at it** ([closing the open questions](../../specs/2026-09-09-open-questions-design.md)), as the spec says (*Testing*: "The rendering is checked by looking at it"), so Vitest covers the pure modules and no Playwright or jsdom appears. That is also forced: `docs/handoff.md` records that headless Chrome cannot launch in this WSL image (missing `libnspr4`). If the rendering should be pinned, the cheapest version is a Node canvas shim producing a PNG per fixture frame and a byte comparison, which adds a native dependency to `viewer/` and roughly one task; nothing else in the plan moves.
2. **Two checked-in fixtures, at a two-round cap** ([closing the open questions](../../specs/2026-09-09-open-questions-design.md)): `skirmish` and `terrain`, capped at two rounds, eight JSON files, regenerated deliberately. A recording runs 5 to 6 MB for an eight-round game, so about 0.7 MB per round, and a two-round recording is on the order of 1.5 MB. Four scenarios uncapped would be tens of megabytes of checked-in JSON. The `casting` scenario is generated on demand, not committed. If the fixtures were instead generated on demand and never committed, `AgreementFixtureTests` would always write them, the agreement test would need a .NET SDK to have run first, and `npm test` alone would stop being a complete check — which is why they are committed.
3. **The viewer does not fetch a recording from the game server or management; it loads from disk or a URL** ([closing the open questions](../../specs/2026-09-09-open-questions-design.md)). A recording is a `sim/` artifact on the developer's own filesystem, and neither server has any concept of one. `?recording=<url>` covers a static file server, which is all a shared recording needs. If recordings should be served, that is an endpoint on somebody's API and a new module concern rather than a viewer change.
4. **The viewer does not refuse a recording whose `meta.gameRules` or `meta.propertiesHash` it has never seen** ([closing the open questions](../../specs/2026-09-09-open-questions-design.md)). It draws whatever the schema version admits and shows both values in the meta panel, because the viewer holds no ruleset and nothing it draws depends on one. Only `schemaVersion` gates the file. A ruleset check would need a list of known rulesets from somewhere, which the viewer has no source for.
5. **A two-pane layout with a tabbed side panel** ([closing the open questions](../../specs/2026-09-09-open-questions-design.md)): board on the left, one of log / Ask / dice / meta on the right, timeline across the bottom, because the log is the panel in constant use and the other three are consulted at a specific frame. Two things sit in the header, outside the tabs, because they are wanted continuously: the source picker and `Scoreboard.svelte`, which carries the round, each team's score, and which team is active or has passed. If all four tabs should instead be stacked and visible, `App.svelte`'s right pane becomes a scrolling column and nothing else changes.

---

## Self-review

**Spec coverage.** Every `viewer/` sentence in `docs/superpowers/specs/2026-09-06-simulation-design.md` maps to a task. *`viewer/` is a developer web app, separate from `web/`* → Tasks 1 and 12 (own directory, own `package.json`, no SvelteKit, nothing shared with `web/`). *Top-down and schematic … a grid in inches, a circle per model coloured by team, terrain as outlines carrying their declarations, deployment zones* → Tasks 4 and 5; the zones are drawn from the rings the recording carries, resolved C#-side rather than guessed here. *a timeline with play, step and scrub. The log sits beside the board and clicking a line jumps to that sequence* → Task 6. *Nothing is rendered in 3D and no mesh is ever loaded* → a Global Constraint and Task 5's canvas argument. *Two feeds* → Tasks 8 (recording) and 11 (live). *the TypeScript types are generated from the C# records rather than hand-written* → Task 2. *the applier is checked against the C# one by replaying a recording through both and comparing projections* → Tasks 9 and 10. *Interactive debugging is a door, not a room* → the empty `ControlChannel` in Task 8, the Ask display in Task 7, and the *What is deliberately not built* section. *Testing* — "the recording reader, the frame-to-geometry projection, the timeline reducer, and the generated applier against a recording" → Tasks 3, 4, 6 and 10, one Vitest file each. The prompt's *Also required* — a plain statement of what is not built, and the two document edits — are the *What is deliberately not built* section and Task 12. The `sim/` half of the spec is another plan's and is untouched.

**Placeholders.** None, and no fallbacks. Every step carries the code or the exact command it needs. `sceneOf` reads `zone.polygons`, `marker.kind`, `marker.diameter` and `marker.label` as declared fields (*Interfaces needed*, items 1–3), and no cast to an optional property appears anywhere in the plan. One assumption is left open and named inline: whether `Tabletop.Protocol.Placement` exists alongside the Geometry one (*Interfaces needed*, item 5). The generated `.ts` files are the one thing not quoted in full, because they are generated; Task 2's tests assert their exact contents instead — including `RecordedMarkerState`'s field order, since order is part of a generated file — and Step 4 prints them.

**Type consistency.** `Scene`'s six members are built in Task 4 and consumed unchanged in Tasks 5 and 8. `Hit` is produced by `pickAt` in Task 5 and consumed by `Board.svelte` and `App.svelte` with the same two shapes. `FeedState`'s six fields are declared in Task 8 and set by both feeds in Tasks 8 and 11; `Feed.control` is `null` in both. `Timeline`'s five fields and the eleven `TimelineAction` kinds are used identically in Task 6's component and Task 8's `App.svelte` — including `{ type: 'jump', seq, seqs }`, which `App.svelte` builds from `state.frames.map(f => f.seq)`. `Projection` is defined in Task 9's `apply.ts` and consumed by `logLines.ts` and `project.ts` in the same task with the same member names. `RecordedFrame`'s sixteen fields are read in Task 4 (`sceneOf`), Task 6 (`LogPanel`), Task 7 (`AskPanel`, `RollPanel`), Task 8 (`Scoreboard` reads `round`, `activeTeamId`, `scores`, `passed`), Task 9 (`FrameProjector.add` writes all of them) and Task 10 (`normaliseFrame` sorts five of them), and no field is spelled two ways. `MarkerShape` has five members, and Task 5's `draw.ts` reads exactly those. `loadFixture` is defined once, in Task 10's `viewer/tests/fixture.ts`, and used by Tasks 10 and 11. `ProjectionDesyncError` is declared once, in Task 9's `apply.ts`, thrown by four helpers there, imported by Task 10's agreement test and caught by name in Task 11's `drain`; no `!` on a `Map.get` remains in `apply.ts` for it to bypass.
