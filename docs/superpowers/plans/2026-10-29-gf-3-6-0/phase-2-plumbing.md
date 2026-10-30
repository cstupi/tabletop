# Phase 2 — Edition plumbing (design step 3)

Index: `index.md`. Spec: `2026-10-29-rule-set-versions-design.md` → "Editions are game-rules ids", "Where the rules live". Group **B**: one worktree, tasks sequential. Nothing else in wave 2 starts before this merges.

Decisions this phase fixes:

- Ids live in `libs/Tabletop.Content/RuleSets.cs` so management, sim and the game server share them: `RuleSets.Gf351 = "grimdark-future"`, `RuleSets.Gf360 = "grimdark-future-3.6"`, `RuleSets.Default = Gf360`, `RuleSets.EditionOf(id)` → `ArmyEditions.V35|V36` (throws on another id), `RuleSets.RulesIdOf(edition)`. `GrimdarkFutureRules.RulesId` becomes an alias of `RuleSets.Gf351` (every existing reference compiles).
- `GfEdition` is declared **complete** in this phase, both instances with the values the rules diff fixes, so phase 5's parallel tasks never edit `GfEdition.cs` (no merge conflicts); each phase-5 task makes one code path read its field. Until then the fields are data nobody reads.
- `GrimdarkFutureRules` keeps one class, two instances `V351` and `V360`; `Instance` stays as an alias of `V351`. Statics that today read a `GfNames` number keep reading it until the phase-5 task that owns that rule switches them to `GfEdition.Of(state)`.
- Content moves into `server/game/Content/grimdark-future-3.5.1/` and `server/game/Content/grimdark-future-3.6/`, same file names in each (`core.properties.json`, `army-rules.part.json`, `relic-wargear.part.json`, `army-book-rules.part.json`, `army-books/<uid>.book.json`). The hash is of the canonical composed document (`PropertySetDocument.Hash`, content only; `Compose` sorts entries), so the move cannot change it; a test pins the 3.5.1 hash to the value measured before the move. Resource logical names become `Tabletop.GameServer.Content.<folder>.<file>` and `Tabletop.GameServer.Content.<folder>.army-books.<file>`.

---

### Task 2.1: `RuleSets`, `GfEdition`, two `GrimdarkFutureRules` instances, the registry — **Opus**

**Files:**
- Create: `libs/Tabletop.Content/RuleSets.cs`, `server/game/Rules/GrimdarkFuture/GfEdition.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.cs:9-13`, `server/game/Rules/IGameRules.cs:115` (`GameRulesRegistry.Resolve`), `server/game/Rules/GrimdarkFuture/GfRules.Options.cs:8` (`OptionCatalog => GfOptions.CatalogOf(Edition)`), `server/game/Rules/GrimdarkFuture/GfOptions.cs:23-32` (`CatalogOf(GfEdition)`: the `line_of_sight` page string from `edition.HybridSightPage`), `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs:14` (the `Lazy` is built per instance already; it gains access to `Edition` for phase 5)
- Test: `tests/Tabletop.GameServer.Tests/GfEditionTests.cs` (new), `tests/Tabletop.GameServer.Tests/GameRulesRegistryTests.cs` (new or existing), `tests/Tabletop.Content.Tests/RuleSetsTests.cs` (new)

**Interfaces:**
- Produces, in `Tabletop.Content`: `public static class RuleSets { public const string Gf351 = "grimdark-future"; public const string Gf360 = "grimdark-future-3.6"; public const string Default = Gf360; public static string EditionOf(string rulesId); public static string RulesIdOf(string edition); public static readonly IReadOnlyList<string> All = [Gf351, Gf360]; }`.
- Produces, in `Tabletop.GameServer.Rules.GrimdarkFuture`:

