# The simulator (`sim/`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A fourth deliverable, `sim/`, that plays a Grimdark Future game from deployment to a decided end without the Godot client — scripted, seeded, or both — against either an in-process session or a real socket, asserting the invariants that hold for any game and writing one recording file per run.

**Architecture:** One `IGameDriver` interface over two backends: `InProcessDriver` hydrates a `GameSession` over `InMemoryEventLog` and `InMemoryPropertiesStore` with an `ISessionClient` that records frames, and `SocketDriver` opens a real WebSocket to a running game server with a session JWT and a Postgres log. Above the driver sit an answer source (a script keyed by question and ordinal, falling through to a seeded policy that chooses uniformly from the whole legal set), a run loop that terminates on decided / round cap / stuck, the five invariant checks, and a recording writer that emits a **projection recording** — one frame per event, full state per frame.

**Tech Stack:** `net10.0`, C# 12, xUnit, `System.Text.Json`, Npgsql (through the game server's own types), `System.Net.WebSockets.ClientWebSocket`.

**Spec:** [`docs/superpowers/specs/2026-09-06-simulation-design.md`](../../specs/2026-09-06-simulation-design.md) — the whole document. `viewer/` is a separate plan's; nothing here builds it.

**Requires landed:** the contracts, geometry, game-server skeleton, interpreter and scheduler plans (a game cannot be played without both rules modules).

## Global Constraints

- `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`, `RootNamespace Tabletop.Sim`. Matches every other project (contracts plan → Global Constraints).
- **`sim/` contains no second implementation of the rules.** It may read anything the engine exposes — the projection, positions, stats, wounds, conditions, teams, the pinned `PropertySet` — and the invariants, the expectations and the recording all do. Two prohibitions bound that reading (spec → *Two ways to answer an Ask*):
  1. **Never narrow the legal set.** An answer is chosen from what the server offered, whole. A policy that filtered the set to answers it judged sensible would stop exercising the ones that find bugs.
  2. **Never compute a rules outcome the engine should compute.** No deciding here whether a target is in range, whether a wound lands, or what a modifier totals. A second rules implementation inside the simulator can agree with a broken engine and hide the fault.
  `Tabletop.Geometry` is a library rather than a ruleset, so calling `Movement.Check` to see whether a destination is inside an allowance the *server* stated is not a rules outcome; deciding what an allowance ought to be would be.
- **The default policy stays uniform over the legal set and ignorant of state**, for a different reason than the prohibitions: a cleverer policy explores a narrower, more plausible region and finds fewer bugs. A state-reading policy is opt-in, named as such, and never the default for a soak run (spec → *Two ways to answer an Ask*).
- **`sim/` is a deliverable, not a test project** (spec → `sim/` is a fourth deliverable). It references `server/game/Tabletop.GameServer.csproj` and the three `libs/`; it references **no test project**, because `tests/Tabletop.GameServer.Tests` and `tests/Tabletop.Sim.Tests` reference it.
- Nothing outside `sim/` and `tests/Tabletop.Sim.Tests` is scaffolded, except the two documentation edits in Task 11 (`CLAUDE.md`, `docs/architecture.md`).
- Names owned by earlier plans are consumed verbatim and never redefined: `Tabletop.Protocol` and `Tabletop.Content` (contracts plan → Interfaces → Produces), `GameSession`, `SessionServices`, `Hydrator`, `IEventLog`, `InMemoryEventLog`, `Replay`, `CommandDispatcher`, `IGameRows`, `OwnedArmy`, `Actor`, `GameState`, `ISessionClient`, `GameServerOptions`, `EventFilter` (game server skeleton plan), `RulesEngineFactory`, `IPropertiesStore`, `InMemoryPropertiesStore`, `PropertiesSeeder`, `PropertiesJson`, `IDiceSource`, `DiceSource` (interpreter plan), `SchedulerFactory` (scheduler plan).
- One world unit = one inch; yaw in degrees; the table frame is the corner frame — origin at a corner, `0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up (geometry values design → *The frame*).
- Identifiers are `Guid.CreateVersion7()`, exactly as everywhere else. `sim/` has no id exception, because the RNG seed does not travel through the game id: **the seed is its own stored value** on the game row, so ids are free to be random (`docs/schema.md` → `game.rng_seed`; see *Reproducibility* below).
- **A run sets the seed explicitly.** `Scenario.Seed` is the run's seed and it reaches the engine as `GameInfo.RngSeed`, the value the management-owned `game.rng_seed` column holds. `ScenarioCompiler` puts it in the `GameInfo` it builds; `SocketRowSeeder` writes it into the `management.game` insert, because a socket run makes a row rather than a `GameInfo`. There is one seed per game, written once, and `sim/` invents no second mechanism.
- **Reproducibility means: the same scenario with the same seed gives the same dice and the same answers. Ids differ between runs.** The dice, because `GameState.RngSeed` is the stored seed and the *k*th die of a game is a pure function of that seed and *k* (interpreter plan Task 5). The answers, because one `Chooser` seeded from the same value is advanced in Ask order over legal sets the engine offers in its own ordinal order. Nothing asserts id equality across two runs, and nothing compares two recordings byte for byte; what is compared is the outcome kind, the round count, the sequence and frame counts, the answer history and the dice.
- **The recording is a projection recording, not an event log** (spec → The recording is the artifact). It is derived and disposable, is never replayed into the engine, and the event log stays the only thing undo and hydration read. Say so in the header comment of every recording type.
- The recording schema is version `1` in a `version` field. The spec leaves versioning undecided; the field costs one integer and a viewer that reads it can refuse a file it does not understand.
- JSON: camelCase, string enums as plain strings, nulls omitted, indented. `sim/` owns `RecordingJson.Options`; it does **not** reuse `WireJson.Options`, because the recording is not a wire format. The one thing it does borrow from `WireJson` is the *spelling of a Protocol enum* — `WireJson.Serialize(value).Trim('"')`, in `RunLoop.BuildRecording` and `SocketRowSeeder` — so that `alternating-activation` is spelled once in the tree and not twice.
- Enforcement in a scenario defaults to `Enforcement.Strict`, which is the opposite of every fixture's default and is the point: a policy answer outside the legal set must be refused loudly rather than logged and accepted.
- Commit after every task, imperative one-line subject, ending with the `Co-Authored-By` trailer naming the
  model that produced it, e.g. `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

