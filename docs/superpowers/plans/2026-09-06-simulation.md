# The simulator (`sim/`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A fourth deliverable, `sim/`, that plays a Grimdark Future game from deployment to a decided end without the Godot client — scripted, seeded, or both — against either an in-process session or a real socket, asserting the invariants that hold for any game and writing one recording file per run.

**Architecture:** One `IGameDriver` interface over two backends: `InProcessDriver` hydrates a `GameSession` over `InMemoryEventLog` and `InMemoryPropertiesStore` with an `ISessionClient` that records frames, and `SocketDriver` opens a real WebSocket to a running game server with a session JWT and a Postgres log. Above the driver sit an answer source (a script keyed by question and ordinal, falling through to a seeded policy that reads only the legal set), a run loop that terminates on decided / round cap / stuck, the five invariant checks, and a recording writer that emits a **projection recording** — one frame per event, full state per frame.

**Tech Stack:** `net10.0`, C# 12, xUnit, `System.Text.Json`, Npgsql (through the game server's own types), `System.Net.WebSockets.ClientWebSocket`.

**Spec:** [`docs/superpowers/specs/2026-09-06-simulation-design.md`](../specs/2026-09-06-simulation-design.md) — the whole document. `viewer/` is a separate plan's; nothing here builds it.

## Global Constraints

- `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`, `RootNamespace Tabletop.Sim`. Matches every other project (contracts plan → Global Constraints).
- **`sim/` never contains a rule.** No `PropertySet`, `IGameRules`, `GfNames`, stat, threshold, action distance or terrain declaration is read to decide *what to do*. The legal set is the whole input of an answer. `sim/` may read `Tabletop.Geometry` (a library, not a ruleset) and the projection's positions, because the legal set names model ids without saying where they stand. Spec → *Two ways to answer an Ask*: "Nothing in `sim/` may read a rule to decide what is a good move".
- **`sim/` is a deliverable, not a test project** (spec → `sim/` is a fourth deliverable, ruled by the project owner 2026-09-06). It references `server/game/Tabletop.GameServer.csproj` and the three `libs/`; it references **no test project**, because `tests/Tabletop.GameServer.Tests` and `tests/Tabletop.Sim.Tests` reference it.
- Nothing outside `sim/` and `tests/Tabletop.Sim.Tests` is scaffolded, except the two documentation edits in Task 11 (`CLAUDE.md`, `docs/architecture.md`).
- Names owned by earlier plans are consumed verbatim and never redefined: `Tabletop.Protocol` and `Tabletop.Content` (contracts plan → Interfaces → Produces), `GameSession`, `SessionServices`, `Hydrator`, `IEventLog`, `InMemoryEventLog`, `Replay`, `CommandDispatcher`, `IGameRows`, `OwnedArmy`, `Actor`, `GameState`, `ISessionClient`, `GameServerOptions`, `EventFilter` (game server skeleton plan), `RulesEngineFactory`, `IPropertiesStore`, `InMemoryPropertiesStore`, `PropertiesSeeder`, `PropertiesJson`, `IDiceSource`, `DiceSource` (interpreter plan), `SchedulerFactory` (scheduler plan).
- One world unit = one inch; yaw in degrees; the table frame is the corner frame — origin at a corner, `0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up (game server skeleton plan → spec correction 3).
- Identifiers are UUIDv7. In `sim/` they are **derived from the run seed** by `SimIds.Of` rather than drawn from `Guid.CreateVersion7()`, so a seed reproduces a run byte for byte; the layout is still a valid version-7 UUID. This is the one deliberate departure from the contracts plan's "no integer ids, `Guid.CreateVersion7()`" constraint, and it is spec correction 1.
- **The recording is a projection recording, not an event log** (spec → The recording is the artifact). It is derived and disposable, is never replayed into the engine, and the event log stays the only thing undo and hydration read. Say so in the header comment of every recording type.
- The recording schema is version `1` in a `version` field. The spec leaves versioning undecided; the field costs one integer and a viewer that reads it can refuse a file it does not understand.
- JSON: camelCase, string enums as plain strings, nulls omitted, indented. `sim/` owns `RecordingJson.Options`; it does **not** reuse `WireJson.Options`, because the recording is not a wire format.
- Enforcement in a scenario defaults to `Enforcement.Strict`, which is the opposite of every fixture's default and is the point: a policy answer outside the legal set must be refused loudly rather than logged and accepted.
- Commit after every task, imperative one-line subject, ending with the two trailers the session requires:
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` and `Claude-Session: <the session URL>`.

---

## File Structure

```
sim/Tabletop.Sim.csproj                  net10.0, RootNamespace Tabletop.Sim; OutputType Exe
sim/SimIds.cs                            deterministic UUIDv7 from (seed, path)
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
sim/Recording/LogLines.cs                one line per event kind, rules-free
sim/SimOptions.cs                        the argument surface
sim/Summary.cs                           the run table and summary.json
sim/Program.cs                           the console entry point
tests/Tabletop.Sim.Tests/
  Tabletop.Sim.Tests.csproj              references sim/ and tests/Tabletop.GameServer.Tests
  PostgresCollection.cs                  the local [CollectionDefinition] over the imported fixture
  SimIdsTests.cs  ChooserTests.cs  AnswerPolicyTests.cs  MoveRegionPolicyTests.cs  AnswerScriptTests.cs
  ScenarioTests.cs  InProcessDriverTests.cs  RecordingTests.cs  RunLoopTests.cs
  InvariantTests.cs  DriverAgreementTests.cs  StepAndWaitTests.cs  ProgramTests.cs
```

**Why `tests/Tabletop.Sim.Tests` and not the game server's test project.** The spec makes `sim/` the harness the interpreter's and scheduler's full-game tests use, so `tests/Tabletop.GameServer.Tests` will reference `sim/`; `sim/` therefore cannot reference it back, and the full-game tests written against this harness live in `tests/Tabletop.Sim.Tests`. `tests/Tabletop.Sim.Tests` **does** reference `tests/Tabletop.GameServer.Tests`, for `PostgresFixture`, `GameServerFactory` and `TestKeys` — a test-to-test reference, which is why `PostgresCollection.cs` re-declares the collection locally (xUnit resolves `[CollectionDefinition]` in the test assembly only, while the fixture type may be imported).

**What `sim/` builds instead of the fixtures.** `TestProperties`, `TestArmies`, `TestGames`, `Sim` and `SchedulerHarness` all live in `tests/Tabletop.GameServer.Tests` and are out of reach for the reason above. `sim/` reaches the same content through the production path: `PropertiesSeeder.EmbeddedDocuments()` + `PropertiesJson.ToSet` for the core Grimdark Future `PropertySet` (the same embedded `grimdark-future-core.properties.json` that `TestProperties.Core` reads), and `ScenarioArmy` for armies. Recorded as *Interfaces needed from other modules* item 1.

---
### Task 1: The project, deterministic ids, the chooser, and the scenario

**Files:**
- Create: `sim/Tabletop.Sim.csproj`, `sim/SimIds.cs`, `sim/Chooser.cs`, `sim/Scenarios/Scenario.cs`, `sim/Scenarios/ScenarioCompiler.cs`, `sim/Scenarios/ScenarioCatalog.cs`
- Create: `tests/Tabletop.Sim.Tests/Tabletop.Sim.Tests.csproj`, `tests/Tabletop.Sim.Tests/PostgresCollection.cs`
- Test: `tests/Tabletop.Sim.Tests/SimIdsTests.cs`, `tests/Tabletop.Sim.Tests/ChooserTests.cs`, `tests/Tabletop.Sim.Tests/ScenarioTests.cs`
- Modify: `Tabletop.sln`