```csharp
public sealed record GfEdition(
    string Id, string Name,                                   // "grimdark-future", "Grimdark Future v3.5.1" | "grimdark-future-3.6", "Grimdark Future v3.6.0"
    string ContentFolder,                                     // "grimdark-future-3.5.1" | "grimdark-future-3.6"
    double MeleeHorizontal, double MeleeVertical,             // C1: 2, 4 | 3, 3
    bool FatiguedSixesTrigger,                                // C2: true | false
    bool MeleeLossByWoundsTaken,                              // C3: false | true
    bool UnitDangerousTest,                                   // C4: false (per model, Tough dice) | true (whole unit, dice = wounds to destroy)
    bool UnstableByDistance,                                  // C5: false (DangerousOnRushCharge on Rush/Charge) | true (over 6" moved into or through)
    IReadOnlyDictionary<TerrainDeclarations, string> TerrainWords, // C5 labels, below
    bool ArtilleryHoldOnly,                                   // S2: true | false
    bool HasAssault,                                          // S3: false | true
    bool CasterTargetNeedsSight, bool SpendersNeedSight, bool SpendersMeasuredToCasterModel, // S4: true,true,false | false,false,true
    bool ImpactBeforeCounter, bool CounterReducesImpact, bool CounterOptional, bool CounterLostWhenFatigued, // S5/S11: false,true,false,false | true,false,true,true
    bool HasCumbersome,                                       // S6: false | true
    int? HeroAuraTough,                                       // S10: null | 9
    bool IndirectNeedsHold,                                   // S12: false | true
    double ScoutReach, double ScoutClearance,                 // S13: 12, 0 | 9, 9
    bool AircraftTransportUnreachable,                        // S15: false | true
    bool UnstoppableKeepsOwnPenalties,                        // S16: false | true
    double SideBattleLong, double SideBattleShort,            // M1: 0.75, 0.625 | 2.0/3, 0.75
    string HybridSightPage,                                   // "p.56" | "3.6.0 p.58"
    string? ArtilleryNote,                                    // "Artillery: may only use Hold actions (p.13)" | null
    string? CumbersomeNote)                                   // null | "Cumbersome: may only use Hold or Advance actions, and can't shoot when moving (3.6.0 p.14)"
{
    public static GfEdition V351 { get; } = ...; public static GfEdition V360 { get; } = ...;
    public static GfEdition Of(GameState state) => Of(state.Info.GameRules);
    public static GfEdition Of(string rulesId) => rulesId == RuleSets.Gf360 ? V360 : rulesId == RuleSets.Gf351 ? V351 : throw new UnknownGameRulesException(rulesId);
}
```