## File Structure

```
sim/Tabletop.Sim.csproj                  net10.0, RootNamespace Tabletop.Sim; OutputType Exe
sim/Chooser.cs                           SplitMix64 chooser: Next, Double, Shuffle, Pick, Subset
sim/Scenarios/Scenario.cs                the record, ScenarioBuilder, ScenarioArmy, ScenarioTerrain
sim/Scenarios/ScenarioCompiler.cs        Scenario -> GameInfo + Board + PropertySet hash
sim/Scenarios/ScenarioCatalog.cs         the named scenarios, by name
sim/Scenarios/Expectation.cs             the expectation records and their evaluation
sim/Answers/PolicyView.cs                the only state an answer may read
sim/Answers/AnswerPolicy.cs              one legal-set shape at a time, from a seed
sim/Answers/AnswerScript.cs              answers keyed by (question, ordinal)
sim/Answers/IAnswerSource.cs             ComposedAnswers: script, then policy, or fail
sim/Drivers/IGameDriver.cs               the interface, SubmitResult, StepResult, DriverMode
sim/Drivers/ScenarioRows.cs              IGameRows over a compiled scenario, in memory
sim/Drivers/RecorderClient.cs            ISessionClient that queues frames
sim/Drivers/InProcessDriver.cs           GameSession + InMemoryEventLog + InMemoryPropertiesStore
sim/Drivers/SocketDriver.cs              ClientWebSocket (or an injected connector) to a real server
sim/Drivers/IWebSocketConnector.cs       ClientWebSocketConnector
sim/Drivers/SocketRowSeeder.cs           the three management rows in raw SQL, then IGameRows writes
sim/Run/RunOutcome.cs                    Decided | RoundLimit | Stuck | Failed | Paused, and StuckReason
sim/Run/AskFingerprint.cs                what "reopens unchanged" means
sim/Run/HandPlacement.cs                 deployment's models into the team's zone, by MoveModels
sim/Run/RunLoop.cs                       drive until decided, capped, stuck or failed
sim/Run/RunReport.cs                     one run's result, for the summary and the exit code
sim/Invariants/ProjectionComparer.cs     structural difference between two projections
sim/Invariants/Invariants.cs             the five checks; Check(all) and the mid-run entry points
sim/Recording/Recording.cs               the schema, as records
sim/Recording/RecordingJson.cs           the JsonSerializerOptions, Write, Read
sim/Recording/FrameProjector.cs          Projection + GameEvent -> RecordedFrame
sim/Recording/LogLines.cs                one line per event kind, printed not interpreted
sim/SimOptions.cs                        the argument surface
sim/Summary.cs                           the run table and summary.json
sim/Program.cs                           the console entry point
tests/Tabletop.Sim.Tests/
  Tabletop.Sim.Tests.csproj              references sim/ and tests/Tabletop.GameServer.Tests
  PostgresCollection.cs                  the local [CollectionDefinition] over the imported fixture
  ChooserTests.cs  AnswerPolicyTests.cs  MoveRegionPolicyTests.cs  AnswerScriptTests.cs
  ScenarioTests.cs  InProcessDriverTests.cs  RecordingTests.cs  RunLoopTests.cs
  InvariantTests.cs  DriverAgreementTests.cs  StepAndWaitTests.cs  ProgramTests.cs
```

**Why `tests/Tabletop.Sim.Tests` and not the game server's test project.** The spec makes `sim/` the harness the interpreter's and scheduler's full-game tests use, so `tests/Tabletop.GameServer.Tests` will reference `sim/`; `sim/` therefore cannot reference it back, and the full-game tests written against this harness live in `tests/Tabletop.Sim.Tests`. `tests/Tabletop.Sim.Tests` **does** reference `tests/Tabletop.GameServer.Tests`, for `PostgresFixture`, `GameServerFactory` and `TestKeys` — a test-to-test reference, which is why `PostgresCollection.cs` re-declares the collection locally (xUnit resolves `[CollectionDefinition]` in the test assembly only, while the fixture type may be imported).

**What `sim/` builds instead of the fixtures.** `TestProperties`, `TestArmies`, `TestGames`, `Sim` and `SchedulerHarness` all live in `tests/Tabletop.GameServer.Tests` and are out of reach for the reason above. `sim/` reaches the same content through the production path: `PropertiesSeeder.EmbeddedDocuments()` + `PropertiesJson.ToSet` for the core Grimdark Future `PropertySet` (the same embedded `grimdark-future-core.properties.json` that `TestProperties.Core` reads), and `ScenarioArmy` for armies. Recorded as *Interfaces needed from other modules* item 1.

## Tasks

Each task is its own file, in build order.

1. [The project, the chooser, and the scenario](task-01-the-project-the-chooser-and-the.md)
2. [The answer policy — every legal-set shape, from the shape alone](task-02-the-answer-policy-every-legal-set.md)
3. [The script, and the composition that makes a mixed run](task-03-the-script-and-the-composition-that.md)
4. [`IGameDriver`, and the in-process driver](task-04-igamedriver-and-the-in-process-driver.md)
5. [The recording — the schema, the writer, and one frame per event](task-05-the-recording-the-schema-the-writer.md)
6. [The run loop — decided, round-capped, stuck, or failed](task-06-the-run-loop-decided-round-capped.md)
7. [The five invariants](task-07-the-five-invariants.md)
8. [The socket driver, and the two drivers agree](task-08-the-socket-driver-and-the-two.md)
9. [The step-and-wait seam](task-09-the-step-and-wait-seam.md)
10. [The console entry point](task-10-the-console-entry-point.md)
11. [`CLAUDE.md`](task-11-claude-md.md)

