# The spell library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every spell of every official Grimdark Future army book is castable and resolved by the engine, and a caster casts only its own book's spells, carried from Army Forge import on each model slot.

**Architecture:** The pinned `PropertySet` becomes a composition of three authored kinds of file: the hand-written core document, a hand-written army-rules part, and 48 generated `<uid>.book.json` files, folded into one document before hashing. A checked-in console tool, `scripts/ArmyBooks`, translates Army Forge's `generation` structure into spell entries. The engine gains what the corpus needs: grants that hold one effect row, consumption by the first step that would apply a grant, four effect kinds, two predicates, wounds and attack kinds for spell hits, and the book filter on the cast Ask.

**Tech Stack:** `net10.0`, C# 12, xUnit, `System.Text.Json`, Npgsql, DbUp (management migrations), Dapper (management).

**Spec:** [`docs/superpowers/specs/2026-10-02-spell-library-design.md`](../../specs/2026-10-02-spell-library-design.md), the whole document. The fifteen hookless rules' hooks are the next plan's ([`2026-10-02-army-rule-hooks`](../2026-10-02-army-rule-hooks/README.md)).

**Requires landed:** every plan through the viewer (`main` at `6f1e0fa`). The enforcement tree (`docs/superpowers/specs/2026-10-02-enforcement-engine-design.md`) runs concurrently on its own branch and is **merged into this tree between Task 13 and Task 14** (*The enforcement merge*, below).

## Global Constraints

- `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`, as every project in `Tabletop.sln`.
- The rules engine lives only in `server/game/`; a rule is named only in `server/game/Rules/GrimdarkFuture/` (`CLAUDE.md` → Layout). The engine never appends or broadcasts and holds no state between calls.
- Authored properties are embedded under `server/game/Content/` and seeded by hash at startup; a game pins one `PropertySet` by hash and old games keep theirs.
- **No Protocol type is renamed and `ProtocolVersion` stays 1.** `ModelSlot.ArmyBook` is the one wire-visible addition: a trailing optional parameter, `string? ArmyBook = null`.
- **Every new field on an existing record is a trailing optional parameter** (`EffectDocument`, `EntryDocument`, `Effect`, `CatalogEntry`, `LiveProperty`, `PropertySetDocument`, `PropertySet`, `ModelSlot`), so every positional constructor in the tests keeps compiling (spec → *Files touched*).
- The spell's value, `catalog_entry.cost`, is Army Forge's `threshold`, never its `cost`.
- `catalog_entry.faction` is null on every generated entry (owner and manager decisions, 2026-10-02).
- A spell rolls dice for its hits exactly when its `generation` carries a non-empty `spellHitRoll`; the printed text wins.
- Never call Army Forge from a test. `./scripts/army-books fetch` is the only code path that does, run by hand.
- Schema changes are new files under `server/management/Persistence/Migrations/`; `001_schema.sql` is never edited. This plan adds `005_spell_library.sql`. Game-server Postgres tests read the compose database, so after Task 3 start management once (`./scripts/management`, stop it after the log reports the migration) to apply 005 there.
- Commit after every task, imperative one-line subject, ending with the `Co-Authored-By` trailer naming the model that produced it.

## Review Focus

- **A caster from a book the set does not list** (an army imported against a book OPR adds next month): no window opens and no error is raised. Task 1 pins it.
- **Two printings of one spell name that differ**: both entries survive composition with qualified names, and a cast by either book offers its own. Tasks 12 and 13 pin it.
- **A grant on a unit that is destroyed before it is consumed**: the grant goes with the unit; nothing reads a grant of a unit with no alive models. Task 6 pins that consumption never throws on a missing unit.
- **A morale test pushed by a spell at a unit already Shaken**: it fails without a roll (p.10), and the spell's on-failure children still apply. Task 10 pins it.
- **Regenerating the books with no Army Forge change**: an empty diff, byte for byte. Task 12 pins it.

---

## File Structure

