# The simulator (`sim/`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A fourth deliverable, `sim/`, that plays a Grimdark Future game from deployment to a decided end without the Godot client — scripted, seeded, or both — against either an in-process session or a real socket, asserting the invariants that hold for any game and writing one recording file per run.

**Architecture:** One `IGameDriver` interface over two backends: `InProcessDriver` hydrates a `GameSession` over `InMemoryEventLog` and `InMemoryPropertiesStore` with an `ISessionClient` that records frames, and `SocketDriver` opens a real WebSocket to a running game server with a session JWT and a Postgres log. Above the driver sit an answer source (a script keyed by question and ordinal, falling through to a seeded policy that chooses uniformly from the whole legal set), a run loop that terminates on decided / round cap / stuck, the five invariant checks, and a recording writer that emits a **projection recording** — one frame per event, full state per frame.

**Tech Stack:** `net10.0`, C# 12, xUnit, `System.Text.Json`, Npgsql (through the game server's own types), `System.Net.WebSockets.ClientWebSocket`.

**Spec:** [`docs/superpowers/specs/2026-09-06-simulation-design.md`](../specs/2026-09-06-simulation-design.md) — the whole document. `viewer/` is a separate plan's; nothing here builds it.

## Global Constraints

- `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`, `RootNamespace Tabletop.Sim`. Matches every other project (contracts plan → Global Constraints).
- **`sim/` contains no second implementation of the rules.** It may read anything the engine exposes — the projection, positions, stats, wounds, conditions, teams, the pinned `PropertySet` — and the invariants, the expectations and the recording all do. Two prohibitions replace what used to be a blanket ban on reading state (owner ruling, 2026-09-06; spec → *Two ways to answer an Ask*):
  1. **Never narrow the legal set.** An answer is chosen from what the server offered, whole. A policy that filtered the set to answers it judged sensible would stop exercising the ones that find bugs.
  2. **Never compute a rules outcome the engine should compute.** No deciding here whether a target is in range, whether a wound lands, or what a modifier totals. A second rules implementation inside the simulator can agree with a broken engine and hide the fault.
  `Tabletop.Geometry` is a library rather than a ruleset, so calling `Movement.Check` to see whether a destination is inside an allowance the *server* stated is not a rules outcome; deciding what an allowance ought to be would be.
- **The default policy stays uniform over the legal set and ignorant of state**, for a different reason than the prohibitions: a cleverer policy explores a narrower, more plausible region and finds fewer bugs. A state-reading policy is opt-in, named as such, and never the default for a soak run (owner ruling, 2026-09-06).
- **`sim/` is a deliverable, not a test project** (spec → `sim/` is a fourth deliverable, ruled by the project owner 2026-09-06). It references `server/game/Tabletop.GameServer.csproj` and the three `libs/`; it references **no test project**, because `tests/Tabletop.GameServer.Tests` and `tests/Tabletop.Sim.Tests` reference it.
- Nothing outside `sim/` and `tests/Tabletop.Sim.Tests` is scaffolded, except the two documentation edits in Task 11 (`CLAUDE.md`, `docs/architecture.md`).
- Names owned by earlier plans are consumed verbatim and never redefined: `Tabletop.Protocol` and `Tabletop.Content` (contracts plan → Interfaces → Produces), `GameSession`, `SessionServices`, `Hydrator`, `IEventLog`, `InMemoryEventLog`, `Replay`, `CommandDispatcher`, `IGameRows`, `OwnedArmy`, `Actor`, `GameState`, `ISessionClient`, `GameServerOptions`, `EventFilter` (game server skeleton plan), `RulesEngineFactory`, `IPropertiesStore`, `InMemoryPropertiesStore`, `PropertiesSeeder`, `PropertiesJson`, `IDiceSource`, `DiceSource` (interpreter plan), `SchedulerFactory` (scheduler plan).
- One world unit = one inch; yaw in degrees; the table frame is the corner frame — origin at a corner, `0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up (game server skeleton plan → spec correction 3).
- Identifiers are `Guid.CreateVersion7()`, exactly as everywhere else. `sim/` has no id exception, because the RNG seed no longer travels through the game id: **the seed is its own stored value** on the game row, so ids are free to be random (owner ruling, 2026-09-06 — see *Reproducibility* below).
- **A run sets the seed explicitly.** `Scenario.Seed` is the run's seed and it reaches the engine as `GameInfo.RngSeed`, the value the management-owned `game.rng_seed` column holds. `ScenarioCompiler` puts it in the `GameInfo` it builds; `SocketRowSeeder` writes it into the `management.game` insert, because a socket run makes a row rather than a `GameInfo`. There is one seed per game, written once, and `sim/` invents no second mechanism.
- **Reproducibility means: the same scenario with the same seed gives the same dice and the same answers. Ids differ between runs.** The dice, because `GameState.RngSeed` is the stored seed and the *k*th die of a game is a pure function of that seed and *k* (interpreter plan Task 5). The answers, because one `Chooser` seeded from the same value is advanced in Ask order over legal sets the engine offers in its own ordinal order. Nothing asserts id equality across two runs, and nothing compares two recordings byte for byte; what is compared is the outcome kind, the round count, the sequence and frame counts, the answer history and the dice.
- **The recording is a projection recording, not an event log** (spec → The recording is the artifact). It is derived and disposable, is never replayed into the engine, and the event log stays the only thing undo and hydration read. Say so in the header comment of every recording type.
- The recording schema is version `1` in a `version` field. The spec leaves versioning undecided; the field costs one integer and a viewer that reads it can refuse a file it does not understand.
- JSON: camelCase, string enums as plain strings, nulls omitted, indented. `sim/` owns `RecordingJson.Options`; it does **not** reuse `WireJson.Options`, because the recording is not a wire format. The one thing it does borrow from `WireJson` is the *spelling of a Protocol enum* — `WireJson.Serialize(value).Trim('"')`, in `RunLoop.BuildRecording` and `SocketRowSeeder` — so that `alternating-activation` is spelled once in the tree and not twice.
- Enforcement in a scenario defaults to `Enforcement.Strict`, which is the opposite of every fixture's default and is the point: a policy answer outside the legal set must be refused loudly rather than logged and accepted.
- Commit after every task, imperative one-line subject, ending with the two trailers the session requires:
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` and `Claude-Session: <the session URL>`.

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

---
### Task 1: The project, the chooser, and the scenario

**Files:**
- Create: `sim/Tabletop.Sim.csproj`, `sim/Chooser.cs`, `sim/Scenarios/Scenario.cs`, `sim/Scenarios/ScenarioCompiler.cs`, `sim/Scenarios/ScenarioCatalog.cs`
- Create: `tests/Tabletop.Sim.Tests/Tabletop.Sim.Tests.csproj`, `tests/Tabletop.Sim.Tests/PostgresCollection.cs`
- Test: `tests/Tabletop.Sim.Tests/ChooserTests.cs`, `tests/Tabletop.Sim.Tests/ScenarioTests.cs`
- Modify: `Tabletop.sln`

**Interfaces:**
- Consumes: `Army`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`, `ArmySnapshot`, `TableState`, `TerrainPlacement`, `TerrainVolume`, `Marker`, `MarkerKind`, `DeploymentZone`, `VisualRef` (Content); `Base`, `TableSize`, `Point3`, `Yaw`, `Volume`, `VolumeKind`, `Footprint`, `Point2`, `TerrainDeclarations` (Geometry); `GameInfo` (including its `RngSeed` member — see *Interfaces needed* item 4), `GameStatus`, `SchedulerShape`, `Enforcement`, `Team`, `Participant`, `Board`, `ArmyInPlay` (Protocol); `PropertiesSeeder.EmbeddedDocuments()`, `PropertiesJson.Hash`, `PropertiesJson.ToSet`, `PropertySet` (game server `Tabletop.GameServer.Properties`); `GrimdarkFutureRules.RulesId` (`Tabletop.GameServer.Rules.GrimdarkFuture`).
- Produces: `sealed class Chooser(ulong seed)` with `int Next(int bound)`, `double Double()`, `bool Chance(double p)`, `T Pick<T>(IReadOnlyList<T> items)`, `IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T>)`, `IReadOnlyList<T> Subset<T>(IReadOnlyList<T> items, int count)`, `long Drawn`; `sealed record Scenario(string Name, ulong Seed, SchedulerShape Shape, Enforcement Enforcement, int RoundCap, TableSize Table, IReadOnlyList<ScenarioArmy> Armies, IReadOnlyList<ScenarioTerrain> Terrain, IReadOnlyList<ScenarioMarker> Markers, IReadOnlyList<string> ZoneShapes, AnswerScript Script, bool ScriptOnly, IReadOnlyList<Expectation> Expectations)` with `Scenario WithSeed(ulong)`; `sealed record ScenarioArmy(string Name, int Units, int ModelsPerUnit, IReadOnlyList<ScenarioRule> UnitRules, IReadOnlyList<ScenarioRule> ModelRules, IReadOnlyList<ScenarioWeapon> Weapons, IReadOnlyDictionary<string,string> UnitStats)`; `sealed record ScenarioRule(int Unit, string Name, int? Parameter)`; `sealed record ScenarioWeapon(string Name, IReadOnlyDictionary<string,string> Stats, IReadOnlyList<ScenarioRule> Rules)`; `sealed record ScenarioTerrain(string Mesh, Point3 Position, double Yaw, double Scale, IReadOnlyList<Volume> Volumes)`; `sealed record ScenarioMarker(MarkerKind Kind, Point3 Position, double Diameter, string? Label)`; `sealed class ScenarioBuilder` (the fluent form, `Build()`); `sealed record CompiledScenario(Scenario Scenario, GameInfo Info, Board Board, PropertySet Properties, IReadOnlyList<Guid> TeamIds, IReadOnlyList<Guid> ParticipantIds, IReadOnlyDictionary<Guid,Guid> TeamOfParticipant, TableState Snapshot, Guid RecorderParticipantId)` with `Guid ParticipantFor(Guid teamId)`; `static class ScenarioCompiler { CompiledScenario Compile(Scenario s); PropertySet CoreProperties { get; } }`; `static class ScenarioCatalog { IReadOnlyList<string> Names { get; } Scenario Get(string name); bool TryGet(string name, out Scenario) }`.

Spec: spec → *Deliberately not decided* ("Whether a scenario is C# or a text format. C# costs nothing to start and keeps the assertions typed") — so a scenario is C#, a record with a fluent builder. Spec → *`sim/` is a fourth deliverable*. `ScenarioCompiler.CoreProperties` is the production path to the same content `TestProperties.Core` reads (File Structure above).

- [ ] **Step 1: Write `sim/Tabletop.Sim.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <RootNamespace>Tabletop.Sim</RootNamespace>
    <AssemblyName>Tabletop.Sim</AssemblyName>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../server/game/Tabletop.GameServer.csproj" />
  </ItemGroup>
</Project>
```

`Tabletop.GameServer` already references the three `libs/`, Npgsql and the ASP.NET Core framework, so nothing else is added. `OutputType Exe` on a project that is also a library is deliberate: `sim/Program.cs` is the console entry point and `tests/Tabletop.Sim.Tests` references the same assembly.

- [ ] **Step 2: Write `tests/Tabletop.Sim.Tests/Tabletop.Sim.Tests.csproj` and `PostgresCollection.cs`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>Tabletop.Sim.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../sim/Tabletop.Sim.csproj" />
    <ProjectReference Include="../Tabletop.GameServer.Tests/Tabletop.GameServer.Tests.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

```csharp
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.Sim.Tests;

/// <summary>
/// xUnit resolves [CollectionDefinition] in the test assembly only, so the collection is declared here
/// over the fixture type imported from tests/Tabletop.GameServer.Tests.
/// </summary>
[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

- [ ] **Step 3: Write the failing tests**

```csharp
namespace Tabletop.Sim.Tests;

public class ChooserTests
{
    [Fact]
    public void Two_choosers_on_one_seed_make_the_same_choices()
    {
        var a = new Chooser(3); var b = new Chooser(3);
        for (var i = 0; i < 100; i++) Assert.Equal(a.Next(17), b.Next(17));
        Assert.Equal(100, a.Drawn);
    }

    [Fact]
    public void Next_stays_in_range_and_covers_it()
    {
        var c = new Chooser(1);
        var seen = new HashSet<int>();
        for (var i = 0; i < 500; i++) { var n = c.Next(4); Assert.InRange(n, 0, 3); seen.Add(n); }
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void Shuffle_and_subset_permute_without_losing_or_repeating()
    {
        var c = new Chooser(9);
        int[] items = [1, 2, 3, 4, 5];
        var shuffled = c.Shuffle(items);
        Assert.Equal([1, 2, 3, 4, 5], shuffled.Order());
        var three = c.Subset(items, 3);
        Assert.Equal(3, three.Count);
        Assert.Equal(3, three.Distinct().Count());
    }

    [Fact]
    public void Next_of_one_draws_nothing_and_next_of_zero_throws()
    {
        var c = new Chooser(1);
        Assert.Equal(0, c.Next(1));
        Assert.Equal(0, c.Drawn);
        Assert.Throws<ArgumentOutOfRangeException>(() => c.Next(0));
    }
}
```

- [ ] **Step 4: Run; expect compile failure**

Run: `dotnet test tests/Tabletop.Sim.Tests`
Expected: `CS0246: The type or namespace name 'Chooser' could not be found`.
- [ ] **Step 5: Write `sim/Chooser.cs`**

The `Chooser` is the whole of `sim/`'s determinism. Ids are `Guid.CreateVersion7()` and vary between
runs; the dice come from the seed the game row stores; the answers come from this one stream.

```csharp
namespace Tabletop.Sim;

/// <summary>
/// SplitMix64 over a counter. Deterministic for a seed and independent of the clock, the thread and
/// the machine. It chooses; it never judges — nothing here knows what a good choice is.
/// </summary>
public sealed class Chooser(ulong seed)
{
    private ulong _state = seed;
    public long Drawn { get; private set; }

    private ulong NextBits()
    {
        Drawn++;
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public int Next(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound), bound, "a choice needs at least one option");
        return bound == 1 ? 0 : (int)(NextBits() % (ulong)bound);
    }

    public double Double() => (NextBits() >> 11) * (1.0 / (1UL << 53));
    public bool Chance(double p) => p > 0 && Double() < p;
    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    public IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T> items)
    {
        var a = items.ToArray();
        for (var i = a.Length - 1; i > 0; i--) { var j = Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        return a;
    }

    public IReadOnlyList<T> Subset<T>(IReadOnlyList<T> items, int count) => Shuffle(items).Take(count).ToList();
}
```

- [ ] **Step 6: Write the failing scenario tests**

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class ScenarioTests
{
    [Fact]
    public void The_builder_produces_two_teams_one_participant_each_and_a_snapshot_per_team()
    {
        var s = new ScenarioBuilder("two-by-two", seed: 5)
            .Table(72, 48)
            .Army("A", units: 2, modelsPerUnit: 3)
            .Army("B", units: 2, modelsPerUnit: 3)
            .Build();
        var c = ScenarioCompiler.Compile(s);

        Assert.Equal(GameStatus.Playing, c.Info.Status);
        Assert.Equal(2, c.TeamIds.Count);
        Assert.Equal(2, c.ParticipantIds.Count);
        Assert.Equal(3, c.Info.Participants.Count);                                     // two players and the recorder
        Assert.Null(c.Info.Participants.Single(p => p.Id == c.RecorderParticipantId).TeamId);
        Assert.Equal([1, 2], c.Info.Teams.Select(t => t.Ordinal));
        Assert.Equal(2, c.Board.Armies.Count);
        Assert.All(c.Board.Armies, a => Assert.Equal(2, a.Snapshot.Units.Count));
        Assert.Equal(c.ParticipantFor(c.TeamIds[0]), c.Info.Participants[0].Id);
        Assert.Equal(new Tabletop.Geometry.TableSize(72, 48), c.Board.Table.Size);
    }

    [Fact]
    public void Compiling_twice_repeats_the_seed_and_not_the_ids()
    {
        var s = ScenarioCatalog.Get("skirmish");
        var a = ScenarioCompiler.Compile(s);
        var b = ScenarioCompiler.Compile(s);
        Assert.Equal(unchecked((long)s.Seed), a.Info.RngSeed);           // the seed is the scenario's, stored
        Assert.Equal(a.Info.RngSeed, b.Info.RngSeed);
        Assert.NotEqual(a.Info.Id, b.Info.Id);                           // ids are ordinary v7 GUIDs
        Assert.NotEqual(a.Board.Armies[0].Snapshot.Units[0].Models[0].Id,
                        b.Board.Armies[0].Snapshot.Units[0].Models[0].Id);
        Assert.NotEqual(a.Info.RngSeed, ScenarioCompiler.Compile(s.WithSeed(s.Seed + 1)).Info.RngSeed);
    }

    [Fact]
    public void The_properties_hash_is_the_embedded_core_set_and_the_rules_are_grimdark_future()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));
        Assert.Equal(ScenarioCompiler.CoreProperties.ContentHash, c.Info.PropertiesHash);
        Assert.Equal("grimdark-future", c.Info.GameRules);
        Assert.Contains("Hold", ScenarioCompiler.CoreProperties.Actions.Select(a => a.Name));
    }

    [Fact]
    public void Every_catalog_scenario_compiles()
    {
        Assert.NotEmpty(ScenarioCatalog.Names);
        foreach (var name in ScenarioCatalog.Names) Assert.NotNull(ScenarioCompiler.Compile(ScenarioCatalog.Get(name)).Board);
        Assert.Throws<KeyNotFoundException>(() => ScenarioCatalog.Get("no-such-scenario"));
    }

    [Fact]
    public void A_scenario_defaults_to_strict_so_an_out_of_set_answer_is_refused() =>
        Assert.Equal(Enforcement.Strict, new ScenarioBuilder("d", 1).Army("A", 1, 1).Army("B", 1, 1).Build().Enforcement);
}
```

- [ ] **Step 7: Run; expect failure, then write `Scenario.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter ScenarioTests`
Expected: `CS0246: ... 'ScenarioBuilder' could not be found`.

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Scenarios;

public sealed record ScenarioRule(int Unit, string Name, int? Parameter = null);
public sealed record ScenarioWeapon(string Name, IReadOnlyDictionary<string, string> Stats, IReadOnlyList<ScenarioRule> Rules);
public sealed record ScenarioTerrain(string Mesh, Point3 Position, double Yaw, double Scale, IReadOnlyList<Volume> Volumes);
public sealed record ScenarioMarker(MarkerKind Kind, Point3 Position, double Diameter, string? Label);

/// <summary>One army per team, in team-ordinal order. Rules go on the side their catalog entry is declared for.</summary>
public sealed record ScenarioArmy(
    string Name,
    int Units,
    int ModelsPerUnit,
    IReadOnlyList<ScenarioRule> UnitRules,
    IReadOnlyList<ScenarioRule> ModelRules,
    IReadOnlyList<ScenarioWeapon> Weapons,
    IReadOnlyDictionary<string, string> UnitStats);

/// <summary>
/// What a run is: a board, armies, a scheduler shape, enforcement, a round cap, a seed, a script and
/// the expectations. Declared in C# because that keeps the assertions typed (spec → Deliberately not decided).
/// </summary>
/// <param name="RoundCap">
/// The maximum number of *whole* rounds. A cap of n ends the run on round n's boundary Ask; round n + 1 never
/// starts. Task 6 → Termination, exactly.
/// </param>
public sealed record Scenario(
    string Name,
    ulong Seed,
    SchedulerShape Shape,
    Enforcement Enforcement,
    int RoundCap,
    TableSize Table,
    IReadOnlyList<ScenarioArmy> Armies,
    IReadOnlyList<ScenarioTerrain> Terrain,
    IReadOnlyList<ScenarioMarker> Markers,
    IReadOnlyList<string> ZoneShapes,
    IReadOnlyList<EntryDocument> Spells,
    AnswerScript Script,
    bool ScriptOnly,
    IReadOnlyList<Expectation> Expectations)
{
    public Scenario WithSeed(ulong seed) => this with { Seed = seed };
}
```
```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Scenarios;

/// <summary>The fluent form. Every setter is optional except two armies — a game needs two teams.</summary>
public sealed class ScenarioBuilder(string name, ulong seed)
{
    private SchedulerShape _shape = SchedulerShape.AlternatingActivation;
    private Enforcement _enforcement = Enforcement.Strict;
    private int _roundCap = 8;
    private TableSize _table = new(72, 48);
    private readonly List<ScenarioArmy> _armies = [];
    private readonly List<ScenarioTerrain> _terrain = [];
    private readonly List<ScenarioMarker> _markers = [];
    private string? _zoneStyle;
    private readonly List<EntryDocument> _spells = [];
    private AnswerScript _script = AnswerScript.Empty;
    private bool _scriptOnly;
    private readonly List<Expectation> _expectations = [];

    public ScenarioBuilder Shape(SchedulerShape shape) { _shape = shape; return this; }
    public ScenarioBuilder Enforcing(Enforcement e) { _enforcement = e; return this; }
    public ScenarioBuilder RoundCap(int rounds) { _roundCap = rounds; return this; }
    public ScenarioBuilder Table(double width, double length) { _table = new TableSize(width, length); return this; }
    /// <summary>A zone style id from Tabletop.Geometry.ZoneStyles; its shapes expand in team-ordinal order.</summary>
    public ScenarioBuilder Zones(string style) { _zoneStyle = style; return this; }

    /// <summary>Spells for this run only, folded into the pinned PropertySet the way TestProperties.WithSpells does.
    /// The shipped content document is every special rule on pages 13-15 and holds no spells, because a spell list belongs
    /// to an army book this project has not designed. Nothing invented here reaches the game's content (ruled 2026-09-09).</summary>
    public ScenarioBuilder Spells(params EntryDocument[] spells) { _spells.AddRange(spells); return this; }

    public ScenarioBuilder Army(string armyName, int units, int modelsPerUnit,
        IReadOnlyList<ScenarioRule>? unitRules = null, IReadOnlyList<ScenarioRule>? modelRules = null,
        IReadOnlyList<ScenarioWeapon>? weapons = null, IReadOnlyDictionary<string, string>? unitStats = null)
    {
        _armies.Add(new ScenarioArmy(armyName, units, modelsPerUnit, unitRules ?? [], modelRules ?? [],
            weapons ?? [new ScenarioWeapon("Rifle", new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" }, [])],
            unitStats ?? new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "4+" }));
        return this;
    }

    public ScenarioBuilder Terrain(string mesh, Point3 at, double yaw = 0, double scale = 1, params Volume[] volumes)
    { _terrain.Add(new ScenarioTerrain(mesh, at, yaw, scale, volumes)); return this; }

    public ScenarioBuilder Marker(MarkerKind kind, Point3 at, double diameter = 2, string? label = null)
    { _markers.Add(new ScenarioMarker(kind, at, diameter, label)); return this; }

    public ScenarioBuilder Scripted(AnswerScript script, bool only = false) { _script = script; _scriptOnly = only; return this; }
    public ScenarioBuilder Expect(params Expectation[] expectations) { _expectations.AddRange(expectations); return this; }

    public Scenario Build()
    {
        if (_armies.Count < 2) throw new InvalidOperationException($"scenario '{name}' needs at least two armies; it has {_armies.Count}");
        var style = _zoneStyle ?? (_armies.Count > 2 ? ZoneStyles.FreeForAll : ZoneStyles.Frontline);
        var shapes = ZoneStyles.ShapesOf(style).Take(_armies.Count).ToList();
        return new Scenario(name, seed, _shape, _enforcement, _roundCap, _table, _armies, _terrain, _markers, shapes, _spells, _script, _scriptOnly, _expectations);
    }
}
```

- [ ] **Step 8: Write `ScenarioCompiler.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Scenarios;

public sealed record CompiledScenario(
    Scenario Scenario, GameInfo Info, Board Board, PropertySet Properties,
    IReadOnlyList<Guid> TeamIds, IReadOnlyList<Guid> ParticipantIds,
    IReadOnlyDictionary<Guid, Guid> TeamOfParticipant, TableState Snapshot, Guid RecorderParticipantId)
{
    public Guid ParticipantFor(Guid teamId) => TeamOfParticipant.First(kv => kv.Value == teamId).Key;
}

/// <summary>
/// A scenario becomes exactly what Hydrator would have read: a GameInfo at playing, a Board over the
/// setup snapshot and one ArmySnapshot per participant. Ids are ordinary v7 GUIDs and differ between
/// runs; what a seed fixes is GameInfo.RngSeed, which is the value the game.rng_seed column holds and
/// therefore the value GameState.RngSeed takes (owner ruling, 2026-09-06).
/// </summary>
public static class ScenarioCompiler
{
    /// <summary>The core Grimdark Future set, through the production path: the game server's embedded document.</summary>
    public static PropertySet CoreProperties { get; } = PropertiesJson.ToSet(CoreDocument, Guid.CreateVersion7);

    static PropertySetDocument CoreDocument { get; } =
        PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId);

    /// <summary>The core set plus a scenario's own spells, so the hash differs and the run is pinned to what it declared.
    /// No spells means CoreProperties itself, which keeps every other scenario on the production hash.</summary>
    public static PropertySet PropertiesFor(IReadOnlyList<EntryDocument> spells) =>
        spells.Count == 0 ? CoreProperties
            : PropertiesJson.ToSet(CoreDocument with { Name = $"{CoreDocument.Name} + scenario spells", Entries = [.. CoreDocument.Entries, .. spells] }, Guid.CreateVersion7);

    public static CompiledScenario Compile(Scenario s)
    {
        var teams = s.Armies.Select((_, i) => new Team(Guid.CreateVersion7(), i + 1)).ToList();
        var participants = teams.Select((t, i) =>
            new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), $"Sim {i + 1}", t.Id, null)).ToList();
        // The recorder: a participant with no team, which is what a spectator is (whole-design review ruling 5).
        // It is never Asked, and its filtered event stream is what the driver's projection and the recording are built from.
        var recorder = new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), "Recorder", null, null);
        participants.Add(recorder);

        var snapshot = new TableState(
            Guid.CreateVersion7(), s.Name, s.Table, VisualRef.OfBuiltin("grass"),
            s.Terrain.Select((t, i) => new TerrainPlacement(
                Guid.CreateVersion7(), VisualRef.OfBuiltin(t.Mesh), t.Position, new Yaw(t.Yaw), t.Scale,
                t.Volumes.Select((v, j) => new TerrainVolume(Guid.CreateVersion7(), v)).ToList())).ToList(),
            s.Markers.Select((m, i) => new Marker(
                Guid.CreateVersion7(), m.Kind, m.Position, new Yaw(0), m.Diameter, m.Label, VisualRef.OfBuiltin("objective"))).ToList(),
            s.ZoneShapes.Select((shape, i) => new DeploymentZone(Guid.CreateVersion7(), shape, i + 1)).ToList());

        var armies = s.Armies.Select((a, i) => new ArmyInPlay(participants[i].Id, BuildArmy(i + 1, a))).ToList();

        // RngSeed is positional and last among the scalars (contracts plan → GameInfo), so a game cannot
        // be built without a seed. This is the one value a scenario's seed fixes.
        var info = new GameInfo(
            Guid.CreateVersion7(), s.Name, GameStatus.Playing, participants[0].AccountId,
            GrimdarkFutureRules.RulesId, CoreProperties.ContentHash, s.Shape, s.Enforcement,
            WaitsBetweenSteps: false, unchecked((long)s.Seed), teams, participants);

        return new CompiledScenario(s, info, new Board(snapshot, armies), PropertiesFor(s.Spells),
            teams.Select(t => t.Id).ToList(), participants.Where(p => p.TeamId is not null).Select(p => p.Id).ToList(),
            participants.Where(p => p.TeamId is not null).ToDictionary(p => p.Id, p => p.TeamId!.Value),
            snapshot, recorder.Id);
    }

    private static ArmySnapshot BuildArmy(int team, ScenarioArmy a)
    {
        var units = new List<Unit>();
        for (var u = 1; u <= a.Units; u++)
        {
            var modelRules = a.ModelRules.Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList();
            var weapons = a.Weapons.Select((w, k) => new Weapon(Guid.CreateVersion7(), w.Name,
                w.Stats, w.Rules.Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList())).ToList();
            var models = Enumerable.Range(1, a.ModelsPerUnit).Select(m => new ModelSlot(
                Guid.CreateVersion7(), m, $"{a.Name} {u}.{m}",
                new Dictionary<string, string>(), modelRules, Base.Circle(1.26), 1.5, null, weapons)).ToList();
            units.Add(new Unit(Guid.CreateVersion7(), u, $"{a.Name} unit {u}", a.UnitStats,
                a.UnitRules.Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList(), models));
        }
        return new ArmySnapshot(Guid.CreateVersion7(), a.Name, "grimdark-future", units);
    }
}
```