---

## Interfaces → Produces

Later plans — the viewer's above all — copy these names verbatim. `IRL<T>` abbreviates `IReadOnlyList<T>`, `IRD<K,V>` abbreviates `IReadOnlyDictionary<K,V>`.

**Every type and member in these tables is `public`**, so another project can reference `sim/Tabletop.Sim.csproj` and drive a run: `tests/Tabletop.Viewer.Tests` does exactly that to write its fixtures (viewer plan → *Interfaces needed from other modules*, item 4). Nothing is `internal` and nothing needs `InternalsVisibleTo`. **`sim/` references no test project**, here or anywhere, so a test project referencing `sim/` is not a cycle (spec → *Testing*).

### `Tabletop.Sim` (root)

| Type | Members |
| --- | --- |
| `sealed class Chooser(ulong seed)` | `int Next(int bound)`, `double Double()`, `bool Chance(double)`, `T Pick<T>(IRL<T>)`, `IRL<T> Shuffle<T>(IRL<T>)`, `IRL<T> Subset<T>(IRL<T>, int)`, `long Drawn` |
| `sealed record SimOptions(string? Scenario, ulong FromSeed, ulong ToSeed, string Driver, string OutputDirectory, int? RoundCap, bool Recording, bool List)` | `static Parse(string[])`, `static string Usage`, `IEnumerable<ulong> Seeds`, `const InProcess = "in-process"`, `const Socket = "socket"`; `sealed class UsageException` |
| `sealed record Summary(string Driver, int Runs, int Passed, int Failed, double TotalMs, IRL<RunReport> Reports)` | `static Of(string, IRL<RunReport>)`, `string Table()` |
| `static class Program` | `Task<int> Main(string[])`, `Task<int> RunAsync(SimOptions, TextWriter, CancellationToken)` |

### `Tabletop.Sim.Scenarios`

| Type | Members |
| --- | --- |
| `sealed record Scenario(string Name, ulong Seed, SchedulerShape Shape, Enforcement Enforcement, int RoundCap, TableSize Table, IRL<ScenarioArmy> Armies, IRL<ScenarioTerrain> Terrain, IRL<ScenarioMarker> Markers, IRL<EntryDocument> Spells, IRL<string> ZoneShapes, AnswerScript Script, bool ScriptOnly, IRL<Expectation> Expectations)` | `Scenario WithSeed(ulong)` |
| `sealed record ScenarioArmy(string Name, int Units, int ModelsPerUnit, IRL<ScenarioRule> UnitRules, IRL<ScenarioRule> ModelRules, IRL<ScenarioWeapon> Weapons, IRD<string,string> UnitStats)`; `ScenarioRule(int Unit, string Name, int? Parameter)`; `ScenarioWeapon(string Name, IRD<string,string> Stats, IRL<ScenarioRule> Rules)`; `ScenarioTerrain(string Mesh, Point3 Position, double Yaw, double Scale, IRL<Volume> Volumes)`; `ScenarioMarker(MarkerKind Kind, Point3 Position, double Diameter, string? Label)` | |
| `sealed class ScenarioBuilder(string name, ulong seed)` | `Shape`, `Enforcing`, `RoundCap`, `Table`, `Zones`, `Army`, `Terrain`, `Marker`, `Scripted`, `Expect`, `Build` |
| `sealed record CompiledScenario(Scenario, GameInfo Info, Board Board, PropertySet Properties, IRL<Guid> TeamIds, IRL<Guid> ParticipantIds, IRD<Guid,Guid> TeamOfParticipant, TableState Snapshot, Guid RecorderParticipantId)` | `Guid ParticipantFor(Guid teamId)` |
| `static class ScenarioCompiler` | `CompiledScenario Compile(Scenario)`, `PropertySet CoreProperties` |
| `static class ScenarioCatalog` | `IRL<string> Names`, `Scenario Get(string)`, `bool TryGet(string, out Scenario)` — `skirmish`, `terrain`, `player-turn`, `free-for-all`, `stalemate`, `casting` |
| `abstract record Expectation` | `EventOfKind(string, int)`, `NoEventOfKind(string)`, `ReachedRound(int)`, `TeamWiped(int)`, `ModelsRemaining(int, int)`, `AskAsked(string, int)`, `Outcome(string)` |
| `static class Expectations` | `IRL<string> Check(IRL<Expectation>, Projection, IRL<GameEvent>, RunOutcome, CompiledScenario)`, `int ModelsOnTable(Projection, CompiledScenario, int teamOrdinal)`, `int ModelsOnTable(Projection, CompiledScenario, Guid teamId)` |

### `Tabletop.Sim.Answers`