```
server/game/Content/grimdark-future-core.properties.json     name becomes "Grimdark Future v3.5.1"
server/game/Content/grimdark-future-army-rules.part.json     the 73 army-book rules spells name (Task 11)
server/game/Content/army-books/<uid>.book.json               48 generated books (Task 13)
server/game/Properties/PropertySet.cs                        Books; new enum values; Effect.TargetTerrain/TargetCondition
server/game/Properties/PropertySetDocument.cs                BookRecord, BookDocument; validation of books and the three spell families
server/game/Properties/PropertiesSeeder.cs                   composition of base + parts + books
server/game/Properties/PostgresPropertiesStore.cs            book, book_spell, the two new effect columns
server/game/Effects/*.cs                                     grants as rows, consumption, Spell attack kind, predicates, wounds
server/game/Rules/GrimdarkFuture/*.cs                        the casting filter, spell hits, the four kinds, the idle sequence
server/management/Persistence/Migrations/005_spell_library.sql
server/management/Content/Armies/ArmyForgeList.cs, ArmyForgeImport.cs, ArmyRepository.cs
server/game/Persistence/PostgresGameRows.cs                  ReadArmyAsync reads army_book
libs/Tabletop.Content/Army.cs                                ModelSlot.ArmyBook
scripts/army-books                                           shell wrapper
scripts/ArmyBooks/                                           Tabletop.ArmyBooks: Program, ArmyForgeBook, SpellTranslator, TextCheck, BookWriter
tests/Tabletop.ArmyBooks.Tests/                              the generator's tests and trimmed fixture books
tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs, GrantRowTests.cs, SpellShapeTests.cs, GrammarAdditionTests.cs, ArmyRulesContentTests.cs
sim/Scenarios/*, sim/Program.cs, sim/SimOptions.cs, sim/Invariants/Invariants.cs
```

## Tasks

Each task is its own file, in build order.

1. [A caster casts its own book's spells](task-01-a-caster-casts-its-own-books-spells.md)
2. [One composed document](task-02-one-composed-document.md)
3. [The schema: migration 005, the new grammar fields, the store](task-03-the-schema-and-the-store.md)
4. [Import carries the book](task-04-import-carries-the-book.md)
5. [A grant is its effect row](task-05-a-grant-is-its-effect-row.md)
6. [A grant is consumed by the first step that would apply it](task-06-consumption.md)
7. [Spell hits: the roll sentence, the spell attack kind, a model target, the casting roll](task-07-spell-hits.md)
8. [Extra wounds](task-08-extra-wounds.md)
9. [Two predicates, the charge's range, the morale test's cause](task-09-predicates-range-and-cause.md)
10. [Four effect kinds and the idle activation](task-10-four-effect-kinds.md)
11. [The army-rules part](task-11-the-army-rules-part.md)
12. [The generator](task-12-the-generator.md)
13. [The 48 books, and every spell is cast](task-13-the-books-and-every-spell.md)
14. [The enforcement merge, and the simulator on real spells](task-14-merge-and-the-simulator.md)
15. [`CLAUDE.md` and the whole-tree check](task-15-claude-md-and-the-whole-tree.md)

## The enforcement merge

This plan and the [army-rule hooks](../2026-10-02-army-rule-hooks/README.md) plan run on one tree, branched from `main` at the start of Task 1. The enforcement tree runs concurrently on its own branch. **When the enforcement branch has landed on `main`, this tree merges `main` in at the start of Task 14, after Task 13's commit**, and not before. Tasks 1 to 13 edit only two files the enforcement tree also edits, `GfNames.cs` (constants) and `GfRules.Scheduling.cs` (Task 10's `ReadAnswer`), both textual; Task 14 is the first that edits the simulator files it also edits (`sim/Invariants/Invariants.cs`, `sim/Scenarios/ScenarioCatalog.cs`, the sim tests), so those edits come after the merge rather than before it. If the enforcement branch has not landed when Task 13 finishes, the tree waits there.

What the merge brings, and what Task 14 absorbs:

- `IRulesEngine.Advance` returns `Emit`, not a bare list. Every test in this plan advances the engine through `Sim` (`tests/.../Infrastructure/Sim.cs`) or `SchedulerHarness`, which the enforcement tree adapts itself, so no test written in Tasks 1 to 13 changes. A test that calls `RulesEngine.Advance` directly reads `.Events` after the merge; this plan writes none.
- A `strict` run that plays four rounds ends `ended` at round 4's boundary. The `casting` scenario's expectations and the `RunLoopTests` and `ProgramTests` lines Task 14 adds accept `ended`.
- Invariant 6 is enforcement's `NothingAdvancesAfterTheEnd`; this plan's is invariant 7, `SpellsComeFromTheCastersBook`.
- `wire.generated.ts` changes on both branches. After the merge, `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests` is run afresh and `cd viewer && npm run check` passes.
- Textual conflicts only in `GfNames.cs`, `GfRules.Scheduling.cs` (this tree's `ReadAnswer` beside enforcement's `JudgeAdvance`) and the migration number: if enforcement landed a `005_*.sql`, rename this tree's to the next free number and update `MigrationTests`.