`Base.Circle(1.26)` is a 32 mm base in inches and `1.5` its height — the same two numbers the scheduler plan's `TestArmies.Build` uses, so a scenario and a fixture describe the same model.

- [ ] **Step 9: Write `ScenarioCatalog.cs`**

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Scenarios;

/// <summary>The named scenarios the console runs. Regression scripts are added here beside the soak ones.</summary>
public static class ScenarioCatalog
{
    private static readonly Dictionary<string, Func<Scenario>> Factories = new(StringComparer.Ordinal)
    {
        ["skirmish"] = () => new ScenarioBuilder("skirmish", seed: 1)
            .Army("Red", units: 3, modelsPerUnit: 3)
            .Army("Blue", units: 3, modelsPerUnit: 3)
            .Build(),

        ["terrain"] = () => new ScenarioBuilder("terrain", seed: 1)
            .Army("Red", 3, 3).Army("Blue", 3, 3)
            .Terrain("ruin", new Point3(36, 24, 0), volumes:
                new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 3,
                    TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, null))
            .Terrain("forest", new Point3(18, 30, 0), volumes:
                new Volume(VolumeKind.Area, Footprint.Circle(4), new Point2(0, 0), new Yaw(0), 0, 2,
                    TerrainDeclarations.Difficult | TerrainDeclarations.Cover, null))
            .Build(),

        ["player-turn"] = () => new ScenarioBuilder("player-turn", seed: 1)
            .Shape(SchedulerShape.PlayerTurn).Army("Red", 3, 3).Army("Blue", 3, 3).Build(),

        ["free-for-all"] = () => new ScenarioBuilder("free-for-all", seed: 1)
            .Army("Red", 2, 2).Army("Blue", 2, 2).Army("Green", 2, 2)
            .Zones(ZoneStyles.FreeForAll).Build(),

        ["stalemate"] = () => new ScenarioBuilder("stalemate", seed: 1)
            .RoundCap(3).Army("Red", 1, 1).Army("Blue", 1, 1).Build(),

        // Casting is not a mode: a spell is cast inside an ordinary activation. This preset exists only because none of the
        // other five fields a caster, so nothing else opens casting's two player decisions in a whole-game run — the token
        // spend map, and the caster spending its own leftover tokens on its own roll (both changed by rulings of 2026-09-06).
        ["casting"] = () => new ScenarioBuilder("casting", seed: 1)
            .Spells(Bolt)
            .Army("Red", 2, 3, modelRules: [new ScenarioRule(0, "Caster", 2)])
            .Army("Blue", 2, 3)
            .Build(),
    };

    /// <summary>One test spell, the shape of a damage spell: an enemy unit in range takes hits. Authored as JSON and parsed
    /// through the production path, so the sim declares a spell exactly as a content document would. Never shipped.</summary>
    static readonly EntryDocument Bolt = PropertiesJson.Parse(
        """
        { "name": "Sim spells", "gameRules": "grimdark-future", "stats": [], "actions": [], "rolls": [], "entries": [
          { "name": "Bolt", "kind": "spell", "cost": 2, "faction": "Sim Book",
            "ruleText": "Target enemy unit within 12\" takes 3 hits with AP(1).",
            "targeting": { "side": "enemy", "grain": "unit", "count": 1, "range": 12 },
            "effects": [ { "subject": "self", "ordinal": 10, "kind": "add_hit", "amount": 3, "conditions": [],
              "children": [ { "subject": "self", "ordinal": 20, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [] } ] } ] } ] }
        """).Entries.Single();

    public static IReadOnlyList<string> Names { get; } = Factories.Keys.Order().ToList();
    public static Scenario Get(string name) =>
        Factories.TryGetValue(name, out var f) ? f() : throw new KeyNotFoundException($"no scenario '{name}'; known: {string.Join(", ", Names)}");
    public static bool TryGet(string name, out Scenario scenario)
    {
        if (Factories.TryGetValue(name, out var f)) { scenario = f(); return true; }
        scenario = null!; return false;
    }
}
```

The zone shape ids come from `Tabletop.Geometry.ZoneStyles.ShapesOf` (`frontline/1`, `free-for-all/1`, …), in team-ordinal order, which is exactly what `SetZoneStyle.Shapes` carries during setup. `sim/` stores the expansion rather than the style name, because that is what a `deployment_zone` row holds (game server skeleton plan → spec correction 4).

- [ ] **Step 10: Write `Expectation.cs`** — the stub the builder needs; Task 6 gives it its evaluation.

```csharp
namespace Tabletop.Sim.Scenarios;

/// <summary>A scenario's own assertion, checked at the end of a run beside the universal invariants.</summary>
public abstract record Expectation
{
    public sealed record EventOfKind(string Kind, int AtLeast = 1) : Expectation;
    public sealed record NoEventOfKind(string Kind) : Expectation;
    public sealed record ReachedRound(int Round) : Expectation;
    public sealed record TeamWiped(int TeamOrdinal) : Expectation;
    public sealed record ModelsRemaining(int TeamOrdinal, int AtLeast) : Expectation;
    public sealed record AskAsked(string Question, int AtLeast = 1) : Expectation;
    public sealed record Outcome(string Kind) : Expectation;
}
```

- [ ] **Step 11: Add to the solution, run the tests**

```bash
cd /home/cstupi/src/tabletop
dotnet sln Tabletop.sln add --solution-folder sim sim/Tabletop.Sim.csproj
dotnet sln Tabletop.sln add --solution-folder tests tests/Tabletop.Sim.Tests
dotnet test tests/Tabletop.Sim.Tests
```

Expected: every test in `ChooserTests` and `ScenarioTests` passes. `AnswerScript.Empty` does not exist yet, so add the two lines Task 3 fills in (`public sealed record AnswerScript { public static AnswerScript Empty { get; } = new(); }` in `sim/Answers/AnswerScript.cs`) to compile, and replace the file wholesale in Task 3.

- [ ] **Step 12: Commit**

```bash
git add sim tests/Tabletop.Sim.Tests Tabletop.sln
git commit -m "Add the sim project, deterministic ids, the chooser and the scenario"
```

---
### Task 2: The answer policy — every legal-set shape, from the shape alone

**Files:**
- Create: `sim/Answers/PolicyView.cs`, `sim/Answers/AnswerPolicy.cs`
- Test: `tests/Tabletop.Sim.Tests/AnswerPolicyTests.cs`

**Interfaces:**
- Consumes: `Ask`, `AskOption`, `LegalSet` and its seven members (`Options`, `PieceSelection`, `Allocation`, `Assignment`, `MoveRegion`, `TokenSpend`, `Confirmation`), `MoveContainment`, `AskAnswer` and its eight members (`Chosen`, `SelectedPieces`, `Allocated`, `Assigned`, `Destinations`, `TokensSpent`, `Confirmed`, `Passed`), `ModelRelocation`, `Location` (`OnTable`, `OnShelf`, `Inside`), `Projection`, `ModelInPlay`, `UnitInPlay`, `Board`, `GameInfo` (Protocol); `PropertySet` (game server `Tabletop.GameServer.Properties`); `PlacedModel`, `ModelBody`, `WorldVolume`, `Placement`, `Point2`, `Point3`, `Yaw`, `TableSize`, `Movement`, `MoveCheck`, `Settling` (Geometry); `TerrainPlacement.WorldVolumes()` (Content); `Chooser` (Task 1).
- Produces: `sealed class PolicyView(Projection projection, Board board, IReadOnlyList<WorldVolume>? volumes = null, GameInfo? info = null, PropertySet? properties = null)` with `Projection Projection`, `Board Board`, `GameInfo? Info`, `PropertySet? Properties`, `TableSize Table`, `IReadOnlyList<WorldVolume> Volumes`, `IReadOnlyList<WorldVolume> VolumesById(IReadOnlyList<Guid> ids)`, `Location LocationOf(Guid modelId)`, `PlacedModel? PlacedOf(Guid modelId)`, `ModelBody BodyOf(Guid modelId)`, `double SurfaceAt(Point2 point)`, `UnitInPlay UnitOf(Guid unitId)`, `ModelInPlay ModelOf(Guid modelId)`, `Guid? TeamOf(Guid unitId)`; `sealed record PolicyOptions(double PassProbability = 0, double EmbarkProbability = 0.25, int MoveSamples = 24)`; `sealed class NoLegalAnswerException(Ask ask) : Exception`; `sealed class AnswerPolicy(Chooser chooser, PolicyOptions? options = null)` with `AskAnswer Answer(Ask ask, PolicyView view)` and `static IReadOnlyList<string> ShapeNames`.

Spec: spec → *Two ways to answer an Ask* — "A seeded policy. Any Ask, answered from its legal set with a seeded random choice"; "It chooses legally, not well"; "An answer is chosen from what the server offered, whole"; "the default policy stays uniform over the legal set and ignorant of state". Spec → *Deliberately not decided*: "Whether the policy is uniform over the legal set or weighted, and whether it prefers to act over passing" — uniform is the owner's ruling; preferring to act (`PassProbability` defaults to `0`) is this plan's choice, and is question 2.

**What `PolicyView` exposes, and what constrains what is done with it.** It is the whole state an
answer may read: the projection (positions, wounds, conditions, grants, scores, the scheduler
position, the open Asks), the board and its unit and model definitions with their stats, the team
each unit belongs to, and the pinned `PropertySet`. It also carries the geometry an answer needs to
be *expressible* — a `MoveRegion` names model ids, an allowance and volume ids and says nothing
about where those models stand, so `PlacedOf`, `BodyOf`, `VolumesById` and `SurfaceAt` are how a
destination gets computed at all.

The blanket ban on reading state is withdrawn (owner ruling, 2026-09-06) and the two prohibitions in
*Global Constraints* replace it: nothing here may narrow the legal set, and nothing here may compute
a rules outcome the engine should compute. Those are properties of the *policy*, not of the view, so
they are enforced by the tests below — `AnswerPolicy` answers from the whole set and its answers are
checked to be uniform over it — rather than by leaving members out of a class.

`Info` and `Properties` are nullable because a unit test builds a view from a bare board with
neither. `RunLoop` always supplies both, so the production path is complete; `TeamOf` returns null
when `Info` is absent.

**Two implementations, not one policy with a switch.** `AnswerPolicy` is the default and stays
uniform over the legal set and ignorant of state: it reads only the geometry members, and never
`UnitOf`, `ModelOf`, `TeamOf` or `Properties`. A policy that reads state is a **second
implementation of `IAnswerSource`** (Task 3), added when someone wants one, and it is not built here.

That shape rather than a `PolicyOptions` flag, for three reasons. The owner's ruling is that a
state-reading policy is "opt-in and named as such" — a type name is named, a boolean defaulting to
false is not, and a boolean is one careless `new PolicyOptions(readState: true)` away from becoming
the soak default. `IAnswerSource` is already the seam the spec says a cleverer opponent "would live
behind", and `ComposedAnswers` already composes sources, so the second implementation costs no new
abstraction. And keeping `AnswerPolicy` free of state reads keeps its own tests able to pass a bare
view, which is what makes "uniform over the legal set" cheap to assert.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

public class AnswerPolicyTests
{
    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7(), C = Guid.CreateVersion7();
    static Ask With(LegalSet legal, bool allowPass = false, bool sealedAsk = false) =>
        new(Guid.CreateVersion7(), "q", "p", [A], legal, allowPass, sealedAsk, null, null);
    static AnswerPolicy Policy(ulong seed = 1, PolicyOptions? o = null) => new(new Chooser(seed), o);
    static PolicyView EmptyView() => new(Projection.Initial(new Board(Bare(), []), Info()), new Board(Bare(), []));
    static TableState Bare() => new(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
    static GameInfo Info() => new(Guid.CreateVersion7(), "g", GameStatus.Playing, A, "grimdark-future", "sha256:x",
        SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 1, [new Team(A, 1)], []);

    [Fact]
    public void Options_picks_one_of_the_choices_by_id()
    {
        var ask = With(new Options([new AskOption("x", "X", []), new AskOption("y", "Y", [])]));
        var chosen = Assert.IsType<Chosen>(Policy().Answer(ask, EmptyView()));
        Assert.Contains(chosen.OptionId, (string[])["x", "y"]);
    }

    [Fact]
    public void An_empty_options_set_passes_when_it_may_and_throws_when_it_may_not()
    {
        Assert.IsType<Passed>(Policy().Answer(With(new Options([]), allowPass: true), EmptyView()));
        Assert.Throws<NoLegalAnswerException>(() => Policy().Answer(With(new Options([])), EmptyView()));
    }

    [Fact]
    public void Pieces_respects_min_and_max_and_picks_only_eligible_ones()
    {
        var answer = Assert.IsType<SelectedPieces>(Policy().Answer(With(new PieceSelection([A, B, C], 2, 2)), EmptyView()));
        Assert.Equal(2, answer.PieceIds.Count);
        Assert.All(answer.PieceIds, id => Assert.Contains(id, (Guid[])[A, B, C]));
        Assert.Equal(2, answer.PieceIds.Distinct().Count());
    }

    [Fact]
    public void Allocation_spends_the_whole_amount_over_the_targets()
    {
        var answer = Assert.IsType<Allocated>(Policy().Answer(With(new Allocation([A, B], 5)), EmptyView()));
        Assert.Equal(5, answer.Counts.Values.Sum());
        Assert.All(answer.Counts.Keys, k => Assert.Contains(k, (Guid[])[A, B]));
    }

    [Fact]
    public void Assignment_covers_every_item_within_the_distinct_target_cap()
    {
        var ask = With(new Assignment([new AskOption("g1", "", []), new AskOption("g2", "", []), new AskOption("g3", "", [])], [A, B, C], 2));
        var answer = Assert.IsType<Assigned>(Policy().Answer(ask, EmptyView()));
        Assert.Equal(["g1", "g2", "g3"], answer.Targets.Keys.Order());
        Assert.True(answer.Targets.Values.Distinct().Count() <= 2);
    }

    [Fact]
    public void A_token_spend_stays_within_every_model_own_cap_and_confirmation_confirms()
    {
        // p.13: any number of tokens up to what each model holds, so the bound is per model and not a total (ruled 2026-09-06).
        var spend = new TokenSpend(new Dictionary<Guid, int> { [A] = 1, [B] = 4 });
        var answer = Assert.IsType<TokensSpent>(Policy().Answer(With(spend), EmptyView()));
        Assert.Equal(new HashSet<Guid> { A, B }, answer.Counts.Keys.ToHashSet());   // one entry per model the map named, whatever order Guid comparison puts them in
        Assert.InRange(answer.Counts[A], 0, 1);
        Assert.InRange(answer.Counts[B], 0, 4);
        Assert.IsType<Confirmed>(Policy().Answer(With(new Confirmation()), EmptyView()));
    }

    [Fact]
    public void Pass_is_taken_only_when_the_ask_allows_it_and_the_probability_says_so()
    {
        var ask = With(new Options([new AskOption("x", "X", [])]), allowPass: true);
        Assert.IsType<Passed>(Policy(1, new PolicyOptions(PassProbability: 1)).Answer(ask, EmptyView()));
        Assert.IsType<Chosen>(Policy(1, new PolicyOptions(PassProbability: 0)).Answer(ask, EmptyView()));
        Assert.IsType<Chosen>(Policy(1, new PolicyOptions(PassProbability: 1))
            .Answer(With(new Options([new AskOption("x", "X", [])])), EmptyView()));
    }

    [Fact]
    public void The_same_seed_answers_a_run_of_asks_identically()
    {
        var asks = Enumerable.Range(0, 30).Select(i => With(new PieceSelection([A, B, C], 1, 3))).ToList();
        var one = Policy(77); var two = Policy(77);
        foreach (var ask in asks) Assert.Equal(one.Answer(ask, EmptyView()), two.Answer(ask, EmptyView()));
    }

    [Fact]
    public void Every_legal_set_shape_is_answered()
    {
        Assert.Equal(["allocation", "assignment", "confirmation", "move_region", "options", "pieces", "token_spend"], AnswerPolicy.ShapeNames.Order());
        var policy = Policy();
        LegalSet[] all =
        [
            new Options([new AskOption("x", "X", [])]),
            new PieceSelection([A], 1, 1),
            new Allocation([A], 1),
            new Assignment([new AskOption("g", "", [])], [A], 1),
            new TokenSpend(new Dictionary<Guid, int> { [A] = 1 }),
            new Confirmation(),
        ];
        foreach (var legal in all) Assert.NotNull(policy.Answer(With(legal), EmptyView()));
    }
}
```

- [ ] **Step 2: Write the failing move-region test** — the one shape that reads the view

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

public class MoveRegionPolicyTests
{
    static readonly Guid Model = Guid.CreateVersion7(), Unit = Guid.CreateVersion7(), Part = Guid.CreateVersion7(), Team = Guid.CreateVersion7();

    static (PolicyView View, Guid ModelId) Standing(Point3 at)
    {
        var slot = new ModelSlot(Model, 1, "m", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, []);
        var unit = new Unit(Unit, 1, "u", new Dictionary<string, string>(), [], [slot]);
        var table = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
        var info = new GameInfo(Guid.CreateVersion7(), "g", GameStatus.Playing, Part, "grimdark-future", "sha256:x",
            SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 2, [new Team(Team, 1)],
            [new Participant(Part, Guid.CreateVersion7(), "p", Team, null)]);
        var board = new Board(table, [new ArmyInPlay(Part, new ArmySnapshot(Guid.CreateVersion7(), "a", "grimdark-future", [unit]))]);
        var projection = Projection.Initial(board, info);
        var applier = new EventApplier(board, info);
        applier.Apply(new GameEvent(1, null, new ModelsMoved([new ModelRelocation(Model, new OnShelf(), new OnTable(new Placement(at, new Yaw(0))))]), null));
        return (new PolicyView(applier.Projection, board), Model);
    }

    [Fact]
    public void A_destination_is_within_the_allowance_and_on_the_table()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion([model], 6, [], [], null, null), false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, view));
            var to = Assert.IsType<OnTable>(Assert.Single(answer.Moves).To);
            var check = Movement.Check(view.PlacedOf(model)!.Value, to.Placement.Position.Horizontal, 6, [], [], null, view.Table);
            Assert.True(check.WithinAllowance, $"seed {seed} moved {check.Distance}");
            Assert.False(check.LeavesTable);
            Assert.Equal(new OnTable(new Placement(view.PlacedOf(model)!.Value.Placement.Position, new Yaw(0))).GetType(), answer.Moves[0].From.GetType());
        }
    }

    [Fact]
    public void An_impassable_volume_is_not_crossed()
    {
        var (view, model) = Standing(new Point3(10, 24, 0));
        var wall = new WorldVolume(Guid.CreateVersion7(),
            new Volume(VolumeKind.Solid, Footprint.Rect(2, 40), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(14, 24, 0), new Yaw(0), 1));
        var walled = new PolicyView(view.Projection, view.Board, [wall]);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team],
            new MoveRegion([model], 12, [wall.Id], [], null, null), false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, walled));
            var to = Assert.IsType<OnTable>(answer.Moves[0].To);
            Assert.False(Movement.Check(walled.PlacedOf(model)!.Value, to.Placement.Position.Horizontal, 12, [wall], [], null, walled.Table).CrossesImpassable);
        }
    }

    [Fact]
    public void Embarking_puts_every_model_inside_an_enterable_container()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var container = Guid.CreateVersion7();
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team],
            new MoveRegion([model], 6, [], [], null, new MoveContainment([container], null, null)), false, false, null, Guid.CreateVersion7());
        var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(4), new PolicyOptions(EmbarkProbability: 1)).Answer(ask, view));
        Assert.Equal(new Inside(container), answer.Moves[0].To);
    }
}
```

`PolicyView` therefore takes an optional volume override — the third parameter of `PolicyView(Projection, Board, IReadOnlyList<WorldVolume>? volumes = null, GameInfo? info = null, PropertySet? properties = null)` — because a test builds volumes without a `TerrainPlacement`, and `SocketDriver` gets its board from the wire. The last two are the state members the ruling permits; every test above passes the first two or three positionally and leaves them null.
- [ ] **Step 3: Run; expect failure, then write `PolicyView.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "AnswerPolicyTests|MoveRegionPolicyTests"`
Expected: `CS0246: ... 'AnswerPolicy' could not be found`.

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

/// <summary>
/// The state an answer may read, which is all of it: the projection (positions, wounds, conditions,
/// grants, scores, the scheduler position), the board and its definitions with their stats, the team
/// each unit belongs to, and the pinned PropertySet. The geometry members are what makes an answer
/// expressible at all — a MoveRegion names model ids without saying where they stand.
///
/// Reading is allowed; two things are not (owner ruling, 2026-09-06 → Global Constraints). No caller
/// may narrow the legal set, and no caller may compute a rules outcome the engine should compute.
/// AnswerPolicy, the default, touches only the geometry members and is uniform over the legal set; a
/// state-reading policy is a second IAnswerSource and is not this class's business.
/// </summary>
public sealed class PolicyView(
    Projection projection, Board board, IReadOnlyList<WorldVolume>? volumes = null,
    GameInfo? info = null, PropertySet? properties = null)
{
    public Projection Projection { get; } = projection;
    public Board Board { get; } = board;

    /// <summary>Null only in a unit test that builds a bare board; RunLoop always supplies both.</summary>
    public GameInfo? Info { get; } = info;
    public PropertySet? Properties { get; } = properties;

    public TableSize Table { get; } = board.Table.Size;
    public IReadOnlyList<WorldVolume> Volumes { get; } = volumes ?? board.Table.Terrain.SelectMany(t => t.WorldVolumes()).ToList();

    public IReadOnlyList<WorldVolume> VolumesById(IReadOnlyList<Guid> ids) =>
        ids.Count == 0 ? [] : Volumes.Where(v => ids.Contains(v.Id)).ToList();

    public Location LocationOf(Guid modelId) => Projection.Models[modelId].Location;

    public PlacedModel? PlacedOf(Guid modelId)
    {
        var model = Projection.Models[modelId];
        return model.Location is OnTable table ? new PlacedModel(BodyOf(modelId), table.Placement) : null;
    }

    public ModelBody BodyOf(Guid modelId)
    {
        var slot = Projection.Models[modelId].Slot;
        return new ModelBody(slot.Base, slot.Height);
    }

    public double SurfaceAt(Point2 point) => Settling.SurfaceHeightAt(point, Volumes);

    // Wounds, conditions, actions, grants and the unit definition with its stats.
    public UnitInPlay UnitOf(Guid unitId) => Projection.Units[unitId];
    public ModelInPlay ModelOf(Guid modelId) => Projection.Models[modelId];

    /// <summary>The team owning a unit, through its participant. Null when no GameInfo was supplied.</summary>
    public Guid? TeamOf(Guid unitId) =>
        Info?.Participants.FirstOrDefault(p => p.Id == Projection.Units[unitId].ParticipantId)?.TeamId;
}
```

- [ ] **Step 4: Write `AnswerPolicy.cs`**

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

/// <summary>Thrown when a legal set admits no answer at all — an empty options list on an Ask that forbids passing.</summary>
public sealed class NoLegalAnswerException(Ask ask)
    : Exception($"the legal set of ask '{ask.Question}' ({ask.Legal.GetType().Name}) admits no answer and the ask forbids passing");

/// <param name="PassProbability">How often a pass is taken when one is allowed and something else is too. Zero: the policy always acts.</param>
/// <param name="EmbarkProbability">How often a move that may embark does.</param>
/// <param name="MoveSamples">Candidate destinations tried before the model stands still.</param>
public sealed record PolicyOptions(double PassProbability = 0, double EmbarkProbability = 0.25, int MoveSamples = 24);

/// <summary>
/// An Ask in, an answer out, from the legal set's shape alone. This is the default policy and it stays
/// ignorant of state on purpose: a cleverer policy explores a narrower, more plausible region and finds
/// fewer bugs (owner ruling, 2026-09-06). It never narrows the set either — every choice is uniform over
/// exactly what the server said was legal. It reads only PolicyView's geometry members, never UnitOf,
/// ModelOf, TeamOf or Properties; a policy that wants those is a second IAnswerSource, named as such.
/// Deterministic for a seed, given the same run: one chooser stream advanced in Ask order.
/// </summary>
public sealed class AnswerPolicy(Chooser chooser, PolicyOptions? options = null)
{
    private readonly PolicyOptions _o = options ?? new PolicyOptions();

    /// <summary>Every LegalSet member the Protocol declares. A shape missing here is a compile-time hole, not a runtime one.</summary>
    public static IReadOnlyList<string> ShapeNames { get; } =
        ["options", "pieces", "allocation", "assignment", "move_region", "token_spend", "confirmation"];

    public AskAnswer Answer(Ask ask, PolicyView view) => ask.Legal switch
    {
        Options o => AnswerOptions(ask, o),
        PieceSelection p => AnswerPieces(ask, p),
        Allocation a => AnswerAllocation(ask, a),
        Assignment a => AnswerAssignment(ask, a),
        MoveRegion m => AnswerMove(ask, m, view),
        TokenSpend t => AnswerSpend(ask, t),
        Confirmation => new Confirmed(),
        _ => throw new NotSupportedException($"legal set {ask.Legal.GetType().Name} has no policy; add it to AnswerPolicy and to ShapeNames"),
    };

    /// <summary>A pass, when the Ask allows one and the dice say so. Null means "answer the shape instead".</summary>
    private AskAnswer? Pass(Ask ask) => ask.AllowPass && chooser.Chance(_o.PassProbability) ? new Passed() : null;