| Type | Members |
| --- | --- |
| `sealed class PolicyView(Projection, Board, IRL<WorldVolume>? volumes = null, GameInfo? info = null, PropertySet? properties = null)` | `Projection`, `Board`, `GameInfo? Info`, `PropertySet? Properties`, `TableSize Table`, `IRL<WorldVolume> Volumes`, `VolumesById`, `LocationOf`, `PlacedOf`, `BodyOf`, `SurfaceAt`, `UnitOf`, `ModelOf`, `TeamOf` — the whole readable state; `AnswerPolicy` uses only the geometry members |
| `sealed record PolicyOptions(double PassProbability = 0, double EmbarkProbability = 0.25, int MoveSamples = 24)` | |
| `sealed class AnswerPolicy(Chooser, PolicyOptions?)` | `AskAnswer Answer(Ask, PolicyView)`, `static IRL<string> ShapeNames`; `sealed class NoLegalAnswerException` — the default policy: uniform over the legal set, ignorant of state |
| `sealed record ScriptKey(string Question, int Ordinal)`; `sealed record AnswerScript(IRD<ScriptKey,AskAnswer>)` | `static Empty`, `static ScriptBuilder Build()`, `bool TryGet(string, int, out AskAnswer)`, `int Count` |
| `sealed class ScriptBuilder` | `At(string, int, AskAnswer)`, `Then(string, AskAnswer)`, `Build()` |
| `interface IAnswerSource` | `AskAnswer Answer(Ask, PolicyView)`, `IRL<AnsweredAsk> History`; `sealed record AnsweredAsk(string Question, int Ordinal, Guid AskId, AskAnswer Answer, bool FromScript)` — also the seam a state-reading policy would implement |
| `sealed class ComposedAnswers(AnswerScript, AnswerPolicy?) : IAnswerSource`; `sealed class UnscriptedAskException` | script first, then the policy; a null policy is script-only |

### `Tabletop.Sim.Drivers`

| Type | Members |
| --- | --- |
| `interface IGameDriver : IAsyncDisposable` | `string Name`, `Guid GameId`, `GameInfo Info`, `Board Board`, `IRL<Guid> TeamIds`, `Guid ParticipantFor(Guid)`, `DriverMode Mode`, `Task StartAsync`, `Task<IRL<OpenAsk>> OpenAsksAsync`, `Task<SubmitResult> SubmitAsync(Guid, Command, CancellationToken)`, `Task<IRL<GameEvent>> DrainEventsAsync`, `Task<Projection> ProjectionAsync`, `Task<StepResult> StepAsync`, default `Task<SubmitResult> AnswerAsync(Guid teamId, Guid askId, AskAnswer, CancellationToken)` |
| `abstract record SubmitResult` | `Accepted(string? Warning)`, `Rejected(RefusalReason, string Detail)` |
| `sealed record StepResult(IRL<GameEvent> Events, IRL<OpenAsk> OpenAsks, bool Quiescent)`; `enum DriverMode { Run, StepAndWait }` | `Quiescent` is true when the server rested and emitted nothing |
| `sealed class RestTimeoutException(TimeSpan timeout, long lastSeq) : Exception` | `TimeSpan Timeout`, `long LastSeq`; thrown by `SocketDriver` when `AtRest` does not arrive inside `RestTimeout`, and turned into `RunOutcome.Failed` by `RunLoop` |
| `sealed class ScenarioRows(CompiledScenario) : IGameRows` | the six reads; the seven writes throw `NotSupportedException` |
| `sealed class RecorderClient(Guid participantId) : ISessionClient` | `IRL<SocketMessage> Frames`, `IRL<SocketMessage> TakeFrames()`, `bool Closed` |
| `sealed class InProcessDriver : IGameDriver` | `static Task<InProcessDriver> StartAsync(CompiledScenario, CancellationToken)`, `GameSession Session`, `Task<Projection> ServerProjectionAsync(CancellationToken)`, `IRL<GameEvent> Log` |
| `interface IWebSocketConnector`; `sealed class ClientWebSocketConnector(Uri)` | `Task<WebSocket> ConnectAsync(CancellationToken)` |
| `sealed record MintedSession(string Jwt, Guid AccountId)`; `sealed record SocketCredentials(IRL<MintedSession> Sessions)` | `const ManagementUrlVariable = "TABLETOP_MANAGEMENT_URL"`, `const MintPath = "/v1/dev/sessions"`, `static MintAsync(CompiledScenario, HttpClient, CancellationToken)`, `static NameFor(CompiledScenario, int)`, `string For(int)`, `Guid AccountOf(int)` |
| `sealed class SocketRowSeeder(NpgsqlDataSource, IGameRows)`; `sealed class PropertiesMissingException` | `Task SeedAsync(CompiledScenario, SocketCredentials, CancellationToken)`, `Task DeleteAsync(Guid, CancellationToken)` |
| `sealed class SocketDriver : IGameDriver` | `static Task<SocketDriver> StartAsync(CompiledScenario, IWebSocketConnector, SocketCredentials, NpgsqlDataSource, CancellationToken)`, `TimeSpan RestTimeout` (30 seconds), `Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken)` |

### `Tabletop.Sim.Run`

| Type | Members |
| --- | --- |
| `enum StuckReason { NoAskNoEvents, AskReopenedUnchanged, SubmitRefused, NoLegalAnswer }` | |
| `abstract record RunOutcome` | `Decided(Guid WinningTeamId)`, `RoundLimit(int Rounds)`, `Stuck(StuckReason, string Detail)`, `Failed(string Detail)`, `Paused(Guid AskId, string Question)`, `string Kind` |
| `sealed record AskFingerprint(string Question, string Teams, string Legal, bool AllowPass, string? WindowKind)` | `static Of(Ask)` — the id is excluded, the legal set included |
| `static class HandPlacement` | `IRL<ModelRelocation> IntoZone(Projection, CompiledScenario, Guid teamId, Guid unitId, Chooser)` |
| `sealed record RunSettings(int MaxSteps = 20_000, int FrameDumpOnFailure = 12, bool CheckInvariants = true)` | |
| `sealed record RunReport(string Scenario, ulong Seed, string Driver, RunOutcome Outcome, int Rounds, long LastSeq, int Frames, int AsksAnswered, double DurationMs, IRL<string> Failures, string? RecordingPath)` | `bool Passed` |
| `sealed class RunLoop(IGameDriver, CompiledScenario, IAnswerSource, Chooser, RunSettings?)` | `Task<RunReport> RunAsync(CancellationToken)`, `Task<RunReport> ResumeAsync(CancellationToken)`, `Task<RunReport> AnswerPausedAsync(Guid teamId, AskAnswer, CancellationToken)`, `FrameProjector Projector`, `IRL<GameEvent> Log`, `IAnswerSource Answers`, `Recording BuildRecording(RunReport)`, `IRL<string> LastFrames(int)` |