**Interfaces:**
- Consumes: `Army`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`, `ArmySnapshot`, `TableState`, `TerrainPlacement`, `TerrainVolume`, `Marker`, `MarkerKind`, `DeploymentZone`, `VisualRef` (Content); `Base`, `TableSize`, `Point3`, `Yaw`, `Volume`, `VolumeKind`, `Footprint`, `Point2`, `TerrainDeclarations` (Geometry); `GameInfo`, `GameStatus`, `SchedulerShape`, `Enforcement`, `Team`, `Participant`, `Board`, `ArmyInPlay` (Protocol); `PropertiesSeeder.EmbeddedDocuments()`, `PropertiesJson.Hash`, `PropertiesJson.ToSet`, `PropertySet` (game server `Tabletop.GameServer.Properties`); `GrimdarkFutureRules.RulesId` (`Tabletop.GameServer.Rules.GrimdarkFuture`).
- Produces: `static class SimIds { Guid Of(ulong seed, string path); Guid GameId(ulong seed); }`; `sealed class Chooser(ulong seed)` with `int Next(int bound)`, `double Double()`, `bool Chance(double p)`, `T Pick<T>(IReadOnlyList<T> items)`, `IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T>)`, `IReadOnlyList<T> Subset<T>(IReadOnlyList<T> items, int count)`, `long Drawn`; `sealed record Scenario(string Name, ulong Seed, SchedulerShape Shape, Enforcement Enforcement, int RoundCap, TableSize Table, IReadOnlyList<ScenarioArmy> Armies, IReadOnlyList<ScenarioTerrain> Terrain, IReadOnlyList<ScenarioMarker> Markers, IReadOnlyList<string> ZoneShapes, AnswerScript Script, bool ScriptOnly, IReadOnlyList<Expectation> Expectations)` with `Scenario WithSeed(ulong)`; `sealed record ScenarioArmy(string Name, int Units, int ModelsPerUnit, IReadOnlyList<ScenarioRule> UnitRules, IReadOnlyList<ScenarioRule> ModelRules, IReadOnlyList<ScenarioWeapon> Weapons, IReadOnlyDictionary<string,string> UnitStats)`; `sealed record ScenarioRule(int Unit, string Name, int? Parameter)`; `sealed record ScenarioWeapon(string Name, IReadOnlyDictionary<string,string> Stats, IReadOnlyList<ScenarioRule> Rules)`; `sealed record ScenarioTerrain(string Mesh, Point3 Position, double Yaw, double Scale, IReadOnlyList<Volume> Volumes)`; `sealed record ScenarioMarker(MarkerKind Kind, Point3 Position, double Diameter, string? Label)`; `sealed class ScenarioBuilder` (the fluent form, `Build()`); `sealed record CompiledScenario(Scenario Scenario, GameInfo Info, Board Board, PropertySet Properties, IReadOnlyList<Guid> TeamIds, IReadOnlyList<Guid> ParticipantIds, IReadOnlyDictionary<Guid,Guid> TeamOfParticipant, TableState Snapshot, Guid RecorderParticipantId)` with `Guid ParticipantFor(Guid teamId)`; `static class ScenarioCompiler { CompiledScenario Compile(Scenario s); PropertySet CoreProperties { get; } }`; `static class ScenarioCatalog { IReadOnlyList<string> Names { get; } Scenario Get(string name); bool TryGet(string name, out Scenario) }`.

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
using Tabletop.Sim;

namespace Tabletop.Sim.Tests;

public class SimIdsTests
{
    [Fact]
    public void An_id_is_a_v7_uuid_and_a_pure_function_of_seed_and_path()
    {
        var a = SimIds.Of(7, "army/1/unit/2");
        Assert.Equal(a, SimIds.Of(7, "army/1/unit/2"));
        Assert.NotEqual(a, SimIds.Of(8, "army/1/unit/2"));
        Assert.NotEqual(a, SimIds.Of(7, "army/1/unit/3"));
        Assert.Equal(7, a.ToByteArray(bigEndian: true)[6] >> 4);        // version 7
        Assert.Equal(2, a.ToByteArray(bigEndian: true)[8] >> 6);        // RFC variant
    }

    [Fact]
    public void The_game_id_is_the_seed_so_the_dice_repeat() =>
        Assert.Equal(SimIds.GameId(42), SimIds.GameId(42));
}
```

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
Expected: `CS0246: The type or namespace name 'SimIds' could not be found`.
- [ ] **Step 5: Write `sim/SimIds.cs` and `sim/Chooser.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Tabletop.Sim;

/// <summary>
/// Ids derived from the run seed, so a seed reproduces a run byte for byte. The layout is a valid
/// version-7 UUID: 48 bits of timestamp, then version 7, then the RFC variant, then hash bytes.
/// The timestamp is fixed at the epoch because a derived id has no time of creation.
/// Every other project uses Guid.CreateVersion7(); this is sim/'s one departure (Global Constraints).
/// </summary>
public static class SimIds
{
    public static Guid Of(ulong seed, string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}/{path}"));
        var bytes = new byte[16];
        Array.Copy(hash, 0, bytes, 0, 16);
        bytes[0] = bytes[1] = bytes[2] = bytes[3] = bytes[4] = bytes[5] = 0;   // the 48-bit timestamp
        bytes[6] = (byte)(0x70 | (bytes[6] & 0x0F));                            // version 7
        bytes[8] = (byte)(0x80 | (bytes[8] & 0x3F));                            // RFC 9562 variant
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>The game id is the RNG seed of the engine (GameState.RngSeed is SHA-256 of it), so the dice follow the run seed.</summary>
    public static Guid GameId(ulong seed) => Of(seed, "game");
}
```

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
    public void Compiling_the_same_scenario_twice_gives_identical_ids()
    {
        var s = ScenarioCatalog.Get("skirmish");
        var a = ScenarioCompiler.Compile(s);
        var b = ScenarioCompiler.Compile(s);
        Assert.Equal(a.Info.Id, b.Info.Id);
        Assert.Equal(a.Board.Armies[0].Snapshot.Units[0].Models[0].Id, b.Board.Armies[0].Snapshot.Units[0].Models[0].Id);
        Assert.NotEqual(a.Info.Id, ScenarioCompiler.Compile(s.WithSeed(s.Seed + 1)).Info.Id);
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
    private AnswerScript _script = AnswerScript.Empty;
    private bool _scriptOnly;
    private readonly List<Expectation> _expectations = [];

    public ScenarioBuilder Shape(SchedulerShape shape) { _shape = shape; return this; }
    public ScenarioBuilder Enforcing(Enforcement e) { _enforcement = e; return this; }
    public ScenarioBuilder RoundCap(int rounds) { _roundCap = rounds; return this; }
    public ScenarioBuilder Table(double width, double length) { _table = new TableSize(width, length); return this; }
    /// <summary>A zone style id from Tabletop.Geometry.ZoneStyles; its shapes expand in team-ordinal order.</summary>
    public ScenarioBuilder Zones(string style) { _zoneStyle = style; return this; }

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
        return new Scenario(name, seed, _shape, _enforcement, _roundCap, _table, _armies, _terrain, _markers, shapes, _script, _scriptOnly, _expectations);
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
/// setup snapshot and one ArmySnapshot per participant. Every id comes from SimIds, so a seed repeats.
/// </summary>
public static class ScenarioCompiler
{
    /// <summary>The core Grimdark Future set, through the production path: the game server's embedded document.</summary>
    private static int _propertyId;

    public static PropertySet CoreProperties { get; } = PropertiesJson.ToSet(
        PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId),
        () => SimIds.Of(0, $"properties/{Interlocked.Increment(ref _propertyId)}"));

    public static CompiledScenario Compile(Scenario s)
    {
        var seed = s.Seed;
        var teams = s.Armies.Select((_, i) => new Team(SimIds.Of(seed, $"team/{i + 1}"), i + 1)).ToList();
        var participants = teams.Select((t, i) =>
            new Participant(SimIds.Of(seed, $"participant/{i + 1}"), SimIds.Of(seed, $"account/{i + 1}"), $"Sim {i + 1}", t.Id, null)).ToList();
        // The recorder: a participant with no team, which is what a spectator is (whole-design review ruling 5).
        // It is never Asked, and its filtered event stream is what the driver's projection and the recording are built from.
        var recorder = new Participant(SimIds.Of(seed, "participant/recorder"), SimIds.Of(seed, "account/recorder"), "Recorder", null, null);
        participants.Add(recorder);

        var snapshot = new TableState(
            SimIds.Of(seed, "table"), s.Name, s.Table, VisualRef.OfBuiltin("grass"),
            s.Terrain.Select((t, i) => new TerrainPlacement(
                SimIds.Of(seed, $"terrain/{i}"), VisualRef.OfBuiltin(t.Mesh), t.Position, new Yaw(t.Yaw), t.Scale,
                t.Volumes.Select((v, j) => new TerrainVolume(SimIds.Of(seed, $"terrain/{i}/volume/{j}"), v)).ToList())).ToList(),
            s.Markers.Select((m, i) => new Marker(
                SimIds.Of(seed, $"marker/{i}"), m.Kind, m.Position, new Yaw(0), m.Diameter, m.Label, VisualRef.OfBuiltin("objective"))).ToList(),
            s.ZoneShapes.Select((shape, i) => new DeploymentZone(SimIds.Of(seed, $"zone/{i}"), shape, i + 1)).ToList());

        var armies = s.Armies.Select((a, i) => new ArmyInPlay(participants[i].Id, BuildArmy(seed, i + 1, a))).ToList();

        var info = new GameInfo(
            SimIds.GameId(seed), s.Name, GameStatus.Playing, participants[0].AccountId,
            GrimdarkFutureRules.RulesId, CoreProperties.ContentHash, s.Shape, s.Enforcement,
            WaitsBetweenSteps: false, teams, participants);

        return new CompiledScenario(s, info, new Board(snapshot, armies), CoreProperties,
            teams.Select(t => t.Id).ToList(), participants.Where(p => p.TeamId is not null).Select(p => p.Id).ToList(),
            participants.Where(p => p.TeamId is not null).ToDictionary(p => p.Id, p => p.TeamId!.Value),
            snapshot, recorder.Id);
    }

    private static ArmySnapshot BuildArmy(ulong seed, int team, ScenarioArmy a)
    {
        var units = new List<Unit>();
        for (var u = 1; u <= a.Units; u++)
        {
            var modelRules = a.ModelRules.Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList();
            var weapons = a.Weapons.Select((w, k) => new Weapon(SimIds.Of(seed, $"army/{team}/unit/{u}/weapon/{k}"), w.Name,
                w.Stats, w.Rules.Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList())).ToList();
            var models = Enumerable.Range(1, a.ModelsPerUnit).Select(m => new ModelSlot(
                SimIds.Of(seed, $"army/{team}/unit/{u}/model/{m}"), m, $"{a.Name} {u}.{m}",
                new Dictionary<string, string>(), modelRules, Base.Circle(1.26), 1.5, null, weapons)).ToList();
            units.Add(new Unit(SimIds.Of(seed, $"army/{team}/unit/{u}"), u, $"{a.Name} unit {u}", a.UnitStats,
                a.UnitRules.Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList(), models));
        }
        return new ArmySnapshot(SimIds.Of(seed, $"army/{team}"), a.Name, "grimdark-future", units);
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
    };

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

Expected: every test in `SimIdsTests`, `ChooserTests` and `ScenarioTests` passes. `AnswerScript.Empty` does not exist yet, so add the two lines Task 3 fills in (`public sealed record AnswerScript { public static AnswerScript Empty { get; } = new(); }` in `sim/Answers/AnswerScript.cs`) to compile, and replace the file wholesale in Task 3.

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
- Consumes: `Ask`, `AskOption`, `LegalSet` and its seven members (`Options`, `PieceSelection`, `Allocation`, `Assignment`, `MoveRegion`, `Bid`, `Confirmation`), `MoveContainment`, `AskAnswer` and its eight members (`Chosen`, `SelectedPieces`, `Allocated`, `Assigned`, `Destinations`, `BidPlaced`, `Confirmed`, `Passed`), `ModelRelocation`, `Location` (`OnTable`, `OnShelf`, `Inside`), `Projection`, `ModelInPlay`, `Board` (Protocol); `PlacedModel`, `ModelBody`, `WorldVolume`, `Placement`, `Point2`, `Point3`, `Yaw`, `TableSize`, `Movement`, `MoveCheck`, `Settling` (Geometry); `TerrainPlacement.WorldVolumes()` (Content); `Chooser` (Task 1).
- Produces: `sealed class PolicyView(Projection projection, Board board)` with `TableSize Table`, `IReadOnlyList<WorldVolume> Volumes`, `IReadOnlyList<WorldVolume> VolumesById(IReadOnlyList<Guid> ids)`, `Location LocationOf(Guid modelId)`, `PlacedModel? PlacedOf(Guid modelId)`, `ModelBody BodyOf(Guid modelId)`, `double SurfaceAt(Point2 point)`; `sealed record PolicyOptions(double PassProbability = 0, double EmbarkProbability = 0.25, int MoveSamples = 24)`; `sealed class NoLegalAnswerException(Ask ask) : Exception`; `sealed class AnswerPolicy(Chooser chooser, PolicyOptions? options = null)` with `AskAnswer Answer(Ask ask, PolicyView view)` and `static IReadOnlyList<string> ShapeNames`.

Spec: spec → *Two ways to answer an Ask* — "A seeded policy. Any Ask, answered from its legal set with a seeded random choice"; "It chooses legally, not well"; "the legal set is the whole of its input". Spec → *Deliberately not decided*: "Whether the policy is uniform over the legal set or weighted, and whether it prefers to act over passing" — this plan chooses uniform, and prefers to act (`PassProbability` defaults to `0`), which is question 2.

**What `PolicyView` deliberately does not expose**, and why it exists at all: a `MoveRegion` names model ids, an allowance and volume ids, and says nothing about where the models stand — so an answer to it is impossible without reading the projection's positions. `PolicyView` is that reading and nothing more: positions, base bodies, the table size, the world volumes the legal set's own ids name, and the settled surface height. It exposes no `PropertySet`, no `IGameRules`, no `UnitInPlay`, no stat, no wound, no condition and no team, so no member of it can tell the policy what a good move is.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

public class AnswerPolicyTests
{
    static readonly Guid A = SimIds.Of(1, "a"), B = SimIds.Of(1, "b"), C = SimIds.Of(1, "c");
    static Ask With(LegalSet legal, bool allowPass = false, bool sealedAsk = false) =>
        new(SimIds.Of(1, "ask"), "q", "p", [A], legal, allowPass, sealedAsk, null, null);
    static AnswerPolicy Policy(ulong seed = 1, PolicyOptions? o = null) => new(new Chooser(seed), o);
    static PolicyView EmptyView() => new(Projection.Initial(new Board(Bare(), []), Info()), new Board(Bare(), []));
    static TableState Bare() => new(SimIds.Of(1, "t"), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
    static GameInfo Info() => new(SimIds.Of(1, "g"), "g", GameStatus.Playing, A, "grimdark-future", "sha256:x",
        SchedulerShape.AlternatingActivation, Enforcement.Strict, false, [new Team(A, 1)], []);

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
    public void Bid_stays_within_the_cap_and_confirmation_confirms()
    {
        Assert.InRange(Assert.IsType<BidPlaced>(Policy().Answer(With(new Bid(3)), EmptyView())).Tokens, 0, 3);
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
        Assert.Equal(["allocation", "assignment", "bid", "confirmation", "move_region", "options", "pieces"], AnswerPolicy.ShapeNames.Order());
        var policy = Policy();
        LegalSet[] all =
        [
            new Options([new AskOption("x", "X", [])]),
            new PieceSelection([A], 1, 1),
            new Allocation([A], 1),
            new Assignment([new AskOption("g", "", [])], [A], 1),
            new Bid(1),
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
    static readonly Guid Model = SimIds.Of(2, "model"), Unit = SimIds.Of(2, "unit"), Part = SimIds.Of(2, "p"), Team = SimIds.Of(2, "team");

    static (PolicyView View, Guid ModelId) Standing(Point3 at)
    {
        var slot = new ModelSlot(Model, 1, "m", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, []);
        var unit = new Unit(Unit, 1, "u", new Dictionary<string, string>(), [], [slot]);
        var table = new TableState(SimIds.Of(2, "t"), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
        var info = new GameInfo(SimIds.Of(2, "g"), "g", GameStatus.Playing, Part, "grimdark-future", "sha256:x",
            SchedulerShape.AlternatingActivation, Enforcement.Strict, false, [new Team(Team, 1)],
            [new Participant(Part, SimIds.Of(2, "acc"), "p", Team, null)]);
        var board = new Board(table, [new ArmyInPlay(Part, new ArmySnapshot(SimIds.Of(2, "army"), "a", "grimdark-future", [unit]))]);
        var projection = Projection.Initial(board, info);
        var applier = new EventApplier(board, info);
        applier.Apply(new GameEvent(1, null, new ModelsMoved([new ModelRelocation(Model, new OnShelf(), new OnTable(new Placement(at, new Yaw(0))))]), null));
        return (new PolicyView(applier.Projection, board), Model);
    }

    [Fact]
    public void A_destination_is_within_the_allowance_and_on_the_table()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var ask = new Ask(SimIds.Of(2, "ask"), "move", "Move", [Team], new MoveRegion([model], 6, [], [], null, null), false, false, null, SimIds.Of(2, "frame"));
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
        var wall = new WorldVolume(SimIds.Of(2, "wall"),
            new Volume(VolumeKind.Solid, Footprint.Rect(2, 40), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(14, 24, 0), new Yaw(0), 1));
        var walled = new PolicyView(view.Projection, view.Board, [wall]);
        var ask = new Ask(SimIds.Of(2, "ask"), "move", "Move", [Team],
            new MoveRegion([model], 12, [wall.Id], [], null, null), false, false, null, SimIds.Of(2, "frame"));
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
        var container = SimIds.Of(2, "container");
        var ask = new Ask(SimIds.Of(2, "ask"), "move", "Move", [Team],
            new MoveRegion([model], 6, [], [], null, new MoveContainment([container], null, null)), false, false, null, SimIds.Of(2, "frame"));
        var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(4), new PolicyOptions(EmbarkProbability: 1)).Answer(ask, view));
        Assert.Equal(new Inside(container), answer.Moves[0].To);
    }
}
```

`PolicyView` therefore takes an optional volume override — `PolicyView(Projection, Board, IReadOnlyList<WorldVolume>? volumes = null)` — because a test builds volumes without a `TerrainPlacement`, and `SocketDriver` gets its board from the wire. Step 3's signature includes it.
- [ ] **Step 3: Run; expect failure, then write `PolicyView.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "AnswerPolicyTests|MoveRegionPolicyTests"`
Expected: `CS0246: ... 'AnswerPolicy' could not be found`.

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

/// <summary>
/// The only state an answer may read, and it is all geometry: where a model stands, how big its base is,
/// how big the table is, and which world volumes the legal set's own ids name. There is no PropertySet
/// here, no IGameRules, no UnitInPlay, no stat, wound, condition or team — so nothing in this class can
/// tell the policy what a good move is (spec → Two ways to answer an Ask).
/// </summary>
public sealed class PolicyView(Projection projection, Board board, IReadOnlyList<WorldVolume>? volumes = null)
{
    public Projection Projection { get; } = projection;
    public Board Board { get; } = board;
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
/// An Ask in, an answer out, from the legal set's shape alone. It is not an opponent and must not become
/// one: every choice here is uniform over what the server said was legal (spec → Two ways to answer an Ask).
/// Deterministic for a seed, given the same run: one chooser stream advanced in Ask order.
/// </summary>
public sealed class AnswerPolicy(Chooser chooser, PolicyOptions? options = null)
{
    private readonly PolicyOptions _o = options ?? new PolicyOptions();

    /// <summary>Every LegalSet member the Protocol declares. A shape missing here is a compile-time hole, not a runtime one.</summary>
    public static IReadOnlyList<string> ShapeNames { get; } =
        ["options", "pieces", "allocation", "assignment", "move_region", "bid", "confirmation"];

    public AskAnswer Answer(Ask ask, PolicyView view) => ask.Legal switch
    {
        Options o => AnswerOptions(ask, o),
        PieceSelection p => AnswerPieces(ask, p),
        Allocation a => AnswerAllocation(ask, a),
        Assignment a => AnswerAssignment(ask, a),
        MoveRegion m => AnswerMove(ask, m, view),
        Bid b => Pass(ask) ?? new BidPlaced(chooser.Next(b.MaxTokens + 1)),
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
    static readonly Guid A = SimIds.Of(3, "a"), B = SimIds.Of(3, "b");
    static Ask Ask(string question, LegalSet? legal = null, bool allowPass = false) =>
        new(SimIds.Of(3, question), question, "p", [A], legal ?? new Options([new AskOption("x", "X", []), new AskOption("y", "Y", [])]), allowPass, false, null, null);
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

/// <summary>Where an answer comes from. The run loop knows only this.</summary>
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
- Consumes: `IGameRows`, `OwnedArmy`, `ISessionClient`, `SessionServices`, `GameSession`, `Hydrator`, `InMemoryEventLog`, `GameServerOptions`, `Replay` (game server skeleton); `RulesEngineFactory`, `InMemoryPropertiesStore`, `DiceSource`, `IDiceSource` (interpreter plan); `SchedulerFactory` (scheduler plan); `SetConfiguration`, `SocketMessage` (`Welcome`, `BoardMessage`, `EventsMessage`, `CommandMessage`, `Refused`, `PresenceMessage`, `PingMessage`, `PresenceEnded`, `GameInfoMessage`), `GameEvent`, `Command`, `AnswerAsk`, `AskAnswer`, `Projection`, `OpenAsk`, `EventApplier`, `RefusalReason`, `Board`, `GameInfo` (Protocol); `TableState`, `ArmySnapshot` (Content); `CompiledScenario`, `ScenarioCompiler` (Task 1); `PolicyView` (Task 2).
- Produces: `abstract record SubmitResult` with `Accepted(string? Warning)` and `Rejected(RefusalReason Reason, string Detail)`; `sealed record StepResult(IReadOnlyList<GameEvent> Events, IReadOnlyList<OpenAsk> OpenAsks, bool Quiescent)`; `enum DriverMode { Run, StepAndWait }`; `interface IGameDriver : IAsyncDisposable` with `Guid GameId`, `GameInfo Info`, `Board Board`, `IReadOnlyList<Guid> TeamIds`, `Guid ParticipantFor(Guid teamId)`, `DriverMode Mode { get; set; }`, `string Name`, `Task StartAsync(CancellationToken ct)`, `Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct)`, `Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)`, `Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct)`, `Task<Projection> ProjectionAsync(CancellationToken ct)`, `Task<StepResult> StepAsync(CancellationToken ct)`, and the default method `Task<SubmitResult> AnswerAsync(Guid teamId, Guid askId, AskAnswer answer, CancellationToken ct)`; `sealed class ScenarioRows(CompiledScenario scenario) : IGameRows`; `sealed class RecorderClient(Guid participantId) : ISessionClient` with `IReadOnlyList<SocketMessage> Frames`, `IReadOnlyList<SocketMessage> TakeFrames()`, `bool Closed`; `sealed class InProcessDriver : IGameDriver` with `static Task<InProcessDriver> StartAsync(CompiledScenario, CancellationToken)`, `GameSession Session`, `Task<Projection> ServerProjectionAsync(CancellationToken)`, `IReadOnlyList<GameEvent> Log`.

Spec: spec → *Two drivers behind one interface* — "In process. The session, the engine and an in-memory log, constructed directly. No socket, no Postgres, no Docker. This is the default because a full game must cost milliseconds"; "A driver interface with one implementation would not have been worth the abstraction." `architecture.md` → Contracts to freeze ("nobody instantiates a game session: a client opens a socket naming a game, and if no session for that game is in memory the server hydrates it from the log") — which is why the in-process driver goes through `Hydrator` and `GameSession.ConnectAsync` rather than building a `GameState` by hand: the two drivers then differ in transport and nothing else.

**The driver's projection is its own.** Every driver keeps an `EventApplier` over the scenario's board and info, fed by the events it drains from the **recorder participant's** stream — a participant with no team, which is what a spectator is. That is the same applier the client uses, so the run loop, the invariants and the recording all read a projection built the way a client builds one, and the socket driver needs no second mechanism. `InProcessDriver.ServerProjectionAsync` additionally exposes the session's own projection, and Task 7's first invariant compares them.

**Quiescence.** `StepAsync` returns when the driver believes the server has rested. In process that is exact: `GameSession.RunAsync` puts a marker at the back of the mailbox, so when it returns every queued turn has run. Over the socket it is a quiet window — no frame for `SocketDriver.QuietWindow` (250 ms by default). The asymmetry is real and is named on the interface, because a fuzz run in process must cost milliseconds and a socket run cannot.

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
        var result = await driver.AnswerAsync(ask.Teams.Single(), ask.Id, new SelectedPieces([SimIds.Of(1, "nope")]), TestContext.Current.CancellationToken);
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

/// <summary>What one step produced. Quiescent means the driver believes the server has rested.</summary>
public sealed record StepResult(IReadOnlyList<GameEvent> Events, IReadOnlyList<OpenAsk> OpenAsks, bool Quiescent);

/// <summary>Run answers every Ask; StepAndWait stops with one open and answers nothing (spec → Interactive debugging is a door).</summary>
public enum DriverMode { Run, StepAndWait }

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
- Consumes: `Projection`, `ModelInPlay`, `UnitInPlay`, `SchedulerPosition`, `OpenAsk`, `Ask`, `LegalSet`, `GameEvent`, `EventPayload` and every member, `EventKinds`, `Grant`, `RollRecord`, `Die`, `Location` (`OnTable`, `OnShelf`, `Inside`), `Board`, `GameInfo`, `EventApplier`, `AskAnswer` members (Protocol); `TableState`, `Marker`, `MarkerKind`, `TerrainPlacement`, `TerrainVolume`, `DeploymentZone`, `VisualRef` (Content); `Volume`, `VolumeKind`, `Footprint`, `FootprintKind`, `TerrainDeclarations`, `Base`, `BaseShape` (Geometry); `CompiledScenario` (Task 1).
- Produces: the record schema below (`Recording`, `RecordingMeta`, `RecordedTeam`, `RecordedBoard`, `RecordedTerrain`, `RecordedVolume`, `RecordedMarker`, `RecordedZone`, `RecordedModel`, `RecordedFrame`, `RecordedModelState`, `RecordedUnitState`, `RecordedMarkerState`, `RecordedAsk`, `RecordedAskOption`, `RecordedRoll`, `RecordedModifier`); `static class RecordingJson` with `const int SchemaVersion = 1`, `JsonSerializerOptions Options`, `Task WriteAsync(Recording, string path, CancellationToken)`, `Task<Recording> ReadAsync(string path, CancellationToken)`, `string Serialize(Recording)`, `Recording Deserialize(string)`; `static class LogLines` with `string Of(GameEvent, Projection after, IReadOnlyDictionary<Guid,int> teamOrdinals)`; `sealed class FrameProjector(CompiledScenario scenario)` with `RecordedBoard Board`, `RecordedFrame Add(GameEvent)`, `IReadOnlyList<RecordedFrame> Frames`, `Projection Projection`, `Recording Build(RecordingMeta meta)`.

Spec: spec → *The recording is the artifact* — "A run writes one file: the board, then one frame per event, each frame carrying the model positions, wounds, conditions and the log line that event produced. It is a **recording of a projection, not a log of events**"; "Given frames, the viewer interpolates nothing and decides nothing; it draws what it is handed"; "The recording is derived and disposable. It is not a save format, it is not replayed into the engine, and the event log remains the only thing undo and hydration read." Spec → *Interactive debugging is a door* — "the recording carries the open Ask at every frame". Spec → *Still open*: "Whether a recording should carry the dice so a viewer can show a roll, or only its result" — this plan carries the dice (question 3).

**Every frame carries whole state, not a delta.** Nothing in a viewer then has to accumulate, which is the point of handing it frames at all; and a scrub to frame 300 needs frame 300 only. A 500-frame game of 40 models is roughly 1.5 MB of JSON, which is a file a browser opens.

- [ ] **Step 1: Write the failing tests**

```csharp
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
        Assert.Contains("moved", frame.Line);
    }

    [Fact]
    public void An_open_ask_appears_on_every_frame_until_it_closes()
    {
        var c = Scenario();
        var projector = new FrameProjector(c);
        var ask = new Ask(SimIds.Of(1, "ask"), AskQuestions.Activate, "Activate", [c.TeamIds[0]],
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
        var roll = new RollRecord(SimIds.Of(1, "roll"), "hit", 4, [new RollModifier("Good Shot", 1)],
            [new Die(3, 4, true), new Die(1, 2, false)]);
        var frame = projector.Add(new GameEvent(1, null,
            new StepCompleted(SimIds.Of(1, "frame"), "hit-roll", null, [roll], []), null));
        var recorded = Assert.Single(frame.Rolls);
        Assert.Equal([3, 1], recorded.Raw);
        Assert.Equal([4, 2], recorded.Modified);
        Assert.Equal([true, false], recorded.Success);
        Assert.Equal(1, recorded.Net);
        Assert.Equal(4, recorded.Threshold);
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
        Assert.DoesNotContain("roundCap", json);          // null, so omitted
        Assert.DoesNotContain("activeTeamId", json);      // no active team yet, so omitted
    }
}
```
- [ ] **Step 2: Run; expect failure, then write `Recording.cs`** — the schema, in full

Run: `dotnet test tests/Tabletop.Sim.Tests --filter RecordingTests`
Expected: `CS0246: ... 'FrameProjector' could not be found`.

```csharp
namespace Tabletop.Sim.Recording;