    private AskAnswer AnswerOptions(Ask ask, Options o)
    {
        if (o.Choices.Count == 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        return Pass(ask) ?? new Chosen(chooser.Pick(o.Choices).Id);
    }

    /// <summary>Ids out of the list the server handed over, whatever they name: units for a deploy, one model of a unit for a Takedown's pick (p.14).</summary>
    private AskAnswer AnswerPieces(Ask ask, PieceSelection p)
    {
        var max = Math.Min(p.Max, p.Eligible.Count);
        if (max < p.Min) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        return Pass(ask) ?? new SelectedPieces(chooser.Subset(p.Eligible, p.Min + chooser.Next(max - p.Min + 1)));
    }

    private AskAnswer AnswerAllocation(Ask ask, Allocation a)
    {
        if (a.Targets.Count == 0 || a.Amount <= 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        if (Pass(ask) is { } passed) return passed;
        var counts = new Dictionary<Guid, int>();
        for (var i = 0; i < a.Amount; i++)
        {
            var target = chooser.Pick(a.Targets);
            counts[target] = counts.GetValueOrDefault(target) + 1;
        }
        return new Allocated(counts);
    }

    /// <summary>
    /// p.13's spend: any number of tokens, per model, up to what that model holds. One draw per entry in the map, each
    /// uniform in [0, that model's maximum], which is what makes a model's own bound the thing under test — a single
    /// draw against the sum could never produce the answer "one from the model holding four and none from the other".
    /// Entries are taken in id order so one seed answers one run identically. An empty map is nothing to spend.
    /// </summary>
    private AskAnswer AnswerSpend(Ask ask, TokenSpend t)
    {
        if (t.MaxByModel.Count == 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        if (Pass(ask) is { } passed) return passed;
        var counts = new Dictionary<Guid, int>();
        foreach (var (modelId, max) in t.MaxByModel.OrderBy(kv => kv.Key))
            counts[modelId] = chooser.Next(Math.Max(max, 0) + 1);
        return new TokensSpent(counts);
    }

    private AskAnswer AnswerAssignment(Ask ask, Assignment a)
    {
        if (a.Items.Count == 0 || a.Targets.Count == 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        if (Pass(ask) is { } passed) return passed;
        var allowed = chooser.Subset(a.Targets, Math.Min(Math.Max(a.MaxDistinctTargets, 1), a.Targets.Count));
        return new Assigned(a.Items.ToDictionary(i => i.Id, _ => chooser.Pick(allowed)));
    }

    private AskAnswer AnswerMove(Ask ask, MoveRegion m, PolicyView view)
    {
        if (m.ModelIds.Count == 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        if (Pass(ask) is { } passed) return passed;

        // Embarking and disembarking are the containment field's, and it carries every id and radius they need.
        if (m.Containment is { Enterable.Count: > 0 } c && chooser.Chance(_o.EmbarkProbability))
        {
            var container = chooser.Pick(c.Enterable);
            return new Destinations(m.ModelIds.Select(id => new ModelRelocation(id, view.LocationOf(id), new Inside(container))).ToList());
        }

        var impassable = view.VolumesById(m.ImpassableVolumeIds);
        var difficult = view.VolumesById(m.DifficultVolumeIds);
        var moves = new List<ModelRelocation>();
        foreach (var id in m.ModelIds)
        {
            var from = view.LocationOf(id);
            var start = view.PlacedOf(id);
            if (start is null) { moves.Add(new ModelRelocation(id, from, from)); continue; }
            var destination = Sample(start.Value, m, impassable, difficult, view);
            moves.Add(new ModelRelocation(id, from, new OnTable(new Placement(
                new Point3(destination.X, destination.Y, view.SurfaceAt(destination)), start.Value.Placement.Yaw))));
        }
        return new Destinations(moves);
    }

    /// <summary>
    /// A uniform bearing and a uniform distance inside the allowance, retried until Movement.Check likes it.
    /// Every parameter of the check comes from the legal set itself; standing still is the fallback and is
    /// always legal, since it is where the model already is.
    /// </summary>
    private Point2 Sample(PlacedModel start, MoveRegion m, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, PolicyView view)
    {
        var origin = start.Placement.Position.Horizontal;
        for (var i = 0; i < _o.MoveSamples; i++)
        {
            var bearing = chooser.Double() * Math.Tau;
            var reach = m.DifficultCap is { } cap ? Math.Min(m.Allowance, Math.Max(cap, 0)) : m.Allowance;
            var distance = chooser.Double() * reach;
            var candidate = new Point2(origin.X + Math.Cos(bearing) * distance, origin.Y + Math.Sin(bearing) * distance);
            var check = Movement.Check(start, candidate, m.Allowance, impassable, difficult, m.DifficultCap, view.Table);
            if (check is { WithinAllowance: true, CrossesImpassable: false, LeavesTable: false }) return candidate;
        }
        return origin;
    }
}
```

The retry budget is per model, not per Ask: `Sample` is called once per model id in the region, so a unit of five gets five independent budgets and one model that cannot find a destination does not freeze the others.

- [ ] **Step 5: Run the tests; expect all green**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "AnswerPolicyTests|MoveRegionPolicyTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add sim/Answers tests/Tabletop.Sim.Tests
git commit -m "Answer every legal-set shape from the shape alone, with a seeded chooser"
```

---
### Task 3: The script, and the composition that makes a mixed run

**Files:**
- Create: `sim/Answers/AnswerScript.cs`, `sim/Answers/IAnswerSource.cs`
- Test: `tests/Tabletop.Sim.Tests/AnswerScriptTests.cs`

**Interfaces:**
- Consumes: `Ask`, `AskAnswer` and its members, `AskQuestions` (Protocol); `AnswerPolicy`, `PolicyView`, `PolicyOptions`, `NoLegalAnswerException` (Task 2); `Chooser` (Task 1).
- Produces: `sealed record ScriptKey(string Question, int Ordinal)`; `sealed record AnswerScript(IReadOnlyDictionary<ScriptKey, AskAnswer> Answers)` with `static AnswerScript Empty`, `static ScriptBuilder Build()`, `bool TryGet(string question, int ordinal, out AskAnswer answer)`, `int Count`; `sealed class ScriptBuilder` with `ScriptBuilder At(string question, int ordinal, AskAnswer answer)`, `ScriptBuilder Then(string question, AskAnswer answer)` (the next unused ordinal for that question, starting at 0), `AnswerScript Build()`; `sealed class UnscriptedAskException(Ask ask, int ordinal) : Exception`; `interface IAnswerSource { AskAnswer Answer(Ask ask, PolicyView view); IReadOnlyList<AnsweredAsk> History { get; } }`; `sealed record AnsweredAsk(string Question, int Ordinal, Guid AskId, AskAnswer Answer, bool FromScript)`; `sealed class ComposedAnswers(AnswerScript script, AnswerPolicy? policy) : IAnswerSource` — script first, then the policy; a null policy is a script-only run and throws `UnscriptedAskException` on the first Ask the script does not cover.

Spec: spec → *Two ways to answer an Ask* — "**Both, and mixed in one run.** A scenario scripts the opening and hands the rest to the policy… An unscripted Ask under a script-only run is a failure; under a mixed run it goes to the policy." Module prompt → "scripted answers keyed by Ask question and ordinal … Scripted answers take precedence, the policy handles the rest, and a script-only run fails on an unscripted Ask."

**The ordinal is per question, counted over the run.** The *n*th `activate` Ask is `("activate", n)` regardless of how many `confirm` Asks fell between them, because that is the number a person writing a script can count. `Ask.Id` is server-minted and unusable as a key; the frame id is not stable across a rewrite of the sequence.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

public class AnswerScriptTests
{
    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7();
    static Ask Ask(string question, LegalSet? legal = null, bool allowPass = false) =>
        new(Guid.CreateVersion7(), question, "p", [A], legal ?? new Options([new AskOption("x", "X", []), new AskOption("y", "Y", [])]), allowPass, false, null, null);
    static PolicyView View() => AnswerPolicyTests.EmptyViewForOtherTests();

    [Fact]
    public void The_builder_keys_by_question_and_ordinal_counting_from_zero()
    {
        var script = AnswerScript.Build()
            .Then(AskQuestions.Activate, new Chosen("x"))
            .Then(AskQuestions.Activate, new Passed())
            .At(AskQuestions.Confirm, 0, new Confirmed())
            .Build();
        Assert.Equal(3, script.Count);
        Assert.True(script.TryGet(AskQuestions.Activate, 0, out var first));
        Assert.Equal(new Chosen("x"), first);
        Assert.True(script.TryGet(AskQuestions.Activate, 1, out var second));
        Assert.Equal(new Passed(), second);
        Assert.False(script.TryGet(AskQuestions.Activate, 2, out _));
    }

    [Fact]
    public void A_duplicate_key_is_a_programming_error()
    {
        var builder = AnswerScript.Build().At(AskQuestions.Activate, 0, new Chosen("x"));
        Assert.Throws<ArgumentException>(() => builder.At(AskQuestions.Activate, 0, new Passed()));
    }

    [Fact]
    public void The_script_wins_and_the_policy_takes_the_rest()
    {
        var script = AnswerScript.Build().Then(AskQuestions.Activate, new Chosen("y")).Build();
        var source = new ComposedAnswers(script, new AnswerPolicy(new Chooser(1)));
        Assert.Equal(new Chosen("y"), source.Answer(Ask(AskQuestions.Activate), View()));
        Assert.IsType<Chosen>(source.Answer(Ask(AskQuestions.Activate), View()));      // second one: the policy
        Assert.IsType<Confirmed>(source.Answer(Ask(AskQuestions.Confirm, new Confirmation()), View()));
        Assert.Equal([true, false, false], source.History.Select(h => h.FromScript));
        Assert.Equal([0, 1, 0], source.History.Select(h => h.Ordinal));
    }

    [Fact]
    public void A_script_only_run_fails_on_the_first_unscripted_ask()
    {
        var script = AnswerScript.Build().Then(AskQuestions.Activate, new Chosen("y")).Build();
        var source = new ComposedAnswers(script, policy: null);
        Assert.Equal(new Chosen("y"), source.Answer(Ask(AskQuestions.Activate), View()));
        var thrown = Assert.Throws<UnscriptedAskException>(() => source.Answer(Ask(AskQuestions.Activate), View()));
        Assert.Contains("activate", thrown.Message);
        Assert.Contains("ordinal 1", thrown.Message);
    }

    [Fact]
    public void The_ordinal_counts_per_question_not_over_all_asks()
    {
        var source = new ComposedAnswers(AnswerScript.Empty, new AnswerPolicy(new Chooser(1)));
        source.Answer(Ask(AskQuestions.Activate), View());
        source.Answer(Ask(AskQuestions.Confirm, new Confirmation()), View());
        source.Answer(Ask(AskQuestions.Activate), View());
        Assert.Equal([("activate", 0), ("confirm", 0), ("activate", 1)], source.History.Select(h => (h.Question, h.Ordinal)));
    }
}
```

`AnswerPolicyTests.EmptyViewForOtherTests()` is Task 2's `EmptyView()` made `internal static` under that name, so both test classes build the same bare view without a second copy of the eight lines.

- [ ] **Step 2: Run; expect failure, then write `AnswerScript.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter AnswerScriptTests`
Expected: `CS0117: 'AnswerScript' does not contain a definition for 'Build'`.

```csharp
using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

/// <summary>The nth Ask of that question in the run, counted from zero. Server-minted Ask ids are not keys.</summary>
public sealed record ScriptKey(string Question, int Ordinal);

/// <summary>
/// The regression form: named answers in order (spec → Two ways to answer an Ask). A scenario scripts the
/// opening; whatever it does not cover goes to the policy, or fails the run when there is no policy.
/// </summary>
public sealed record AnswerScript(IReadOnlyDictionary<ScriptKey, AskAnswer> Answers)
{
    public static AnswerScript Empty { get; } = new(new Dictionary<ScriptKey, AskAnswer>());
    public static ScriptBuilder Build() => new();
    public int Count => Answers.Count;

    public bool TryGet(string question, int ordinal, out AskAnswer answer) =>
        Answers.TryGetValue(new ScriptKey(question, ordinal), out answer!);
}

public sealed class ScriptBuilder
{
    private readonly Dictionary<ScriptKey, AskAnswer> _answers = [];
    private readonly Dictionary<string, int> _next = [];

    public ScriptBuilder At(string question, int ordinal, AskAnswer answer)
    {
        var key = new ScriptKey(question, ordinal);
        if (!_answers.TryAdd(key, answer)) throw new ArgumentException($"'{question}' ordinal {ordinal} is already scripted", nameof(ordinal));
        _next[question] = Math.Max(_next.GetValueOrDefault(question), ordinal + 1);
        return this;
    }

    /// <summary>The next unscripted ordinal for that question.</summary>
    public ScriptBuilder Then(string question, AskAnswer answer) => At(question, _next.GetValueOrDefault(question), answer);

    public AnswerScript Build() => new(_answers);
}
```

- [ ] **Step 3: Write `IAnswerSource.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

public sealed record AnsweredAsk(string Question, int Ordinal, Guid AskId, AskAnswer Answer, bool FromScript);

/// <summary>
/// Where an answer comes from. The run loop knows only this. It is also the seam a state-reading
/// policy lives behind when someone builds one: the view it is handed carries the whole state, and a
/// named implementation makes the choice to read it opt-in rather than a default. Whatever implements
/// this must still answer from the legal set whole, and must not compute a rules outcome itself.
/// </summary>
public interface IAnswerSource
{
    AskAnswer Answer(Ask ask, PolicyView view);
    IReadOnlyList<AnsweredAsk> History { get; }
}

public sealed class UnscriptedAskException(Ask ask, int ordinal)
    : Exception($"ask '{ask.Question}' ordinal {ordinal} is unscripted and this run is script-only");

/// <summary>
/// Script first, then the policy. A null policy is a script-only run, and the first uncovered Ask fails it
/// (spec → Two ways to answer an Ask).
/// </summary>
public sealed class ComposedAnswers(AnswerScript script, AnswerPolicy? policy) : IAnswerSource
{
    private readonly Dictionary<string, int> _seen = [];
    private readonly List<AnsweredAsk> _history = [];
    public IReadOnlyList<AnsweredAsk> History => _history;

    public AskAnswer Answer(Ask ask, PolicyView view)
    {
        var ordinal = _seen.GetValueOrDefault(ask.Question);
        _seen[ask.Question] = ordinal + 1;
        if (script.TryGet(ask.Question, ordinal, out var scripted))
        {
            _history.Add(new AnsweredAsk(ask.Question, ordinal, ask.Id, scripted, FromScript: true));
            return scripted;
        }
        if (policy is null) throw new UnscriptedAskException(ask, ordinal);
        var answer = policy.Answer(ask, view);
        _history.Add(new AnsweredAsk(ask.Question, ordinal, ask.Id, answer, FromScript: false));
        return answer;
    }
}
```

A sealed Ask addressed to several teams is answered once per team, so `ComposedAnswers.Answer` is called once per (Ask, team) pair and the ordinal advances each time. That is what a script writer counts: one line per answer given, not one per Ask opened. Task 6's run loop is what calls it per unanswered team.

- [ ] **Step 4: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter AnswerScriptTests
git add sim/Answers tests/Tabletop.Sim.Tests
git commit -m "Script answers by question and ordinal, and fall through to the policy"
```

---
### Task 4: `IGameDriver`, and the in-process driver

**Files:**
- Create: `sim/Drivers/IGameDriver.cs`, `sim/Drivers/ScenarioRows.cs`, `sim/Drivers/RecorderClient.cs`, `sim/Drivers/InProcessDriver.cs`
- Test: `tests/Tabletop.Sim.Tests/InProcessDriverTests.cs`

**Interfaces:**
- Consumes: `IGameRows`, `OwnedArmy`, `ISessionClient`, `SessionServices`, `GameSession`, `Hydrator`, `InMemoryEventLog`, `GameServerOptions`, `Replay` (game server skeleton); `RulesEngineFactory`, `InMemoryPropertiesStore`, `DiceSource`, `IDiceSource` (interpreter plan); `SchedulerFactory` (scheduler plan); `SetConfiguration`, `SocketMessage` (`Welcome`, `BoardMessage`, `EventsMessage`, `CommandMessage`, `Refused`, `PresenceMessage`, `PingMessage`, `PresenceEnded`, `GameInfoMessage`, `AtRest`), `GameEvent`, `Command`, `AnswerAsk`, `AskAnswer`, `Projection`, `OpenAsk`, `EventApplier`, `RefusalReason`, `Board`, `GameInfo` (Protocol); `TableState`, `ArmySnapshot` (Content); `CompiledScenario`, `ScenarioCompiler` (Task 1); `PolicyView` (Task 2).
- Produces: `abstract record SubmitResult` with `Accepted(string? Warning)` and `Rejected(RefusalReason Reason, string Detail)`; `sealed record StepResult(IReadOnlyList<GameEvent> Events, IReadOnlyList<OpenAsk> OpenAsks, bool Quiescent)`; `enum DriverMode { Run, StepAndWait }`; `sealed class RestTimeoutException(TimeSpan timeout, long lastSeq) : Exception` with `TimeSpan Timeout` and `long LastSeq`; `interface IGameDriver : IAsyncDisposable` with `Guid GameId`, `GameInfo Info`, `Board Board`, `IReadOnlyList<Guid> TeamIds`, `Guid ParticipantFor(Guid teamId)`, `DriverMode Mode { get; set; }`, `string Name`, `Task StartAsync(CancellationToken ct)`, `Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct)`, `Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)`, `Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct)`, `Task<Projection> ProjectionAsync(CancellationToken ct)`, `Task<StepResult> StepAsync(CancellationToken ct)`, and the default method `Task<SubmitResult> AnswerAsync(Guid teamId, Guid askId, AskAnswer answer, CancellationToken ct)`; `sealed class ScenarioRows(CompiledScenario scenario) : IGameRows`; `sealed class RecorderClient(Guid participantId) : ISessionClient` with `IReadOnlyList<SocketMessage> Frames`, `IReadOnlyList<SocketMessage> TakeFrames()`, `bool Closed`; `sealed class InProcessDriver : IGameDriver` with `static Task<InProcessDriver> StartAsync(CompiledScenario, CancellationToken)`, `GameSession Session`, `Task<Projection> ServerProjectionAsync(CancellationToken)`, `IReadOnlyList<GameEvent> Log`.

Spec: spec → *Two drivers behind one interface* — "In process. The session, the engine and an in-memory log, constructed directly. No socket, no Postgres, no Docker. This is the default because a full game must cost milliseconds"; "A driver interface with one implementation would not have been worth the abstraction." `architecture.md` → Contracts to freeze ("nobody instantiates a game session: a client opens a socket naming a game, and if no session for that game is in memory the server hydrates it from the log") — which is why the in-process driver goes through `Hydrator` and `GameSession.ConnectAsync` rather than building a `GameState` by hand: the two drivers then differ in transport and nothing else.

**The driver's projection is its own.** Every driver keeps an `EventApplier` over the scenario's board and info, fed by the events it drains from the **recorder participant's** stream — a participant with no team, which is what a spectator is. That is the same applier the client uses, so the run loop, the invariants and the recording all read a projection built the way a client builds one, and the socket driver needs no second mechanism. `InProcessDriver.ServerProjectionAsync` additionally exposes the session's own projection, and Task 7's first invariant compares them.

**Quiescence is exact on both drivers.** `StepAsync` returns when the server has said it is at rest. In process the signal is a marker: `GameSession.RunAsync` puts one at the back of the mailbox, so when it returns every queued turn has run. Over the socket the signal is the `AtRest(long Seq)` frame the project owner ruled on 2026-09-06 — sent when the game server has finished processing an inbound `connect` or `command`, after the last frame that processing produced. It replaces the 250 ms quiet window an earlier draft used, which guessed at rest and therefore read a slow server as a stuck game and cost a quarter of a second per step.

**Who receives it, from the game server plan's ruling and not assumed here.** `Seq` is the log's high-water mark, `Play?.Projection.LastSeq ?? 0`, so it is `0` before the log opens and unchanged when the batch appended nothing. The frame is broadcast to every socket on the game when the batch appended at least one event, and sent to the provoking client alone when it appended none, so a refusal disturbs nobody but its sender (game server skeleton plan → Task 9, *The rest frame*). Two things follow for `SocketDriver`, and both are in the code below. `SubmitAsync` reads the submitting socket's own rest frame, which arrives whatever the outcome and always after a `Refused` for that command, so the refusal check needs no timing window. And `StepAsync` waits on the recorder's socket **only when the last command appended**, which the submitting socket's `AtRest.Seq` says by standing above what the recorder has applied; otherwise no batch is coming to the recorder and the step is an empty one. Every driver's absorb path selects `EventsMessage` and ignores every other kind, which is what lets a consumer with no use for `at_rest` (or for any kind added later) see it without faulting.

**The timeout is a liveness guard, not a quiescence guess.** `SocketDriver.RestTimeout` is 30 seconds and bounds one wait for `AtRest`. Any value above the slowest real step is correct, and no designed step approaches it, which is why the number is two orders of magnitude larger than the window it replaces rather than a tuned one. Expiry throws `RestTimeoutException`; `RunLoop.RunAsync` catches it and the run ends `Failed` with the last frames dumped, so a socket soak exits `1`. Expiry is never `Stuck`: stuck means the engine made no progress, and a rest timeout means the harness stopped observing.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class InProcessDriverTests
{
    static CompiledScenario Scenario() => ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));

    [Fact]
    public async Task Starting_hydrates_a_playing_game_and_opens_the_first_ask()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, TestContext.Current.CancellationToken);
        Assert.Equal(c.Info.Id, driver.GameId);
        Assert.Equal(c.TeamIds, driver.TeamIds);
        Assert.Equal("in-process", driver.Name);

        var asks = await driver.OpenAsksAsync(TestContext.Current.CancellationToken);
        var ask = Assert.Single(asks).Ask;
        Assert.Equal(AskQuestions.Deploy, ask.Question);
        Assert.Contains(ask.Teams.Single(), c.TeamIds);
    }

    [Fact]
    public async Task The_drivers_projection_holds_every_model_on_the_shelf_before_deployment()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, TestContext.Current.CancellationToken);
        var projection = await driver.ProjectionAsync(TestContext.Current.CancellationToken);
        Assert.Equal(18, projection.Models.Count);                             // two armies of three units of three
        Assert.All(projection.Models.Values, m => Assert.IsType<OnShelf>(m.Location));
        Assert.Equal(0, projection.Scheduler.Round);
    }

    [Fact]
    public async Task An_answer_is_accepted_and_produces_events()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, TestContext.Current.CancellationToken);
        var ask = (await driver.OpenAsksAsync(TestContext.Current.CancellationToken)).Single().Ask;
        var team = ask.Teams.Single();
        var eligible = Assert.IsType<PieceSelection>(ask.Legal).Eligible;
        var result = await driver.AnswerAsync(team, ask.Id, new SelectedPieces([eligible[0]]), TestContext.Current.CancellationToken);
        Assert.IsType<SubmitResult.Accepted>(result);
        var step = await driver.StepAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(step.Events);
        Assert.Contains(step.Events, e => e.Payload is AskAnswered);
    }

    [Fact]
    public async Task An_answer_outside_the_legal_set_is_rejected_under_strict()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, TestContext.Current.CancellationToken);
        var ask = (await driver.OpenAsksAsync(TestContext.Current.CancellationToken)).Single().Ask;
        var result = await driver.AnswerAsync(ask.Teams.Single(), ask.Id, new SelectedPieces([Guid.CreateVersion7()]), TestContext.Current.CancellationToken);
        var rejected = Assert.IsType<SubmitResult.Rejected>(result);
        Assert.Equal(RefusalReason.OutsideLegalSet, rejected.Reason);
    }

    [Fact]
    public async Task The_drivers_projection_and_the_servers_agree_after_every_step()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, TestContext.Current.CancellationToken);
        var mine = await driver.ProjectionAsync(TestContext.Current.CancellationToken);
        var theirs = await driver.ServerProjectionAsync(TestContext.Current.CancellationToken);
        Assert.Equal(theirs.LastSeq, mine.LastSeq);
        Assert.Equal(theirs.Models.Count, mine.Models.Count);
        Assert.Equal(theirs.OpenAsks.Keys.Order(), mine.OpenAsks.Keys.Order());
    }

    [Fact]
    public async Task Draining_twice_does_not_return_the_same_event_twice()
    {
        var c = Scenario();
        await using var driver = await InProcessDriver.StartAsync(c, TestContext.Current.CancellationToken);
        var first = await driver.DrainEventsAsync(TestContext.Current.CancellationToken);
        var second = await driver.DrainEventsAsync(TestContext.Current.CancellationToken);
        Assert.Empty(second);
        Assert.Equal(first.Select(e => e.Seq).Order(), first.Select(e => e.Seq));
    }
}
```

`TestContext.Current.CancellationToken` is xUnit's; where the installed xUnit version does not have it, pass `CancellationToken.None`.
- [ ] **Step 2: Run; expect failure, then write `IGameDriver.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter InProcessDriverTests`
Expected: `CS0246: ... 'InProcessDriver' could not be found`.

```csharp
using Tabletop.Protocol;

namespace Tabletop.Sim.Drivers;

public abstract record SubmitResult
{
    public sealed record Accepted(string? Warning) : SubmitResult;
    public sealed record Rejected(RefusalReason Reason, string Detail) : SubmitResult;
}

/// <summary>What one step produced. Quiescent means the server rested without emitting anything.</summary>
public sealed record StepResult(IReadOnlyList<GameEvent> Events, IReadOnlyList<OpenAsk> OpenAsks, bool Quiescent);

/// <summary>Run answers every Ask; StepAndWait stops with one open and answers nothing (spec → Interactive debugging is a door).</summary>
public enum DriverMode { Run, StepAndWait }

/// <summary>
/// The server did not say it was at rest inside the driver's timeout. A hung server or a dead
/// connection, never a stuck game: RunLoop turns it into RunOutcome.Failed.
/// </summary>
public sealed class RestTimeoutException(TimeSpan timeout, long lastSeq)
    : Exception($"the server did not reach rest within {timeout.TotalSeconds:0.#}s; last applied seq {lastSeq}")
{
    public TimeSpan Timeout { get; } = timeout;
    public long LastSeq { get; } = lastSeq;
}

/// <summary>
/// One game, played from outside. Two implementations: in process and over the socket, and the same
/// scenario must pass against either (spec → Two drivers behind one interface).
/// </summary>
public interface IGameDriver : IAsyncDisposable
{
    string Name { get; }
    Guid GameId { get; }
    GameInfo Info { get; }
    Board Board { get; }
    IReadOnlyList<Guid> TeamIds { get; }
    Guid ParticipantFor(Guid teamId);
    DriverMode Mode { get; set; }

    /// <summary>The scenario's board and armies become a playing game, and the driver is connected to it.</summary>
    Task StartAsync(CancellationToken ct);

    Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct);
    Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct);

    /// <summary>Every event that arrived since the last drain, in sequence order, applied to the driver's own projection.</summary>
    Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct);

    Task<Projection> ProjectionAsync(CancellationToken ct);

    /// <summary>Let the server rest, then report what it produced.</summary>
    Task<StepResult> StepAsync(CancellationToken ct);

    Task<SubmitResult> AnswerAsync(Guid teamId, Guid askId, AskAnswer answer, CancellationToken ct) =>
        SubmitAsync(ParticipantFor(teamId), new AnswerAsk(askId, answer), ct);
}
```

- [ ] **Step 3: Write `ScenarioRows.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;

namespace Tabletop.Sim.Drivers;

/// <summary>
/// Management's rows, in memory, for exactly one scenario at status playing. The writes belong to lobby and
/// setup, which a scenario skips: it declares the setup result directly, and the setup phase is not logged
/// anyway (setup content → The setup phase is not logged).
///
/// This is also how an in-process run sets the RNG seed: ReadGameAsync hands back the scenario's own
/// GameInfo, whose RngSeed the compiler set from the scenario seed, so Hydrator reaches GameState.RngSeed
/// by the ordinary path. The socket driver has to write the rng_seed column instead (Task 8).
/// </summary>
public sealed class ScenarioRows(Scenarios.CompiledScenario scenario) : IGameRows
{
    public Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(gameId == scenario.Info.Id ? scenario.Info : null);

    public Task<Guid?> FindParticipantAsync(Guid gameId, Guid accountId, CancellationToken ct) =>
        Task.FromResult(scenario.Info.Participants.FirstOrDefault(p => p.AccountId == accountId)?.Id);

    public Task<TableState?> ReadDraftAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<TableState?>(gameId == scenario.Info.Id ? scenario.Snapshot : null);

    public Task<IReadOnlyList<ArmyInPlay>> ReadSnapshotsAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(scenario.Board.Armies);

    public Task<OwnedArmy?> ReadArmyAsync(Guid armyId, CancellationToken ct) => Task.FromResult<OwnedArmy?>(null);
    public Task<bool> PropertiesExistAsync(string hash, CancellationToken ct) =>
        Task.FromResult(hash == scenario.Properties.ContentHash);

    private static Task Unsupported(string member) =>
        throw new NotSupportedException($"a scenario is already playing; {member} belongs to lobby or setup");

    public Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct) => throw new NotSupportedException("a scenario declares its teams");
    public Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct) => Unsupported(nameof(SetParticipantTeamAsync));
    public Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct) => Unsupported(nameof(SetParticipantArmyAsync));
    public Task SetConfigurationAsync(Guid gameId, SetConfiguration configuration, CancellationToken ct) => Unsupported(nameof(SetConfigurationAsync));
    public Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct) => Unsupported(nameof(StartSetupAsync));
    public Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct) => Unsupported(nameof(WriteDraftAsync));
    public Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshots, CancellationToken ct) =>
        Unsupported(nameof(EndSetupAsync));
}
```

- [ ] **Step 4: Write `RecorderClient.cs`**

```csharp
using System.Net.WebSockets;
using Tabletop.GameServer.Session;
using Tabletop.Protocol;

namespace Tabletop.Sim.Drivers;