### `Tabletop.Sim.Invariants`

| Type | Members |
| --- | --- |
| `static class ProjectionComparer` | `IRL<string> Differences(Projection expected, Projection actual)` |
| `sealed record InvariantResult(string Name, bool Held, string? Detail)`; `sealed class InvariantViolatedException` | `IRL<InvariantResult> Failures` |
| `static class Invariants` | `CheckAll`, `ReplayReproducesTheProjection`, `UndoAndRedoReproduceTheDice`, `EveryAskAnsweredByAnAddressedTeam`, `NoModelInTwoPlacesOrOffTheSlab`, `TheRoundAdvancedOnlyThroughTheBoundaryAsk` |
| `static class MidRun` | `void Assert(Projection, IRL<GameEvent>, CompiledScenario)` |

### `Tabletop.Sim.Recording` — the viewer's whole input

| Type | Members |
| --- | --- |
| `sealed record Recording(int Version, RecordingMeta Meta, RecordedBoard Board, IRL<RecordedFrame> Frames)` | `version` serialises as `schemaVersion` |
| `sealed record RecordingMeta(string Scenario, ulong Seed, string Driver, string GameRules, string PropertiesHash, string SchedulerShape, string Enforcement, int? RoundCap, IRL<RecordedTeam> Teams, string Outcome, string? OutcomeDetail, Guid? WinningTeamId, int Rounds, long LastSeq, DateTimeOffset StartedAt, double DurationMs)` | |
| `RecordedTeam(Guid Id, int Ordinal, string Name, IRL<string> Participants)` | |
| `RecordedBoard(double Width, double Length, string Texture, IRL<RecordedTerrain> Terrain, IRL<RecordedMarker> Markers, IRL<RecordedZone> Zones, IRL<RecordedModel> Models)` | |
| `RecordedTerrain(Guid Id, string Mesh, double X, double Y, double Z, double Yaw, double Scale, IRL<RecordedVolume> Volumes)` | |
| `RecordedVolume(Guid Id, string Kind, string Footprint, double A, double B, double OffsetX, double OffsetY, double Yaw, double Bottom, double Top, IRL<string> Declarations, string? Notes)` | |
| `RecordedMarker(Guid Id, string Kind, double Diameter, string? Label)`; `RecordedZone(Guid Id, string Shape, int TeamOrdinal, IRL<IRL<Point2>> Polygons)` | `polygons` is the shape already resolved by `ZoneStyles.Resolve`, one ring per polygon, `{x, y}` in table inches |
| `RecordedModel(Guid Id, Guid UnitId, string UnitName, string Name, int Ordinal, Guid TeamId, int TeamOrdinal, string BaseShape, double BaseA, double BaseB, double Height)` | |
| `RecordedFrame(int Index, long Seq, string Kind, Guid? Actor, string? Warning, int Round, string? Phase, Guid? ActiveTeamId, string Line, IRL<RecordedModelState> Models, IRL<RecordedUnitState> Units, IRL<RecordedMarkerState> Markers, IRL<RecordedAsk> OpenAsks, IRL<RecordedRoll> Rolls, IRD<Guid,int> Scores, IRL<Guid> Passed)` | `Scores` is `Projection.Scores` and `Passed` is `Projection.Scheduler.Passed`, both whole rather than deltas |
| `RecordedModelState(Guid Id, string Where, double? X, double? Y, double? Z, double? Yaw, Guid? ContainerId, int Wounds, int SpellTokens, IRL<string> Conditions)` | `Where` is `table`, `shelf` or `inside` |
| `RecordedUnitState(Guid Id, IRL<string> Actions, IRL<string> Conditions, IRL<string> Grants, int WoundsThisActivation)`; `RecordedMarkerState(Guid Id, string Kind, double Diameter, string? Label, double X, double Y, double Z, double Yaw)` | a marker's definition rides along per frame, because one placed during play is in no board block |
| `RecordedAsk(Guid Id, string Question, string Prompt, IRL<Guid> Teams, bool AllowPass, bool Sealed, string? WindowKind, Guid? FrameId, string LegalKind, IRL<Guid> Answered, IRL<RecordedAskOption> Options, IRL<Guid> Pieces, double? Allowance)`; `RecordedAskOption(string Id, string Label, IRL<Guid> Pieces)` | |
| `RecordedRoll(Guid Id, string Roll, int? Threshold, int Net, IRL<int> Raw, IRL<int> Modified, IRL<bool> Success, IRL<RecordedModifier> Modifiers)`; `RecordedModifier(string Source, int Amount)` | |
| `static class RecordingJson` | `const int SchemaVersion = 1`, `JsonSerializerOptions Options`, `WriteAsync`, `ReadAsync`, `Serialize`, `Deserialize` |
| `static class LogLines` | `string Of(GameEvent, Projection after, IRD<Guid,int> teamOrdinals)` |
| `sealed class FrameProjector(CompiledScenario)` | `RecordedBoard Board`, `RecordedFrame Add(GameEvent)`, `IRL<RecordedFrame> Frames`, `Projection Projection`, `Recording Build(RecordingMeta)` |

---
## Applied from the viewer plan

`docs/superpowers/plans/2026-09-06-viewer/README.md` reads this plan's recording schema and needs four things of it, plus one doc comment. All five are **in the tasks above**, because they came from a sibling plan and not from a spec: nothing a spec says changes, and the recording stays derived, so the whole cost of a schema change is a re-run.