// A RECORDING OF A PROJECTION, NOT A LOG OF EVENTS (spec → The recording is the artifact).
// Every frame carries whole state, so a viewer interpolates nothing and decides nothing: it draws what it
// is handed. The recording is derived and disposable — it is not a save format, it is never replayed into
// the engine, and the event log in Postgres remains the only thing undo and hydration read. A reader who
// wants what happened rather than what it looked like wants game.game_event, not this.

/// <summary>One run. `schemaVersion` is RecordingJson.SchemaVersion; a reader that does not know it should refuse the file.</summary>
public sealed record Recording(int Version, RecordingMeta Meta, RecordedBoard Board, IReadOnlyList<RecordedFrame> Frames);

/// <param name="Outcome">`decided`, `round_limit`, `stuck` or `failed`.</param>
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

public sealed record RecordedZone(Guid Id, string Shape, int TeamOrdinal);

/// <param name="BaseShape">`circle`, `oval` or `square`.</param>
/// <param name="BaseA">A circle's diameter, an oval's length, a square's side — all in inches.</param>
/// <param name="BaseB">An oval's width; equal to BaseA otherwise.</param>
public sealed record RecordedModel(
    Guid Id, Guid UnitId, string UnitName, string Name, int Ordinal,
    Guid TeamId, int TeamOrdinal, string BaseShape, double BaseA, double BaseB, double Height);