/// <summary>An ISessionClient that keeps its frames instead of writing them to a socket.</summary>
public sealed class RecorderClient(Guid participantId) : ISessionClient
{
    private readonly List<SocketMessage> _frames = [];
    private readonly Lock _gate = new();

    public Guid ConnectionId { get; } = Guid.CreateVersion7();
    public Guid ParticipantId { get; } = participantId;
    public bool Closed { get; private set; }

    public IReadOnlyList<SocketMessage> Frames { get { lock (_gate) return _frames.ToList(); } }

    public IReadOnlyList<SocketMessage> TakeFrames()
    {
        lock (_gate) { var taken = _frames.ToList(); _frames.Clear(); return taken; }
    }

    public ValueTask SendAsync(SocketMessage message) { lock (_gate) _frames.Add(message); return ValueTask.CompletedTask; }
    public ValueTask CloseAsync(WebSocketCloseStatus status, string reason) { Closed = true; return ValueTask.CompletedTask; }
}
```

`ConnectionId` is the one `Guid.CreateVersion7()` in `sim/`: it is a connection, not game state, and never reaches a recording.

- [ ] **Step 5: Write `InProcessDriver.cs`**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Session;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

/// <summary>
/// The session, the engine and an in-memory log, constructed directly — no socket, no Postgres, no Docker
/// (spec → Two drivers behind one interface). It still goes through Hydrator and GameSession.ConnectAsync,
/// so the two drivers differ in transport and nothing else.
/// </summary>
public sealed class InProcessDriver : IGameDriver
{
    private readonly CompiledScenario _scenario;
    private readonly EventApplier _applier;
    private readonly List<RecorderClient> _players = [];
    private RecorderClient _recorder = null!;
    private GameSession _session = null!;

    private InProcessDriver(CompiledScenario scenario)
    {
        _scenario = scenario;
        _applier = new EventApplier(scenario.Board, scenario.Info);
    }

    public static async Task<InProcessDriver> StartAsync(CompiledScenario scenario, CancellationToken ct)
    {
        var driver = new InProcessDriver(scenario);
        await driver.StartAsync(ct);
        return driver;
    }

    public string Name => "in-process";
    public Guid GameId => _scenario.Info.Id;
    public GameInfo Info => _scenario.Info;
    public Board Board => _scenario.Board;
    public IReadOnlyList<Guid> TeamIds => _scenario.TeamIds;
    public Guid ParticipantFor(Guid teamId) => _scenario.ParticipantFor(teamId);
    public DriverMode Mode { get; set; } = DriverMode.Run;
    public GameSession Session => _session;
    public IReadOnlyList<GameEvent> Log => _applier.Log;

    public async Task StartAsync(CancellationToken ct)
    {
        var services = new SessionServices(
            new ScenarioRows(_scenario),
            new InMemoryEventLog(),
            new RulesEngineFactory(new InMemoryPropertiesStore().Add(_scenario.Properties), new DiceSource(), new SchedulerFactory()),
            new GameServerOptions { DbConnection = "", StytchProjectId = "sim", StytchJwksUrl = "unused" },
            NullLoggerFactory.Instance);

        _session = await Hydrator.HydrateAsync(GameId, services, ct);
        await _session.StartAsync();                                   // the engine catches up: roll-off, round 0, deployment

        _recorder = new RecorderClient(_scenario.RecorderParticipantId);
        await _session.ConnectAsync(_recorder);
        foreach (var participantId in _scenario.ParticipantIds)
        {
            var client = new RecorderClient(participantId);
            _players.Add(client);
            await _session.ConnectAsync(client);
        }
        Absorb(_recorder.TakeFrames());
    }

    public Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OpenAsk>>(_applier.Projection.OpenAsks.Values.ToList());

    public async Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)
    {
        var client = _players.FirstOrDefault(c => c.ParticipantId == participantId)
            ?? throw new ArgumentException($"no connected client for participant {participantId}", nameof(participantId));
        await _session.ReceiveAsync(client, new CommandMessage(Guid.CreateVersion7(), command));
        var refused = client.TakeFrames().OfType<Refused>().LastOrDefault();
        Absorb(_recorder.TakeFrames());
        return refused is null
            ? new SubmitResult.Accepted(null)
            : new SubmitResult.Rejected(refused.Reason, refused.Detail);
    }

    public Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct) => Task.FromResult(Absorb(_recorder.TakeFrames()));

    public Task<Projection> ProjectionAsync(CancellationToken ct) => Task.FromResult(_applier.Projection);

    /// <summary>The session's own projection, for the invariant that the two agree. In-process only.</summary>
    public Task<Projection> ServerProjectionAsync(CancellationToken ct) =>
        _session.RunAsync(() => Task.FromResult(_session.Play!.Projection));

    public async Task<StepResult> StepAsync(CancellationToken ct)
    {
        // The mailbox is FIFO, so a marker at the back means every queued turn — every Advance — has run.
        await _session.RunAsync(() => Task.FromResult(0));
        var events = Absorb(_recorder.TakeFrames());
        return new StepResult(events, _applier.Projection.OpenAsks.Values.ToList(), events.Count == 0);
    }

    private IReadOnlyList<GameEvent> Absorb(IReadOnlyList<SocketMessage> frames)
    {
        var events = frames.OfType<EventsMessage>().SelectMany(m => m.Events).OrderBy(e => e.Seq).ToList();
        foreach (var e in events) if (e.Seq > _applier.Projection.LastSeq) _applier.Apply(e);
        return events;
    }

    public async ValueTask DisposeAsync() => await _session.DrainAsync("sim finished");
}
```

`GameServerOptions` needs no real values here: nothing verifies a JWT, nothing opens a connection, and the presence timer is `SessionHost`'s, which is not running.

- [ ] **Step 6: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter InProcessDriverTests
git add sim/Drivers tests/Tabletop.Sim.Tests
git commit -m "Drive a hydrated session in process behind IGameDriver"
```

---
### Task 5: The recording — the schema, the writer, and one frame per event

**Files:**
- Create: `sim/Recording/Recording.cs`, `sim/Recording/RecordingJson.cs`, `sim/Recording/LogLines.cs`, `sim/Recording/FrameProjector.cs`
- Test: `tests/Tabletop.Sim.Tests/RecordingTests.cs`

**Interfaces:**
- Consumes: `Projection`, `ModelInPlay`, `UnitInPlay`, `SchedulerPosition`, `OpenAsk`, `Ask`, `LegalSet`, `GameEvent`, `EventPayload` and every member, `EventKinds`, `Grant`, `RollRecord`, `Die`, `Location` (`OnTable`, `OnShelf`, `Inside`), `Board`, `GameInfo`, `EventApplier`, `AskAnswer` members (Protocol); `TableState`, `Marker`, `MarkerKind`, `TerrainPlacement`, `TerrainVolume`, `DeploymentZone`, `VisualRef` (Content); `Volume`, `VolumeKind`, `Footprint`, `FootprintKind`, `TerrainDeclarations`, `Base`, `BaseShape`, `Point2`, `Polygon`, `ZoneStyles` (Geometry); `CompiledScenario` (Task 1).
- Produces: the record schema below (`Recording`, `RecordingMeta`, `RecordedTeam`, `RecordedBoard`, `RecordedTerrain`, `RecordedVolume`, `RecordedMarker`, `RecordedZone`, `RecordedModel`, `RecordedFrame`, `RecordedModelState`, `RecordedUnitState`, `RecordedMarkerState`, `RecordedAsk`, `RecordedAskOption`, `RecordedRoll`, `RecordedModifier`); `static class RecordingJson` with `const int SchemaVersion = 1`, `JsonSerializerOptions Options`, `Task WriteAsync(Recording, string path, CancellationToken)`, `Task<Recording> ReadAsync(string path, CancellationToken)`, `string Serialize(Recording)`, `Recording Deserialize(string)`; `static class LogLines` with `string Of(GameEvent, Projection after, IReadOnlyDictionary<Guid,int> teamOrdinals)`; `sealed class FrameProjector(CompiledScenario scenario)` with `RecordedBoard Board`, `RecordedFrame Add(GameEvent)`, `IReadOnlyList<RecordedFrame> Frames`, `Projection Projection`, `Recording Build(RecordingMeta meta)`.

Spec: spec → *The recording is the artifact* — "A run writes one file: the board, then one frame per event, each frame carrying the model positions, wounds, conditions and the log line that event produced. It is a **recording of a projection, not a log of events**"; "Given frames, the viewer interpolates nothing and decides nothing; it draws what it is handed"; "The recording is derived and disposable. It is not a save format, it is not replayed into the engine, and the event log remains the only thing undo and hydration read." Spec → *Interactive debugging is a door* — "the recording carries the open Ask at every frame". Spec → *Still open*: "Whether a recording should carry the dice so a viewer can show a roll, or only its result" — this plan carries the dice (question 3).

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
            new OnTable(new Tabletop.Protocol.Placement(new Tabletop.Geometry.Point3(6, 6, 0), new Tabletop.Geometry.Yaw(0))))]);
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
        var roll = new RollRecord(Guid.CreateVersion7(), "hit", 4, [new RollModifier("Good Shot", 1)],
            [new Die(3, 4, true), new Die(1, 2, false)]);
        var frame = projector.Add(new GameEvent(1, null,
            new StepCompleted(Guid.CreateVersion7(), "hit-roll", null, [roll], []), null));
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
        var captain = new RollRecord(Guid.CreateVersion7(), "hit", 3, [], [new Die(4, 4, true)]);
        var squad = new RollRecord(Guid.CreateVersion7(), "hit", 5, [], [new Die(4, 4, false)]);
        var frame = projector.Add(new GameEvent(1, null,
            new StepCompleted(Guid.CreateVersion7(), "roll-to-hit", null, [captain, squad], []), null));
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
        var meta = new RecordingMeta("terrain", 1, "in-process", "grimdark-future", c.Properties.ContentHash,
            "alternating-activation", "strict", 8, [], "round_limit", null, null, 1, 2, DateTimeOffset.UnixEpoch, 12.5);
        var recording = projector.Build(meta);

        var path = Path.Combine(Path.GetTempPath(), $"sim-{Guid.CreateVersion7():N}.json");
        try
        {
            await RecordingJson.WriteAsync(recording, path, TestContext.Current.CancellationToken);
            var read = await RecordingJson.ReadAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(RecordingJson.SchemaVersion, read.Version);
            Assert.Equal(recording, read);
            Assert.Contains("\"schemaVersion\"", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
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
        Assert.Contains("\"scores\"", json);              // empty, but present: a frame is whole state
        Assert.Contains("\"passed\"", json);
        Assert.DoesNotContain("roundCap", json);          // null, so omitted
        Assert.DoesNotContain("activeTeamId", json);      // no active team yet, so omitted
    }
}
```
- [ ] **Step 2: Run; expect failure, then write `Recording.cs`** — the schema, in full

Run: `dotnet test tests/Tabletop.Sim.Tests --filter RecordingTests`
Expected: `CS0246: ... 'FrameProjector' could not be found`.

```csharp
using Tabletop.Geometry;   // Point2 only: a zone's resolved rings are the one geometry type in the schema.

namespace Tabletop.Sim.Recording;

// A RECORDING OF A PROJECTION, NOT A LOG OF EVENTS (spec → The recording is the artifact).
// Every frame carries whole state, so a viewer interpolates nothing and decides nothing: it draws what it
// is handed. The recording is derived and disposable — it is not a save format, it is never replayed into
// the engine, and the event log in Postgres remains the only thing undo and hydration read. A reader who
// wants what happened rather than what it looked like wants game.game_event, not this.

/// <summary>One run. `schemaVersion` is RecordingJson.SchemaVersion; a reader that does not know it should refuse the file.</summary>
public sealed record Recording(int Version, RecordingMeta Meta, RecordedBoard Board, IReadOnlyList<RecordedFrame> Frames);

/// <param name="Outcome">
/// `decided`, `round_limit`, `stuck` or `failed` for a finished run; `live` for a feed that is still
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
    Guid Id, string Shape, int TeamOrdinal, IReadOnlyList<IReadOnlyList<Point2>> Polygons);

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

`Recording.Version` serialises as `version`; the test asserts `schemaVersion` appears in the file, so `Recording` declares it as `[JsonPropertyName("schemaVersion")] int Version` — add that attribute to the record's first parameter (`[property: JsonPropertyName("schemaVersion")] int Version`). A reader checks it first and refuses anything it does not know.

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
            table.Zones.Select(z => new RecordedZone(z.Id, z.Shape, z.TeamOrdinal,
                ZoneStyles.Resolve(z.Shape, table.Size).Select(g => g.Vertices).ToList())).ToList(),
            models);
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

`ZoneStyles.Resolve` answers in the corner frame the recording already uses, so nothing converts. An unknown shape id resolves to no rings, so `polygons` is `[]` and a viewer draws no outline while still showing the id — the same fallback as a client that does not know the style (client design → *An unknown zone shape id*).

- [ ] **Step 6: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter RecordingTests
git add sim/Recording tests/Tabletop.Sim.Tests
git commit -m "Record a projection frame per event, and its JSON schema"
```

---
### Task 6: The run loop — decided, round-capped, stuck, or failed

**Files:**
- Create: `sim/Run/RunOutcome.cs`, `sim/Run/AskFingerprint.cs`, `sim/Run/HandPlacement.cs`, `sim/Run/RunLoop.cs`, `sim/Run/RunReport.cs`
- Modify: `sim/Scenarios/Expectation.cs` (its evaluation)
- Test: `tests/Tabletop.Sim.Tests/RunLoopTests.cs`

**Interfaces:**
- Consumes: `IGameDriver`, `SubmitResult`, `StepResult`, `DriverMode` (Task 4); `IAnswerSource`, `PolicyView`, `UnscriptedAskException`, `NoLegalAnswerException`, `AnsweredAsk` (Tasks 2–3); `FrameProjector`, `Recording`, `RecordingMeta`, `RecordedTeam` (Task 5); `Projection`, `OpenAsk`, `Ask`, `LegalSet`, `AskAnswer`, `AskQuestions`, `MoveModels`, `ModelRelocation`, `Location`, `OnTable`, `OnShelf`, `GameEvent`, `EventKinds`, `Command`, `WireJson` (Protocol — `Serialize<T>` on an enum, for the meta block's spelling only); `Placement`, `Point2`, `Point3`, `Yaw`, `Polygon`, `PlacedModel`, `ZoneStyles`, `Settling` (Geometry); `CompiledScenario`, `Scenario`, `Expectation` (Task 1).
- Produces: `enum StuckReason { NoAskNoEvents, AskReopenedUnchanged, SubmitRefused, NoLegalAnswer }`; `abstract record RunOutcome` with `Decided(Guid WinningTeamId)`, `RoundLimit(int Rounds)`, `Stuck(StuckReason Reason, string Detail)`, `Failed(string Detail)`, `Paused(Guid AskId, string Question)`, and `string Kind` (`decided`, `round_limit`, `stuck`, `failed`, `paused`); `sealed record AskFingerprint(string Question, string Teams, string Legal, bool AllowPass, string? WindowKind)` with `static AskFingerprint Of(Ask)`; `static class HandPlacement` with `IReadOnlyList<ModelRelocation> IntoZone(Projection, CompiledScenario, Guid teamId, Guid unitId, Chooser)`; `sealed record RunSettings(int MaxSteps = 20_000, int FrameDumpOnFailure = 12, bool CheckInvariants = true)`; `sealed record RunReport(string Scenario, ulong Seed, string Driver, RunOutcome Outcome, int Rounds, long LastSeq, int Frames, int AsksAnswered, double DurationMs, IReadOnlyList<string> Failures, string? RecordingPath)` with `bool Passed`; `sealed class RunLoop(IGameDriver driver, CompiledScenario scenario, IAnswerSource answers, Chooser chooser, RunSettings? settings = null)` with `Task<RunReport> RunAsync(CancellationToken ct)`, `FrameProjector Projector`, `Recording BuildRecording(RunReport)`, `IReadOnlyList<string> LastFrames(int n)`; `static class Expectations` with `IReadOnlyList<string> Check(IReadOnlyList<Expectation>, Projection, IReadOnlyList<GameEvent>, RunOutcome, CompiledScenario)`.

Spec: spec → *What a full game means* — the three-row table, "a run that reaches none of them is a bug the simulator exists to find"; "Stuck is the interesting one. A scheduler or interpreter that cannot make progress is a hang in production and a failed assertion here." Module prompt → "Detect stuck precisely: no open Ask and no events emitted, or an Ask answered that reopens unchanged. A stuck run is a failure with the last frames dumped."

**Termination, exactly.**

| End | Condition as this loop computes it |
| --- | --- |
| Decided | Exactly one team has a model whose `Location is OnTable`, and at least one other team has none. A team's models are its participants' armies' models. |
| Round limit | `Projection.Scheduler.Round >= Scenario.RoundCap` and every open Ask is `AskQuestions.EndRound`. |
| Stuck: `NoAskNoEvents` | A step produced no events and no Ask is open. |
| Stuck: `AskReopenedUnchanged` | The Ask answered at step *n* is closed and an Ask with the same `AskFingerprint` opens, with no event other than the three Ask events in between. **This check is defeatable and is not the only guard:** a single non-Ask event since the last answer clears the suspicion, so an engine that emits one cosmetic event per turn of the loop re-Asks forever without ever tripping it, and is caught only by `RunSettings.MaxSteps` (20 000), which is the backstop. |
| Stuck: `SubmitRefused` | An answer the driver refuses. Under `strict` that is a real bug in the policy or the engine, and the run stops rather than looping. |
| Stuck: `NoLegalAnswer` | `NoLegalAnswerException`: a legal set with no answer and no pass. |
| Failed | An `UnscriptedAskException`, a `RestTimeoutException` (the socket driver waited out `RestTimeout` for `AtRest`), a failed expectation, or a failed invariant. |

**A cap of *n* means *n* whole rounds.** The boundary condition is not a detail: `Round >= RoundCap` **and every open Ask is the round-boundary Ask** stops the run at the end of round *n*, where `Round > RoundCap` alone would let round *n* + 1 start and stop part-way through it. A cap of two therefore ends after the second round, not during the third. Two reasons for that reading. A cap is a maximum number of rounds, so exceeding it by a fraction of a round is a cap that does not hold; and stopping on the boundary Ask leaves the recording ending on a whole round, which is what a viewer's round scrubber and every `ReachedRound` expectation assume. The boundary Ask always arrives, so the loop cannot sail past the cap waiting for it: the scheduler cannot advance a round except through an answered `end_round` Ask, which is invariant 5, `TheRoundAdvancedOnlyThroughTheBoundaryAsk` (Task 7). This is the condition the design document records (`docs/design/simulation/design.md` → *The run loop and the four outcomes*); the table above and `RunLoop.DriveAsync` state it identically, and `A_cap_of_two_stops_at_the_end_of_the_second_round` pins it.

**Why the loop places models by hand.** Deployment's Ask is `deploy` over a `PieceSelection` of units — and it is the question name that says so, not the shape, which is what keeps a `PieceSelection` of model ids such as a Takedown's pick (p.14) out of this path; the scheduler spec's phase ends "when the last model of that unit has a placement", and moving models by hand is an ordinary `MoveModels` command, not an Ask (scheduler → The turn is an Ask: "moving models by hand never ends one"). So after a `deploy` answer the loop puts that unit's models on the table itself, inside the team's own deployment zone, using `ZoneStyles.Resolve` — geometry from a library, not a rules outcome the engine should have computed. Nothing checks whether the placement is *good*; placement is free under every enforcement setting (`architecture.md` → Rules enforcement: "free placement, with base overlap, terrain intersection, and broken coherency flagged rather than prevented").

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class RunLoopTests
{
    static async Task<RunReport> Run(string name, ulong seed = 1, Action<ScenarioBuilder>? tweak = null)
    {
        var scenario = ScenarioCatalog.Get(name).WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, TestContext.Current.CancellationToken);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed ^ 0xF00D));
        return await loop.RunAsync(TestContext.Current.CancellationToken);
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
        await using var driver = await InProcessDriver.StartAsync(compiled, TestContext.Current.CancellationToken);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(AnswerScript.Empty, new AnswerPolicy(new Chooser(1))), new Chooser(2));
        await loop.RunAsync(TestContext.Current.CancellationToken);
        var placed = loop.Projector.Frames
            .First(f => f.Round >= 1 && f.Kind == EventKinds.Of(new RoundStarted(1, [], [])))
            .Models.Count(m => m.Where == "table");
        Assert.True(placed >= 12, $"only {placed} of 18 models were on the table at round one");
    }

    // Reproducibility is same-dice and same-answers, not same-ids: the seed is stored on the game and
    // the ids are ordinary v7 GUIDs, so nothing here may compare an id across two runs (Global
    // Constraints → Reproducibility). RunOutcome.Decided carries a team *id*, so the comparison is on
    // Kind plus the winner's ordinal.
    static async Task<(RunReport Report, int? WinnerOrdinal, IReadOnlyList<int> Dice)> RunTwice(string name, ulong seed)
    {
        var scenario = ScenarioCatalog.Get(name).WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        await using var driver = await InProcessDriver.StartAsync(compiled, TestContext.Current.CancellationToken);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed ^ 0xF00D));
        var report = await loop.RunAsync(TestContext.Current.CancellationToken);
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
        await using var driver = await InProcessDriver.StartAsync(compiled, TestContext.Current.CancellationToken);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(7))), new Chooser(7));
        var report = await loop.RunAsync(TestContext.Current.CancellationToken);

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
        await using var driver = await InProcessDriver.StartAsync(compiled, TestContext.Current.CancellationToken);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, null), new Chooser(1));
        var report = await loop.RunAsync(TestContext.Current.CancellationToken);
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
        await using var driver = await InProcessDriver.StartAsync(compiled, TestContext.Current.CancellationToken);
        var loop = new RunLoop(driver, compiled,
            new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(3))), new Chooser(3));
        var report = await loop.RunAsync(TestContext.Current.CancellationToken);

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
/// Five ends, not the three spec → What a full game means tabulates. Decided, RoundLimit and Stuck are the
/// spec's; Failed is an assertion breach — an unscripted Ask in a script-only run, a failed expectation or a
/// failed invariant — and Paused is the step-and-wait seam of Task 9. The spec's table is asked to name all
/// five in spec correction 11, because as written it reads as exhaustive and is not.
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
    private const double Spacing = 1.6;   // just over a 32 mm base, so a unit lands in a loose clump
    private const int Attempts = 40;

    public static IReadOnlyList<ModelRelocation> IntoZone(
        Projection projection, CompiledScenario scenario, Guid teamId, Guid unitId, Chooser chooser)
    {
        var unit = projection.Units[unitId];
        var table = scenario.Board.Table;
        var ordinal = scenario.Info.Teams.Single(t => t.Id == teamId).Ordinal;
        var shape = table.Zones.FirstOrDefault(z => z.TeamOrdinal == ordinal)?.Shape;
        var anchor = Anchor(shape, table.Size, chooser);

        var moves = new List<ModelRelocation>();
        var shelved = unit.Definition.Models.Where(m => projection.Models[m.Id].Location is OnShelf).ToList();
        for (var i = 0; i < shelved.Count; i++)
        {
            var point = Spread(anchor, i, shape, table.Size);
            moves.Add(new ModelRelocation(shelved[i].Id, projection.Models[shelved[i].Id].Location,
                new OnTable(new Placement(new Point3(point.X, point.Y, 0), new Yaw(0)))));
        }
        return moves;
    }

    private static Point2 Anchor(string? shape, TableSize table, Chooser chooser)
    {
        for (var i = 0; i < Attempts; i++)
        {
            var candidate = new Point2(chooser.Double() * table.Width, chooser.Double() * table.Length);
            if (shape is null || ZoneStyles.Contains(shape, table, candidate)) return candidate;
        }
        return new Point2(table.Width / 2, table.Length / 2);
    }

    /// <summary>A square-ish clump about the anchor, pulled back onto the zone when a member falls outside it.</summary>
    private static Point2 Spread(Point2 anchor, int index, string? shape, TableSize table)
    {
        var side = (int)Math.Ceiling(Math.Sqrt(index + 1));
        var candidate = new Point2(anchor.X + index % side * Spacing, anchor.Y + index / side * Spacing);
        if (shape is null || ZoneStyles.Contains(shape, table, candidate)) return candidate;
        var back = new Point2(anchor.X - index % side * Spacing, anchor.Y - index / side * Spacing);
        return shape is null || ZoneStyles.Contains(shape, table, back) ? back : anchor;
    }
}
```

The `Z` is `0` and the server settles it: the interpreter plan's move step calls `Settling.SurfaceHeightAt`, and hand moves are "copied as sent, including the client's `Z`" (game server skeleton plan → Interfaces needed 3) — so a model deployed onto a hill sits at `0` until it next moves, which is a cosmetic wrong that the simulator does not exist to fix. Named here so nobody mistakes it for a bug in geometry.
- [ ] **Step 4: Write `RunReport.cs` and the expectation evaluation**

```csharp
namespace Tabletop.Sim.Run;