1. **`RecordedZone` carries `Polygons`** (viewer plan → *Interfaces needed from other modules*, item 1). `IReadOnlyList<IReadOnlyList<Point2>>`, wire name `polygons`, one ring per polygon of `{x, y}` in table inches, filled in `FrameProjector.BuildBoard` by `ZoneStyles.Resolve(z.Shape, table.Size)` and flattened to each `Polygon.Vertices`. Recorded rather than left to the reader because the board block otherwise carries only a shape id such as `frontline/1`; resolving one is `Tabletop.Geometry.ZoneStyles`, which is C#, and OPR's frontline is an absolute twelve-inch inset rather than a fraction of the table, so it cannot be normalised out of the id and the size. Porting `ZoneStyles` would be a second geometry implementation in the browser. It costs a handful of points once per zone in the board block, not per frame. Task 5 Step 1's board test pins the ring's four vertices and the twelve inches, Step 5's `BuildBoard` calls the resolver, `Point2`/`Polygon`/`ZoneStyles` are in Task 5's Consumes list, and `Recording.cs` has `using Tabletop.Geometry;`. The viewer's fallback is `Scene.zones[i].polygons = []` and no outline drawn, which is also what an unresolvable shape id produces.

2. **`RecordedMarkerState` carries `Kind`, `Diameter` and `Label`** (item 2). `BuildBoard` reads `table.Markers`, so a token dropped during play by `marker_placed` would otherwise appear in a frame with a position and no definition at all, and the viewer's fallback is to draw it as a dashed half-inch ring labelled with the first eight characters of its id, with `MarkerShape.known` false. The alternative — having `BuildBoard` include every marker that appears in any frame rather than only the table's — is rejected on three counts. The live feed cannot implement it: it receives the `board` socket frame before any event, so a marker not yet placed is not knowable when the board is written, and the two feeds must produce the same shape. It would make `FrameProjector.Board` mutable and make the board block depend on how far the run got, so two runs of one scenario would carry different boards for the same table. And it would leave a frame that a reader has to join against a block which may not hold the row — a frame that is not whole state, which is the one thing the schema is for. Per-frame is also nearly free here: `Projection.Markers` holds `Marker` values that already carry kind, diameter and label, so it is three fields off a value `Add` already reads, and a game has single-digit markers — unlike a model's base, which is why models keep the definition/state split. `RecordedBoard.Markers` is the *setup* markers, which is what the board block describes; where both name the same marker they agree, and the frame is what a viewer draws from.

3. **`RecordedFrame` carries `Scores` and `Passed`** (item 3). `IReadOnlyDictionary<Guid, int> Scores` from `Projection.Scores`, `IReadOnlyList<Guid> Passed` from `Projection.Scheduler.Passed`; both copied rather than aliased, since the applier keeps mutating them and every frame is kept for the whole run, and both whole rather than deltas like everything else on a frame. The applier maintains both and `ProjectionComparer` compares both (Task 7); a frame carrying neither would leave a viewer unable to show the score or which team had passed. `RecordedFrame` has sixteen members, and Task 5 Step 1 has `A_frame_carries_the_score_and_who_has_passed`.

4. **The public surface `tests/Tabletop.Viewer.Tests` needs** (item 4). Every name on that list is `public`: `RunLoop.BuildRecording` (Task 6) and `RunLoop.Log` (Task 7), `InProcessDriver.Info` and `.Board` (Task 4), `CompiledScenario.RecorderParticipantId`, `ScenarioCatalog.Get`, `ScenarioCompiler.Compile`, `Scenario` — a `record`, so `with { RoundCap = 2 }` works — and `Chooser(ulong)` (Task 1), `AnswerPolicy(Chooser, PolicyOptions?)` (Task 2), `ComposedAnswers(AnswerScript, AnswerPolicy?)` (Task 3), `RunSettings()` (Task 6, every parameter defaulted), all six `RecordingJson` members and every `Recorded*` record (Task 5). *Interfaces → Produces* states it at the top, together with the fact that **`sim/` references no test project**, so a test project referencing `sim/` is not a cycle (spec → *Testing*).

5. **`RecordingMeta`'s `Outcome` doc comment** (the viewer plan asks it of this file rather than of a spec). It reads that `decided`, `round_limit`, `stuck` and `failed` are what a finished run writes, and that `live` is what `viewer/`'s live feed sets when it manufactures a meta from `GameInfo`; `sim/` writes only the first four. The field is a `string`, and the alternative — a null `meta` on a live feed — would strip the team names the Ask panel needs to say who an Ask addresses.

---
## Interfaces needed from other modules

Everything below is consumed by its exact name; nothing here is a request to rename or reshape.

