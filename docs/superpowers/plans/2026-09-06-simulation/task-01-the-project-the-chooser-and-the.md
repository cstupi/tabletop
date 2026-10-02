# Task 1: The project, the chooser, and the scenario

**Files:**
- Create: `sim/Tabletop.Sim.csproj`, `sim/Program.cs`, `sim/Chooser.cs`, `sim/Scenarios/Scenario.cs`, `sim/Scenarios/ScenarioBuilder.cs`, `sim/Scenarios/ScenarioCompiler.cs`, `sim/Scenarios/ScenarioCatalog.cs`, `sim/Scenarios/Expectation.cs`
- Create: `tests/Tabletop.Sim.Tests/Tabletop.Sim.Tests.csproj`, `tests/Tabletop.Sim.Tests/PostgresCollection.cs`
- Test: `tests/Tabletop.Sim.Tests/ChooserTests.cs`, `tests/Tabletop.Sim.Tests/ScenarioTests.cs`
- Modify: `Tabletop.sln`

**Interfaces:**
- Consumes: `Army`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`, `ArmySnapshot`, `TableState`, `TerrainPlacement`, `TerrainVolume`, `Marker`, `MarkerKind`, `DeploymentZone`, `VisualRef` (Content); `Base`, `TableSize`, `Point3`, `Yaw`, `Volume`, `VolumeKind`, `Footprint`, `Point2`, `TerrainDeclarations` (Geometry); `GameInfo` (including its `RngSeed` member — see *Interfaces needed* item 4), `GameStatus`, `SchedulerShape`, `Enforcement`, `Team`, `Participant`, `Board`, `ArmyInPlay` (Protocol); `PropertiesSeeder.EmbeddedDocuments()`, `PropertiesJson.Hash`, `PropertiesJson.Parse`, `PropertiesJson.ToSet`, `PropertySet`, `PropertySetDocument`, `EntryDocument` (game server `Tabletop.GameServer.Properties`); `GrimdarkFutureRules.RulesId` (`Tabletop.GameServer.Rules.GrimdarkFuture`).
- Produces: `sealed class Chooser(ulong seed)` with `int Next(int bound)`, `double Double()`, `bool Chance(double p)`, `T Pick<T>(IReadOnlyList<T> items)`, `IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T>)`, `IReadOnlyList<T> Subset<T>(IReadOnlyList<T> items, int count)`, `long Drawn`; `sealed record Scenario(string Name, ulong Seed, SchedulerShape Shape, Enforcement Enforcement, int RoundCap, TableSize Table, IReadOnlyList<ScenarioArmy> Armies, IReadOnlyList<ScenarioTerrain> Terrain, IReadOnlyList<ScenarioMarker> Markers, IReadOnlyList<string> ZoneShapes, IReadOnlyList<EntryDocument> Spells, AnswerScript Script, bool ScriptOnly, IReadOnlyList<Expectation> Expectations)` with `Scenario WithSeed(ulong)`; `sealed record ScenarioArmy(string Name, int Units, int ModelsPerUnit, IReadOnlyList<ScenarioRule> UnitRules, IReadOnlyList<ScenarioRule> ModelRules, IReadOnlyList<ScenarioWeapon> Weapons, IReadOnlyDictionary<string,string> UnitStats)`; `sealed record ScenarioRule(int Unit, string Name, int? Parameter = null)`; `sealed record ScenarioWeapon(string Name, IReadOnlyDictionary<string,string> Stats, IReadOnlyList<ScenarioRule> Rules)`; `sealed record ScenarioTerrain(string Mesh, Point3 Position, double Yaw, double Scale, IReadOnlyList<Volume> Volumes)`; `sealed record ScenarioMarker(MarkerKind Kind, Point3 Position, double Diameter, string? Label)`; `sealed class ScenarioBuilder(string name, ulong seed)` (the fluent form: `Shape`, `Enforcing`, `RoundCap`, `Table`, `Zones`, `Spells(params EntryDocument[])`, `Army`, `Terrain`, `Marker`, `Scripted`, `Expect`, `Build()`); `sealed record CompiledScenario(Scenario Scenario, GameInfo Info, Board Board, PropertySet Properties, IReadOnlyList<Guid> TeamIds, IReadOnlyList<Guid> ParticipantIds, IReadOnlyDictionary<Guid,Guid> TeamOfParticipant, TableState Snapshot, Guid RecorderParticipantId)` with `Guid ParticipantFor(Guid teamId)`; `static class ScenarioCompiler { CompiledScenario Compile(Scenario s); PropertySet CoreProperties { get; } PropertySet PropertiesFor(IReadOnlyList<EntryDocument> spells); }`; `static class ScenarioCatalog { IReadOnlyList<string> Names { get; } Scenario Get(string name); bool TryGet(string name, out Scenario) }`.

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

- [ ] **Step 7: Run; expect failure, then write `Scenario.cs` and `ScenarioBuilder.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter ScenarioTests`
Expected: `CS0246: ... 'ScenarioBuilder' could not be found`.

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;
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

`ScenarioBuilder` is its own file, `sim/Scenarios/ScenarioBuilder.cs`.

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;
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
    /// to an army book this project has not designed. Nothing invented here reaches the game's content.</summary>
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
/// therefore the value GameState.RngSeed takes.
/// </summary>
public static class ScenarioCompiler
{
    // Declared first: static initializers run in textual order and CoreProperties reads it.
    static PropertySetDocument CoreDocument { get; } =
        PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId);

    /// <summary>The core Grimdark Future set, through the production path: the game server's embedded document.</summary>
    public static PropertySet CoreProperties { get; } = PropertiesJson.ToSet(CoreDocument, Guid.CreateVersion7);

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

        // The properties the engine is handed and the hash the game row names must be one set (hydration loads by hash).
        var props = PropertiesFor(s.Spells);

        // RngSeed is positional and last among the scalars (contracts plan → GameInfo), so a game cannot
        // be built without a seed. This is the one value a scenario's seed fixes.
        var info = new GameInfo(
            Guid.CreateVersion7(), s.Name, GameStatus.Playing, participants[0].AccountId,
            GrimdarkFutureRules.RulesId, props.ContentHash, s.Shape, s.Enforcement,
            WaitsBetweenSteps: false, unchecked((long)s.Seed), teams, participants);

        return new CompiledScenario(s, info, new Board(snapshot, armies), props,
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
            // Weapon ids are per model: the engine addresses weapons by id across the unit.
            var models = Enumerable.Range(1, a.ModelsPerUnit).Select(m => new ModelSlot(
                Guid.CreateVersion7(), m, $"{a.Name} {u}.{m}",
                new Dictionary<string, string>(), modelRules, Base.Circle(1.26), 1.5, null,
                a.Weapons.Select(w => new Weapon(Guid.CreateVersion7(), w.Name,
                    w.Stats, w.Rules.Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList())).ToList())).ToList();
            units.Add(new Unit(Guid.CreateVersion7(), u, $"{a.Name} unit {u}", a.UnitStats,
                a.UnitRules.Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Name, r.Parameter)).ToList(), models));
        }
        return new ArmySnapshot(Guid.CreateVersion7(), a.Name, "grimdark-future", units);
    }
}
```

