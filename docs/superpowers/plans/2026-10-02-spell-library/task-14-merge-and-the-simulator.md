# Task 14: The enforcement merge, and the simulator on real spells

**Files:**
- Merge: `main` (carrying the enforcement branch) into this tree
- Modify: `server/game/Properties/PropertySetDocument.cs` (refuse a spell no book lists)
- Modify: `sim/Scenarios/Scenario.cs` (`ScenarioArmy.ArmyBook`; `Scenario.Spells` removed), `sim/Scenarios/ScenarioBuilder.cs`, `sim/Scenarios/ScenarioCompiler.cs`, `sim/Scenarios/ScenarioCatalog.cs`
- Modify: `sim/Program.cs`, `sim/SimOptions.cs` (the socket refusal goes)
- Modify: `sim/Invariants/Invariants.cs` (invariant 7, `SpellsComeFromTheCastersBook`)
- Modify: `tests/Tabletop.Sim.Tests/ProgramTests.cs`, `RunLoopTests.cs`, `DriverAgreementTests.cs`, `InvariantTests.cs`, `ScenarioTests.cs`
- Modify: `tests/Tabletop.GameServer.Tests/PropertySetDocumentTests.cs`
- Regenerate: `viewer/src/lib/protocol/wire.generated.ts`

**Interfaces:**
- Consumes: the enforcement tree's `Emit`, `RunOutcome.Ended`, invariant 6 `NothingAdvancesAfterTheEnd` (*The enforcement merge* in the README); `PropertySet.SpellsOf` (Task 1); the shipped books (Task 13).
- Produces: `ScenarioArmy(..., IReadOnlyDictionary<string, string> UnitStats, string? ArmyBook = null)`; `ScenarioBuilder.Army(..., string? armyBook = null)`; `Invariants.SpellsComeFromTheCastersBook(IReadOnlyList<GameEvent> log, CompiledScenario scenario) → InvariantResult`. Removed: `Scenario.Spells`, `ScenarioBuilder.Spells`, `ScenarioCompiler.PropertiesFor`.

Spec → *Testing*, *The simulator*, and *Against the enforcement tree*. Every scenario now pins the production hash, so the game server can load what the scenario pins and `--driver socket` runs `casting`.

- [ ] **Step 1: Take the enforcement branch in**

Run: `git fetch && git merge main` (the enforcement branch has landed on `main`; if it has not, stop here until it has).
Resolve the textual conflicts the README names: `GfNames.cs` (keep both sets of constants), `GfRules.Scheduling.cs` (keep this tree's `ReadAnswer` and enforcement's `JudgeAdvance`), and the migration number (if `main` now holds a `005_*.sql`, `git mv` this tree's `005_spell_library.sql` to the next free number, update its header comment, and update the table count in `MigrationTests`).

Run: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests && (cd viewer && npm run check) && docker compose up -d && dotnet test`
Expected: PASS. `wire.generated.ts` now carries `armyBook?: string` on `ModelSlot` and enforcement's `game_ended`. Every test this tree wrote advances the engine through `Sim` or `SchedulerHarness`, which the enforcement branch already adapted to `Emit`.

Commit the merge: `git commit` (the default merge message, with the `Co-Authored-By` trailer).

- [ ] **Step 2: Write the failing tests**

In `tests/Tabletop.GameServer.Tests/PropertySetDocumentTests.cs`:

```csharp
    [Fact]
    public void A_spell_no_book_lists_is_refused()
    {
        var orphan = new EntryDocument("Orphan", CatalogKind.Spell, false, null, 1, null, "Orphan.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12),
            [new EffectDocument(EffectSubject.Self, 10, EffectKind.AddHit, null, null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction, [], [])]);
        Assert.Contains(PropertiesJson.Validate(TestProperties.CoreDocument with { Entries = [.. TestProperties.CoreDocument.Entries, orphan] }),
            p => p.Contains("Orphan: no book lists this spell"));
    }
```

In `tests/Tabletop.Sim.Tests/ProgramTests.cs`, delete `The_socket_driver_rejects_a_scenario_that_declares_its_own_spells` and add:

```csharp
    [Fact]
    public void The_casting_scenario_fields_a_real_books_casters_and_pins_the_production_set()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("casting"));
        Assert.Equal(ScenarioCompiler.CoreProperties.ContentHash, c.Info.PropertiesHash);
        var casters = c.Board.Armies.SelectMany(a => a.Snapshot.Units).SelectMany(u => u.Models).Where(m => m.Properties.Any(p => p.Name == "Caster")).ToList();
        Assert.NotEmpty(casters);
        Assert.All(casters, m => Assert.Equal("z65fgu0l29i4lnlu", m.ArmyBook));
        Assert.Equal(6, c.Properties.SpellsOf("z65fgu0l29i4lnlu").Count);
    }