`TerrainWords` (the card and label words; everything not listed keeps today's word in both): 3.5.1 `BlocksLineOfSight` "Blocks line of sight", `SeeInAndOut` "See in and out", `DangerousOnRushCharge` "Dangerous when rushing or charging", `Steep` "Difficult moving up"; 3.6.0 "Sight blocking", "Dense", "Unstable", "Steep". `Steep` is the flag Task C5 adds to `TerrainDeclarations`; until then this map omits it (C5 adds the two entries).

- `GrimdarkFutureRules`: `public GfEdition Edition { get; }`, `private GrimdarkFutureRules(GfEdition e)`, `public static GrimdarkFutureRules V351 { get; } = new(GfEdition.V351)`, `V360`, `public static GrimdarkFutureRules Instance => V351`, `public const string RulesId = RuleSets.Gf351`, `public string Id => Edition.Id`.
- `GameRulesRegistry.Resolve(id)`: `Gf351 → V351`, `Gf360 → V360`, else `UnknownGameRulesException`.

- [ ] **Step 1: Failing tests**

```csharp
// RuleSetsTests
[Fact] public void Editions_map_to_ids_both_ways() { Assert.Equal("3.6", RuleSets.EditionOf(RuleSets.Gf360)); Assert.Equal(RuleSets.Gf351, RuleSets.RulesIdOf("3.5")); Assert.Throws<ArgumentException>(() => RuleSets.EditionOf("x")); }
// GameRulesRegistryTests
[Fact] public void Both_editions_resolve_to_their_own_instance_and_name()
{
    Assert.Same(GrimdarkFutureRules.V351, GameRulesRegistry.Resolve("grimdark-future"));
    Assert.Same(GrimdarkFutureRules.V360, GameRulesRegistry.Resolve("grimdark-future-3.6"));
    Assert.Equal("grimdark-future-3.6", GrimdarkFutureRules.V360.Id);
    Assert.Throws<UnknownGameRulesException>(() => GameRulesRegistry.Resolve("grimdark-future-3.7"));
}
// GfEditionTests
[Fact] public void Of_reads_the_games_rules_id() { Assert.Same(GfEdition.V360, GfEdition.Of(StateWithRules("grimdark-future-3.6"))); Assert.Same(GfEdition.V351, GfEdition.Of(StateWithRules("grimdark-future"))); }
[Fact] public void The_3_5_1_values_are_todays_constants() { var e = GfEdition.V351; Assert.Equal((GfNames.MeleeHorizontal, GfNames.MeleeVertical, GfNames.ScoutReach), (e.MeleeHorizontal, e.MeleeVertical, e.ScoutReach)); Assert.True(e.ArtilleryHoldOnly && e.FatiguedSixesTrigger && e.CounterReducesImpact); }
[Fact] public void The_3_6_0_values_are_the_rules_diffs() { var e = GfEdition.V360; Assert.Equal((3.0, 3.0, 9.0, 9.0), (e.MeleeHorizontal, e.MeleeVertical, e.ScoutReach, e.ScoutClearance)); Assert.Equal(9, e.HeroAuraTough); Assert.Equal((2.0/3, 0.75), (e.SideBattleLong, e.SideBattleShort)); }
[Fact] public void The_option_catalog_cites_the_editions_page() { Assert.Contains("p.56", GrimdarkFutureRules.V351.OptionCatalog.Options.Single(o => o.Key == "line_of_sight").Page); Assert.Contains("3.6.0 p.58", GrimdarkFutureRules.V360.OptionCatalog.Options.Single(o => o.Key == "line_of_sight").Page); }
```

(`StateWithRules`: `TestGames`/`Sim` fixtures build a `GameState` with a `GameInfo`; use `with { GameRules = id }`.)

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~GfEdition|FullyQualifiedName~GameRulesRegistry"` and `dotnet test tests/Tabletop.Content.Tests --filter RuleSets` — FAIL.
- [ ] **Step 3: Implement** as the Interfaces block says. `GfOptions.Catalog` stays as `CatalogOf(GfEdition.V351)` for any remaining caller; `GfOptionsTests` that read `GfOptions.Catalog` keep passing.
- [ ] **Step 4: Run** `dotnet test tests/Tabletop.GameServer.Tests` (whole project) — PASS: nothing behavioural changed for 3.5.1.
- [ ] **Step 5: Commit** `git commit -am "Rules: two Grimdark Future editions, GfEdition, the registry resolves both"`.

---

### Task 2.2: Content folders per edition; the 3.5.1 hash pinned; the 3.6 copy — **Opus**

**Files:**
- Move (`git mv`): `server/game/Content/grimdark-future-core.properties.json` → `Content/grimdark-future-3.5.1/core.properties.json`; `grimdark-future-army-rules.part.json` → `.../army-rules.part.json`; `grimdark-future-relic-wargear.part.json` → `.../relic-wargear.part.json`; `grimdark-future-army-book-rules.part.json` → `.../army-book-rules.part.json`; `Content/army-books/*.book.json` → `Content/grimdark-future-3.5.1/army-books/`.
- Create: `Content/grimdark-future-3.6/` with copies of the five kinds above, `"gameRules": "grimdark-future-3.6"` in every file (core, three parts, 48 books) and the core's `"name": "Grimdark Future v3.6.0"`; a one-off script in the scratchpad does the replace (`sed`), not by hand.
- Modify: `server/game/Tabletop.GameServer.csproj:20-22` (globs `Content/*/*.properties.json`, `Content/*/*.part.json`, `Content/*/army-books/*.book.json`, LogicalName `Tabletop.GameServer.Content.%(RecursiveDir)%(Filename)%(Extension)` with `/` → `.`: check the produced names with `GetManifestResourceNames()` in the test), `server/game/Properties/PropertiesSeeder.cs:8,11` (`GeneratedPartName = "army-book-rules.part.json"`; `EmbeddedPart(string contentFolder, string fileName)` matches `$".Content.{contentFolder}.{fileName}"`), every caller of `GeneratedPart`/`EmbeddedPart` (`sim/Suite/SuiteRunner.cs:~25` → `EmbeddedPart(GfEdition.V351.ContentFolder, GeneratedPartName)` for now; phase 6 makes it per edition), `tests/Tabletop.GameServer.Tests/Infrastructure/TestProperties.cs:8` (the resource name of the bare core), `tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs:19-22`, `tests/Tabletop.Sim.Tests/Suite/ArmyGamesTests.cs:36-43` (paths), `server/game/AGENTS.md:23` and "Conventions" lines 153-158.
- Test: `tests/Tabletop.GameServer.Tests/ContentFoldersTests.cs` (new)

**Interfaces:**
- Produces: `PropertiesSeeder.EmbeddedDocuments()` returns two documents, `GameRules` `grimdark-future` and `grimdark-future-3.6`. `PropertiesSeeder.GeneratedPartName`, `PropertiesSeeder.EmbeddedPart(contentFolder, fileName)`.

- [ ] **Step 1: Measure the 3.5.1 hash before anything moves**: a throwaway test or `dotnet run` snippet printing `PropertiesJson.Hash(PropertiesSeeder.EmbeddedDocuments().Single())`; copy the `sha256:…` value into the test below.

- [ ] **Step 2: Failing tests**

```csharp
[Fact] public void The_3_5_1_set_hashes_as_before_the_folder_move()
    => Assert.Equal("sha256:<measured in step 1>", PropertiesJson.Hash(PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == RuleSets.Gf351)));
[Fact] public void Two_editions_are_embedded_each_in_its_folder()
{
    var docs = PropertiesSeeder.EmbeddedDocuments();
    Assert.Equal([RuleSets.Gf351, RuleSets.Gf360], docs.Select(d => d.GameRules).Order());
    Assert.Equal("Grimdark Future v3.6.0", docs.Single(d => d.GameRules == RuleSets.Gf360).Name);
    Assert.NotNull(PropertiesSeeder.EmbeddedPart("grimdark-future-3.6", PropertiesSeeder.GeneratedPartName));
}
[Fact] public void Every_3_6_file_names_the_3_6_id()   // reads the manifest: every resource under Content.grimdark-future-3.6. parses with GameRules == Gf360
```

- [ ] **Step 3: Run** `dotnet test tests/Tabletop.GameServer.Tests --filter ContentFolders` — FAIL (one document, no folder).
- [ ] **Step 4: Move, copy, edit** as listed. Fix `TestProperties.RulebookDocument`'s resource name and `SpellLibraryTests` ("two composed documents, one per edition").
- [ ] **Step 5: Run** `dotnet test tests/Tabletop.GameServer.Tests tests/Tabletop.Sim.Tests` — PASS, the hash test included. Then `docker compose restart game` (or `./scripts/game`) and `docker compose logs game | grep -i seed`: both sets seed; `GET /v1/properties` lists two. Existing dev games keep `grimdark-future` and hydrate (open one in the viewer).
- [ ] **Step 6: Recipe** in `server/game/AGENTS.md` → Layout and Conventions: "Content is one folder per edition (`Content/<GfEdition.ContentFolder>/`): `core.properties.json`, the hand parts, the generated part and `army-books/`. A new edition is a new folder, a new id in `RuleSets`, a `GfEdition` and a `GrimdarkFutureRules` instance. Editing a 3.5.1 file makes a new 3.5.1 hash; 3.6 files never move a 3.5.1 game." Update the embedded-resource glob line.
- [ ] **Step 7: Commit** `git add -A server/game/Content server/game/Tabletop.GameServer.csproj server/game/Properties server/game/AGENTS.md tests sim && git commit -m "Content: one folder per edition; 3.6 starts as a copy of 3.5.1; the 3.5.1 hash is pinned"`.

---

### Task 2.3: The generator writes an edition's folder — **Sonnet**

**Files:**
- Modify: `scripts/ArmyBooks/Program.cs:35-79` (`generate --edition`: `folder = edition == "3.5" ? "grimdark-future-3.5.1" : "grimdark-future-3.6"`, `rulesId = RuleSets.RulesIdOf(edition)`; core `content/<folder>/core.properties.json`, hand part `content/<folder>/army-rules.part.json`; outputs `content/<folder>/army-books/` and `content/<folder>/army-book-rules.part.json`; the units file path unchanged until Task 7.3), `scripts/ArmyBooks/BookWriter.cs:24` (`BookDocument` game rules from a parameter), `scripts/ArmyBooks/RuleWriter.cs:89` (`PropertySetDocument(name, rulesId, …)`; name `"Grimdark Future army-book rules (generated)"` unchanged for 3.5 so the serialized output stays identical)
- Test: `tests/Tabletop.ArmyBooks.Tests/RuleWriterTests.cs`, `BookWriterTests.cs` (one test each: the id passed is the id written)

**Interfaces:**
- Produces: `RuleWriter.Build(books, core, hand, rulesId)` / `BookWriter.Build(books, rulesId)` signatures (append the parameter; default none). `./scripts/army-books generate --edition 3.5` rewrites `Content/grimdark-future-3.5.1/`.

- [ ] **Step 1: Failing tests**: `The_part_names_the_rules_id_it_was_built_for` (`RuleWriter.Build(..., "grimdark-future-3.6")` → document `GameRules == "grimdark-future-3.6"`), same for `BookWriter`.
- [ ] **Step 2: Run** `dotnet test tests/Tabletop.ArmyBooks.Tests` — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify byte-identity**: `./scripts/army-books generate --edition 3.5 && git status --porcelain server/game/Content` prints nothing. Tests PASS.
- [ ] **Step 5: Recipe**: `scripts/ArmyBooks/AGENTS.md`: "`generate --edition E` reads `.superpowers/armyforge/E/` and that edition's core and hand part, writes that edition's `army-books/` and generated part."
- [ ] **Step 6: Commit** `git commit -am "Army books: generate writes an edition's content folder"`.

---

### Task 2.4: `ScenarioCompiler` and the test fixtures pick the core document by id — **Sonnet**

**Files:**
- Modify: `sim/Scenarios/ScenarioCompiler.cs:37-42` (`CoreDocuments: IReadOnlyDictionary<string, PropertySetDocument>` keyed by `GameRules`; `CoreDocument` = the 3.5.1 one; `CorePropertiesOf(string rulesId)` cached per id; `CoreProperties` = `CorePropertiesOf(RuleSets.Gf351)`), `sim/Lifecycle/LifecycleGame.Lobby.cs:17` (`"grimdark-future"` → `RuleSets.Gf351`), the five `.Single(d => d.GameRules == GrimdarkFutureRules.RulesId)` test sites (`SpellGamesTests.cs:132`, `SpecialGamesTests.cs:87`, `ArmyGamesTests.cs:339`, `CoreGamesTests.cs:83`, `SocketRowsContract.cs:178`) → `ScenarioCompiler.CoreDocuments[RuleSets.Gf351]`
- Test: `tests/Tabletop.Sim.Tests/ScenarioCompilerTests.cs` (existing or new): `Core_properties_exist_for_both_editions_and_differ_in_id` (`CorePropertiesOf(Gf360).GameRules == Gf360`, `CorePropertiesOf(Gf351).ContentHash == CoreProperties.ContentHash`).

- [ ] **Step 1: Failing test** as above. **Step 2:** `dotnet test tests/Tabletop.Sim.Tests --filter ScenarioCompiler` — FAIL. **Step 3:** implement. **Step 4:** `dotnet test tests/Tabletop.Sim.Tests` and `dotnet run --project sim -- suite --only core` — PASS (every game still 3.5.1). **Step 5:** commit `git commit -am "Sim: core properties per edition"`.