/// <param name="Index">Zero-based position in Frames; the timeline's coordinate.</param>
/// <param name="Seq">The event log sequence this frame is the state after.</param>
/// <param name="Kind">The event kind, snake_case, exactly as EventKinds spells it.</param>
/// <param name="Line">One human line, generated by LogLines and rules-free.</param>
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
    IReadOnlyList<RecordedRoll> Rolls);

/// <param name="Where">`table`, `shelf` or `inside` — the three places a model can be (game flow → The shelf, Inside a container).</param>
/// <param name="ContainerId">Set when Where is `inside`; a model inside a container has no position.</param>
public sealed record RecordedModelState(
    Guid Id, string Where, double? X, double? Y, double? Z, double? Yaw, Guid? ContainerId,
    int Wounds, int SpellTokens, IReadOnlyList<string> Conditions);

public sealed record RecordedUnitState(
    Guid Id, IReadOnlyList<string> Actions, IReadOnlyList<string> Conditions,
    IReadOnlyList<string> Grants, int WoundsThisActivation);

public sealed record RecordedMarkerState(Guid Id, double X, double Y, double Z, double Yaw);

/// <param name="LegalKind">The legal set's wire kind: options, pieces, allocation, assignment, move_region, bid, confirmation.</param>
/// <param name="Answered">The teams that have already answered — a sealed Ask's answers stay hidden until it closes.</param>
/// <param name="Options">Present for an options or assignment set: the choice ids, their labels and the pieces they name.</param>
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
- [ ] **Step 4: Write `LogLines.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Sim.Recording;

/// <summary>
/// One line per event, from the event and the projection after it. Rules-free by construction: every name
/// it prints — a sequence, a step, an action, a condition, a question — is a string the event already
/// carries, and nothing here decides what any of them mean.
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

    private static string Dice(IReadOnlyList<RollRecord> rolls) =>
        rolls.Count == 0 ? "" : ": " + string.Join("; ", rolls.Select(r =>
            $"{r.Roll} {string.Join(",", r.Dice.Select(d => d.Success ? $"{d.Modified}!" : $"{d.Modified}"))}"));

    private static string Answer(AskAnswer? answer) => answer switch
    {
        null => "nothing",
        Chosen c => c.OptionId,
        SelectedPieces s => $"{s.PieceIds.Count} piece(s)",
        Allocated a => $"{a.Counts.Values.Sum()} allocated",
        Assigned a => $"{a.Targets.Count} assignment(s)",
        Destinations d => $"{d.Moves.Count} destination(s)",
        BidPlaced b => $"a bid of {b.Tokens}",
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
            p.Markers.Values.Select(m => new RecordedMarkerState(m.Id, m.Position.X, m.Position.Y, m.Position.Z, m.Yaw.Degrees)).ToList(),
            p.OpenAsks.Values.Select(AskOf).ToList(),
            rolls);
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
            Bid b => ("bid", [], [], (double)b.MaxTokens),
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
            table.Zones.Select(z => new RecordedZone(z.Id, z.Shape, z.TeamOrdinal)).ToList(),
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

The markers' initial positions live on `RecordedMarkerState` frame by frame like a model's, and `RecordedMarker` carries only what does not change — the kind, the diameter and the label. A marker placed during play appears in the first frame after its `marker_placed`.

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
- Consumes: `IGameDriver`, `SubmitResult`, `StepResult`, `DriverMode` (Task 4); `IAnswerSource`, `PolicyView`, `UnscriptedAskException`, `NoLegalAnswerException`, `AnsweredAsk` (Tasks 2–3); `FrameProjector`, `Recording`, `RecordingMeta`, `RecordedTeam` (Task 5); `Projection`, `OpenAsk`, `Ask`, `LegalSet`, `AskAnswer`, `AskQuestions`, `MoveModels`, `ModelRelocation`, `Location`, `OnTable`, `OnShelf`, `GameEvent`, `EventKinds`, `Command` (Protocol); `Placement`, `Point2`, `Point3`, `Yaw`, `Polygon`, `PlacedModel`, `ZoneStyles`, `Settling` (Geometry); `CompiledScenario`, `Scenario`, `Expectation` (Task 1).
- Produces: `enum StuckReason { NoAskNoEvents, AskReopenedUnchanged, SubmitRefused, NoLegalAnswer }`; `abstract record RunOutcome` with `Decided(Guid WinningTeamId)`, `RoundLimit(int Rounds)`, `Stuck(StuckReason Reason, string Detail)`, `Failed(string Detail)`, `Paused(Guid AskId, string Question)`, and `string Kind` (`decided`, `round_limit`, `stuck`, `failed`, `paused`); `sealed record AskFingerprint(string Question, string Teams, string Legal, bool AllowPass, string? WindowKind)` with `static AskFingerprint Of(Ask)`; `static class HandPlacement` with `IReadOnlyList<ModelRelocation> IntoZone(Projection, CompiledScenario, Guid teamId, Guid unitId, Chooser)`; `sealed record RunSettings(int MaxSteps = 20_000, int FrameDumpOnFailure = 12, bool CheckInvariants = true)`; `sealed record RunReport(string Scenario, ulong Seed, string Driver, RunOutcome Outcome, int Rounds, long LastSeq, int Frames, int AsksAnswered, double DurationMs, IReadOnlyList<string> Failures, string? RecordingPath)` with `bool Passed`; `sealed class RunLoop(IGameDriver driver, CompiledScenario scenario, IAnswerSource answers, Chooser chooser, RunSettings? settings = null)` with `Task<RunReport> RunAsync(CancellationToken ct)`, `FrameProjector Projector`, `Recording BuildRecording(RunReport)`, `IReadOnlyList<string> LastFrames(int n)`; `static class Expectations` with `IReadOnlyList<string> Check(IReadOnlyList<Expectation>, Projection, IReadOnlyList<GameEvent>, RunOutcome, CompiledScenario)`.

Spec: spec → *What a full game means* — the three-row table, "a run that reaches none of them is a bug the simulator exists to find"; "Stuck is the interesting one. A scheduler or interpreter that cannot make progress is a hang in production and a failed assertion here." Module prompt → "Detect stuck precisely: no open Ask and no events emitted, or an Ask answered that reopens unchanged. A stuck run is a failure with the last frames dumped."

**Termination, exactly.**

| End | Condition as this loop computes it |
| --- | --- |
| Decided | Exactly one team has a model whose `Location is OnTable`, and at least one other team has none. A team's models are its participants' armies' models. |
| Round limit | `Projection.Scheduler.Round > Scenario.RoundCap`. |
| Stuck: `NoAskNoEvents` | A step produced no events and no Ask is open. |
| Stuck: `AskReopenedUnchanged` | The Ask answered at step *n* is closed and an Ask with the same `AskFingerprint` opens, with no event other than the three Ask events in between. |
| Stuck: `SubmitRefused` | An answer the driver refuses. Under `strict` that is a real bug in the policy or the engine, and the run stops rather than looping. |
| Stuck: `NoLegalAnswer` | `NoLegalAnswerException`: a legal set with no answer and no pass. |
| Failed | An `UnscriptedAskException`, a failed expectation, or a failed invariant. |

**Why the loop places models by hand.** Deployment's Ask is `deploy` over a `PieceSelection` of units; the scheduler spec's phase ends "when the last model of that unit has a placement", and moving models by hand is an ordinary `MoveModels` command, not an Ask (scheduler → The turn is an Ask: "moving models by hand never ends one"). So after a `deploy` answer the loop puts that unit's models on the table itself, inside the team's own deployment zone, using `ZoneStyles.Resolve` — geometry, not a rule. Nothing checks whether the placement is *good*; placement is free under every enforcement setting (`architecture.md` → Rules enforcement: "free placement, with base overlap, terrain intersection, and broken coherency flagged rather than prevented").

- [ ] **Step 1: Write the failing tests**

```csharp
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

    [Fact]
    public async Task The_same_seed_produces_the_same_run()
    {
        var one = await Run("skirmish", 12);
        var two = await Run("skirmish", 12);
        Assert.Equal(one.Outcome, two.Outcome);
        Assert.Equal(one.LastSeq, two.LastSeq);
        Assert.Equal(one.Frames, two.Frames);
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
        Assert.True(report.Rounds <= 4, $"the cap of 3 was exceeded: {report.Rounds}");
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

    [Fact]
    public void An_ask_fingerprint_ignores_the_id_and_notices_the_legal_set()
    {
        var team = SimIds.Of(1, "t");
        Ask ask(Guid id, int options) => new(id, AskQuestions.Activate, "p", [team],
            new Options(Enumerable.Range(0, options).Select(i => new AskOption($"o{i}", "", [])).ToList()), true, false, null, null);
        Assert.Equal(AskFingerprint.Of(ask(SimIds.Of(1, "a"), 2)), AskFingerprint.Of(ask(SimIds.Of(1, "b"), 2)));
        Assert.NotEqual(AskFingerprint.Of(ask(SimIds.Of(1, "a"), 2)), AskFingerprint.Of(ask(SimIds.Of(1, "a"), 3)));
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

/// <summary>The three ends of spec → What a full game means, plus the fourth a scenario or an invariant can produce.</summary>
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
        Bid b => $"bid:{b.MaxTokens}",
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
/// does that. It reads no rule: the zone comes from the deployment_zone rows and Tabletop.Geometry resolves
/// the shape, and nothing here judges whether a placement is good — placement is free under every
/// enforcement setting (architecture.md → Rules enforcement).
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

/// <param name="MaxSteps">A hard stop, so a bug in this loop cannot spin forever.</param>
public sealed record RunSettings(int MaxSteps = 20_000, int FrameDumpOnFailure = 12, bool CheckInvariants = true);

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
/// Drive a game to one of the three ends spec → What a full game means lists, and fail loudly on the fourth
/// thing that can happen: no progress at all. Every answer comes from the answer source; nothing here reads
/// a rule.
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
        catch (NoLegalAnswerException ex) { outcome = new RunOutcome.Stuck(StuckReason.NoLegalAnswer, ex.Message); }

        var projection = await driver.ProjectionAsync(ct);
        failures.AddRange(Expectations.Check(scenario.Scenario.Expectations, projection, _log, outcome, scenario));
        if (outcome is RunOutcome.Stuck or RunOutcome.Failed)
            failures.Add($"{outcome.Kind}: {outcome}\nlast frames:\n{string.Join("\n", LastFrames(_settings.FrameDumpOnFailure))}");

        return new RunReport(scenario.Scenario.Name, scenario.Scenario.Seed, driver.Name, outcome,
            projection.Scheduler.Round, projection.LastSeq, Projector.Frames.Count,
            answers.History.Count, clock.Elapsed.TotalMilliseconds, failures, null);
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
        var view = new PolicyView(projection, driver.Board);
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

    /// <summary>An Ask answered and reopening unchanged, with nothing but the three Ask events between.</summary>
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
        scenario.Info.SchedulerShape.ToString(), scenario.Info.Enforcement.ToString().ToLowerInvariant(),
        scenario.Scenario.RoundCap,
        scenario.Info.Teams.Select(t => new RecordedTeam(t.Id, t.Ordinal,
            scenario.Scenario.Armies.ElementAtOrDefault(t.Ordinal - 1)?.Name ?? $"Team {t.Ordinal}",
            scenario.Info.Participants.Where(p => p.TeamId == t.Id).Select(p => p.Name).ToList())).ToList(),
        report.Outcome.Kind,
        report.Outcome switch { RunOutcome.Stuck s => s.Detail, RunOutcome.Failed f => f.Detail, _ => null },
        report.Outcome is RunOutcome.Decided d ? d.WinningTeamId : null,
        report.Rounds, report.LastSeq, DateTimeOffset.UtcNow, report.DurationMs));
}
```

`SchedulerShape.ToString()` gives `AlternatingActivation`; the recording wants the wire spelling, so write `WireJson.Serialize(scenario.Info.SchedulerShape).Trim('"')` — the one place `sim/` borrows the Protocol's own spelling of an enum, so a viewer sees `alternating-activation` and not two conventions.

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
- Undoing to an arbitrary sequence and redoing reproduces the same dice, because the seed is game state.
- Every Ask is answered by a team that Ask addressed.
- No model is in two places, and no model on the table is outside the slab.
- The round only advances through the boundary Ask, per the ruling of 2026-09-06.

**Each one, as this task computes it.**

1. **Replay.** `Replay.To(board, info, log, log[^1].Seq)` against the driver's own projection, through `ProjectionComparer`. In process the same comparison also runs against `InProcessDriver.ServerProjectionAsync`, which is the stronger form: the server's live projection, the driver's incrementally-applied one, and a cold replay all three agree.
2. **Undo and the dice.** `Undone(N)` shortens the effective log, and the dice are counter-based on `(GameState.RngSeed, cursor)` (interpreter plan Task 5), so the *k*th die of the game is a pure function of the seed and *k*. The check is therefore over the log alone and needs no second run: `EventApplier.EffectiveLog` after an appended `Undone(N)` must contain the same dice, in the same order, at the same cursor positions as the original log's first *N* sequences. Doing it for every `N` at which a `step_completed` carrying dice appears covers "an arbitrary sequence" exhaustively rather than by sampling. A *redo* that re-rolls is then guaranteed by the same arithmetic, and is checked once for real in `DriverAgreementTests` (Task 8) by submitting `Undo` to the driver and replaying the recorded answers.
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
/// The invariants that hold for any game, whatever the ruleset (spec → What it asserts). Nothing here reads a
/// rule: each one is a statement about the log, the projection and the slab.
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
    /// The dice are counter-based on (RngSeed, cursor), so die k is a pure function of the seed and k, and
    /// Undone(N) shortens the effective log rather than rewriting it. Checked at every sequence that carries
    /// dice, which is "an arbitrary sequence" exhaustively.
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
- Produces: `interface IWebSocketConnector { Task<WebSocket> ConnectAsync(CancellationToken ct); }`; `sealed class ClientWebSocketConnector(Uri uri) : IWebSocketConnector`; `sealed record SocketCredentials(IReadOnlyList<string> Jwts)` with `static SocketCredentials FromEnvironment()`, `Guid AccountOf(int index)` (the `account_id` claim, read without verifying — the server verifies), `string For(int index)`; `sealed class SocketRowSeeder(NpgsqlDataSource db, IGameRows rows)` with `Task SeedAsync(CompiledScenario, SocketCredentials, CancellationToken)` and `Task DeleteAsync(Guid gameId, CancellationToken)`, plus `sealed class PropertiesMissingException(string hash)`; `sealed class SocketDriver : IGameDriver` with `static Task<SocketDriver> StartAsync(CompiledScenario, IWebSocketConnector, SocketCredentials, NpgsqlDataSource, CancellationToken)`, `TimeSpan QuietWindow { get; init; }`, `Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken)`.

Spec: spec → *Two drivers behind one interface* — "Over the socket. A real WebSocket to a running game server, a real session JWT, real rows and a real event log in Postgres. This exercises the handshake, the wire format, hydration, reconnect and the whole path `architecture.md` calls the only path"; "The same scenario passing in process and failing over the socket localises the fault to transport, serialization or persistence, which is the reason both exist." Module prompt → "Prefer writing rows directly through `IGameRows` plus a test-signed JWT, as the game server's own tests do."

**How the rows are written.** `account`, `game` (at `lobby`, with the scenario's own id and configuration), `team` and `participant` go in as raw SQL, because those four are management's writes and `IGameRows` has no method for them (game server skeleton plan → Interfaces needed 1). Everything after that goes through `IGameRows`: `StartSetupAsync(gameId, snapshot)` writes the whole `table_state` tree and flips to `setup`, and `EndSetupAsync(gameId, snapshot, snapshotsByParticipant)` writes each participant's `army_snapshot` and flips to `playing`. So the scenario's own ids survive — `CreateTeamAsync` would mint its own and break the seed — and the tree is written by the code that owns it.

**One JWT per team, plus one for the recorder.** A session JWT names one account, `FindParticipantAsync(gameId, accountId)` maps an account to one participant, and a newer socket for a participant closes the older one — so N participants need N accounts and N tokens. In tests `TestKeys.Mint` produces them; from the console they come from `TABLETOP_SIM_JWT` as a comma-separated list, in team-ordinal order with the recorder last. This is question 4.

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

    static SocketCredentials Credentials(CompiledScenario c) =>
        new(c.Info.Participants.Select(p => TestKeys.Mint(p.AccountId)).ToList());

    static async Task<RunReport> RunWith(IGameDriver driver, CompiledScenario c, ulong seed, CancellationToken ct)
    {
        var loop = new RunLoop(driver, c, new ComposedAnswers(c.Scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed));
        var report = await loop.RunAsync(ct);
        Assert.All(Invariants.CheckAll(await driver.ProjectionAsync(ct), loop.Log, c), r => Assert.True(r.Held, $"{driver.Name} {r.Name}: {r.Detail}"));
        return report;
    }

    [Fact]
    public async Task The_same_scenario_reaches_the_same_end_in_process_and_over_the_socket()
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

            Assert.Equal("socket", overSocket.Driver);
            Assert.Equal(inProcess.Outcome, overSocket.Outcome);
            Assert.Equal(inProcess.Rounds, overSocket.Rounds);
            Assert.Equal(inProcess.LastSeq, overSocket.LastSeq);
            Assert.Equal(inProcess.Frames, overSocket.Frames);
            Assert.Equal(inProcess.AsksAnswered, overSocket.AsksAnswered);
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
            Assert.Equal(lastSeq, await pg.CountAsync("select coalesce(max(seq), 0) from game.game_event where game_id = $1", c.Info.Id));

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

`RunLoop` therefore exposes its answer source: add `public IAnswerSource Answers => answers;`. `PostgresFixture.CountAsync` is the skeleton plan's (`Task<long> CountAsync(string sql, params object[] parameters)`); if its signature does not take parameters, inline the `game_id` in the SQL string.
- [ ] **Step 2: Run; expect failure, then write `IWebSocketConnector.cs`**

Run: `docker compose up -d && dotnet test tests/Tabletop.Sim.Tests --filter DriverAgreementTests`
Expected: `CS0246: ... 'SocketDriver' could not be found`.

```csharp
using System.Net.WebSockets;
using System.Text.Json;

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