```

In `tests/Tabletop.Sim.Tests/InvariantTests.cs`, `Every_invariant_holds_over_a_whole_game`'s count becomes `Assert.Equal(7, results.Count);`, and add:

```csharp
    [Fact]
    public async Task A_spell_picked_from_another_book_breaks_invariant_seven()
    {
        var scenario = ScenarioCompiler.Compile(ScenarioCatalog.Get("casting"));
        var caster = scenario.Board.Armies[0].Snapshot.Units.SelectMany(u => u.Models).First(m => m.Properties.Any(p => p.Name == "Caster"));
        var foreign = scenario.Properties.SpellsOf("1wj1ysgxpuuz9bc7")[0].Name;                       // an Orc Marauders spell
        var forged = new List<GameEvent> { new(1, null, new StepCompleted(Guid.CreateVersion7(), "pick-spell", new Chosen($"{foreign}|{caster.Id}"), [], []), null) };
        var result = Invariants.Invariants.SpellsComeFromTheCastersBook(forged, scenario);
        Assert.False(result.Held);
        Assert.Contains(foreign, result.Detail);
        await Task.CompletedTask;
    }
```

In `tests/Tabletop.Sim.Tests/DriverAgreementTests.cs`, give the agreement theory a scenario parameter: `[InlineData("skirmish", 21UL)]`, `[InlineData("skirmish", 24UL)]`, `[InlineData("casting", 3UL)]`, signature `The_same_scenario_plays_the_same_game_on_both_drivers(string name, ulong seed)`, and `ScenarioCatalog.Get(name)` in place of `ScenarioCatalog.Get("skirmish")`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.Sim.Tests tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~ProgramTests|FullyQualifiedName~InvariantTests|FullyQualifiedName~PropertySetDocumentTests"`
Expected: FAIL: the orphan validates, the casting casters carry no book, there is no invariant 7.

- [ ] **Step 4: Refuse an orphan spell**

In `PropertiesJson.Validate`, after the book checks Task 1 added:

```csharp
        var listed = books.SelectMany(b => b.Spells).ToHashSet();
        foreach (var e in d.Entries.Where(e => e.Kind == CatalogKind.Spell && !listed.Contains(e.Name))) problems.Add($"{e.Name}: no book lists this spell");
```

`TestProperties.WithSpells` lists every spell it adds under `TestBook` (Task 1), so every test set still validates.

- [ ] **Step 5: The scenario's book, and no scenario spells**

In `sim/Scenarios/Scenario.cs`, `ScenarioArmy` gains `string? ArmyBook = null` as its last parameter, and `Scenario` loses `IReadOnlyList<EntryDocument> Spells` (remove the `using Tabletop.GameServer.Properties;` if nothing else needs it).

In `ScenarioBuilder`: delete `_spells` and `Spells(...)`; `Army(...)` gains a last parameter `string? armyBook = null` passed as `ScenarioArmy`'s last argument; `Build()` constructs `Scenario` without `_spells`.

In `ScenarioCompiler`: delete `PropertiesFor`; `Compile` uses `var props = CoreProperties;`; `BuildArmy`'s `new ModelSlot(...)` passes `a.ArmyBook` as its last argument. Update the class's `CoreProperties` summary: *"The shipped Grimdark Future set, through the production path: the game server's composed embedded document, every army book included."*