/// <param name="MaxSteps">
/// A hard stop, so a bug in this loop cannot spin forever — and the only guard against an engine that makes
/// no real progress while emitting one non-Ask event per pass, which clears the AskReopenedUnchanged check.
/// </param>
public sealed record RunSettings(int MaxSteps = 20_000, int FrameDumpOnFailure = 12, bool CheckInvariants = true);

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
                    failures.Add($"expected team {ordinal} wiped, it has {ModelsOnTable(projection, scenario, ordinal)} models on the table");
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

    public static int ModelsOnTable(Projection projection, CompiledScenario scenario, int teamOrdinal)
    {
        var teamId = scenario.Info.Teams.Single(t => t.Ordinal == teamOrdinal).Id;
        return ModelsOnTable(projection, scenario, teamId);
    }

    public static int ModelsOnTable(Projection projection, CompiledScenario scenario, Guid teamId) =>
        projection.Models.Values.Count(m =>
            m.Location is OnTable && scenario.TeamOfParticipant.GetValueOrDefault(projection.Units[m.UnitId].ParticipantId) == teamId);
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
    private int _sinceLastAnswer;

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

            var projection = await driver.ProjectionAsync(ct);
            if (Decided(projection) is { } winner) return new RunOutcome.Decided(winner);

            var open = result.OpenAsks.Where(a => a.Ask.Teams.Any(t => !a.Answers.ContainsKey(t))).ToList();
            if (open.Count == 0)
            {
                if (result.Events.Count == 0)
                    return new RunOutcome.Stuck(StuckReason.NoAskNoEvents, $"no ask is open and nothing was emitted at seq {projection.LastSeq}");
                continue;
            }

            // n whole rounds, ending on round n's boundary Ask rather than part-way through round n + 1.
            // The boundary Ask always comes: the scheduler cannot advance a round without one (invariant 5).
            if (projection.Scheduler.Round >= scenario.Scenario.RoundCap &&
                open.All(a => a.Ask.Question == AskQuestions.EndRound))
                return new RunOutcome.RoundLimit(projection.Scheduler.Round);

            if (driver.Mode == DriverMode.StepAndWait) return new RunOutcome.Paused(open[0].Ask.Id, open[0].Ask.Question);

            if (Reopened(open[0].Ask, result.Events) is { } stuck) return stuck;

            var submitted = await AnswerAsync(open[0], projection, ct);
            if (submitted is { } refusal) return refusal;
        }
        return new RunOutcome.Stuck(StuckReason.NoAskNoEvents, $"the loop ran {_settings.MaxSteps} steps without an end");
    }

    private async Task<RunOutcome?> AnswerAsync(OpenAsk open, Projection projection, CancellationToken ct)
    {
        // The whole state, because the ruling of 2026-09-06 lets an answer read it. AnswerPolicy, the
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
            _sinceLastAnswer = 0;
            Absorb(await driver.DrainEventsAsync(ct));

            if (DeployQuestions.Contains(open.Ask.Question) && answer is SelectedPieces selected)
                foreach (var unitId in selected.PieceIds) await PlaceAsync(teamId, unitId, ct);

            if (open.Ask.Sealed) continue;      // a sealed Ask wants every addressed team's answer before it closes
            break;
        }
        return null;
    }

    private async Task PlaceAsync(Guid teamId, Guid unitId, CancellationToken ct)
    {
        var projection = await driver.ProjectionAsync(ct);
        if (!projection.Units.ContainsKey(unitId)) return;
        var moves = HandPlacement.IntoZone(projection, scenario, teamId, unitId, chooser);
        if (moves.Count == 0) return;
        await driver.SubmitAsync(driver.ParticipantFor(teamId), new MoveModels(moves), ct);
        Absorb(await driver.DrainEventsAsync(ct));
    }

    /// <summary>
    /// An Ask answered and reopening unchanged, with nothing but the three Ask events between. One non-Ask
    /// event since the last answer clears the suspicion, so this catches a tight re-Ask loop and nothing
    /// slower: an engine emitting one cosmetic event per pass re-Asks forever and is caught by
    /// RunSettings.MaxSteps instead, which is the backstop and not a formality.
    /// </summary>
    private RunOutcome? Reopened(Ask ask, IReadOnlyList<GameEvent> since)
    {
        _sinceLastAnswer += since.Count(e => e.Payload is not (AskOpened or AskAnswered or AskClosed));
        if (_lastAnswered is null || _sinceLastAnswer > 0) return null;
        return AskFingerprint.Of(ask) == _lastAnswered
            ? new RunOutcome.Stuck(StuckReason.AskReopenedUnchanged, $"'{ask.Question}' reopened unchanged after being answered")
            : null;
    }

    /// <summary>One team with models on the table and at least one other with none (spec → What a full game means).</summary>
    private Guid? Decided(Projection projection)
    {
        var standing = scenario.TeamIds.Where(t => Expectations.ModelsOnTable(projection, scenario, t) > 0).ToList();
        if (projection.Scheduler.Round < 1) return null;                       // deployment: the shelf is not a wipe
        return standing.Count == 1 && scenario.TeamIds.Count > 1 ? standing[0] : null;
    }

    private void Absorb(IReadOnlyList<GameEvent> events)
    {
        foreach (var e in events) { _log.Add(e); Projector.Add(e); }
    }

    public IReadOnlyList<string> LastFrames(int n) =>
        Projector.Frames.TakeLast(n).Select(f => $"  [{f.Index}] seq {f.Seq} {f.Kind}: {f.Line}").ToList();

    public Recording BuildRecording(RunReport report) => Projector.Build(new RecordingMeta(
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

---
### Task 7: The five invariants

**Files:**
- Create: `sim/Invariants/ProjectionComparer.cs`, `sim/Invariants/Invariants.cs`
- Modify: `sim/Run/RunLoop.cs` (run the invariants at the end when `RunSettings.CheckInvariants`)
- Test: `tests/Tabletop.Sim.Tests/InvariantTests.cs`

**Interfaces:**
- Consumes: `Projection`, `UnitInPlay`, `ModelInPlay`, `SchedulerPosition`, `Frame`, `OpenAsk`, `EventApplier`, `GameEvent`, `EventPayload` members, `Location` (`OnTable`, `OnShelf`, `Inside`), `ModelRelocation`, `AskQuestions`, `Board`, `GameInfo`, `Undo` (Protocol); `Replay` (game server skeleton); `Distance`, `TableSize` (Geometry); `IGameDriver`, `InProcessDriver` (Task 4); `CompiledScenario` (Task 1); `RunOutcome` (Task 6).
- Produces: `static class ProjectionComparer` with `IReadOnlyList<string> Differences(Projection expected, Projection actual)`; `sealed record InvariantResult(string Name, bool Held, string? Detail)`; `static class Invariants` with `IReadOnlyList<InvariantResult> CheckAll(Projection driverProjection, IReadOnlyList<GameEvent> log, CompiledScenario scenario)`, and one method per invariant — `InvariantResult ReplayReproducesTheProjection(...)`, `InvariantResult UndoAndRedoReproduceTheDice(...)`, `InvariantResult EveryAskAnsweredByAnAddressedTeam(...)`, `InvariantResult NoModelInTwoPlacesOrOffTheSlab(...)`, `InvariantResult TheRoundAdvancedOnlyThroughTheBoundaryAsk(...)`; `static class MidRun` with `void Assert(Projection, IReadOnlyList<GameEvent>, CompiledScenario)` (throws `InvariantViolatedException` with every failure); `sealed class InvariantViolatedException(IReadOnlyList<InvariantResult>) : Exception`.

Spec: spec → *What it asserts*, all five bullets verbatim:
- Replaying the log from the initial state reproduces the final projection.
- Undoing to an arbitrary sequence and redoing reproduces the same dice, because the seed is stored on the game and never changes.
- Every Ask is answered by a team that Ask addressed.
- No model is in two places, and no model on the table is outside the slab.
- The round only advances through the boundary Ask, per the ruling of 2026-09-06.

**Each one, as this task computes it.**

1. **Replay.** `Replay.To(board, info, log, log[^1].Seq)` against the driver's own projection, through `ProjectionComparer`. In process the same comparison also runs against `InProcessDriver.ServerProjectionAsync`, which is the stronger form: the server's live projection, the driver's incrementally-applied one, and a cold replay all three agree.
2. **Undo and the dice.** `Undone(N)` shortens the effective log, and the dice are counter-based on `(GameState.RngSeed, cursor)` (interpreter plan Task 5) where `RngSeed` is the value stored on the game row and read at hydration, so the *k*th die of the game is a pure function of that seed and *k*. The check is therefore over the log alone and needs no second run: `EventApplier.EffectiveLog` after an appended `Undone(N)` must contain the same dice, in the same order, at the same cursor positions as the original log's first *N* sequences. Doing it for every `N` at which a `step_completed` carrying dice appears covers "an arbitrary sequence" exhaustively rather than by sampling. A *redo* that re-rolls is then guaranteed by the same arithmetic, and is checked once for real in `DriverAgreementTests` (Task 8) by submitting `Undo` to the driver and replaying the recorded answers.
3. **Addressed teams.** For each `AskAnswered`, the `AskOpened` with that id must have the answering team in `Ask.Teams`. An `AskClosed` with an empty answer list is a superseded scheduler Ask and is not an answer (scheduler plan → spec correction 4).
4. **One place, on the slab.** Walk the log: for each `ModelRelocation`, `From` must equal the model's location before the event, so no move invents a location; and every `OnTable` position must satisfy `0 ≤ x ≤ width`, `0 ≤ y ≤ length` on the final projection and on every intermediate one.
5. **The round boundary.** For every `RoundStarted` with `Round ≥ 2`, either the nearest preceding `AskClosed` closed an Ask whose question is `AskQuestions.EndRound`, or the `GameEvent.Actor` is non-null — which is the menu's `set_round`, an override that is "visible to opponents and recorded in the event log, never silent" (`architecture.md` → Rules enforcement). Rounds 0 and 1 are deployment's flip into round one and are exempt.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Invariants;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class InvariantTests
{
    static async Task<(CompiledScenario Scenario, RunLoop Loop, RunReport Report, IGameDriver Driver)> Played(ulong seed = 1)
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        var driver = await InProcessDriver.StartAsync(compiled, TestContext.Current.CancellationToken);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed));
        var report = await loop.RunAsync(TestContext.Current.CancellationToken);
        return (compiled, loop, report, driver);
    }

    [Fact]
    public async Task Every_invariant_holds_over_a_whole_game()
    {
        var (scenario, loop, report, driver) = await Played();
        await using var _ = driver;
        var results = Invariants.CheckAll(await driver.ProjectionAsync(TestContext.Current.CancellationToken), loop.Log, scenario);
        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.True(r.Held, $"{r.Name}: {r.Detail}"));
        Assert.Empty(report.Failures);
    }

    [Fact]
    public async Task The_run_loop_reports_a_broken_invariant_as_a_failure()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        // A round that started with no boundary Ask and no actor: exactly what the ruling of 2026-09-06 forbids.
        var forged = loop.Log.Append(new GameEvent(loop.Log[^1].Seq + 1, null, new RoundStarted(9, scenario.TeamIds, []), null)).ToList();
        var result = Invariants.TheRoundAdvancedOnlyThroughTheBoundaryAsk(forged);
        Assert.False(result.Held);
        Assert.Contains("round 9", result.Detail);
    }

    [Fact]
    public async Task A_cold_replay_the_incremental_projection_and_the_servers_all_agree()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        var mine = await driver.ProjectionAsync(TestContext.Current.CancellationToken);
        var cold = Replay.To(scenario.Board, scenario.Info, loop.Log, loop.Log[^1].Seq);
        Assert.Empty(ProjectionComparer.Differences(cold, mine));
        var server = await ((InProcessDriver)driver).ServerProjectionAsync(TestContext.Current.CancellationToken);
        Assert.Empty(ProjectionComparer.Differences(server, mine));
    }

    [Fact]
    public async Task An_undo_to_any_dice_bearing_sequence_reproduces_the_dice()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        var result = Invariants.UndoAndRedoReproduceTheDice(loop.Log, scenario);
        Assert.True(result.Held, result.Detail);
    }

    [Fact]
    public void The_comparer_names_what_differs()
    {
        var scenario = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));
        var a = Projection.Initial(scenario.Board, scenario.Info);
        var applier = new EventApplier(scenario.Board, scenario.Info);
        var model = scenario.Board.Armies[0].Snapshot.Units[0].Models[0].Id;
        applier.Apply(new GameEvent(1, null, new WoundsSet(model, 2), null));
        var differences = ProjectionComparer.Differences(a, applier.Projection);
        Assert.Contains(differences, d => d.Contains("LastSeq"));
        Assert.Contains(differences, d => d.Contains("Wounds"));
    }

    [Fact]
    public async Task Mid_run_assertion_throws_with_every_failure_named()
    {
        var (scenario, loop, _, driver) = await Played();
        await using var _ = driver;
        MidRun.Assert(await driver.ProjectionAsync(TestContext.Current.CancellationToken), loop.Log, scenario);   // does not throw
        var forged = loop.Log.Append(new GameEvent(loop.Log[^1].Seq + 1, null, new RoundStarted(9, scenario.TeamIds, []), null)).ToList();
        var thrown = Assert.Throws<InvariantViolatedException>(() =>
            MidRun.Assert(Replay.To(scenario.Board, scenario.Info, forged, forged[^1].Seq), forged, scenario));
        Assert.Contains("round", thrown.Message);
    }
}
```

`RunLoop` therefore exposes its log: add `public IReadOnlyList<GameEvent> Log => _log;` beside `Projector`.
- [ ] **Step 2: Run; expect failure, then write `ProjectionComparer.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter InvariantTests`
Expected: `CS0246: ... 'Invariants' could not be found`.

```csharp
using Tabletop.Protocol;

namespace Tabletop.Sim.Invariants;

/// <summary>
/// Projection is a class, not a record, so it has no structural equality. This is that equality, field by
/// field, reporting every difference rather than the first — a wrong projection usually differs in a pattern.
/// </summary>
public static class ProjectionComparer
{
    public static IReadOnlyList<string> Differences(Projection expected, Projection actual)
    {
        var differences = new List<string>();
        void Check(string what, object? a, object? b)
        {
            if (!Equals(a, b)) differences.Add($"{what}: expected {a}, got {b}");
        }

        Check("LastSeq", expected.LastSeq, actual.LastSeq);
        Check("Scheduler.Round", expected.Scheduler.Round, actual.Scheduler.Round);
        Check("Scheduler.Phase", expected.Scheduler.Phase, actual.Scheduler.Phase);
        Check("Scheduler.ActiveTeamId", expected.Scheduler.ActiveTeamId, actual.Scheduler.ActiveTeamId);
        Check("Scheduler.Order", string.Join(",", expected.Scheduler.Order), string.Join(",", actual.Scheduler.Order));
        Check("Scheduler.Passed", string.Join(",", expected.Scheduler.Passed.Order()), string.Join(",", actual.Scheduler.Passed.Order()));
        Check("Stack", string.Join(",", expected.Stack.Select(f => $"{f.Id:N}/{f.Sequence}/{f.Steps.Count}")),
                        string.Join(",", actual.Stack.Select(f => $"{f.Id:N}/{f.Sequence}/{f.Steps.Count}")));
        Check("OpenAsks", string.Join(",", expected.OpenAsks.Keys.Order()), string.Join(",", actual.OpenAsks.Keys.Order()));
        Check("Markers", string.Join(",", expected.Markers.Keys.Order()), string.Join(",", actual.Markers.Keys.Order()));
        Check("Scores", string.Join(",", expected.Scores.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value}")),
                        string.Join(",", actual.Scores.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value}")));

        foreach (var id in expected.Models.Keys.Union(actual.Models.Keys).Order())
        {
            if (!expected.Models.TryGetValue(id, out var a) || !actual.Models.TryGetValue(id, out var b))
            { differences.Add($"model {id:N} is in one projection only"); continue; }
            Check($"model {id:N} Location", a.Location, b.Location);
            Check($"model {id:N} Wounds", a.Wounds, b.Wounds);
            Check($"model {id:N} SpellTokens", a.SpellTokens, b.SpellTokens);
            Check($"model {id:N} Conditions", string.Join(",", a.Conditions.Order()), string.Join(",", b.Conditions.Order()));
        }

        foreach (var id in expected.Units.Keys.Union(actual.Units.Keys).Order())
        {
            if (!expected.Units.TryGetValue(id, out var a) || !actual.Units.TryGetValue(id, out var b))
            { differences.Add($"unit {id:N} is in one projection only"); continue; }
            Check($"unit {id:N} Actions", string.Join(",", a.Actions.Order()), string.Join(",", b.Actions.Order()));
            Check($"unit {id:N} Conditions", string.Join(",", a.Conditions.Order()), string.Join(",", b.Conditions.Order()));
            Check($"unit {id:N} Grants", string.Join(",", a.Grants.Select(g => g.Id).Order()), string.Join(",", b.Grants.Select(g => g.Id).Order()));
            Check($"unit {id:N} WoundsThisActivation", a.WoundsThisActivation, b.WoundsThisActivation);
        }
        return differences;
    }
}
```

- [ ] **Step 3: Write `Invariants.cs`**

```csharp
using Tabletop.GameServer.Log;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Invariants;

public sealed record InvariantResult(string Name, bool Held, string? Detail);

public sealed class InvariantViolatedException(IReadOnlyList<InvariantResult> failures)
    : Exception("invariants violated:\n" + string.Join("\n", failures.Select(f => $"  {f.Name}: {f.Detail}")))
{
    public IReadOnlyList<InvariantResult> Failures { get; } = failures;
}

/// <summary>
/// The invariants that hold for any game, whatever the ruleset (spec → What it asserts). These read state
/// freely — wounds, conditions, positions, the scheduler position — which the ruling of 2026-09-06 allows.
/// What they contain is no second implementation of the rules: each one is a statement about the log, the
/// projection and the slab, and none of them recomputes an outcome the engine produced.
/// </summary>
public static class Invariants
{
    public static IReadOnlyList<InvariantResult> CheckAll(Projection driverProjection, IReadOnlyList<GameEvent> log, CompiledScenario scenario) =>
    [
        ReplayReproducesTheProjection(driverProjection, log, scenario),
        UndoAndRedoReproduceTheDice(log, scenario),
        EveryAskAnsweredByAnAddressedTeam(log),
        NoModelInTwoPlacesOrOffTheSlab(log, scenario),
        TheRoundAdvancedOnlyThroughTheBoundaryAsk(log),
    ];

    public static InvariantResult ReplayReproducesTheProjection(Projection driverProjection, IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        if (log.Count == 0) return new InvariantResult(nameof(ReplayReproducesTheProjection), true, null);
        var cold = Replay.To(scenario.Board, scenario.Info, log, log[^1].Seq);
        var differences = ProjectionComparer.Differences(cold, driverProjection);
        return new InvariantResult(nameof(ReplayReproducesTheProjection), differences.Count == 0, string.Join("; ", differences.Take(10)));
    }

    /// <summary>
    /// The dice are counter-based on (RngSeed, cursor), and RngSeed is the seed stored on the game row, so it
    /// never changes for a game: die k is a pure function of that seed and k, and Undone(N) shortens the
    /// effective log rather than rewriting it. Checked at every sequence that carries dice, which is
    /// "an arbitrary sequence" exhaustively.
    /// </summary>
    public static InvariantResult UndoAndRedoReproduceTheDice(IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var original = DiceOf(log);
        foreach (var target in log.Where(e => e.Payload is StepCompleted { Rolls.Count: > 0 }).Select(e => e.Seq))
        {
            var undone = log.Append(new GameEvent(log[^1].Seq + 1, null, new Undone(target), null)).ToList();
            var effective = EventApplier.EffectiveLog(undone);
            var kept = DiceOf(effective);
            var expected = original.Take(kept.Count).ToList();
            if (!kept.SequenceEqual(expected))
                return new InvariantResult(nameof(UndoAndRedoReproduceTheDice), false,
                    $"undoing to {target} changed the dice: kept [{string.Join(",", kept)}] against [{string.Join(",", expected)}]");
        }
        return new InvariantResult(nameof(UndoAndRedoReproduceTheDice), true, null);
    }

    private static IReadOnlyList<int> DiceOf(IReadOnlyList<GameEvent> log) => log
        .SelectMany(e => e.Payload switch
        {
            StepCompleted s => s.Rolls.SelectMany(r => r.Dice.Select(d => d.Raw)),
            DiceRerolled d => d.Replaced.Select(r => r.New.Raw),
            _ => [],
        })
        .ToList();

    public static InvariantResult EveryAskAnsweredByAnAddressedTeam(IReadOnlyList<GameEvent> log)
    {
        var asked = log.Where(e => e.Payload is AskOpened).ToDictionary(e => ((AskOpened)e.Payload).Ask.Id, e => ((AskOpened)e.Payload).Ask);
        foreach (var e in log)
        {
            if (e.Payload is not AskAnswered answered) continue;
            if (!asked.TryGetValue(answered.AskId, out var ask))
                return new InvariantResult(nameof(EveryAskAnsweredByAnAddressedTeam), false, $"seq {e.Seq} answers ask {answered.AskId:N}, which was never opened");
            if (!ask.Teams.Contains(answered.TeamId))
                return new InvariantResult(nameof(EveryAskAnsweredByAnAddressedTeam), false,
                    $"seq {e.Seq}: team {answered.TeamId:N} answered '{ask.Question}', addressed to {string.Join(",", ask.Teams.Select(t => t.ToString("N")))}");
        }
        return new InvariantResult(nameof(EveryAskAnsweredByAnAddressedTeam), true, null);
    }

    public static InvariantResult NoModelInTwoPlacesOrOffTheSlab(IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var where = new Dictionary<Guid, Location>();
        foreach (var model in scenario.Board.Armies.SelectMany(a => a.Snapshot.Units).SelectMany(u => u.Models)) where[model.Id] = new OnShelf();
        var size = scenario.Board.Table.Size;

        foreach (var e in EventApplier.EffectiveLog(log))
        {
            foreach (var move in Relocations(e.Payload))
            {
                if (!where.TryGetValue(move.ModelId, out var was))
                    return Fail($"seq {e.Seq} moves model {move.ModelId:N}, which is not in any army");
                if (!Equals(was, move.From))
                    return Fail($"seq {e.Seq} moves model {move.ModelId:N} from {move.From}, but it was at {was}");
                where[move.ModelId] = move.To;
                if (move.To is OnTable table)
                {
                    var p = table.Placement.Position;
                    if (p.X < 0 || p.X > size.Width || p.Y < 0 || p.Y > size.Length)
                        return Fail($"seq {e.Seq} puts model {move.ModelId:N} at {p.X},{p.Y}, off a {size.Width}x{size.Length} slab");
                }
            }
        }
        return new InvariantResult(nameof(NoModelInTwoPlacesOrOffTheSlab), true, null);

        InvariantResult Fail(string detail) => new(nameof(NoModelInTwoPlacesOrOffTheSlab), false, detail);
    }

    private static IEnumerable<ModelRelocation> Relocations(EventPayload payload) => payload switch
    {
        ModelsMoved m => m.Moves,
        RoundStarted r => r.Changes.SelectMany(Relocations),
        TurnEnded t => t.Changes.SelectMany(Relocations),
        StepCompleted s => s.Changes.SelectMany(Relocations),
        _ => [],
    };

    /// <summary>Ruled 2026-09-06: the round does not advance on its own (scheduler plan → spec correction 5, game flow → 11).</summary>
    public static InvariantResult TheRoundAdvancedOnlyThroughTheBoundaryAsk(IReadOnlyList<GameEvent> log)
    {
        var effective = EventApplier.EffectiveLog(log);
        var asked = effective.Where(e => e.Payload is AskOpened).ToDictionary(e => ((AskOpened)e.Payload).Ask.Id, e => ((AskOpened)e.Payload).Ask);
        for (var i = 0; i < effective.Count; i++)
        {
            if (effective[i].Payload is not RoundStarted { Round: >= 2 } started) continue;
            if (effective[i].Actor is not null) continue;                                   // the menu's set_round: an override, and logged
            var closed = effective.Take(i).LastOrDefault(e => e.Payload is AskClosed);
            var question = closed?.Payload is AskClosed c && asked.TryGetValue(c.AskId, out var ask) ? ask.Question : null;
            if (question != AskQuestions.EndRound)
                return new InvariantResult(nameof(TheRoundAdvancedOnlyThroughTheBoundaryAsk), false,
                    $"seq {effective[i].Seq} started round {started.Round} without a closed '{AskQuestions.EndRound}' ask (nearest closed ask: {question ?? "none"})");
        }
        return new InvariantResult(nameof(TheRoundAdvancedOnlyThroughTheBoundaryAsk), true, null);
    }
}

/// <summary>The mid-run entry point the module prompt asks for: assert now, and throw with every failure named.</summary>
public static class MidRun
{
    public static void Assert(Projection projection, IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var failures = Invariants.CheckAll(projection, log, scenario).Where(r => !r.Held).ToList();
        if (failures.Count > 0) throw new InvariantViolatedException(failures);
    }
}
```

- [ ] **Step 4: Wire the invariants into `RunLoop.RunAsync`**

Insert after the expectation check, before the `RunReport` is built:

```csharp
        if (_settings.CheckInvariants)
            failures.AddRange(Invariants.Invariants.CheckAll(projection, _log, scenario)
                .Where(r => !r.Held).Select(r => $"{r.Name}: {r.Detail}"));

        if (driver is InProcessDriver inProcess)
        {
            var server = await inProcess.ServerProjectionAsync(ct);
            failures.AddRange(ProjectionComparer.Differences(server, projection)
                .Select(d => $"the driver's projection differs from the server's — {d}"));
        }
```

The invariants run **after** the recording is complete, because the undo invariant reads the log rather than writing to it and the projection comparison reads state; neither appends an event, so the recording is unaffected either way. `DriverAgreementTests` (Task 8) is the one place a real `Undo` command is submitted, and it runs after its own recording is written.

- [ ] **Step 5: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter InvariantTests
git add sim/Invariants sim/Run tests/Tabletop.Sim.Tests
git commit -m "Assert the five invariants that hold for any game"
```

---
### Task 8: The socket driver, and the two drivers agree

**Files:**
- Create: `sim/Drivers/IWebSocketConnector.cs`, `sim/Drivers/SocketRowSeeder.cs`, `sim/Drivers/SocketDriver.cs`
- Test: `tests/Tabletop.Sim.Tests/DriverAgreementTests.cs`

**Interfaces:**
- Consumes: `IGameDriver`, `SubmitResult`, `StepResult`, `DriverMode` (Task 4); `IGameRows`, `PostgresGameRows`, `GameServerFactory`, `TestKeys` (game server skeleton; the last two only from the test project); `SocketMessage` and every kind, `WireJson`, `ProtocolVersion`, `Connect`, `Welcome`, `Rejected`, `BoardMessage`, `EventsMessage`, `CommandMessage`, `Refused`, `GameEvent`, `EventApplier`, `Projection`, `OpenAsk`, `Board`, `GameInfo`, `GameStatus`, `SchedulerShape`, `Enforcement` (Protocol); `ArmySnapshot`, `TableState`, `ContentJson` (Content); `NpgsqlDataSource` (Npgsql); `CompiledScenario` (Task 1).
- Produces: `interface IWebSocketConnector { Task<WebSocket> ConnectAsync(CancellationToken ct); }`; `sealed class ClientWebSocketConnector(Uri uri) : IWebSocketConnector`; `sealed record MintedSession(string Jwt, Guid AccountId)`; `sealed record SocketCredentials(IReadOnlyList<MintedSession> Sessions)` with `static Task<SocketCredentials> MintAsync(CompiledScenario, HttpClient, CancellationToken)`, `static string NameFor(CompiledScenario, int index)`, `Guid AccountOf(int index)`, `string For(int index)`; `sealed class SocketRowSeeder(NpgsqlDataSource db, IGameRows rows)` with `Task SeedAsync(CompiledScenario, SocketCredentials, CancellationToken)` and `Task DeleteAsync(Guid gameId, CancellationToken)`, plus `sealed class PropertiesMissingException(string hash)`; `sealed class SocketDriver : IGameDriver` with `static Task<SocketDriver> StartAsync(CompiledScenario, IWebSocketConnector, SocketCredentials, NpgsqlDataSource, CancellationToken)`, `TimeSpan RestTimeout { get; init; }`, `Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken)`.

Spec: spec → *Two drivers behind one interface* — "Over the socket. A real WebSocket to a running game server, a real session JWT, real rows and a real event log in Postgres. This exercises the handshake, the wire format, hydration, reconnect and the whole path `architecture.md` calls the only path"; "The same scenario passing in process and failing over the socket localises the fault to transport, serialization or persistence, which is the reason both exist." Module prompt → "Prefer writing rows directly through `IGameRows` plus a test-signed JWT, as the game server's own tests do."

**How the rows are written.** `account`, `game` (at `lobby`, with the scenario's own id, its configuration and its `rng_seed`), `team` and `participant` go in as raw SQL, because those four are management's writes and `IGameRows` has no method for them (game server skeleton plan → Interfaces needed 1). The `rng_seed` column is the whole of how a socket run sets the seed: the in-process driver hands `compiled.Info` straight to the session, while this driver has to put the same number in the row the server will hydrate from. Both therefore run the same dice for the same scenario seed, which is what makes the two-driver agreement test meaningful. Everything after that goes through `IGameRows`: `StartSetupAsync(gameId, snapshot)` writes the whole `table_state` tree and flips to `setup`, and `EndSetupAsync(gameId, snapshot, snapshotsByParticipant)` writes each participant's `army_snapshot` and flips to `playing`. So the scenario's own ids survive — `CreateTeamAsync` would mint its own, and the driver's projection, its recording and its answers are all keyed on the `CompiledScenario`'s ids — and the tree is written by the code that owns it.

**One minted session per team, plus one for the recorder.** A session JWT names one account, `FindParticipantAsync(gameId, accountId)` maps an account to one participant, and a newer socket for a participant closes the older one — so N participants need N accounts and N tokens. In tests `TestKeys.Mint` produces them against the fake JWKS, so the suite reaches no network. From the console they come from the management API's development-only mint, `POST /v1/dev/sessions`, one call per participant in team-ordinal order with the recorder last. Owner ruling 4 of 2026-09-06 settled question 1: hand-pasting tokens is rejected, and management holds the Stytch secret, so the mint lives there and returns a real Stytch session JWT for a named account, creating the Stytch user and the `account` row as needed.

**What that changes here.** `SocketCredentials` no longer reads an environment variable and no longer decodes a JWT to find its account id: `MintAsync` calls the endpoint and the response carries `account.id` beside the token, so `AccountOf` is a lookup. `NameFor(scenario, index)` is `sim-{scenario name}-{index}`, which is stable across runs and distinct within a scenario, so re-running a seed mints for the same accounts rather than accumulating new ones. `SocketRowSeeder` still inserts `management.account` with `on conflict (id) do nothing`, which is now a no-op for a minted account and remains the path for a test-minted one. `--driver socket` needs no token variable at all; it needs `TABLETOP_MANAGEMENT_URL` (default `http://localhost:5100`) and a management API running under `ASPNETCORE_ENVIRONMENT=Development`, which `./scripts/management` sets.

**And what it costs.** A console socket run now reaches Stytch's test project once per participant, because the tokens are real ones minted there. The in-process driver reaches nothing, and neither does `DriverAgreementTests`, which still mints with `TestKeys`. A `404` from the mint means the management API is not running as a development environment, and `MintAsync` says so rather than reporting a bad token later at the handshake.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net.WebSockets;
using Npgsql;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Invariants;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

[Collection("Postgres")]
public class DriverAgreementTests(PostgresFixture pg)
{
    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    // The test never calls management's mint: TestKeys signs against the fake JWKS the factory serves,
    // so DriverAgreementTests reaches no network and needs no Stytch project.
    static SocketCredentials Credentials(CompiledScenario c) =>
        new(c.Info.Participants.Select(p => new MintedSession(TestKeys.Mint(p.AccountId), p.AccountId)).ToList());

    static async Task<RunReport> RunWith(IGameDriver driver, CompiledScenario c, ulong seed, CancellationToken ct)
    {
        var loop = new RunLoop(driver, c, new ComposedAnswers(c.Scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed));
        var report = await loop.RunAsync(ct);
        Assert.All(Invariants.CheckAll(await driver.ProjectionAsync(ct), loop.Log, c), r => Assert.True(r.Held, $"{driver.Name} {r.Name}: {r.Detail}"));
        return report;
    }

    [Fact]
    public async Task The_same_scenario_plays_the_same_game_on_both_drivers()
    {
        const ulong seed = 21;
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(seed);
        var c = ScenarioCompiler.Compile(scenario);
        var ct = TestContext.Current.CancellationToken;

        RunReport inProcess;
        await using (var driver = await InProcessDriver.StartAsync(c, ct)) inProcess = await RunWith(driver, c, seed, ct);

        using var factory = new GameServerFactory();
        var rows = new PostgresGameRows(pg.Db);
        var seeder = new SocketRowSeeder(pg.Db, rows);
        await seeder.SeedAsync(c, Credentials(c), ct);
        try
        {
            RunReport overSocket;
            await using (var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), Credentials(c), pg.Db, ct))
                overSocket = await RunWith(driver, c, seed, ct);

            // Run for run, not merely a legitimate end on each: see the note under this block for why
            // the rest frame makes this sound where the quiet window could not.
            Assert.Equal("in-process", inProcess.Driver);
            Assert.Equal("socket", overSocket.Driver);
            Assert.True(inProcess.Passed, string.Join("\n", inProcess.Failures));
            Assert.True(overSocket.Passed, string.Join("\n", overSocket.Failures));
            Assert.Equal(inProcess.Outcome, overSocket.Outcome);
            Assert.Equal(inProcess.Rounds, overSocket.Rounds);
            Assert.Equal(inProcess.LastSeq, overSocket.LastSeq);
            Assert.Equal(inProcess.Frames, overSocket.Frames);
            Assert.Equal(inProcess.AsksAnswered, overSocket.AsksAnswered);

            // The run reached a legitimate end and did real work, so the equalities above are not two
            // runs agreeing on having gone nowhere.
            Assert.Contains(overSocket.Outcome.Kind, (string[])["decided", "round_limit"]);
            Assert.True(overSocket.Rounds >= 1, $"the socket run only reached round {overSocket.Rounds}");
            Assert.True(overSocket.AsksAnswered > 10);

            // Nothing lost, reordered or duplicated on the wire: seq starts at 1 and rises by one per
            // event, and the recording keeps a frame per event, so Frames == LastSeq on both drivers.
            Assert.Equal(inProcess.LastSeq, (long)inProcess.Frames);
            Assert.Equal(overSocket.LastSeq, (long)overSocket.Frames);
        }
        finally { await seeder.DeleteAsync(c.Info.Id, ct); }
    }