/// <summary>
/// One session JWT per participant, in team-ordinal order with the recorder last. Nothing here verifies a
/// token: the game server does that against Stytch's JWKS. The account id is read out only so the seeder
/// can write a participant row that the handshake will find.
/// </summary>
public sealed record SocketCredentials(IReadOnlyList<string> Jwts)
{
    public const string EnvironmentVariable = "TABLETOP_SIM_JWT";

    public static SocketCredentials FromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException($"{EnvironmentVariable} is not set; the socket driver needs one session JWT per team plus one for the recorder, comma separated");
        return new SocketCredentials(raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    public string For(int index) => index < Jwts.Count
        ? Jwts[index]
        : throw new InvalidOperationException($"{EnvironmentVariable} has {Jwts.Count} tokens; participant {index} needs one of its own");

    public Guid AccountOf(int index)
    {
        var payload = For(index).Split('.')[1];
        var json = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/').PadRight((payload.Length + 3) / 4 * 4, '='));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("account_id", out var claim) && Guid.TryParse(claim.GetString(), out var id)
            ? id
            : throw new InvalidOperationException($"token {index} carries no account_id claim; management adds it via session_custom_claims");
    }
}
```

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

        await Execute(connection, transaction, ct,
            """
            insert into management.game (id, name, status, game_rules, properties_hash, scheduler_shape, enforcement, waits_between_steps, created_by)
            values ($1, $2, 'lobby', $3, $4, $5, $6, $7, $8)
            """,
            info.Id, info.Name, info.GameRules, info.PropertiesHash,
            Wire(info.SchedulerShape), Wire(info.Enforcement), info.WaitsBetweenSteps, accounts[0]);

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
    private readonly CancellationTokenSource _stopping = new();
    private Peer _recorder = null!;

    private SocketDriver(CompiledScenario scenario, IWebSocketConnector connector, SocketCredentials credentials)
    {
        _scenario = scenario;
        _connector = connector;
        _credentials = credentials;
        _applier = new EventApplier(scenario.Board, scenario.Info);
    }

    public TimeSpan QuietWindow { get; init; } = TimeSpan.FromMilliseconds(250);

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
        // Refusals come back on the submitting socket; events come back on every socket, including the recorder's.
        var refusal = await WaitForRefusalAsync(peer, commandId, ct);
        await QuietAsync(ct);
        return refusal is null ? new SubmitResult.Accepted(null) : new SubmitResult.Rejected(refusal.Reason, refusal.Detail);
    }

    public Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct)
    {
        var events = new List<GameEvent>();
        while (_recorder.Inbox.Reader.TryRead(out var frame))
            if (frame is EventsMessage message) events.AddRange(message.Events);
        foreach (var player in _players) while (player.Inbox.Reader.TryRead(out _)) { }
        var ordered = events.OrderBy(e => e.Seq).ToList();
        foreach (var e in ordered) if (e.Seq > _applier.Projection.LastSeq) _applier.Apply(e);
        return Task.FromResult<IReadOnlyList<GameEvent>>(ordered);
    }

    public Task<Projection> ProjectionAsync(CancellationToken ct) => Task.FromResult(_applier.Projection);

    public async Task<StepResult> StepAsync(CancellationToken ct)
    {
        await QuietAsync(ct);
        var events = await DrainEventsAsync(ct);
        return new StepResult(events, _applier.Projection.OpenAsks.Values.ToList(), events.Count == 0);
    }

    /// <summary>Reconnect: the same participant, a second socket. The first is closed by the server with 1000.</summary>
    public async Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken ct)
    {
        var old = _recorder;
        _recorder = await OpenAsync(old.ParticipantId, _scenario.Info.Participants.Count - 1, ct);
        await QuietAsync(ct);
        await DrainEventsAsync(ct);
        return old.Socket.CloseStatus;
    }

    /// <summary>No frame on the recorder's socket for a whole window means the server has rested.</summary>
    private async Task QuietAsync(CancellationToken ct)
    {
        while (true)
        {
            using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
            window.CancelAfter(QuietWindow);
            try { await _recorder.Inbox.Reader.WaitToReadAsync(window.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return; }
            if (!_recorder.Inbox.Reader.TryPeek(out _)) return;
            await Task.Yield();
            if (_recorder.Inbox.Reader.Count > 0) { await DrainEventsAsync(ct); continue; }
        }
    }

    private async Task<Refused?> WaitForRefusalAsync(Peer peer, Guid commandId, CancellationToken ct)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(QuietWindow);
        try
        {
            while (await peer.Inbox.Reader.WaitToReadAsync(window.Token))
                while (peer.Inbox.Reader.TryRead(out var frame))
                    if (frame is Refused refused && refused.MessageId == commandId) return refused;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        return null;
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

`QuietAsync` is the whole of the asymmetry the interface names: it waits for a silence of `QuietWindow` on the recorder's socket, draining anything that arrives in the meantime. A 250 ms window over a loopback socket costs a quarter of a second per step, so a socket run of a full game takes minutes where the in-process run takes milliseconds — which is exactly why the in-process driver is the default and the socket one is a smaller set of scenarios in CI.

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
                new PolicyView(await driver.ProjectionAsync(TestContext.Current.CancellationToken), driver.Board)), TestContext.Current.CancellationToken);
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

Environment for `--driver socket`: `TABLETOP_DB_CONNECTION` (the same variable the servers read), `TABLETOP_SIM_WS` (default `ws://localhost:5200/ws`), `TABLETOP_SIM_JWT` (one session JWT per team plus one for the recorder, comma separated). Exit code `0` when every run passed, `1` otherwise, `2` on a usage error.

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
        and TABLETOP_SIM_JWT — one session JWT per team plus one for the recorder, comma separated.
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
        var credentials = SocketCredentials.FromEnvironment();