In `ScenarioCatalog`: delete `Bolt`, and `casting` becomes:

```csharp
        // Casting is not a mode: a spell is cast inside an ordinary activation. This preset exists only because none of the
        // other scenarios fields a caster. Both armies are Human Defense Force (z65fgu0l29i4lnlu), whose six spells cover a unit-rule
        // grant, an "against" grant, a speed grant, unit and model damage, and one to three targets (spell library spec → Testing).
        ["casting"] = () => new ScenarioBuilder("casting", seed: 1)
            .Army("Red", 2, 3, modelRules: [new ScenarioRule(1, "Caster", 2)], armyBook: "z65fgu0l29i4lnlu")
            .Army("Blue", 2, 3, modelRules: [new ScenarioRule(1, "Caster", 2)], armyBook: "z65fgu0l29i4lnlu")
            .Expect(new Expectation.AskAsked("cast"))
            .Build(),
```

In `sim/Program.cs`, delete the `if (options.Driver == SimOptions.Socket && template.Spells.Count > 0)` block and its comment; in `sim/SimOptions.cs`, delete the usage line *"A scenario that declares its own spells (casting) cannot run on the socket driver."*

- [ ] **Step 6: Invariant 7**

In `sim/Invariants/Invariants.cs`, add the method and append it to `CheckAll` after enforcement's sixth:

```csharp
    /// <summary>
    /// Invariant 7 (spell library spec → Testing). p.13: "the Caster's spells must be picked from their own faction": every closed
    /// pick-spell names a spell the caster model's own army book lists in the pinned set, and every grant a step wrote names an effect
    /// row the pinned set holds. Reads the log, the board and the pinned set; names no rule.
    /// </summary>
    public static InvariantResult SpellsComeFromTheCastersBook(IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var slots = scenario.Board.Armies.SelectMany(a => a.Snapshot.Units).SelectMany(u => u.Models).ToDictionary(m => m.Id);
        var effects = scenario.Properties.AllEffects().Select(e => e.Id).ToHashSet();
        foreach (var e in EventApplier.EffectiveLog(log))
        {
            if (e.Payload is not StepCompleted step) continue;
            if (step.Step == "pick-spell" && step.Answer is Chosen { OptionId: var id } && id.LastIndexOf('|') is var cut and > 0
                && Guid.TryParse(id[(cut + 1)..], out var caster) && slots.TryGetValue(caster, out var slot)
                && scenario.Properties.SpellsOf(slot.ArmyBook).All(s => s.Name != id[..cut]))
                return new InvariantResult(nameof(SpellsComeFromTheCastersBook), false, $"seq {e.Seq}: caster {caster:N} of book {slot.ArmyBook ?? "none"} picked '{id[..cut]}'");
            foreach (var g in step.Changes.OfType<GrantAdded>().Where(g => !effects.Contains(g.Grant.EffectId)))
                return new InvariantResult(nameof(SpellsComeFromTheCastersBook), false, $"seq {e.Seq}: grant {g.Grant.Id:N} names effect {g.Grant.EffectId:N}, which the pinned set does not hold");
        }
        return new InvariantResult(nameof(SpellsComeFromTheCastersBook), true, null);
    }
```

`RunLoopTests`' `casting` case already runs the scenario twice; after the merge its outcome may be `ended` (enforcement), which the test compares between the two runs rather than naming.

- [ ] **Step 7: Run every suite**

Run: `docker compose up -d && dotnet test && (cd viewer && npm test && npm run check)`
Expected: PASS. `DriverAgreementTests`' `casting` case plays the same game in process and over the socket, which is the refusal's removal proven.

- [ ] **Step 8: Commit**

```bash
git add server/game/Properties/PropertySetDocument.cs sim tests/Tabletop.Sim.Tests tests/Tabletop.GameServer.Tests/PropertySetDocumentTests.cs viewer/src/lib/protocol/wire.generated.ts
git commit -m "Run the casting scenario on a real army book, on both drivers, with invariant 7"
```