1. **Interpreter plan** — `PropertiesSeeder.EmbeddedDocuments()` (`static IReadOnlyList<PropertySetDocument>`) and `PropertiesJson.ToSet(PropertySetDocument, Func<Guid>)` must both be `public`, because `ScenarioCompiler.CoreProperties` is how `sim/` gets the core Grimdark Future set without touching a test project. `PropertySetDocument.GameRules` is read to pick the document. Also `InMemoryPropertiesStore.Add(PropertySet)` returning `this`, `DiceSource()` parameterless, and `RulesEngineFactory(IPropertiesStore, IDiceSource, ISchedulerFactory)` public.
2. **Scheduler plan** — `SchedulerFactory()` parameterless and public, since `InProcessDriver` registers it directly rather than through DI. `AskQuestions.EndRound` is the boundary Ask's question, and invariant 5 reads it from `Tabletop.Protocol.AskQuestions` (contracts plan → `Asks.cs`).
3. **Game server skeleton plan** — `AtRest(long Seq)` at the end of `ConnectAsync` at every status and at the end of `HandleCommandAsync` whatever the outcome, with `Seq` the log's high-water mark (`Play?.Projection.LastSeq ?? 0`), broadcast when the batch appended at least one event and sent to the provoking client alone when it appended none, and a command's `Refused` sent before that command's `AtRest`. **This plan consumes it unchanged**; it is why `SocketDriver.SubmitAsync` reads the submitting socket's rest frame and why `StepAsync` waits on the recorder's only when the command appended. Also `GameSession.ConnectAsync(ISessionClient)`, `ReceiveAsync(ISessionClient, SocketMessage)`, `RunAsync<T>(Func<Task<T>>)`, `DrainAsync(string)` and the `Play` property public; `Hydrator.HydrateAsync`; `Replay.To(Board, GameInfo, IReadOnlyList<GameEvent>, long)`; `InMemoryEventLog()`; `GameServerOptions` constructible from object-initializer syntax with only `DbConnection`, `StytchProjectId` and `StytchJwksUrl` set (every other member defaulted); `PostgresGameRows(NpgsqlDataSource)`; and `PostgresFixture.CountAsync`, confirmed from that plan as `Task<int> CountAsync(string sql, params object[] args)` — positional parameters, so `DriverAgreementTests` passes the game id as `$1`, and an `int` return, so the one comparison against a `long` sequence casts. Nothing here asks for a change.
4. **Contracts plan** — `AtRest(long Seq)` as a `SocketMessage` kind, wire kind `at_rest`, server to client. It is `public sealed record AtRest(long Seq) : SocketMessage` with `[JsonDerivedType(typeof(AtRest), "at_rest")]`, the eighth of thirteen kinds; this plan consumes it and adds nothing to it. Also `EventApplier.EffectiveLog(IReadOnlyList<GameEvent>)` as a public static, which the undo invariant and invariant 4 both call; `Projection.Initial(Board, GameInfo)`; `WireJson.Serialize<T>(T)` accepting an enum, which is how `sim/` prints `alternating-activation` rather than inventing a second spelling. Also **`GameInfo.RngSeed`**, the stored RNG seed, together with the `management.game` column it comes from. That plan owns the name, the SQL type and the C# type; this one consumes its choice.

   **This plan follows it: `long RngSeed`, positional and last among `GameInfo`'s scalars — after `WaitsBetweenSteps`, before `Teams` and `Participants` — over a `management.game.rng_seed bigint not null` column with no default** (contracts plan → *Interfaces → Produces*; `docs/schema.md` → `rng_seed`). Positional on purpose, so a construction that forgets the seed fails to compile rather than silently getting `0`. `long` because `bigint` is signed, so `ScenarioCompiler` writes `unchecked((long)s.Seed)` from the scenario's `ulong Seed`; the reverse cast (`unchecked((ulong)info.RngSeed)`, where `GameState.RngSeed : ulong` is set) belongs to the interpreter plan, not this one. `SocketRowSeeder` passes `info.RngSeed` straight into the insert. The four places this plan spells the seed are `ScenarioCompiler.Compile`, `SocketRowSeeder.SeedAsync`, and the seed assertions in `ScenarioTests` and `RunLoopTests`.
   **Two shapes consumed as stated:** `TokenSpend(IReadOnlyDictionary<Guid,int> MaxByModel)` answered by `TokensSpent(IReadOnlyDictionary<Guid,int> Counts)`, wire kinds `token_spend` and `tokens_spent`, among seven legal-set kinds and eight answer kinds, so `AnswerPolicy.ShapeNames` names seven and `AnswerSpend` draws once per entry (p.14). And a Takedown's chosen model as a `PieceSelection` over model ids, which needs nothing at all here: `AnswerPieces` picks ids out of the list it is handed without asking what they name, and the deployment loop keys on the question name rather than the answer shape.

5. **Geometry plan** — `Movement.Check(PlacedModel, Point2, double, IReadOnlyList<WorldVolume>, IReadOnlyList<WorldVolume>, double?, TableSize?) → MoveCheck` with `WithinAllowance`, `CrossesImpassable` and `LeavesTable`; `ZoneStyles.ShapesOf(string)`, `ZoneStyles.Resolve(string, TableSize)`, `ZoneStyles.Contains(string, TableSize, Point2)`, and the style constants `Frontline` and `FreeForAll`; `Settling.SurfaceHeightAt(Point2, IReadOnlyList<WorldVolume>)`.

   `Resolve` is assumed to be `IReadOnlyList<Polygon> Resolve(string shapeId, TableSize)` with `sealed record Polygon(IReadOnlyList<Point2> Vertices)` (geometry design → *Interfaces → Produces*; contracts design → the `Polygon` row), answering in the corner frame, and to return no polygons for a shape id it does not know rather than throwing. `RecordedZone.Polygons` is that list flattened to `Vertices`, so if `Resolve` throws on an unknown id instead, `BuildBoard` wraps the call and records `[]`.
6. **The viewer plan** — consumes this plan's whole `Tabletop.Sim.Recording` table above as its input format, and `RecordingJson.SchemaVersion` as the number to check. It should generate its TypeScript types from these C# records, as the spec requires of the Protocol types, rather than hand-writing them.

   **It needs four things of the schema and one doc comment; all five are in this plan** — see *Applied from the viewer plan* above. Nothing else it asks for is a change: the public surface its test project references is public, and `sim/` references no test project.
7. **Management API plan** — `POST /v1/dev/sessions`, body `{"name": "..."}`, response `{"sessionJwt": "...", "account": {"id": "...", "name": "..."}}`, mapped only when `IHostEnvironment.IsDevelopment()`. `SocketCredentials.MintAsync` calls it once per participant. That plan's Task 5 provides it, together with the test that the route is absent outside a development environment. This plan needs nothing else from management: the seeder writes `management.account`, `game`, `team` and `participant` as raw SQL, and the mint's `on conflict do nothing` insert of the same account row is harmless.
8. **The interpreter and scheduler plans, later** — when their full-game tests are written they reference `sim/Tabletop.Sim.csproj` from `tests/Tabletop.Sim.Tests` and build a `Scenario`, an `InProcessDriver` and a `RunLoop`. `sim/` never references a test project (spec → *Testing*).

## Rulings