        var db = NpgsqlDataSource.Create(connection);
        await new SocketRowSeeder(db, new PostgresGameRows(db)).SeedAsync(compiled, credentials, ct);
        return await SocketDriver.StartAsync(compiled, new ClientWebSocketConnector(new Uri(address)), credentials, db, ct);
    }
}
```

A socket run leaves its rows and its log in Postgres on purpose: the game is then in the database to look at, and `sim/` is a developer tool. `SocketRowSeeder.DeleteAsync` exists for the tests, which must not accumulate games, and for a `--driver socket` run that is repeated with the same seed — the second seed insert fails on the primary key, and the message says to delete the game.

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

### Task 11: `CLAUDE.md` and `docs/architecture.md`

**Files:**
- Modify: `CLAUDE.md` (Layout, Commands)
- Modify: `docs/architecture.md` (Deliverables, Repository layout)

**Interfaces:**
- Consumes: everything Tasks 1–10 produced, as the facts these two documents now record.
- Produces: no code. The whole-tree check in Step 4 is this plan's exit gate.

Spec: spec → *`sim/` is a fourth deliverable* — "The layout rule in `CLAUDE.md` had three shipping directories, `client/`, `server/` and `web/`… `sim/` and `viewer/` are a fourth and fifth. Ruled by the project owner on 2026-09-06"; "They ship to developers, not to players. Neither is in the client's export, neither is referenced by a server, and a release that omits both is complete."

`viewer/` is another plan's, so it gets one line in each document and nothing more.

- [ ] **Step 1: Edit `CLAUDE.md` → Layout, the kinds list**

Before (lines 42–48, as the contracts plan Task 16 leaves them — that task adds `tests/` to the third bullet):

```markdown
Three kinds of top-level directory:

- **Deliverables** — `client/`, `server/`, `web/`. These ship.
- **Shared code** — `libs/`. Consumed by the deliverables, never shipped alone.
- **Repo-level support** — `docs/`, `scripts/`, `tests/`. Neither shipped nor consumed. Dev tooling
  stays out of `client/`, because everything under the Godot project root is project
  content that the editor scans and lists.
```

After:

```markdown
Three kinds of top-level directory:

- **Deliverables** — `client/`, `server/`, `web/`, `sim/`, `viewer/`. These ship. The first three
  ship to players; `sim/` and `viewer/` ship to developers — neither is in the client's export,
  neither is referenced by a server, and a release that omits both is complete (ruled 2026-09-06,
  [simulation](docs/superpowers/specs/2026-09-06-simulation-design.md)).
- **Shared code** — `libs/`. Consumed by the deliverables, never shipped alone.
- **Repo-level support** — `docs/`, `scripts/`, `tests/`. Neither shipped nor consumed. Dev tooling
  stays out of `client/`, because everything under the Godot project root is project
  content that the editor scans and lists.
```

- [ ] **Step 2: Edit `CLAUDE.md` → Layout, the directory list and the closing note**

Before (lines 57–59 and 70–71):

```markdown
- `web/` — Svelte SPA. Owns *preparation*: armies, assets including STL conversion in the
  browser, the account, and login. Does not exist yet.
- `libs/` — `Tabletop.Geometry`, `Tabletop.Protocol`, `Tabletop.Content`. Empty.
```

After — insert two bullets after `web/`:

```markdown
- `web/` — Svelte SPA. Owns *preparation*: armies, assets including STL conversion in the
  browser, the account, and login. Does not exist yet.
- `sim/` — `Tabletop.Sim`, a library plus a console entry point that plays a whole game with no
  Godot client: a scenario, a seeded answer policy over the server's legal sets, two drivers
  (in process and over the socket), the invariant checks, and one recording file per run.
  Contains no rule: it reads legal sets and asserts invariants.
- `viewer/` — a static developer web app that draws a recording top-down. Does not exist yet.
- `libs/` — `Tabletop.Geometry`, `Tabletop.Protocol`, `Tabletop.Content`. Empty.
```

Before (the closing note, as the contracts plan leaves it):

```markdown
`server/` and `web/` are empty on purpose. Each module is designed
before it is built.
```

After:

```markdown
`server/`, `web/` and `viewer/` are empty or absent on purpose. Each module is designed
before it is built.
```

- [ ] **Step 3: Edit `CLAUDE.md` → Commands, and `docs/architecture.md`**

In **Commands**, add after the `dotnet test` line:

```bash
dotnet run --project sim -- --list                 # the scenarios
dotnet run --project sim -- skirmish --seeds 1..20 # twenty games, recordings in ./recordings
```

`docs/architecture.md` → Deliverables, before:

```markdown
Four things ship, and they have different target platforms:

| Deliverable | Directory | Platform |
| --- | --- | --- |
| Game client | `client/` | Windows (Godot export) |
| Game server | `server/game/` | Linux (container) |
| Management API | `server/management/` | Linux (container) |
| Web app | `web/` | Static (Svelte SPA) |
```

After:

```markdown
Six things ship. Four go to players; two go to developers.

| Deliverable | Directory | Platform | Audience |
| --- | --- | --- | --- |
| Game client | `client/` | Windows (Godot export) | Players |
| Game server | `server/game/` | Linux (container) | Players |
| Management API | `server/management/` | Linux (container) | Players |
| Web app | `web/` | Static (Svelte SPA) | Players |
| Simulator | `sim/` | .NET console + library | Developers |
| Developer viewer | `viewer/` | Static page | Developers |

**Ruled 2026-09-06** ([simulation](superpowers/specs/2026-09-06-simulation-design.md)): `sim/` and
`viewer/` are deliverables rather than tests, because a simulator that only xUnit can start is not
a thing you can point at a problem. Neither is in the client's export, neither is referenced by a
server, and a release that omits both is complete. `sim/` plays a game headlessly and asserts the
invariants that hold for any game; it contains no rule. `viewer/` draws a run's recording top-down
and is separate from `web/`, which owns preparation for players.
```

`docs/architecture.md` → Repository layout, before:

```
client/                  Godot game client (the only Godot dependency)
server/game/             live sessions, rules engine, event log
server/management/       ASP.NET Core CRUD API
web/                     Svelte SPA
libs/                    Tabletop.Geometry, Tabletop.Protocol, Tabletop.Content
docs/  scripts/          repo-level support
```

After:

```
client/                  Godot game client (the only Godot dependency)
server/game/             live sessions, rules engine, event log
server/management/       ASP.NET Core CRUD API
web/                     Svelte SPA
sim/                     headless full-game simulator (developers)
viewer/                  recording viewer, a static page (developers)
libs/                    Tabletop.Geometry, Tabletop.Protocol, Tabletop.Content
docs/  scripts/  tests/  repo-level support
```

And, in the paragraph after that block, before:

```markdown
Top-level directories are deliverables, except `libs/` (consumed by them) and `docs/`/`scripts/`
(neither).
```

After:

```markdown
Top-level directories are deliverables, except `libs/` (consumed by them) and
`docs/`/`scripts/`/`tests/` (neither).
```

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
git add CLAUDE.md docs/architecture.md
git commit -m "Record sim and viewer as developer deliverables, per the 2026-09-06 ruling"
```