`Base.Circle(1.26)` is a 32 mm base in inches and `1.5` its height — the same two numbers the scheduler plan's `TestArmies.Build` uses, so a scenario and a fixture describe the same model.

`CoreDocument` is declared before `CoreProperties` because static initialisers run in textual order. Each model gets its own weapon ids, since the engine addresses weapons by id across a unit. `ScenarioRule`'s unit number is 1-based, matching `Unit.Ordinal`. `Compile` builds the `PropertySet` once through `PropertiesFor`, and `GameInfo.PropertiesHash` is that set's `ContentHash`, so a scenario with spells names the set it plays under.

- [ ] **Step 9: Write `ScenarioCatalog.cs`**

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Scenarios;

/// <summary>The named scenarios the console runs. Regression scripts are added here beside the soak ones.</summary>
public static class ScenarioCatalog
{
    /// <summary>One test spell, the shape of a damage spell: an enemy unit in range takes hits. Authored as JSON and parsed
    /// through the production path, so the sim declares a spell exactly as a content document would. Never shipped.</summary>
    static readonly EntryDocument Bolt = PropertiesJson.Parse(
        """
        { "name": "Sim spells", "gameRules": "grimdark-future", "stats": [], "actions": [], "rolls": [], "entries": [
          { "name": "Bolt", "kind": "spell", "cost": 2, "faction": "Sim Book",
            "ruleText": "Target enemy unit within 24\" takes 3 hits with AP(1).",
            "targeting": { "side": "enemy", "grain": "unit", "count": 1, "range": 24 },
            "effects": [ { "subject": "self", "ordinal": 10, "kind": "add_hit", "amount": 3, "conditions": [],
              "children": [ { "subject": "self", "ordinal": 20, "kind": "grant_property", "targetEntry": "AP", "amount": 1, "conditions": [] } ] } ] } ] }
        """).Entries.Single();

    private static readonly Dictionary<string, Func<Scenario>> Factories = new(StringComparer.Ordinal)
    {
        ["skirmish"] = () => new ScenarioBuilder("skirmish", seed: 1)
            .Army("Red", units: 3, modelsPerUnit: 3)
            .Army("Blue", units: 3, modelsPerUnit: 3)
            .Build(),

        ["terrain"] = () => new ScenarioBuilder("terrain", seed: 1)
            .Army("Red", 3, 3).Army("Blue", 3, 3)
            .Terrain("ruin", new Point3(36, 24, 0), volumes:
                [new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 3,
                    TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, null)])
            .Terrain("forest", new Point3(18, 30, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Circle(4), new Point2(0, 0), new Yaw(0), 0, 2,
                    TerrainDeclarations.Difficult | TerrainDeclarations.Cover, null)])
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
        // spend map (p.13), and the caster spending its own leftover tokens on its own roll.
        ["casting"] = () => new ScenarioBuilder("casting", seed: 1)
            .Spells(Bolt)
            .Army("Red", 2, 3, modelRules: [new ScenarioRule(1, "Caster", 2)])
            .Army("Blue", 2, 3, modelRules: [new ScenarioRule(1, "Caster", 2)])
            .Build(),
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

`Bolt` is declared above `Factories` so the dictionary initialiser reads it after it is assigned. Its range is 24 inches. `casting` fields a `Caster(2)` on both armies: at a shorter range or with one caster, no spell ever has a target and no spend window is addressed to two teams. The `terrain` volumes are passed as one collection expression to the `volumes:` parameter.

The zone shape ids come from `Tabletop.Geometry.ZoneStyles.ShapesOf` (`frontline/1`, `free-for-all/1`, …), in team-ordinal order, which is exactly what `SetZoneStyle.Shapes` carries during setup. `sim/` stores the expansion rather than the style name, because that is what a `deployment_zone` row holds (setup phase design → *Zones*).

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

- [ ] **Step 10b: Write the `sim/Program.cs` stub**

`OutputType Exe` fails with CS5001 without an entry point, and the real one arrives in Task 10.

```csharp
// Console entry point; the runner arrives with a later task.
return 0;
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