    [Fact]
    public async Task The_log_lands_in_postgres_and_hydration_reproduces_the_game()
    {
        const ulong seed = 22;
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate").WithSeed(seed));
        var ct = TestContext.Current.CancellationToken;
        using var factory = new GameServerFactory();
        var seeder = new SocketRowSeeder(pg.Db, new PostgresGameRows(pg.Db));
        await seeder.SeedAsync(c, Credentials(c), ct);
        try
        {
            long lastSeq;
            Projection played;
            await using (var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), Credentials(c), pg.Db, ct))
            {
                var report = await RunWith(driver, c, seed, ct);
                lastSeq = report.LastSeq;
                played = await driver.ProjectionAsync(ct);
            }
            // PostgresFixture.CountAsync returns int, so the cast is what makes the comparison a long one.
            Assert.Equal(lastSeq, (long)await pg.CountAsync(
                "select coalesce(max(seq), 0) from game.game_event where game_id = $1", c.Info.Id));

            // Every socket gone, so the session left memory; connecting again hydrates from the log alone.
            await using var again = await SocketDriver.StartAsync(c, new FactoryConnector(factory), Credentials(c), pg.Db, ct);
            Assert.Empty(ProjectionComparer.Differences(played, await again.ProjectionAsync(ct)));
        }
        finally { await seeder.DeleteAsync(c.Info.Id, ct); }
    }

    [Fact]
    public async Task Undoing_and_redoing_over_the_socket_reproduces_the_dice()
    {
        const ulong seed = 23;
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(seed));
        var ct = TestContext.Current.CancellationToken;
        using var factory = new GameServerFactory();
        var seeder = new SocketRowSeeder(pg.Db, new PostgresGameRows(pg.Db));
        await seeder.SeedAsync(c, Credentials(c), ct);
        try
        {
            await using var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), Credentials(c), pg.Db, ct);
            var loop = new RunLoop(driver, c, new ComposedAnswers(c.Scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed));
            await loop.RunAsync(ct);

            var before = loop.Log.Where(e => e.Payload is StepCompleted { Rolls.Count: > 0 }).ToList();
            Assert.NotEmpty(before);
            var target = before[before.Count / 2].Seq - 1;

            await driver.SubmitAsync(driver.ParticipantFor(driver.TeamIds[0]), new Undo(target), ct);
            var replay = new RunLoop(driver, c, new ComposedAnswers(Replayed(loop), null), new Chooser(seed));
            await replay.RunAsync(ct);

            var after = replay.Log.Where(e => e.Payload is StepCompleted { Rolls.Count: > 0 }).ToList();
            var overlap = Math.Min(before.Count, after.Count);
            Assert.Equal(
                before.Take(overlap).SelectMany(e => ((StepCompleted)e.Payload).Rolls.SelectMany(r => r.Dice.Select(d => d.Raw))),
                after.Take(overlap).SelectMany(e => ((StepCompleted)e.Payload).Rolls.SelectMany(r => r.Dice.Select(d => d.Raw))));
        }
        finally { await seeder.DeleteAsync(c.Info.Id, ct); }
    }

    /// <summary>The answers the first run gave, replayed in order, so the redo takes the same path.</summary>
    static AnswerScript Replayed(RunLoop loop)
    {
        var builder = AnswerScript.Build();
        foreach (var answered in loop.Answers.History) builder.At(answered.Question, answered.Ordinal, answered.Answer);
        return builder.Build();
    }
}
```

**Why the two drivers are compared run-for-run again.** An earlier draft of this test asserted `inProcess.Outcome == overSocket.Outcome` and equality of `Rounds`, `LastSeq`, `Frames` and `AsksAnswered`. A later draft weakened all five to a legitimate-end check, because the 250 ms quiet window could not guarantee that the two drivers saw the same Asks at the same step boundary. The project owner's ruling of 2026-09-06 removes that reason, so the strict form is back, and this note records why it is now sound rather than why it was dropped.

What the socket transport always guaranteed: every event the server emits reaches the recorder's socket, once, in `seq` order, and `DrainEventsAsync` sorts by `seq` and skips anything already applied, so the socket driver's projection is always a prefix of the same event sequence and never a different one. Per-viewer filtering does not weaken that, because it nulls `ask_answered.Answer` rather than withholding an event (contracts design → *Sequences*: "the filtering nulls a field rather than withholding an event"), so every stream carries every sequence number. That is what the `Frames == LastSeq` assertions check, and it is what a serialization, transport or persistence fault breaks.

What the quiet window could not guarantee was *when* an event was observed. A server slower than 250 ms handed the loop a shorter list of open Asks than the in-process driver would have seen, and two Asks of the same question open at once could then be paired with different `(question, ordinal)` keys, take different answers from the same seeded `Chooser`, and play a different game from there. `AtRest` closes exactly that gap. Both drivers now return from `StepAsync` on a positive rest signal rather than on a guess: the marker at the back of the mailbox in process, the frame on the recorder's socket over the wire. Each step therefore sees every event of the batch and every Ask the batch opened, so the `(question, ordinal)` keys, the answers drawn from the `Chooser` and the sequence of commands are the same on both drivers by construction, for the same scenario and the same seed.

What remains transport-invariant rather than run-identical is stated separately and still holds: both runs pass, both satisfy all five invariants (`RunWith` checks them on each driver), and `Frames == LastSeq` holds on each independently of the other. Those are the assertions that survive a driver disagreement and localise it, which is why they stay even though the run-for-run equalities subsume them on a passing run. The two tests below carry the rest of the localisation the spec asks for: the log lands in Postgres, and hydrating from it alone reproduces the projection. A socket run that now disagrees with the in-process run on outcome, rounds, `LastSeq`, `Frames` or `AsksAnswered` is a real fault in transport, serialization or persistence, which is what this test exists to say.

`RunLoop` therefore exposes its answer source: add `public IAnswerSource Answers => answers;`.

`PostgresFixture.CountAsync` was checked against the game server skeleton plan rather than assumed: it is `public async Task<int> CountAsync(string sql, params object[] args)` (that plan, Task 3's fixture), which does take positional parameters, so the `$1` form above is right and the inline-SQL fallback is not needed. It returns `int` rather than `long` — it ends in `Convert.ToInt32(await cmd.ExecuteScalarAsync())` — which is why the `max(seq)` comparison casts. A game long enough to overflow an `int` sequence is not a game this test runs.
- [ ] **Step 2: Run; expect failure, then write `IWebSocketConnector.cs`**

Run: `docker compose up -d && dotnet test tests/Tabletop.Sim.Tests --filter DriverAgreementTests`
Expected: `CS0246: ... 'SocketDriver' could not be found`.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

/// <summary>Where the socket comes from: a real server over TCP, or a test server in memory.</summary>
public interface IWebSocketConnector
{
    Task<WebSocket> ConnectAsync(CancellationToken ct);
}

public sealed class ClientWebSocketConnector(Uri uri) : IWebSocketConnector
{
    public async Task<WebSocket> ConnectAsync(CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, ct);
        return socket;
    }
}

/// <summary>A real Stytch session JWT and the account it names.</summary>
public sealed record MintedSession(string Jwt, Guid AccountId);

/// <summary>
/// One session per participant, in team-ordinal order with the recorder last. Nothing here verifies a
/// token: the game server does that against Stytch's JWKS. From the console the sessions come from
/// management's development-only mint; in tests TestKeys mints them against the fake JWKS.
/// </summary>
public sealed record SocketCredentials(IReadOnlyList<MintedSession> Sessions)
{
    public const string ManagementUrlVariable = "TABLETOP_MANAGEMENT_URL";
    public const string MintPath = "/v1/dev/sessions";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record MintAccount(Guid Id, string Name);
    private sealed record MintResponse(string SessionJwt, MintAccount Account);

    /// <summary>Stable across runs and distinct within a scenario, so a re-run mints for the same accounts.</summary>
    public static string NameFor(CompiledScenario scenario, int index) => $"sim-{scenario.Scenario.Name}-{index}";

    public static async Task<SocketCredentials> MintAsync(CompiledScenario scenario, HttpClient http, CancellationToken ct)
    {
        var sessions = new List<MintedSession>();
        for (var i = 0; i < scenario.Info.Participants.Count; i++)
        {
            using var response = await http.PostAsJsonAsync(MintPath, new { name = NameFor(scenario, i) }, Json, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException(
                    $"{http.BaseAddress}{MintPath.TrimStart('/')} answered 404; the management API must run as a development environment (./scripts/management sets ASPNETCORE_ENVIRONMENT=Development)");
            response.EnsureSuccessStatusCode();
            var minted = await response.Content.ReadFromJsonAsync<MintResponse>(Json, ct)
                ?? throw new InvalidOperationException("the mint returned an empty body");
            sessions.Add(new MintedSession(minted.SessionJwt, minted.Account.Id));
        }
        return new SocketCredentials(sessions);
    }

    public string For(int index) => At(index).Jwt;
    public Guid AccountOf(int index) => At(index).AccountId;

    private MintedSession At(int index) => index < Sessions.Count
        ? Sessions[index]
        : throw new InvalidOperationException($"{Sessions.Count} sessions were minted; participant {index} needs one of its own");
}
```

`MintAsync` mints serially rather than in parallel. Each call is one HTTP round trip plus three or four
Stytch calls, three participants make it four calls, and the mint inserts an `account` row, so a
sequential loop is both fast enough and free of a write race on one account id.

- [ ] **Step 3: Write `SocketRowSeeder.cs`**

```csharp
using Npgsql;
using Tabletop.Content;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

public sealed class PropertiesMissingException(string hash)
    : Exception($"no properties row for {hash}; start the game server once so it seeds its embedded documents");

/// <summary>
/// The rows a scenario needs before a socket can name its game. account, game, team and participant are
/// management's writes and are raw SQL here; the table_state tree and the army snapshots go through
/// IGameRows, which owns them (game server skeleton plan → Interfaces needed 1).
/// </summary>
public sealed class SocketRowSeeder(NpgsqlDataSource db, IGameRows rows)
{
    public async Task SeedAsync(CompiledScenario scenario, SocketCredentials credentials, CancellationToken ct)
    {
        if (!await rows.PropertiesExistAsync(scenario.Properties.ContentHash, ct))
            throw new PropertiesMissingException(scenario.Properties.ContentHash);

        var accounts = scenario.Info.Participants.Select((_, i) => credentials.AccountOf(i)).ToList();
        var info = scenario.Info;

        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        for (var i = 0; i < accounts.Count; i++)
            await Execute(connection, transaction, ct,
                "insert into management.account (id, name) values ($1, $2) on conflict (id) do nothing",
                accounts[i], info.Participants[i].Name);

        // rng_seed is how a socket run sets the seed: the driver writes a row rather than constructing a
        // GameInfo, so the scenario's seed goes in the insert and the game server reads it back at
        // hydration with the rest of the game's row (owner ruling, 2026-09-06).
        await Execute(connection, transaction, ct,
            """
            insert into management.game (id, name, status, game_rules, properties_hash, scheduler_shape, enforcement, waits_between_steps, created_by, rng_seed)
            values ($1, $2, 'lobby', $3, $4, $5, $6, $7, $8, $9)
            """,
            info.Id, info.Name, info.GameRules, info.PropertiesHash,
            Wire(info.SchedulerShape), Wire(info.Enforcement), info.WaitsBetweenSteps, accounts[0], info.RngSeed);

        foreach (var team in info.Teams)
            await Execute(connection, transaction, ct,
                "insert into management.team (id, game_id, ordinal) values ($1, $2, $3)", team.Id, info.Id, team.Ordinal);

        for (var i = 0; i < info.Participants.Count; i++)
            await Execute(connection, transaction, ct,
                "insert into management.participant (id, game_id, team_id, account_id, army_id, army_snapshot) values ($1, $2, $3, $4, null, null)",
                info.Participants[i].Id, info.Id, info.Participants[i].TeamId, accounts[i]);

        await transaction.CommitAsync(ct);

        // The setup result, written by the code that owns the tree; end-setup flips the game to playing.
        await rows.StartSetupAsync(info.Id, scenario.Snapshot, ct);
        await rows.EndSetupAsync(info.Id, scenario.Snapshot,
            scenario.Board.Armies.ToDictionary(a => a.ParticipantId, a => (ArmySnapshot?)a.Snapshot), ct);
    }

    public async Task DeleteAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await Execute(connection, null, ct, "delete from game.game_event where game_id = $1", gameId);
        await Execute(connection, null, ct, "delete from management.game where id = $1", gameId);   // the tree cascades
    }

    private static string Wire<T>(T value) where T : struct, Enum => WireJson.Serialize(value).Trim('"');

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct,
        string sql, params object?[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
```

`management.participant` carries `game_id` and `army_id` per the skeleton plan's *Interfaces needed* item 1 (`participant (id, game_id, team_id, account_id, army_id, army_snapshot)`); `docs/schema.md`'s diagram omits both, which is a staleness in the diagram rather than in the schema — `scripts/postgres/init/001_schema.sql` is the authority. If the SQL disagrees, follow the SQL and record it as a spec correction to `schema.md`.
- [ ] **Step 4: Write `SocketDriver.cs`**

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Npgsql;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

/// <summary>
/// A real WebSocket to a running game server, a real session JWT, real rows and a real event log in Postgres
/// — the whole path architecture.md calls the only path (spec → Two drivers behind one interface).
/// </summary>
public sealed class SocketDriver : IGameDriver
{
    private sealed class Peer(Guid participantId, WebSocket socket)
    {
        public Guid ParticipantId { get; } = participantId;
        public WebSocket Socket { get; } = socket;
        public Channel<SocketMessage> Inbox { get; } = Channel.CreateUnbounded<SocketMessage>();
        public Task? Reader { get; set; }
    }

    private readonly CompiledScenario _scenario;
    private readonly IWebSocketConnector _connector;
    private readonly SocketCredentials _credentials;
    private readonly EventApplier _applier;
    private readonly List<Peer> _players = [];
    private readonly List<GameEvent> _pending = [];
    private readonly CancellationTokenSource _stopping = new();
    // True when the last command appended, so a rest frame is on its way to the recorder too.
    private bool _restDueOnRecorder;
    private Peer _recorder = null!;

    private SocketDriver(CompiledScenario scenario, IWebSocketConnector connector, SocketCredentials credentials)
    {
        _scenario = scenario;
        _connector = connector;
        _credentials = credentials;
        _applier = new EventApplier(scenario.Board, scenario.Info);
    }

    /// <summary>How long one wait for AtRest may take before the server counts as hung. Not a quiescence guess.</summary>
    public TimeSpan RestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public static async Task<SocketDriver> StartAsync(CompiledScenario scenario, IWebSocketConnector connector,
        SocketCredentials credentials, NpgsqlDataSource db, CancellationToken ct)
    {
        var driver = new SocketDriver(scenario, connector, credentials);
        await driver.StartAsync(ct);
        return driver;
    }

    public string Name => "socket";
    public Guid GameId => _scenario.Info.Id;
    public GameInfo Info => _scenario.Info;
    public Board Board => _scenario.Board;
    public IReadOnlyList<Guid> TeamIds => _scenario.TeamIds;
    public Guid ParticipantFor(Guid teamId) => _scenario.ParticipantFor(teamId);
    public DriverMode Mode { get; set; } = DriverMode.Run;

    public async Task StartAsync(CancellationToken ct)
    {
        // The recorder connects first, so its stream carries the whole log from the start.
        var recorderIndex = _scenario.Info.Participants.Count - 1;
        _recorder = await OpenAsync(_scenario.RecorderParticipantId, recorderIndex, ct);
        for (var i = 0; i < _scenario.ParticipantIds.Count; i++)
            _players.Add(await OpenAsync(_scenario.ParticipantIds[i], i, ct));
        // Connect appends nothing, so its AtRest goes to the connecting client alone: waiting for the
        // recorder's own says the whole log has arrived, where draining whatever had arrived raced it.
        await RestAsync(ct);
        await DrainEventsAsync(ct);
    }

    private async Task<Peer> OpenAsync(Guid participantId, int credentialIndex, CancellationToken ct)
    {
        var socket = await _connector.ConnectAsync(ct);
        var peer = new Peer(participantId, socket);
        peer.Reader = Task.Run(() => ReadLoopAsync(peer), _stopping.Token);
        await SendAsync(peer, new Connect(_credentials.For(credentialIndex), GameId, ProtocolVersion.Current));
        var welcome = await ExpectAsync(peer, ct);
        if (welcome is Rejected rejected) throw new InvalidOperationException($"the server rejected the connection: {rejected.Reason}");
        if (welcome is not Welcome w) throw new InvalidOperationException($"expected a welcome, got {welcome.GetType().Name}");
        if (w.ParticipantId != participantId)
            throw new InvalidOperationException($"the JWT at index {credentialIndex} named participant {w.ParticipantId:N}, not {participantId:N}");
        return peer;
    }

    public Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OpenAsk>>(_applier.Projection.OpenAsks.Values.ToList());