---
## Interfaces → Produces

Later plans — the viewer's above all — copy these names verbatim. `IRL<T>` abbreviates `IReadOnlyList<T>`.

### `Tabletop.Sim` (root)

| Type | Members |
| --- | --- |
| `static class SimIds` | `Guid Of(ulong seed, string path)`, `Guid GameId(ulong seed)` — deterministic UUIDv7 |
| `sealed class Chooser(ulong seed)` | `int Next(int bound)`, `double Double()`, `bool Chance(double)`, `T Pick<T>(IRL<T>)`, `IRL<T> Shuffle<T>(IRL<T>)`, `IRL<T> Subset<T>(IRL<T>, int)`, `long Drawn` |
| `sealed record SimOptions(string? Scenario, ulong FromSeed, ulong ToSeed, string Driver, string OutputDirectory, int? RoundCap, bool Recording, bool List)` | `static Parse(string[])`, `static string Usage`, `IEnumerable<ulong> Seeds`, `const InProcess = "in-process"`, `const Socket = "socket"`; `sealed class UsageException` |
| `sealed record Summary(string Driver, int Runs, int Passed, int Failed, double TotalMs, IRL<RunReport> Reports)` | `static Of(string, IRL<RunReport>)`, `string Table()` |
| `static class Program` | `Task<int> Main(string[])`, `Task<int> RunAsync(SimOptions, TextWriter, CancellationToken)` |

### `Tabletop.Sim.Scenarios`

| Type | Members |
| --- | --- |
| `sealed record Scenario(string Name, ulong Seed, SchedulerShape Shape, Enforcement Enforcement, int RoundCap, TableSize Table, IRL<ScenarioArmy> Armies, IRL<ScenarioTerrain> Terrain, IRL<ScenarioMarker> Markers, IRL<string> ZoneShapes, AnswerScript Script, bool ScriptOnly, IRL<Expectation> Expectations)` | `Scenario WithSeed(ulong)` |
| `sealed record ScenarioArmy(string Name, int Units, int ModelsPerUnit, IRL<ScenarioRule> UnitRules, IRL<ScenarioRule> ModelRules, IRL<ScenarioWeapon> Weapons, IRD<string,string> UnitStats)`; `ScenarioRule(int Unit, string Name, int? Parameter)`; `ScenarioWeapon(string Name, IRD<string,string> Stats, IRL<ScenarioRule> Rules)`; `ScenarioTerrain(string Mesh, Point3 Position, double Yaw, double Scale, IRL<Volume> Volumes)`; `ScenarioMarker(MarkerKind Kind, Point3 Position, double Diameter, string? Label)` | |
| `sealed class ScenarioBuilder(string name, ulong seed)` | `Shape`, `Enforcing`, `RoundCap`, `Table`, `Zones`, `Army`, `Terrain`, `Marker`, `Scripted`, `Expect`, `Build` |
| `sealed record CompiledScenario(Scenario, GameInfo Info, Board Board, PropertySet Properties, IRL<Guid> TeamIds, IRL<Guid> ParticipantIds, IRD<Guid,Guid> TeamOfParticipant, TableState Snapshot, Guid RecorderParticipantId)` | `Guid ParticipantFor(Guid teamId)` |
| `static class ScenarioCompiler` | `CompiledScenario Compile(Scenario)`, `PropertySet CoreProperties` |
| `static class ScenarioCatalog` | `IRL<string> Names`, `Scenario Get(string)`, `bool TryGet(string, out Scenario)` — `skirmish`, `terrain`, `player-turn`, `free-for-all`, `stalemate` |
| `abstract record Expectation` | `EventOfKind(string, int)`, `NoEventOfKind(string)`, `ReachedRound(int)`, `TeamWiped(int)`, `ModelsRemaining(int, int)`, `AskAsked(string, int)`, `Outcome(string)` |
| `static class Expectations` | `IRL<string> Check(IRL<Expectation>, Projection, IRL<GameEvent>, RunOutcome, CompiledScenario)`, `int ModelsOnTable(Projection, CompiledScenario, int teamOrdinal)`, `int ModelsOnTable(Projection, CompiledScenario, Guid teamId)` |

### `Tabletop.Sim.Answers`

| Type | Members |
| --- | --- |
| `sealed class PolicyView(Projection, Board, IRL<WorldVolume>? volumes = null)` | `Projection`, `Board`, `TableSize Table`, `IRL<WorldVolume> Volumes`, `VolumesById`, `LocationOf`, `PlacedOf`, `BodyOf`, `SurfaceAt` — geometry only, by design |
| `sealed record PolicyOptions(double PassProbability = 0, double EmbarkProbability = 0.25, int MoveSamples = 24)` | |
| `sealed class AnswerPolicy(Chooser, PolicyOptions?)` | `AskAnswer Answer(Ask, PolicyView)`, `static IRL<string> ShapeNames`; `sealed class NoLegalAnswerException` |
| `sealed record ScriptKey(string Question, int Ordinal)`; `sealed record AnswerScript(IRD<ScriptKey,AskAnswer>)` | `static Empty`, `static ScriptBuilder Build()`, `bool TryGet(string, int, out AskAnswer)`, `int Count` |
| `sealed class ScriptBuilder` | `At(string, int, AskAnswer)`, `Then(string, AskAnswer)`, `Build()` |
| `interface IAnswerSource` | `AskAnswer Answer(Ask, PolicyView)`, `IRL<AnsweredAsk> History`; `sealed record AnsweredAsk(string Question, int Ordinal, Guid AskId, AskAnswer Answer, bool FromScript)` |
| `sealed class ComposedAnswers(AnswerScript, AnswerPolicy?) : IAnswerSource`; `sealed class UnscriptedAskException` | script first, then the policy; a null policy is script-only |

### `Tabletop.Sim.Drivers`

| Type | Members |
| --- | --- |
| `interface IGameDriver : IAsyncDisposable` | `string Name`, `Guid GameId`, `GameInfo Info`, `Board Board`, `IRL<Guid> TeamIds`, `Guid ParticipantFor(Guid)`, `DriverMode Mode`, `Task StartAsync`, `Task<IRL<OpenAsk>> OpenAsksAsync`, `Task<SubmitResult> SubmitAsync(Guid, Command, CancellationToken)`, `Task<IRL<GameEvent>> DrainEventsAsync`, `Task<Projection> ProjectionAsync`, `Task<StepResult> StepAsync`, default `Task<SubmitResult> AnswerAsync(Guid teamId, Guid askId, AskAnswer, CancellationToken)` |
| `abstract record SubmitResult` | `Accepted(string? Warning)`, `Rejected(RefusalReason, string Detail)` |
| `sealed record StepResult(IRL<GameEvent> Events, IRL<OpenAsk> OpenAsks, bool Quiescent)`; `enum DriverMode { Run, StepAndWait }` | |
| `sealed class ScenarioRows(CompiledScenario) : IGameRows` | the six reads; the seven writes throw `NotSupportedException` |
| `sealed class RecorderClient(Guid participantId) : ISessionClient` | `IRL<SocketMessage> Frames`, `IRL<SocketMessage> TakeFrames()`, `bool Closed` |
| `sealed class InProcessDriver : IGameDriver` | `static Task<InProcessDriver> StartAsync(CompiledScenario, CancellationToken)`, `GameSession Session`, `Task<Projection> ServerProjectionAsync(CancellationToken)`, `IRL<GameEvent> Log` |
| `interface IWebSocketConnector`; `sealed class ClientWebSocketConnector(Uri)` | `Task<WebSocket> ConnectAsync(CancellationToken)` |
| `sealed record SocketCredentials(IRL<string> Jwts)` | `const EnvironmentVariable = "TABLETOP_SIM_JWT"`, `static FromEnvironment()`, `string For(int)`, `Guid AccountOf(int)` |
| `sealed class SocketRowSeeder(NpgsqlDataSource, IGameRows)`; `sealed class PropertiesMissingException` | `Task SeedAsync(CompiledScenario, SocketCredentials, CancellationToken)`, `Task DeleteAsync(Guid, CancellationToken)` |
| `sealed class SocketDriver : IGameDriver` | `static Task<SocketDriver> StartAsync(CompiledScenario, IWebSocketConnector, SocketCredentials, NpgsqlDataSource, CancellationToken)`, `TimeSpan QuietWindow`, `Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken)` |

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
| `RecordedMarker(Guid Id, string Kind, double Diameter, string? Label)`; `RecordedZone(Guid Id, string Shape, int TeamOrdinal)` | |
| `RecordedModel(Guid Id, Guid UnitId, string UnitName, string Name, int Ordinal, Guid TeamId, int TeamOrdinal, string BaseShape, double BaseA, double BaseB, double Height)` | |
| `RecordedFrame(int Index, long Seq, string Kind, Guid? Actor, string? Warning, int Round, string? Phase, Guid? ActiveTeamId, string Line, IRL<RecordedModelState> Models, IRL<RecordedUnitState> Units, IRL<RecordedMarkerState> Markers, IRL<RecordedAsk> OpenAsks, IRL<RecordedRoll> Rolls)` | |
| `RecordedModelState(Guid Id, string Where, double? X, double? Y, double? Z, double? Yaw, Guid? ContainerId, int Wounds, int SpellTokens, IRL<string> Conditions)` | `Where` is `table`, `shelf` or `inside` |
| `RecordedUnitState(Guid Id, IRL<string> Actions, IRL<string> Conditions, IRL<string> Grants, int WoundsThisActivation)`; `RecordedMarkerState(Guid Id, double X, double Y, double Z, double Yaw)` | |
| `RecordedAsk(Guid Id, string Question, string Prompt, IRL<Guid> Teams, bool AllowPass, bool Sealed, string? WindowKind, Guid? FrameId, string LegalKind, IRL<Guid> Answered, IRL<RecordedAskOption> Options, IRL<Guid> Pieces, double? Allowance)`; `RecordedAskOption(string Id, string Label, IRL<Guid> Pieces)` | |
| `RecordedRoll(Guid Id, string Roll, int? Threshold, int Net, IRL<int> Raw, IRL<int> Modified, IRL<bool> Success, IRL<RecordedModifier> Modifiers)`; `RecordedModifier(string Source, int Amount)` | |
| `static class RecordingJson` | `const int SchemaVersion = 1`, `JsonSerializerOptions Options`, `WriteAsync`, `ReadAsync`, `Serialize`, `Deserialize` |
| `static class LogLines` | `string Of(GameEvent, Projection after, IRD<Guid,int> teamOrdinals)` |
| `sealed class FrameProjector(CompiledScenario)` | `RecordedBoard Board`, `RecordedFrame Add(GameEvent)`, `IRL<RecordedFrame> Frames`, `Projection Projection`, `Recording Build(RecordingMeta)` |

---
## Spec corrections proposed