1. **A socket run gets its session JWTs from a development-only endpoint that mints one.** Hand-pasting is rejected. The endpoint is `POST /v1/dev/sessions` on the **management API**, not the game server, because management holds the Stytch project secret and already creates users and authenticates sessions; the game server holds no credential and could not mint anything real. It takes a name, finds or creates the Stytch user and the `account` row, and returns a real Stytch session JWT, so the socket driver stays on the production verification path. It is gated on `IHostEnvironment.IsDevelopment()` rather than on a configuration flag, and it is absent from the route table a production build serves. Task 8: `SocketCredentials.MintAsync` calls it, `AccountOf` reads the account id from the mint's response, `NameFor` makes the account names stable per scenario and participant, and `--driver socket` has no token variable. The cost is that a console socket run needs network access to the Stytch test project; the in-process driver and `DriverAgreementTests` need none. See the management API plan, Task 5, for the endpoint and its absence test.
2. **A `stuck` run fails the console's exit code** ([closing the open questions](../../specs/2026-09-09-open-questions-design.md)). A game that cannot finish is a bug in the rules engine, and CI is the only place anyone will run 500 seeds: `RunReport.Passed` is false for `stuck` and `failed`, so `dotnet run --project sim -- skirmish --seeds 1..500` exits `1` if any game hangs. The frames are dumped either way.
3. **`sim/` is in `Tabletop.sln`**, in a `sim` solution folder — the solution is *"at the repo root, spanning every project"* (`CLAUDE.md`; [closing the open questions](../../specs/2026-09-09-open-questions-design.md)) — so `dotnet build` at the root builds it and `dotnet test` runs its tests, which is what makes it part of the whole-tree check every plan ends with.
4. **The catalog ships six scenarios**: `skirmish`, `terrain`, `player-turn`, `free-for-all`, `stalemate` and `casting` — one per thing that could plausibly diverge (a plain game, terrain declarations, the second scheduler shape, three teams, a run that must hit the cap, and a spell cast). `casting` is there because casting is the only path with two player decisions a whole-game run would otherwise never open — the per-model token spend map (p.13), and the caster spending its own leftover tokens on its own roll — and the only sealed window a full game reaches, so nothing else exercises `TokenSpend`, the sealed reveal, or a window addressed to more than one team. Casting is not a mode: a spell is cast inside an ordinary activation, so the preset differs from `skirmish` only in fielding a `Caster(2)` and declaring a spell. The spell is the scenario's own, folded into the pinned `PropertySet` the way `TestProperties.WithSpells` does, because the shipped content document is every special rule on pages 13–15 and holds no spells — a spell list belongs to an army book this project has not designed ([closing the open questions](../../specs/2026-09-09-open-questions-design.md)). A regression scenario for a specific rules interaction is a script and is added the same way; the shape of the catalog does not change.
5. **The socket driver waits for a rest frame, not a quiet window.** The game server sends `AtRest(long Seq)` when it has finished processing an inbound `connect` or `command`, carrying the log's high-water mark, broadcast when the batch appended and sent to the provoking client alone when it did not. `SocketDriver.StepAsync` waits for that frame rather than for silence, and `RestTimeout` (30 seconds) is a guard on a hung server rather than a guess at quiescence; expiry is `RestTimeoutException`, which `RunLoop` turns into a `Failed` run. Task 8 builds it, and `DriverAgreementTests` compares the two drivers run for run. In process is exact by construction.

---

## Self-review

**Spec coverage.** Every section of `docs/superpowers/specs/2026-09-06-simulation-design.md` that concerns `sim/` maps to a task: *`sim/` is a fourth deliverable* → Tasks 1 and 11; *Two ways to answer an Ask* → Tasks 2 and 3; *Two drivers behind one interface* → Tasks 4 and 8; *The recording is the artifact* → Task 5; *Interactive debugging is a door* → Task 9; *What a full game means* → Task 6; *What it asserts* → Task 7; *Testing* (`sim/` half) → the tests in every task. The `viewer/` sections are another plan's and are untouched beyond the one line each in Task 11. The *Deliberately not decided* / *Still open* items that `sim/` had to settle — the recording schema and its version field, the dice in the recording, the round cap's home in the scenario, and the policy's preference to act — are settled in the spec rather than silently here. *What a full game means* tabulates all five outcomes, `Failed` and `Paused` included, so `RunOutcome` and the spec agree.

**Placeholders.** None. Every step carries the code or the exact command it needs. `PostgresFixture.CountAsync` is confirmed against the game server skeleton plan — `Task<int> CountAsync(string sql, params object[] args)`, positional parameters and an `int` return — so Task 8 casts its one `long` comparison rather than carrying an inline-SQL fallback. The one place where an earlier plan's signature could not be confirmed from its text, `Projection`'s team-ordinal lookup, names its fallback inline rather than leaving a gap.

**Termination is stated once.** The *Termination, exactly* table and `RunLoop.DriveAsync` give the round limit the same way — `Round >= RoundCap` with every open Ask the boundary Ask — which is also what `docs/design/simulation/design.md` records. `A_cap_of_two_stops_at_the_end_of_the_second_round` pins the boundary. Where a check is weaker than it looks it says so: `AskReopenedUnchanged` names `RunSettings.MaxSteps` as its backstop in the table, in the `Reopened` doc comment and in `RunSettings`' own.

**Type consistency.** `SubmitResult.Accepted`/`Rejected`, `RunOutcome.Decided`/`RoundLimit`/`Stuck`/`Failed`/`Paused`, `RecordedFrame`'s sixteen members, `PolicyView`'s five-parameter constructor (two positional, three optional) and `LogLines.Of`'s three-argument signature are used the same way in every task that touches them. `CompiledScenario` has `RecorderParticipantId` from Task 1 and is consumed with it in Tasks 4, 5, 6 and 8. `RunLoop` has `Log` from Task 7 and `Answers` from Task 8, both named where they are added.