    public async Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)
    {
        var peer = _players.FirstOrDefault(p => p.ParticipantId == participantId)
            ?? throw new ArgumentException($"no socket for participant {participantId}", nameof(participantId));
        var commandId = Guid.CreateVersion7();
        await SendAsync(peer, new CommandMessage(commandId, command));
        // Refusals come back on the submitting socket, before that command's AtRest, so the rest frame
        // ends the wait and no timing window is involved. Its Seq is the log's high-water mark: above
        // what the recorder has applied means the command appended, and the game server broadcasts the
        // batch and its rest frame to every socket. Equal means it appended nothing, and the rest frame
        // went to this socket alone, so the recorder has nothing coming and StepAsync must not wait.
        var (refusal, restSeq) = await WaitForRefusalAsync(peer, commandId, ct);
        _restDueOnRecorder = restSeq > _applier.Projection.LastSeq;
        return refusal is null ? new SubmitResult.Accepted(null) : new SubmitResult.Rejected(refusal.Reason, refusal.Detail);
    }

    public Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct)
    {
        // Anything RestAsync already read is in _pending; anything queued since is read here. Every
        // kind but EventsMessage is ignored rather than rejected, which is how a driver tolerates a
        // frame it has no use for.
        while (_recorder.Inbox.Reader.TryRead(out var frame))
            if (frame is EventsMessage message) _pending.AddRange(message.Events);
        foreach (var player in _players) while (player.Inbox.Reader.TryRead(out _)) { }
        var ordered = _pending.OrderBy(e => e.Seq).ToList();
        _pending.Clear();
        foreach (var e in ordered) if (e.Seq > _applier.Projection.LastSeq) _applier.Apply(e);
        return Task.FromResult<IReadOnlyList<GameEvent>>(ordered);
    }

    public Task<Projection> ProjectionAsync(CancellationToken ct) => Task.FromResult(_applier.Projection);

    public async Task<StepResult> StepAsync(CancellationToken ct)
    {
        // No batch is on its way when the last command appended nothing, and none is on its way before
        // the first command of the run: the server is already at rest and the step is an empty one.
        AtRest? rest = null;
        if (_restDueOnRecorder) { rest = await RestAsync(ct); _restDueOnRecorder = false; }
        var events = await DrainEventsAsync(ct);
        // One connection delivers in order, so every EventsMessage of the batch preceded its AtRest.
        // A mismatch is a lost, reordered or duplicated frame, and it is a fault rather than a step.
        if (rest is not null && _applier.Projection.LastSeq != rest.Seq)
            throw new InvalidOperationException(
                $"the server rested at seq {rest.Seq} but the recorder's stream reached {_applier.Projection.LastSeq}");
        return new StepResult(events, _applier.Projection.OpenAsks.Values.ToList(), events.Count == 0);
    }

    /// <summary>Reconnect: the same participant, a second socket. The first is closed by the server with 1000.</summary>
    public async Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken ct)
    {
        var old = _recorder;
        _pending.Clear();
        _restDueOnRecorder = false;
        _recorder = await OpenAsync(old.ParticipantId, _scenario.Info.Participants.Count - 1, ct);
        await RestAsync(ct);
        await DrainEventsAsync(ct);
        return old.Socket.CloseStatus;
    }

    /// <summary>
    /// Read the recorder's socket until the server says it is at rest, keeping the events read on the
    /// way for DrainEventsAsync. RestTimeout guards a hung server; it is not a quiescence guess.
    /// </summary>
    private async Task<AtRest> RestAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RestTimeout);
        try
        {
            while (true)
            {
                var frame = await _recorder.Inbox.Reader.ReadAsync(deadline.Token);
                if (frame is EventsMessage message) _pending.AddRange(message.Events);
                else if (frame is AtRest rest) return rest;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq);
        }
        catch (ChannelClosedException)
        {
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq);
        }
    }

    /// <summary>
    /// A command's Refused precedes that command's AtRest on the submitting socket, whatever the
    /// outcome, so the rest frame ends the wait. Returns the refusal if there was one and the rest
    /// frame's Seq either way.
    /// </summary>
    private async Task<(Refused? Refusal, long RestSeq)> WaitForRefusalAsync(Peer peer, Guid commandId, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RestTimeout);
        Refused? refusal = null;
        try
        {
            while (true)
            {
                var frame = await peer.Inbox.Reader.ReadAsync(deadline.Token);
                if (frame is Refused r && r.MessageId == commandId) refusal = r;
                if (frame is AtRest rest) return (refusal, rest.Seq);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq);
        }
        catch (ChannelClosedException)
        {
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq);
        }
    }

    private async Task<SocketMessage> ExpectAsync(Peer peer, CancellationToken ct)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(TimeSpan.FromSeconds(10));
        return await peer.Inbox.Reader.ReadAsync(window.Token);
    }

    private static async Task SendAsync(Peer peer, SocketMessage message) =>
        await peer.Socket.SendAsync(Encoding.UTF8.GetBytes(WireJson.Serialize(message)), WebSocketMessageType.Text, true, CancellationToken.None);

    private async Task ReadLoopAsync(Peer peer)
    {
        var buffer = new byte[4 * 1024 * 1024];
        try
        {
            while (peer.Socket.State == WebSocketState.Open)
            {
                var total = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await peer.Socket.ReceiveAsync(new Memory<byte>(buffer, total, buffer.Length - total), _stopping.Token);
                    if (result.MessageType == WebSocketMessageType.Close) { peer.Inbox.Writer.TryComplete(); return; }
                    total += result.Count;
                } while (!result.EndOfMessage);
                peer.Inbox.Writer.TryWrite(WireJson.Deserialize(Encoding.UTF8.GetString(buffer, 0, total)));
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally { peer.Inbox.Writer.TryComplete(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        foreach (var peer in _players.Append(_recorder))
        {
            try { await peer.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "sim finished", CancellationToken.None); }
            catch (WebSocketException) { }
            peer.Socket.Dispose();
        }
        _stopping.Dispose();
    }
}
```

`RestAsync` is where the asymmetry used to live. It reads the recorder's socket until an `AtRest` arrives, keeping the events it passes on the way, so a step costs what the server takes to answer it and nothing more. Three earlier hazards go with the window it replaces: a slow server no longer reads as a rested one, `StartAsync` and `ReconnectRecorderAsync` no longer race the connect-time log delivery, and `WaitForRefusalAsync` no longer reads a slow refusal as an acceptance. The in-process driver is still the default, because it needs no Postgres, no server process and no Docker, and the socket one still runs a smaller set of scenarios in CI.

**What Task 8 breaks without**, all of it already decided by the game server skeleton plan and consumed rather than requested here: an `AtRest` at the end of `ConnectAsync` at every status, an `AtRest` at the end of `HandleCommandAsync` whatever the outcome, a `Refused` before that command's `AtRest` on the sender's socket, and `Seq` as the log's high-water mark. A server that appends events and never sends the frame leaves the step waiting out `RestTimeout` and the run `Failed`, which is loud rather than silent and is why the wait is bounded at all.

- [ ] **Step 5: Run the tests; expect all green, then commit**

```bash
docker compose up -d
dotnet test tests/Tabletop.Sim.Tests --filter DriverAgreementTests
git add sim/Drivers tests/Tabletop.Sim.Tests
git commit -m "Play the same scenario over a real socket and a real Postgres log"
```

---
### Task 9: The step-and-wait seam

**Files:**
- Modify: `sim/Run/RunLoop.cs` (`ResumeAsync`, and the pause is already `RunOutcome.Paused`)
- Test: `tests/Tabletop.Sim.Tests/StepAndWaitTests.cs`

**Interfaces:**
- Consumes: `IGameDriver`, `DriverMode` (Task 4); `RunLoop`, `RunOutcome`, `RunReport` (Task 6); `Ask`, `AskAnswer`, `Projection` (Protocol).
- Produces: on `RunLoop` — `Task<RunReport> ResumeAsync(CancellationToken ct)` (continue from a pause under the current `Mode`) and `Task<RunReport> AnswerPausedAsync(Guid teamId, AskAnswer answer, CancellationToken ct)` (answer the Ask the run paused on, from outside the answer source, and pause again).

Spec: spec → *Interactive debugging is a door, not a room* — "Playback is the whole of the first version. The viewer sends no command and answers no Ask. The design leaves the seam for it: … the driver interface has a step-and-wait mode … None of that is built yet." Module prompt item 8: "Build the seam and the mode. Do not build a control channel or anything in a browser."

**What the seam is and is not.** `DriverMode.StepAndWait` makes the loop stop at the first Ask it would have answered and report `RunOutcome.Paused`, having answered nothing. `ResumeAsync` continues; `AnswerPausedAsync` supplies one answer from outside. That is the whole seam: a caller with a socket, an HTTP endpoint or a REPL can drive a paused game one decision at a time. There is no control channel, no server, no browser and no viewer code here.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class StepAndWaitTests
{
    static async Task<(CompiledScenario, InProcessDriver, RunLoop)> Paused(ulong seed = 31)
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(seed);
        var compiled = ScenarioCompiler.Compile(scenario);
        var driver = await InProcessDriver.StartAsync(compiled, TestContext.Current.CancellationToken);
        driver.Mode = DriverMode.StepAndWait;
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(AnswerScript.Empty, new AnswerPolicy(new Chooser(seed))), new Chooser(seed));
        return (compiled, driver, loop);
    }

    [Fact]
    public async Task Step_and_wait_stops_with_the_ask_open_and_answers_nothing()
    {
        var (_, driver, loop) = await Paused();
        await using var _ = driver;
        var report = await loop.RunAsync(TestContext.Current.CancellationToken);
        var paused = Assert.IsType<RunOutcome.Paused>(report.Outcome);
        Assert.Equal(AskQuestions.Deploy, paused.Question);
        Assert.Equal(0, report.AsksAnswered);
        var open = Assert.Single(await driver.OpenAsksAsync(TestContext.Current.CancellationToken));
        Assert.Equal(paused.AskId, open.Ask.Id);
        Assert.Empty(open.Answers);
        Assert.True(report.Passed);                      // a pause is a stop, not a failure
    }

    [Fact]
    public async Task Resuming_in_run_mode_finishes_the_game()
    {
        var (_, driver, loop) = await Paused();
        await using var _ = driver;
        await loop.RunAsync(TestContext.Current.CancellationToken);
        driver.Mode = DriverMode.Run;
        var report = await loop.ResumeAsync(TestContext.Current.CancellationToken);
        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
        Assert.True(report.AsksAnswered > 10);
    }

    [Fact]
    public async Task Answering_the_paused_ask_from_outside_advances_one_decision_and_pauses_again()
    {
        var (compiled, driver, loop) = await Paused();
        await using var _ = driver;
        var first = Assert.IsType<RunOutcome.Paused>((await loop.RunAsync(TestContext.Current.CancellationToken)).Outcome);
        var ask = (await driver.OpenAsksAsync(TestContext.Current.CancellationToken)).Single().Ask;
        var unit = Assert.IsType<PieceSelection>(ask.Legal).Eligible[0];

        var report = await loop.AnswerPausedAsync(ask.Teams.Single(), new SelectedPieces([unit]), TestContext.Current.CancellationToken);
        var second = Assert.IsType<RunOutcome.Paused>(report.Outcome);
        Assert.NotEqual(first.AskId, second.AskId);

        var projection = await driver.ProjectionAsync(TestContext.Current.CancellationToken);
        Assert.All(projection.Units[unit].Definition.Models, m => Assert.IsType<OnTable>(projection.Models[m.Id].Location));
    }

    [Fact]
    public async Task Stepping_repeatedly_walks_the_whole_game_one_ask_at_a_time()
    {
        var (_, driver, loop) = await Paused(32);
        await using var _ = driver;
        var report = await loop.RunAsync(TestContext.Current.CancellationToken);
        var steps = 0;
        while (report.Outcome is RunOutcome.Paused && steps++ < 2000)
        {
            var ask = (await driver.OpenAsksAsync(TestContext.Current.CancellationToken)).First(a => a.Ask.Teams.Any(t => !a.Answers.ContainsKey(t)));
            var team = ask.Ask.Teams.First(t => !ask.Answers.ContainsKey(t));
            report = await loop.AnswerPausedAsync(team, new AnswerPolicy(new Chooser((ulong)steps)).Answer(ask.Ask,
                new PolicyView(await driver.ProjectionAsync(TestContext.Current.CancellationToken), driver.Board,
                    volumes: null, driver.Info)), TestContext.Current.CancellationToken);
        }
        Assert.Contains(report.Outcome.Kind, (string[])["decided", "round_limit"]);
    }
}
```

- [ ] **Step 2: Run; expect failure, then add the two methods to `RunLoop`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter StepAndWaitTests`
Expected: `CS1061: 'RunLoop' does not contain a definition for 'ResumeAsync'`.

`RunAsync`'s body already ends in a report; extract the reporting so both entry points share it.

```csharp
    /// <summary>Continue after a pause, under whatever Mode the driver is in now.</summary>
    public Task<RunReport> ResumeAsync(CancellationToken ct) => RunAsync(ct);

    /// <summary>
    /// Answer the Ask the run paused on, from outside the answer source, then continue under the current
    /// Mode — so in StepAndWait this advances exactly one decision. This is the seam the spec leaves for
    /// answering an Ask from a debugger; nothing here is a control channel.
    /// </summary>
    public async Task<RunReport> AnswerPausedAsync(Guid teamId, AskAnswer answer, CancellationToken ct)
    {
        var open = (await driver.OpenAsksAsync(ct)).FirstOrDefault(a => a.Ask.Teams.Contains(teamId) && !a.Answers.ContainsKey(teamId))
            ?? throw new InvalidOperationException($"no ask is open for team {teamId:N}");
        var result = await driver.AnswerAsync(teamId, open.Ask.Id, answer, ct);
        if (result is SubmitResult.Rejected rejected)
            throw new InvalidOperationException($"'{open.Ask.Question}' refused: {rejected.Reason} {rejected.Detail}");
        Absorb(await driver.DrainEventsAsync(ct));

        if (DeployQuestions.Contains(open.Ask.Question) && answer is SelectedPieces selected)
            foreach (var unitId in selected.PieceIds) await PlaceAsync(teamId, unitId, ct);

        return await RunAsync(ct);
    }
```

`RunAsync` is re-entrant because every piece of its state — the log, the projector, the fingerprint — accumulates rather than resets, so calling it again continues the same run. Reset `_lastAnswered` to `null` at the top of `DriveAsync` so a pause between an answer and its reopening is not mistaken for a hang.

- [ ] **Step 3: Run the tests; expect all green, then commit**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter StepAndWaitTests
git add sim/Run tests/Tabletop.Sim.Tests
git commit -m "Pause with an Ask open, and answer it from outside the answer source"
```

---
### Task 10: The console entry point

**Files:**
- Create: `sim/Program.cs`, `sim/SimOptions.cs`, `sim/Summary.cs`
- Test: `tests/Tabletop.Sim.Tests/ProgramTests.cs`

**Interfaces:**
- Consumes: everything above; `NpgsqlDataSource` (Npgsql).
- Produces: `sealed record SimOptions(string? Scenario, ulong FromSeed, ulong ToSeed, string Driver, string OutputDirectory, int? RoundCap, bool Recording, bool List)` with `static SimOptions Parse(string[] args)`, `static string Usage`, `IEnumerable<ulong> Seeds`, and `sealed class UsageException(string) : Exception`; `sealed record Summary(string Driver, int Runs, int Passed, int Failed, double TotalMs, IReadOnlyList<RunReport> Reports)` with `string Table()`; `static class Program` with `Task<int> Main(string[])` and `Task<int> RunAsync(SimOptions, TextWriter, CancellationToken)`.

Spec: module prompt item 7 — "Run a scenario by name, or N games from a seed range, writing recordings and a summary. Decide the argument surface and keep it small."

**The argument surface, and nothing more.**

```
dotnet run --project sim -- <scenario> [options]

  --seed N              one seed (default: the scenario's own)
  --seeds A..B          a closed range of seeds, one game each
  --driver in-process   the default; or socket
  --out DIR             where recordings and summary.json go (default: ./recordings)
  --rounds N            override the scenario's round cap
  --no-recording        run and report, write no files
  --list                list the scenario names and exit
```

Environment for `--driver socket`: `TABLETOP_DB_CONNECTION` (the same variable the servers read), `TABLETOP_SIM_WS` (default `ws://localhost:5200/ws`), `TABLETOP_MANAGEMENT_URL` (default `http://localhost:5100`, where the session mint lives). There is no token variable: the tokens are minted per run. Exit code `0` when every run passed, `1` otherwise, `2` on a usage error.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Sim;

namespace Tabletop.Sim.Tests;

public class ProgramTests
{
    [Fact]
    public void The_argument_surface_parses_a_name_a_seed_and_a_range()
    {
        var one = SimOptions.Parse(["skirmish", "--seed", "9"]);
        Assert.Equal("skirmish", one.Scenario);
        Assert.Equal([9ul], one.Seeds);
        Assert.Equal("in-process", one.Driver);

        var many = SimOptions.Parse(["skirmish", "--seeds", "3..6", "--driver", "socket", "--out", "/tmp/x", "--rounds", "2", "--no-recording"]);
        Assert.Equal([3ul, 4ul, 5ul, 6ul], many.Seeds);
        Assert.Equal("socket", many.Driver);
        Assert.Equal("/tmp/x", many.OutputDirectory);
        Assert.Equal(2, many.RoundCap);
        Assert.False(many.Recording);

        Assert.True(SimOptions.Parse(["--list"]).List);
        Assert.Throws<UsageException>(() => SimOptions.Parse([]));
        Assert.Throws<UsageException>(() => SimOptions.Parse(["skirmish", "--driver", "carrier-pigeon"]));
        Assert.Throws<UsageException>(() => SimOptions.Parse(["skirmish", "--seeds", "6..3"]));
        Assert.Throws<UsageException>(() => SimOptions.Parse(["skirmish", "--nonsense"]));
    }

    [Fact]
    public async Task Listing_the_scenarios_exits_zero_and_names_them()
    {
        var output = new StringWriter();
        Assert.Equal(0, await Program.RunAsync(SimOptions.Parse(["--list"]), output, TestContext.Current.CancellationToken));
        Assert.Contains("skirmish", output.ToString());
        Assert.Contains("stalemate", output.ToString());
    }

    [Fact]
    public async Task Running_a_seed_range_writes_a_recording_each_and_one_summary()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sim-{Guid.CreateVersion7():N}");
        var output = new StringWriter();
        try
        {
            var code = await Program.RunAsync(
                SimOptions.Parse(["stalemate", "--seeds", "1..3", "--out", directory]), output, TestContext.Current.CancellationToken);
            Assert.Equal(0, code);
            Assert.Equal(3, Directory.GetFiles(directory, "stalemate-*.json").Length);
            Assert.True(File.Exists(Path.Combine(directory, "summary.json")));
            Assert.Contains("3 runs", output.ToString());
            Assert.Contains("stalemate", output.ToString());

            var recording = await Recording.RecordingJson.ReadAsync(Path.Combine(directory, "stalemate-1.json"), TestContext.Current.CancellationToken);
            Assert.Equal(1ul, recording.Meta.Seed);
            Assert.Equal("in-process", recording.Meta.Driver);
            Assert.NotEmpty(recording.Frames);

            // RunLoop always leaves RunReport.RecordingPath null; this is the only code that fills it in,
            // and the path reaches summary.json by no other route.
            Assert.Contains("stalemate-1.json",
                await File.ReadAllTextAsync(Path.Combine(directory, "summary.json"), TestContext.Current.CancellationToken));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task An_unknown_scenario_is_a_usage_error()
    {
        var output = new StringWriter();
        Assert.Equal(2, await Program.RunAsync(SimOptions.Parse(["no-such-scenario"]), output, TestContext.Current.CancellationToken));
        Assert.Contains("no scenario", output.ToString());
    }

    [Fact]
    public async Task No_recording_writes_nothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sim-{Guid.CreateVersion7():N}");
        var output = new StringWriter();
        Assert.Equal(0, await Program.RunAsync(
            SimOptions.Parse(["stalemate", "--seed", "1", "--out", directory, "--no-recording"]), output, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(directory));
    }
}
```

- [ ] **Step 2: Run; expect failure, then write `SimOptions.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter ProgramTests`
Expected: `CS0246: ... 'SimOptions' could not be found`.

```csharp
namespace Tabletop.Sim;

public sealed class UsageException(string message) : Exception(message);

public sealed record SimOptions(
    string? Scenario, ulong FromSeed, ulong ToSeed, string Driver,
    string OutputDirectory, int? RoundCap, bool Recording, bool List)
{
    public const string InProcess = "in-process";
    public const string Socket = "socket";

    public static string Usage => """
        dotnet run --project sim -- <scenario> [options]

          --seed N              one seed (default: the scenario's own)
          --seeds A..B          a closed range of seeds, one game each
          --driver in-process   the default; or socket
          --out DIR             where recordings and summary.json go (default: ./recordings)
          --rounds N            override the scenario's round cap
          --no-recording        run and report, write no files
          --list                list the scenario names and exit

        For --driver socket: TABLETOP_DB_CONNECTION, TABLETOP_SIM_WS (default ws://localhost:5200/ws)
        and TABLETOP_MANAGEMENT_URL (default http://localhost:5100). Session tokens are minted from
        POST /v1/dev/sessions, which needs the management API run as a development environment.
        """;

    public IEnumerable<ulong> Seeds
    {
        get { for (var seed = FromSeed; seed <= ToSeed; seed++) yield return seed; }
    }

    public static SimOptions Parse(string[] args)
    {
        string? scenario = null, driver = InProcess, output = "recordings";
        ulong? from = null, to = null;
        int? rounds = null;
        var recording = true;
        var list = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--list": list = true; break;
                case "--no-recording": recording = false; break;
                case "--seed": from = to = ULong(Next(args, ref i, "--seed")); break;
                case "--seeds":
                    var parts = Next(args, ref i, "--seeds").Split("..", StringSplitOptions.TrimEntries);
                    if (parts.Length != 2) throw new UsageException("--seeds wants A..B");
                    from = ULong(parts[0]); to = ULong(parts[1]);
                    if (from > to) throw new UsageException($"--seeds {from}..{to} runs backwards");
                    break;
                case "--driver":
                    driver = Next(args, ref i, "--driver");
                    if (driver is not (InProcess or Socket)) throw new UsageException($"--driver is {InProcess} or {Socket}, not '{driver}'");
                    break;
                case "--out": output = Next(args, ref i, "--out"); break;
                case "--rounds": rounds = int.Parse(Next(args, ref i, "--rounds")); break;
                default:
                    if (args[i].StartsWith('-')) throw new UsageException($"unknown option '{args[i]}'\n\n{Usage}");
                    if (scenario is not null) throw new UsageException($"one scenario at a time; got '{scenario}' and '{args[i]}'");
                    scenario = args[i];
                    break;
            }
        }

        if (!list && scenario is null) throw new UsageException(Usage);
        var seed = from ?? (scenario is not null && Scenarios.ScenarioCatalog.TryGet(scenario, out var s) ? s.Seed : 1);
        return new SimOptions(scenario, seed, to ?? seed, driver, output, rounds, recording, list);
    }

    private static string Next(string[] args, ref int i, string option) =>
        ++i < args.Length ? args[i] : throw new UsageException($"{option} wants a value");

    private static ulong ULong(string text) =>
        ulong.TryParse(text, out var value) ? value : throw new UsageException($"'{text}' is not a seed");
}
```
- [ ] **Step 3: Write `Summary.cs`**

```csharp
using Tabletop.Sim.Run;

namespace Tabletop.Sim;

public sealed record Summary(string Driver, int Runs, int Passed, int Failed, double TotalMs, IReadOnlyList<RunReport> Reports)
{
    public static Summary Of(string driver, IReadOnlyList<RunReport> reports) => new(
        driver, reports.Count, reports.Count(r => r.Passed), reports.Count(r => !r.Passed),
        reports.Sum(r => r.DurationMs), reports);

    public string Table()
    {
        var lines = new List<string>
        {
            $"{Runs} runs on {Driver}: {Passed} passed, {Failed} failed, {TotalMs:N0} ms",
            "  seed  outcome      rounds  seq   frames  asks   ms",
        };
        lines.AddRange(Reports.Select(r =>
            $"  {r.Seed,-5} {r.Outcome.Kind,-12} {r.Rounds,-7} {r.LastSeq,-5} {r.Frames,-7} {r.AsksAnswered,-6} {r.DurationMs,-6:N0}"));
        lines.AddRange(Reports.Where(r => !r.Passed).SelectMany(r =>
            r.Failures.Select(f => $"  seed {r.Seed}: {f}")));
        return string.Join("\n", lines);
    }
}
```

- [ ] **Step 4: Write `Program.cs`**

```csharp
using Npgsql;
using Tabletop.GameServer.Persistence;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try { return await RunAsync(SimOptions.Parse(args), Console.Out, CancellationToken.None); }
        catch (UsageException usage) { Console.Error.WriteLine(usage.Message); return 2; }
    }

    public static async Task<int> RunAsync(SimOptions options, TextWriter output, CancellationToken ct)
    {
        if (options.List)
        {
            foreach (var name in ScenarioCatalog.Names) output.WriteLine(name);
            return 0;
        }

        if (!ScenarioCatalog.TryGet(options.Scenario!, out var template))
        {
            output.WriteLine($"no scenario '{options.Scenario}'; known: {string.Join(", ", ScenarioCatalog.Names)}");
            return 2;
        }

        var reports = new List<RunReport>();
        foreach (var seed in options.Seeds)
        {
            var scenario = template.WithSeed(seed);
            if (options.RoundCap is { } cap) scenario = scenario with { RoundCap = cap };
            reports.Add(await PlayAsync(scenario, options, ct));
        }

        var summary = Summary.Of(options.Driver, reports);
        output.WriteLine(summary.Table());
        if (options.Recording)
        {
            Directory.CreateDirectory(options.OutputDirectory);
            await File.WriteAllTextAsync(Path.Combine(options.OutputDirectory, "summary.json"),
                System.Text.Json.JsonSerializer.Serialize(summary, RecordingJson.Options), ct);
        }
        return summary.Failed == 0 ? 0 : 1;
    }

    private static async Task<RunReport> PlayAsync(Scenario scenario, SimOptions options, CancellationToken ct)
    {
        var compiled = ScenarioCompiler.Compile(scenario);
        var answers = new ComposedAnswers(scenario.Script, scenario.ScriptOnly ? null : new AnswerPolicy(new Chooser(scenario.Seed)));

        await using var driver = options.Driver == SimOptions.Socket
            ? await OpenSocketAsync(compiled, ct)
            : await InProcessDriver.StartAsync(compiled, ct);

        var loop = new RunLoop(driver, compiled, answers, new Chooser(scenario.Seed ^ 0x5EED));
        var report = await loop.RunAsync(ct);

        if (!options.Recording) return report;
        var path = Path.Combine(options.OutputDirectory, $"{scenario.Name}-{scenario.Seed}.json");
        await RecordingJson.WriteAsync(loop.BuildRecording(report), path, ct);
        return report with { RecordingPath = path };
    }

    private static async Task<IGameDriver> OpenSocketAsync(CompiledScenario compiled, CancellationToken ct)
    {
        var connection = Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION")
            ?? throw new UsageException("TABLETOP_DB_CONNECTION is not set; --driver socket needs the same database the servers use");
        var address = Environment.GetEnvironmentVariable("TABLETOP_SIM_WS") ?? "ws://localhost:5200/ws";
        var management = Environment.GetEnvironmentVariable(SocketCredentials.ManagementUrlVariable) ?? "http://localhost:5100";
        using var http = new HttpClient { BaseAddress = new Uri(management) };
        var credentials = await SocketCredentials.MintAsync(compiled, http, ct);

        var db = NpgsqlDataSource.Create(connection);
        await new SocketRowSeeder(db, new PostgresGameRows(db)).SeedAsync(compiled, credentials, ct);
        return await SocketDriver.StartAsync(compiled, new ClientWebSocketConnector(new Uri(address)), credentials, db, ct);
    }
}
```

A socket run leaves its rows and its log in Postgres on purpose: the game is then in the database to look at, and `sim/` is a developer tool. `SocketRowSeeder.DeleteAsync` exists for the tests, which must not accumulate games. Repeating a `--driver socket` run with the same seed now makes a *second* game rather than colliding on the primary key, because the game id is random and only `rng_seed` repeats — so a seed range leaves one row per run and `--driver socket` accumulates games until someone deletes them. That is the price of the seed being stored rather than derived, and it is the right way round: the two runs are separate games that rolled the same dice.

- [ ] **Step 5: Run the tests and the console for real**

```bash
dotnet test tests/Tabletop.Sim.Tests --filter ProgramTests
dotnet run --project sim -- --list
dotnet run --project sim -- skirmish --seeds 1..20 --out /tmp/sim
```

Expected: twenty runs, every one `decided` or `round_limit`, no failures, twenty recordings and a `summary.json` in `/tmp/sim`, and the whole thing in a few seconds. A `stuck` run here is the simulator doing its job: read the dumped frames, and the bug is in the scheduler or the interpreter.

- [ ] **Step 6: Commit**

```bash
git add sim tests/Tabletop.Sim.Tests
git commit -m "Run a scenario or a seed range from the console, writing recordings and a summary"
```

---

### Task 11: `CLAUDE.md`

**Files:**
- Modify: `CLAUDE.md` (Layout, Commands)
- Verify only: `docs/architecture.md`. The deliverables ruling landed there on 2026-09-08, ahead of
  this plan, so Step 3 checks it rather than editing it.

**Interfaces:**
- Consumes: everything Tasks 1–10 produced, as the facts this document now records.
- Produces: no code. The whole-tree check in Step 4 is this plan's exit gate.

Spec: spec → *`sim/` is a fourth deliverable* — "The layout rule in `CLAUDE.md` had three shipping directories, `client/`, `server/` and `web/`… `sim/` and `viewer/` are a fourth and fifth. Ruled by the project owner on 2026-09-06"; "They ship to developers, not to players. Neither is in the client's export, neither is referenced by a server, and a release that omits both is complete."

`viewer/` is another plan's, so it keeps the stub line it already has and nothing more.

- [ ] **Step 1: `CLAUDE.md` → Layout, the kinds list — already correct, verify only**

**There is no edit here, and this step exists to say so.** The undercount was cleared on
2026-09-08 rather than left until this task, because `CLAUDE.md` is loaded into every session and
was asserting three deliverables against a ruling made on 2026-09-06. The first bullet already
reads:

```markdown
- **Deliverables** — `client/`, `server/`, `web/`, `sim/`, `viewer/`. These ship. The first three
  ship to players; `sim/` and `viewer/` ship to developers, so neither is in the client's export,
  neither is referenced by a server, and a release that omits both is complete. Ruled 2026-09-06,
  `docs/superpowers/specs/2026-09-06-simulation-design.md`.
```

The `Repo-level support` bullet's `tests/` belongs to the contracts plan Task 16, not to this task.

- [ ] **Step 2: Edit `CLAUDE.md` → Layout, the `sim/` bullet**

**This step replaces one bullet, and it restates none of the neighbouring bullets.** Eight plans
edit this list before this one, and three of them rewrite the text a wider edit would have to
quote:

- the contracts plan Task 16 replaced the `libs/` bullet — it now names each library and its
  contents and ends "`net10.0`, Godot-free, persistence-free." — and added a `tests/` bullet after
  it, which the management, game server skeleton and client shell plans each extended;
- the geometry plan Task 13 Step 5 rewrote `Tabletop.Geometry`'s parenthesis inside that bullet;
- the web app plan Task 17 Step 2 replaced the `web/` bullet with the Svelte SPA text, and
  **deleted** the sentence "`web/` is empty on purpose." from the closing note.

So do not reproduce the `web/` or `libs/` bullets. Both bullets this step used to insert already
sit between the `web/` bullet and the `libs/` one, added as stubs on 2026-09-08 when the
deliverables undercount was cleared. Replace the `sim/` stub, which reads:

```markdown
- `sim/` — `Tabletop.Sim`, a headless simulator that plays whole games with no client, over an
  in-process driver or a real socket, and records a projection per event. Does not exist yet.
```

with:

```markdown
- `sim/` — `Tabletop.Sim`, a library plus a console entry point that plays a whole game with no
  Godot client: a scenario, a seeded answer policy over the server's legal sets, two drivers
  (in process and over the socket), the invariant checks, and one recording file per run.
  It reads game state freely but holds no second implementation of the rules: it answers from the
  server's legal sets without narrowing them, and asserts invariants.
```

Leave the `viewer/` stub alone. The viewer plan Task 12 Step 1 replaces it, and its *Before* quotes
that stub.

**The closing note is not touched.** It began as "`server/`, `web/` and `libs/` are empty or absent
on purpose. Each module is designed before it is built."; the contracts plan narrowed the first
sentence to `server/` and `web/`, the management plan to `server/game/` and `web/`, the game server
skeleton plan to `web/` alone, and the web app plan Task 17 Step 2 **deleted it**. By the time this
plan runs the note reads, in full:

```markdown
Each module is designed before it is built.
```

Leave it exactly so. Nothing in the list is empty any more except `viewer/`, and its own bullet
says it does not exist yet — which is where the viewer plan Task 12 Step 1 replaces it. Re-adding
"`server/`, `web/` and `viewer/` are empty or absent on purpose." would contradict the three plans
that built `server/management/`, `server/game/` and `web/`.

- [ ] **Step 3: Edit `CLAUDE.md` → Commands, and `docs/architecture.md`**

In **Commands**, add two lines at the end of the block. The `dotnet test` line the contracts plan
Task 16 added has since been re-commented by the management plan Task 14 Step 2 and now reads
`dotnet test                  # every xUnit project under tests/; the server tests need docker compose up -d`,
and the management, web app, game server skeleton and client shell plans have each appended their
own run lines after it — so append rather than insert at a quoted anchor, and change no existing
line:

```bash
dotnet run --project sim -- --list                 # the scenarios
dotnet run --project sim -- skirmish --seeds 1..20 # twenty games, recordings in ./recordings
```

**`docs/architecture.md` needs nothing from this task.** Its Deliverables table already carries six
rows and an Audience column, the ruling paragraph already sits above the client's platform
correction, and the Repository layout block already lists `sim/` and `viewer/`. All of it landed on
2026-09-08 with the `CLAUDE.md` kinds list, for the same reason. What is still missing there is
`tests/`, on the layout block's last line and in the paragraph below it, and that belongs to the
contracts plan's spec correction 9, which is the coordinator's rather than a task in any plan.

Confirm the table's client row still reads *Windows first, other desktop platforms possible (Godot
export)*. The 2026-09-06 platform correction is what put that wording there, and an edit that
quotes the original "Windows (Godot export)" would revert it.

- [ ] **Step 4: Run the whole check**

```bash
cd /home/cstupi/src/tabletop
docker compose up -d
dotnet build && dotnet test
dotnet run --project sim -- skirmish --seeds 1..50 --no-recording
find sim tests/Tabletop.Sim.Tests -path '*/obj/*' -maxdepth 3 -type d | grep -v Unix
git status --short
```