1. **`CLAUDE.md` → Conventions**, and **`docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md` → Global Constraints**, the constraint *"Identifiers are `Guid` created with `Guid.CreateVersion7()`; no integer ids."* Add: *"`sim/` is the one exception: a simulator's ids are derived from its run seed by `SimIds.Of(seed, path)` so that a seed reproduces a run byte for byte, and the game id in particular is what `GameState.RngSeed` hashes — a random game id would mean random dice. The layout is still a valid version-7 UUID, and nothing outside `sim/` derives an id."* Reason: reproducibility is the whole point of a seeded soak run, and `Guid.CreateVersion7()` cannot give it.
2. **`docs/superpowers/specs/2026-09-06-simulation-design.md` → Deliberately not decided**, the item *"The recording's exact schema and whether it is versioned. It is derived, so a breaking change costs a re-run."* Replace with: *"**Settled 2026-09-06 by the simulation plan.** The schema is the record set in `sim/Recording/Recording.cs`, and it carries `schemaVersion` — one integer, so a viewer can refuse a file it does not understand rather than mis-draw it. It stays derived, so a breaking change still costs only a re-run."*
3. **Same file → Still open**, the item *"Whether a recording should carry the dice so a viewer can show a roll, or only its result."* Strike with: *"**Settled 2026-09-06 by the simulation plan: it carries the dice** — `RecordedRoll` holds every die's raw and modified face, whether it succeeded, the threshold and the modifiers. The result alone would make a viewer that cannot show why a unit died, which is the first thing a person debugging the engine wants to see, and the cost is a few hundred integers per game."*
4. **Same file → Still open**, the item *"Whether the round cap belongs in the scenario or in `GameRules`."* Strike with: *"**Settled 2026-09-06 by the simulation plan: the scenario.** OPR has no round limit and `GameRules` must not invent one; the cap is a property of a *run*, not of a ruleset, and putting it in `GameRules` would put a testing concern inside the rules. `Scenario.RoundCap`, overridable from the console with `--rounds`."*
5. **Same file → Deliberately not decided**, the item *"Whether the policy is uniform over the legal set or weighted, and whether it prefers to act over passing."* Replace with: *"**Settled 2026-09-06 by the simulation plan: uniform, and it prefers to act.** `PolicyOptions.PassProbability` defaults to `0`, so a pass is taken only when the legal set admits nothing else; weighting stays a knob rather than a decision, because a weighted policy is a step toward an opponent that plays well and that is a later feature."*
6. **Same file → Two ways to answer an Ask**, after *"the legal set is the whole of its input"*. Add: *"One reading is unavoidable and is not a rule: a `move_region` legal set names model ids, an allowance and volume ids, and says nothing about where those models stand, so an answer to it must read their positions. `sim/`'s `PolicyView` is exactly that reading and nothing more — positions, base bodies, the table size and the world volumes the set's own ids name — and it exposes no `PropertySet`, `IGameRules`, stat, wound, condition or team, so nothing in it can say what a good move is."*
7. **Same file → What a full game means**, the Decided row: *"One team has models and the others do not."* Add: *"Read on the table, and only from round one: before deployment every model is on the shelf, so every team has none and the condition would fire on the first frame of every game."*
8. **Same file → Testing**, the sentence *"It is then the harness the interpreter's and scheduler's own full-game tests use."* Add: *"Those tests live in `tests/Tabletop.Sim.Tests` rather than in `tests/Tabletop.GameServer.Tests`, because `sim/` references the game server and a test project that `sim/` referenced back would be a cycle. `sim/` therefore reaches the core `Properties` set through the production path — `PropertiesSeeder.EmbeddedDocuments()` — rather than through `TestProperties`, and builds its armies with `ScenarioArmy` rather than `TestArmies`."*
9. **Same file → `sim/` is a fourth deliverable.** Add a paragraph: *"One thing the simulator must do that is not an answer to an Ask: **deploy**. Deployment's Ask names a unit, and putting that unit's models on the table is an ordinary `MoveModels` command rather than an Ask (the scheduler → The turn is an Ask: 'moving models by hand never ends one'). So `sim/` places models itself, inside the team's own deployment zone, resolving the zone's shape with `Tabletop.Geometry.ZoneStyles`. That is geometry rather than a rule, and nothing judges whether a placement is good — placement is free under every enforcement setting."*
10. **`docs/schema.md` → Diagram**, the `PARTICIPANT` block, which lists `id`, `team_id`, `account_id`, `army_snapshot`. Add `uuid game_id FK` and `uuid army_id FK`. Reason: the game server plan's `FindParticipantAsync(gameId, accountId)`, its `SetParticipantArmyAsync`, and the management plan's `POST /v1/games/{id}/join` all require both columns, and `scripts/postgres/init/001_schema.sql` has them; the diagram is stale rather than the schema. `sim/`'s `SocketRowSeeder` inserts both.

## Interfaces needed from other modules

Everything below is consumed by its exact name; nothing here is a request to rename or reshape.

1. **Interpreter plan** — `PropertiesSeeder.EmbeddedDocuments()` (`static IReadOnlyList<PropertySetDocument>`) and `PropertiesJson.ToSet(PropertySetDocument, Func<Guid>)` must both be `public`, because `ScenarioCompiler.CoreProperties` is how `sim/` gets the core Grimdark Future set without touching a test project. `PropertySetDocument.GameRules` is read to pick the document. Also `InMemoryPropertiesStore.Add(PropertySet)` returning `this`, `DiceSource()` parameterless, and `RulesEngineFactory(IPropertiesStore, IDiceSource, ISchedulerFactory)` public.
2. **Scheduler plan** — `SchedulerFactory()` parameterless and public, since `InProcessDriver` registers it directly rather than through DI. `AskQuestions.EndRound` is the boundary Ask's question, and invariant 5 reads it from `Tabletop.Protocol.AskQuestions` (added to the contracts plan on 2026-09-06 by that plan's *Interfaces needed*).
3. **Game server skeleton plan** — `GameSession.ConnectAsync(ISessionClient)`, `ReceiveAsync(ISessionClient, SocketMessage)`, `RunAsync<T>(Func<Task<T>>)`, `DrainAsync(string)` and the `Play` property public; `Hydrator.HydrateAsync`; `Replay.To(Board, GameInfo, IReadOnlyList<GameEvent>, long)`; `InMemoryEventLog()`; `GameServerOptions` constructible from object-initializer syntax with only `DbConnection`, `StytchProjectId` and `StytchJwksUrl` set (every other member defaulted); `PostgresGameRows(NpgsqlDataSource)`; and `PostgresFixture.CountAsync` taking parameters — assumed `Task<long> CountAsync(string sql, params object[] parameters)`. If it takes no parameters, `DriverAgreementTests` inlines the game id into the SQL string instead.
4. **Contracts plan** — `EventApplier.EffectiveLog(IReadOnlyList<GameEvent>)` as a public static, which the undo invariant and invariant 4 both call; `Projection.Initial(Board, GameInfo)`; `WireJson.Serialize<T>(T)` accepting an enum, which is how `sim/` prints `alternating-activation` rather than inventing a second spelling.
5. **Geometry plan** — `Movement.Check(PlacedModel, Point2, double, IReadOnlyList<WorldVolume>, IReadOnlyList<WorldVolume>, double?, TableSize?) → MoveCheck` with `WithinAllowance`, `CrossesImpassable` and `LeavesTable`; `ZoneStyles.ShapesOf(string)`, `ZoneStyles.Resolve(string, TableSize)`, `ZoneStyles.Contains(string, TableSize, Point2)`, and the style constants `Frontline` and `FreeForAll`; `Settling.SurfaceHeightAt(Point2, IReadOnlyList<WorldVolume>)`.
6. **The viewer plan** — consumes this plan's whole `Tabletop.Sim.Recording` table above as its input format, and `RecordingJson.SchemaVersion` as the number to check. It should generate its TypeScript types from these C# records, as the spec requires of the Protocol types, rather than hand-writing them.
7. **The interpreter and scheduler plans, later** — when their full-game tests are written they reference `sim/Tabletop.Sim.csproj` from `tests/Tabletop.Sim.Tests` and build a `Scenario`, an `InProcessDriver` and a `RunLoop`. `sim/` never references a test project (spec correction 8).

## Questions for the project owner

1. **How does a socket run get its session JWTs?** Assumed a comma-separated `TABLETOP_SIM_JWT` with one real session token per team plus one for the recorder, since a token names one account and an account maps to one participant. That is clumsy: a three-team soak needs four tokens pasted by hand, and they expire. If instead the game server should grow a development-only endpoint that mints a token for a named account — off by default, on only under `ASPNETCORE_ENVIRONMENT=Development` — then `SocketCredentials.FromEnvironment` becomes a call to it, `SocketRowSeeder` creates the accounts itself, and `--driver socket` needs no environment variable at all. That is a change to the game server, which is why it is a question rather than a decision. Nothing else in the plan moves either way.
2. **Should a `stuck` run fail the console's exit code?** Assumed yes: `RunReport.Passed` is false for `stuck` and `failed`, so `dotnet run --project sim -- skirmish --seeds 1..500` exits `1` if any game hangs, which is what makes it usable in CI. If a stuck run should only warn, `Summary.Failed` counts `failed` alone and the exit code changes; the frames are still dumped either way.
3. **Should `sim/` be in `Tabletop.sln`?** Assumed yes, in a `sim` solution folder, so `dotnet build` at the root builds it and `dotnet test` runs its tests — which is what makes it part of the whole-tree check every plan ends with. If it should be excluded so a client-only build stays small, it moves to its own solution and `CLAUDE.md`'s Commands section grows a line; nothing in the code changes.
4. **How many scenarios should the catalog ship with, and are these the right five?** Assumed `skirmish`, `terrain`, `player-turn`, `free-for-all` and `stalemate` — one per thing that could plausibly diverge (a plain game, terrain declarations, the second scheduler shape, three teams, and a run that must hit the cap). A regression scenario for a specific rules interaction is a script and is added the same way; the shape of the catalog does not change.
5. **Is 250 ms the right quiet window for the socket driver?** Assumed yes, which makes a full socket game take minutes. If socket runs should be fast enough for a seed range rather than a single scenario, the driver needs a positive signal that the server has rested — a frame the server sends when `AdvanceAsync` finds nothing to do — and that is a new `SocketMessage` kind owned by the contracts plan, so it is a question rather than a tuning knob. In process is unaffected and stays exact.

---

## Self-review

**Spec coverage.** Every section of `docs/superpowers/specs/2026-09-06-simulation-design.md` that concerns `sim/` maps to a task: *`sim/` is a fourth deliverable* → Tasks 1 and 11; *Two ways to answer an Ask* → Tasks 2 and 3; *Two drivers behind one interface* → Tasks 4 and 8; *The recording is the artifact* → Task 5; *Interactive debugging is a door* → Task 9; *What a full game means* → Task 6; *What it asserts* → Task 7; *Testing* (`sim/` half) → the tests in every task. The `viewer/` sections are another plan's and are untouched beyond the one line each in Task 11. Five *Deliberately not decided* / *Still open* items that `sim/` had to settle are settled in the spec corrections rather than silently.

**Placeholders.** None. Every step carries the code or the exact command it needs; the two places where an earlier plan's signature could not be confirmed from its text (`PostgresFixture.CountAsync`'s parameters, `Projection`'s team-ordinal lookup) name the fallback inline rather than leaving a gap.

**Type consistency.** `SubmitResult.Accepted`/`Rejected`, `RunOutcome.Decided`/`RoundLimit`/`Stuck`/`Failed`/`Paused`, `RecordedFrame`'s fourteen members, `PolicyView`'s three-argument constructor and `LogLines.Of`'s three-argument signature are used the same way in every task that touches them. `CompiledScenario` gained `RecorderParticipantId` in Task 1 and is consumed with it in Tasks 4, 5, 6 and 8. `RunLoop` gained `Log` in Task 7 and `Answers` in Task 8, both named where they are added.