---

## Interfaces → Produces

`IRL<T>` is `IReadOnlyList<T>`, `IRD<K,V>` is `IReadOnlyDictionary<K,V>`. The hooks plan consumes these verbatim.

| Type | Members | Task |
| --- | --- | --- |
| `ModelSlot` (Content) | trailing `string? ArmyBook = null` | 1 |
| `sealed record BookRecord(string Uid, string Name, string? Version, IRL<string> Spells)` (Properties) | | 1 |
| `PropertySetDocument` | trailing `IRL<BookRecord>? Books = null` | 1 |
| `PropertySet` | ctor trailing `IRL<BookRecord>? books = null`; `IRD<string, BookRecord> Books`; `IRL<CatalogEntry> SpellsOf(string? bookUid)` | 1 |
| `GfCasting.SpellOptions(GameState, PropertySet, Guid unit)` | filters by each caster slot's `ArmyBook` | 1 |
| `TestProperties` | `const string TestBook = "test-book"`; `WithSpells(params EntryDocument[])` lists them under `TestBook`; `Core` from `PropertiesSeeder.EmbeddedDocuments()` | 1, 2 |
| `sealed record BookDocument(string Uid, string Name, string? Version, string GameRules, IRL<EntryDocument> Spells)` | | 2 |
| `PropertiesSeeder` | `static PropertySetDocument Compose(PropertySetDocument core, IEnumerable<PropertySetDocument> parts, IEnumerable<BookDocument> books)`; `static IRL<PropertySetDocument> EmbeddedDocuments()` returns composed documents | 2 |
| `EffectKind` | `+ CountAsTerrain, TakeTest, SetCondition, Move` | 3 |
| `ConditionKind` | `+ HoldsProperty, MostModels` | 3 |
| `enum TargetTerrain { Difficult, Dangerous }` | | 3 |
| `Effect`, `EffectDocument` | trailing `TargetTerrain? TargetTerrain = null, string? TargetCondition = null` | 3 |
| `AfUnit` (management) | trailing `string? ArmyId = null` | 4 |
| `LiveProperty` | trailing `Effect? GrantRow = null`; `IRL<Effect> Effects` (the row when `GrantRow` is set, else `Entry.Effects`) | 5 |
| `LiveProperties.Gather` | drops a subject-opponent granted row whose opponent is off the caster's team | 5 |
| `LiveSet` | ctor trailing `HashSet<(Guid UnitId, Guid GrantId)>? read = null`; `void Read(LiveProperty)`; `IReadOnlyCollection<(Guid UnitId, Guid GrantId)> GrantsRead` | 6 |
| `RollEffects` | `static IRL<StateChange> Consume(LiveSet live)` (every grant read, with the rest of its spell's grants on that unit); `static void ReadPerDie(LiveSet, Side, string roll)`; `ConsumeGrants` removed | 6 |
| `AttackKind` | `+ Spell` | 7 |
| `GfAttack` | `static AttackKind KindOf(StepContext)`; `static IRL<(string Entry, int Amount, int Count)> GrantedOf(FrameView)`; `TakedownModel` reads a casting frame's model target | 7 |
| `GfRolls` | `CastingPlan(StepContext ctx, LiveSet live, int netTokens)` | 7 |
| `DieEffects` | `(int ExtraHits, int ExtraWounds, IRL<(string Entry, int? Parameter)> Grants)`; `ExtraWounds` are wounds the rolling side's unit takes | 8 |
| `GfAttack.SelfWoundsStep` | step `self-wounds`, last in `attack-sequence` and `spell-hits` | 8 |
| `GfMorale` | `static AttackKind? CauseOf(GameState, Guid unit)` | 9 |
| `GfCasting` | `static Sequence SpellTest()`; `GfMoves.Idle()`, `GfMoves.ForcedMoveRegion(StepContext, Guid, double)`, `GfMoves.ApplyForcedMove(StepContext, Guid, double, IRL<ModelRelocation>) → (IRL<StateChange>, IReadOnlyDictionary<string, int>)`, both reading the target's own terrain rules (`GfMoves.TerrainFor`) | 10 |
| `ScenarioArmy` (sim) | trailing `string? ArmyBook = null`; `Scenario.Spells`, `ScenarioBuilder.Spells`, `ScenarioCompiler.PropertiesFor` removed | 14 |
| `Invariants` (sim) | `SpellsComeFromTheCastersBook(IRL<GameEvent>, CompiledScenario)`, the seventh | 14 |
| `GfNames` | `IdleSeq = "idle"`, `SpellTestSeq = "spell-test"`, `CounterAttack = "Counter-Attack"`, `Unwieldy = "Unwieldy"`, `ChargeRangeKey = "charge_range"` | 9, 10, 11 |
| `Tabletop.ArmyBooks` | `SpellTranslator.Translate(AfBook, AfSpell) → EntryDocument`; `TextCheck.Problems(AfSpell, EntryDocument)`; `BookWriter.Build(IRL<AfBook>) → IRL<BookDocument>`, `Check`, `Serialize`, `Write` | 12 |

## Interfaces needed from other modules

1. **Enforcement tree** — `Emit` and `RunOutcome.Ended`, consumed in Task 14 only, as *The enforcement merge* says.
2. **Interpreter plan** — `Sim`, `ScriptedDice`, `WithSequences`, `Armies`, `TestArmies` in `tests/Tabletop.GameServer.Tests/Infrastructure/`, as they are on `main`.

## Rulings

1. **Test units carry `TestProperties.TestBook` by default.** `Armies.Unit` and `TestArmies.Build` give every slot `ArmyBook = TestProperties.TestBook`, and `WithSpells` lists its spells under that book. So every existing casting test keeps its meaning with no edit, a caster against `TestProperties.Core` (whose books never include `test-book`) is still offered nothing, and the tests that need a real book use `Armies.InBook(unit, uid)`. This is why `GfRollsTests`, `OutOfSetAnswerTests`, `RoundBoundaryTests`, `CommandDispatcherTests` and `GrimdarkFutureRulesTests` need no change beyond what Task 1 lists.
2. **Composition dedupes by canonical form.** Two entries with one name are one entry when `JsonSerializer.Serialize(entry, PropertiesJson.Options)` is equal, else `InvalidDataException`. The generator guarantees equal reprints serialise equally (Task 12).
3. **`charge_range` is in hundredths of an inch**, an `int` in the `charge-target` step's values, because `StepCompleted.Values` holds integers.
4. **The idle activation runs a sequence.** `ReadAnswer` gives Idle `RunsSequence: true` and `ActionSequence("Idle")` names `idle`, which is one step, `GfMoves.ActivationTerrainStep`. `GfHooks.OnTurnEnded` still reads `act.Action == ActivationOption.Idle`.

## Self-review

**Spec coverage.** *A caster casts its own book's spells* → Tasks 1, 4. *One set holds every book* → Task 2. *The generator* → Task 12. *Translation* (all sixteen rows, the roll sentence, the model target, *against*) → Tasks 7, 12, 13. *Text and generation* → Task 12. *A grant is its effect row* → Tasks 5, 6. *Grammar additions* → Tasks 7 (`spell`, casting roll), 8 (`add_hit` wounds), 9 (predicates, charge range, morale cause), 10 (four kinds, idle). *Army-book rules spells name* → Task 11. *Schema and wire* → Tasks 3, 4, 14. *Testing* → each task, Task 13's theory and Task 14's simulator. *Against the enforcement tree* → *The enforcement merge* and Task 14.

**Placeholders.** None: every code step carries its code; Task 11's JSON lists every entry; Task 13's book files are generated by Task 12's tool from the local dump, and the plan says where the dump is and how to fetch it.

**Type consistency.** `BookRecord`, `BookDocument`, `LiveProperty.GrantRow`, `DieEffects`' four members, `TargetTerrain` and the `GfNames` constants are spelled the same in every task that uses them.