Expected: build clean, every test green, fifty runs with no failures, no `obj/` outside `obj/Unix/`, and nothing unexpected in `git status`.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "Record what sim/ is in CLAUDE.md, and how to run it"
```

---
## Interfaces → Produces

Later plans — the viewer's above all — copy these names verbatim. `IRL<T>` abbreviates `IReadOnlyList<T>`, `IRD<K,V>` abbreviates `IReadOnlyDictionary<K,V>`.

**Every type and member in these tables is `public`**, so another project can reference `sim/Tabletop.Sim.csproj` and drive a run: `tests/Tabletop.Viewer.Tests` does exactly that to write its fixtures (viewer plan → *Interfaces needed from other modules*, item 4). Nothing is `internal` and nothing needs `InternalsVisibleTo`. **`sim/` references no test project**, here or anywhere, so a test project referencing `sim/` is not a cycle (spec correction 8).

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
## Applied from the viewer plan, 2026-09-06

`docs/superpowers/plans/2026-09-06-viewer.md` was written against this plan's recording schema and found four gaps in it, plus one doc comment. All five are **applied above rather than proposed**, because they came from a sibling plan and not from a spec: nothing a spec says changes, and the recording stays derived, so the whole cost is a re-run.

1. **`RecordedZone` gained `Polygons`** (viewer plan → *Interfaces needed from other modules*, item 1). `IReadOnlyList<IReadOnlyList<Point2>>`, wire name `polygons`, one ring per polygon of `{x, y}` in table inches, filled in `FrameProjector.BuildBoard` by `ZoneStyles.Resolve(z.Shape, table.Size)` and flattened to each `Polygon.Vertices`. Recorded rather than left to the reader because the board block carried only a shape id such as `frontline/1`; resolving one is `Tabletop.Geometry.ZoneStyles`, which is C#, and OPR's frontline is an absolute twelve-inch inset rather than a fraction of the table, so it cannot be normalised out of the id and the size. Porting `ZoneStyles` would be a second geometry implementation in the browser. It costs a handful of points once per zone in the board block, not per frame. Task 5 Step 1's board test pins the ring's four vertices and the twelve inches, Step 5's `BuildBoard` calls the resolver, `Point2`/`Polygon`/`ZoneStyles` join Task 5's Consumes list, and `Recording.cs` gains `using Tabletop.Geometry;`. The viewer's fallback was `Scene.zones[i].polygons = []` and no outline drawn, which is now also what an unresolvable shape id produces.

2. **`RecordedMarkerState` gained `Kind`, `Diameter` and `Label`** (item 2) — the first of the two fixes the viewer plan offered, not the second. The gap was real: `BuildBoard` reads `table.Markers`, so a token dropped during play by `marker_placed` appeared in a frame with a position and no definition at all, and the viewer's fallback was to draw it as a dashed half-inch ring labelled with the first eight characters of its id, with `MarkerShape.known` false. The alternative — having `BuildBoard` include every marker that appears in any frame rather than only the table's — was rejected on three counts. The live feed cannot implement it: it receives the `board` socket frame before any event, so a marker not yet placed is not knowable when the board is written, and the two feeds must produce the same shape. It would make `FrameProjector.Board` mutable and make the board block depend on how far the run got, so two runs of one scenario would carry different boards for the same table. And it would leave a frame that a reader has to join against a block which may not hold the row — a frame that is not whole state, which is the one thing the schema is for. Per-frame is also nearly free here: `Projection.Markers` holds `Marker` values that already carry kind, diameter and label, so it is three fields off a value `Add` already reads, and a game has single-digit markers — unlike a model's base, which is why models keep the definition/state split. `RecordedBoard.Markers` stays as the *setup* markers, which is what the board block describes; where both name the same marker they agree, and the frame is what a viewer draws from.

3. **`RecordedFrame` gained `Scores` and `Passed`** (item 3). `IReadOnlyDictionary<Guid, int> Scores` from `Projection.Scores`, `IReadOnlyList<Guid> Passed` from `Projection.Scheduler.Passed`; both copied rather than aliased, since the applier keeps mutating them and every frame is kept for the whole run, and both whole rather than deltas like everything else on a frame. The applier maintains both and `ProjectionComparer` already compares both (Task 7), so a frame carrying neither meant a viewer could show neither the score nor which team had passed. `RecordedFrame`'s fourteen members became sixteen, and Task 5 Step 1 gained `A_frame_carries_the_score_and_who_has_passed`.

4. **The public surface `tests/Tabletop.Viewer.Tests` needs** (item 4). Every name on that list is already `public` in this plan as written, and nothing needed changing: `RunLoop.BuildRecording` (Task 6) and `RunLoop.Log` (Task 7), `InProcessDriver.Info` and `.Board` (Task 4), `CompiledScenario.RecorderParticipantId`, `ScenarioCatalog.Get`, `ScenarioCompiler.Compile`, `Scenario` — a `record`, so `with { RoundCap = 2 }` works — and `Chooser(ulong)` (Task 1), `AnswerPolicy(Chooser, PolicyOptions?)` (Task 2), `ComposedAnswers(AnswerScript, AnswerPolicy?)` (Task 3), `RunSettings()` (Task 6, every parameter defaulted), all six `RecordingJson` members and every `Recorded*` record (Task 5). *Interfaces → Produces* now states it at the top, together with the fact that **`sim/` references no test project**, so a test project referencing `sim/` is not a cycle (spec correction 8).

5. **`RecordingMeta`'s `Outcome` doc comment** (viewer plan → *Spec corrections proposed*, item 3, which is aimed at this file rather than at a spec). It now reads that `decided`, `round_limit`, `stuck` and `failed` are what a finished run writes, and that `live` is what `viewer/`'s live feed sets when it manufactures a meta from `GameInfo`; `sim/` writes only the first four. The field was already a `string`, so nothing about the schema changes, and the alternative — a null `meta` on a live feed — would strip the team names the Ask panel needs to say who an Ask addresses.

---
## Spec corrections proposed

**Applied on 2026-09-08.** These were written into the documents they name, across five passes split
by target file so that no two passes wrote the same file. The list below is kept as the historical
record of what each correction asked for and why, so a *Before* quote here no longer matches the
document it quotes. `docs/handoff.md` → *The spec corrections are applied* records the exceptions:
the corrections that were already applied, the ones folded into a later correction, and the ones
skipped as superseded.

1. **Withdrawn.** This correction asked `CLAUDE.md` and the contracts plan to carve out a UUIDv7 exception for `sim/`. It is no longer needed: the project owner ruled on 2026-09-06 that the RNG seed is a stored value on the game row rather than a hash of the game id, so a simulator run sets `GameInfo.RngSeed` explicitly and draws ordinary `Guid.CreateVersion7()` ids. `CLAUDE.md`'s and the contracts plan's identifier constraints stand unamended.
2. **`docs/superpowers/specs/2026-09-06-simulation-design.md` → Deliberately not decided**, the item *"The recording's exact schema and whether it is versioned. It is derived, so a breaking change costs a re-run."* Replace with: *"**Settled 2026-09-06 by the simulation plan.** The schema is the record set in `sim/Recording/Recording.cs`, and it carries `schemaVersion` — one integer, so a viewer can refuse a file it does not understand rather than mis-draw it. It stays derived, so a breaking change still costs only a re-run."*
3. **Same file → Still open**, the item *"Whether a recording should carry the dice so a viewer can show a roll, or only its result."* Strike with: *"**Settled 2026-09-06 by the simulation plan: it carries the dice** — `RecordedRoll` holds every die's raw and modified face, whether it succeeded, the threshold and the modifiers. The result alone would make a viewer that cannot show why a unit died, which is the first thing a person debugging the engine wants to see, and the cost is a few hundred integers per game."*
4. **Same file → Still open**, the item *"Whether the round cap belongs in the scenario or in `GameRules`."* Strike with: *"**Settled 2026-09-06 by the simulation plan: the scenario.** OPR has no round limit and `GameRules` must not invent one; the cap is a property of a *run*, not of a ruleset, and putting it in `GameRules` would put a testing concern inside the rules. `Scenario.RoundCap`, overridable from the console with `--rounds`."*
5. **Same file → Deliberately not decided**, the item *"Whether the policy prefers to act over passing when both are legal."* — the uniform half having already been settled by the project owner's ruling of 2026-09-06. Replace with: *"**Settled 2026-09-06 by the simulation plan: it prefers to act.** `PolicyOptions.PassProbability` defaults to `0`, so a pass is taken only when the legal set admits nothing else. It stays a knob rather than a decision, because tuning it toward plausible play is a step toward an opponent that plays well and that is a later feature."*
6. **Applied to the spec, not proposed.** This correction argued that reading model positions was the one unavoidable reading and that `PolicyView` should expose nothing else. The project owner's ruling of 2026-09-06 makes that argument obsolete — the simulator may read anything — so `docs/superpowers/specs/2026-09-06-simulation-design.md` → *Two ways to answer an Ask* has been edited in place and now reads:

   > **Ruled 2026-09-06.** The simulator may read anything the engine exposes: properties, stats, wounds, conditions, teams, the projection, the pinned `PropertySet`. An earlier draft banned all of it, which was both over-broad and self-contradictory — the invariant checks, the expectations and the recording all read wounds, conditions, positions and properties. Two prohibitions carry the argument instead:
   >
   > 1. **It must never narrow the legal set.** An answer is chosen from what the server offered, whole. A policy that filtered the set to answers it judged sensible would stop exercising the ones that find bugs.
   > 2. **It must never compute a rules outcome the engine should compute.** No deciding for itself whether a target is in range, whether a wound lands, or what a modifier totals. A second rules implementation inside the simulator can agree with a broken engine and hide the fault.
   >
   > Separately, and for a different reason: the **default** policy stays uniform over the legal set and ignorant of state. A cleverer policy explores a narrower, more plausible region and finds fewer bugs, so a state-reading policy is opt-in and named as such, never the default for a soak run. An opponent that plays well is a later feature and would live behind the same seam.
7. **Same file → What a full game means**, the Decided row: *"One team has models and the others do not."* Add: *"Read on the table, and only from round one: before deployment every model is on the shelf, so every team has none and the condition would fire on the first frame of every game."* **Subsumed by correction 11**, which replaces that whole table and carries this wording in it; apply 11 and this row is already done.
8. **Same file → Testing**, the sentence *"It is then the harness the interpreter's and scheduler's own full-game tests use."* Add: *"Those tests live in `tests/Tabletop.Sim.Tests` rather than in `tests/Tabletop.GameServer.Tests`, because `sim/` references the game server and a test project that `sim/` referenced back would be a cycle. `sim/` therefore reaches the core `Properties` set through the production path — `PropertiesSeeder.EmbeddedDocuments()` — rather than through `TestProperties`, and builds its armies with `ScenarioArmy` rather than `TestArmies`."*
9. **Same file → `sim/` is a fourth deliverable.** Add a paragraph: *"One thing the simulator must do that is not an answer to an Ask: **deploy**. Deployment's Ask names a unit, and putting that unit's models on the table is an ordinary `MoveModels` command rather than an Ask (the scheduler → The turn is an Ask: 'moving models by hand never ends one'). So `sim/` places models itself, inside the team's own deployment zone, resolving the zone's shape with `Tabletop.Geometry.ZoneStyles`. That is geometry rather than a rule, and nothing judges whether a placement is good — placement is free under every enforcement setting."*
10. **`docs/schema.md` → Diagram**, the `PARTICIPANT` block, which lists `id`, `team_id`, `account_id`, `army_snapshot`. Add `uuid game_id FK` and `uuid army_id FK`. Reason: the game server plan's `FindParticipantAsync(gameId, accountId)`, its `SetParticipantArmyAsync`, and the management plan's `POST /v1/games/{id}/join` all require both columns, and `scripts/postgres/init/001_schema.sql` has them; the diagram is stale rather than the schema. `sim/`'s `SocketRowSeeder` inserts both.
11. **`docs/superpowers/specs/2026-09-06-simulation-design.md` → What a full game means**, the sentence *"A run reaches one of three ends, and a run that reaches none of them is a bug the simulator exists to find."* and the three-row table under it. The plan produces five `RunOutcome` kinds, not three, and the spec names only three, so the table cannot be read as exhaustive as it stands. Replace the sentence and the table with:

    > A run reaches one of five ends. Three are the game ending; two are the harness. A run that reaches none of them is a bug the simulator exists to find.
    >
    > | End | Condition |
    > | --- | --- |
    > | Decided | One team has models and the others do not. Read on the table, and only from round one: before deployment every model is on the shelf, so every team has none and the condition would fire on the first frame of every game |
    > | Round limit | A configured round cap, since OPR has no round limit and a stalemate must still terminate. A cap of *n* means *n* whole rounds: the run ends on round *n*'s boundary Ask rather than part-way through round *n* + 1 |
    > | Stuck | No Ask is open and the engine emits nothing, or an Ask is answered and reopens unchanged |
    > | Failed | An Ask the script does not cover in a script-only run, a failed scenario expectation, or a failed invariant. This is an assertion breach rather than a game ending, and it is the outcome that makes the console exit non-zero |
    > | Paused | The step-and-wait seam: the loop stopped with an Ask open and answered nothing, waiting for a person or a test to answer it. A paused run is not a failure and is resumed rather than ended |

    Three of those five rows are the spec's own. The Decided row carries the wording correction 7 asks for, folded in so the replacement can be pasted whole, which leaves 7 subsumed rather than in conflict; the Round limit row gains the boundary sentence this plan settled (Task 6 → *A cap of *n* means *n* whole rounds*), because a cap that is off by one round is the difference between two readings of the same word. The two new rows are `Failed` and `Paused`. Both are already elsewhere in the spec — `Failed` in *What it asserts* ("a failed assertion here") and *Two ways to answer an Ask* ("an unscripted Ask under a script-only run is a failure"), `Paused` in *Interactive debugging is a door, not a room* ("the driver interface has a step-and-wait mode") — but neither is in the one table that claims to enumerate how a run can end, which is the table anybody counting outcomes reads. Naming them there also states the thing the plan relies on and the spec does not say: `Decided`, `RoundLimit` and `Paused` pass; `Stuck` and `Failed` do not.

## Interfaces needed from other modules

Everything below is consumed by its exact name; nothing here is a request to rename or reshape.

1. **Interpreter plan** — `PropertiesSeeder.EmbeddedDocuments()` (`static IReadOnlyList<PropertySetDocument>`) and `PropertiesJson.ToSet(PropertySetDocument, Func<Guid>)` must both be `public`, because `ScenarioCompiler.CoreProperties` is how `sim/` gets the core Grimdark Future set without touching a test project. `PropertySetDocument.GameRules` is read to pick the document. Also `InMemoryPropertiesStore.Add(PropertySet)` returning `this`, `DiceSource()` parameterless, and `RulesEngineFactory(IPropertiesStore, IDiceSource, ISchedulerFactory)` public.
2. **Scheduler plan** — `SchedulerFactory()` parameterless and public, since `InProcessDriver` registers it directly rather than through DI. `AskQuestions.EndRound` is the boundary Ask's question, and invariant 5 reads it from `Tabletop.Protocol.AskQuestions` (added to the contracts plan on 2026-09-06 by that plan's *Interfaces needed*).
3. **Game server skeleton plan** — `AtRest(long Seq)` at the end of `ConnectAsync` at every status and at the end of `HandleCommandAsync` whatever the outcome, with `Seq` the log's high-water mark (`Play?.Projection.LastSeq ?? 0`), broadcast when the batch appended at least one event and sent to the provoking client alone when it appended none, and a command's `Refused` sent before that command's `AtRest`. **That plan landed this on 2026-09-06 and this one consumes it unchanged**; it is why `SocketDriver.SubmitAsync` reads the submitting socket's rest frame and why `StepAsync` waits on the recorder's only when the command appended. Also `GameSession.ConnectAsync(ISessionClient)`, `ReceiveAsync(ISessionClient, SocketMessage)`, `RunAsync<T>(Func<Task<T>>)`, `DrainAsync(string)` and the `Play` property public; `Hydrator.HydrateAsync`; `Replay.To(Board, GameInfo, IReadOnlyList<GameEvent>, long)`; `InMemoryEventLog()`; `GameServerOptions` constructible from object-initializer syntax with only `DbConnection`, `StytchProjectId` and `StytchJwksUrl` set (every other member defaulted); `PostgresGameRows(NpgsqlDataSource)`; and `PostgresFixture.CountAsync`, confirmed from that plan as `Task<int> CountAsync(string sql, params object[] args)` — positional parameters, so `DriverAgreementTests` passes the game id as `$1`, and an `int` return, so the one comparison against a `long` sequence casts. Nothing here asks for a change.
4. **Contracts plan** — `AtRest(long Seq)` as a `SocketMessage` kind, wire kind `at_rest`, server to client. **Landed 2026-09-06** as `public sealed record AtRest(long Seq) : SocketMessage` with `[JsonDerivedType(typeof(AtRest), "at_rest")]`, the eighth of thirteen kinds; this plan consumes it and adds nothing to it. Also `EventApplier.EffectiveLog(IReadOnlyList<GameEvent>)` as a public static, which the undo invariant and invariant 4 both call; `Projection.Initial(Board, GameInfo)`; `WireJson.Serialize<T>(T)` accepting an enum, which is how `sim/` prints `alternating-activation` rather than inventing a second spelling. Also **`GameInfo.RngSeed`**, the stored RNG seed the project owner ruled on 2026-09-06, together with the `management.game` column it comes from. That plan owns the name, the SQL type and the C# type; this one consumes its choice.

   **Landed 2026-09-06, and this plan follows it: `long RngSeed`, positional and last among `GameInfo`'s scalars — after `WaitsBetweenSteps`, before `Teams` and `Participants` — over a `management.game.rng_seed bigint not null` column with no default** (contracts plan → *Interfaces → Produces* and spec correction 12). Positional on purpose, so a construction that forgets the seed fails to compile rather than silently getting `0`. `long` because `bigint` is signed, so `ScenarioCompiler` writes `unchecked((long)s.Seed)` from the scenario's `ulong Seed`; the reverse cast (`unchecked((ulong)info.RngSeed)`, where `GameState.RngSeed : ulong` is set) belongs to the interpreter plan, not this one. `SocketRowSeeder` passes `info.RngSeed` straight into the insert. The four places this plan spells the seed are `ScenarioCompiler.Compile`, `SocketRowSeeder.SeedAsync`, and the seed assertions in `ScenarioTests` and `RunLoopTests`.
   **The two shapes the rulings of 2026-09-06 landed, consumed as stated:** `TokenSpend(IReadOnlyDictionary<Guid,int> MaxByModel)` answered by `TokensSpent(IReadOnlyDictionary<Guid,int> Counts)`, still wire kinds `token_spend` and `tokens_spent`, still seven legal-set kinds and eight answer kinds, so `AnswerPolicy.ShapeNames` is unchanged and `AnswerSpend` draws once per entry (p.14). And a Takedown's chosen model as a `PieceSelection` over model ids, which needs nothing at all here: `AnswerPieces` picks ids out of the list it is handed without asking what they name, and the deployment loop keys on the question name rather than the answer shape.

5. **Geometry plan** — `Movement.Check(PlacedModel, Point2, double, IReadOnlyList<WorldVolume>, IReadOnlyList<WorldVolume>, double?, TableSize?) → MoveCheck` with `WithinAllowance`, `CrossesImpassable` and `LeavesTable`; `ZoneStyles.ShapesOf(string)`, `ZoneStyles.Resolve(string, TableSize)`, `ZoneStyles.Contains(string, TableSize, Point2)`, and the style constants `Frontline` and `FreeForAll`; `Settling.SurfaceHeightAt(Point2, IReadOnlyList<WorldVolume>)`.

   `Resolve` is assumed to be `IReadOnlyList<Polygon> Resolve(string shapeId, TableSize)` with `sealed record Polygon(IReadOnlyList<Point2> Vertices)` (geometry design → *Interfaces → Produces*; contracts design → the `Polygon` row), answering in the corner frame, and to return no polygons for a shape id it does not know rather than throwing. `RecordedZone.Polygons` is that list flattened to `Vertices`, so if `Resolve` throws on an unknown id instead, `BuildBoard` wraps the call and records `[]`.
6. **The viewer plan** — consumes this plan's whole `Tabletop.Sim.Recording` table above as its input format, and `RecordingJson.SchemaVersion` as the number to check. It should generate its TypeScript types from these C# records, as the spec requires of the Protocol types, rather than hand-writing them.

   **That plan landed 2026-09-06 and named four gaps in the schema and one doc comment; all five are applied here** — see *Applied from the viewer plan* above. Nothing else it asks for is a change: the public surface its test project references was already public, and `sim/` references no test project either way.
7. **Management API plan** — `POST /v1/dev/sessions`, body `{"name": "..."}`, response `{"sessionJwt": "...", "account": {"id": "...", "name": "..."}}`, mapped only when `IHostEnvironment.IsDevelopment()`. `SocketCredentials.MintAsync` calls it once per participant. Landed in that plan's Task 5 by owner ruling 4 of 2026-09-06, together with the test that the route is absent outside a development environment. This plan needs nothing else from management: the seeder writes `management.account`, `game`, `team` and `participant` as raw SQL, and the mint's `on conflict do nothing` insert of the same account row is harmless.
8. **The interpreter and scheduler plans, later** — when their full-game tests are written they reference `sim/Tabletop.Sim.csproj` from `tests/Tabletop.Sim.Tests` and build a `Scenario`, an `InProcessDriver` and a `RunLoop`. `sim/` never references a test project (spec correction 8).

## Questions for the project owner

1. **How does a socket run get its session JWTs? Answered by the project owner on 2026-09-06, ruling 4: from a development-only endpoint that mints one.** Hand-pasting is rejected. The endpoint is `POST /v1/dev/sessions` on the **management API**, not the game server, because management holds the Stytch project secret and already creates users and authenticates sessions; the game server holds no credential and could not mint anything real. It takes a name, finds or creates the Stytch user and the `account` row, and returns a real Stytch session JWT, so the socket driver stays on the production verification path. It is gated on `IHostEnvironment.IsDevelopment()` rather than on a configuration flag, and it is absent from the route table a production build serves. Applied in Task 8: `SocketCredentials.MintAsync` replaces `FromEnvironment`, `AccountOf` reads the account id from the mint's response instead of decoding the token, `NameFor` makes the account names stable per scenario and participant, and `--driver socket` has no token variable. The cost is that a console socket run now needs network access to the Stytch test project; the in-process driver and `DriverAgreementTests` still need none. See the management API plan, Task 5, for the endpoint and its absence test.
2. **Should a `stuck` run fail the console's exit code? Ruled 2026-09-09 by the owner, as assumed: yes** ([closing the open questions](../specs/2026-09-09-open-questions-design.md)). A game that cannot finish is a bug in the rules engine, and CI is the only place anyone will run 500 seeds. Assumed yes: `RunReport.Passed` is false for `stuck` and `failed`, so `dotnet run --project sim -- skirmish --seeds 1..500` exits `1` if any game hangs, which is what makes it usable in CI. If a stuck run should only warn, `Summary.Failed` counts `failed` alone and the exit code changes; the frames are still dumped either way.
3. **Should `sim/` be in `Tabletop.sln`? Closed 2026-09-09 on `CLAUDE.md`'s own terms: yes** — the solution is *"at the repo root, spanning every project"* ([closing the open questions](../specs/2026-09-09-open-questions-design.md)). Assumed yes, in a `sim` solution folder, so `dotnet build` at the root builds it and `dotnet test` runs its tests — which is what makes it part of the whole-tree check every plan ends with. If it should be excluded so a client-only build stays small, it moves to its own solution and `CLAUDE.md`'s Commands section grows a line; nothing in the code changes.
4. **How many scenarios should the catalog ship with, and are these the right five? Ruled 2026-09-09 by the owner: six.** `casting` joins them, because casting is the only path with two player decisions a whole-game run would otherwise never open — the token spend map, and the caster spending its own leftover tokens on its own roll — and both were changed by rulings of 2026-09-06. Casting is not a mode: a spell is cast inside an ordinary activation, so the preset differs from `skirmish` only in fielding a `Caster(2)` and declaring a spell. The spell is the scenario's own, folded into the pinned `PropertySet` the way `TestProperties.WithSpells` does, because the shipped content document is every special rule on pages 13–15 and holds no spells — a spell list belongs to an army book this project has not designed ([closing the open questions](../specs/2026-09-09-open-questions-design.md)). The original assumption: `skirmish`, `terrain`, `player-turn`, `free-for-all` and `stalemate` — one per thing that could plausibly diverge (a plain game, terrain declarations, the second scheduler shape, three teams, and a run that must hit the cap). A regression scenario for a specific rules interaction is a script and is added the same way; the shape of the catalog does not change. **One candidate sixth, raised 2026-09-06 and not added:** a casting scenario. Casting now holds two player decisions rather than one — the caster's spell and target, then a simultaneous per-model token spend by both teams (p.13) — and it is the only sealed window a full game reaches, so nothing in the present five exercises `TokenSpend`, the sealed reveal, or a window addressed to more than one team. Adding it is a `Scenario` with two casters per side and a `--script` for the spend, not a change to anything else.
5. **Is 250 ms the right quiet window for the socket driver? Answered by the project owner on 2026-09-06: it is replaced by a rest frame.** The game server sends `AtRest(long Seq)` when it has finished processing an inbound `connect` or `command`, carrying the log's high-water mark, broadcast when the batch appended and sent to the provoking client alone when it did not. `SocketDriver.StepAsync` waits for that frame instead of for silence, and `RestTimeout` (30 seconds) replaces `QuietWindow` as a guard on a hung server rather than as a guess at quiescence; expiry is `RestTimeoutException`, which `RunLoop` turns into a `Failed` run. Task 8 is rewritten accordingly, and `DriverAgreementTests` compares the two drivers run for run again. In process is unaffected and stays exact.

---

## Self-review

**Spec coverage.** Every section of `docs/superpowers/specs/2026-09-06-simulation-design.md` that concerns `sim/` maps to a task: *`sim/` is a fourth deliverable* → Tasks 1 and 11; *Two ways to answer an Ask* → Tasks 2 and 3; *Two drivers behind one interface* → Tasks 4 and 8; *The recording is the artifact* → Task 5; *Interactive debugging is a door* → Task 9; *What a full game means* → Task 6; *What it asserts* → Task 7; *Testing* (`sim/` half) → the tests in every task. The `viewer/` sections are another plan's and are untouched beyond the one line each in Task 11. Four *Deliberately not decided* / *Still open* items that `sim/` had to settle are settled in the spec corrections rather than silently; corrections 1 and 6 were overtaken by the project owner's rulings of 2026-09-06 and now record what became of them. Correction 11 is the one place the plan outruns the spec rather than filling a gap it left: the loop produces five outcomes where *What a full game means* tabulates three, so that table is asked to name `Failed` and `Paused` too, and correction 7 folds into it.

**Placeholders.** None. Every step carries the code or the exact command it needs. `PostgresFixture.CountAsync` was left assumed in an earlier draft and is now confirmed against the game server skeleton plan — `Task<int> CountAsync(string sql, params object[] args)`, positional parameters and an `int` return — so the inline-SQL fallback is gone and Task 8 casts instead. The one remaining place where an earlier plan's signature could not be confirmed from its text, `Projection`'s team-ordinal lookup, names its fallback inline rather than leaving a gap.

**Termination is stated once.** The *Termination, exactly* table and `RunLoop.DriveAsync` now give the round limit the same way — `Round >= RoundCap` with every open Ask the boundary Ask — which is also what `docs/design/simulation/design.md` records. `A_cap_of_two_stops_at_the_end_of_the_second_round` pins the boundary that the two earlier readings disagreed about. Where a check is weaker than it looks it says so: `AskReopenedUnchanged` names `RunSettings.MaxSteps` as its backstop in the table, in the `Reopened` doc comment and in `RunSettings`' own.

**Type consistency.** `SubmitResult.Accepted`/`Rejected`, `RunOutcome.Decided`/`RoundLimit`/`Stuck`/`Failed`/`Paused`, `RecordedFrame`'s sixteen members, `PolicyView`'s five-parameter constructor (two positional, three optional) and `LogLines.Of`'s three-argument signature are used the same way in every task that touches them. `CompiledScenario` gained `RecorderParticipantId` in Task 1 and is consumed with it in Tasks 4, 5, 6 and 8. `RunLoop` gained `Log` in Task 7 and `Answers` in Task 8, both named where they are added.
