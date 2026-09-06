# Interpreter and Effects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build, in `server/game/` only, the rules engine's resolution half: the pinned `Properties` set loaded from the eight tables (and the seeding step that puts authored content into them), the roll primitive over a seeded RNG that is game state, the effect grammar's two passes, the resolution stack interpreter (frames rebuilt from the log; steps that complete, ask, push or open a window; confirm gates; the sealed bid), the one `IRulesEngine` implementation shared with the scheduler plan, and Grimdark Future's sequences — the four actions, shooting and melee over the shared attack-sequence → resolve-hits → deal-wounds, casting, morale, dangerous terrain, and containment (embarking and disembarking as part of a move, and a destroyed container disgorging what it held) — each tested headlessly against the rulebook.

**Architecture:** `Properties` is typed C# (`PropertySet`) built from rows; a `PropertySetDocument` is the authored JSON form, content-addressed by a canonical hash and seeded as rows once. The interpreter is generic: it reads the top frame's position and the log since that frame's last completed step (its *history*), runs the `IStep` the ruleset supplies, and turns the outcome into payloads — `step_completed`, `ask_opened`, `sequence_started`, `sequence_finished`. It never appends; `RulesEngine.Advance` returns payloads and the session appends them. Dice are a counter-based generator: the seed derives from the game id and the cursor is the number of dice drawn in the effective log, so replay and undo reproduce the same dice without storing RNG state. `GrimdarkFutureRules` supplies sequences, steps, roll conventions, threshold functions, hooks and name checks; nothing under `Interpreter/`, `Effects/`, `Dice/` or `Engine/` names an OPR rule.

**Tech Stack:** .NET 10 (`net10.0`, C# 12), Npgsql 10.0.3 (the skeleton's), xUnit 2.9.3; `Tabletop.Geometry` (geometry plan), `Tabletop.Protocol` and `Tabletop.Content` (contracts plan); the game server skeleton's `IGameRules`, `IRulesEngine`, `GameState`, `CommandDispatcher`, `InMemoryEventLog`, `PostgresFixture`.

**Spec:** `docs/superpowers/specs/2026-09-03-action-sequences-design.md` (primary); `2026-08-28-resolution-design.md`; `2026-08-29-effect-grammar-design.md`; `2026-09-04-spell-effects-design.md`; `2026-08-29-content-values-design.md`; `2026-08-29-identity-and-enumerations-design.md`; `2026-08-28-game-flow-design.md`; `2026-09-04-scheduler-design.md` (the seam only); `2026-09-04-whole-design-review.md` (newest, wins); `docs/schema.md` (the eight `Properties` tables, the roll row); `docs/architecture.md` → Command / Event spine, Undo, Abilities, Rules as data. Rulebook: Grimdark Future v3.4.1 pages 7, 8–10, 11, 13–14, 20, 29–34, read for this plan. Earlier plans obeyed verbatim: `docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md`, `2026-09-05-game-server-skeleton.md` (its *Interfaces needed → Scheduler and interpreter plans* is binding), `2026-09-05-management-api.md` (`GET /v1/properties`, the `properties` rows), `2026-09-05-geometry.md` (`Distance`, `LineOfSight`, `Cover`, `Movement`, `Bodies`, `Containment`, `Settling`).

## Global Constraints

- `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`, root namespace `Tabletop.GameServer`; `obj/` under `obj/$(OS)/`; SDK `10.0.400`.
- Everything here lives in `server/game/`. Nothing is added to `libs/`; no rules type reaches `client/` (`architecture.md` → What is actually shared; CLAUDE.md).
- The skeleton's seam is extended, never replaced: members are added to `IGameRules` and implemented on `GrimdarkFutureRules`; `IRulesEngine`, `GameState`, `EngineResult`, `CommandDispatcher` keep their signatures; the one addition to `GameState` is the RNG seed (skeleton → Interfaces needed 2).
- One `IRulesEngine` implementation, `Engine.RulesEngine`, registered by `RulesEngineFactory` in place of `InertEngineFactory`. The scheduler plan plugs in through `Engine.IScheduler` (Task 10) and does not implement `IRulesEngine` itself. The one `IGameRules` is the skeleton's file; this plan adds its members (Task 8) beside the scheduler plan's — the merged interface is written in full in the skeleton plan → Interfaces → Produces → `Tabletop.GameServer.Rules`.
- The engine returns payloads and never appends or broadcasts. The interpreter never emits `ask_answered` or `ask_closed` (the dispatcher does; the scheduler emits one `ask_closed` with no answers for an unframed Ask of its own superseded by a menu edit) and reads `ask_closed` from the log to resume. It reads `state.Projection` and the effective log on every call and holds no state of its own between calls (scheduler → Nothing is an invariant; skeleton Task 6).
- Replay applies recorded results and never re-computes: a roll's threshold, modifiers and per-die raw and modified values are in the `RollRecord`; a step's settled parameters are in `StepCompleted.Values` (Interfaces needed 1). Nothing in a step's history is re-derived from `GameRules` (action sequences → The resolution stack).
- A step holds no C# locals across a suspension: every `IStep.Run` reads its position, values and history from the log and returns one outcome (action sequences → Why a stack and not a call stack).
- Only OPR's `GameRules` is built; 40k constrains interfaces (action sequences → What the interface must not assume). Nothing under `Interpreter/`, `Effects/`, `Dice/`, `Engine/` or `Properties/` may name a stat, roll, action, condition or catalog entry; Task 18 tests it.
- Enumeration, not validation: every Ask carries a server-computed legal set; legality of an answer is the dispatcher's (`LegalSetMembership` + `EnforcementPolicy`). The engine judges only its own commands (`RerollDice`, `SetDice`, `AddModifier`).
- Identifiers are `Guid.CreateVersion7()`; one world unit = one inch; geometry constants (1", 2"/4", 3", 6", 9", 12", 18") come from `GrimdarkFutureRules`, never from a library (geometry plan → Interfaces needed).
- Local only; Postgres from `docker-compose.yml`; the store tests use `[Collection("Postgres")]` and the skeleton's `PostgresFixture`. No hosting.
- Test-driven: every task starts with a failing xUnit test against an in-memory log and in-memory `Properties`; every OPR sequence has a test from the rulebook's worked example or a constructed one with scripted dice.
- Commit after every task, one-line imperative message, with the `Co-Authored-By` and `Claude-Session` trailers the session requires.

---

## File Structure

```
server/game/
  Tabletop.GameServer.csproj                      modified: embed Content/*.properties.json
  Program.cs                                      modified: IPropertiesStore, IDiceSource, ISchedulerFactory, RulesEngineFactory, seeding at startup
  Content/grimdark-future-core.properties.json    the OPR core Properties, authored in-repo
  Properties/PropertySet.cs                       the typed in-memory set: the eight tables' shape
  Properties/PropertySetDocument.cs               the authored file format, validation, canonical hash, ToSet
  Properties/IPropertiesStore.cs                  LoadAsync / SeedAsync; InMemoryPropertiesStore
  Properties/PostgresPropertiesStore.cs           reads and writes the eight tables
  Properties/PropertiesSeeder.cs                  embedded documents → rows, idempotent by hash
  Dice/IDiceSource.cs                             IDiceSource, DiceSource (counter-based), ScriptedDice
  Dice/DiceCursor.cs                              dice drawn so far, counted from the effective log
  Dice/RollPlan.cs                                RollConvention, RollPlan, RollOutcome (reading a record back)
  Dice/Roller.cs                                  RollPlan → RollRecord, plus forced rerolls as dice_rerolled
  Rules/IRulesEngine.cs                           modified: GameState.RngSeed
  Effects/Interaction.cs                          Party, AttackKind, Interaction
  Effects/Conditions.cs                           the eight predicates
  Effects/LiveProperties.cs                       pass one: gather, aggregate, grant/suppress/modify-parameter → LiveSet
  Effects/RollEffects.cs                          pass two for one roll → RollPlan adjustments and per-die effects
  Effects/StatReader.cs                           stats and action distances under modify_stat / replace_stat
  Effects/Lookup.cs                               name checks over unit, models and the grants store
  Interpreter/Steps.cs                            IStep, StepOutcome union, Sequence
  Interpreter/FrameReader.cs                      FrameView, StepHistory from Projection + effective log
  Interpreter/StepContext.cs                      what a step sees
  Interpreter/SequenceInterpreter.cs              Advance over the top frame; confirm gates; Start
  Interpreter/Windows.cs                          Reaction, WindowRunner (offered and invoked windows, sealed bid)
  Interpreter/Battlefield.cs                      PlacedModel from ModelInPlay, terrain volumes, LoS, range, cover
  Engine/IScheduler.cs                            the scheduler seam: IScheduler, ISchedulerFactory, NoScheduler
  Engine/RulesEngine.cs                           IRulesEngine: Judge/Execute dice commands; Advance = interpreter, then scheduler
  Engine/RulesEngineFactory.cs
  Rules/IGameRules.cs                             modified: the interpreter's members
  Rules/GrimdarkFuture/GfRules.Sequences.cs       this plan's partial of GrimdarkFutureRules (the skeleton's GfRules.cs declares RulesId/Instance/Id): sequence catalog, RollSpec, hooks
  Rules/GrimdarkFuture/GfNames.cs                 the stat, action, roll, condition and entry names
  Rules/GrimdarkFuture/GfRolls.cs                 conventions, threshold functions, cover, fatigue, roll-off
  Rules/GrimdarkFuture/GfHooks.cs                 round/turn hooks, end-of-turn morale, reactions at windows
  Rules/GrimdarkFuture/GfMoves.cs                 move step with the casting checkpoint, embarking and disembarking, dangerous terrain, the four actions
  Rules/GrimdarkFuture/GfAttack.cs                attack-sequence, resolve-hits, deal-wounds (assign, then disgorge)
  Rules/GrimdarkFuture/GfShooting.cs              shooting: weapon groups and targets
  Rules/GrimdarkFuture/GfMelee.cs                 charge and melee
  Rules/GrimdarkFuture/GfMorale.cs                morale test
  Rules/GrimdarkFuture/GfCasting.cs               casting: spell, sealed bid, roll, targets, effects
  Rules/GrimdarkFuture/GfContainers.cs            a destroyed container: the test, Shaken, placement within 6", then removal
tests/Tabletop.GameServer.Tests/
  Infrastructure/TestProperties.cs                the core set from the embedded document; a test spell book
  Infrastructure/Sim.cs                           a playing game over InMemoryEventLog with scripted dice, Advance + Answer loop
  PropertySetTests.cs  PropertySetDocumentTests.cs  CoreContentTests.cs  PropertiesStoreTests.cs  DiceTests.cs  LivePropertiesTests.cs  RollEffectsTests.cs
  InterpreterTests.cs  WindowTests.cs  RulesEngineTests.cs  GfRollsTests.cs  GfDealWoundsTests.cs
  GfAttackTests.cs  GfShootingTests.cs  GfMeleeTests.cs  GfMoraleTests.cs  GfCastingTests.cs  GfContainersTests.cs  InterfaceConstraintTests.cs
```

`Properties/`, `Dice/`, `Effects/`, `Interpreter/` and `Engine/` are ruleset-free. `Rules/GrimdarkFuture/` is the only place OPR's names appear. Tests reuse the skeleton's `RecordingClient`, `FixedAskEngine` and `PostgresFixture`.

---
### Task 1: The typed `Properties` set

**Files:**
- Create: `server/game/Properties/PropertySet.cs`
- Test: `tests/Tabletop.GameServer.Tests/PropertySetTests.cs`

**Interfaces:**
- Consumes: `Tabletop.Content.StatKind`.
- Produces (namespace `Tabletop.GameServer.Properties`): `enum StatLevel { Unit, Model, Weapon }`; `StatDefinition(StatLevel Level, string Name, StatKind Kind)`; `StatRef(StatLevel Level, string Name)`; `ActionDefinition(string Name, double Distance)`; `RollDefinition(string Name)`; `enum CatalogKind { Unit, Model, Weapon, Spell }`; `enum Aggregation { All, Any, Majority, None }`; `enum TargetSide { Friendly, Enemy }`; `enum TargetGrain { Unit, Model }`; `SpellTargeting(TargetSide Side, TargetGrain Grain, int Count, double Range)`; `CatalogEntry(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction, string RuleText, SpellTargeting? Targeting, IRL<Effect> Effects)`; `enum EffectSubject { Self, Opponent }`; `enum EffectKind { ModifyRoll, ModifyStat, ReplaceStat, ForceReroll, Roll, AddHit, Multiply, OverrideResult, GrantProperty, SuppressProperty, ModifyParameter, SuppressModifiers }`; `enum TargetQuantity { Hits, Wounds }`; `enum TargetResult { Wound, MoraleTest }`; `enum TargetModifier { Cover, ToHitPenalties, RangePenalties }`; `enum CapKind { ModelsInTargetUnit }`; `enum EffectDuration { Interaction, Once }`; `Effect(Guid Id, EffectSubject Subject, int Ordinal, EffectKind Kind, string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction, TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier, int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind, string? Replacement, EffectDuration Duration, IRL<EffectCondition> Conditions, IRL<Effect> Children)` with `int EffectiveAmount(int? parameter)`; `enum ConditionKind { ActionTaken, AttackKind, Range, RollResult, ActorState, PositionTerrain, RoundNumber, UsageCount }`; `enum Comparison { Eq, Neq, Gte, Lte, Gt, Lt }`; `EffectCondition(Guid Id, ConditionKind Kind, EffectSubject? Subject, Comparison Comparison, int? Number, IRL<string> Values)` with `bool Compare(int actual)`; `sealed class PropertySet(string ContentHash, string Name, string GameRules, IRL<StatDefinition> Stats, IRL<ActionDefinition> Actions, IRL<RollDefinition> Rolls, IRL<CatalogEntry> Entries)` with `IRD<string, CatalogEntry> ByName`, `CatalogEntry? Entry(string)`, `StatDefinition? Stat(StatLevel, string)`, `ActionDefinition Action(string)` (throws `KeyNotFoundException`), `bool HasRoll(string)`, `IEnumerable<Effect> AllEffects()` (children included, depth first).
- Every column of the eight tables in `schema.md` has a member here; every closed set is an enum whose wire spelling is the schema's check-constraint value under `ContentJson`'s snake_case policy (`modify_roll`, `models_in_target_unit`, `target_number`).

Spec: `schema.md` → `Properties` is eight tables (every column), One `effect` table not twelve (`amount` signed, `amount_from_parameter`, three one-kind columns, `parent_effect_id` nesting, `duration`), A condition is a row and its values are rows (`neq`; a set of values); effect grammar → An effect, Conditions, The twelve effect kinds, `Roll` nests; identity and enumerations → The closed sets; spell effects → Target selection, Faction rules are catalog entries.

- [ ] **Step 1: Write the failing test**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests;

public class PropertySetTests
{
    public static Effect ModifyRoll(string roll, int amount, bool fromParameter = false, EffectSubject subject = EffectSubject.Opponent, int ordinal = 10, params EffectCondition[] conditions) =>
        new(Guid.CreateVersion7(), subject, ordinal, EffectKind.ModifyRoll, roll, null, null, null, null, null, null, amount, fromParameter, null, null, null, EffectDuration.Interaction, conditions, []);

    public static PropertySet Tiny() => new("sha256:tiny", "Tiny", "grimdark-future",
        [new StatDefinition(StatLevel.Unit, "Quality", StatKind.TargetNumber), new StatDefinition(StatLevel.Weapon, "Range", StatKind.Distance)],
        [new ActionDefinition("Hold", 0), new ActionDefinition("Advance", 6)],
        [new RollDefinition("hit"), new RollDefinition("defense")],
        [new CatalogEntry("AP", CatalogKind.Weapon, true, null, null, null, "Targets get -X to Defense rolls.", null, [ModifyRoll("defense", -1, fromParameter: true)])]);

    [Fact]
    public void Lookups_answer_by_name_and_level()
    {
        var set = Tiny();
        Assert.Equal(6, set.Action("Advance").Distance);
        Assert.Throws<KeyNotFoundException>(() => set.Action("Rush"));
        Assert.Equal(StatKind.Distance, set.Stat(StatLevel.Weapon, "Range")!.Kind);
        Assert.Null(set.Stat(StatLevel.Unit, "Range"));
        Assert.True(set.HasRoll("hit")); Assert.False(set.HasRoll("save"));
        Assert.Equal(-3, set.Entry("AP")!.Effects[0].EffectiveAmount(3));
        Assert.Equal(-1, set.Entry("AP")!.Effects[0] with { AmountFromParameter = false } is var e ? e.EffectiveAmount(3) : 0);
    }

    [Fact]
    public void Comparison_covers_the_six_operators_and_neq_tests_absence()
    {
        EffectCondition C(Comparison c) => new(Guid.CreateVersion7(), ConditionKind.Range, null, c, 9, []);
        Assert.True(C(Comparison.Gt).Compare(10)); Assert.False(C(Comparison.Gt).Compare(9));
        Assert.True(C(Comparison.Gte).Compare(9)); Assert.True(C(Comparison.Lt).Compare(8)); Assert.True(C(Comparison.Lte).Compare(9));
        Assert.True(C(Comparison.Eq).Compare(9)); Assert.True(C(Comparison.Neq).Compare(0));
    }

    [Fact]
    public void Enums_spell_as_the_schema_does()
    {
        Assert.Equal("\"modify_roll\"", ContentJson.Serialize(EffectKind.ModifyRoll));
        Assert.Equal("\"models_in_target_unit\"", ContentJson.Serialize(CapKind.ModelsInTargetUnit));
        Assert.Equal("\"to_hit_penalties\"", ContentJson.Serialize(TargetModifier.ToHitPenalties));
        Assert.Equal(ConditionKind.PositionTerrain, ContentJson.Deserialize<ConditionKind>("\"position_terrain\""));
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.GameServer.Tests --filter PropertySetTests`; expect compile errors.**

- [ ] **Step 3: Write `PropertySet.cs`**

```csharp
using Tabletop.Content;

namespace Tabletop.GameServer.Properties;

public enum StatLevel { Unit, Model, Weapon }
public sealed record StatDefinition(StatLevel Level, string Name, StatKind Kind);
/// <summary>stat_definition's key is (level, name), so a reference to a stat carries both (contracts plan spec correction 6).</summary>
public sealed record StatRef(StatLevel Level, string Name);
/// <summary>An action row: a name and a distance, because Fast and Slow need something to point at (schema.md).</summary>
public sealed record ActionDefinition(string Name, double Distance);
/// <summary>A roll row: a name and nothing else. Convention, threshold and cap are GameRules'.</summary>
public sealed record RollDefinition(string Name);

public enum CatalogKind { Unit, Model, Weapon, Spell }
public enum Aggregation { All, Any, Majority, None }
public enum TargetSide { Friendly, Enemy }
public enum TargetGrain { Unit, Model }
public sealed record SpellTargeting(TargetSide Side, TargetGrain Grain, int Count, double Range);

/// <summary>One named rule. Effects may be empty: then it is a name GameRules checks for at a hook it owns.</summary>
public sealed record CatalogEntry(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction,
    string RuleText, SpellTargeting? Targeting, IReadOnlyList<Effect> Effects);

public enum EffectSubject { Self, Opponent }
public enum EffectKind { ModifyRoll, ModifyStat, ReplaceStat, ForceReroll, Roll, AddHit, Multiply, OverrideResult, GrantProperty, SuppressProperty, ModifyParameter, SuppressModifiers }
public enum TargetQuantity { Hits, Wounds }
public enum TargetResult { Wound, MoraleTest }
public enum TargetModifier { Cover, ToHitPenalties, RangePenalties }
public enum CapKind { ModelsInTargetUnit }
public enum EffectDuration { Interaction, Once }

/// <summary>One effect row with its conditions and nested children (Roll's per-success effect, a hit's granted rule).</summary>
public sealed record Effect(
    Guid Id, EffectSubject Subject, int Ordinal, EffectKind Kind,
    string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction,
    TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier,
    int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind, string? Replacement, EffectDuration Duration,
    IReadOnlyList<EffectCondition> Conditions, IReadOnlyList<Effect> Children)
{
    /// <summary>The signed amount: when it comes from the parameter, Amount is the sign (schema.md → amount is signed).</summary>
    public int EffectiveAmount(int? parameter) => AmountFromParameter ? Math.Sign(Amount ?? 1) * (parameter ?? 0) : Amount ?? 1;
}

public enum ConditionKind { ActionTaken, AttackKind, Range, RollResult, ActorState, PositionTerrain, RoundNumber, UsageCount }
public enum Comparison { Eq, Neq, Gte, Lte, Gt, Lt }

/// <summary>A predicate. Relational kinds take no subject; Values is a set (Rush/Charge is membership, not or).</summary>
public sealed record EffectCondition(Guid Id, ConditionKind Kind, EffectSubject? Subject, Comparison Comparison, int? Number, IReadOnlyList<string> Values)
{
    public bool Compare(int actual) => Comparison switch
    {
        Comparison.Eq => actual == Number, Comparison.Neq => actual != Number, Comparison.Gte => actual >= Number,
        Comparison.Lte => actual <= Number, Comparison.Gt => actual > Number, Comparison.Lt => actual < Number, _ => false,
    };
}

/// <summary>The pinned data half of a ruleset, in memory. Addressed by its content hash; a game pins one.</summary>
public sealed class PropertySet(string contentHash, string name, string gameRules, IReadOnlyList<StatDefinition> stats,
    IReadOnlyList<ActionDefinition> actions, IReadOnlyList<RollDefinition> rolls, IReadOnlyList<CatalogEntry> entries)
{
    public string ContentHash { get; } = contentHash;
    public string Name { get; } = name;
    public string GameRules { get; } = gameRules;
    public IReadOnlyList<StatDefinition> Stats { get; } = stats;
    public IReadOnlyList<ActionDefinition> Actions { get; } = actions;
    public IReadOnlyList<RollDefinition> Rolls { get; } = rolls;
    public IReadOnlyList<CatalogEntry> Entries { get; } = entries;
    public IReadOnlyDictionary<string, CatalogEntry> ByName { get; } = entries.ToDictionary(e => e.Name);

    public CatalogEntry? Entry(string name) => ByName.GetValueOrDefault(name);
    public StatDefinition? Stat(StatLevel level, string name) => Stats.FirstOrDefault(s => s.Level == level && s.Name == name);
    public ActionDefinition Action(string name) => Actions.FirstOrDefault(a => a.Name == name) ?? throw new KeyNotFoundException($"No action '{name}' in {ContentHash}.");
    public bool HasRoll(string name) => Rolls.Any(r => r.Name == name);
    public IEnumerable<Effect> AllEffects() => Entries.SelectMany(e => e.Effects).SelectMany(Flatten);
    static IEnumerable<Effect> Flatten(Effect e) => new[] { e }.Concat(e.Children.SelectMany(Flatten));
}
```

- [ ] **Step 4: Run; expect 3 passed. Commit: `Define the typed Properties set`.**

### Task 2: The authored document — validation, canonical hash, `ToSet`

**Files:**
- Create: `server/game/Properties/PropertySetDocument.cs`
- Test: `tests/Tabletop.GameServer.Tests/PropertySetDocumentTests.cs`

**Interfaces:**
- Consumes: Task 1; `Tabletop.Content.ContentJson`.
- Produces: `PropertySetDocument(string Name, string GameRules, IRL<StatDefinition> Stats, IRL<ActionDefinition> Actions, IRL<string> Rolls, IRL<EntryDocument> Entries)`; `EntryDocument(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction, string RuleText, SpellTargeting? Targeting, IRL<EffectDocument> Effects)`; `EffectDocument(EffectSubject Subject, int Ordinal, EffectKind Kind, string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction, TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier, int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind, string? Replacement, EffectDuration Duration, IRL<ConditionDocument> Conditions, IRL<EffectDocument> Children)`; `ConditionDocument(ConditionKind Kind, EffectSubject? Subject, Comparison Comparison, int? Number, IRL<string> Values)`; `static class PropertiesJson` with `PropertySetDocument Parse(string json)`, `string Canonical(PropertySetDocument)` (sorted, camelCase, no ids), `string Hash(PropertySetDocument)` (`"sha256:" + 64 lowercase hex over the canonical form`), `IReadOnlyList<string> Validate(PropertySetDocument)` (every reference resolves, every per-kind check of `schema.md` holds, and every top-level effect on a `spell` entry is either `add_hit` or `duration = once`; empty when valid), `PropertySet ToSet(PropertySetDocument, Func<Guid> newId)` (throws `InvalidDataException` listing the validation failures).
- The document has no ids: effect and condition ids are minted when the rows are inserted (Task 4) and read back with them, so `Grant.EffectId` is stable within a database. The hash covers content only, so the same file seeds the same hash on every machine.

Spec: `architecture.md` → Rules as data ("how authored content reaches those tables … is the one part of this section no design doc has touched"), Pinning ("content-addressed"); `schema.md` → `Properties` is eight tables ("pinning by content hash now needs a canonical serialisation to hash"), The catalog's per-kind check, Per-kind `CHECK` constraints on `effect`; spell effects → Duration ("always with one duration: **once**"), Damage spells need nothing new (kind 6). Spec correction 1 and question 1 record the mechanism.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests;

public class PropertySetDocumentTests
{
    public const string ApJson = """
    { "name": "Doc", "gameRules": "grimdark-future",
      "stats": [ { "level": "unit", "name": "Quality", "kind": "target_number" }, { "level": "unit", "name": "Defense", "kind": "target_number" } ],
      "actions": [ { "name": "Hold", "distance": 0 }, { "name": "Advance", "distance": 6 } ],
      "rolls": [ "hit", "defense" ],
      "entries": [
        { "name": "Regeneration", "kind": "model", "aggregation": "none", "ruleText": "When taking a wound, roll one die. On a 5+ it is ignored.",
          "effects": [ { "subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 5,
                         "children": [ { "subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound" } ] } ] },
        { "name": "Poison", "kind": "weapon", "ruleText": "Ignores Regeneration; re-roll unmodified Defense rolls of 6.",
          "effects": [ { "subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense",
                         "conditions": [ { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "defense", "raw" ] } ] },
                       { "subject": "opponent", "ordinal": 5, "kind": "suppress_property", "targetEntry": "Regeneration" } ] }
      ] }
    """;

    [Fact]
    public void Parses_hashes_canonically_and_builds_a_set()
    {
        var doc = PropertiesJson.Parse(ApJson);
        var reordered = doc with { Entries = doc.Entries.Reverse().ToList(), Rolls = doc.Rolls.Reverse().ToList() };
        Assert.Equal(PropertiesJson.Hash(doc), PropertiesJson.Hash(reordered));
        Assert.Matches("^sha256:[0-9a-f]{64}$", PropertiesJson.Hash(doc));
        Assert.NotEqual(PropertiesJson.Hash(doc), PropertiesJson.Hash(doc with { Name = "Other" }));
        var set = PropertiesJson.ToSet(doc, Guid.CreateVersion7);
        Assert.Equal(PropertiesJson.Hash(doc), set.ContentHash);
        var poison = set.Entry("Poison")!;
        Assert.Equal(2, poison.Effects.Count);
        Assert.Equal(["defense", "raw"], poison.Effects[0].Conditions[0].Values);
        Assert.Equal(EffectKind.OverrideResult, set.Entry("Regeneration")!.Effects[0].Children[0].Kind);
        Assert.Equal(3, set.AllEffects().Count());
        Assert.NotEqual(Guid.Empty, poison.Effects[0].Id);
        Assert.All(set.AllEffects(), e => Assert.Equal(EffectDuration.Interaction, e.Duration));
    }

    [Fact]
    public void Validation_checks_every_reference_and_per_kind_rule()
    {
        var doc = PropertiesJson.Parse(ApJson);
        Assert.Empty(PropertiesJson.Validate(doc));
        var e0 = doc.Entries[1].Effects[0];
        var bad = doc with { Entries = [
            doc.Entries[0] with { Aggregation = null },                                                    // model needs aggregation
            doc.Entries[1] with { Effects = [ e0 with { TargetRoll = "save" }, e0 with { Kind = EffectKind.Multiply, Threshold = 2 } ] },
            new EntryDocument("Bolt", CatalogKind.Spell, true, null, null, "Book", "x", null, []),        // spell: no parameter, needs cost + targeting
            new EntryDocument("Ghost", CatalogKind.Weapon, false, null, null, null, "x", null,
                [ new EffectDocument(EffectSubject.Self, 0, EffectKind.SuppressProperty, null, null, "Nobody", null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, [], []) ]) ] };
        var problems = PropertiesJson.Validate(bad);
        Assert.Contains(problems, p => p.Contains("Regeneration") && p.Contains("aggregation"));
        Assert.Contains(problems, p => p.Contains("save"));
        Assert.Contains(problems, p => p.Contains("threshold") && p.Contains("Poison"));
        Assert.Contains(problems, p => p.Contains("Bolt") && p.Contains("cost"));
        Assert.Contains(problems, p => p.Contains("Bolt") && p.Contains("parameter"));
        Assert.Contains(problems, p => p.Contains("Nobody"));
        Assert.Throws<InvalidDataException>(() => PropertiesJson.ToSet(bad, Guid.CreateVersion7));
    }

    [Fact]
    public void A_spells_effect_is_hits_or_a_once_grant_and_nothing_else()
    {
        var doc = PropertiesJson.Parse(ApJson);
        EntryDocument Spell(EffectKind kind, EffectDuration duration) => new("Bolt", CatalogKind.Spell, false, null, 2, "Book", "x",
            new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12),
            [new EffectDocument(EffectSubject.Self, 10, kind, null, null, kind == EffectKind.GrantProperty ? "Regeneration" : null, null, null, null, null, 3, false, null, null, null, duration, [], [])]);
        Assert.Empty(PropertiesJson.Validate(doc with { Entries = [.. doc.Entries, Spell(EffectKind.AddHit, EffectDuration.Interaction)] }));
        Assert.Empty(PropertiesJson.Validate(doc with { Entries = [.. doc.Entries, Spell(EffectKind.GrantProperty, EffectDuration.Once)] }));
        Assert.Contains(PropertiesJson.Validate(doc with { Entries = [.. doc.Entries, Spell(EffectKind.GrantProperty, EffectDuration.Interaction)] }),
            p => p.Contains("Bolt") && p.Contains("duration"));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `PropertySetDocument.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tabletop.Content;

namespace Tabletop.GameServer.Properties;

// The authored form: the eight tables without ids or the hash. Field names are the tables' columns in camelCase.
public sealed record PropertySetDocument(string Name, string GameRules, IReadOnlyList<StatDefinition> Stats, IReadOnlyList<ActionDefinition> Actions,
    IReadOnlyList<string> Rolls, IReadOnlyList<EntryDocument> Entries);
public sealed record EntryDocument(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction, string RuleText,
    SpellTargeting? Targeting, IReadOnlyList<EffectDocument> Effects);
public sealed record EffectDocument(EffectSubject Subject, int Ordinal, EffectKind Kind, string? TargetRoll, StatRef? TargetStat, string? TargetEntry, string? TargetAction,
    TargetQuantity? TargetQuantity, TargetResult? TargetResult, TargetModifier? TargetModifier, int? Amount, bool AmountFromParameter, int? Threshold, CapKind? CapKind,
    string? Replacement, EffectDuration Duration, IReadOnlyList<ConditionDocument> Conditions, IReadOnlyList<EffectDocument> Children);
public sealed record ConditionDocument(ConditionKind Kind, EffectSubject? Subject, Comparison Comparison, int? Number, IReadOnlyList<string> Values);

public static class PropertiesJson
{
    static readonly JsonSerializerOptions Options = ContentJson.Configure(new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });

    public static PropertySetDocument Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<PropertySetDocument>(json, Options) ?? throw new JsonException("empty document");
        // Omitted lists are empty lists, omitted duration is interaction, omitted amountFromParameter is false: the schema's defaults.
        return doc with
        {
            Stats = doc.Stats ?? [], Actions = doc.Actions ?? [], Rolls = doc.Rolls ?? [],
            Entries = (doc.Entries ?? []).Select(e => e with { Effects = Fix(e.Effects) }).ToList(),
        };
        static IReadOnlyList<EffectDocument> Fix(IReadOnlyList<EffectDocument>? effects) =>
            (effects ?? []).Select(f => f with { Conditions = (f.Conditions ?? []).Select(c => c with { Values = c.Values ?? [] }).ToList(), Children = Fix(f.Children) }).ToList();
    }

    /// <summary>Sorted where order carries no meaning (stats, actions, rolls, entries); effect and condition order is the author's and is kept.</summary>
    public static string Canonical(PropertySetDocument d) => JsonSerializer.Serialize(d with
    {
        Stats = d.Stats.OrderBy(s => s.Level).ThenBy(s => s.Name, StringComparer.Ordinal).ToList(),
        Actions = d.Actions.OrderBy(a => a.Name, StringComparer.Ordinal).ToList(),
        Rolls = d.Rolls.OrderBy(r => r, StringComparer.Ordinal).ToList(),
        Entries = d.Entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList(),
    }, Options);

    public static string Hash(PropertySetDocument d) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(d))));

    public static IReadOnlyList<string> Validate(PropertySetDocument d)
    {
        var problems = new List<string>();
        var names = d.Entries.Select(e => e.Name).ToHashSet();
        if (names.Count != d.Entries.Count) problems.Add("entry names must be unique");
        foreach (var e in d.Entries)
        {
            if ((e.Kind == CatalogKind.Model) != (e.Aggregation is not null)) problems.Add($"{e.Name}: aggregation is set exactly when kind is model");
            var spell = e.Kind == CatalogKind.Spell;
            if (spell != (e.Cost is not null && e.Targeting is not null)) problems.Add($"{e.Name}: cost and targeting are set exactly when kind is spell");
            if (spell && e.TakesParameter) problems.Add($"{e.Name}: a spell takes no parameter");
            // Spell effects (09-04) read forty-eight real spells as N hits or a grant that is "always with one duration: once".
            // Anything else on a spell would be applied nowhere, so it is an authoring error rather than a silent no-op.
            if (spell) foreach (var f in e.Effects.Where(f => f.Kind != EffectKind.AddHit && f.Duration != EffectDuration.Once))
                problems.Add($"{e.Name}: a spell's effect is add_hit or has duration once; {f.Kind} with duration {f.Duration} applies nowhere");
            foreach (var f in e.Effects) ValidateEffect(d, names, e.Name, f, problems);
        }
        return problems;
    }

    static void ValidateEffect(PropertySetDocument d, HashSet<string> names, string entry, EffectDocument f, List<string> problems)
    {
        string At(string what) => $"{entry}: {what}";
        if (f.TargetRoll is not null && !d.Rolls.Contains(f.TargetRoll)) problems.Add(At($"no roll '{f.TargetRoll}'"));
        if (f.TargetStat is { } s && !d.Stats.Any(x => x.Level == s.Level && x.Name == s.Name)) problems.Add(At($"no stat {s.Level}/{s.Name}"));
        if (f.TargetEntry is not null && !names.Contains(f.TargetEntry)) problems.Add(At($"no entry '{f.TargetEntry}'"));
        if (f.TargetAction is not null && d.Actions.All(a => a.Name != f.TargetAction)) problems.Add(At($"no action '{f.TargetAction}'"));
        if (f.Threshold is not null && f.Kind != EffectKind.Roll) problems.Add(At("threshold is roll only"));
        if (f.CapKind is not null && f.Kind != EffectKind.Multiply) problems.Add(At("cap_kind is multiply only"));
        if (f.Replacement is not null && f.Kind != EffectKind.ReplaceStat) problems.Add(At("replacement is replace_stat only"));
        if (f.Kind == EffectKind.Roll && f.Children.Count == 0) problems.Add(At("a roll needs a per-success child"));
        foreach (var c in f.Conditions)
            if (c.Kind is ConditionKind.Range or ConditionKind.RoundNumber or ConditionKind.UsageCount && c.Subject is not null) problems.Add(At($"{c.Kind} takes no subject"));
        foreach (var child in f.Children) ValidateEffect(d, names, entry, child, problems);
    }

    public static PropertySet ToSet(PropertySetDocument d, Func<Guid> newId)
    {
        var problems = Validate(d);
        if (problems.Count > 0) throw new InvalidDataException(string.Join("; ", problems));
        Effect Build(EffectDocument f) => new(newId(), f.Subject, f.Ordinal, f.Kind, f.TargetRoll, f.TargetStat, f.TargetEntry, f.TargetAction, f.TargetQuantity, f.TargetResult,
            f.TargetModifier, f.Amount, f.AmountFromParameter, f.Threshold, f.CapKind, f.Replacement, f.Duration,
            f.Conditions.Select(c => new EffectCondition(newId(), c.Kind, c.Subject, c.Comparison, c.Number, c.Values)).ToList(), f.Children.Select(Build).ToList());
        return new PropertySet(Hash(d), d.Name, d.GameRules, d.Stats, d.Actions, d.Rolls.Select(r => new RollDefinition(r)).ToList(),
            d.Entries.Select(e => new CatalogEntry(e.Name, e.Kind, e.TakesParameter, e.Aggregation, e.Cost, e.Faction, e.RuleText, e.Targeting, e.Effects.Select(Build).ToList())).ToList());
    }
}
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter PropertySetDocumentTests`; expect 3 passed. Commit: `Parse, validate and content-address an authored Properties document`.**

### Task 3: Grimdark Future's core `Properties`, authored in-repo

**Files:**
- Create: `server/game/Content/grimdark-future-core.properties.json`
- Modify: `server/game/Tabletop.GameServer.csproj` (embed `Content/*.properties.json`)
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/TestProperties.cs`, `tests/Tabletop.GameServer.Tests/CoreContentTests.cs`

**Interfaces:**
- Consumes: Task 2.
- Produces: the embedded resource `Tabletop.GameServer.Content.grimdark-future-core.properties.json`; test infrastructure `TestProperties.Core` (the set built from it with `Guid.CreateVersion7`), `TestProperties.CoreDocument`, `TestProperties.WithSpells(params EntryDocument[] spells)` (the core plus a test spell book, rebuilt so the hash differs). Stat names `Quality`, `Defense` (unit), `Range`, `Attacks` (weapon) — the names the management plan's import writes (management plan → Interfaces needed → Game-server plan). Actions `Hold` 0, `Advance` 6, `Rush` 12, `Charge` 12. Rolls `hit`, `defense`, `morale`, `casting`.
- Every special rule on pages 13–14 is an entry. Effect rows follow `schema.md` → Worked against the book (`AP`, `Stealth`, `Impact`, `Poison`) and the effect grammar's table; the rest are read from the rulebook text quoted in `ruleText`. Entries with no effects are names `GrimdarkFutureRules` checks (effect grammar → What `GameRules` handles instead): `Ambush`, `Scout`, `Flying`, `Strider`, `Immobile`, `Limited`, `Counter`, `Caster`, `Hero`, `Tough`, `Fear`, `Transport`, `Sniper`'s targeting, `Aircraft`'s movement. `roll_result` conditions carry `values: [roll, "raw" | "modified"]` and `number` the face (spec correction 2).
- **Two entries are in both places** (effect grammar → "A property may be in both places"): `Indirect` carries its two effect rows *and* is checked by name for *"may target enemies that are not in line of sight"*, which is no kind (`GfAttack.EligibleModels`, Task 13); `Aircraft` carries its two and is checked by name during movement. `Hero`'s third clause, *"must use the unit's Defense until all other models have been killed"*, needs **no** effect row: `Defense` is declared at unit level ([content values](../specs/2026-08-29-content-values-design.md) → Stats are typed and declared at three levels; OPR puts Quality and Defense on the unit), so every model in a unit already rolls the unit's Defense and a `replace_stat` row would restate it. `Hero` stays a pure name check, read by `EligibleForNextWound` for *"heroes must be assigned wounds last"* (Task 12).
- Kinds and aggregation follow the rulebook's wording: "models with this special rule" → `model`; a rule read on the unit's pooled roll aggregates `all` (`Stealth`, `Furious`, `Relentless`, `Fast`, `Slow`, `Flying`, `Strider`, `Immobile`), `Fearless` is `majority` (its own text), `Entrenched` is `any` (the model being shot at), per-model rules are `none` (`Tough`, `Regeneration`, `Impact`, `Caster`, `Hero`, `Fear`, `Transport`); `Aircraft`, `Ambush`, `Scout` are `unit`; the rest are `weapon`. Question 4 records the readings.

Spec: rulebook pages 13–14 (every rule); effect grammar → The twelve effect kinds, `Roll` nests, Multiplication carries a cap, Conditions belong to the effect; `schema.md` → An action is a row, A roll is a row, `amount` is signed.

- [ ] **Step 1: Write the failing test and `TestProperties`**

```csharp
using System.Reflection;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestProperties
{
    public const string ResourceName = "Tabletop.GameServer.Content.grimdark-future-core.properties.json";
    public static PropertySetDocument CoreDocument { get; } = PropertiesJson.Parse(ReadResource());
    public static PropertySet Core { get; } = PropertiesJson.ToSet(CoreDocument, Guid.CreateVersion7);

    public static PropertySet WithSpells(params EntryDocument[] spells) =>
        PropertiesJson.ToSet(CoreDocument with { Name = "Core + test spells", Entries = [.. CoreDocument.Entries, .. spells] }, Guid.CreateVersion7);

    static string ReadResource()
    {
        using var s = typeof(PropertySet).Assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidOperationException($"{ResourceName} is not embedded");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
```

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public class CoreContentTests
{
    static readonly PropertySet Core = TestProperties.Core;

    [Fact]
    public void The_document_is_valid_and_names_the_rulebooks_thirty_two_rules()
    {
        Assert.Empty(PropertiesJson.Validate(TestProperties.CoreDocument));
        string[] rules = ["Aircraft", "Ambush", "AP", "Blast", "Caster", "Counter", "Deadly", "Entrenched", "Fast", "Fear", "Fearless", "Flying", "Furious", "Hero",
            "Immobile", "Impact", "Indirect", "Lance", "Lock-On", "Limited", "Poison", "Regeneration", "Relentless", "Reliable", "Rending", "Scout", "Slow", "Sniper",
            "Stealth", "Strider", "Tough", "Transport"];
        Assert.Equal(rules.OrderBy(r => r), Core.Entries.Select(e => e.Name).OrderBy(r => r));
        Assert.Equal(["Advance", "Charge", "Hold", "Rush"], Core.Actions.Select(a => a.Name).OrderBy(n => n));
        Assert.Equal([("Advance", 6.0), ("Charge", 12.0), ("Hold", 0.0), ("Rush", 12.0)], Core.Actions.OrderBy(a => a.Name).Select(a => (a.Name, a.Distance)));
        Assert.Equal(["casting", "defense", "hit", "morale"], Core.Rolls.Select(r => r.Name).OrderBy(n => n));
        Assert.Equal("grimdark-future", Core.GameRules);
    }

    [Fact]
    public void The_four_worked_rows_match_schema_md()
    {
        var ap = Core.Entry("AP")!.Effects.Single();
        Assert.Equal((EffectSubject.Opponent, EffectKind.ModifyRoll, "defense", -1, true), (ap.Subject, ap.Kind, ap.TargetRoll, ap.Amount, ap.AmountFromParameter));
        var stealth = Core.Entry("Stealth")!;
        Assert.Equal(Aggregation.All, stealth.Aggregation);
        Assert.Equal(2, stealth.Effects.Single().Conditions.Count);
        Assert.Contains(stealth.Effects[0].Conditions, c => c.Kind == ConditionKind.Range && c.Comparison == Comparison.Gt && c.Number == 9 && c.Subject is null);
        var impact = Core.Entry("Impact")!.Effects.Single();
        Assert.Equal((EffectKind.Roll, true, 2, EffectKind.AddHit), (impact.Kind, impact.AmountFromParameter, impact.Threshold, impact.Children.Single().Kind));
        Assert.Contains(impact.Conditions, c => c.Kind == ConditionKind.ActorState && c.Comparison == Comparison.Neq && c.Values.Contains("fatigued"));
        var poison = Core.Entry("Poison")!.Effects;
        Assert.Equal(2, poison.Count);
        Assert.Contains(poison, e => e.Kind == EffectKind.ForceReroll && e.TargetRoll == "defense" && e.Conditions.Single().Values.SequenceEqual(["defense", "raw"]));
        Assert.Contains(poison, e => e.Kind == EffectKind.SuppressProperty && e.TargetEntry == "Regeneration" && e.Subject == EffectSubject.Opponent && e.Conditions.Count == 0);
    }

    [Fact]
    public void Name_check_rules_carry_no_effects_and_blast_resolves_last()
    {
        foreach (var name in new[] { "Ambush", "Scout", "Flying", "Strider", "Immobile", "Limited", "Counter", "Caster", "Hero", "Tough", "Fear", "Transport" })
            Assert.Empty(Core.Entry(name)!.Effects);
        var blast = Core.Entry("Blast")!.Effects.Single(e => e.Kind == EffectKind.Multiply);
        Assert.Equal((TargetQuantity.Hits, CapKind.ModelsInTargetUnit), (blast.TargetQuantity, blast.CapKind));
        Assert.True(blast.Ordinal > Core.AllEffects().Where(e => e.Kind != EffectKind.Multiply).Max(e => e.Ordinal));
        Assert.Equal(0, Core.Entry("Deadly")!.Effects.Single().Ordinal);
        Assert.Equal(3, Core.Entry("Fast")!.Effects.Count);
        Assert.Equal(["Advance", "Charge", "Rush"], Core.Entry("Slow")!.Effects.Select(e => e.TargetAction).OrderBy(a => a));
    }
}
```

- [ ] **Step 2: Add to `Tabletop.GameServer.csproj`**

```xml
  <ItemGroup>
    <EmbeddedResource Include="Content/*.properties.json" LogicalName="Tabletop.GameServer.Content.%(Filename)%(Extension)" />
  </ItemGroup>
```

- [ ] **Step 3: Write `Content/grimdark-future-core.properties.json`** (`ruleText` is the rulebook's sentence; abbreviated here with `…` only where the page text is long — the file carries the full sentence)

```json
{
  "name": "Grimdark Future v3.4.1 core", "gameRules": "grimdark-future",
  "stats": [
    { "level": "unit", "name": "Quality", "kind": "target_number" }, { "level": "unit", "name": "Defense", "kind": "target_number" },
    { "level": "weapon", "name": "Range", "kind": "distance" }, { "level": "weapon", "name": "Attacks", "kind": "count" } ],
  "actions": [ { "name": "Hold", "distance": 0 }, { "name": "Advance", "distance": 6 }, { "name": "Rush", "distance": 12 }, { "name": "Charge", "distance": 12 } ],
  "rolls": [ "hit", "defense", "morale", "casting" ],
  "entries": [
    { "name": "Aircraft", "kind": "unit", "ruleText": "May only use Advance actions, moving in a straight line, and adding 30\" to its total move … units targeting them get -12\" range and -1 to hit.",
      "effects": [
        { "subject": "opponent", "ordinal": 10, "kind": "modify_stat", "targetStat": { "level": "weapon", "name": "Range" }, "amount": -12 },
        { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1 } ] },
    { "name": "Ambush", "kind": "unit", "ruleText": "May be set aside before deployment. At the start of any round after the first, may be deployed anywhere over 9\" away from enemy units. …", "effects": [] },
    { "name": "AP", "kind": "weapon", "takesParameter": true, "ruleText": "Targets get -X to Defense rolls when blocking hits from weapons with this special rule.",
      "effects": [ { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "defense", "amount": -1, "amountFromParameter": true } ] },
    { "name": "Blast", "kind": "weapon", "takesParameter": true, "ruleText": "Ignores cover, and after resolving other special rules, each hit is multiplied by X, where X is up to as many hits as models in the target unit.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "suppress_modifiers", "targetModifier": "cover" },
        { "subject": "self", "ordinal": 90, "kind": "multiply", "targetQuantity": "hits", "amount": 1, "amountFromParameter": true, "capKind": "models_in_target_unit" } ] },
    { "name": "Caster", "kind": "model", "aggregation": "none", "takesParameter": true, "ruleText": "Gets X spell tokens at the start of each round, but can't hold more than 6 tokens at once. …", "effects": [] },
    { "name": "Counter", "kind": "weapon", "ruleText": "Strikes first with this weapon when charged, and the charging unit gets -1 total Impact rolls per model with Counter.", "effects": [] },
    { "name": "Deadly", "kind": "weapon", "takesParameter": true, "ruleText": "Assign each wound to one model, and multiply it by X. Hits from Deadly must be resolved first, and these wounds don't carry over to other models if the original target is killed.",
      "effects": [ { "subject": "self", "ordinal": 0, "kind": "multiply", "targetQuantity": "wounds", "amount": 1, "amountFromParameter": true } ] },
    { "name": "Entrenched", "kind": "model", "aggregation": "any", "ruleText": "Enemies get -2 to hit when shooting at models with this special rule from over 9\" away, as long as the Entrenched model hasn't moved since the beginning of its last activation.",
      "effects": [ { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -2, "conditions": [
        { "kind": "attack_kind", "comparison": "eq", "values": [ "shooting" ] }, { "kind": "range", "comparison": "gt", "number": 9 },
        { "kind": "actor_state", "subject": "self", "comparison": "neq", "values": [ "moved" ] } ] } ] },
    { "name": "Fast", "kind": "model", "aggregation": "all", "ruleText": "Models with this special rule move +2\" when using Advance and +4\" when using Rush/Charge.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": 2 },
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": 4 },
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": 4 } ] },
    { "name": "Fear", "kind": "model", "aggregation": "none", "takesParameter": true, "ruleText": "This model counts as having dealt +X wounds when checking who won melee.", "effects": [] },
    { "name": "Fearless", "kind": "model", "aggregation": "majority", "ruleText": "Whenever a unit where most models have this rule fails a morale test, roll one die. On a 4+ it counts as passed instead.",
      "effects": [ { "subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 4, "children": [ { "subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "morale_test" } ] } ] },
    { "name": "Flying", "kind": "model", "aggregation": "all", "ruleText": "Models with this special rule may move through units and terrain, and ignore terrain effects whilst moving.", "effects": [] },
    { "name": "Furious", "kind": "model", "aggregation": "all", "ruleText": "When charging, unmodified rolls of 6 to hit in melee deal one extra hit (only the original hit counts as a 6 for special rules).",
      "effects": [ { "subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [
        { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Charge" ] }, { "kind": "attack_kind", "comparison": "eq", "values": [ "melee" ] },
        { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "hit", "raw" ] } ] } ] },
    { "name": "Hero", "kind": "model", "aggregation": "none", "ruleText": "Heroes with up to Tough(6) may deploy as part of one multi-model unit without another Hero. …", "effects": [] },
    { "name": "Immobile", "kind": "model", "aggregation": "all", "ruleText": "Models with this special rule may only use Hold actions.", "effects": [] },
    { "name": "Impact", "kind": "model", "aggregation": "none", "takesParameter": true, "ruleText": "Roll X dice when attacking after charging, unless fatigued. For each 2+ the target takes one hit.",
      "effects": [ { "subject": "self", "ordinal": 20, "kind": "roll", "amount": 1, "amountFromParameter": true, "threshold": 2,
        "conditions": [ { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Charge" ] }, { "kind": "actor_state", "subject": "self", "comparison": "neq", "values": [ "fatigued" ] } ],
        "children": [ { "subject": "self", "ordinal": 0, "kind": "add_hit", "amount": 1 } ] } ] },
    { "name": "Indirect", "kind": "weapon", "ruleText": "Gets -1 to hit rolls when shooting after moving. May target enemies that are not in line of sight as if in line of sight, and ignores cover from sight obstructions.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [ { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Advance" ] } ] },
        { "subject": "self", "ordinal": 10, "kind": "suppress_modifiers", "targetModifier": "cover" } ] },
    { "name": "Lance", "kind": "weapon", "ruleText": "When charging, gets +1 to hit rolls and AP(+1) in melee.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": 1, "conditions": [ { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Charge" ] }, { "kind": "attack_kind", "comparison": "eq", "values": [ "melee" ] } ] },
        { "subject": "self", "ordinal": 5, "kind": "modify_parameter", "targetEntry": "AP", "amount": 1, "conditions": [ { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Charge" ] }, { "kind": "attack_kind", "comparison": "eq", "values": [ "melee" ] } ] } ] },
    { "name": "Lock-On", "kind": "weapon", "ruleText": "Ignores cover and all negative modifiers to hit rolls and range.",
      "effects": [
        { "subject": "self", "ordinal": 5, "kind": "suppress_modifiers", "targetModifier": "cover" },
        { "subject": "self", "ordinal": 5, "kind": "suppress_modifiers", "targetModifier": "to_hit_penalties" },
        { "subject": "self", "ordinal": 5, "kind": "suppress_modifiers", "targetModifier": "range_penalties" } ] },
    { "name": "Limited", "kind": "weapon", "ruleText": "Weapons with this rule may only be used once per game.", "effects": [] },
    { "name": "Poison", "kind": "weapon", "ruleText": "This weapon ignores Regeneration, and the target must re-roll unmodified Defense rolls of 6 when blocking hits.",
      "effects": [
        { "subject": "opponent", "ordinal": 20, "kind": "force_reroll", "targetRoll": "defense", "conditions": [ { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "defense", "raw" ] } ] },
        { "subject": "opponent", "ordinal": 5, "kind": "suppress_property", "targetEntry": "Regeneration" } ] },
    { "name": "Regeneration", "kind": "model", "aggregation": "none", "ruleText": "When taking a wound, roll one die. On a 5+ it is ignored.",
      "effects": [ { "subject": "self", "ordinal": 50, "kind": "roll", "amount": 1, "threshold": 5, "children": [ { "subject": "self", "ordinal": 0, "kind": "override_result", "targetResult": "wound" } ] } ] },
    { "name": "Relentless", "kind": "model", "aggregation": "all", "ruleText": "When this model uses a Hold action and shoots, unmodified rolls of 6 to hit deal one extra hit (only the original hit counts as a 6 for special rules).",
      "effects": [ { "subject": "self", "ordinal": 30, "kind": "add_hit", "amount": 1, "conditions": [
        { "kind": "action_taken", "subject": "self", "comparison": "eq", "values": [ "Hold" ] }, { "kind": "attack_kind", "comparison": "eq", "values": [ "shooting" ] },
        { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "hit", "raw" ] } ] } ] },
    { "name": "Reliable", "kind": "weapon", "ruleText": "Models attacks at Quality 2+ with this weapon.",
      "effects": [ { "subject": "self", "ordinal": 1, "kind": "replace_stat", "targetStat": { "level": "unit", "name": "Quality" }, "replacement": "2+" } ] },
    { "name": "Rending", "kind": "weapon", "ruleText": "This weapon ignores Regeneration, and unmodified rolls of 6 to hit get AP(4).",
      "effects": [
        { "subject": "opponent", "ordinal": 5, "kind": "suppress_property", "targetEntry": "Regeneration" },
        { "subject": "self", "ordinal": 30, "kind": "grant_property", "targetEntry": "AP", "amount": 4, "conditions": [ { "kind": "roll_result", "comparison": "eq", "number": 6, "values": [ "hit", "raw" ] } ] } ] },
    { "name": "Scout", "kind": "unit", "ruleText": "May be set aside before deployment. After all other units are deployed, must be deployed and may then be placed anywhere within 12\" of their position. …", "effects": [] },
    { "name": "Slow", "kind": "model", "aggregation": "all", "ruleText": "Models with this special rule move -2\" when using Advance, and -4\" when using Rush/Charge.",
      "effects": [
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Advance", "amount": -2 },
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Rush", "amount": -4 },
        { "subject": "self", "ordinal": 10, "kind": "modify_stat", "targetAction": "Charge", "amount": -4 } ] },
    { "name": "Sniper", "kind": "weapon", "ruleText": "Shoots at Quality 2+, and each model with Sniper may pick any model in the target unit as its individual target, which is resolved as if it was a unit of 1. Sniper weapon shooting must be resolved before other weapons.",
      "effects": [ { "subject": "self", "ordinal": 1, "kind": "replace_stat", "targetStat": { "level": "unit", "name": "Quality" }, "replacement": "2+", "conditions": [ { "kind": "attack_kind", "comparison": "eq", "values": [ "shooting" ] } ] } ] },
    { "name": "Stealth", "kind": "model", "aggregation": "all", "ruleText": "Enemies get -1 to hit rolls when shooting at units where all models have this rule from over 9\" away.",
      "effects": [ { "subject": "opponent", "ordinal": 10, "kind": "modify_roll", "targetRoll": "hit", "amount": -1, "conditions": [
        { "kind": "attack_kind", "comparison": "eq", "values": [ "shooting" ] }, { "kind": "range", "comparison": "gt", "number": 9 } ] } ] },
    { "name": "Strider", "kind": "model", "aggregation": "all", "ruleText": "May ignore the effects of difficult terrain when moving.", "effects": [] },
    { "name": "Tough", "kind": "model", "aggregation": "none", "takesParameter": true, "ruleText": "This model must take X wounds before being killed. … heroes must be assigned wounds last.", "effects": [] },
    { "name": "Transport", "kind": "model", "aggregation": "none", "takesParameter": true, "ruleText": "May transport up to X models or Heroes with up to Tough(6) …", "effects": [] }
  ]
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter CoreContentTests`; expect 3 passed. Commit: `Author Grimdark Future's core Properties as an in-repo document`.**

### Task 4: The store — eight tables in, eight tables out — and the startup seeder

**Files:**
- Create: `server/game/Properties/IPropertiesStore.cs`, `server/game/Properties/PostgresPropertiesStore.cs`, `server/game/Properties/PropertiesSeeder.cs`
- Modify: `server/game/Program.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/PostgresFixture.cs` (the skeleton's fixture gains `DeletePropertiesAsync`)
- Test: `tests/Tabletop.GameServer.Tests/PropertiesStoreTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3; the skeleton's `RowMapping.EnumText/EnumFrom`, `PostgresFixture`; `scripts/postgres/init/001_schema.sql` (the eight tables, `effect.target_stat_level`).
- Produces: `interface IPropertiesStore { Task<PropertySet?> LoadAsync(string contentHash, CancellationToken ct); Task<bool> SeedAsync(PropertySetDocument document, CancellationToken ct); }` (`SeedAsync` returns false when the hash already exists and writes nothing); `sealed class InMemoryPropertiesStore : IPropertiesStore` (+ `Add(PropertySet)`); `sealed class PostgresPropertiesStore(NpgsqlDataSource db) : IPropertiesStore`; `sealed class PropertiesSeeder(IPropertiesStore store, ILogger<PropertiesSeeder> log)` with `static IReadOnlyList<PropertySetDocument> EmbeddedDocuments()` and `Task<IReadOnlyList<string>> SeedEmbeddedAsync(CancellationToken ct)` (returns the hashes now present); `Program.cs` registers `IPropertiesStore` and runs `SeedEmbeddedAsync` once at startup, before the host serves.
- Row mapping: `roll_result` values and the other `condition_value` rows are written in list order with an `ordinal`-free primary key `(condition_id, value)`, so they are read back sorted by a stable rule: the loader orders `condition_value` by `value` **except** for `roll_result`, whose two values are the roll name and `raw|modified` and are re-ordered so the roll name comes first (a roll named `raw` or `modified` is refused by `Validate`; spec correction 2 names the shape). Effect children are rows whose `parent_effect_id` is the parent; the loader reads all effect rows of the set, then assembles the tree ordered by `ordinal` then `id`.
- Who seeds: the game server, at startup, from its embedded documents — it is the only process that interprets the catalog, and the write is idempotent by hash. Management's `GET /v1/properties` then lists the set. Spec correction 1; question 1.

Spec: `schema.md` → `Properties` is eight tables; `architecture.md` → Pinning ("hashed and pinned in the game record"); management plan → Interfaces needed → Game-server plan ("load at least one `properties` row, without which `POST /v1/games` answers `422`").

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class PropertiesStoreTests(PostgresFixture pg)
{
    readonly PostgresPropertiesStore _store = new(pg.Db);

    [Fact]
    public async Task Seeding_the_core_document_round_trips_through_the_eight_tables_and_is_idempotent()
    {
        var doc = TestProperties.CoreDocument with { Name = "Core " + Guid.CreateVersion7().ToString("N")[..6] };
        var hash = PropertiesJson.Hash(doc);
        Assert.Null(await _store.LoadAsync(hash, CancellationToken.None));
        Assert.True(await _store.SeedAsync(doc, CancellationToken.None));
        Assert.False(await _store.SeedAsync(doc, CancellationToken.None));
        var set = await _store.LoadAsync(hash, CancellationToken.None);
        Assert.NotNull(set);
        var expected = PropertiesJson.ToSet(doc, Guid.CreateVersion7);
        Assert.Equal(Shape(expected), Shape(set));
        Assert.Equal(32, set.Entries.Count);
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.properties where content_hash = $1", hash));
        Assert.Equal(expected.AllEffects().Count(), await pg.CountAsync("select count(*) from management.effect where properties_hash = $1", hash));
        var again = await _store.LoadAsync(hash, CancellationToken.None);
        Assert.Equal(set.Entry("Poison")!.Effects[0].Id, again!.Entry("Poison")!.Effects[0].Id);     // ids are stable once seeded
        await pg.DeletePropertiesAsync(hash);
    }

    /// <summary>The set with every id blanked, as canonical JSON, so two loads compare on content.</summary>
    static string Shape(PropertySet s)
    {
        Effect Blank(Effect e) => e with { Id = Guid.Empty, Conditions = e.Conditions.Select(c => c with { Id = Guid.Empty }).ToList(), Children = e.Children.Select(Blank).ToList() };
        var entries = s.Entries.OrderBy(e => e.Name, StringComparer.Ordinal).Select(e => e with { Effects = e.Effects.Select(Blank).ToList() });
        return ContentJson.Serialize(new { s.ContentHash, s.Name, s.GameRules, Stats = s.Stats.OrderBy(x => x.Level).ThenBy(x => x.Name), Actions = s.Actions.OrderBy(a => a.Name), Rolls = s.Rolls.OrderBy(r => r.Name), Entries = entries });
    }

    [Fact]
    public async Task The_embedded_documents_seed_at_startup()
    {
        var docs = PropertiesSeeder.EmbeddedDocuments();
        Assert.Single(docs);
        var hashes = await new PropertiesSeeder(_store, Microsoft.Extensions.Logging.Abstractions.NullLogger<PropertiesSeeder>.Instance).SeedEmbeddedAsync(CancellationToken.None);
        Assert.Equal([PropertiesJson.Hash(docs[0])], hashes);
        Assert.NotNull(await _store.LoadAsync(hashes[0], CancellationToken.None));
    }
}
```

Add `public Task DeletePropertiesAsync(string hash)` to the skeleton's `PostgresFixture` (`delete from management.properties where content_hash = $1`; the seven tables cascade) — the fixture only tracks rows it seeded itself. The embedded core set is left in place after the second test: it is what a developer's database should hold.

- [ ] **Step 2: Run; expect compile errors. Write `IPropertiesStore.cs`**

```csharp
namespace Tabletop.GameServer.Properties;

/// <summary>The pinned sets. Load by hash at hydration; seed once from an authored document.</summary>
public interface IPropertiesStore
{
    Task<PropertySet?> LoadAsync(string contentHash, CancellationToken ct);
    /// <summary>Writes the eight tables for the document's hash. False, and nothing written, when the hash is already present.</summary>
    Task<bool> SeedAsync(PropertySetDocument document, CancellationToken ct);
}

public sealed class InMemoryPropertiesStore : IPropertiesStore
{
    private readonly Dictionary<string, PropertySet> _sets = new();
    public InMemoryPropertiesStore Add(PropertySet set) { _sets[set.ContentHash] = set; return this; }
    public Task<PropertySet?> LoadAsync(string contentHash, CancellationToken ct) => Task.FromResult(_sets.GetValueOrDefault(contentHash));
    public Task<bool> SeedAsync(PropertySetDocument document, CancellationToken ct)
    {
        var hash = PropertiesJson.Hash(document);
        if (_sets.ContainsKey(hash)) return Task.FromResult(false);
        _sets[hash] = PropertiesJson.ToSet(document, Guid.CreateVersion7);
        return Task.FromResult(true);
    }
}
```

- [ ] **Step 3: Write `PostgresPropertiesStore.cs`**

```csharp
using Npgsql;
using Tabletop.Content;
using static Tabletop.GameServer.Persistence.RowMapping;

namespace Tabletop.GameServer.Properties;

/// <summary>The eight tables. Enum spellings are the schema's check-constraint values, which are the wire spellings (RowMapping).</summary>
public sealed class PostgresPropertiesStore(NpgsqlDataSource db) : IPropertiesStore
{
    public async Task<PropertySet?> LoadAsync(string hash, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        string name, rules;
        await using (var cmd = new NpgsqlCommand("select name, game_rules from management.properties where content_hash = $1", conn) { Parameters = { new() { Value = hash } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct)) { if (!await r.ReadAsync(ct)) return null; (name, rules) = (r.GetString(0), r.GetString(1)); }

        var stats = await Rows(conn, "select level, name, kind from management.stat_definition where properties_hash = $1", hash, ct,
            r => new StatDefinition(EnumFrom<StatLevel>(r.GetString(0)), r.GetString(1), EnumFrom<StatKind>(r.GetString(2))));
        var actions = await Rows(conn, "select name, distance from management.action where properties_hash = $1", hash, ct, r => new ActionDefinition(r.GetString(0), r.GetFloat(1)));
        var rolls = await Rows(conn, "select name from management.roll where properties_hash = $1", hash, ct, r => new RollDefinition(r.GetString(0)));
        var values = (await Rows(conn, "select cv.condition_id, cv.value from management.condition_value cv join management.effect_condition c on c.id = cv.condition_id join management.effect e on e.id = c.effect_id where e.properties_hash = $1 order by cv.value", hash, ct,
            r => (Id: r.GetGuid(0), Value: r.GetString(1)))).ToLookup(x => x.Id, x => x.Value);
        var conditions = (await Rows(conn, "select c.id, c.effect_id, c.kind, c.subject, c.comparison, c.number from management.effect_condition c join management.effect e on e.id = c.effect_id where e.properties_hash = $1 order by c.id", hash, ct,
            r => (EffectId: r.GetGuid(1), Condition: Condition(r.GetGuid(0), EnumFrom<ConditionKind>(r.GetString(2)), NullableString(r, 3), EnumFrom<Comparison>(r.GetString(4)), r.IsDBNull(5) ? null : r.GetInt32(5), values[r.GetGuid(0)].ToList())))).ToLookup(x => x.EffectId, x => x.Condition);
        var effectRows = await Rows(conn, "select id, entry_name, parent_effect_id, subject, ordinal, kind, target_roll, target_stat_level, target_stat, target_entry, target_action, target_quantity, target_result, target_modifier, amount, amount_from_parameter, threshold, cap_kind, replacement, duration from management.effect where properties_hash = $1 order by ordinal, id", hash, ct,
            r => (Entry: r.GetString(1), Parent: NullableGuid(r, 2), Row: r.GetGuid(0), Build: new Func<IReadOnlyList<Effect>, Effect>(children => new Effect(r.GetGuid(0), EnumFrom<EffectSubject>(r.GetString(3)), r.GetInt32(4), EnumFrom<EffectKind>(r.GetString(5)),
                NullableString(r, 6), r.IsDBNull(8) ? null : new StatRef(EnumFrom<StatLevel>(r.GetString(7)), r.GetString(8)), NullableString(r, 9), NullableString(r, 10),
                r.IsDBNull(11) ? null : EnumFrom<TargetQuantity>(r.GetString(11)), r.IsDBNull(12) ? null : EnumFrom<TargetResult>(r.GetString(12)), r.IsDBNull(13) ? null : EnumFrom<TargetModifier>(r.GetString(13)),
                r.IsDBNull(14) ? null : r.GetInt32(14), r.GetBoolean(15), r.IsDBNull(16) ? null : r.GetInt32(16), r.IsDBNull(17) ? null : EnumFrom<CapKind>(r.GetString(17)), NullableString(r, 18), EnumFrom<EffectDuration>(r.GetString(19)),
                conditions[r.GetGuid(0)].ToList(), children))));
        IReadOnlyList<Effect> Children(Guid parent) => effectRows.Where(x => x.Parent == parent).Select(x => x.Build(Children(x.Row))).ToList();
        var entries = await Rows(conn, "select name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range from management.catalog_entry where properties_hash = $1 order by name", hash, ct,
            r => new CatalogEntry(r.GetString(0), EnumFrom<CatalogKind>(r.GetString(1)), r.GetBoolean(2), r.IsDBNull(3) ? null : EnumFrom<Aggregation>(r.GetString(3)), r.IsDBNull(4) ? null : r.GetInt32(4), NullableString(r, 5), r.GetString(6),
                r.IsDBNull(7) ? null : new SpellTargeting(EnumFrom<TargetSide>(r.GetString(7)), EnumFrom<TargetGrain>(r.GetString(8)), r.GetInt32(9), r.GetFloat(10)),
                effectRows.Where(x => x.Entry == r.GetString(0) && x.Parent is null).Select(x => x.Build(Children(x.Row))).ToList()));
        return new PropertySet(hash, name, rules, stats, actions, rolls, entries);
    }

    /// <summary>roll_result's two values are the roll name and raw|modified; the roll name goes first on the way back.</summary>
    static EffectCondition Condition(Guid id, ConditionKind kind, string? subject, Comparison cmp, int? number, List<string> values)
    {
        if (kind == ConditionKind.RollResult && values.Count == 2 && values[0] is "raw" or "modified") values.Reverse();
        return new EffectCondition(id, kind, subject is null ? null : EnumFrom<EffectSubject>(subject), cmp, number, values);
    }

    static async Task<List<T>> Rows<T>(NpgsqlConnection conn, string sql, string hash, CancellationToken ct, Func<NpgsqlDataReader, T> map)
    {
        var list = new List<T>();
        await using var cmd = new NpgsqlCommand(sql, conn) { Parameters = { new() { Value = hash } } };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(map(r));
        return list;
    }

    public async Task<bool> SeedAsync(PropertySetDocument doc, CancellationToken ct)
    {
        var set = PropertiesJson.ToSet(doc, Guid.CreateVersion7);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        async Task<int> Run(string sql, params object?[] args)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            foreach (var a in args) cmd.Parameters.AddWithValue(a ?? DBNull.Value);
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        if (await Run("insert into management.properties (content_hash, name, game_rules) values ($1, $2, $3) on conflict do nothing", set.ContentHash, set.Name, set.GameRules) == 0) return false;
        foreach (var s in set.Stats) await Run("insert into management.stat_definition (properties_hash, level, name, kind) values ($1, $2, $3, $4)", set.ContentHash, EnumText(s.Level), s.Name, EnumText(s.Kind));
        foreach (var a in set.Actions) await Run("insert into management.action (properties_hash, name, distance) values ($1, $2, $3)", set.ContentHash, a.Name, (float)a.Distance);
        foreach (var r in set.Rolls) await Run("insert into management.roll (properties_hash, name) values ($1, $2)", set.ContentHash, r.Name);
        foreach (var e in set.Entries)
            await Run("insert into management.catalog_entry (properties_hash, name, kind, takes_parameter, aggregation, cost, faction, rule_text, target_side, target_grain, target_count, target_range) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)",
                set.ContentHash, e.Name, EnumText(e.Kind), e.TakesParameter, e.Aggregation is { } ag ? EnumText(ag) : null, e.Cost, e.Faction, e.RuleText,
                e.Targeting is { } t ? EnumText(t.Side) : null, e.Targeting is { } t2 ? EnumText(t2.Grain) : null, e.Targeting?.Count, e.Targeting is { } t3 ? (float?)t3.Range : null);
        foreach (var e in set.Entries) foreach (var f in e.Effects) await Effect(e.Name, null, f);
        await tx.CommitAsync(ct);
        return true;

        async Task Effect(string entry, Guid? parent, Effect f)
        {
            await Run("insert into management.effect (id, properties_hash, entry_name, parent_effect_id, subject, ordinal, kind, target_roll, target_stat_level, target_stat, target_entry, target_action, target_quantity, target_result, target_modifier, amount, amount_from_parameter, threshold, cap_kind, replacement, duration) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20, $21)",
                f.Id, set.ContentHash, entry, parent, EnumText(f.Subject), f.Ordinal, EnumText(f.Kind), f.TargetRoll, f.TargetStat is { } s ? EnumText(s.Level) : null, f.TargetStat?.Name, f.TargetEntry, f.TargetAction,
                f.TargetQuantity is { } q ? EnumText(q) : null, f.TargetResult is { } tr ? EnumText(tr) : null, f.TargetModifier is { } tm ? EnumText(tm) : null, f.Amount, f.AmountFromParameter, f.Threshold,
                f.CapKind is { } ck ? EnumText(ck) : null, f.Replacement, EnumText(f.Duration));
            foreach (var c in f.Conditions)
            {
                await Run("insert into management.effect_condition (id, effect_id, kind, subject, comparison, number) values ($1, $2, $3, $4, $5, $6)", c.Id, f.Id, EnumText(c.Kind), c.Subject is { } cs ? EnumText(cs) : null, EnumText(c.Comparison), c.Number);
                foreach (var v in c.Values) await Run("insert into management.condition_value (condition_id, value) values ($1, $2)", c.Id, v);
            }
            foreach (var child in f.Children) await Effect(entry, f.Id, child);
        }
    }
}
```

`Validate` (Task 2) gains one rule so the read-back order is unambiguous: a `roll_result` condition has exactly two values, the second being `raw` or `modified`, and no roll may be named `raw` or `modified`.

- [ ] **Step 4: Write `PropertiesSeeder.cs` and register**

```csharp
namespace Tabletop.GameServer.Properties;

/// <summary>Authored content reaches the tables here: every embedded *.properties.json is seeded once, by hash, when the server starts.</summary>
public sealed class PropertiesSeeder(IPropertiesStore store, ILogger<PropertiesSeeder> log)
{
    public static IReadOnlyList<PropertySetDocument> EmbeddedDocuments()
    {
        var asm = typeof(PropertiesSeeder).Assembly;
        return asm.GetManifestResourceNames().Where(n => n.EndsWith(".properties.json")).OrderBy(n => n)
            .Select(n => { using var s = asm.GetManifestResourceStream(n)!; using var r = new StreamReader(s); return PropertiesJson.Parse(r.ReadToEnd()); }).ToList();
    }

    public async Task<IReadOnlyList<string>> SeedEmbeddedAsync(CancellationToken ct)
    {
        var hashes = new List<string>();
        foreach (var doc in EmbeddedDocuments())
        {
            var inserted = await store.SeedAsync(doc, ct);
            var hash = PropertiesJson.Hash(doc);
            log.LogInformation("Properties '{Name}' {Hash}: {What}", doc.Name, hash, inserted ? "seeded" : "already present");
            hashes.Add(hash);
        }
        return hashes;
    }
}
```

`Program.cs`: `builder.Services.AddSingleton<IPropertiesStore, PostgresPropertiesStore>(); builder.Services.AddSingleton<PropertiesSeeder>();` and, after `var app = builder.Build();`: `if (!app.Environment.IsEnvironment("Test")) await app.Services.GetRequiredService<PropertiesSeeder>().SeedEmbeddedAsync(CancellationToken.None);` (the test factory seeds through the fixture). The `Test` guard keeps `WebApplicationFactory` tests off the shared database's write path.

- [ ] **Step 5: Run `docker compose up -d && dotnet test tests/Tabletop.GameServer.Tests --filter PropertiesStoreTests`; expect 2 passed. Then `dotnet run --project server/game` once and check `psql -c "select name, content_hash from management.properties"` lists the core set. Commit: `Load and seed Properties through the eight tables`.**

### Task 5: The roll primitive — a seeded, counter-based generator that is game state

**Files:**
- Create: `server/game/Dice/IDiceSource.cs`, `server/game/Dice/DiceCursor.cs`, `server/game/Dice/RollPlan.cs`, `server/game/Dice/Roller.cs`
- Modify: `server/game/Rules/IRulesEngine.cs` (`GameState.RngSeed`)
- Test: `tests/Tabletop.GameServer.Tests/DiceTests.cs`

**Interfaces:**
- Consumes: `GameState`, `EventApplier.EffectiveLog`, `StepCompleted`, `DiceRerolled`, `RollRecord`, `Die`, `RollModifier`, `DieReplacement` (Protocol).
- Produces (namespace `Tabletop.GameServer.Dice`): `interface IDiceSource { int Draw(ulong seed, long index, int sides); }`; `sealed class DiceSource : IDiceSource` (SplitMix64 over `seed ^ index`, uniform in `1..sides`); `sealed class ScriptedDice(params int[] faces) : IDiceSource` (face `faces[index % Length]`, seed ignored); `static class DiceCursor { static long DrawnSoFar(GameState state); }` (Σ `Dice.Count` over every `RollRecord` in every `StepCompleted` of the effective log, plus Σ `Replaced.Count` over every `DiceRerolled`); `GameState.RngSeed : ulong` (first eight bytes of SHA-256 of `Info.Id`); `sealed record RollConvention(int Sides, int? ModifierCap)`; `sealed record RollPlan(string Roll, int? Threshold, IRL<RollModifier> Modifiers, RollConvention Convention, int? RequiredRaw, IReadOnlySet<int> RerollRaw)` with `int NetModifier` (summed, then clamped to `±ModifierCap` when set); `sealed record RollResult(RollRecord Record, DiceRerolled? ForcedRerolls, long NextCursor)`; `sealed class Roller(IDiceSource dice, ulong seed, long cursor)` with `RollResult Roll(Guid frameId, RollPlan plan, int count)` and `Die Judge(RollPlan plan, int raw)`; `static class RollOutcome` with `int Successes(RollRecord)`, `int Failures(RollRecord)`, `int CountRaw(RollRecord, int face)`, `IRL<Die> Reroll(RollPlan, RollRecord, IRL<int> indices, Roller) → IRL<DieReplacement>`.
- Semantics: `Modified = Raw + NetModifier`; `Success = RequiredRaw is { } r ? Raw >= r : Threshold is { } t && Modified >= t`; a null threshold (the roll-off) records `Success = false` for every die and compares raw. A forced reroll (`RerollRaw` contains the raw face) draws one more die once and is recorded as a `DiceRerolled` payload following the `step_completed`, so the record shows the replaced die and the cursor counts every draw (action sequences → One roll primitive; resolution → A reroll records which dice it replaced).
- The seed is derived, not stored, and the cursor is counted, not stored: rebuilding from the log reproduces the dice, and `Undone(N)` shortens the effective log so the next roll after an undo repeats the roll it replaced (`architecture.md` → Undo: "rewinding and redoing the same action reproduces the same dice").

Spec: `architecture.md` → Command / Event spine ("RNG lives in server state as a seed"), Undo; action sequences → One roll primitive (the split table: the engine owns the seeded RNG and per-die recording; `GameRules` the convention, threshold and cap); scheduler → Deployment (null threshold; ties rerolled); resolution → Players roll their own dice.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class DiceTests
{
    static readonly RollConvention D6 = new(6, null);
    static RollPlan Plan(int? threshold, params RollModifier[] mods) => new("hit", threshold, mods, D6, null, new HashSet<int>());
    static GameState State(params EventPayload[] history)
    {
        var applier = new EventApplier(Samples.Board, Samples.Game);
        applier.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, null, p, null)));
        return new GameState(Samples.Game, Samples.Board, applier, GrimdarkFutureRules.Instance);
    }

    [Fact]
    public void The_source_is_deterministic_uniform_and_seed_dependent()
    {
        var d = new DiceSource();
        Assert.Equal(d.Draw(7, 3, 6), d.Draw(7, 3, 6));
        Assert.NotEqual(Enumerable.Range(0, 20).Select(i => d.Draw(7, i, 6)), Enumerable.Range(0, 20).Select(i => d.Draw(8, i, 6)));
        var faces = Enumerable.Range(0, 60_000).Select(i => d.Draw(1, i, 6)).GroupBy(f => f).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(6, faces.Count); Assert.All(faces.Values, n => Assert.InRange(n, 9_000, 11_000));
        Assert.Equal([3, 4, 5, 3], Enumerable.Range(0, 4).Select(i => new ScriptedDice(3, 4, 5).Draw(0, i, 6)));
    }

    [Fact]
    public void A_roll_records_raw_modified_success_and_advances_the_cursor()
    {
        var roller = new Roller(new ScriptedDice(3, 4, 5), 0, 0);
        var result = roller.Roll(Samples.Frame, Plan(4, new RollModifier("Stealth", -1)), 3);
        Assert.Equal([(3, 2, false), (4, 3, false), (5, 4, true)], result.Record.Dice.Select(x => (x.Raw, x.Modified, x.Success)));
        Assert.Equal((4, "hit", 3L), (result.Record.Threshold, result.Record.Roll, result.NextCursor));
        Assert.Null(result.ForcedRerolls);
        Assert.Equal(1, RollOutcome.Successes(result.Record)); Assert.Equal(2, RollOutcome.Failures(result.Record));
    }

    [Fact]
    public void Caps_required_raw_and_null_threshold()
    {
        var capped = new Roller(new ScriptedDice(4), 0, 0).Roll(Samples.Frame, Plan(4, new RollModifier("a", 1), new RollModifier("b", 1)) with { Convention = new RollConvention(6, 1) }, 1).Record.Dice[0];
        Assert.Equal((4, 5), (capped.Raw, capped.Modified));
        var fatigued = new Roller(new ScriptedDice(5, 6), 0, 0).Roll(Samples.Frame, Plan(2) with { RequiredRaw = 6 }, 2).Record.Dice;
        Assert.Equal([false, true], fatigued.Select(x => x.Success));
        var rollOff = new Roller(new ScriptedDice(2, 6), 0, 0).Roll(Samples.Frame, Plan(null), 2).Record;
        Assert.Null(rollOff.Threshold); Assert.All(rollOff.Dice, x => Assert.False(x.Success)); Assert.Equal(1, RollOutcome.CountRaw(rollOff, 6));
    }

    [Fact]
    public void A_forced_reroll_is_a_dice_rerolled_payload_and_the_cursor_counts_the_extra_draw()
    {
        var plan = Plan(4) with { Roll = "defense", RerollRaw = new HashSet<int> { 6 } };
        var result = new Roller(new ScriptedDice(6, 2, 6), 0, 0).Roll(Samples.Frame, plan, 2);
        Assert.Equal([6, 2], result.Record.Dice.Select(x => x.Raw));                                  // the record holds the dice as rolled
        var rr = Assert.IsType<DiceRerolled>(result.ForcedRerolls);
        Assert.Equal((0, 6, 6), (rr.Replaced.Single().Index, rr.Replaced.Single().Old.Raw, rr.Replaced.Single().New.Raw));   // rerolled once, still a 6: blocked
        Assert.Equal(3, result.NextCursor);
    }

    [Fact]
    public void The_cursor_is_counted_from_the_effective_log_and_the_seed_from_the_game()
    {
        var hit = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [new Die(3, 3, false), new Die(5, 5, true)]);
        var state = State(new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], []),
            new DiceRerolled(Samples.Frame, hit.Id, [new DieReplacement(0, new Die(3, 3, false), new Die(6, 6, true))]),
            new DiceEdited(Samples.Frame, hit.Id, [new DieReplacement(1, new Die(5, 5, true), new Die(1, 1, false))]));
        Assert.Equal(3, DiceCursor.DrawnSoFar(state));
        var undone = State(new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], []), new Undone(0));
        Assert.Equal(0, DiceCursor.DrawnSoFar(undone));
        Assert.Equal(state.RngSeed, undone.RngSeed);
        Assert.NotEqual(0UL, state.RngSeed);
        Assert.NotEqual(state.RngSeed, new GameState(Samples.Game with { Id = Guid.CreateVersion7() }, Samples.Board, state.Applier, GrimdarkFutureRules.Instance).RngSeed);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Add to `GameState` in `Rules/IRulesEngine.cs`**

```csharp
    /// <summary>The RNG seed is game state and is derived, never stored: the first eight bytes of SHA-256(game id). The cursor is Dice.DiceCursor.</summary>
    public ulong RngSeed { get; } = BitConverter.ToUInt64(System.Security.Cryptography.SHA256.HashData(info.Id.ToByteArray()), 0);
```

- [ ] **Step 3: Write `IDiceSource.cs` and `DiceCursor.cs`**

```csharp
namespace Tabletop.GameServer.Dice;

public interface IDiceSource { int Draw(ulong seed, long index, int sides); }

/// <summary>Counter-based: die i of a game is a pure function of (seed, i), so the cursor is the whole RNG state.</summary>
public sealed class DiceSource : IDiceSource
{
    public int Draw(ulong seed, long index, int sides)
    {
        var z = seed ^ ((ulong)index * 0x9E3779B97F4A7C15UL) + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % (ulong)sides) + 1;
    }
}

/// <summary>Tests: the faces in order, cycling. The seed is ignored; the index is the cursor.</summary>
public sealed class ScriptedDice(params int[] faces) : IDiceSource
{
    public int Draw(ulong seed, long index, int sides) => faces[(int)(index % faces.Length)];
}
```

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dice;

public static class DiceCursor
{
    /// <summary>Every die ever drawn in the live log: the dice in each roll record, plus each forced or player reroll. Edits draw nothing.</summary>
    public static long DrawnSoFar(GameState state) => EventApplier.EffectiveLog(state.Applier.Log).Sum(e => e.Payload switch
    {
        StepCompleted s => s.Rolls.Sum(r => (long)r.Dice.Count),
        DiceRerolled d => d.Replaced.Count,
        _ => 0L,
    });
}
```

- [ ] **Step 4: Write `RollPlan.cs` and `Roller.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dice;

/// <summary>What GameRules supplies per roll: the die, and whether summed modifiers are clamped (OPR never; 40k ±1).</summary>
public sealed record RollConvention(int Sides, int? ModifierCap);

/// <summary>One roll, settled: the threshold GameRules derived, the modifiers the two passes and the players contributed, and the per-die rules.</summary>
public sealed record RollPlan(string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, RollConvention Convention, int? RequiredRaw, IReadOnlySet<int> RerollRaw)
{
    public int NetModifier
    {
        get { var sum = Modifiers.Sum(m => m.Amount); return Convention.ModifierCap is { } cap ? Math.Clamp(sum, -cap, cap) : sum; }
    }
}

public sealed record RollResult(RollRecord Record, DiceRerolled? ForcedRerolls, long NextCursor);

/// <summary>Draws from the cursor, judges each die under the plan, and records everything. Summing is generic; clamping is the convention's.</summary>
public sealed class Roller(IDiceSource dice, ulong seed, long cursor)
{
    public long Cursor { get; private set; } = cursor;

    public Die Judge(RollPlan plan, int raw)
    {
        var modified = raw + plan.NetModifier;
        var success = plan.RequiredRaw is { } r ? raw >= r : plan.Threshold is { } t && modified >= t;
        return new Die(raw, modified, success);
    }

    public int Draw(RollPlan plan) => dice.Draw(seed, Cursor++, plan.Convention.Sides);

    public RollResult Roll(Guid frameId, RollPlan plan, int count)
    {
        var rolled = Enumerable.Range(0, count).Select(_ => Judge(plan, Draw(plan))).ToList();
        var record = new RollRecord(Guid.CreateVersion7(), plan.Roll, plan.Threshold, plan.Modifiers, rolled);
        var replaced = new List<DieReplacement>();
        for (var i = 0; i < rolled.Count; i++)
            if (plan.RerollRaw.Contains(rolled[i].Raw)) replaced.Add(new DieReplacement(i, rolled[i], Judge(plan, Draw(plan))));   // once, never again
        return new RollResult(record, replaced.Count == 0 ? null : new DiceRerolled(frameId, record.Id, replaced), Cursor);
    }
}

/// <summary>Reading a record back. A record's dice are what stands after every reroll and edit the applier copied in.</summary>
public static class RollOutcome
{
    public static int Successes(RollRecord r) => r.Dice.Count(d => d.Success);
    public static int Failures(RollRecord r) => r.Dice.Count(d => !d.Success);
    public static int CountRaw(RollRecord r, int face) => r.Dice.Count(d => d.Raw == face);
    /// <summary>A player's reroll: the named dice (all when empty) drawn again under the record's own threshold and modifiers.</summary>
    public static IReadOnlyList<DieReplacement> Reroll(RollPlan plan, RollRecord record, IReadOnlyList<int> indices, Roller roller) =>
        (indices.Count == 0 ? Enumerable.Range(0, record.Dice.Count) : indices).Select(i => new DieReplacement(i, record.Dice[i], roller.Judge(plan, roller.Draw(plan)))).ToList();
}
```

- [ ] **Step 5: Run `dotnet test tests/Tabletop.GameServer.Tests --filter DiceTests`; expect 5 passed. Commit: `Add the roll primitive over a seeded, counter-based dice source`.**

### Task 6: Pass one — which properties are live

**Files:**
- Create: `server/game/Effects/Interaction.cs`, `server/game/Effects/Conditions.cs`, `server/game/Effects/LiveProperties.cs`, `server/game/Effects/Lookup.cs`
- Test: `tests/Tabletop.GameServer.Tests/LivePropertiesTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 3; `GameState`, `Projection`, `UnitInPlay`, `ModelInPlay`, `Grant`, `OnTable`, `OnShelf`, `Inside` (Protocol); `PropertyInstance` (Content). Test infrastructure `Armies` is written here and used by every later test.
- Produces (namespace `Tabletop.GameServer.Effects`): `enum AttackKind { Shooting, Melee }`; `enum Side { Attacker, Defender }`; `sealed record Party(Guid UnitId, IRL<Guid> ModelIds, IRL<Guid> WeaponIds)` with `static Party.Whole(GameState, Guid unitId)` (every model still in play — on the table or inside a container — no weapons) and `static Party.Of(GameState, Guid unitId, IEnumerable<Guid> modelIds, IEnumerable<Guid> weaponIds)`; `sealed record Interaction(GameState State, PropertySet Properties, Party Attacker, Party? Defender, AttackKind? Attack, string? ActionTaken, double? RangeInches, IRL<string> DefenderTerrain)` with `Party Of(Side)`, `Side Other(Side)`, `int Round`; `sealed record LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId)` with `Side ActedOn(Effect)`; `sealed class LiveSet(IRL<LiveProperty> Live, IReadOnlySet<TargetModifier> SuppressedModifiers, IRL<(LiveProperty Source, Effect Effect)> Applied)` with `IEnumerable<LiveProperty> On(Side)`, `bool Has(Side, string entry)`, `int? Parameter(Side, string entry)` (summed over instances when `TakesParameter`), `IEnumerable<(LiveProperty Source, Effect Effect)> Effects(Side actedOn, EffectKind kind, DieContext? die = null)` (ordinal order; conditions judged now, with the die when one is given), `LiveSet WithGranted(Side holder, string entry, int? parameter)` (a per-die grant folded in later); `static class Conditions { static bool Hold(Interaction, Side holder, Effect, LiveProperty source, DieContext? die) }` where `sealed record DieContext(string Roll, int Raw, int Modified)`; `static class LiveProperties { static LiveSet Settle(Interaction) }`; `static class Lookup` with `IEnumerable<PropertyInstance> UnitProperties(GameState, PropertySet, Guid unitId)` (the unit's own plus grants as instances, a grant's parameter read from its effect's amount), `IEnumerable<(Guid ModelId, PropertyInstance)> ModelProperties(GameState, Guid unitId, IEnumerable<Guid> modelIds)`, `IEnumerable<(Guid WeaponId, Guid ModelId, PropertyInstance)> WeaponProperties(GameState, IEnumerable<Guid> weaponIds)`, `bool UnitHas(GameState, PropertySet, Guid unitId, string entry)` (own properties, aggregated model properties, and the grants store), `int? UnitParameter(GameState, PropertySet, Guid unitId, string entry)`, `int? ModelParameter(GameState, Guid modelId, string entry)`, `IRL<Guid> LivingModels(GameState, Guid unitId)` (on the table — the models with a position), `IRL<Guid> AliveModels(GameState, Guid unitId)` (on the table **or** inside a container — every model still in play), `Guid? ContainerOf(GameState, Guid unitId)` (the container its models are inside, null when none is), `IRL<Guid> OccupantUnits(GameState, Guid containerModelId)` (the units with a model inside it, in projection order).
- **Two readings of "alive", and which is which.** [Game flow](../specs/2026-08-28-game-flow-design.md) → Inside a container: a model is in exactly one of three places, and *"a transported model is very much in play"* while the shelf is out of play. So `LivingModels` is *has a position* — every geometric question, every sight blocker — and `AliveModels` is *still in play* — how many models a unit has, who may be assigned a wound, whether a unit is destroyed. `Party.Whole` takes the second, so an occupant unit's own properties are still gathered when a wrecked transport wounds it (Task 19).
- Pass one: gather instances for both parties (unit properties; model properties aggregated per the entry's `Aggregation` over the party's models — `none` keeps one instance per model; weapon properties per weapon id; grants as instances whose `GrantId` is set); collapse duplicates by `(holder, entry)` unless `TakesParameter` (page 13: instances of the same rule don't stack unless it has (X)); then apply, in `Ordinal` order across every effect of kinds `GrantProperty`, `SuppressProperty`, `ModifyParameter`, `SuppressModifiers` whose conditions hold and reference no `RollResult` (those are per-die, Task 7): grant adds an instance on the acted-on side, suppress removes every instance of the entry there, modify-parameter adds to every instance's parameter there, suppress-modifiers records the class. The result is what pass two consumes.

Spec: effect grammar → Subject (`self`/`opponent` resolved from where the property is attached), Conditions (flat list, all hold; relational predicates take no subject), Properties act on other properties (two passes; `Poison` deletes `Regeneration`; `Lance` changes what `AP` contributes), Aggregation is not a predicate; identity and enumerations → The Stealth case; spell effects → The grants store ("read by the two effect passes alongside the unit's own properties, and by `GameRules`' name checks"); rulebook p.13 (instances don't stack unless (X)).

- [ ] **Step 1: Write the army builder the effect and sequence tests share, `Infrastructure/Armies.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>Units with named properties, placed on a 72x48 table. Team1's participant is P1; Team2's is P2.</summary>
public static class Armies
{
    public static readonly Guid P2 = Guid.CreateVersion7();
    public static PropertyInstance P(string name, int? parameter = null) => new(name, parameter);
    public static Weapon Gun(string name, string range, int attacks, params PropertyInstance[] props) =>
        new(Guid.CreateVersion7(), name, new Dictionary<string, string> { ["Range"] = range, ["Attacks"] = attacks.ToString() }, props);
    public static Weapon Melee(string name, int attacks, params PropertyInstance[] props) =>
        new(Guid.CreateVersion7(), name, new Dictionary<string, string> { ["Attacks"] = attacks.ToString() }, props);

    public static Unit Unit(string name, string quality, string defense, PropertyInstance[] unitProps, int models, PropertyInstance[] modelProps, params Weapon[] weapons) =>
        new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string> { ["Quality"] = quality, ["Defense"] = defense }, unitProps,
            Enumerable.Range(1, models).Select(i => new ModelSlot(Guid.CreateVersion7(), i, $"{name} {i}", new Dictionary<string, string>(), modelProps, Base.Circle(1.26), 1.5, null,
                weapons.Select(w => w with { Id = Guid.CreateVersion7() }).ToList())).ToList());

    /// <summary>A playing game: two armies, every model deployed in a row at the given x, one inch apart along y.</summary>
    public static GameState State(IReadOnlyList<Unit> team1, IReadOnlyList<Unit> team2, double x1 = -12, double x2 = 12, Enforcement enforcement = Enforcement.Warn, bool waits = false)
    {
        var info = Tabletop.Protocol.Tests.Samples.Game with { Enforcement = enforcement, WaitsBetweenSteps = waits,
            Participants = [new Participant(Tabletop.Protocol.Tests.Samples.P1, Guid.CreateVersion7(), "Clay", Tabletop.Protocol.Tests.Samples.Team1, null), new Participant(P2, Guid.CreateVersion7(), "Opponent", Tabletop.Protocol.Tests.Samples.Team2, null)] };
        var board = new Board(Tabletop.Content.Tests.TableStateTests.Sample() with { Terrain = [] },
            [new ArmyInPlay(Tabletop.Protocol.Tests.Samples.P1, new ArmySnapshot(Guid.CreateVersion7(), "One", "grimdark-future", team1)), new ArmyInPlay(P2, new ArmySnapshot(Guid.CreateVersion7(), "Two", "grimdark-future", team2))]);
        var applier = new EventApplier(board, info);
        var moves = new List<ModelRelocation>();
        foreach (var (units, x) in new[] { (team1, x1), (team2, x2) })
            foreach (var (u, ui) in units.Select((u, i) => (u, i)))
                foreach (var m in u.Models) moves.Add(new ModelRelocation(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(x, ui * 12 + m.Ordinal * 1.5 - 20, 0), new Yaw(0)))));
        applier.Apply(new GameEvent(1, null, new ModelsMoved(moves), null));
        return new GameState(info, board, applier, GrimdarkFutureRules.Instance);
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class LivePropertiesTests
{
    static readonly PropertySet Core = TestProperties.Core;

    [Fact]
    public void Poison_suppresses_regeneration_and_lance_changes_what_ap_contributes()
    {
        var lancers = Unit("Lancers", "4+", "4+", [], 3, [], Melee("Lance", 1, P("AP", 1), P("Lance"), P("Poison")));
        var trolls = Unit("Trolls", "4+", "4+", [], 2, [P("Regeneration"), P("Tough", 3)]);
        var state = State([lancers], [trolls]);
        var weapons = state.Projection.Units[lancers.Id].Definition.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
        var ix = new Interaction(state, Core, Party.Of(state, lancers.Id, lancers.Models.Select(m => m.Id), weapons), Party.Whole(state, trolls.Id), AttackKind.Melee, "Charge", 1, []);
        var live = LiveProperties.Settle(ix);
        Assert.False(live.Has(Side.Defender, "Regeneration"));
        Assert.True(live.Has(Side.Defender, "Tough"));
        Assert.Equal(2, live.Parameter(Side.Attacker, "AP"));                                       // AP(1) +1 from Lance, charging in melee
        Assert.Equal(2, live.Applied.Count(a => a.Effect.Kind is EffectKind.SuppressProperty or EffectKind.ModifyParameter));
        var shooting = LiveProperties.Settle(ix with { Attack = AttackKind.Shooting, ActionTaken = "Hold" });
        Assert.Equal(1, shooting.Parameter(Side.Attacker, "AP"));
        Assert.Equal(6, live.Parameter(Side.Defender, "Tough"));                                   // summed over two Tough(3) models
    }

    [Fact]
    public void Model_properties_aggregate_and_instances_of_a_plain_rule_do_not_stack()
    {
        var mixed = Unit("Mixed", "4+", "4+", [], 3, [P("Stealth"), P("Fearless"), P("Regeneration")]);
        var stealthy = mixed with { Models = mixed.Models.Select((m, i) => i == 0 ? m with { Properties = [P("Fearless")] } : m).ToList() };
        var state = State([Unit("Shooters", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1))], [stealthy]);
        var ix = new Interaction(state, Core, Party.Whole(state, state.Board.Armies[0].Snapshot.Units[0].Id), Party.Whole(state, stealthy.Id), AttackKind.Shooting, "Hold", 12, []);
        var live = LiveProperties.Settle(ix);
        Assert.False(live.Has(Side.Defender, "Stealth"));                                           // all: one model lacks it
        Assert.True(live.Has(Side.Defender, "Fearless"));                                           // majority: 3 of 3
        Assert.Equal(2, live.On(Side.Defender).Count(p => p.Entry.Name == "Regeneration"));         // none: per model, two models have it
        Assert.Single(live.On(Side.Defender).Where(p => p.Entry.Name == "Fearless"));               // collapsed: no (X)
    }

    [Fact]
    public void Grants_are_read_like_own_properties_and_an_opponent_grant_acts_on_the_other_side()
    {
        var a = Unit("A", "4+", "4+", [], 2, [], Melee("Claws", 1));
        var b = Unit("B", "4+", "4+", [], 2, []);
        var state = State([a], [b]);
        var apGrant = new Grant(Guid.CreateVersion7(), Core.Entry("Rending")!.Effects[1].Id, "Regeneration", a.Id, 1);   // granted Regeneration on B (entry name is what matters)
        state.Applier.Apply(new GameEvent(2, null, new GrantAdded(b.Id, apGrant), null));
        Assert.True(Lookup.UnitHas(state, Core, b.Id, "Regeneration"));
        Assert.False(Lookup.UnitHas(state, Core, a.Id, "Regeneration"));
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, a.Id), Party.Whole(state, b.Id), AttackKind.Melee, "Charge", 1, []));
        Assert.Equal(apGrant.Id, live.On(Side.Defender).Single(p => p.Entry.Name == "Regeneration").GrantId);
    }

    [Fact]
    public void Conditions_read_range_action_attack_kind_and_actor_state()
    {
        var e = Unit("Dug in", "4+", "4+", [], 2, [P("Entrenched")]);
        var s = Unit("Shooters", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var state = State([s], [e]);
        Interaction Ix(double range, string action) => new(state, Core, Party.Whole(state, s.Id), Party.Whole(state, e.Id), AttackKind.Shooting, action, range, []);
        var entrenched = Core.Entry("Entrenched")!.Effects[0];
        var holder = new LiveProperty(Side.Defender, Core.Entry("Entrenched")!, null, e.Models[0].Id, null, null);
        Assert.True(Conditions.Hold(Ix(12, "Hold"), Side.Defender, entrenched, holder, null));
        Assert.False(Conditions.Hold(Ix(9, "Hold"), Side.Defender, entrenched, holder, null));
        state.Applier.Apply(new GameEvent(2, null, new ConditionSet(new Subject(SubjectKind.Unit, e.Id), "moved", true), null));
        Assert.False(Conditions.Hold(Ix(12, "Hold"), Side.Defender, entrenched, holder, null));
        var furious = Core.Entry("Furious")!.Effects[0];
        var fh = new LiveProperty(Side.Attacker, Core.Entry("Furious")!, null, null, null, null);
        Assert.True(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, furious, fh, new DieContext("hit", 6, 5)));
        Assert.False(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, furious, fh, new DieContext("hit", 5, 6)));
        Assert.False(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, furious, fh, null));   // a roll_result predicate cannot hold before the die
    }

    [Fact]
    public void A_model_inside_a_container_is_still_in_play_but_has_no_position()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 2, [P("Regeneration")]);
        var state = State([bus, troops], [Unit("T", "4+", "4+", [], 1, [])]);
        var container = bus.Models[0].Id;
        state.Applier.Apply(new GameEvent(2, null, new ModelsMoved(troops.Models
            .Select(m => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new Inside(container))).ToList()), null));
        Assert.Empty(Lookup.LivingModels(state, troops.Id));                                       // no position: no distance, no line of sight, no cover
        Assert.Equal(2, Lookup.AliveModels(state, troops.Id).Count);                               // still in play
        Assert.Equal(container, Lookup.ContainerOf(state, troops.Id));
        Assert.Null(Lookup.ContainerOf(state, bus.Id));
        Assert.Equal([troops.Id], Lookup.OccupantUnits(state, container));
        // Party.Whole is the in-play reading, so an occupant's own properties still settle — which is what lets a wrecked transport wound it.
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, troops.Id), null, null, null, null, []));
        Assert.Equal(2, live.On(Side.Attacker).Count(p => p.Entry.Name == "Regeneration"));
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `Interaction.cs` and `Lookup.cs`**

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

public enum AttackKind { Shooting, Melee }
public enum Side { Attacker, Defender }

/// <summary>One side of an interaction: the unit, the models taking part, the weapons in use.</summary>
public sealed record Party(Guid UnitId, IReadOnlyList<Guid> ModelIds, IReadOnlyList<Guid> WeaponIds)
{
    public static Party Whole(GameState state, Guid unitId) => new(unitId, Lookup.AliveModels(state, unitId), []);
    public static Party Of(GameState state, Guid unitId, IEnumerable<Guid> modelIds, IEnumerable<Guid> weaponIds) => new(unitId, modelIds.ToList(), weaponIds.ToList());
}

/// <summary>What the two passes evaluate against. Defender is null for a lone roll (morale, casting, dangerous terrain).</summary>
public sealed record Interaction(GameState State, PropertySet Properties, Party Attacker, Party? Defender, AttackKind? Attack, string? ActionTaken, double? RangeInches, IReadOnlyList<string> DefenderTerrain)
{
    public Party Of(Side side) => side == Side.Attacker ? Attacker : Defender ?? throw new InvalidOperationException("no defender in this interaction");
    public static Side Other(Side side) => side == Side.Attacker ? Side.Defender : Side.Attacker;
    public int Round => State.Projection.Scheduler.Round;
}
```

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

/// <summary>Name checks and instance gathering over a unit's own properties, its models' and weapons', and the grants store.</summary>
public static class Lookup
{
    /// <summary>The unit's models that have a position. Every geometric question asks this one.</summary>
    public static IReadOnlyList<Guid> LivingModels(GameState state, Guid unitId) =>
        state.Projection.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).Select(m => m.Id).ToList();

    /// <summary>The unit's models still in play — on the table or inside a container. Counts, wound allocation and "is it destroyed" ask this one (game flow → Inside a container: the shelf is out of play; a transported model is not).</summary>
    public static IReadOnlyList<Guid> AliveModels(GameState state, Guid unitId) =>
        state.Projection.Models.Values.Where(m => m.UnitId == unitId && m.Location is not OnShelf).Select(m => m.Id).ToList();

    /// <summary>The container this unit is inside, or null. A unit embarks and disembarks whole, so the first occupant model answers for all.</summary>
    public static Guid? ContainerOf(GameState state, Guid unitId) =>
        state.Projection.Models.Values.FirstOrDefault(m => m.UnitId == unitId && m.Location is Inside) is { Location: Inside i } ? i.ContainerId : null;

    /// <summary>The units with at least one model inside the given container model, in projection order.</summary>
    public static IReadOnlyList<Guid> OccupantUnits(GameState state, Guid containerModelId) =>
        state.Projection.Models.Values.Where(m => m.Location is Inside i && i.ContainerId == containerModelId).Select(m => m.UnitId).Distinct().ToList();

    /// <summary>The unit's own properties plus its grants as instances. A grant's parameter is the granting effect's amount ("gets AP(1)" → AP with parameter 1), read from the pinned set by Grant.EffectId.</summary>
    public static IEnumerable<PropertyInstance> UnitProperties(GameState state, PropertySet props, Guid unitId)
    {
        var unit = state.Projection.Units[unitId];
        return unit.Definition.Properties.Concat(unit.Grants.Select(g => new PropertyInstance(g.EntryName, props.AllEffects().FirstOrDefault(e => e.Id == g.EffectId)?.Amount)));
    }

    public static IEnumerable<(Guid ModelId, PropertyInstance Property)> ModelProperties(GameState state, Guid unitId, IEnumerable<Guid> modelIds) =>
        modelIds.SelectMany(id => state.Projection.Models[id].Slot.Properties.Select(p => (id, p)));

    public static IEnumerable<(Guid WeaponId, Guid ModelId, PropertyInstance Property)> WeaponProperties(GameState state, IEnumerable<Guid> weaponIds)
    {
        var wanted = weaponIds.ToHashSet();
        return state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons.Where(w => wanted.Contains(w.Id)).SelectMany(w => w.Properties.Select(p => (w.Id, m.Id, p))));
    }

    /// <summary>Own properties, model properties under the entry's aggregation over living models, and grants.</summary>
    public static bool UnitHas(GameState state, PropertySet props, Guid unitId, string entry)
    {
        if (UnitProperties(state, props, unitId).Any(p => p.Name == entry)) return true;
        var living = LivingModels(state, unitId);
        var with = living.Count(id => state.Projection.Models[id].Slot.Properties.Any(p => p.Name == entry));
        return props.Entry(entry)?.Aggregation switch
        {
            Aggregation.All => living.Count > 0 && with == living.Count, Aggregation.Any or Aggregation.None => with > 0,
            Aggregation.Majority => with * 2 > living.Count, _ => false,
        };
    }

    public static int? UnitParameter(GameState state, PropertySet props, Guid unitId, string entry)
    {
        var own = UnitProperties(state, props, unitId).Where(p => p.Name == entry).Select(p => p.Parameter).ToList();
        if (own.Count > 0) return own.Sum(p => p ?? 0);
        var model = LivingModels(state, unitId).Select(id => ModelParameter(state, id, entry)).Where(p => p is not null).ToList();
        return model.Count == 0 ? null : model.Sum(p => p ?? 0);
    }

    public static int? ModelParameter(GameState state, Guid modelId, string entry) =>
        state.Projection.Models[modelId].Slot.Properties.FirstOrDefault(p => p.Name == entry)?.Parameter;
}
```

- [ ] **Step 4: Write `Conditions.cs`**

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

public sealed record DieContext(string Roll, int Raw, int Modified);

/// <summary>The eight predicates. A subject-bearing predicate tests the side it names relative to the holder; relational ones test the interaction.</summary>
public static class Conditions
{
    public static bool Hold(Interaction ix, Side holder, Effect effect, LiveProperty source, DieContext? die) =>
        effect.Conditions.All(c => Holds(ix, holder, c, source, die));

    public static bool Holds(Interaction ix, Side holder, EffectCondition c, LiveProperty source, DieContext? die)
    {
        var side = c.Subject == EffectSubject.Opponent ? Interaction.Other(holder) : holder;
        switch (c.Kind)
        {
            case ConditionKind.ActionTaken: return ix.ActionTaken is { } a && Membership(c, a);
            case ConditionKind.AttackKind: return ix.Attack is { } k && Membership(c, k == AttackKind.Shooting ? "shooting" : "melee");
            case ConditionKind.Range: return ix.RangeInches is { } r && c.Compare((int)Math.Floor(r));
            case ConditionKind.RollResult:
                if (die is null || c.Values.Count != 2 || die.Roll != c.Values[0]) return false;
                return c.Compare(c.Values[1] == "raw" ? die.Raw : die.Modified);
            case ConditionKind.ActorState:
            {
                var unit = ix.State.Projection.Units[ix.Of(side).UnitId];
                var present = c.Values.Any(v => unit.Conditions.Contains(v) || (source.ModelId is { } m && ix.Of(side).ModelIds.Contains(m) && ix.State.Projection.Models[m].Conditions.Contains(v)));
                return c.Comparison == Comparison.Neq ? !present : present;
            }
            // The terrain list describes the defender, or the lone party when there is no defender (morale, a move, a dangerous terrain test).
            case ConditionKind.PositionTerrain: return Membership(c, ix.DefenderTerrain, ix.Defender is null || side == Side.Defender);
            case ConditionKind.RoundNumber: return c.Compare(ix.Round);
            case ConditionKind.UsageCount: return c.Compare(Usage(ix, source));
            default: return false;
        }
    }

    static bool Membership(EffectCondition c, string actual) => c.Comparison == Comparison.Neq ? !c.Values.Contains(actual) : c.Values.Contains(actual);
    static bool Membership(EffectCondition c, IReadOnlyList<string> actual, bool applies) => applies && (c.Comparison == Comparison.Neq ? !actual.Intersect(c.Values).Any() : actual.Intersect(c.Values).Any());

    /// <summary>How many sequences in the live log have used this weapon: the usage record Limited reads.</summary>
    static int Usage(Interaction ix, LiveProperty source) => source.WeaponId is not { } w ? 0
        : EventApplier.EffectiveLog(ix.State.Applier.Log).Count(e => e.Payload is SequenceStarted s && s.Context.WeaponIds.Contains(w));
}
```

- [ ] **Step 5: Write `LiveProperties.cs`**

```csharp
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Effects;

/// <summary>One instance of a catalog entry in play: who holds it and where it came from.</summary>
public sealed record LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId)
{
    public Side ActedOn(Effect e) => e.Subject == EffectSubject.Self ? Holder : Interaction.Other(Holder);
}

/// <summary>Pass one's result: the properties that are live, the modifier classes suppressed, and what was applied to get there.</summary>
public sealed class LiveSet(Interaction interaction, IReadOnlyList<LiveProperty> live, IReadOnlySet<TargetModifier> suppressedModifiers, IReadOnlyList<(LiveProperty Source, Effect Effect)> applied)
{
    public Interaction Interaction { get; } = interaction;
    public IReadOnlyList<LiveProperty> Live { get; } = live;
    public IReadOnlySet<TargetModifier> SuppressedModifiers { get; } = suppressedModifiers;
    public IReadOnlyList<(LiveProperty Source, Effect Effect)> Applied { get; } = applied;

    public IEnumerable<LiveProperty> On(Side side) => Live.Where(p => p.Holder == side);
    public bool Has(Side side, string entry) => On(side).Any(p => p.Entry.Name == entry);
    public int? Parameter(Side side, string entry)
    {
        var instances = On(side).Where(p => p.Entry.Name == entry).ToList();
        return instances.Count == 0 ? null : instances.Sum(p => p.Parameter ?? 0);
    }

    /// <summary>Effects of one kind acting on a side, in ordinal order, whose conditions hold now (a per-die condition is judged with the die).</summary>
    public IEnumerable<(LiveProperty Source, Effect Effect)> Effects(Side actedOn, EffectKind kind, DieContext? die = null) =>
        Live.SelectMany(p => p.Entry.Effects.Where(e => e.Kind == kind && p.ActedOn(e) == actedOn && Conditions.Hold(Interaction, p.Holder, e, p, die)).Select(e => (p, e)))
            .OrderBy(x => x.e.Ordinal).Select(x => (x.p, x.e));

    public LiveSet WithGranted(Side holder, string entry, int? parameter) =>
        Interaction.Properties.Entry(entry) is { } ce ? new LiveSet(Interaction, [.. Live, new LiveProperty(holder, ce, parameter, null, null, null)], SuppressedModifiers, Applied) : this;
}

public static class LiveProperties
{
    static readonly EffectKind[] PassOne = [EffectKind.GrantProperty, EffectKind.SuppressProperty, EffectKind.ModifyParameter, EffectKind.SuppressModifiers];

    public static LiveSet Settle(Interaction ix)
    {
        var live = Gather(ix, Side.Attacker).ToList();
        if (ix.Defender is not null) live.AddRange(Gather(ix, Side.Defender));
        var suppressed = new HashSet<TargetModifier>();
        var applied = new List<(LiveProperty, Effect)>();
        // Every pass-one effect whose conditions hold now, across both sides, in ordinal order. Per-die grants (a roll_result condition) wait for pass two.
        var candidates = live.SelectMany(p => p.Entry.Effects.Where(e => PassOne.Contains(e.Kind) && e.Conditions.All(c => c.Kind != ConditionKind.RollResult)).Select(e => (p, e)))
            .OrderBy(x => x.e.Ordinal).ToList();
        foreach (var (source, effect) in candidates)
        {
            if (!live.Contains(source) || !Conditions.Hold(ix, source.Holder, effect, source, null)) continue;   // suppressed earlier this pass, or conditions fail
            var side = source.ActedOn(effect);
            switch (effect.Kind)
            {
                case EffectKind.GrantProperty when ix.Properties.Entry(effect.TargetEntry!) is { } granted:
                    live.Add(new LiveProperty(side, granted, effect.Amount, source.ModelId, source.WeaponId, null)); break;
                case EffectKind.SuppressProperty: live.RemoveAll(p => p.Holder == side && p.Entry.Name == effect.TargetEntry); break;
                case EffectKind.ModifyParameter:
                    for (var i = 0; i < live.Count; i++)
                        if (live[i].Holder == side && live[i].Entry.Name == effect.TargetEntry) live[i] = live[i] with { Parameter = (live[i].Parameter ?? 0) + effect.EffectiveAmount(source.Parameter) };
                    break;
                case EffectKind.SuppressModifiers: suppressed.Add(effect.TargetModifier!.Value); break;
            }
            applied.Add((source, effect));
        }
        return new LiveSet(ix, live, suppressed, applied);
    }

    /// <summary>Unit properties and grants, model properties under aggregation, weapon properties; duplicates collapsed unless the rule takes (X).</summary>
    static IEnumerable<LiveProperty> Gather(Interaction ix, Side side)
    {
        var party = ix.Of(side);
        var state = ix.State;
        var unit = state.Projection.Units[party.UnitId];
        var result = new List<LiveProperty>();
        foreach (var p in unit.Definition.Properties) if (ix.Properties.Entry(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, null, null, null));
        foreach (var g in unit.Grants) if (ix.Properties.Entry(g.EntryName) is { } e)
            result.Add(new LiveProperty(side, e, ix.Properties.AllEffects().FirstOrDefault(x => x.Id == g.EffectId)?.Amount, null, null, g.Id));
        var models = party.ModelIds.Count > 0 ? party.ModelIds : Lookup.LivingModels(state, party.UnitId);
        foreach (var group in Lookup.ModelProperties(state, party.UnitId, models).GroupBy(x => x.Property.Name))
        {
            if (ix.Properties.Entry(group.Key) is not { } e) continue;
            var count = group.Select(x => x.ModelId).Distinct().Count();
            var live = e.Aggregation switch { Aggregation.All => count == models.Count, Aggregation.Any => count > 0, Aggregation.Majority => count * 2 > models.Count, _ => true };
            if (!live) continue;
            if (e.Aggregation == Aggregation.None) result.AddRange(group.Select(x => new LiveProperty(side, e, x.Property.Parameter, x.ModelId, null, null)));
            else result.Add(new LiveProperty(side, e, group.First().Property.Parameter, null, null, null));
        }
        foreach (var (weaponId, modelId, p) in Lookup.WeaponProperties(state, party.WeaponIds))
            if (ix.Properties.Entry(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, modelId, weaponId, null));
        // Page 13: instances of the same rule don't stack unless it has (X). Per-model rules (aggregation none) keep one instance per model;
        // a weapon rule is one rule per weapon *type*, so three Lances' AP(1) are one AP(1) and the group's parameter is the weapon's, not the sum.
        string WeaponName(Guid? id) => id is { } w ? state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(x => x.Id == w).Name : "";
        return result.GroupBy(p => (p.Entry.Name, p.Entry.Aggregation == Aggregation.None ? p.ModelId ?? Guid.Empty : Guid.Empty, p.WeaponId is null ? "" : WeaponName(p.WeaponId), p.GrantId ?? Guid.Empty))
            .Select(g => g.First());
    }
}
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.GameServer.Tests --filter LivePropertiesTests`; expect 5 passed. Commit: `Settle which properties are live: pass one of the effect grammar`.**

### Task 7: Pass two — what the live properties do to a roll, a stat and a die

**Files:**
- Create: `server/game/Effects/RollEffects.cs`, `server/game/Effects/StatReader.cs`
- Test: `tests/Tabletop.GameServer.Tests/RollEffectsTests.cs`

**Interfaces:**
- Consumes: Tasks 5–6; `StatValue` (Content).
- Produces: `static class StatReader` with `int TargetNumber(LiveSet, Side, StatRef)` (the side's stat text read as a target number, then `ReplaceStat` effects in ordinal order — last wins — then `ModifyStat` amounts summed), `double? WeaponRange(LiveSet, Side, Guid weaponId)` (null for a melee weapon; `ModifyStat` on `Range` applied; negative amounts dropped when `RangePenalties` is suppressed), `int Attacks(GameState, Guid weaponId)`, `double ActionDistance(LiveSet, Side, string action)` (the action row's distance plus `ModifyStat` effects whose `TargetAction` is it); `sealed record DieEffects(int ExtraHits, IRL<(string Entry, int? Parameter)> Grants)`; `static class RollEffects` with `RollPlan Plan(LiveSet, Side roller, string roll, int? threshold, IEnumerable<RollModifier> baseModifiers, RollConvention, int? requiredRaw, string hitRollName)` (adds one `RollModifier(entry name, amount)` per `ModifyRoll` effect acting on the roller for this roll; drops `cover`-sourced base modifiers when `Cover` is suppressed and negative modifiers when `ToHitPenalties` is suppressed and the roll is a hit roll — the roll named by the ruleset as its hit roll is passed in as `hitRollName`; collects `RerollRaw` from `ForceReroll` effects on this roll whose only unmet condition is the die), `DieEffects PerDie(LiveSet, Side roller, Die die, string roll)` (`AddHit` amounts and `GrantProperty` entries whose conditions hold for this die), `int HitMultiplier(LiveSet, Side attacker, int defenderModels)` (product of `Multiply hits` amounts, each capped by `CapKind`), `int WoundMultiplier(LiveSet, Side attacker)`, `IRL<(LiveProperty Source, Effect Roll)> OwnRolls(LiveSet, Side, Func<Effect, bool> childFilter)` (kind-5 effects whose child matches: `Regeneration` for a wound, `Fearless` for a morale test, `Impact` for hits).
- The hit roll's name is the ruleset's; `Plan` takes it as a parameter so `ToHitPenalties` can be applied without the effects module knowing OPR's roll names.

Spec: effect grammar → The twelve effect kinds (1–8), "Unmodified" is a first-class concept, Multiplication carries a cap; action sequences → One roll primitive ("Effects apply at the roll or step they attach to, via the two passes"); resolution → Modifiers and abilities are different things; rulebook p.13–14 (`Lock-On`, `Blast`, `Furious`, `Rending`, `Reliable`, `Poison`).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class RollEffectsTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static readonly RollConvention D6 = new(6, null);
    static readonly StatRef Quality = new(StatLevel.Unit, "Quality"), Defense = new(StatLevel.Unit, "Defense");

    static (LiveSet Live, Tabletop.Content.Unit A, Tabletop.Content.Unit D) Shooting(Tabletop.Content.Unit shooter, Tabletop.Content.Unit target, double range, string action = "Hold")
    {
        var state = State([shooter], [target]);
        var weapons = shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
        return (LiveProperties.Settle(new Interaction(state, Core, Party.Of(state, shooter.Id, shooter.Models.Select(m => m.Id), weapons), Party.Whole(state, target.Id), AttackKind.Shooting, action, range, [])), shooter, target);
    }

    [Fact]
    public void Stealth_and_aircraft_modify_the_hit_roll_and_lock_on_ignores_the_penalties_and_cover()
    {
        var (live, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)), Unit("T", "4+", "4+", [P("Aircraft")], 2, [P("Stealth")]), 12);
        var plan = RollEffects.Plan(live, Side.Attacker, "hit", StatReader.TargetNumber(live, Side.Attacker, Quality), [], D6, null, hitRollName: "hit");
        Assert.Equal(4, plan.Threshold); Assert.Equal(-2, plan.NetModifier);
        Assert.Equal(["Aircraft", "Stealth"], plan.Modifiers.Select(m => m.Source).OrderBy(s => s));
        var (locked, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Beam", "24\"", 1, P("Lock-On"))), Unit("T", "4+", "4+", [P("Aircraft")], 2, [P("Stealth")]), 12);
        Assert.Equal(0, RollEffects.Plan(locked, Side.Attacker, "hit", 4, [], D6, null, "hit").NetModifier);
        var defense = RollEffects.Plan(locked, Side.Defender, "defense", 4, [new RollModifier("cover", 1)], D6, null, "hit");
        Assert.Empty(defense.Modifiers);                                                                          // cover suppressed
        Assert.Equal(24.0, StatReader.WeaponRange(locked, Side.Attacker, locked.Interaction.Attacker.WeaponIds[0]));   // -12" range penalty ignored
        Assert.Equal(12.0, StatReader.WeaponRange(live, Side.Attacker, live.Interaction.Attacker.WeaponIds[0]));
    }

    [Fact]
    public void Reliable_replaces_quality_ap_modifies_defense_and_poison_forces_a_reroll_of_sixes()
    {
        var (live, _, _) = Shooting(Unit("S", "5+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Reliable"), P("AP", 2), P("Poison"))), Unit("T", "4+", "3+", [], 2, [P("Regeneration")]), 6);
        Assert.Equal(2, StatReader.TargetNumber(live, Side.Attacker, Quality));
        var defense = RollEffects.Plan(live, Side.Defender, "defense", StatReader.TargetNumber(live, Side.Defender, Defense), [], D6, null, "hit");
        Assert.Equal((3, -2), (defense.Threshold, defense.NetModifier));
        Assert.Equal([6], defense.RerollRaw);
        Assert.Empty(RollEffects.OwnRolls(live, Side.Defender, c => c.Kind == EffectKind.OverrideResult && c.TargetResult == TargetResult.Wound));   // Regeneration suppressed
    }

    [Fact]
    public void Per_die_effects_add_hits_and_grant_on_unmodified_sixes_and_blast_multiplies_with_a_cap()
    {
        var charger = Unit("C", "4+", "4+", [], 3, [P("Furious")], Melee("Blade", 1, P("Rending")));
        var state = State([charger], [Unit("T", "4+", "4+", [], 2, [])]);
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Of(state, charger.Id, charger.Models.Select(m => m.Id), charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id))), Party.Whole(state, state.Board.Armies[1].Snapshot.Units[0].Id), AttackKind.Melee, "Charge", 1, []));
        var six = RollEffects.PerDie(live, Side.Attacker, new Die(6, 6, true), "hit");
        Assert.Equal(1, six.ExtraHits); Assert.Equal([("AP", (int?)4)], six.Grants);
        var five = RollEffects.PerDie(live, Side.Attacker, new Die(5, 6, true), "hit");
        Assert.Equal((0, 0), (five.ExtraHits, five.Grants.Count));
        var (blast, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Mortar", "30\"", 1, P("Blast", 3))), Unit("T", "4+", "4+", [], 2, []), 12);
        Assert.Equal(2, RollEffects.HitMultiplier(blast, Side.Attacker, defenderModels: 2));
        Assert.Equal(3, RollEffects.HitMultiplier(blast, Side.Attacker, defenderModels: 5));
        Assert.Equal(1, RollEffects.WoundMultiplier(blast, Side.Attacker));
        var (deadly, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Cannon", "30\"", 1, P("Deadly", 3))), Unit("T", "4+", "4+", [], 2, []), 12);
        Assert.Equal(3, RollEffects.WoundMultiplier(deadly, Side.Attacker));
    }

    [Fact]
    public void Action_distances_carry_fast_and_slow()
    {
        var fast = Unit("F", "4+", "4+", [], 2, [P("Fast")]);
        var state = State([fast], [Unit("T", "4+", "4+", [], 1, [])]);
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, fast.Id), null, null, "Advance", null, []));
        Assert.Equal((8.0, 16.0, 0.0), (StatReader.ActionDistance(live, Side.Attacker, "Advance"), StatReader.ActionDistance(live, Side.Attacker, "Charge"), StatReader.ActionDistance(live, Side.Attacker, "Hold")));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `StatReader.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;

namespace Tabletop.GameServer.Effects;

/// <summary>Stats and distances as the live properties leave them: replace first (ordinal order, last wins), then modify.</summary>
public static class StatReader
{
    public static int TargetNumber(LiveSet live, Side side, StatRef stat)
    {
        var state = live.Interaction.State;
        var unit = state.Projection.Units[live.Interaction.Of(side).UnitId];
        var text = stat.Level == StatLevel.Unit ? unit.Definition.Stats[stat.Name] : throw new NotSupportedException("only unit-level target numbers are read here; model-level stats are a second ruleset's");
        var value = StatValue.Read(StatKind.TargetNumber, text).TargetNumber;
        foreach (var (_, e) in live.Effects(side, EffectKind.ReplaceStat).Where(x => x.Effect.TargetStat == stat)) value = StatValue.Read(StatKind.TargetNumber, e.Replacement!).TargetNumber;
        foreach (var (src, e) in live.Effects(side, EffectKind.ModifyStat).Where(x => x.Effect.TargetStat == stat)) value += e.EffectiveAmount(src.Parameter);
        return value;
    }

    public static double? WeaponRange(LiveSet live, Side side, Guid weaponId)
    {
        var weapon = live.Interaction.State.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(w => w.Id == weaponId);
        if (!weapon.Stats.TryGetValue("Range", out var text)) return null;
        var range = StatValue.Read(StatKind.Distance, text).Inches;
        var rangeRef = new StatRef(StatLevel.Weapon, "Range");
        foreach (var (src, e) in live.Effects(side, EffectKind.ModifyStat).Where(x => x.Effect.TargetStat == rangeRef))
        {
            var amount = e.EffectiveAmount(src.Parameter);
            if (amount < 0 && live.SuppressedModifiers.Contains(TargetModifier.RangePenalties)) continue;
            range += amount;
        }
        return Math.Max(0, range);
    }

    public static int Attacks(GameState state, Guid weaponId) =>
        StatValue.Read(StatKind.Count, state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(w => w.Id == weaponId).Stats["Attacks"]).Count;

    public static double ActionDistance(LiveSet live, Side side, string action)
    {
        var distance = live.Interaction.Properties.Action(action).Distance;
        foreach (var (src, e) in live.Effects(side, EffectKind.ModifyStat).Where(x => x.Effect.TargetAction == action)) distance += e.EffectiveAmount(src.Parameter);
        return Math.Max(0, distance);
    }
}
```

The stat names `Range` and `Attacks` appear here because `StatKind.Distance` and `StatKind.Count` need a *which* — they are the weapon stat declared as the distance the weapon reaches and the count of attacks. Task 16's constraint test whitelists exactly these two strings in `Effects/`; a second ruleset with differently named weapon stats moves them onto `IGameRules` (question 5).

- [ ] **Step 3: Write `RollEffects.cs`**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

public sealed record DieEffects(int ExtraHits, IReadOnlyList<(string Entry, int? Parameter)> Grants);

public static class RollEffects
{
    /// <summary>The roll as the live set leaves it. Summing is generic; the convention clamps. Suppressed modifier classes are removed before summing.</summary>
    public static RollPlan Plan(LiveSet live, Side roller, string roll, int? threshold, IEnumerable<RollModifier> baseModifiers, RollConvention convention, int? requiredRaw, string hitRollName)
    {
        var modifiers = baseModifiers.ToList();
        foreach (var (src, e) in live.Effects(roller, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == roll))
            modifiers.Add(new RollModifier(src.Entry.Name, e.EffectiveAmount(src.Parameter)));
        if (live.SuppressedModifiers.Contains(TargetModifier.Cover)) modifiers.RemoveAll(m => m.Source == "cover");
        if (roll == hitRollName && live.SuppressedModifiers.Contains(TargetModifier.ToHitPenalties)) modifiers.RemoveAll(m => m.Amount < 0);
        // ForceReroll: its roll_result condition names the face; every other condition must hold now.
        var reroll = new HashSet<int>();
        foreach (var p in live.Live)
            foreach (var e in p.Entry.Effects.Where(e => e.Kind == EffectKind.ForceReroll && e.TargetRoll == roll && p.ActedOn(e) == roller))
            {
                var face = e.Conditions.FirstOrDefault(c => c.Kind == ConditionKind.RollResult && c.Values.Count == 2 && c.Values[0] == roll && c.Values[1] == "raw");
                if (face?.Number is { } n && e.Conditions.Where(c => c != face).All(c => Conditions.Holds(live.Interaction, p.Holder, c, p, null))) reroll.Add(n);
            }
        return new RollPlan(roll, threshold, modifiers, convention, requiredRaw, reroll);
    }

    /// <summary>What one die does beyond its success: extra hits (Furious, Relentless) and per-hit grants (Rending's AP(4)).</summary>
    public static DieEffects PerDie(LiveSet live, Side roller, Die die, string roll)
    {
        var ctx = new DieContext(roll, die.Raw, die.Modified);
        var extra = live.Effects(roller, EffectKind.AddHit, ctx).Where(x => x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult)).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        var grants = live.Effects(roller, EffectKind.GrantProperty, ctx).Where(x => x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult))
            .Select(x => (x.Effect.TargetEntry!, x.Effect.Amount)).ToList();
        return new DieEffects(extra, grants);
    }

    public static int HitMultiplier(LiveSet live, Side attacker, int defenderModels) => Multiplier(live, attacker, TargetQuantity.Hits, defenderModels);
    public static int WoundMultiplier(LiveSet live, Side attacker) => Multiplier(live, attacker, TargetQuantity.Wounds, int.MaxValue);

    static int Multiplier(LiveSet live, Side side, TargetQuantity quantity, int defenderModels)
    {
        var product = 1;
        foreach (var (src, e) in live.Effects(side, EffectKind.Multiply).Where(x => x.Effect.TargetQuantity == quantity))
        {
            var x = Math.Max(1, e.EffectiveAmount(src.Parameter));
            if (e.CapKind == CapKind.ModelsInTargetUnit) x = Math.Min(x, Math.Max(1, defenderModels));
            product *= x;
        }
        return product;
    }

    /// <summary>Kind-5 rolls a property owns (Regeneration, Fearless, Impact), selected by what their per-success child does.</summary>
    public static IReadOnlyList<(LiveProperty Source, Effect Roll)> OwnRolls(LiveSet live, Side side, Func<Effect, bool> childFilter) =>
        live.Effects(side, EffectKind.Roll).Where(x => x.Effect.Children.Any(childFilter)).ToList();
}
```

`LiveSet.Effects(side, kind, die)` evaluates conditions with the die when one is given; `PerDie` passes it and keeps only effects that actually have a `RollResult` condition, so an unconditional `AddHit` (a spell's *takes 4 hits*, Task 17) is not counted per die.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter RollEffectsTests`; expect 4 passed. Commit: `Apply the live properties to rolls, stats and dice: pass two`.**

### Task 8: The interpreter — frames from the log, steps, outcomes, confirm gates

**Files:**
- Create: `server/game/Interpreter/Steps.cs`, `server/game/Interpreter/FrameReader.cs`, `server/game/Interpreter/StepContext.cs`, `server/game/Interpreter/SequenceInterpreter.cs`
- Modify: `server/game/Rules/IGameRules.cs` (the interpreter's members, added beside the scheduler plan's)
- Create: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (this plan's partial of the skeleton's `GrimdarkFutureRules`, with `NotImplementedException` bodies until Tasks 11–17 fill them)
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs`, `tests/Tabletop.GameServer.Tests/InterpreterTests.cs`

**Interfaces:**
- Consumes: Tasks 5–7; `Projection.Stack`, `Frame`, `OpenAsk`, `Ask`, `AskQuestions.Confirm`, `Confirmation`, `SequenceStarted/Finished/Abandoned`, `StepCompleted` (with `Values`, Interfaces needed 1), `AskOpened/Closed`, `TeamAnswer` (Protocol).
- Produces (namespace `Tabletop.GameServer.Interpreter`): `interface IStep { string Name { get; } StepOutcome Run(StepContext ctx); }`; `sealed record Sequence(string Name, IRL<IStep> Steps)`; `abstract record StepOutcome` with `Complete(IRL<StateChange> Changes, IRD<string,int>? Values = null, bool Repeat = false)` (`Repeat`: the step runs again with a fresh history and the same position — how a move step takes its next segment), `Finish(IRL<StateChange> Changes, IRD<string,int>? Values = null)` (completes the step and ends the sequence), `OpenAsks(IRL<Ask> Asks)`, `Push(string Sequence, SequenceContext Context)`; `sealed record AskRecord(Ask Ask, IRD<Guid, AskAnswer>? Answers)` with `bool Closed`, `AskAnswer? Single`; `sealed record ChildRecord(Guid FrameId, string Sequence, SequenceContext Context, bool Finished, bool Abandoned)`; `sealed record StepHistory(IRL<AskRecord> Asks, IRL<ChildRecord> Children)` with `bool Waiting` (an Ask still open), `AskRecord? LastClosed(string? question = null)`, `int Confirmed`; `sealed record FrameView(Frame Frame, int Position, IRD<string,int> Values, StepHistory History, FrameView? Parent)` with `Guid Id`, `SequenceContext Context`, `int Value(string key, int fallback = 0)`; `static class FrameReader { static IRL<FrameView> Read(GameState state); }` (bottom first); `interface ISequenceStarter { IRL<EventPayload> Start(GameState, string, SequenceContext); }`; `sealed class StepContext` with `GameState State`, `PropertySet Properties`, `IGameRules Rules`, `FrameView Frame`, `StepHistory History` (= `Frame.History`), `Projection Projection`, `Guid? ActorTeam`, `Guid? TeamOf(Guid unitId)`, `RollRecord Roll(RollPlan plan, int count)` (through a `Roller` at the log's cursor; records accumulate for the completing payload), `IRL<RollRecord> Rolls`, `IRL<DiceRerolled> ForcedRerolls`, `RollPlan Plan(LiveSet live, Side roller, string roll, IEnumerable<RollModifier> baseModifiers, int? requiredRaw = null)` (threshold from `Rules.RollSpec(roll)`), `Ask Ask(string question, string prompt, Guid team, LegalSet legal, bool allowPass = false, string? windowKind = null)` (an Ask on this frame); `sealed class SequenceInterpreter(IGameRules rules, PropertySet properties, IDiceSource dice) : ISequenceStarter` (named so because it lives in the `Interpreter` namespace) with `IRL<EventPayload> Advance(GameState state)` and `IRL<EventPayload> Start(GameState state, string sequence, SequenceContext context)`; `static class Teams { static Guid? Of(GameState, Guid unitId); }`.
- Produces on `IGameRules` (namespace `Tabletop.GameServer.Rules`): `Sequence Sequence(string name)` (throws `UnknownSequenceException(string)`); `string? ActionSequence(string actionName)` (the sequence an `action` row starts; null when the action has none — an out-of-set name accepted under `warn`/`silent`, which the scheduler records without a sequence); `RollSpec RollSpec(string roll)` with `sealed record RollSpec(string Roll, RollConvention Convention, Func<LiveSet, Side, int?> Threshold)`; `string HitRoll { get; }` (the roll `ToHitPenalties` suppresses); `IRL<Reaction> ReactionsAt(GameState, PropertySet, string point, FrameView frame)` (Task 9); `IRL<SequenceStart> EndOfTurnSequences(GameState, PropertySet, TurnContext turn)`, `IRL<StateChange> OnRoundStarted(GameState, PropertySet, int round)`, `IRL<StateChange> OnTurnEnded(GameState, PropertySet, TurnContext turn)` (Task 11; `TurnContext(Guid TeamId, Activation? Activation, long AnsweredAtSeq, long ActivationEndedAtSeq)` and `Activation(Guid UnitId, string Action, bool RunsSequence, IRL<StateChange> Changes)` are the scheduler plan's Task 1 types, namespace `Tabletop.GameServer.Scheduler` — these three hooks are the merged form of that plan's `EndOfTurn`, `RoundStartChanges` and `TurnEndChanges`, which no longer exist); `sealed record Reaction(Guid TeamId, string OptionId, string Label, string Sequence, SequenceContext Context)`; `sealed record SequenceStart(string Sequence, SequenceContext Context)` (the scheduler plan's `HookStartSequences` carries these).
- How a frame's position and history are read: position = the frame's completed steps less its repeats (a `step_completed` naming the same step as the one before it is a repeat; a `Sequence` never lists one name twice in a row); values = the `Values` of those steps folded in order (later wins); history = the effective log after the frame's last `step_completed` (or its `sequence_started`): `ask_opened` with this `FrameId` → an `AskRecord`, `ask_closed` → its answers; a `sequence_started` while this frame is on top → a direct `ChildRecord`, marked by its `sequence_finished`/`sequence_abandoned`. A step re-reads all of this on every run; that is the whole of its memory.
- `Advance`: nothing on the stack → `[]`; the top frame past its last step → `sequence_finished`; an open Ask on the top frame → `[]`; `WaitsBetweenSteps` and position > 0 and no closed confirm in the history → a `Confirm` Ask to the actor's team; otherwise run the step: `Complete` → `step_completed` (its `Answer` is the last closed non-confirm Ask's single answer, its `Rolls` the context's, `Changes`, `Values`) then the forced `dice_rerolled` payloads, then `sequence_finished` when it was the last step; `Finish` → the same with `sequence_finished` regardless; `OpenAsks` → `ask_opened` each; `Push` → `sequence_started` for the child. One outcome per call; the session's `AdvanceAsync` loop calls again.

Spec: action sequences → The resolution stack (four kinds; a frame is name, context, position, produced values), A step ends one of four ways, One roll primitive ("when on, every step boundary is a confirm gate — the gate is an Ask owned by the interpreter"), Asks; resolution → A pipeline in progress is state; `architecture.md` → Abilities (a sequence is a list of C# steps run over a frame stack); scheduler → The turn is an Ask (the scheduler starts a sequence through `Start`).

- [ ] **Step 1: Write the `IGameRules` additions**

Add these `using`s and records to `Rules/IGameRules.cs`, and the members to the interface body beside the scheduler plan's (that plan's Task 2 adds its own block the same way; the merged file is written in full in the skeleton plan → Interfaces → Produces → `Tabletop.GameServer.Rules`). `TurnContext` is the scheduler plan's Task 1 record (`Scheduler/SchedulerTypes.cs`); land that task first, or add the record from it — it has no other dependency.

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules;

public sealed record RollSpec(string Roll, RollConvention Convention, Func<LiveSet, Side, int?> Threshold);
public sealed record Reaction(Guid TeamId, string OptionId, string Label, string Sequence, SequenceContext Context);
public sealed record SequenceStart(string Sequence, SequenceContext Context);
public sealed class UnknownSequenceException(string name) : Exception($"No sequence named '{name}'.");

public interface IGameRules
{
    // ... string Id, and the scheduler plan's members ...

    // Added by the interpreter plan. Sequences and steps: the step lists are per-ruleset C# (architecture.md → Where data stops).
    Sequence Sequence(string name);
    /// <summary>The sequence an action row starts; null when the action has none (an out-of-set name accepted under warn/silent).</summary>
    string? ActionSequence(string actionName);
    // Per roll: the dice convention and the threshold derivation, a function (action sequences → constraints 2–4).
    RollSpec RollSpec(string roll);
    string HitRoll { get; }
    // Windows: who can react at a named point, and with what (action sequences → Windows).
    IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame);
    // Hooks the scheduler calls (scheduler → The line), each once per turn or round, with the scheduler's TurnContext.
    /// <summary>Sequences to run after the activation's own work, before turn_ended (OPR: end-of-activation morale). Empty: nothing to run.</summary>
    IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, TurnContext turn);
    /// <summary>Changes riding on round_started (OPR: fatigue cleared, spell tokens granted). Round 0 — the phases before round one — gets none.</summary>
    IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round);
    /// <summary>Changes riding on turn_ended (OPR: wounds-this-activation to 0; an idle activation clears Shaken).</summary>
    IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, TurnContext turn);
}
```

`GrimdarkFutureRules` is already `public sealed partial class` in `Rules/GrimdarkFuture/GfRules.cs` (skeleton Task 6); create `Rules/GrimdarkFuture/GfRules.Sequences.cs` as a further partial with each new member's body `throw new NotImplementedException()` until its task. Do not redeclare `RulesId`, `Instance` or `Id`. `GameRulesRegistry` is unchanged.

- [ ] **Step 2: Write `Steps.cs` and `FrameReader.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>A step is ruleset C#. It reads game state and its frame and ends one of four ways. It holds nothing between runs.</summary>
public interface IStep { string Name { get; } StepOutcome Run(StepContext ctx); }

/// <summary>Starts a sequence on the stack: what the scheduler (Task 10) needs from the interpreter.</summary>
public interface ISequenceStarter { IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context); }

/// <summary>No two consecutive steps share a name: a repeated completion of one name is how a repeating step is told from the next step.</summary>
public sealed record Sequence(string Name, IReadOnlyList<IStep> Steps)
{
    public Sequence { if (Steps.Zip(Steps.Skip(1)).Any(p => p.First.Name == p.Second.Name)) throw new ArgumentException($"{Name}: consecutive steps share a name"); }
}

public abstract record StepOutcome;
/// <summary>step_completed; the frame advances. Values are the settled parameters later steps and child frames read. Repeat keeps the position: the same step runs again with a fresh history (a move step's next segment).</summary>
public sealed record Complete(IReadOnlyList<StateChange> Changes, IReadOnlyDictionary<string, int>? Values = null, bool Repeat = false) : StepOutcome;
/// <summary>step_completed, then sequence_finished: the remaining steps never happen (a charge that cannot reach ends the activation).</summary>
public sealed record Finish(IReadOnlyList<StateChange> Changes, IReadOnlyDictionary<string, int>? Values = null) : StepOutcome;
/// <summary>The frame suspends until every Ask is closed. Several Asks: a window offered to several teams, one each.</summary>
public sealed record OpenAsks(IReadOnlyList<Ask> Asks) : StepOutcome;
/// <summary>A nested sequence starts; this step runs again when it finishes and sees it in History.Children.</summary>
public sealed record Push(string Sequence, SequenceContext Context) : StepOutcome;

public sealed record AskRecord(Ask Ask, IReadOnlyDictionary<Guid, AskAnswer>? Answers)
{
    public bool Closed => Answers is not null;
    public AskAnswer? Single => Answers is { Count: 1 } a ? a.Values.First() : null;
}
public sealed record ChildRecord(Guid FrameId, string Sequence, SequenceContext Context, bool Finished, bool Abandoned);

public sealed record StepHistory(IReadOnlyList<AskRecord> Asks, IReadOnlyList<ChildRecord> Children)
{
    public static readonly StepHistory Empty = new([], []);
    public bool Waiting => Asks.Any(a => !a.Closed);
    public AskRecord? LastClosed(string? question = null) => Asks.LastOrDefault(a => a.Closed && (question is null || a.Ask.Question == question));
    public int Confirmed => Asks.Count(a => a.Closed && a.Ask.Question == AskQuestions.Confirm);
    public IEnumerable<AskRecord> ClosedExceptConfirm => Asks.Where(a => a.Closed && a.Ask.Question != AskQuestions.Confirm);
}

public sealed record FrameView(Frame Frame, int Position, IReadOnlyDictionary<string, int> Values, StepHistory History, FrameView? Parent)
{
    public Guid Id => Frame.Id;
    public SequenceContext Context => Frame.Context;
    public int Value(string key, int fallback = 0) => Values.GetValueOrDefault(key, fallback);
}
```

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>Frames, positions, values and histories, read from the projection's stack and the effective log. Nothing here is stored.</summary>
public static class FrameReader
{
    public static IReadOnlyList<FrameView> Read(GameState state)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var views = new List<FrameView>();
        foreach (var frame in state.Projection.Stack)
        {
            var started = log.FindIndex(e => e.Payload is SequenceStarted s && s.FrameId == frame.Id);
            var lastStep = log.FindLastIndex(e => e.Payload is StepCompleted s && s.FrameId == frame.Id);
            var values = new Dictionary<string, int>();
            foreach (var s in log.Where(e => e.Payload is StepCompleted sc && sc.FrameId == frame.Id).Select(e => (StepCompleted)e.Payload))
                foreach (var (k, v) in s.Values ?? new Dictionary<string, int>()) values[k] = v;
            var position = frame.Steps.Count - frame.Steps.Zip(frame.Steps.Skip(1)).Count(p => p.First.Step == p.Second.Step);   // a repeat completes the same step again
            views.Add(new FrameView(frame, position, values, History(log, frame.Id, Math.Max(started, lastStep) + 1), views.LastOrDefault()));
        }
        return views;
    }

    /// <summary>Everything that happened to this frame since its current step began: its Asks, and the sequences it pushed (depth 1 only).</summary>
    static StepHistory History(IReadOnlyList<GameEvent> log, Guid frameId, int from)
    {
        var asks = new List<AskRecord>();
        var children = new List<ChildRecord>();
        var depth = 0;
        for (var i = from; i < log.Count; i++)
        {
            switch (log[i].Payload)
            {
                case AskOpened o when o.Ask.FrameId == frameId: asks.Add(new AskRecord(o.Ask, null)); break;
                case AskClosed c when asks.FindIndex(a => a.Ask.Id == c.AskId) is var k and >= 0:
                    asks[k] = asks[k] with { Answers = c.Answers.ToDictionary(a => a.TeamId, a => a.Answer) }; break;
                case SequenceStarted s when s.FrameId != frameId:
                    if (depth == 0) children.Add(new ChildRecord(s.FrameId, s.Sequence, s.Context, false, false));
                    depth++; break;
                case SequenceFinished f when f.FrameId != frameId: Close(children, f.FrameId, abandoned: false); depth = Math.Max(0, depth - 1); break;
                case SequenceAbandoned a when a.FrameId != frameId: Close(children, a.FrameId, abandoned: true); depth = Math.Max(0, depth - 1); break;
            }
        }
        return new StepHistory(asks, children);
    }

    static void Close(List<ChildRecord> children, Guid frameId, bool abandoned)
    {
        var k = children.FindIndex(c => c.FrameId == frameId);
        if (k >= 0) children[k] = children[k] with { Finished = !abandoned, Abandoned = abandoned };
    }
}
```

- [ ] **Step 3: Write `StepContext.cs` and `SequenceInterpreter.cs`**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

public static class Teams
{
    public static Guid? Of(GameState state, Guid unitId) =>
        state.Projection.Units.TryGetValue(unitId, out var u) ? state.Info.Participants.FirstOrDefault(p => p.Id == u.ParticipantId)?.TeamId : null;
    public static IReadOnlyList<Guid> Others(GameState state, Guid? team) => state.Info.Teams.Select(t => t.Id).Where(t => t != team).ToList();
}

/// <summary>What a step sees: state, the pinned set, its frame and history, and a roller at the log's cursor.</summary>
public sealed class StepContext(GameState state, PropertySet properties, IGameRules rules, FrameView frame, Roller roller)
{
    private readonly List<RollRecord> _rolls = new();
    private readonly List<DiceRerolled> _forced = new();

    public GameState State { get; } = state;
    public PropertySet Properties { get; } = properties;
    public IGameRules Rules { get; } = rules;
    public FrameView Frame { get; } = frame;
    public StepHistory History => Frame.History;
    public Projection Projection => State.Projection;
    public Guid? ActorTeam => Frame.Context.ActorUnitId is { } u ? Teams.Of(State, u) : null;
    public Guid? TeamOf(Guid unitId) => Teams.Of(State, unitId);
    public IReadOnlyList<RollRecord> Rolls => _rolls;
    public IReadOnlyList<DiceRerolled> ForcedRerolls => _forced;

    public RollPlan Plan(LiveSet live, Side roller, string roll, IEnumerable<RollModifier> baseModifiers, int? requiredRaw = null)
    {
        var spec = Rules.RollSpec(roll);
        return RollEffects.Plan(live, roller, roll, spec.Threshold(live, roller), baseModifiers, spec.Convention, requiredRaw, Rules.HitRoll);
    }

    public RollRecord Roll(RollPlan plan, int count)
    {
        var result = roller.Roll(Frame.Id, plan, count);
        _rolls.Add(result.Record);
        if (result.ForcedRerolls is { } f) _forced.Add(f);
        return result.Record;
    }

    public Ask Ask(string question, string prompt, Guid team, LegalSet legal, bool allowPass = false, string? windowKind = null) =>
        new(Guid.CreateVersion7(), question, prompt, [team], legal, allowPass, false, windowKind, Frame.Id);
}

/// <summary>Runs the top frame's current step and turns its outcome into payloads. Generic: knows no ruleset.</summary>
public sealed class SequenceInterpreter(IGameRules rules, PropertySet properties, IDiceSource dice) : ISequenceStarter
{
    public IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context)
    {
        rules.Sequence(sequence);   // throws UnknownSequenceException
        return [new SequenceStarted(Guid.CreateVersion7(), sequence, context)];
    }

    public IReadOnlyList<EventPayload> Advance(GameState state)
    {
        var frames = FrameReader.Read(state);
        if (frames.Count == 0) return [];
        var top = frames[^1];
        var sequence = rules.Sequence(top.Frame.Sequence);
        if (top.Position >= sequence.Steps.Count) return [new SequenceFinished(top.Id)];
        if (top.History.Waiting) return [];
        var actorTeam = top.Context.ActorUnitId is { } u ? Teams.Of(state, u) : null;
        if (state.Info.WaitsBetweenSteps && top.Position > 0 && actorTeam is { } gate && top.History.Confirmed == 0)
            return [new AskOpened(new Ask(Guid.CreateVersion7(), AskQuestions.Confirm, $"Continue: {sequence.Steps[top.Position].Name}", [gate], new Confirmation(), false, false, null, top.Id))];

        var step = sequence.Steps[top.Position];
        var ctx = new StepContext(state, properties, rules, top, new Roller(dice, state.RngSeed, DiceCursor.DrawnSoFar(state)));
        var events = new List<EventPayload>();
        switch (step.Run(ctx))
        {
            case Complete c: Completed(events, ctx, step, c.Changes, c.Values, finished: !c.Repeat && top.Position + 1 == sequence.Steps.Count); break;
            case Finish f: Completed(events, ctx, step, f.Changes, f.Values, finished: true); break;
            case OpenAsks a: events.AddRange(a.Asks.Select(ask => new AskOpened(ask))); break;
            case Push p: rules.Sequence(p.Sequence); events.Add(new SequenceStarted(Guid.CreateVersion7(), p.Sequence, p.Context)); break;
        }
        return events;
    }

    static void Completed(List<EventPayload> events, StepContext ctx, IStep step, IReadOnlyList<StateChange> changes, IReadOnlyDictionary<string, int>? values, bool finished)
    {
        var answer = ctx.History.ClosedExceptConfirm.LastOrDefault()?.Single;
        events.Add(new StepCompleted(ctx.Frame.Id, step.Name, answer, ctx.Rolls, changes) { Values = values is { Count: > 0 } ? values : null });
        events.AddRange(ctx.ForcedRerolls);
        if (finished) events.Add(new SequenceFinished(ctx.Frame.Id));
    }
}
```

- [ ] **Step 4: Write `StubRules.cs` and the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>A ruleset made of the sequences a test hands it. Rolls: D6, threshold 4, no cap. Never scheduled (Sim runs it under NoScheduler), so the scheduler's hooks are inert.</summary>
public sealed class StubRules(params Sequence[] sequences) : IGameRules
{
    public string Id => "stub";
    public string HitRoll => "hit";
    public Sequence Sequence(string name) => sequences.FirstOrDefault(s => s.Name == name) ?? throw new UnknownSequenceException(name);
    public string? ActionSequence(string actionName) => actionName.ToLowerInvariant();
    public RollSpec RollSpec(string roll) => new(roll, new RollConvention(6, null), (_, _) => 4);
    public IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame) => Reactions?.Invoke(state, point, frame) ?? [];
    public Func<GameState, string, FrameView, IReadOnlyList<Reaction>>? Reactions { get; init; }
    public IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, Scheduler.TurnContext turn) => [];
    public IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round) => [];
    public IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, Scheduler.TurnContext turn) => [];

    // The scheduler plan's members (its Task 2), inert here.
    public IReadOnlyList<string> PhasesBeforeRounds => [];
    public IReadOnlyList<string> PhasesInTurn => [];
    public Scheduler.FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams) => new Scheduler.OrderedTeams(teams);
    public Scheduler.TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId) => null;
    public Scheduler.Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer) => null;
    public bool ActivationComplete(GameState state, Scheduler.Activation activation) => true;
    public Scheduler.HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order) => new Scheduler.HookDone();
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeamId, IReadOnlyList<Guid> reactingTeams) => null;
}

public sealed class LambdaStep(string name, Func<StepContext, StepOutcome> run) : IStep
{
    public string Name => name;
    public StepOutcome Run(StepContext ctx) => run(ctx);
}
```

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class InterpreterTests
{
    static readonly Tabletop.Content.Unit A = Unit("A", "4+", "4+", [], 2, []), B = Unit("B", "4+", "4+", [], 2, []);

    static Sequence Outer() => new("outer", [
        new LambdaStep("first", ctx => new Complete([], new Dictionary<string, int> { ["x"] = 1 })),
        new LambdaStep("ask", ctx => ctx.History.LastClosed() is { Single: Chosen c } ? new Complete([new ScoreSet(Samples.Team1, c.OptionId == "a" ? 10 : 20)], new Dictionary<string, int> { ["x"] = 2 })
            : new OpenAsks([ctx.Ask("pick", "Pick", ctx.ActorTeam!.Value, new Options([new AskOption("a", "A", []), new AskOption("b", "B", [])]))])),
        new LambdaStep("push", ctx => ctx.History.Children.Any(ch => ch.Finished) ? new Complete([]) : new Push("inner", ctx.Frame.Context)),
        new LambdaStep("roll", ctx => { ctx.Roll(new RollPlan("hit", 4, [], new RollConvention(6, null), null, new HashSet<int> { 6 }), 2); return new Complete([]); })]);
    static Sequence Inner() => new("inner", [new LambdaStep("only", ctx => new Complete([], new Dictionary<string, int> { ["parent_x"] = ctx.Frame.Parent!.Value("x") }))]);

    /// <summary>Appends the engine's payloads as events and advances until it rests. Answers are applied as the dispatcher would log them.</summary>
    static List<EventPayload> Run(GameState state, SequenceInterpreter interpreter)
    {
        var all = new List<EventPayload>();
        for (var guard = 0; guard < 100; guard++)
        {
            var events = interpreter.Advance(state);
            if (events.Count == 0) return all;
            Append(state, events); all.AddRange(events);
        }
        throw new InvalidOperationException("did not rest");
    }
    static void Append(GameState state, IEnumerable<EventPayload> events) { foreach (var e in events) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, e, null)); }
    static void Answer(GameState state, Ask ask, AskAnswer answer) => Append(state, [new AskAnswered(ask.Id, ask.Teams[0], answer, false), new AskClosed(ask.Id, [new TeamAnswer(ask.Teams[0], answer)])]);

    [Fact]
    public void A_sequence_completes_asks_pushes_reads_values_across_frames_and_finishes()
    {
        var state = State([A], [B]);
        var interpreter = new SequenceInterpreter(new StubRules(Outer(), Inner()), TestProperties.Core, new ScriptedDice(6, 3, 5));
        Append(state, interpreter.Start(state, "outer", new SequenceContext(A.Id, [], [], null)));
        var first = Run(state, interpreter);
        Assert.Equal(["step_completed", "ask_opened"], first.Select(EventKinds.Of));
        var ask = ((AskOpened)first[1]).Ask;
        Assert.Equal((Samples.Team1, "pick", state.Projection.Stack[0].Id), (ask.Teams[0], ask.Question, ask.FrameId));
        Assert.Equal(1, FrameReader.Read(state)[0].Position);
        Assert.Empty(interpreter.Advance(state));                                                 // waiting on the Ask

        Answer(state, ask, new Chosen("a"));
        var rest = Run(state, interpreter);
        Assert.Equal(["step_completed", "sequence_started", "step_completed", "sequence_finished", "step_completed", "dice_rerolled", "sequence_finished"], rest.Select(EventKinds.Of));
        var askStep = (StepCompleted)rest[0];
        Assert.Equal((new Chosen("a"), 10), (askStep.Answer, ((ScoreSet)askStep.Changes[0]).Score));
        Assert.Equal(2, ((StepCompleted)rest[2]).Values!["parent_x"]);                              // the child reads the parent's folded values: "ask" overwrote x
        var roll = ((StepCompleted)rest[4]).Rolls.Single();
        Assert.Equal([6, 3], roll.Dice.Select(d => d.Raw));
        Assert.Equal(5, ((DiceRerolled)rest[5]).Replaced.Single().New.Raw);                        // the forced reroll drew the third scripted die
        Assert.Empty(state.Projection.Stack);
        Assert.Equal(3, DiceCursor.DrawnSoFar(state));
    }

    [Fact]
    public void Waiting_between_steps_gates_every_boundary_with_a_confirm_ask_to_the_actor()
    {
        var state = State([A], [B], waits: true);
        var interpreter = new SequenceInterpreter(new StubRules(Outer(), Inner()), TestProperties.Core, new ScriptedDice(1));
        Append(state, interpreter.Start(state, "outer", new SequenceContext(A.Id, [], [], null)));
        var first = Run(state, interpreter);
        Assert.Equal(["step_completed", "ask_opened"], first.Select(EventKinds.Of));
        var confirm = ((AskOpened)first[1]).Ask;
        Assert.Equal((AskQuestions.Confirm, Samples.Team1), (confirm.Question, confirm.Teams[0]));
        Assert.IsType<Confirmation>(confirm.Legal);
        Answer(state, confirm, new Confirmed());
        var next = Run(state, interpreter);
        Assert.Equal("pick", ((AskOpened)next.Single()).Ask.Question);                            // the confirm is spent; the step's own Ask follows
    }

    [Fact]
    public void Abandoning_pops_the_frames_and_a_frame_with_no_actor_is_never_gated()
    {
        var state = State([A], [B], waits: true);
        var interpreter = new SequenceInterpreter(new StubRules(new Sequence("two", [new LambdaStep("a", _ => new Complete([])), new LambdaStep("b", _ => new Complete([]))])), TestProperties.Core, new ScriptedDice(1));
        Append(state, interpreter.Start(state, "two", new SequenceContext(null, [], [], null)));
        Assert.Equal(["step_completed", "step_completed", "sequence_finished"], Run(state, interpreter).Select(EventKinds.Of));
        Append(state, interpreter.Start(state, "two", new SequenceContext(null, [], [], null)));
        Append(state, [new SequenceAbandoned(state.Projection.Stack[0].Id)]);
        Assert.Empty(interpreter.Advance(state));
        Assert.Throws<UnknownSequenceException>(() => interpreter.Start(state, "nope", new SequenceContext(null, [], [], null)));
    }
}
```

- [ ] **Step 5: Run `dotnet test tests/Tabletop.GameServer.Tests --filter InterpreterTests`; expect 3 passed. Commit: `Run sequences over frames rebuilt from the log: the interpreter`.**

### Task 9: Windows — offered, invoked, and the sealed bid

**Files:**
- Create: `server/game/Interpreter/Windows.cs`
- Modify: `server/game/Interpreter/StepContext.cs` (`SealedAsk`)
- Test: `tests/Tabletop.GameServer.Tests/WindowTests.cs`

**Interfaces:**
- Consumes: Task 8; `AskQuestions.React`, `AskQuestions.Bid`, `Bid`, `BidPlaced`, `Passed`, `Chosen` (Protocol); `Projection.Scheduler.Order`.
- Produces: `static class Windows` with `IRL<Guid> ReactionOrder(GameState state, Guid? actorTeam, IRL<Guid> reactingTeams)` (**the one reaction-ordering policy**, reconciled with the scheduler plan's Task 8: `state.Rules.OrderReactions(state, actorTeam, reactingTeams)` when it returns non-null; otherwise the scheduler's order rotated to start after the actor's team — the actor's own team last, since OPR's casting window is offered to it — teams missing from the order following in `Info.Teams` order, restricted to `reactingTeams`, with teams in `Projection.Scheduler.Passed` dropped; scheduler → Windows), `StepOutcome? Run(StepContext ctx, string point)` (null when the window is done: nobody can react, or every eligible team has passed or its reaction finished; otherwise `OpenAsks` of the next team's react-or-pass Ask, or `Push` of the next chosen reaction), `StepOutcome? SealedBid(StepContext ctx, string windowKind, IRD<Guid,int> maxByTeam, string prompt)` (one sealed Ask to every team with a positive max; null once it is closed), `IRD<Guid,int> Bids(StepContext ctx, string windowKind)` (each team's `BidPlaced.Tokens` clamped to its max, 0 for a pass); `StepContext.SealedAsk(string question, string prompt, IRL<Guid> teams, LegalSet legal, string windowKind)`.
- A window is one Ask per eligible team, `WindowKind = point`, `AllowPass = true`, `Options` = that team's reactions; teams are asked in `ReactionOrder`, one at a time, and a chosen reaction is pushed before the next team is asked, so reactions resolve in turn order after the actor (scheduler → Windows). The k-th chosen reaction is the k-th child in the step's history — that is how a re-run step knows which reactions have been pushed. Nothing is asked when `ReactionsAt` is empty (action sequences → Windows: "If nothing can react, no window opens").
- The sealed bid is one Ask addressed to every eligible team with `Sealed = true`; the dispatcher withholds answers until all have answered and closes with every answer (skeleton Task 5–6). `Bid.MaxTokens` is one number for the whole Ask, so it carries the largest team maximum and `Bids` clamps each team to its own; question 6 records this.

Spec: action sequences → Windows (offered, invoked, sealed; passing is a real answer, logged; the window closes when every offered team has passed or its reaction has finished); scheduler → Windows (ordering: turn order from the team after the actor; muting is the client's); `architecture.md` → per-player filtered streams (the sealed bid is their one use).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class WindowTests
{
    static readonly Tabletop.Content.Unit A = Unit("A", "4+", "4+", [], 2, []), B = Unit("B", "4+", "4+", [], 2, []);
    static Sequence Main() => new("main", [new LambdaStep("window", ctx => Windows.Run(ctx, "charge-declared") ?? new Complete([]))]);
    static Sequence Reaction() => new("overwatch", [new LambdaStep("shoot", _ => new Complete([new ScoreSet(Samples.Team2, 1)]))]);
    static readonly Dictionary<Guid, int> Max = new() { [Samples.Team1] = 2, [Samples.Team2] = 3 };
    static Sequence Bidding() => new("bidding", [
        new LambdaStep("declare", _ => new Complete([], Max.ToDictionary(kv => $"bid_max_{kv.Key:N}", kv => kv.Value))),   // the per-team maxima the bid reads back
        new LambdaStep("bid", ctx => Windows.SealedBid(ctx, "casting", Max, "Bid tokens")
            ?? new Complete([], Windows.Bids(ctx, "casting").ToDictionary(kv => kv.Key.ToString("N"), kv => kv.Value)))]);

    static StubRules Rules(bool reactive) => new(Main(), Reaction(), Bidding())
    {
        Reactions = (state, point, frame) => reactive && point == "charge-declared" ? [new Reaction(Samples.Team2, "overwatch", "Overwatch", "overwatch", new SequenceContext(B.Id, [frame.Context.ActorUnitId!.Value], [], null))] : [],
    };

    static void Append(GameState state, IEnumerable<EventPayload> events) { foreach (var e in events) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, e, null)); }
    static List<EventPayload> Run(GameState state, SequenceInterpreter i) { var all = new List<EventPayload>(); for (var n = 0; n < 50; n++) { var ev = i.Advance(state); if (ev.Count == 0) return all; Append(state, ev); all.AddRange(ev); } throw new InvalidOperationException(); }
    static void Close(GameState state, Ask ask, params (Guid Team, AskAnswer Answer)[] answers)
    {
        foreach (var (t, a) in answers) Append(state, [new AskAnswered(ask.Id, t, a, false)]);
        Append(state, [new AskClosed(ask.Id, answers.Select(x => new TeamAnswer(x.Team, x.Answer)).ToList())]);
    }

    [Fact]
    public void A_reaction_is_offered_to_the_other_team_pushed_when_chosen_and_the_window_closes_after_it()
    {
        var state = State([A], [B]);
        var i = new SequenceInterpreter(Rules(reactive: true), TestProperties.Core, new ScriptedDice(1));
        Append(state, i.Start(state, "main", new SequenceContext(A.Id, [], [], null)));
        var ask = ((AskOpened)Run(state, i).Single()).Ask;
        Assert.Equal((AskQuestions.React, "charge-declared", Samples.Team2, true), (ask.Question, ask.WindowKind, ask.Teams.Single(), ask.AllowPass));
        Assert.Equal("overwatch", ((Options)ask.Legal).Choices.Single().Id);
        Close(state, ask, (Samples.Team2, new Chosen("overwatch")));
        var events = Run(state, i);
        Assert.Equal(["sequence_started", "step_completed", "sequence_finished", "step_completed", "sequence_finished"], events.Select(EventKinds.Of));
        Assert.Equal("overwatch", ((SequenceStarted)events[0]).Sequence);
        Assert.Equal(1, state.Projection.Scores[Samples.Team2]);
    }

    [Fact]
    public void Passing_closes_the_window_and_nothing_is_asked_when_nobody_can_react()
    {
        var state = State([A], [B]);
        var i = new SequenceInterpreter(Rules(reactive: true), TestProperties.Core, new ScriptedDice(1));
        Append(state, i.Start(state, "main", new SequenceContext(A.Id, [], [], null)));
        Close(state, ((AskOpened)Run(state, i).Single()).Ask, (Samples.Team2, new Passed()));
        Assert.Equal(["step_completed", "sequence_finished"], Run(state, i).Select(EventKinds.Of));
        var quiet = State([A], [B]);
        var j = new SequenceInterpreter(Rules(reactive: false), TestProperties.Core, new ScriptedDice(1));
        Append(quiet, j.Start(quiet, "main", new SequenceContext(A.Id, [], [], null)));
        Assert.Equal(["step_completed", "sequence_finished"], Run(quiet, j).Select(EventKinds.Of));
        Assert.Equal([Samples.Team2, Samples.Team1], Windows.ReactionOrder(state, Samples.Team1, [Samples.Team1, Samples.Team2]));   // the actor's own team last
        Assert.Equal([Samples.Team2], Windows.ReactionOrder(state, Samples.Team1, [Samples.Team2]));                                   // only the reacting teams
    }

    [Fact]
    public void A_sealed_bid_is_one_ask_to_every_team_and_reads_back_clamped()
    {
        var state = State([A], [B]);
        var i = new SequenceInterpreter(Rules(reactive: false), TestProperties.Core, new ScriptedDice(1));
        Append(state, i.Start(state, "bidding", new SequenceContext(A.Id, [], [], null)));
        var ask = ((AskOpened)Run(state, i).Last()).Ask;
        Assert.True(ask.Sealed); Assert.Equal(2, ask.Teams.Count); Assert.Equal(3, ((Bid)ask.Legal).MaxTokens); Assert.Equal("casting", ask.WindowKind);
        Close(state, ask, (Samples.Team1, new BidPlaced(3)), (Samples.Team2, new Passed()));
        var done = (StepCompleted)Run(state, i)[0];
        Assert.Equal((2, 0), (done.Values![Samples.Team1.ToString("N")], done.Values[Samples.Team2.ToString("N")]));   // team 1 clamped to its own max
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `Windows.cs` and add `SealedAsk` to `StepContext`**

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>An Ask about interrupting. One Ask per eligible team, asked in reaction order, each chosen reaction pushed before the next team is asked.</summary>
public static class Windows
{
    /// <summary>
    /// Who reacts first (scheduler → Windows): GameRules.OrderReactions when it answers; else turn order starting after the actor's team
    /// (the actor's own team last), teams the order does not list following in Info.Teams order, only the reacting teams, passed teams dropped.
    /// </summary>
    public static IReadOnlyList<Guid> ReactionOrder(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams)
    {
        if (state.Rules.OrderReactions(state, actorTeam, reactingTeams) is { } replaced) return replaced;
        var s = state.Projection.Scheduler;
        var order = s.Order.Count > 0 ? s.Order.ToList() : state.Info.Teams.Select(t => t.Id).ToList();
        var start = actorTeam is { } a && order.Contains(a) ? order.IndexOf(a) + 1 : 0;
        var rotated = order.Skip(start).Concat(order.Take(start)).Concat(state.Info.Teams.Select(t => t.Id).Where(t => !order.Contains(t))).ToList();
        return rotated.Where(t => reactingTeams.Contains(t) && !s.Passed.Contains(t)).ToList();
    }

    public static StepOutcome? Run(StepContext ctx, string point)
    {
        var reactions = ctx.Rules.ReactionsAt(ctx.State, ctx.Properties, point, ctx.Frame);
        if (reactions.Count == 0) return null;
        var asks = ctx.History.Asks.Where(a => a.Ask.WindowKind == point && a.Ask.Question == AskQuestions.React).ToList();
        var pushed = 0;
        foreach (var team in ReactionOrder(ctx.State, ctx.ActorTeam, reactions.Select(r => r.TeamId).Distinct().ToList()))
        {
            var mine = reactions.Where(r => r.TeamId == team).ToList();
            if (mine.Count == 0) continue;
            var ask = asks.FirstOrDefault(a => a.Ask.Teams[0] == team);
            if (ask is null)
                return new OpenAsks([ctx.Ask(AskQuestions.React, $"React at {point}?", team, new Options(mine.Select(r => new AskOption(r.OptionId, r.Label, [r.Context.ActorUnitId ?? Guid.Empty])).ToList()), allowPass: true, windowKind: point)]);
            if (!ask.Closed) return new OpenAsks([]);   // unreachable: Advance returns before running a waiting step
            if (ask.Single is not Chosen chosen) continue;   // passed, or an out-of-set answer accepted under warn/silent
            var reaction = mine.FirstOrDefault(r => r.OptionId == chosen.OptionId) ?? mine[0];
            if (ctx.History.Children.Count <= pushed) return new Push(reaction.Sequence, reaction.Context);
            pushed++;   // its child exists and, since this frame is on top, has finished or was abandoned
        }
        return null;
    }

    public static StepOutcome? SealedBid(StepContext ctx, string windowKind, IReadOnlyDictionary<Guid, int> maxByTeam, string prompt)
    {
        var teams = maxByTeam.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
        if (teams.Count == 0) return null;
        var existing = ctx.History.Asks.FirstOrDefault(a => a.Ask.WindowKind == windowKind && a.Ask.Question == AskQuestions.Bid);
        if (existing is null) return new OpenAsks([ctx.SealedAsk(AskQuestions.Bid, prompt, teams, new Bid(maxByTeam.Values.Max()), windowKind)]);
        return existing.Closed ? null : new OpenAsks([]);
    }

    public static IReadOnlyDictionary<Guid, int> Bids(StepContext ctx, string windowKind)
    {
        var ask = ctx.History.Asks.LastOrDefault(a => a.Ask.WindowKind == windowKind && a.Ask.Question == AskQuestions.Bid && a.Closed);
        if (ask is null) return new Dictionary<Guid, int>();
        var max = ((Bid)ask.Ask.Legal).MaxTokens;
        return ask.Ask.Teams.ToDictionary(t => t, t => ask.Answers!.GetValueOrDefault(t) is BidPlaced b ? Math.Clamp(b.Tokens, 0, Math.Min(max, MaxFor(ctx, windowKind, t))) : 0);
    }

    /// <summary>The per-team maximum is not in the Ask, so the step that opened the bid records it: Values["bid_max_" + team] (Task 17 writes them).</summary>
    static int MaxFor(StepContext ctx, string windowKind, Guid team) => ctx.Frame.Value($"bid_max_{team:N}", int.MaxValue);
}
```

```csharp
    // StepContext
    public Ask SealedAsk(string question, string prompt, IReadOnlyList<Guid> teams, LegalSet legal, string windowKind) =>
        new(Guid.CreateVersion7(), question, prompt, teams, legal, true, true, windowKind, Frame.Id);
```

`SealedBid` records nothing itself: the step before it writes each team's maximum as `Values["bid_max_<team>"]` (the test's `declare` step; casting's spell step in Task 17), which is what lets `Bids` clamp without the Ask carrying per-team numbers.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter WindowTests`; expect 3 passed. Commit: `Open windows: react-or-pass Asks in reaction order, and the sealed bid`.**

### Task 10: `RulesEngine` — the one `IRulesEngine`, and the scheduler's seam

**Files:**
- Create: `server/game/Engine/IScheduler.cs`, `server/game/Engine/RulesEngine.cs`, `server/game/Engine/RulesEngineFactory.cs`
- Modify: `server/game/Program.cs` (replace `InertEngineFactory`; register `IDiceSource`, `ISchedulerFactory`), `server/game/Session/ConnectionHandshake.cs` (the skeleton's hydrate catch gains `Engine.PropertiesNotFoundException`)
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/Sim.cs`, `tests/Tabletop.GameServer.Tests/RulesEngineTests.cs` (this plan's; the scheduler plan's engine tests are `SchedulerFactoryTests.cs`)

**Interfaces:**
- Consumes: Tasks 4–9; the skeleton's `IRulesEngine`, `IRulesEngineFactory`, `GameState`, `Actor`, `EngineResult`, `Emit`, `RefuseCommand`, `Legality`, `CommandDispatcher`, `RerollDice`, `SetDice`, `AddModifier`, `DiceRerolled`, `DiceEdited`, `ModifierAdded`, `RollModifier`, `RefusalReason`.
- Produces (namespace `Tabletop.GameServer.Engine`): `interface IScheduler { IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter); }` (`ISequenceStarter` is Task 8's, in `Interpreter/Steps.cs`, implemented by `SequenceInterpreter`); `interface ISchedulerFactory { IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties); }` (the set, because the scheduler reads the `action` rows' names for the turn Ask and passes the set to the three hooks); `sealed class NoScheduler : IScheduler` (returns `[]`) and `sealed class NoSchedulerFactory : ISchedulerFactory` — the registration until the scheduler plan replaces it; `sealed class RulesEngine(IGameRules rules, PropertySet properties, IDiceSource dice, IScheduler scheduler) : IRulesEngine` with `SequenceInterpreter Interpreter { get; }`; `sealed class RulesEngineFactory(IPropertiesStore store, IDiceSource dice, ISchedulerFactory schedulers) : IRulesEngineFactory` (loads the game's set by `game.PropertiesHash`, cached by hash; throws `PropertiesNotFoundException(string hash)`); test infrastructure `Sim` (below).
- **The seam, stated for the scheduler plan** (reconciled with that plan 2026-09-05). `RulesEngine.Advance(state)`: (1) if any **framed** Ask is open (`OpenAsks` with `Ask.FrameId` non-null — every Ask the interpreter opens is framed) → `[]`; (2) if the resolution stack is non-empty → `Interpreter.Advance(state)`; (3) otherwise → `scheduler.Advance(state, Interpreter)`. Unframed Asks are the scheduler's (the turn Ask, the deploy Asks, a round-boundary Ask) and the scheduler is still called while one is open: it returns `[]` for an Ask of its own that is live, and it is what closes — with an empty answer list — an Ask of its own that a menu edit (`set_active_team`, `set_round`) superseded, the one `ask_closed` the engine ever writes. So the scheduler runs only between sequences: it opens the turn Ask (`Ask.FrameId = null`, question `AskQuestions.Activate`), reads its `ask_closed` from the log on a later call, emits its own `ActionRecorded` and — when `rules.ActionSequence(action)` is non-null — `starter.Start(state, sequence, new SequenceContext(unitId, [], [], action))`, sees `sequence_finished` for that frame on a later call, calls `rules.EndOfTurnSequences(state, properties, turn)` and starts what it returns, and when those have finished emits `turn_ended` with `rules.OnTurnEnded(state, properties, turn)`'s changes; `round_started` carries `rules.OnRoundStarted(state, properties, round)`'s changes. The roll-off is a ruleset sequence the scheduler starts by the name `FirstTeamDecision.RollOffNeeded(sequence)` gives it and reads back from the frame's `step_completed.Values` (`place_1..place_n`, Task 11). `Judge`/`Execute` are the interpreter's alone (the scheduler has no commands; a pass is an Ask answer). `RulesEngine.Judge`: `RerollDice`/`SetDice` → `Legal` when the frame is on the stack and the roll is in one of its completed steps, `Illegal` otherwise; `AddModifier` → `Legal` when the frame is on the stack and `properties.HasRoll(roll)`; any other command → `Illegal`. `Execute`: `RerollDice` → one `DiceRerolled` with the named dice (all when empty) redrawn under the record's own threshold and modifiers at the log's cursor; `SetDice` → `DiceEdited` with each die re-judged from its new raw under the record's threshold and modifiers; `AddModifier` → `ModifierAdded(frame, roll, new RollModifier("player", amount))`; otherwise `RefuseCommand(Unknown, …)`.
- A reroll or edit does not re-run the step: later steps derive counts from the roll record as it now stands (Task 12–13 read `RollOutcome.Successes` and the step's settled `Values`), which is what "rerolls stay commands between steps" means with the wait setting on.

Spec: skeleton Task 6 (the seam; `Advance` called after every appended batch until empty); scheduler → The line, The turn is an Ask, Events; action sequences → One roll primitive ("Rerolls and hand-added modifiers stay commands between steps"); resolution → Players roll their own dice ("A reroll records which dice it replaced. A manual reroll is that with the selection set to all"), Rerolling does not break undo (visible, logged with an actor).

- [ ] **Step 1: Write `Sim.cs`, the harness every sequence test uses**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>A playing game driven the way the session drives it: dispatch a command, append, advance until the engine rests.</summary>
public sealed class Sim
{
    public GameState State { get; }
    public RulesEngine Engine { get; }
    public CommandDispatcher Dispatcher { get; }
    public List<EventPayload> Emitted { get; } = new();

    public Sim(GameState state, IGameRules rules, PropertySet properties, IDiceSource dice, IScheduler? scheduler = null)
    {
        State = state;
        Engine = new RulesEngine(rules, properties, dice, scheduler ?? new NoScheduler());
        Dispatcher = new CommandDispatcher(Engine);
    }

    public static Sim Gf(GameState state, params int[] dice) => new(state, GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(dice.Length == 0 ? [4] : dice));

    public void Append(IEnumerable<EventPayload> events, Guid? actor = null)
    {
        foreach (var e in events) { State.Applier.Apply(new GameEvent(State.Projection.LastSeq + 1, actor, e, null)); Emitted.Add(e); }
    }

    /// <summary>Advance until the engine returns nothing (the session's AdvanceAsync loop).</summary>
    public IReadOnlyList<EventPayload> Run()
    {
        var from = Emitted.Count;
        for (var i = 0; i < 1000; i++) { var events = Engine.Advance(State); if (events.Count == 0) return Emitted.Skip(from).ToList(); Append(events); }
        throw new InvalidOperationException("the engine did not rest in 1000 steps");
    }

    public IReadOnlyList<EventPayload> Start(string sequence, SequenceContext context) { Append(Engine.Interpreter.Start(State, sequence, context)); return Run(); }

    /// <summary>The open Ask addressed to the team (exactly one expected).</summary>
    public Ask AskFor(Guid team) => State.Projection.OpenAsks.Values.Single(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team)).Ask;
    public Ask? AskForOrNull(Guid team) => State.Projection.OpenAsks.Values.SingleOrDefault(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team))?.Ask;

    public DispatchOutcome Dispatch(Command command, Guid participant, Guid? team)
    {
        var outcome = Dispatcher.Dispatch(State, command, new Actor(participant, team));
        if (outcome is Applied a) Append(a.Events, participant);
        return outcome;
    }

    /// <summary>Answer the team's one open Ask, then run. Fails the test when the dispatcher refuses.</summary>
    public IReadOnlyList<EventPayload> Answer(Guid team, AskAnswer answer, Guid? participant = null)
    {
        var ask = AskFor(team);
        var outcome = Dispatch(new AnswerAsk(ask.Id, answer), participant ?? State.Info.Participants.First(p => p.TeamId == team).Id, team);
        if (outcome is Refused r) throw new Xunit.Sdk.XunitException($"refused: {r.Reason} {r.Detail}");
        return Run();
    }

    public IEnumerable<StepCompleted> Steps(string name) => Emitted.OfType<StepCompleted>().Where(s => s.Step == name);
    public UnitInPlay Unit(Guid id) => State.Projection.Units[id];
    public ModelInPlay Model(Guid id) => State.Projection.Models[id];
    public int Alive(Guid unitId) => State.Projection.Models.Values.Count(m => m.UnitId == unitId && m.Location is OnTable);
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class RulesEngineTests
{
    static readonly Tabletop.Content.Unit A = Unit("A", "4+", "4+", [], 2, []), B = Unit("B", "4+", "4+", [], 2, []);
    static Sequence Roller() => new("roller", [new LambdaStep("roll", ctx => { ctx.Roll(new RollPlan("hit", 4, [new RollModifier("x", 1)], new RollConvention(6, null), null, new HashSet<int>()), 3); return new Complete([]); }),
        new LambdaStep("wait", ctx => ctx.History.LastClosed() is null ? new OpenAsks([ctx.Ask("go", "Go", ctx.ActorTeam!.Value, new Options([new AskOption("go", "Go", [])]))]) : new Complete([]))]);

    /// <summary>A scheduler that opens one Ask when the stack is empty and nothing has been asked yet.</summary>
    sealed class OneAsk(Ask ask) : IScheduler
    {
        public IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter) =>
            state.Applier.Log.Any(e => e.Payload is AskOpened o && o.Ask.Id == ask.Id) ? [] : [new AskOpened(ask)];
    }

    static Sim Make(IScheduler? scheduler = null) => new(State([A], [B]), new StubRules(Roller()), TestProperties.Core, new ScriptedDice(2, 5, 6, 1, 3), scheduler);

    [Fact]
    public void Advance_runs_the_interpreter_while_frames_exist_and_the_scheduler_when_they_do_not()
    {
        var turn = new Ask(Guid.CreateVersion7(), AskQuestions.Activate, "Activate", [Samples.Team1], new Options([new AskOption("a", "A", [])]), true, false, null, null);
        var sim = Make(new OneAsk(turn));
        Assert.Equal(["ask_opened"], sim.Run().Select(EventKinds.Of));                  // empty stack: the scheduler speaks
        Assert.Empty(sim.Run());                                                          // an open Ask blocks everything
        sim.Answer(Samples.Team1, new Passed());
        sim.Append(sim.Engine.Interpreter.Start(sim.State, "roller", new SequenceContext(A.Id, [], [], null)));
        Assert.Equal(["step_completed", "ask_opened"], sim.Run().Select(EventKinds.Of));  // a frame: the interpreter speaks, the scheduler is silent
    }

    [Fact]
    public void Reroll_set_dice_and_add_modifier_are_judged_and_executed_against_the_frame()
    {
        var sim = Make();
        sim.Start("roller", new SequenceContext(A.Id, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.Equal([3, 6, 7], roll.Dice.Select(d => d.Modified));
        var clay = new Actor(Samples.P1, Samples.Team1);

        Assert.Equal(Legality.Legal, sim.Engine.Judge(sim.State, new RerollDice(frame, roll.Id, [0]), clay));
        Assert.Equal(Legality.Illegal, sim.Engine.Judge(sim.State, new RerollDice(Guid.CreateVersion7(), roll.Id, []), clay));
        Assert.Equal(Legality.Illegal, sim.Engine.Judge(sim.State, new AddModifier(frame, "save", 1), clay));
        Assert.Equal(Legality.Legal, sim.Engine.Judge(sim.State, new AddModifier(frame, "defense", -1), clay));

        var rerolled = Assert.IsType<Applied>(sim.Dispatch(new RerollDice(frame, roll.Id, [0]), Samples.P1, Samples.Team1));
        var rr = Assert.IsType<DiceRerolled>(rerolled.Events.Single());
        Assert.Equal((0, 2, 1, 2, false), (rr.Replaced[0].Index, rr.Replaced[0].Old.Raw, rr.Replaced[0].New.Raw, rr.Replaced[0].New.Modified, rr.Replaced[0].New.Success));   // the 4th scripted die, +1, under threshold 4
        Assert.Equal(1, sim.State.Projection.Stack[0].Steps[0].Rolls[0].Dice[0].Raw);
        Assert.Equal(4, DiceCursor.DrawnSoFar(sim.State));

        var edited = Assert.IsType<Applied>(sim.Dispatch(new SetDice(frame, roll.Id, [new DieValue(1, 3)]), Samples.P1, Samples.Team1));
        var ed = Assert.IsType<DiceEdited>(edited.Events.Single());
        Assert.Equal((5, 3, 4, true), (ed.Replaced[0].Old.Raw, ed.Replaced[0].New.Raw, ed.Replaced[0].New.Modified, ed.Replaced[0].New.Success));
        Assert.Equal(4, DiceCursor.DrawnSoFar(sim.State));                                  // an edit draws nothing

        var mod = Assert.IsType<Applied>(sim.Dispatch(new AddModifier(frame, "hit", 2), Samples.P1, Samples.Team1));
        Assert.Equal(new RollModifier("player", 2), Assert.IsType<ModifierAdded>(mod.Events.Single()).Modifier);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(sim.Dispatch(new RerollDice(frame, Guid.CreateVersion7(), []), Samples.P1, Samples.Team1)).Reason);
    }

    [Fact]
    public void The_factory_loads_the_pinned_set_and_refuses_an_unknown_hash()
    {
        var store = new InMemoryPropertiesStore().Add(TestProperties.Core);
        var factory = new RulesEngineFactory(store, new DiceSource(), new NoSchedulerFactory());
        var game = Samples.Game with { PropertiesHash = TestProperties.Core.ContentHash };
        Assert.IsType<RulesEngine>(factory.Create(GrimdarkFutureRules.Instance, game));
        Assert.Throws<PropertiesNotFoundException>(() => factory.Create(GrimdarkFutureRules.Instance, Samples.Game with { PropertiesHash = "sha256:nope" }));
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `IScheduler.cs`**

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Engine;

/// <summary>
/// The scheduler plan's seam. Called by RulesEngine.Advance only when no framed Ask is open and the resolution stack is empty; returns the
/// scheduler's next payloads — round_started, turn_started, the turn Ask (Ask.FrameId null), the action's sequence_started via the
/// starter, turn_ended — or nothing. It reads the log for its own Asks' ask_closed and for sequence_finished, returns nothing while an
/// Ask of its own is live, and closes one of its own Asks that a menu edit superseded (the engine's one ask_closed).
/// </summary>
public interface IScheduler { IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter); }

/// <summary>Per game: the ruleset, the game, and its pinned set (the scheduler reads the action rows' names and hands the set to the hooks).</summary>
public interface ISchedulerFactory { IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties); }

/// <summary>Until the scheduler plan lands: nothing drives turns, so sequences start only through tests or the scheduler's future registration.</summary>
public sealed class NoScheduler : IScheduler { public IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter) => []; }
public sealed class NoSchedulerFactory : ISchedulerFactory { public IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties) => new NoScheduler(); }
```

- [ ] **Step 4: Write `RulesEngine.cs` and `RulesEngineFactory.cs`; make `Interpreter` implement `ISequenceStarter`**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Engine;

/// <summary>The one IRulesEngine. Dice commands are judged and executed here; Advance is the interpreter while frames exist and the scheduler when they do not.</summary>
public sealed class RulesEngine(IGameRules rules, PropertySet properties, IDiceSource dice, IScheduler scheduler) : IRulesEngine
{
    public SequenceInterpreter Interpreter { get; } = new(rules, properties, dice);

    public Legality Judge(GameState state, Command command, Actor actor) => command switch
    {
        RerollDice r => Find(state, r.FrameId, r.RollId) is not null ? Legality.Legal : Legality.Illegal,
        SetDice s => Find(state, s.FrameId, s.RollId) is not null ? Legality.Legal : Legality.Illegal,
        AddModifier m => state.Projection.Stack.Any(f => f.Id == m.FrameId) && properties.HasRoll(m.Roll) ? Legality.Legal : Legality.Illegal,
        _ => Legality.Illegal,
    };

    public EngineResult Execute(GameState state, Command command, Actor actor)
    {
        switch (command)
        {
            case RerollDice r when Find(state, r.FrameId, r.RollId) is { } record:
            {
                var roller = new Roller(dice, state.RngSeed, DiceCursor.DrawnSoFar(state));
                return new Emit([new DiceRerolled(r.FrameId, r.RollId, RollOutcome.Reroll(PlanOf(record), record, r.DiceIndices, roller))]);
            }
            case SetDice s when Find(state, s.FrameId, s.RollId) is { } record:
            {
                var plan = PlanOf(record); var roller = new Roller(dice, state.RngSeed, 0);
                return new Emit([new DiceEdited(s.FrameId, s.RollId, s.Dice.Select(d => new DieReplacement(d.Index, record.Dice[d.Index], roller.Judge(plan, d.Raw))).ToList())]);
            }
            case AddModifier m when state.Projection.Stack.Any(f => f.Id == m.FrameId):
                return new Emit([new ModifierAdded(m.FrameId, m.Roll, new RollModifier("player", m.Amount))]);
            default:
                return new RefuseCommand(RefusalReason.Unknown, $"{command.GetType().Name}: no such frame or roll");
        }
    }

    /// <summary>A framed Ask blocks everything; an unframed one is the scheduler's to wait on or close.</summary>
    public IReadOnlyList<EventPayload> Advance(GameState state)
    {
        if (state.Projection.OpenAsks.Values.Any(a => a.Ask.FrameId is not null)) return [];
        if (state.Projection.Stack.Count > 0) return Interpreter.Advance(state);
        return scheduler.Advance(state, Interpreter);
    }

    static RollRecord? Find(GameState state, Guid frameId, Guid rollId) =>
        state.Projection.Stack.FirstOrDefault(f => f.Id == frameId)?.Steps.SelectMany(s => s.Rolls).FirstOrDefault(r => r.Id == rollId);

    /// <summary>A record re-judged under its own threshold and modifiers, in the ruleset's convention for that roll; RequiredRaw cannot be read back and is not re-applied.</summary>
    RollPlan PlanOf(RollRecord record) => new(record.Roll, record.Threshold, record.Modifiers, rules.RollSpec(record.Roll).Convention, null, new HashSet<int>());
}

public sealed class PropertiesNotFoundException(string hash) : Exception($"No Properties set with hash {hash} is loaded; the game cannot run.");

/// <summary>Replaces InertEngineFactory. The pinned set is loaded once per hash; Create is synchronous, so the load blocks at hydration.</summary>
public sealed class RulesEngineFactory(IPropertiesStore store, IDiceSource dice, ISchedulerFactory schedulers) : IRulesEngineFactory
{
    private readonly Dictionary<string, PropertySet> _cache = new();

    public IRulesEngine Create(IGameRules rules, GameInfo game)
    {
        PropertySet set;
        lock (_cache)
        {
            if (!_cache.TryGetValue(game.PropertiesHash, out set!))
                _cache[game.PropertiesHash] = set = store.LoadAsync(game.PropertiesHash, CancellationToken.None).GetAwaiter().GetResult() ?? throw new PropertiesNotFoundException(game.PropertiesHash);
        }
        return new RulesEngine(rules, set, dice, schedulers.Create(rules, game, set));
    }
}
```

In `server/game/Session/ConnectionHandshake.cs` (skeleton Task 8) widen the hydrate catch to `catch (Exception ex) when (ex is GameNotFoundException or GameRowsInconsistentException or Rules.UnknownGameRulesException or Engine.PropertiesNotFoundException)` — the socket closes with `1011 cannot hydrate this game` instead of the process faulting.

`SequenceInterpreter` already implements `ISequenceStarter` (Task 8 declared it; the interface is defined here). A record's `RequiredRaw` (fatigue's unmodified-6s rule) is not in `RollRecord`, so a player reroll of a fatigued hit die is judged as `Modified >= Threshold`; Task 11 sets the threshold to 6 and no modifiers for that case, so the two agree (`Die.Success` is `Raw + 0 >= 6`).

- [ ] **Step 5: Register in `Program.cs`** — replace `builder.Services.AddSingleton<IRulesEngineFactory, InertEngineFactory>();` with:

```csharp
builder.Services.AddSingleton<IDiceSource, DiceSource>();
builder.Services.AddSingleton<ISchedulerFactory, NoSchedulerFactory>();   // the scheduler plan replaces this registration
builder.Services.AddSingleton<IRulesEngineFactory, RulesEngineFactory>();
```

`Hydrator` (skeleton Task 7) calls `Engines.Create` after reading the log; the `ConnectionHandshake` catch above is what turns a `PropertiesNotFoundException` there into a `1011` close. `InertEngine`/`InertEngineFactory` stay in the tree for the skeleton's tests. The scheduler plan's Task 10 replaces the `NoSchedulerFactory` line with `SchedulerFactory` and nothing else here.

- [ ] **Step 6: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect every earlier test green plus 3 here (the skeleton's `EndSetupTests` still uses `FixedAskEngineFactory` through `ConfigureTestServices`, so it is unaffected). Commit: `Add the rules engine: dice commands, and Advance split between interpreter and scheduler`.**

### Task 11: Grimdark Future's rolls, names, hooks and the battlefield reader

**Files:**
- Create: `server/game/Interpreter/Battlefield.cs`, `server/game/Rules/GrimdarkFuture/GfNames.cs`, `server/game/Rules/GrimdarkFuture/GfRolls.cs`, `server/game/Rules/GrimdarkFuture/GfHooks.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (Task 8's partial: the bodies)
- Test: `tests/Tabletop.GameServer.Tests/GfRollsTests.cs`

**Interfaces:**
- Consumes: Tasks 6–10; geometry plan's `Distance.EdgeToEdge/Vertical/InRange`, `LineOfSight.Check`, `Cover.InsideCoverVolumes/SightBlockersBetween`, `Containment.VolumesContaining`, `Bodies.AsBlockingVolume`, `Movement.Check`, `Settling.SurfaceHeightAt`; `TerrainPlacement.WorldVolumes()` (Content).
- Produces (namespace `Tabletop.GameServer.Interpreter`): `static class Battlefield` with `PlacedModel? Placed(GameState, Guid modelId)` (null off the table — on the shelf or inside a container), `PlacedModel At(GameState, Guid modelId, Placement)` (the body at a placement it does not hold yet: how a destination is judged, and the only way to place an occupant), `bool? InsideReach(GameState, Guid fromUnit, Guid toUnit)` (p.14: false when one is inside and the other out, true when both are in one container or the reaching unit is inside the other's container model, null when neither is inside and the answer is a measurement), `IRL<WorldVolume> Terrain(GameState)`, `IRL<WorldVolume> Blockers(GameState, Guid exceptUnitId)` (terrain plus every other unit's models as sight blockers — rulebook p.8 and p.11), `bool CanSee(GameState, Guid fromModel, Guid toModel)`, `double Distance(GameState, Guid modelA, Guid modelB)`, `double Vertical(GameState, Guid modelA, Guid modelB)`, `double? UnitDistance(GameState, Guid unitA, Guid unitB)` (closest pair of living models), `bool InCover(GameState, Guid targetUnit, Guid shooterModel)` (p.11: majority of models fully inside a Cover volume or behind a sight blocker; a single model mostly inside — centre inside), `IRL<string> TerrainUnder(GameState, Guid unitId)` (declaration names of volumes containing any living model — the `position_terrain` predicate's values), `Placement Settle(GameState, Placement clientPlacement)` (the server's Z from `Settling.SurfaceHeightAt`).
- Produces (namespace `Tabletop.GameServer.Rules.GrimdarkFuture`): `static class GfNames` (`Quality`, `Defense`, `Range`, `Attacks`; actions `Hold`, `Advance`, `Rush`, `Charge`; rolls `Hit = "hit"`, `DefenseRoll = "defense"`, `Morale = "morale"`, `Casting = "casting"`, `RollOff = "roll-off"`; conditions `Shaken = "shaken"`, `Fatigued = "fatigued"`, `Moved = "moved"`; window point `BeforeAttacking = "before-attacking"`; sequence names `HoldSeq = "hold"`, `AdvanceSeq = "advance"`, `RushSeq = "rush"`, `ChargeSeq = "charge"`, `Shooting = "shooting"`, `Melee = "melee"`, `AttackSequence = "attack-sequence"`, `ResolveHits = "resolve-hits"`, `DealWounds = "deal-wounds"`, `MoraleTest = "morale-test"`, `DangerousTerrain = "dangerous-terrain"`, `CastingSeq = "casting"`, `RollOffSeq = "roll-off"`; melee reach `MeleeHorizontal = 2`, `MeleeVertical = 4`; `ChargeCloseIn = 3`, `Consolidate = 3`, `FallBack = 1`, `Separation = 1`; `DifficultCap = 6`; `ExitWithin = 6` (p.14's *"fully within 6\" of it"*); `TokenCap = 6`; `CastRange = 18`; `AmbushDistance = 9`; and the scheduler plan's names, so there is one names class: phases `Deployment = "deployment"`, `Scouts = "scouts"`, `Rounds = Scheduler.SchedulerBase.RoundsPhase`; `Deploy = "Deploy"` (the `Activation.Action` of a deployment answer); rules `Scout`, `Ambush`, `Immobile`, `Aircraft`, `Caster`, `Tough`, `Transport`; condition `SetAside = "set-aside"`; sequence name `TransportDestroyedSeq = "transport-destroyed"` (Task 19)); `GrimdarkFutureRules` (the partial in `GfRules.Sequences.cs`) implementing `Sequence`, `ActionSequence` (`Hold→hold`, …, null otherwise), `RollSpec`, `HitRoll`, `ReactionsAt`, `EndOfTurnSequences`, `OnRoundStarted`, `OnTurnEnded`; `static class GfRolls` with `RollSpec Spec(string roll)`, `RollPlan HitPlan(StepContext, LiveSet, AttackKind)` (threshold from Quality; fatigued in melee → `RequiredRaw = 6` and threshold 6 with no modifiers, p.9), `RollPlan DefensePlan(StepContext, LiveSet, bool inCover)` (base modifier `cover +1` when shooting into cover, p.11), `RollPlan MoralePlan(StepContext, LiveSet)`, `RollPlan CastingPlan(StepContext, int netBid)` (4+, modifier `bid` ±), `Sequence RollOffSequence()` (**the roll-off, owned here and consumed by the scheduler**: one die per team in ordinal order, any teams still tied roll again among themselves until every team is ordered — p.49 needs the whole descending order, not just a winner; `Values["winner_ordinal"]` and `Values["place_1"]..["place_n"]` = team ordinals best first, which the scheduler's `RollOff.Order` reads into `round_started.Order`); `static class GfHooks` with the three hook bodies — `OnRoundStarted(GameState, PropertySet)`, `OnTurnEnded(GameState, PropertySet, TurnContext)`, `EndOfTurnSequences(GameState, PropertySet, Guid? actor)` — and `IRL<Guid> UnitsToTestAtEndOfActivation(GameState, PropertySet, Guid? actor)` (took wounds this activation, now at half or less of starting size — or Tough value for a single model — and did not fight a melee this activation; p.10), `bool AtHalfOrLess(GameState, PropertySet, Guid unitId)`, `int StartingSize(GameState, Guid unitId)` (models in the snapshot), `bool FoughtMeleeThisActivation(GameState, Guid? actor, Guid unitId)` (the actor bounds the activation; no activation frame means false).
- The two flagged interpretations are constants here, visibly: `GfRules.OneTryPerSpellScope = "activation"` (question 2) and `GfRules.RecomputeAttacksPerGroup = true` (question 3).

Spec: action sequences → One roll primitive (the split), Grimdark Future's sequences (fatigue read by the melee hit roll; the End-of-activation hook; the token economy: +X a round, capped at 6, even off the table), Rulings; scheduler → The round boundary (clear round-scoped conditions, spell tokens), The line (end-of-turn hook), Deployment (roll-off: null threshold, ties rerolled); rulebook p.7–11, p.13 (`Caster`, cover).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfRollsTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static readonly IGameRules Gf = GrimdarkFutureRules.Instance;

    [Fact]
    public void Roll_specs_are_per_roll_and_thresholds_are_functions()
    {
        var a = Unit("A", "3+", "5+", [], 1, [], Melee("Fist", 1)); var b = Unit("B", "4+", "2+", [], 1, []);
        var state = State([a], [b]);
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, a.Id), Party.Whole(state, b.Id), AttackKind.Melee, "Charge", 1, []));
        Assert.Equal(3, Gf.RollSpec(GfNames.Hit).Threshold(live, Side.Attacker));
        Assert.Equal(2, Gf.RollSpec(GfNames.DefenseRoll).Threshold(live, Side.Defender));
        Assert.Equal(4, Gf.RollSpec(GfNames.Morale).Threshold(live, Side.Defender));
        Assert.Equal(4, Gf.RollSpec(GfNames.Casting).Threshold(live, Side.Attacker));
        Assert.Null(Gf.RollSpec(GfNames.RollOff).Threshold(live, Side.Attacker));
        Assert.Equal(new RollConvention(6, null), Gf.RollSpec(GfNames.Hit).Convention);
        Assert.Equal(GfNames.Hit, Gf.HitRoll);
        Assert.Equal(GfNames.ChargeSeq, Gf.ActionSequence(GfNames.Charge));
        Assert.Throws<UnknownSequenceException>(() => Gf.Sequence("nope"));
    }

    [Fact]
    public void Fatigue_makes_melee_hit_only_on_unmodified_sixes_and_cover_adds_one_to_defense_against_shooting()
    {
        var a = Unit("A", "3+", "5+", [], 1, [], Melee("Fist", 1, P("Lance"))); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(State([a], [b]), 6, 5);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Fatigued, true)]);
        var frame = new FrameView(new Frame(Guid.CreateVersion7(), "x", new SequenceContext(a.Id, [b.Id], [], GfNames.Charge)), 0, new Dictionary<string, int>(), StepHistory.Empty, null);
        var ctx = new StepContext(sim.State, Core, Gf, frame, new Roller(new ScriptedDice(6, 5), sim.State.RngSeed, 0));
        var live = LiveProperties.Settle(new Interaction(sim.State, Core, Party.Of(sim.State, a.Id, a.Models.Select(m => m.Id), a.Models[0].Weapons.Select(w => w.Id)), Party.Whole(sim.State, b.Id), AttackKind.Melee, GfNames.Charge, 1, []));
        var plan = GfRolls.HitPlan(ctx, live, AttackKind.Melee);
        Assert.Equal((6, 6, 0), (plan.RequiredRaw, plan.Threshold, plan.Modifiers.Count));       // Lance's +1 does not help a fatigued unit
        Assert.Equal([true, false], ctx.Roll(plan, 2).Dice.Select(d => d.Success));
        var cover = GfRolls.DefensePlan(ctx, live with { }, inCover: true);
        Assert.Contains(cover.Modifiers, m => m.Source == "cover" && m.Amount == 1);
        Assert.DoesNotContain(GfRolls.DefensePlan(ctx, LiveProperties.Settle(live.Interaction with { Attack = AttackKind.Shooting }), inCover: false).Modifiers, m => m.Source == "cover");
    }

    [Fact]
    public void Round_start_clears_fatigue_and_grants_capped_tokens_and_turn_end_clears_wounds_this_activation()
    {
        var caster = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 2)]); var grunts = Unit("Grunts", "4+", "4+", [], 3, []);
        var state = State([caster], [grunts]);
        state.Applier.Apply(new GameEvent(2, null, new ConditionSet(new Subject(SubjectKind.Unit, grunts.Id), GfNames.Fatigued, true), null));
        state.Applier.Apply(new GameEvent(3, null, new SpellTokensSet(caster.Models[0].Id, 5), null));
        state.Applier.Apply(new GameEvent(4, null, new WoundsThisActivationSet(grunts.Id, 2), null));
        var round = Gf.OnRoundStarted(state, Core, 2);
        Assert.Contains(round, c => c is ConditionSet { Condition: GfNames.Fatigued, Present: false } cs && cs.Subject.Id == grunts.Id);
        Assert.Contains(round, c => c is SpellTokensSet { Tokens: 6 } t && t.ModelId == caster.Models[0].Id);
        var turn = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team2, null, 0, 0));
        Assert.Contains(turn, c => c is WoundsThisActivationSet { Wounds: 0 } w && w.UnitId == grunts.Id);
        Assert.Empty(Gf.OnRoundStarted(state, Core, 0));                                                          // the phases before round one carry none
        state.Applier.Apply(new GameEvent(5, null, new ConditionSet(new Subject(SubjectKind.Unit, grunts.Id), GfNames.Shaken, true), null));
        var idle = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team2, new Activation(grunts.Id, ActivationOption.Idle, false, []), 0, 0));
        Assert.Contains(idle, c => c is ConditionSet { Condition: GfNames.Shaken, Present: false } cs && cs.Subject.Id == grunts.Id);   // p.10: idling stops Shaken
        var moved = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team2, new Activation(grunts.Id, GfNames.Advance, true, []), 0, 0));
        Assert.DoesNotContain(moved, c => c is ConditionSet { Condition: GfNames.Shaken, Present: false });                             // an ordinary Advance does not
    }

    [Fact]
    public void A_shaken_aircrafts_mandatory_advance_still_stops_it_being_shaken()
    {
        var jet = Unit("Jet", "4+", "4+", [P("Aircraft")], 1, []);
        var state = State([jet], [Unit("T", "4+", "4+", [], 1, [])]);
        state.Applier.Apply(new GameEvent(2, null, new ConditionSet(new Subject(SubjectKind.Unit, jet.Id), GfNames.Shaken, true), null));
        // p.13: an Aircraft may only use Advance actions, even if Shaken, and "their mandatory move doesn't break the staying idle requirement to stop being Shaken".
        var advanced = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team1, new Activation(jet.Id, GfNames.Advance, true, []), 0, 0));
        Assert.Contains(advanced, c => c is ConditionSet { Condition: GfNames.Shaken, Present: false } cs && cs.Subject.Id == jet.Id);
    }

    [Fact]
    public void End_of_activation_tests_units_at_half_or_less_that_took_wounds_and_did_not_fight_a_melee()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 4, []); var big = Unit("Big", "4+", "4+", [], 1, [P("Tough", 6)]);
        var state = State([a], [b, big]);
        void Kill(Guid model) => state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved([new ModelRelocation(model, state.Projection.Models[model].Location, new OnShelf())]), null));
        Kill(b.Models[0].Id); Kill(b.Models[1].Id);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsThisActivationSet(b.Id, 2), null));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsSet(big.Models[0].Id, 3), null));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsThisActivationSet(big.Id, 3), null));
        var tests = Gf.EndOfTurnSequences(state, Core, new TurnContext(Samples.Team1, new Activation(a.Id, GfNames.Hold, true, []), 0, 0));
        Assert.Equal(2, tests.Count);
        Assert.All(tests, t => Assert.Equal((GfNames.MoraleTest, "wounds"), (t.Sequence, t.Context.Action)));
        Assert.Equal(new HashSet<Guid> { b.Id, big.Id }, tests.Select(t => t.Context.ActorUnitId!.Value).ToHashSet());
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsThisActivationSet(b.Id, 1), null));
        Assert.True(GfHooks.AtHalfOrLess(state, Core, b.Id));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsSet(big.Models[0].Id, 2), null));
        Assert.False(GfHooks.AtHalfOrLess(state, Core, big.Id));                                   // 4 of Tough(6) left
    }

    [Fact]
    public void Containment_settles_reach_without_measuring_and_carried_models_still_count()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var riders = Unit("Riders", "4+", "4+", [], 4, []);
        var stowaways = Unit("Stowaways", "4+", "4+", [], 2, []);
        var outside = Unit("Outside", "4+", "4+", [], 1, []);
        var state = State([bus, riders], [stowaways, outside]);
        var container = bus.Models[0].Id;
        void Embark(Tabletop.Content.Unit u) => state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null,
            new ModelsMoved(u.Models.Select(m => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new Inside(container))).ToList()), null));
        Embark(riders); Embark(stowaways);
        Assert.Null(Battlefield.InsideReach(state, outside.Id, bus.Id));                           // both on the table: measure it
        Assert.False(Battlefield.InsideReach(state, outside.Id, riders.Id));                       // p.14: no sight into a transport
        Assert.False(Battlefield.InsideReach(state, riders.Id, outside.Id));                       // nor out of one
        Assert.True(Battlefield.InsideReach(state, riders.Id, stowaways.Id));                      // p.14: units inside can target other units inside
        Assert.True(Battlefield.InsideReach(state, riders.Id, bus.Id));                            // p.14: and the Transport itself
        // p.10 counts the models a unit still has, and being carried has cost it none.
        Assert.False(GfHooks.AtHalfOrLess(state, Core, riders.Id));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null,
            new ModelsMoved(riders.Models.Take(2).Select(m => new ModelRelocation(m.Id, new Inside(container), new OnShelf())).ToList()), null));
        Assert.True(GfHooks.AtHalfOrLess(state, Core, riders.Id));                                 // two of four dead inside it
    }

    [Fact]
    public void The_roll_off_records_one_die_per_team_and_rerolls_ties_until_one_wins()
    {
        var sim = Sim.Gf(State([Unit("A", "4+", "4+", [], 1, [])], [Unit("B", "4+", "4+", [], 1, [])]), 4, 4, 2, 5);
        sim.Append([new RoundStarted(0, [Samples.Team1, Samples.Team2], [])]);
        var events = sim.Start(GfNames.RollOffSeq, new SequenceContext(null, [], [], null));
        var step = events.OfType<StepCompleted>().Single();
        Assert.Equal(2, step.Rolls.Count);                                                          // a tie, then the decider
        Assert.All(step.Rolls, r => Assert.Null(r.Threshold));
        Assert.Equal([4, 4], step.Rolls[0].Dice.Select(d => d.Raw)); Assert.Equal([2, 5], step.Rolls[1].Dice.Select(d => d.Raw));
        Assert.Equal((2, 2, 1), (step.Values!["winner_ordinal"], step.Values["place_1"], step.Values["place_2"]));   // what the scheduler reads into round_started.Order
        Assert.Equal(4, DiceCursor.DrawnSoFar(sim.State));
    }

    [Fact]
    public void The_battlefield_reads_range_sight_and_cover_from_geometry()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 2, []);
        var state = State([a], [b], x1: -6, x2: 6);
        Assert.True(Battlefield.CanSee(state, a.Models[0].Id, b.Models[0].Id));
        Assert.InRange(Battlefield.Distance(state, a.Models[0].Id, b.Models[0].Id), 10.0, 12.0);   // 12" centre to centre, less the two bases
        Assert.False(Battlefield.InCover(state, b.Id, a.Models[0].Id));
        Assert.Empty(Battlefield.TerrainUnder(state, b.Id));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `Battlefield.cs`**

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>Geometry questions over the projection. Every answer is a measurement; what it means is the ruleset's.</summary>
public static class Battlefield
{
    public static PlacedModel? Placed(GameState state, Guid modelId) =>
        state.Projection.Models[modelId].Location is OnTable t ? At(state, modelId, t.Placement) : null;

    /// <summary>The model's body at a placement it does not hold yet — how a destination is judged before it is applied, and the only way to place an occupant, which has no position of its own.</summary>
    public static PlacedModel At(GameState state, Guid modelId, Placement placement) =>
        new(new ModelBody(state.Projection.Models[modelId].Slot.Base, state.Projection.Models[modelId].Slot.Height), placement);

    /// <summary>
    /// What containment settles about one unit reaching another, before anything is measured (p.14: "Units inside/outside of
    /// Transports don't have line of sight to the outside/inside … but units inside can target other units inside, and the
    /// Transport itself"). False: one is inside and the other cannot see in or out. True: both are inside the same container,
    /// or the reaching unit is inside and the other owns the container — they are in the same box, so there is no distance and
    /// no sight line to take. Null: neither is inside anything, so measure it the usual way.
    /// </summary>
    public static bool? InsideReach(GameState state, Guid fromUnit, Guid toUnit)
    {
        var (from, to) = (Effects.Lookup.ContainerOf(state, fromUnit), Effects.Lookup.ContainerOf(state, toUnit));
        if (from is null && to is null) return null;
        if (from is not { } container) return false;                                              // outside looking in
        return to == container || state.Projection.Models[container].UnitId == toUnit;
    }

    public static IReadOnlyList<WorldVolume> Terrain(GameState state) => state.Board.Table.Terrain.SelectMany(t => t.WorldVolumes()).ToList();

    /// <summary>Terrain plus every model not in the given unit, as sight blockers (p.8: a unit ignores its own models; p.11: other units block).</summary>
    public static IReadOnlyList<WorldVolume> Blockers(GameState state, Guid exceptUnitId) =>
        Terrain(state).Concat(state.Projection.Models.Values.Where(m => m.UnitId != exceptUnitId && m.Location is OnTable)
            .Select(m => Bodies.AsBlockingVolume(m.Id, Placed(state, m.Id)!.Value))).ToList();

    public static bool CanSee(GameState state, Guid fromModel, Guid toModel)
    {
        var (from, to) = (Placed(state, fromModel), Placed(state, toModel));
        if (from is null || to is null) return false;
        var blockers = Blockers(state, state.Projection.Models[fromModel].UnitId).Where(v => v.Id != toModel && v.Id != fromModel).ToList();
        return LineOfSight.Check(from.Value, to.Value, blockers).Clear;
    }

    public static double Distance(GameState state, Guid a, Guid b) => Geometry.Distance.EdgeToEdge(Placed(state, a)!.Value, Placed(state, b)!.Value);
    public static double Vertical(GameState state, Guid a, Guid b) => Geometry.Distance.Vertical(Placed(state, a)!.Value, Placed(state, b)!.Value);

    public static double? UnitDistance(GameState state, Guid unitA, Guid unitB)
    {
        var pairs = from a in Effects.Lookup.LivingModels(state, unitA) from b in Effects.Lookup.LivingModels(state, unitB) select Distance(state, a, b);
        return pairs.Any() ? pairs.Min() : null;
    }

    /// <summary>p.11: the majority of models fully inside cover terrain or behind a sight blocker (multi-model); mostly inside — centre inside — for a single model.</summary>
    public static bool InCover(GameState state, Guid targetUnit, Guid shooterModel)
    {
        var shooter = Placed(state, shooterModel); if (shooter is null) return false;
        var terrain = Terrain(state);
        var blockers = Blockers(state, state.Projection.Models[shooterModel].UnitId);
        var living = Effects.Lookup.LivingModels(state, targetUnit);
        if (living.Count == 1)
        {
            var only = Placed(state, living[0])!.Value;
            return Containment.VolumesContaining(only, terrain).Any(id => terrain.First(v => v.Id == id).Volume.Declarations.HasFlag(TerrainDeclarations.Cover))
                || Cover.SightBlockersBetween(shooter.Value, only, blockers.Where(v => v.Id != living[0]).ToList()).Count > 0;
        }
        var covered = living.Count(id => { var m = Placed(state, id)!.Value; return Cover.InsideCoverVolumes(m, terrain).Count > 0 || Cover.SightBlockersBetween(shooter.Value, m, blockers.Where(v => v.Id != id).ToList()).Count > 0; });
        return covered * 2 > living.Count;
    }

    public static IReadOnlyList<string> TerrainUnder(GameState state, Guid unitId)
    {
        var terrain = Terrain(state);
        var flags = Effects.Lookup.LivingModels(state, unitId).SelectMany(id => Containment.VolumesContaining(Placed(state, id)!.Value, terrain)).Distinct()
            .Select(id => terrain.First(v => v.Id == id).Volume.Declarations).Aggregate(TerrainDeclarations.None, (acc, d) => acc | d);
        return Enum.GetValues<TerrainDeclarations>().Where(f => f != TerrainDeclarations.None && flags.HasFlag(f)).Select(f => Tabletop.Content.ContentJson.Serialize(f).Trim('"')).ToList();
    }

    public static Placement Settle(GameState state, Placement client) =>
        new(client.Position with { Z = Settling.SurfaceHeightAt(client.Position, Terrain(state)) }, client.Yaw);
}
```

- [ ] **Step 3: Write `GfNames.cs` and fill in `GfRules.Sequences.cs`**

```csharp
namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>Every Grimdark Future name the C# half knows. The data half (Content/grimdark-future-core.properties.json) declares the same names.</summary>
public static class GfNames
{
    public const string Quality = "Quality", Defense = "Defense", Range = "Range", Attacks = "Attacks";
    public const string Hold = "Hold", Advance = "Advance", Rush = "Rush", Charge = "Charge";
    public const string Hit = "hit", DefenseRoll = "defense", Morale = "morale", Casting = "casting", RollOff = "roll-off";
    public const string Shaken = "shaken", Fatigued = "fatigued", Moved = "moved";
    public const string BeforeAttacking = "before-attacking";
    public const string HoldSeq = "hold", AdvanceSeq = "advance", RushSeq = "rush", ChargeSeq = "charge", Shooting = "shooting", Melee = "melee";
    public const string AttackSequence = "attack-sequence", ResolveHits = "resolve-hits", DealWounds = "deal-wounds", MoraleTest = "morale-test";
    public const string DangerousTerrain = "dangerous-terrain", CastingSeq = "casting", RollOffSeq = "roll-off";
    public const string TransportDestroyedSeq = "transport-destroyed";                                                  // p.14: a wrecked container disgorges (Task 19)
    public const double MeleeHorizontal = 2, MeleeVertical = 4, ChargeCloseIn = 3, Consolidate = 3, FallBack = 1, Separation = 1, CastRange = 18, DifficultCap = 6;
    /// <summary>p.14 and p.20: a unit leaving a container "must stay fully within 6" of it".</summary>
    public const double ExitWithin = 6;
    public const int TokenCap = 6;

    // The scheduler plan's names (its Task 2 reads them from here; one names class).
    public const string Deployment = "deployment", Scouts = "scouts", Rounds = Scheduler.SchedulerBase.RoundsPhase;   // phases
    public const string Deploy = "Deploy";                                                                              // Activation.Action for a deployment answer
    public const string Scout = "Scout", Ambush = "Ambush", Immobile = "Immobile", Aircraft = "Aircraft", Caster = "Caster", Tough = "Tough";
    public const string Transport = "Transport";                                                                        // p.14; its (X) is carried and never read — game flow: capacity is not enforced
    public const string SetAside = "set-aside";                                                                         // menu-written condition on a shelved unit
    public const double AmbushDistance = 9;
}
```

`GfNames.Rounds` is `SchedulerBase.RoundsPhase` so the two spellings cannot drift; land the scheduler plan's `SchedulerBase.cs` constant first (it has no other dependency) or write `"rounds"` and switch when it lands.

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The interpreter plan's partial of GrimdarkFutureRules (identity is in the skeleton's GfRules.cs; the scheduler's hooks in GfRules.Scheduling.cs and GfRules.Deployment.cs). The sequence catalog fills in over Tasks 12–17.</summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>Flagged interpretations, not silent guesses (action sequences → Rulings). Questions 2 and 3.</summary>
    public const string OneTryPerSpellScope = "activation";
    public const bool RecomputeAttacksPerGroup = true;

    private readonly Lazy<IReadOnlyDictionary<string, Sequence>> _sequences = new(() => new Sequence[]
    {
        GfRolls.RollOffSequence(),
        // Tasks 12–17 add: GfAttack.DealWounds(), GfAttack.ResolveHits(), GfAttack.AttackSequence(), GfShooting.Shooting(), GfMoves.Hold(), GfMoves.Advance(),
        // GfMoves.Rush(), GfMoves.DangerousTerrain(), GfMelee.Charge(), GfMelee.Melee(), GfMorale.MoraleTest(), GfCasting.Casting()
    }.ToDictionary(s => s.Name));

    public Sequence Sequence(string name) => _sequences.Value.TryGetValue(name, out var s) ? s : throw new UnknownSequenceException(name);
    public string? ActionSequence(string actionName) => actionName switch
    {
        GfNames.Hold => GfNames.HoldSeq, GfNames.Advance => GfNames.AdvanceSeq, GfNames.Rush => GfNames.RushSeq, GfNames.Charge => GfNames.ChargeSeq,
        _ => null,   // an out-of-set action accepted under warn/silent: recorded, no sequence
    };
    public RollSpec RollSpec(string roll) => GfRolls.Spec(roll);
    public string HitRoll => GfNames.Hit;
    public IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame) => GfHooks.ReactionsAt(state, properties, point, frame);
    public IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, TurnContext turn) => GfHooks.EndOfTurnSequences(state, properties, turn.Activation?.UnitId);
    public IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round) => round == 0 ? [] : GfHooks.OnRoundStarted(state, properties);
    public IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, TurnContext turn) => GfHooks.OnTurnEnded(state, properties, turn);
}
```

- [ ] **Step 4: Write `GfRolls.cs`**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>OPR's dice: 1D6 against a target number, never clamped. Thresholds are functions of the live set; fatigue and cover are read here.</summary>
public static class GfRolls
{
    static readonly RollConvention D6 = new(6, null);
    static readonly StatRef Quality = new(StatLevel.Unit, GfNames.Quality), Defense = new(StatLevel.Unit, GfNames.Defense);

    public static RollSpec Spec(string roll) => roll switch
    {
        GfNames.Hit => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Quality)),
        GfNames.DefenseRoll => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Defense)),
        GfNames.Morale => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Quality)),
        GfNames.Casting => new(roll, D6, (_, _) => 4),
        GfNames.RollOff => new(roll, D6, (_, _) => null),
        _ => new(roll, D6, (_, _) => null),   // a property's own roll (Regeneration, Impact, Fearless) carries its threshold in the effect
    };

    /// <summary>p.9: after attacking in melee for the first time in a round, a unit hits only on unmodified 6s until the round ends.</summary>
    public static RollPlan HitPlan(StepContext ctx, LiveSet live, AttackKind kind)
    {
        var attacker = ctx.Projection.Units[live.Interaction.Attacker.UnitId];
        if (kind == AttackKind.Melee && attacker.Conditions.Contains(GfNames.Fatigued))
            return new RollPlan(GfNames.Hit, 6, [], D6, RequiredRaw: 6, RerollRaw: new HashSet<int>());
        return ctx.Plan(live, Side.Attacker, GfNames.Hit, []);
    }

    /// <summary>p.11: +1 to Defense against shooting when in cover. Blast, Indirect and Lock-On suppress it (pass one).</summary>
    public static RollPlan DefensePlan(StepContext ctx, LiveSet live, bool inCover) =>
        ctx.Plan(live, Side.Defender, GfNames.DefenseRoll, live.Interaction.Attack == AttackKind.Shooting && inCover ? [new RollModifier("cover", 1)] : []);

    public static RollPlan MoralePlan(StepContext ctx, LiveSet live) => ctx.Plan(live, Side.Attacker, GfNames.Morale, []);

    /// <summary>p.13: one die on 4+, ±1 per token bid for or against.</summary>
    public static RollPlan CastingPlan(StepContext ctx, int netBid) =>
        new(GfNames.Casting, 4, netBid == 0 ? [] : [new RollModifier("bid", netBid)], D6, null, new HashSet<int>());

    /// <summary>
    /// The roll-off the scheduler starts (scheduler → Deployment; p.6, p.49): one die per team in ordinal order, no threshold; teams whose
    /// results are still tied roll again among themselves — roll k's dice belong, in ordinal order, to the teams tied after rolls 0..k-1 —
    /// until every team is ordered, because free-for-all activates "in descending order" and needs the whole order, not a winner.
    /// Values: winner_ordinal, and place_1..place_n — the team ordinals best first — which Scheduler.RollOff.Order reads into round_started.Order.
    /// </summary>
    public static Sequence RollOffSequence() => new(GfNames.RollOffSeq, [new RollOffStep()]);

    sealed class RollOffStep : IStep
    {
        public string Name => "roll-off";
        public StepOutcome Run(StepContext ctx)
        {
            var teams = ctx.State.Info.Teams.OrderBy(t => t.Ordinal).ToList();
            var plan = new RollPlan(GfNames.RollOff, null, [], D6, null, new HashSet<int>());
            var keys = teams.ToDictionary(t => t.Id, _ => new List<int>());
            for (var round = 0; round < 100; round++)
            {
                var tied = teams.Where(t => teams.Any(o => o.Id != t.Id && keys[o.Id].SequenceEqual(keys[t.Id]))).ToList();
                if (tied.Count == 0) break;
                var record = ctx.Roll(plan, tied.Count);
                for (var i = 0; i < tied.Count; i++) keys[tied[i].Id].Add(record.Dice[i].Raw);
            }
            var ranked = teams.OrderByDescending(t => keys[t.Id], KeyComparer.Instance).ThenBy(t => t.Ordinal).ToList();
            var values = new Dictionary<string, int>();
            if (ranked.Count > 0) values["winner_ordinal"] = ranked[0].Ordinal;
            for (var k = 0; k < ranked.Count; k++) values[$"place_{k + 1}"] = ranked[k].Ordinal;
            return new Complete([], values);
        }
    }

    /// <summary>Lexicographic over each team's raw results; a longer key (more tie-break rolls) ranks above its prefix.</summary>
    sealed class KeyComparer : IComparer<List<int>>
    {
        public static readonly KeyComparer Instance = new();
        public int Compare(List<int>? a, List<int>? b)
        {
            for (var i = 0; i < Math.Min(a!.Count, b!.Count); i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Count.CompareTo(b.Count);
        }
    }
}
```

- [ ] **Step 5: Write `GfHooks.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The hooks the scheduler calls, and who may react at a window. Every reading is of the projection and the log, never of a flag.</summary>
public static class GfHooks
{
    /// <summary>Round boundary: fatigue clears (action sequences → Fatigue); every Caster(X) gets X tokens, capped at 6, on the table or not (p.13).</summary>
    public static IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet props)
    {
        var changes = new List<StateChange>();
        foreach (var u in state.Projection.Units.Values.Where(u => u.Conditions.Contains(GfNames.Fatigued)))
            changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.Fatigued, false));
        foreach (var m in state.Projection.Models.Values)
            if (Lookup.ModelParameter(state, m.Id, GfNames.Caster) is { } x && x > 0) changes.Add(new SpellTokensSet(m.Id, Math.Min(GfNames.TokenCap, m.SpellTokens + x)));
        return changes;
    }

    /// <summary>turn_ended: the wounds-this-activation counters clear (whole-design review → Gaps filled); an activation spent idle stops the unit being Shaken (p.10).</summary>
    public static IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet props, Scheduler.TurnContext turn)
    {
        var changes = state.Projection.Units.Values.Where(u => u.WoundsThisActivation > 0).Select(u => (StateChange)new WoundsThisActivationSet(u.Id, 0)).ToList();
        // p.10: "Shaken units must spend their activation being idle and doing nothing, which stops them from being Shaken at the end of their activation."
        // p.13: an Aircraft "may only use Advance actions … (even if Shaken)" and "their mandatory move doesn't break the staying idle requirement
        // to stop being Shaken" — so a Shaken Aircraft's Advance counts as having idled.
        if (turn.Activation is { } act && state.Projection.Units[act.UnitId].Conditions.Contains(GfNames.Shaken)
            && (act.Action == Scheduler.ActivationOption.Idle || (act.Action == GfNames.Advance && Lookup.UnitHas(state, props, act.UnitId, GfNames.Aircraft))))
            changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, act.UnitId), GfNames.Shaken, false));
        return changes;
    }

    /// <summary>p.10: at the end of an activation, a unit that took wounds leaving it at half or less tests; units that fought this melee compared wounds instead. The actor bounds "this activation".</summary>
    public static IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet props, Guid? actor) =>
        UnitsToTestAtEndOfActivation(state, props, actor).Select(u => new SequenceStart(GfNames.MoraleTest, new SequenceContext(u, [], [], "wounds"))).ToList();

    public static IReadOnlyList<Guid> UnitsToTestAtEndOfActivation(GameState state, PropertySet props, Guid? actor) =>
        state.Projection.Units.Values.Where(u => u.WoundsThisActivation > 0 && AtHalfOrLess(state, props, u.Id) && !FoughtMeleeThisActivation(state, actor, u.Id) && Lookup.AliveModels(state, u.Id).Count > 0)
            .Select(u => u.Id).ToList();

    public static int StartingSize(GameState state, Guid unitId) => state.Projection.Units[unitId].Definition.Models.Count;

    /// <summary>Half or less of starting size, or of the Tough value for a single-model unit (p.10). Counted over the models still in play: a unit inside a transport has not lost a model by being carried.</summary>
    public static bool AtHalfOrLess(GameState state, PropertySet props, Guid unitId)
    {
        var start = StartingSize(state, unitId);
        if (start == 1)
        {
            var model = state.Projection.Units[unitId].Definition.Models[0];
            var tough = Lookup.ModelParameter(state, model.Id, "Tough") ?? 1;
            var remaining = state.Projection.Models[model.Id].Location is not OnShelf ? tough - state.Projection.Models[model.Id].Wounds : 0;
            return remaining * 2 <= tough;
        }
        return Lookup.AliveModels(state, unitId).Count * 2 <= start;
    }

    /// <summary>A melee sequence started since the actor's activation began, with this unit as charger or target. No activation frame in the log means no melee this activation — never a scan of the whole log, which would report a melee from an earlier round.</summary>
    public static bool FoughtMeleeThisActivation(GameState state, Guid? actor, Guid unitId)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var activation = log.FindLastIndex(e => e.Payload is SequenceStarted s
            && s.Context.Action is GfNames.Hold or GfNames.Advance or GfNames.Rush or GfNames.Charge
            && (actor is null || s.Context.ActorUnitId == actor));
        if (activation < 0) return false;
        return log.Skip(activation).Any(e => e.Payload is SequenceStarted { Sequence: GfNames.Melee } m && (m.Context.ActorUnitId == unitId || m.Context.TargetUnitIds.Contains(unitId)));
    }

    /// <summary>The one core window: before attacking, the acting unit's own team may cast if a model in it has tokens (p.13). Task 17 supplies the sequence.</summary>
    public static IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet props, string point, FrameView frame)
    {
        if (point != GfNames.BeforeAttacking || frame.Context.ActorUnitId is not { } unit) return [];
        var casters = Lookup.AliveModels(state, unit).Where(m => state.Projection.Models[m].SpellTokens > 0 && Lookup.ModelParameter(state, m, "Caster") is not null).ToList();   // p.14: a caster inside a container may still cast at what is inside with it
        if (casters.Count == 0 || Teams.Of(state, unit) is not { } team) return [];
        return [new Reaction(team, "cast", "Cast a spell", GfNames.CastingSeq, new SequenceContext(unit, [], [], null))];
    }
}
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfRollsTests`; expect 8 passed. Commit: `Add Grimdark Future's roll specs, hooks, names and the battlefield reader`.**

### Task 12: deal-wounds

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (deal-wounds; Task 13 adds resolve-hits and attack-sequence to the same file)
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (register `GfAttack.DealWounds()`)
- Test: `tests/Tabletop.GameServer.Tests/GfDealWoundsTests.cs`

**Interfaces:**
- Consumes: Tasks 6–11; `Allocation`, `Allocated`, `WoundsSet`, `ModelsMoved`, `WoundsThisActivationSet` (Protocol).
- Produces: `static class GfAttack` with `Sequence DealWounds()` (name `deal-wounds`, one step `assign`; Task 19 adds `disgorge`); `static Interaction AttackInteraction(StepContext ctx, Guid? actor, Guid target, IRL<Guid> weapons, AttackKind? kind, string? action)` (range from `Battlefield.UnitDistance`, terrain from `Battlefield.TerrainUnder(target)`); `static int WoundsToDeal(FrameView frame)` (failures of every `defense` roll in the parent frame's completed steps, or the parent's `Values["wounds"]` when it rolled none — dangerous terrain); `static IRL<Guid> EligibleForNextWound(GameState, PropertySet, Guid unit)` (p.14: a wounded Tough model first, until it dies; heroes last — over `Lookup.AliveModels`, so a unit inside a container is wounded like any other).
- **Task 19 comes back to this sequence.** `DealWounds()` gains a second step, `disgorge`, and `assign`'s casualty line gains one condition: a model at its capacity that is a container holding somebody is *not* shelved here, because p.14 places its occupants *"before removal"*. Nothing else in this task changes, and a container with nobody inside is an ordinary casualty.
- Context of a `deal-wounds` frame: `ActorUnitId` = the attacker (null when terrain wounds), `TargetUnitIds = [target]`, `WeaponIds` = the attacking group's weapons (so `Deadly`, `Poison`'s and `Rending`'s suppression of `Regeneration` are live), `Action` = the attacker's action. The Ask is `Allocation(Targets = eligible models, Amount = wounds)` to the target's team; the answer is applied as given (an out-of-set allocation accepted under `warn`/`silent` is applied too — nothing is an invariant). Per assigned wound: multiplied by `WoundMultiplier` (`Deadly(X)`, first by ordinal 0, no carry-over past the model's capacity); `Regeneration` live on that model → one die at 5+ per wound (a `RollRecord` named `Regeneration`), success ignores it; the model's `Wounds` rises; at `Tough(X)` (default 1) it goes to the shelf. `WoundsThisActivation` on the target rises by the wounds that landed.

Spec: action sequences → Grimdark Future's sequences (deal-wounds: `Tough` stacking, heroes last, `Deadly` first and no carry-over, `Regeneration` per wound unless suppressed, casualties to the shelf, wounds-this-activation); rulebook p.8 ("the defending player may remove models from the target in any order"), p.13 `Deadly`, p.14 `Regeneration`, `Tough`, `Hero`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfDealWoundsTests
{
    /// <summary>Starts deal-wounds under a parent frame that recorded the wounds, the way resolve-hits does.</summary>
    static Sim Start(Tabletop.Content.Unit attacker, Tabletop.Content.Unit target, int wounds, int[] dice, params Guid[] weapons)
    {
        var sim = Sim.Gf(State([attacker], [target]), dice);
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(attacker.Id, [target.Id], weapons, GfNames.Hold)),
            new StepCompleted(parent, "wounds", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = wounds } }]);
        sim.Start(GfNames.DealWounds, new SequenceContext(attacker.Id, [target.Id], weapons, GfNames.Hold));
        return sim;
    }

    [Fact]
    public void The_defender_allocates_and_tough_models_absorb_until_they_die()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var t = Unit("T", "4+", "4+", [], 3, []) with { Models = [.. Unit("T", "4+", "4+", [], 2, []).Models, Unit("Big", "4+", "4+", [], 1, [P("Tough", 3)]).Models[0] with { Ordinal = 3 }] };
        var sim = Start(a, t, 3, [1]);
        var ask = sim.AskFor(Samples.Team2);
        var legal = Assert.IsType<Allocation>(ask.Legal);
        Assert.Equal((3, 3), (legal.Amount, legal.Targets.Count));
        var (plain, big) = (t.Models[0].Id, t.Models[2].Id);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [plain] = 1, [big] = 2 }));
        Assert.Equal(2, sim.Alive(t.Id));
        Assert.IsType<OnShelf>(sim.Model(plain).Location);
        Assert.Equal(2, sim.Model(big).Wounds);
        Assert.Equal(3, sim.Unit(t.Id).WoundsThisActivation);
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Equal([big], GfAttack.EligibleForNextWound(sim.State, TestProperties.Core, t.Id));                    // a wounded Tough model takes the next wound (p.14)
    }

    [Fact]
    public void Deadly_multiplies_per_model_without_carry_over_and_heroes_are_eligible_last()
    {
        var gun = Gun("Cannon", "24\"", 1, P("Deadly", 3));
        var a = Unit("A", "4+", "4+", [], 1, [], gun);
        var t = Unit("T", "4+", "4+", [], 2, []) with { Models = [Unit("T", "4+", "4+", [], 1, []).Models[0], Unit("H", "4+", "4+", [], 1, [P("Hero"), P("Tough", 3)]).Models[0] with { Ordinal = 2 }] };
        var weapons = a.Models[0].Weapons.Select(w => w.Id).ToArray();
        var sim = Start(a, t, 1, [1], weapons);
        Assert.Equal([t.Models[0].Id], Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Targets);      // the hero waits
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[0].Id] = 1 }));
        Assert.Equal(1, sim.Alive(t.Id));
        Assert.Equal(0, sim.Model(t.Models[1].Id).Wounds);                                                            // nothing carried over
        Assert.Equal(1, sim.Unit(t.Id).WoundsThisActivation);                                                         // one wound landed on a one-wound model
    }

    [Fact]
    public void Regeneration_rolls_per_wound_unless_the_weapon_ignores_it()
    {
        var troll = Unit("Troll", "4+", "4+", [], 1, [P("Regeneration"), P("Tough", 6)]);
        var plain = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var sim = Start(plain, troll, 2, [5, 2]);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [troll.Models[0].Id] = 2 }));
        var regen = sim.Steps("assign").Single().Rolls.Single();
        Assert.Equal(("Regeneration", 5, 2), (regen.Roll, regen.Threshold, regen.Dice.Count));
        Assert.Equal(1, sim.Model(troll.Models[0].Id).Wounds);
        Assert.Equal(1, sim.Unit(troll.Id).WoundsThisActivation);
        var poisoner = Unit("B", "4+", "4+", [], 1, [], Gun("Dart", "24\"", 1, P("Poison")));
        var sim2 = Start(poisoner, troll, 2, [5, 5], poisoner.Models[0].Weapons.Select(w => w.Id).ToArray());
        sim2.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [troll.Models[0].Id] = 2 }));
        Assert.Empty(sim2.Steps("assign").Single().Rolls);
        Assert.Equal(2, sim2.Model(troll.Models[0].Id).Wounds);
    }

    [Fact]
    public void Zero_wounds_finishes_without_asking()
    {
        var sim = Start(Unit("A", "4+", "4+", [], 1, []), Unit("T", "4+", "4+", [], 2, []), 0, [1]);
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Empty(sim.State.Projection.Stack.Where(f => f.Sequence == GfNames.DealWounds));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `GfAttack.cs` (deal-wounds part)**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    public static Sequence DealWounds() => new(GfNames.DealWounds, [new AssignStep()]);

    public static Interaction AttackInteraction(StepContext ctx, Guid? actor, Guid target, IReadOnlyList<Guid> weapons, AttackKind? kind, string? action)
    {
        var state = ctx.State;
        if (actor is not { } a) return new Interaction(state, ctx.Properties, Party.Whole(state, target), null, kind, action, null, Battlefield.TerrainUnder(state, target));
        var models = weapons.Count > 0 ? state.Projection.Models.Values.Where(m => m.Slot.Weapons.Any(w => weapons.Contains(w.Id))).Select(m => m.Id).ToList() : Lookup.LivingModels(state, a);
        return new Interaction(state, ctx.Properties, Party.Of(state, a, models, weapons), Party.Whole(state, target), kind, action, Battlefield.UnitDistance(state, a, target), Battlefield.TerrainUnder(state, target));
    }

    /// <summary>Failures of the parent's defense rolls as they now stand (a reroll between steps counts), or the parent's declared wounds.</summary>
    public static int WoundsToDeal(FrameView frame)
    {
        var parent = frame.Parent ?? throw new InvalidOperationException("deal-wounds needs a parent frame");
        var defense = parent.Frame.Steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.DefenseRoll).ToList();
        return defense.Count > 0 ? defense.Sum(Dice.RollOutcome.Failures) : parent.Value("wounds");
    }

    /// <summary>p.14: keep wounding the most-wounded Tough model until it dies; heroes are assigned wounds last. The in-play reading, so a unit inside a wrecked transport can be wounded before it is placed (Task 19).</summary>
    public static IReadOnlyList<Guid> EligibleForNextWound(GameState state, PropertySet props, Guid unit)
    {
        var living = Lookup.AliveModels(state, unit);
        var woundedTough = living.Where(m => state.Projection.Models[m].Wounds > 0 && (Lookup.ModelParameter(state, m, "Tough") ?? 1) > 1).OrderByDescending(m => state.Projection.Models[m].Wounds).ToList();
        if (woundedTough.Count > 0) return [woundedTough[0]];
        var notHeroes = living.Where(m => !state.Projection.Models[m].Slot.Properties.Any(p => p.Name == "Hero")).ToList();
        return notHeroes.Count > 0 ? notHeroes : living;
    }

    sealed class AssignStep : IStep
    {
        public string Name => "assign";
        public StepOutcome Run(StepContext ctx)
        {
            var target = ctx.Frame.Context.TargetUnitIds[0];
            var wounds = WoundsToDeal(ctx.Frame);
            if (wounds <= 0) return new Finish([]);
            var team = ctx.TeamOf(target) ?? throw new InvalidOperationException("target unit has no team");
            if (ctx.History.LastClosed() is not { Single: { } answer })
                return new OpenAsks([ctx.Ask("remove_casualties", $"Assign {wounds} wound(s)", team, new Allocation(EligibleForNextWound(ctx.State, ctx.Properties, target), wounds))]);

            var actor = ctx.Frame.Context.ActorUnitId;
            var ix = AttackInteraction(ctx, actor, target, ctx.Frame.Context.WeaponIds, null, ctx.Frame.Context.Action);
            var live = LiveProperties.Settle(ix);
            var targetSide = actor is null ? Side.Attacker : Side.Defender;
            var multiplier = actor is null ? 1 : RollEffects.WoundMultiplier(live, Side.Attacker);
            // Regeneration is aggregation none: one live instance per model, keyed here by model; its threshold is the effect row's.
            var regen = RollEffects.OwnRolls(live, targetSide, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.Wound })
                .Where(x => x.Source.ModelId is not null).ToDictionary(x => x.Source.ModelId!.Value, x => x.Roll);
            var allocation = answer is Allocated a ? a.Counts : new Dictionary<Guid, int> { [EligibleForNextWound(ctx.State, ctx.Properties, target)[0]] = wounds };

            var changes = new List<StateChange>(); var landed = 0; var moves = new List<ModelRelocation>();
            foreach (var (modelId, count) in allocation.Where(kv => kv.Value > 0))
            {
                var model = ctx.Projection.Models[modelId];
                var capacity = Lookup.ModelParameter(ctx.State, modelId, "Tough") ?? 1;
                var incoming = count * multiplier;
                if (regen.TryGetValue(modelId, out var roll))
                {
                    var record = ctx.Roll(new Dice.RollPlan(live.On(targetSide).First(p => p.Entry.Effects.Contains(roll)).Entry.Name, roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), incoming);
                    incoming = Dice.RollOutcome.Failures(record);   // each success overrides one wound
                }
                var total = Math.Min(capacity, model.Wounds + incoming);
                landed += total - model.Wounds;
                changes.Add(new WoundsSet(modelId, total));
                if (total >= capacity) moves.Add(new ModelRelocation(modelId, model.Location, new OnShelf()));
            }
            if (moves.Count > 0) changes.Add(new ModelsMoved(moves));
            changes.Add(new WoundsThisActivationSet(target, ctx.Projection.Units[target].WoundsThisActivation + landed));
            return new Complete(changes, new Dictionary<string, int> { ["wounds_dealt"] = landed });
        }
    }
}
```

`GfRules._sequences` gains `GfAttack.DealWounds()`. A property's own roll is recorded under the entry's name (`Regeneration`) with the effect row's threshold, so nothing here knows the number 5 (spec correction 3).

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfDealWoundsTests`; expect 4 passed. Commit: `Deal wounds: the defender allocates, Tough and Deadly and Regeneration resolve, casualties shelve`.**

### Task 13: attack-sequence and resolve-hits — the four steps of pages 8 and 9

**Files:**
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs`, `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`
- Test: `tests/Tabletop.GameServer.Tests/GfAttackTests.cs`

**Interfaces:**
- Consumes: Tasks 7, 11, 12.
- Produces: `GfAttack.AttackSequence()` (`attack-sequence`: `determine-attacks`, `roll-to-hit`, `resolve`), `GfAttack.ResolveHits()` (`resolve-hits`: `roll-to-block`, `deal`); `static AttackKind KindOf(GameState, IRL<Guid> weapons)` (a group with a `Range` stat shoots, otherwise melee — content values → a melee weapon has no range); `static IRL<Guid> EligibleModels(GameState, LiveSet, Guid attacker, Guid target, IRL<Guid> weapons, AttackKind)` (shooting: line of sight to a target model and a weapon in range of it, p.8; melee: within 2" horizontally and 4" vertically, p.9; `Battlefield.InsideReach` first, so containment short-circuits both — p.14: no sight into or out of a container, but an occupant may target a fellow occupant and the container itself, with nothing to measure); `static int HitsFrom(FrameView parent)` (successes of the parent's `hit` rolls plus its `extra_hits`, times its `hit_multiplier`; `parent.Value("hits")` when the parent rolled no hit roll — `Impact`).
- Context of an `attack-sequence` frame: `ActorUnitId` = the attacker, `TargetUnitIds = [target]`, `WeaponIds` = one weapon group (one weapon type), `Action` = the action the attacker took this activation (null for a strike-back). `roll-to-hit` records `Values`: `extra_hits` (`Furious`/`Relentless` per unmodified 6), `hit_multiplier` (`Blast(X)` capped at models in the target), and `granted_<entry>_<amount>` counts for per-die grants (`Rending`'s 6s), so `roll-to-block` rolls one defense roll per grant group with that property folded into the live set. Cover: `Battlefield.InCover(target, first eligible attacker model)`.
- `RecomputeAttacksPerGroup = true`: `determine-attacks` reads the target's living models when its own group starts, so a later group's attacks reflect an earlier group's casualties (question 3).

Spec: action sequences → Grimdark Future's sequences (attack-sequence, resolve-hits), Shooting and melee are one sequence; rulebook p.8 (the worked example: 3 attacks, Quality 4+, rolls 3/4/5 → 2 hits; Defense 4+, rolls 3/4 → 1 wound), p.9 (melee reach), p.13 `Blast`, `Furious`, p.14 `Rending`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfAttackTests
{
    static SequenceContext Attack(Tabletop.Content.Unit a, Tabletop.Content.Unit t, string action) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), action);

    [Fact]
    public void Page_8_three_warriors_shoot_the_dwarves_two_hits_one_wound_one_casualty()
    {
        var warriors = Unit("Dynasty Warriors", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1));
        var dwarves = Unit("Dwarves", "4+", "4+", [], 4, []);
        var sim = Sim.Gf(State([warriors], [dwarves], x1: -6, x2: 6), 3, 4, 5, 3, 4);
        sim.Start(GfNames.AttackSequence, Attack(warriors, dwarves, GfNames.Hold));
        var determine = sim.Steps("determine-attacks").Single();
        Assert.Equal(3, determine.Values!["attacks"]);
        var hit = sim.Steps("roll-to-hit").Single().Rolls.Single();
        Assert.Equal((GfNames.Hit, 4, 2), (hit.Roll, hit.Threshold, hit.Dice.Count(d => d.Success)));
        var block = sim.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Equal((GfNames.DefenseRoll, 4, 2, 1), (block.Roll, block.Threshold, block.Dice.Count, block.Dice.Count(d => !d.Success)));
        Assert.Equal([GfNames.AttackSequence, GfNames.ResolveHits, GfNames.DealWounds], sim.State.Projection.Stack.Select(f => f.Sequence));
        var ask = sim.AskFor(Samples.Team2);
        Assert.Equal(1, Assert.IsType<Allocation>(ask.Legal).Amount);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [dwarves.Models[0].Id] = 1 }));
        Assert.Equal(3, sim.Alive(dwarves.Id));
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Equal(5, Dice.DiceCursor.DrawnSoFar(sim.State));
    }

    [Fact]
    public void Out_of_range_models_add_no_attacks_and_no_attacks_finishes_early()
    {
        var far = Unit("Far", "4+", "4+", [], 2, [], Gun("Pistol", "6\"", 2));
        var t = Unit("T", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([far], [t], x1: -12, x2: 12), 6);
        sim.Start(GfNames.AttackSequence, Attack(far, t, GfNames.Hold));
        Assert.Equal(0, sim.Steps("determine-attacks").Single().Values!["attacks"]);
        Assert.Empty(sim.Steps("roll-to-hit")); Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Blast_multiplies_hits_up_to_the_target_size_and_rending_sixes_block_against_ap_four()
    {
        var mortar = Unit("Mortar", "4+", "4+", [], 1, [], Gun("Mortar", "30\"", 2, P("Blast", 3)));
        var pair = Unit("Pair", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([mortar], [pair], x1: -6, x2: 6), 4, 4, 1, 1, 1, 1);
        sim.Start(GfNames.AttackSequence, Attack(mortar, pair, GfNames.Hold));
        Assert.Equal(2, sim.Steps("roll-to-hit").Single().Values!["hit_multiplier"]);                   // 3 capped at 2 models
        Assert.Equal(4, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);                // 2 hits × 2
        Assert.Equal(4, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);

        var render = Unit("Render", "4+", "4+", [], 2, [], Gun("Saw", "12\"", 1, P("Rending")));
        var target = Unit("T", "4+", "2+", [], 3, []);
        var sim2 = Sim.Gf(State([render], [target], x1: -4, x2: 4), 6, 4, 5, 5);
        sim2.Start(GfNames.AttackSequence, Attack(render, target, GfNames.Hold));
        var hit = sim2.Steps("roll-to-hit").Single();
        Assert.Equal(1, hit.Values!["granted_AP_4"]);
        var blocks = sim2.Steps("roll-to-block").Single().Rolls;
        Assert.Equal(2, blocks.Count);
        var rendered = blocks.Single(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -4));
        Assert.Equal((1, false), (rendered.Dice.Count, rendered.Dice[0].Success));                     // 5 − 4 = 1 against 2+: a wound
        Assert.True(blocks.Single(r => r != rendered).Dice[0].Success);                                 // the plain hit: 5 against 2+
    }

    [Fact]
    public void Furious_adds_a_hit_per_unmodified_six_only_when_charging_in_melee()
    {
        var berserkers = Unit("Berserkers", "4+", "4+", [], 2, [P("Furious")], Melee("Axe", 1));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([berserkers], [t], x1: -1, x2: 1), 6, 3, 1, 1, 1);
        sim.Start(GfNames.AttackSequence, Attack(berserkers, t, GfNames.Charge));
        Assert.Equal(1, sim.Steps("roll-to-hit").Single().Values!["extra_hits"]);
        Assert.Equal(2, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);
        var hold = Sim.Gf(State([berserkers], [t], x1: -1, x2: 1), 6, 3, 1, 1);
        hold.Start(GfNames.AttackSequence, Attack(berserkers, t, GfNames.Hold));
        Assert.Equal(0, hold.Steps("roll-to-hit").Single().Values!.GetValueOrDefault("extra_hits"));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Add to `GfAttack.cs`**

```csharp
    public static Sequence AttackSequence() => new(GfNames.AttackSequence, [new DetermineAttacksStep(), new RollToHitStep(), new PushStep("resolve", GfNames.ResolveHits)]);
    public static Sequence ResolveHits() => new(GfNames.ResolveHits, [new RollToBlockStep(), new PushStep("deal", GfNames.DealWounds)]);

    public static AttackKind KindOf(GameState state, IReadOnlyList<Guid> weapons) =>
        state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => weapons.Contains(w.Id))?.Stats.ContainsKey(GfNames.Range) == true ? AttackKind.Shooting : AttackKind.Melee;

    /// <summary>
    /// p.8: models with line of sight to a target model and a weapon in range of it; p.9: within 2" horizontally and 4" vertically.
    /// Containment answers before any of that (p.14): a unit inside a container and a unit outside it cannot target each other at
    /// all, and two units in one container — or an occupant and the container model itself — need no measurement, because there is
    /// no distance and no sight line inside a box. Everything armed is eligible then; only the weapon check survives.
    /// </summary>
    public static IReadOnlyList<Guid> EligibleModels(GameState state, LiveSet live, Guid attacker, Guid target, IReadOnlyList<Guid> weapons, AttackKind kind)
    {
        if (Battlefield.InsideReach(state, attacker, target) is { } reaches)
            return reaches ? Lookup.AliveModels(state, attacker).Where(m => state.Projection.Models[m].Slot.Weapons.Any(w => weapons.Contains(w.Id))).ToList() : [];
        var targets = Lookup.LivingModels(state, target);
        return Lookup.LivingModels(state, attacker).Where(m =>
        {
            var mine = state.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id)).ToList();
            if (mine.Count == 0) return false;
            if (kind == AttackKind.Melee) return targets.Any(t => Battlefield.Distance(state, m, t) <= GfNames.MeleeHorizontal && Battlefield.Vertical(state, m, t) <= GfNames.MeleeVertical);
            var range = mine.Max(w => StatReader.WeaponRange(live, Side.Attacker, w.Id) ?? 0);
            var indirect = live.Has(Side.Attacker, "Indirect");
            return targets.Any(t => Battlefield.Distance(state, m, t) <= range && (indirect || Battlefield.CanSee(state, m, t)));
        }).ToList();
    }

    public static int HitsFrom(FrameView parent)
    {
        var hitRolls = parent.Frame.Steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.Hit).ToList();
        if (hitRolls.Count == 0) return parent.Value("hits");
        return (hitRolls.Sum(Dice.RollOutcome.Successes) + parent.Value("extra_hits")) * Math.Max(1, parent.Value("hit_multiplier", 1));
    }

    static (Interaction Ix, LiveSet Live, AttackKind Kind) Settle(StepContext ctx)
    {
        var c = ctx.Frame.Context;
        var kind = KindOf(ctx.State, c.WeaponIds);
        var ix = AttackInteraction(ctx, c.ActorUnitId, c.TargetUnitIds[0], c.WeaponIds, kind, c.Action);
        return (ix, LiveProperties.Settle(ix), kind);
    }

    /// <summary>Pushes a child once and completes when it has finished. Values pass through the frame stack, not through this step.</summary>
    public sealed class PushStep(string name, string sequence) : IStep
    {
        public string Name => name;
        public StepOutcome Run(StepContext ctx) => ctx.History.Children.Any(ch => ch.Finished || ch.Abandoned) ? new Complete([]) : new Push(sequence, ctx.Frame.Context);
    }

    sealed class DetermineAttacksStep : IStep
    {
        public string Name => "determine-attacks";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            var eligible = EligibleModels(ctx.State, live, ix.Attacker.UnitId, ix.Defender!.UnitId, ctx.Frame.Context.WeaponIds, kind);
            var attacks = eligible.Sum(m => ctx.Projection.Models[m].Slot.Weapons.Where(w => ctx.Frame.Context.WeaponIds.Contains(w.Id)).Sum(w => StatReader.Attacks(ctx.State, w.Id)));
            var values = new Dictionary<string, int> { ["attacks"] = attacks, ["eligible_models"] = eligible.Count };
            return attacks == 0 ? new Finish([], values) : new Complete([], values);
        }
    }

    sealed class RollToHitStep : IStep
    {
        public string Name => "roll-to-hit";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            var record = ctx.Roll(GfRolls.HitPlan(ctx, live, kind), ctx.Frame.Value("attacks"));
            var values = new Dictionary<string, int> { ["extra_hits"] = 0, ["hit_multiplier"] = RollEffects.HitMultiplier(live, Side.Attacker, Lookup.LivingModels(ctx.State, ix.Defender!.UnitId).Count) };
            foreach (var die in record.Dice.Where(d => d.Success))
            {
                var effects = RollEffects.PerDie(live, Side.Attacker, die, GfNames.Hit);
                values["extra_hits"] += effects.ExtraHits;
                foreach (var (entry, amount) in effects.Grants) values[$"granted_{entry}_{amount}"] = values.GetValueOrDefault($"granted_{entry}_{amount}") + 1;
            }
            return new Complete([], values);
        }
    }

    sealed class RollToBlockStep : IStep
    {
        public string Name => "roll-to-block";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            var parent = ctx.Frame.Parent!;
            var hits = HitsFrom(parent);
            if (hits <= 0) return new Finish([], new Dictionary<string, int> { ["wounds"] = 0 });
            var shooter = EligibleModels(ctx.State, live, ix.Attacker.UnitId, ix.Defender!.UnitId, ctx.Frame.Context.WeaponIds, kind).FirstOrDefault();
            var inCover = kind == AttackKind.Shooting && shooter != Guid.Empty && Battlefield.InCover(ctx.State, ix.Defender.UnitId, shooter);
            var multiplier = Math.Max(1, parent.Value("hit_multiplier", 1));
            var wounds = 0; var plain = hits;
            foreach (var (key, count) in parent.Values.Where(kv => kv.Key.StartsWith("granted_")))
            {
                var parts = key.Split('_');   // granted_<entry>_<amount>
                var granted = live.WithGranted(Side.Attacker, parts[1], int.Parse(parts[2]));
                var group = count * multiplier; plain -= group;
                wounds += Dice.RollOutcome.Failures(ctx.Roll(GfRolls.DefensePlan(ctx, granted, inCover), group));
            }
            if (plain > 0) wounds += Dice.RollOutcome.Failures(ctx.Roll(GfRolls.DefensePlan(ctx, live, inCover), plain));
            return new Complete([], new Dictionary<string, int> { ["wounds"] = wounds });
        }
    }
```

`GfRules._sequences` gains `GfAttack.AttackSequence()` and `GfAttack.ResolveHits()`. `deal-wounds` (Task 12) reads the defense rolls back from the parent, so a reroll of a block die between `roll-to-block` and `assign` (wait-between-steps on) changes the wounds asked for.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfAttackTests`; expect 4 passed. Commit: `Add the attack sequence and resolve-hits: determine, hit, block, deal`.**

### Task 14: Shooting, the move step with its casting checkpoint, embarking and disembarking, dangerous terrain, and the four actions

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfShooting.cs`, `server/game/Rules/GrimdarkFuture/GfMoves.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`, `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`PushStep` gains a context factory), `tests/Tabletop.GameServer.Tests/Infrastructure/Armies.cs` (`State` gains `terrain`)
- Test: `tests/Tabletop.GameServer.Tests/GfShootingTests.cs`

**Interfaces:**
- Consumes: Tasks 9, 11–13; geometry plan's `Movement.Check`, `Containment.VolumesContaining`, `Containment.BaseFullyWithin`; `MoveRegion`, `MoveContainment`, `Destinations`, `Inside`, `Assignment`, `Assigned`, `Options`, `Chosen` (Protocol).
- Produces: `GfShooting.Shooting()` (`shooting`: `declare-targets`, `fire-groups`); `static IRL<WeaponGroup> Groups(GameState, PropertySet, Guid unit)` with `sealed record WeaponGroup(string Name, IRL<Guid> WeaponIds, bool Sniper)` (one per ranged weapon type across living models, `Limited` weapons already used this game excluded); `static IRL<Guid> ValidTargets(StepContext, Guid unit, WeaponGroup)` (enemy units with a model still in play, `EligibleModels` deciding — so a unit inside a container is a candidate and is ruled out, or, for a fellow occupant, in); `GfMoves.Hold()`, `Advance()`, `Rush()` (`hold`: `activation-terrain`, `casting-window`, `shoot-or-not`; `advance`: `activation-terrain`, `move`, `shoot-or-not`; `rush`: `activation-terrain`, `move`), `GfMoves.DangerousTerrain()` (`dangerous-terrain`: `test`, `deal`); `sealed class MoveStep(string action, bool ignoreSeparation)`, `sealed class ShootOrNotStep`, `sealed class CastingWindowStep`, `sealed class ActivationTerrainStep`, `sealed class DangerousTestStep`; `static int DangerousDice(GameState, IEnumerable<Guid> models)` (one die per model, or the model's `Tough` value in dice); `static IRL<Guid> StandingInDangerous(GameState, Guid unit)`; `static bool IsContainer(GameState, Guid modelId)`; `static (bool Embarked, bool Disembarked) ContainerMovesThisActivation(GameState, Guid unit)`; `static IRL<Guid> ContainersInReach(StepContext, Guid unit, double remaining, IRL<WorldVolume> impassable)`; `static Placement? ExitPlacement(GameState, Guid modelId, Guid container, Placement proposed, double within)`; `GfAttack.PushStep(string name, string sequence, Func<StepContext, SequenceContext>? context = null)`.
- **p.11 has three triggers for a dangerous terrain test — *moves in*, *moves through*, *is activated in* — and it is a **model's** test, not the unit's.** `activation-terrain` is the first step of all four action sequences and pushes `dangerous-terrain` for the models standing in a `Dangerous` volume when the activation begins; the move step pushes it for the models that ended in one *or* whose path crossed one (`Movement.Check` with the dangerous volumes passed in its `difficult` slot, reading `EntersDifficult` — the same swept test, asked of a different set). Either way the pushing step writes `Values["dangerous_dice"]` and `test` rolls exactly that many, so only the affected models are tested. Transport destruction, the third invoker [action sequences](../specs/2026-09-03-action-sequences-design.md) names, reuses `DangerousTestStep` the same way from Task 19.
- The move step is a phase machine over `Values["phase"]`, one segment per `Complete(Repeat: true)`: phase 0 opens a `MoveRegion` Ask (the unit's models still in play, `allowance − used`, the impassable volume ids, the difficult volume ids, `GfNames.DifficultCap` — `null` for a `Strider`/`Flying` unit, which the cap does not bind — and `MoveStep.Containers(…)`) and, when it closes with `Destinations`, applies them with the server's settled Z (`Battlefield.Settle`), sets the unit's `moved` condition and adds the segment's `distance_used_tenths` (the largest `Movement.Check(...).Distance` over the moved models, in tenths of an inch); phase 1 pushes `dangerous-terrain` when a moved model entered or crossed a `Dangerous` volume (p.11); phase 2 is the casting checkpoint — `Windows.Run(ctx, BeforeAttacking)`, a react-or-pass Ask to the actor's own team when `GfHooks.ReactionsAt` finds a caster with tokens, nothing otherwise. **A finished cast returns the step to phase 0 when budget remains and to phase 2 otherwise, in both cases with a fresh history, so the window reopens: p.13 is *"spend as many tokens as the spell's value to try casting **one or more spells** (only one try per spell)"*, and one window per activation would allow only one** (action sequences → Windows: "checkpoint a move mid-path … then resume the move with its remaining budget"). A `Passed` answer to the move Ask is a zero move. The `distance_used_tenths` value in the test below is the segment total. The 1" separation and coherency are not enforced here (flagged by the client; `architecture.md` → Rules enforcement) — `ignoreSeparation` only changes the Ask's prompt for a charge.
- **Embarking and disembarking are this step's, because p.14 makes them a move**: *"units may enter/exit by using any move action"*. `MoveStep.Containers` decides what the Ask offers, and `Apply` handles the three destination shapes a `Destinations` answer can now carry:
  - **`To is Inside`** — embarking. Nothing is measured and no path is swept: p.14 says *"they ignore the 1" move restriction when embarking (only one model needs to reach the Transport for everyone to embark)"*, so the whole unit may name the container even though `ContainersInReach` found it in reach of a single model, and the segment's `distance_used_tenths` does not grow. The 1" restriction itself is not enforced anywhere in this plan (the client flags it; `architecture.md` → Rules enforcement), so *ignoring* it needs no code — only the Ask's prompt and the client's decision not to flag it. The one guard is state integrity: an `Inside` whose container is not a model on the table is dropped.
  - **`From is Inside`, `To is OnTable`** — disembarking. An occupant has no start position, so there is again no distance and no swept path; the whole constraint is p.14's *"must stay fully within 6" of it when exiting"*, which is `ExitPlacement` over the geometry plan's `Containment.BaseFullyWithin`. It is checked on the server rather than trusted from the client because it is the one containment rule that is geometry; a placement outside the radius is not applied and that model stays inside.
  - **table to table** — unchanged.
- **"Never both in one activation" (p.14) lives in `Containers`**, which reads `ContainerMovesThisActivation` and returns `null` for the exit after an embark and `null` for the enterable list after a disembark. It is the legal set that enforces it, the way every other rule here does — the answer is applied as sent (`architecture.md` → Rules enforcement), and `LegalSet.Contains` under `strict` enforcement checks a `move_region` answer's model ids only (game server plan, Task 8). A **Charge** offers the exit but never the enterable list: p.14 says *"Units may use Charge actions to disembark"*, and charging a transport is an attack on it rather than a way into it.
- **Capacity is never checked.** [Game flow](../specs/2026-08-28-game-flow-design.md) → Inside a container rules it explicitly: *"Capacity is not enforced."* `Transport(X)`'s parameter is carried by the catalog entry and read by nothing; `GfMoves.IsContainer` asks only whether the name is there.
- **`Hold` from inside a container is legal and changes nothing** — the unit stays inside, and `shoot-or-not` finds a target only among the container's other occupants and the container itself (`EligibleModels`, Task 13). A unit inside cannot be *charged*: `charge-target` measures to the target's living models and an occupant has none (Task 16).

- `shoot-or-not`: a Shaken unit spends the activation idle and stops being Shaken — `Finish([ConditionSet(unit, shaken, false)])` (p.10); no valid target → `Complete`; else `Options([shoot, pass])` and `shoot` pushes `shooting`. `declare-targets`: `Assignment(Items = groups, Targets = valid target units, MaxDistinctTargets = 2)` (p.8: "up to two different targets"); `fire-groups` reads the previous step's `Answer` (`Assigned`) from `Frame.Frame.Steps[0].Answer`, pushes one `attack-sequence` per group, `Sniper` groups first (p.14), and completes when the last has finished.

Spec: action sequences → Grimdark Future's sequences (Shooting; the four actions; Dangerous terrain — "invoked by moves through it, activating in it"), Windows (invoked, mid-move); game flow → Inside a container (enter or exit with any move action, never both in one activation, exit within 6", capacity not enforced); whole-design review → Gaps filled (the four OPR actions are sequences); `architecture.md` → How the client stays responsive (parameters, not an answer); rulebook p.14 `Transport(X)`, p.20 solid buildings; rulebook p.7 (Hold/Advance/Rush distances, the difficult-terrain cap on p.11), p.8 (targets declared before rolling; weapon groups; two targets), p.10 (Shaken idle), p.11 (dangerous terrain: a model that moves in or through it, or is activated in it, rolls one die or its Tough value in dice; each 1 is a wound), p.13 (`Caster`: one or more spells, one try per spell).

- [ ] **Step 1: Extend `Armies.State` with `IReadOnlyList<TerrainPlacement>? terrain = null` (placed on the sample table) and write the failing tests**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfShootingTests
{
    static Destinations MoveAll(Sim sim, Tabletop.Content.Unit u, double dx) => new(u.Models.Select(m => sim.Model(m.Id)).Select(m => new ModelRelocation(m.Id, m.Location, new OnTable(new Placement(((OnTable)m.Location).Placement.Position with { X = ((OnTable)m.Location).Placement.Position.X + dx }, new Yaw(0))))).ToList());

    [Fact]
    public void Advance_moves_six_inches_then_offers_shooting_which_declares_groups_and_targets()
    {
        var squad = Unit("Squad", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1), Gun("Sniper Rifle", "30\"", 1, P("Sniper")));
        var a = Unit("A", "4+", "4+", [], 2, []); var b = Unit("B", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([squad], [a, b], x1: -10, x2: 10), 6, 6, 6, 1, 1, 1, 6, 6, 6, 1, 1, 1);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(squad.Id, [], [], GfNames.Advance));
        var move = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((6.0, 3), (move.Allowance, move.ModelIds.Count));
        sim.Answer(Samples.Team1, MoveAll(sim, squad, 2));
        Assert.Contains(GfNames.Moved, sim.Unit(squad.Id).Conditions);
        Assert.Equal(20, sim.Steps("move").First().Values!["distance_used_tenths"]);                           // the segment; the step completes again after the checkpoint
        var shoot = sim.AskFor(Samples.Team1);
        Assert.Equal(["shoot", "pass"], Assert.IsType<Options>(shoot.Legal).Choices.Select(c => c.Id));
        sim.Answer(Samples.Team1, new Chosen("shoot"));
        var declare = Assert.IsType<Assignment>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((["Rifle", "Sniper Rifle"], 2, 2), (declare.Items.Select(i => i.Id).OrderBy(x => x).ToArray(), declare.Targets.Count, declare.MaxDistinctTargets));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = a.Id, ["Sniper Rifle"] = b.Id }));
        var attacks = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).ToList();
        Assert.Equal(b.Id, attacks[0].Context.TargetUnitIds[0]);                                          // Sniper first
        Assert.Equal(GfNames.Advance, attacks[0].Context.Action);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [b.Models[0].Id] = 3 }));       // three sniper wounds (Quality 2+, sixes)
        Assert.Equal(a.Id, sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).Last().Context.TargetUnitIds[0]);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [a.Models[0].Id] = 3 }));
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Fast_and_slow_change_the_allowance_and_rush_never_offers_shooting()
    {
        var fast = Unit("Fast", "4+", "4+", [], 2, [P("Fast")], Gun("Rifle", "24\"", 1));
        var sim = Sim.Gf(State([fast], [Unit("T", "4+", "4+", [], 1, [])], x1: -6, x2: 6), 1);
        sim.Start(GfNames.RushSeq, new SequenceContext(fast.Id, [], [], GfNames.Rush));
        Assert.Equal(16.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        sim.Answer(Samples.Team1, new Passed());
        Assert.Null(sim.AskForOrNull(Samples.Team1)); Assert.Empty(sim.State.Projection.Stack);
        var slow = Unit("Slow", "4+", "4+", [], 2, [P("Slow")], Gun("Rifle", "24\"", 1));
        var sim2 = Sim.Gf(State([slow], [Unit("T", "4+", "4+", [], 1, [])], x1: -6, x2: 6), 1);
        sim2.Start(GfNames.AdvanceSeq, new SequenceContext(slow.Id, [], [], GfNames.Advance));
        Assert.Equal(4.0, Assert.IsType<MoveRegion>(sim2.AskFor(Samples.Team1).Legal).Allowance);
    }

    [Fact]
    public void Hold_with_a_shaken_unit_is_idle_and_clears_shaken_and_hold_without_a_target_asks_nothing()
    {
        var u = Unit("U", "4+", "4+", [], 2, [], Gun("Pistol", "6\"", 1));
        var sim = Sim.Gf(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 1);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, u.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(u.Id).Conditions); Assert.Empty(sim.State.Projection.Stack);
        var sim2 = Sim.Gf(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 1);
        sim2.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        Assert.Null(sim2.AskForOrNull(Samples.Team1)); Assert.Empty(sim2.State.Projection.Stack);           // pistols out of range: nothing to ask
    }

    [Fact]
    public void Moving_into_dangerous_terrain_tests_only_the_models_that_entered_it_and_wounds_on_ones()
    {
        var swamp = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("swamp"), new Point3(-4, -18, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(8, 8), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null))]);
        var pair = Unit("Pair", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([pair], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12, terrain: [swamp]), 1, 3);
        sim.Start(GfNames.RushSeq, new SequenceContext(pair.Id, [], [], GfNames.Rush));
        Assert.Empty(sim.Steps("test"));                                                                   // p.11: nobody was activated in it
        sim.Answer(Samples.Team1, MoveAll(sim, pair, 8));
        var test = sim.Steps("test").Single().Rolls.Single();
        Assert.Equal((2, 1), (test.Dice.Count, test.Dice.Count(d => !d.Success)));                          // both models entered: two dice
        Assert.Equal(1, Assert.IsType<Allocation>(sim.AskFor(Samples.Team1).Legal).Amount);                 // the unit's own team allocates its wound
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [pair.Models[0].Id] = 1 }));
        Assert.Equal(1, sim.Alive(pair.Id));
    }

    [Fact]
    public void A_move_action_embarks_the_whole_unit_when_one_model_reaches_the_transport()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 3, []);
        var sim = Sim.Gf(State([bus, troops], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12), 1);
        // Armies.State puts each unit in its own row 12" away, so park the bus 3" from the nearest trooper.
        var near = ((OnTable)sim.Model(troops.Models[0].Id).Location).Placement.Position;
        sim.Append([new ModelsMoved([new ModelRelocation(bus.Models[0].Id, sim.Model(bus.Models[0].Id).Location,
            new OnTable(new Placement(near with { X = near.X + 3 }, new Yaw(0))))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(troops.Id, [], [], GfNames.Advance));
        var ask = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([bus.Models[0].Id], ask.Containment!.Enterable);                                     // p.14: within 6" of one model, by a clear path
        Assert.Null(ask.Containment.ExitingId);
        Assert.Equal(3, ask.ModelIds.Count);
        sim.Answer(Samples.Team1, new Destinations(troops.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(bus.Models[0].Id))).ToList()));
        Assert.All(troops.Models, m => Assert.Equal(bus.Models[0].Id, Assert.IsType<Inside>(sim.Model(m.Id).Location).ContainerId));
        Assert.Equal(0, sim.Steps("move").First().Values!["distance_used_tenths"]);                        // p.14: embarking measures nothing
        Assert.Empty(Lookup.LivingModels(sim.State, troops.Id));
        Assert.Equal(3, Lookup.AliveModels(sim.State, troops.Id).Count);
    }

    [Fact]
    public void A_unit_inside_disembarks_within_six_inches_and_may_not_embark_again_this_activation()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([bus, troops], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12), 1);
        var container = bus.Models[0].Id;
        sim.Append([new ModelsMoved(troops.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container))).ToList())]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(troops.Id, [], [], GfNames.Advance));
        var ask = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((container, GfNames.ExitWithin), (ask.Containment!.ExitingId!.Value, ask.Containment.ExitWithin!.Value));
        Assert.Empty(ask.Containment.Enterable);                                                          // p.14: never both in one activation
        Assert.Equal(2, ask.ModelIds.Count);                                                              // named although neither has a position
        var seat = ((OnTable)sim.Model(container).Location).Placement.Position;
        Placement Out(double dx) => new(seat with { X = seat.X + dx }, new Yaw(0));
        sim.Answer(Samples.Team1, new Destinations([
            new ModelRelocation(troops.Models[0].Id, new Inside(container), new OnTable(Out(4))),          // inside the radius
            new ModelRelocation(troops.Models[1].Id, new Inside(container), new OnTable(Out(9))),          // outside it: refused
        ]));
        Assert.Equal(seat.X + 4, ((OnTable)sim.Model(troops.Models[0].Id).Location).Placement.Position.X, 6);
        Assert.Equal(container, Assert.IsType<Inside>(sim.Model(troops.Models[1].Id).Location).ContainerId);
        Assert.Contains(GfNames.Moved, sim.Unit(troops.Id).Conditions);
        Assert.Equal(0, sim.Steps("move").First().Values!["distance_used_tenths"]);                        // no start position, so nothing is measured
        Assert.Equal((false, true), GfMoves.ContainerMovesThisActivation(sim.State, troops.Id));      // p.14: it disembarked, so it may not embark again
    }

    [Fact]
    public void One_model_standing_in_dangerous_terrain_tests_at_activation_and_only_that_model()
    {
        var lava = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("lava"), new Point3(-12, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null))]);
        var pair = Unit("Pair", "4+", "4+", [], 2, []);   // model 1 at y −18.5, model 2 at y −17: only the first is inside
        var sim = Sim.Gf(State([pair], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12, terrain: [lava]), 1);
        sim.Start(GfNames.HoldSeq, new SequenceContext(pair.Id, [], [], GfNames.Hold));
        Assert.Single(sim.Steps("test").Single().Rolls.Single().Dice);                                      // p.11: activated in it, one model, one die
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [pair.Models[0].Id] = 1 }));
        Assert.Equal(1, sim.Alive(pair.Id));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `GfShooting.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed record WeaponGroup(string Name, IReadOnlyList<Guid> WeaponIds, bool Sniper);

public static class GfShooting
{
    public static Sequence Shooting() => new(GfNames.Shooting, [new DeclareTargetsStep(), new FireGroupsStep()]);

    /// <summary>One group per ranged weapon type over the living models (p.8). A Limited weapon already fired this game is left out (p.14).</summary>
    public static IReadOnlyList<WeaponGroup> Groups(GameState state, PropertySet props, Guid unit)
    {
        var used = EventApplier.EffectiveLog(state.Applier.Log).Where(e => e.Payload is SequenceStarted).SelectMany(e => ((SequenceStarted)e.Payload).Context.WeaponIds).ToHashSet();
        return Lookup.AliveModels(state, unit).SelectMany(m => state.Projection.Models[m].Slot.Weapons).Where(w => w.Stats.ContainsKey(GfNames.Range))
            .Where(w => !(w.Properties.Any(p => p.Name == "Limited") && used.Contains(w.Id)))
            .GroupBy(w => w.Name).Select(g => new WeaponGroup(g.Key, g.Select(w => w.Id).ToList(), g.First().Properties.Any(p => p.Name == "Sniper"))).ToList();
    }

    public static IReadOnlyList<Guid> ValidTargets(StepContext ctx, Guid unit, WeaponGroup group)
    {
        var team = ctx.TeamOf(unit);
        // The in-play reading: an enemy unit inside a container is still a candidate, and EligibleModels is what rules it out (or, for a fellow occupant, in).
        return ctx.Projection.Units.Values.Where(u => ctx.TeamOf(u.Id) != team && Lookup.AliveModels(ctx.State, u.Id).Count > 0).Select(u => u.Id).Where(target =>
        {
            var live = LiveProperties.Settle(GfAttack.AttackInteraction(ctx, unit, target, group.WeaponIds, AttackKind.Shooting, ctx.Frame.Context.Action));
            return GfAttack.EligibleModels(ctx.State, live, unit, target, group.WeaponIds, AttackKind.Shooting).Count > 0;
        }).ToList();
    }

    sealed class DeclareTargetsStep : IStep
    {
        public string Name => "declare-targets";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var groups = GfShooting.Groups(ctx.State, ctx.Properties, unit).Select(g => (Group: g, Targets: ValidTargets(ctx, unit, g))).Where(x => x.Targets.Count > 0).ToList();
            if (groups.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("declare_targets", "Declare weapon groups and up to two targets", ctx.ActorTeam!.Value,
                new Assignment(groups.Select(x => new AskOption(x.Group.Name, x.Group.Name, x.Group.WeaponIds)).ToList(), groups.SelectMany(x => x.Targets).Distinct().ToList(), 2))]);
        }
    }

    sealed class FireGroupsStep : IStep
    {
        public string Name => "fire-groups";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.Frame.Frame.Steps[0].Answer is not Assigned assigned) return new Complete([]);
            var groups = GfShooting.Groups(ctx.State, ctx.Properties, unit).Where(g => assigned.Targets.ContainsKey(g.Name)).OrderByDescending(g => g.Sniper).ThenBy(g => g.Name).ToList();
            var pushed = ctx.History.Children.Count;
            if (pushed < groups.Count && (pushed == 0 || ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned))
                return new Push(GfNames.AttackSequence, new SequenceContext(unit, [assigned.Targets[groups[pushed].Name]], groups[pushed].WeaponIds, ctx.Frame.Context.Action));
            return new Complete([]);
        }
    }
}
```

- [ ] **Step 3: Write `GfMoves.cs` and widen `Windows.Run`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMoves
{
    public static Sequence Hold() => new(GfNames.HoldSeq, [new ActivationTerrainStep(), new CastingWindowStep(), new ShootOrNotStep()]);
    public static Sequence Advance() => new(GfNames.AdvanceSeq, [new ActivationTerrainStep(), new MoveStep(GfNames.Advance, false), new ShootOrNotStep()]);
    public static Sequence Rush() => new(GfNames.RushSeq, [new ActivationTerrainStep(), new MoveStep(GfNames.Rush, false)]);
    public static Sequence DangerousTerrain() => new(GfNames.DangerousTerrain, [new DangerousTestStep(), new GfAttack.PushStep("deal", GfNames.DealWounds, ctx => new SequenceContext(null, [ctx.Frame.Context.ActorUnitId!.Value], [], null))]);

    static LiveSet Lone(StepContext ctx, Guid unit, string? action) => LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, null, action, null, Battlefield.TerrainUnder(ctx.State, unit)));

    static IReadOnlyList<WorldVolume> Declared(IReadOnlyList<WorldVolume> terrain, TerrainDeclarations flag) => terrain.Where(v => v.Volume.Declarations.HasFlag(flag)).ToList();

    /// <summary>p.11: one die per model, or the model's Tough value in dice.</summary>
    public static int DangerousDice(GameState state, IEnumerable<Guid> models) => models.Sum(m => Lookup.ModelParameter(state, m, "Tough") ?? 1);

    /// <summary>
    /// p.14: "units can't both embark/disembark as part of the same activation". Read from the log, bounded by the activation the
    /// way GfHooks.FoughtMeleeThisActivation is — every relocation into or out of a container is a models_moved, so this is a scan,
    /// never a flag. A hand move from the menu counts, which is what a player doing it by hand should expect.
    /// </summary>
    public static (bool Embarked, bool Disembarked) ContainerMovesThisActivation(GameState state, Guid unit)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var activation = log.FindLastIndex(e => e.Payload is SequenceStarted s
            && s.Context.Action is GfNames.Hold or GfNames.Advance or GfNames.Rush or GfNames.Charge && s.Context.ActorUnitId == unit);
        if (activation < 0) return (false, false);
        var mine = Lookup.AliveModels(state, unit).ToHashSet();
        var moves = log.Skip(activation).Select(e => e.Payload).OfType<ModelsMoved>().SelectMany(m => m.Moves).Where(r => mine.Contains(r.ModelId)).ToList();
        return (moves.Any(r => r.To is Inside), moves.Any(r => r.From is Inside));
    }

    /// <summary>
    /// p.14: the containers a model of this unit could reach — a friendly model with Transport(X) on the table, within the move's
    /// remaining allowance of one of the unit's models, by a straight path crossing no impassable volume (the same reading as
    /// charge-target's). Only one model has to reach it, so the answer is per container, not per model. Capacity is not checked:
    /// game flow rules it unenforced and Transport's (X) is carried unread.
    /// </summary>
    /// <summary>p.14: a model that can hold others — `Transport` is on it. The (X) is carried and never read; game flow rules capacity unenforced. The name lives here because `Effects/` may not name an OPR rule (constraint 1, Task 18).</summary>
    public static bool IsContainer(GameState state, Guid modelId) => Lookup.ModelParameter(state, modelId, GfNames.Transport) is not null;

    public static IReadOnlyList<Guid> ContainersInReach(StepContext ctx, Guid unit, double remaining, IReadOnlyList<WorldVolume> impassable)
    {
        var team = ctx.TeamOf(unit);
        var movers = Lookup.LivingModels(ctx.State, unit);
        if (movers.Count == 0) return [];
        return ctx.Projection.Models.Values.Where(c => c.Location is OnTable && c.UnitId != unit && IsContainer(ctx.State, c.Id) && ctx.TeamOf(c.UnitId) == team)
            .Where(c => movers.Any(m => Battlefield.Distance(ctx.State, m, c.Id) <= remaining
                && !Movement.Check(Battlefield.Placed(ctx.State, m)!.Value, Battlefield.Placed(ctx.State, c.Id)!.Value.Placement.Position.Horizontal,
                    remaining, impassable, [], null, ctx.State.Board.Table.Size).CrossesImpassable))
            .Select(c => c.Id).ToList();
    }

    /// <summary>
    /// p.14 and p.20: "must stay fully within 6" of it when exiting". The settled placement when the whole base is inside the
    /// radius of the container's base, null otherwise — the one containment constraint that is geometry, so it is checked on the
    /// server rather than trusted from the client, and a model whose placement is refused simply stays inside.
    /// </summary>
    public static Placement? ExitPlacement(GameState state, Guid modelId, Guid container, Placement proposed, double within)
    {
        var settled = Battlefield.Settle(state, proposed);
        return Battlefield.Placed(state, container) is { } c && Containment.BaseFullyWithin(Battlefield.At(state, modelId, settled), c, within) ? settled : null;
    }

    /// <summary>p.11 "is activated in it": the unit's living models whose base centre is inside a Dangerous volume right now.</summary>
    public static IReadOnlyList<Guid> StandingInDangerous(GameState state, Guid unit)
    {
        var dangerous = Declared(Battlefield.Terrain(state), TerrainDeclarations.Dangerous);
        return dangerous.Count == 0 ? [] : Lookup.LivingModels(state, unit)
            .Where(m => Containment.VolumesContaining(Battlefield.Placed(state, m)!.Value, dangerous).Count > 0).ToList();
    }

    /// <summary>p.11's third trigger, the first step of every action sequence: a model activated in dangerous terrain tests before it does anything else.</summary>
    public sealed class ActivationTerrainStep : IStep
    {
        public string Name => "activation-terrain";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.Children.Count > 0) return new Complete([]);                               // the test has been run
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (Lone(ctx, unit, ctx.Frame.Context.Action).Has(Side.Attacker, "Flying")) return new Complete([]);   // p.14: ignores terrain effects
            var standing = StandingInDangerous(ctx.State, unit);
            if (standing.Count == 0) return new Complete([]);
            // Write the dice count first, then push: the child reads dangerous_dice off this frame, so it must be settled before the push.
            return ctx.Frame.Value("dangerous_dice") > 0
                ? new Push(GfNames.DangerousTerrain, ctx.Frame.Context)
                : new Complete([], new Dictionary<string, int> { ["dangerous_dice"] = DangerousDice(ctx.State, standing) }, Repeat: true);
        }
    }

    /// <summary>The casting checkpoint on its own: Hold has no move to hang it on. p.13 allows more than one spell, so a finished cast reopens the window with a fresh history.</summary>
    public sealed class CastingWindowStep : IStep
    {
        public string Name => "casting-window";
        public StepOutcome Run(StepContext ctx) =>
            Windows.Run(ctx, GfNames.BeforeAttacking) ?? new Complete([], null, Repeat: ctx.History.Children.Any(c => c.Sequence == GfNames.CastingSeq));
    }

    /// <summary>
    /// One segment per run of its phase machine, carried in Values["phase"]: 0 ask for a move → apply it (Repeat); 1 dangerous terrain test (Push) → (Repeat);
    /// 2 the casting checkpoint: react-or-pass to the actor's own team; a cast with budget left goes back to phase 0 (Repeat), otherwise the step completes.
    /// </summary>
    public sealed class MoveStep(string action, bool ignoreSeparation) : IStep
    {
        public string Name => "move";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var live = Lone(ctx, unit, action);
            var allowance = StatReader.ActionDistance(live, Side.Attacker, action);
            var used = ctx.Frame.Value("distance_used_tenths") / 10.0;
            var terrain = Battlefield.Terrain(ctx.State);
            switch (ctx.Frame.Value("phase"))
            {
                case 0 when ctx.History.LastClosed("move") is not { } moved:
                    var impassable = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).Select(v => v.Id).ToList();
                    var difficult = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Difficult)).Select(v => v.Id).ToList();
                    var strider = live.Has(Side.Attacker, "Strider") || live.Has(Side.Attacker, "Flying");
                    var containment = Containers(ctx, unit, action, Math.Max(0, allowance - used), Declared(terrain, TerrainDeclarations.Impassable));
                    var prompt = containment?.ExitingId is not null ? $"Disembark: place every model fully within {GfNames.ExitWithin:0.#}\" of the transport"
                        : ignoreSeparation ? "Charge: move into contact" : $"Move up to {allowance - used:0.#}\"";
                    return new OpenAsks([ctx.Ask("move", prompt, ctx.ActorTeam!.Value,
                        new MoveRegion(Lookup.AliveModels(ctx.State, unit), Math.Max(0, allowance - used), impassable, difficult, strider ? null : GfNames.DifficultCap, containment), allowPass: true)]);
                case 0:
                {
                    var (changes, segment, dangerousDice) = Apply(ctx, unit, live, allowance, ctx.History.LastClosed("move")!.Single as Destinations, terrain);
                    return new Complete(changes, new Dictionary<string, int>
                        { ["distance_used_tenths"] = ctx.Frame.Value("distance_used_tenths") + segment, ["dangerous_dice"] = dangerousDice, ["phase"] = dangerousDice > 0 ? 1 : 2 }, Repeat: true);
                }
                case 1 when ctx.History.Children.Count == 0: return new Push(GfNames.DangerousTerrain, new SequenceContext(unit, [], [], action));
                case 1: return new Complete([], new Dictionary<string, int> { ["phase"] = 2 }, Repeat: true);
                default:
                    if (Windows.Run(ctx, GfNames.BeforeAttacking) is { } window) return window;
                    // p.13: a caster may try more than one spell in an activation, so a finished cast reopens the window — back to the
                    // move when budget remains, otherwise round again on the checkpoint. A pass leaves no casting child and the step ends.
                    var cast = ctx.History.Children.Any(c => c.Sequence == GfNames.CastingSeq);
                    return new Complete([], new Dictionary<string, int> { ["phase"] = allowance - used > 0 ? 0 : 2 }, Repeat: cast);
            }
        }

        /// <summary>
        /// p.14: what containment this segment allows. Inside a container → the exit, unless the unit embarked earlier in this
        /// activation. Outside → the containers in reach, unless it disembarked earlier in this activation, and never on a Charge —
        /// p.14 lets a Charge disembark, and charging a transport is an attack on it, not a way into it. Null when containment plays
        /// no part in this move, which is every move on open table. This is where "never both in one activation" is enforced.
        /// </summary>
        static MoveContainment? Containers(StepContext ctx, Guid unit, string action, double remaining, IReadOnlyList<WorldVolume> impassable)
        {
            var (embarked, disembarked) = ContainerMovesThisActivation(ctx.State, unit);
            if (Lookup.ContainerOf(ctx.State, unit) is { } inside)
                return embarked ? null : new MoveContainment([], inside, GfNames.ExitWithin);
            if (disembarked || action == GfNames.Charge) return null;
            var enterable = ContainersInReach(ctx, unit, remaining, impassable);
            return enterable.Count > 0 ? new MoveContainment(enterable, null, null) : null;
        }
        // Allowance, DifficultVolumeIds and DifficultCap are unbound whenever Containment.ExitingId is set:
        // an occupant has no position, so nothing is swept and no distance is measured from a start. The
        // region is the container's base grown by ExitWithin. The three are still populated from the action
        // so the record has one shape, and the client ignores them on a disembark (client shell -> MoveRuler).

        /// <summary>The destinations settled onto the surface, the moved condition, the segment's distance in tenths, and the dangerous-terrain dice the models that moved in or through a Dangerous volume owe (p.11).</summary>
        static (List<StateChange> Changes, int SegmentTenths, int DangerousDice) Apply(StepContext ctx, Guid unit, LiveSet live, double allowance, Destinations? d, IReadOnlyList<WorldVolume> terrain)
        {
            var changes = new List<StateChange>(); var used = 0.0; var moves = new List<ModelRelocation>(); var exposed = new List<Guid>();
            var impassable = Declared(terrain, TerrainDeclarations.Impassable);
            var difficult = Declared(terrain, TerrainDeclarations.Difficult);
            var dangerous = Declared(terrain, TerrainDeclarations.Dangerous);
            var strider = live.Has(Side.Attacker, "Strider") || live.Has(Side.Attacker, "Flying");   // p.14: Strider ignores difficult terrain, Flying all terrain effects
            var flying = live.Has(Side.Attacker, "Flying");                                          // Strider does *not* skip a dangerous terrain test; only Flying does
            foreach (var m in d?.Moves ?? [])
            {
                // p.14: embarking costs no measured distance and sweeps no path — "only one model needs to reach the Transport for
                // everyone to embark", and the 1" restriction is ignored (it is client-flagged here anyway; architecture.md → Rules enforcement).
                if (m.To is Inside into)
                {
                    if (ctx.Projection.Models[into.ContainerId].Location is OnTable) moves.Add(m);          // never point Inside at something that is not on the table
                    continue;
                }
                if (m.To is not OnTable to) continue;
                // p.14: a disembarking model has no start position, so nothing is measured or swept; the radius is the whole constraint.
                if (m.From is Inside from)
                {
                    if (ExitPlacement(ctx.State, m.ModelId, from.ContainerId, to.Placement, GfNames.ExitWithin) is { } exit)
                        moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(exit)));
                    continue;
                }
                if (Battlefield.Placed(ctx.State, m.ModelId) is not { } start) continue;
                var settled = Battlefield.Settle(ctx.State, to.Placement);
                used = Math.Max(used, Movement.Check(start, settled.Position.Horizontal, allowance, impassable, difficult, strider ? null : GfNames.DifficultCap, ctx.State.Board.Table.Size).Distance);
                // p.11 is "moves in or through", which the landing position alone does not answer (geometry plan → VolumesCrossed is the in-or-through test).
                if (Movement.VolumesCrossed(start, settled.Position.Horizontal, dangerous).Count > 0) exposed.Add(m.ModelId);
                moves.Add(new ModelRelocation(m.ModelId, m.From, new OnTable(settled)));
            }
            if (moves.Count > 0) { changes.Add(new ModelsMoved(moves)); changes.Add(new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Moved, true)); }
            return (changes, (int)Math.Round(used * 10), flying ? 0 : DangerousDice(ctx.State, exposed));
        }
    }
```


    /// <summary>p.7–8: Hold and Advance may shoot. p.10: a Shaken unit spends the activation idle and stops being Shaken.</summary>
    public sealed class ShootOrNotStep : IStep
    {
        public string Name => "shoot-or-not";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.Projection.Units[unit].Conditions.Contains(GfNames.Shaken)) return new Finish([new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, false)]);
            if (ctx.History.Children.Count > 0) return ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete([]) : new OpenAsks([]);
            if (ctx.History.LastClosed() is { } closed) return closed.Single is Chosen { OptionId: "shoot" } ? new Push(GfNames.Shooting, ctx.Frame.Context) : new Complete([]);
            var canShoot = GfShooting.Groups(ctx.State, ctx.Properties, unit).Any(g => GfShooting.ValidTargets(ctx, unit, g).Count > 0);
            if (!canShoot) return new Complete([]);
            return new OpenAsks([ctx.Ask("shoot_or_not", "Shoot?", ctx.ActorTeam!.Value, new Options([new AskOption("shoot", "Shoot", [unit]), new AskOption("pass", "Don't shoot", [])]))]);
        }
    }

    /// <summary>p.11: one die per *affected* model (or that model's Tough value in dice); each 1 is a wound. The pushing step settled how many in Values["dangerous_dice"]; the fallback is the models standing in it now. Values["wounds"] feeds deal-wounds with no attacker.</summary>
    public sealed class DangerousTestStep : IStep
    {
        public string Name => "test";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var owed = ctx.Frame.Parent?.Value("dangerous_dice") ?? 0;
            var dice = owed > 0 ? owed : DangerousDice(ctx.State, StandingInDangerous(ctx.State, unit));
            if (dice == 0) return new Finish([], new Dictionary<string, int> { ["wounds"] = 0 });
            var record = ctx.Roll(new Dice.RollPlan(GfNames.DangerousTerrain, 2, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), dice);
            var wounds = Dice.RollOutcome.Failures(record);
            return wounds == 0 ? new Finish([], new Dictionary<string, int> { ["wounds"] = 0 }) : new Complete([], new Dictionary<string, int> { ["wounds"] = wounds });
        }
    }
}
```

`GfAttack.PushStep` gains an optional `Func<StepContext, SequenceContext>? context` and pushes `context?.Invoke(ctx) ?? ctx.Frame.Context`. `GfRules._sequences` gains `GfShooting.Shooting()`, `GfMoves.Hold()`, `GfMoves.Advance()`, `GfMoves.Rush()`, `GfMoves.DangerousTerrain()`. `Windows.Run` is unchanged: a repeating step's history is only its current segment, which is what both the checkpoint and the second spell need. `Sniper`'s per-model pick (p.14) is not modelled — a Sniper group resolves first at Quality 2+ against the unit; question 7.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfShootingTests`; expect 7 passed. Commit: `Add Hold, Advance and Rush, shooting with weapon groups, the casting checkpoint, dangerous terrain, and embarking and disembarking as part of a move`.**

### Task 15: The morale test

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfMorale.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`
- Test: `tests/Tabletop.GameServer.Tests/GfMoraleTests.cs`

**Interfaces:**
- Consumes: Tasks 7, 11, 12.
- Produces: `GfMorale.MoraleTest()` (`morale-test`, one step `test`). Context: `ActorUnitId` = the unit testing, `Action` = `"wounds"` (end of activation) or `"melee"` (lost a melee). Values: `passed` (0/1), `routed` (0/1).
- p.10: one Quality test; a Shaken unit always fails (no die is rolled); `Fearless` (majority) on a failure rolls the property's own roll (`Fearless`, threshold from the effect row) and a success counts as passed. A failed wounds test → Shaken. A failed melee test → Rout when at half or less of starting size (or Tough value, single model) — every model to the shelf — otherwise Shaken. A unit with no living models finishes without testing.

Spec: action sequences → Grimdark Future's sequences (Morale test; removal only on the melee path); rulebook p.10 (both morale paragraphs), p.13 `Fearless`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfMoraleTests
{
    static Sim WithLosses(Tabletop.Content.Unit u, int dead, params int[] dice)
    {
        var sim = Sim.Gf(State([Unit("A", "4+", "4+", [], 1, [])], [u]), dice);
        sim.Append(u.Models.Take(dead).Select(m => (EventPayload)new ModelsMoved([new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnShelf())])));
        return sim;
    }

    [Fact]
    public void A_failed_wounds_test_shakes_and_a_passed_one_does_nothing()
    {
        var u = Unit("U", "4+", "4+", [], 4, []);
        var sim = WithLosses(u, 2, 3);
        sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        var step = sim.Steps("test").Single();
        Assert.Equal((GfNames.Morale, 4, false), (step.Rolls[0].Roll, step.Rolls[0].Threshold, step.Rolls[0].Dice[0].Success));
        Assert.Contains(GfNames.Shaken, sim.Unit(u.Id).Conditions);
        Assert.Equal(2, sim.Alive(u.Id));
        var ok = WithLosses(u, 2, 5);
        ok.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        Assert.DoesNotContain(GfNames.Shaken, ok.Unit(u.Id).Conditions);
        Assert.Equal(1, ok.Steps("test").Single().Values!["passed"]);
    }

    [Fact]
    public void A_failed_melee_test_at_half_or_less_routs_and_above_half_shakes()
    {
        var u = Unit("U", "4+", "4+", [], 4, []);
        var routed = WithLosses(u, 2, 1);
        routed.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "melee"));
        Assert.Equal(0, routed.Alive(u.Id)); Assert.Equal(1, routed.Steps("test").Single().Values!["routed"]);
        var shaken = WithLosses(u, 1, 1);
        shaken.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "melee"));
        Assert.Equal(3, shaken.Alive(u.Id)); Assert.Contains(GfNames.Shaken, shaken.Unit(u.Id).Conditions);
    }

    [Fact]
    public void Shaken_always_fails_and_fearless_rerolls_a_failure_at_four_plus()
    {
        var u = Unit("U", "4+", "4+", [], 3, [P("Fearless")]);
        var sim = WithLosses(u, 1, 1, 4);
        sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        var step = sim.Steps("test").Single();
        Assert.Equal([GfNames.Morale, "Fearless"], step.Rolls.Select(r => r.Roll));
        Assert.Equal(4, step.Rolls[1].Threshold);
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(u.Id).Conditions);
        var already = WithLosses(Unit("S", "4+", "4+", [], 2, []), 0, 6);
        var s = already.State.Board.Armies[1].Snapshot.Units[0].Id;
        already.Append([new ConditionSet(new Subject(SubjectKind.Unit, s), GfNames.Shaken, true)]);
        already.Start(GfNames.MoraleTest, new SequenceContext(s, [], [], "melee"));
        Assert.Empty(already.Steps("test").Single().Rolls);                                     // no die: Shaken always fails
        Assert.Equal(2, already.Alive(s));                                                     // above half: a failed melee test shakes, and it already is
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `GfMorale.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMorale
{
    public static Sequence MoraleTest() => new(GfNames.MoraleTest, [new TestStep()]);

    sealed class TestStep : IStep
    {
        public string Name => "test";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var living = Lookup.AliveModels(ctx.State, unit);                                     // in play, not positioned: a unit carried in a transport still takes its test
            if (living.Count == 0) return new Finish([]);
            var live = LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, null, ctx.Frame.Context.Action, null, Battlefield.TerrainUnder(ctx.State, unit)));
            var u = ctx.Projection.Units[unit];
            var passed = false;
            if (!u.Conditions.Contains(GfNames.Shaken))
            {
                passed = Dice.RollOutcome.Successes(ctx.Roll(GfRolls.MoralePlan(ctx, live), 1)) == 1;
                if (!passed)
                    foreach (var (source, roll) in RollEffects.OwnRolls(live, Side.Attacker, c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.MoraleTest }))
                        passed |= Dice.RollOutcome.Successes(ctx.Roll(new Dice.RollPlan(source.Entry.Name, roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Morale).Convention, null, new HashSet<int>()), roll.EffectiveAmount(source.Parameter))) > 0;
            }
            var values = new Dictionary<string, int> { ["passed"] = passed ? 1 : 0, ["routed"] = 0 };
            if (passed) return new Complete([], values);
            if (ctx.Frame.Context.Action == "melee" && GfHooks.AtHalfOrLess(ctx.State, ctx.Properties, unit))
            {
                values["routed"] = 1;
                return new Complete([new ModelsMoved(living.Select(m => new ModelRelocation(m, ctx.Projection.Models[m].Location, new OnShelf())).ToList())], values);
            }
            return new Complete([new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, true)], values);
        }
    }
}
```

`GfRules._sequences` gains `GfMorale.MoraleTest()`.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfMoraleTests`; expect 3 passed. Commit: `Add the morale test: Shaken, Rout on the melee path, Fearless`.**

### Task 16: Charge and melee

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfMelee.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`
- Test: `tests/Tabletop.GameServer.Tests/GfMeleeTests.cs`

**Interfaces:**
- Consumes: Tasks 11–15; `PieceSelection`, `SelectedPieces` (Protocol).
- Produces: `GfMelee.Charge()` (`charge`: `activation-terrain` (`GfMoves.ActivationTerrainStep`, p.11's *activated in it*, Task 14), `charge-target`, `move` (`MoveStep(Charge, ignoreSeparation: true)`), `defender-closes`, `fight`), `GfMelee.Melee()` (`melee`: `melee-start`, `counter`, `impact`, `charger-attacks`, `strike-back`, `compare`, `consolidate`); `static IRL<Guid> MeleeWeapons(GameState, Guid unit, Func<Weapon,bool>? filter = null)`; `static int WoundsCaused(StepContext, Guid by, Guid to, string beforeKey, bool attacked)` (the target's `WoundsThisActivation` less the value recorded at `melee-start`, plus `Fear(X)` summed over the attacker's models when it attacked).
- `charge-target`: `PieceSelection(min 1, max 1)` over enemy units with a model within charge distance (`StatReader.ActionDistance(Charge)`, `Fast`/`Slow` included) of a charger model whose straight path crosses no impassable volume (`Movement.Check(...).CrossesImpassable`); none → `Finish` (p.7: a charge needs a target in distance). **A charger inside a container measures from the container instead** — p.14 says *"Units may use Charge actions to disembark"*, and an occupant has no position of its own, so its reach is `GfNames.ExitWithin` from the container it is inside: an enemy within that is one it could be placed fully within 6" of and then be in contact with. The step's own `move` (`MoveStep(Charge, ignoreSeparation: true)`) is then the disembark, since `MoveStep.Containers` offers the exit on a Charge and never the enterable list (Task 14). The mirror also falls out: **a unit inside a container cannot be charged**, because the predicate measures to the target's *living* models and an occupant has none — which is p.14's no line of sight in. `defender-closes`: `MoveRegion(3")` to the defender's team for its models not in base contact (`Distance.EdgeToEdge > 0`) — `Passed` moves nobody. `fight`: no charger model in melee reach → `Finish` (p.7: "the unit may complete its move in any direction and ends its activation"); else `Push(melee, (charger, [target], charger's melee weapons, Charge))`.
- `melee`: `melee-start` records both units' `WoundsThisActivation` (`charger_before`, `defender_before`); `counter` pushes an `attack-sequence` for the defender's `Counter` weapons before anything else (the 09-03 ruling); `impact` — charger models with `Impact(X)`, unless fatigued: `Σ X − (defender models in reach with a Counter weapon)` dice on the effect's threshold, recorded as roll `Impact`, `Values["hits"]`, then `Push(resolve-hits)` when hits > 0; `charger-attacks` pushes the charger's `attack-sequence` and, when it finishes, sets `fatigued` on the charger; `strike-back` — a living defender is asked `Options([strike, pass])`; `strike` pushes the defender's `attack-sequence` (its `Action` null: fatigue and `Furious` read the conditions, not the charge) and then sets `fatigued` on the defender; `compare` — wounds caused each way (`Fear` counting for a unit that attacked), a destroyed unit always loses, the loser pushes `morale-test` with `Action = "melee"`, a tie tests nobody; `consolidate` — one unit destroyed or routed → the other's team gets `MoveRegion(3")`; otherwise the charger's team gets `MoveRegion(1")` to fall back (p.10).

Spec: action sequences → Grimdark Future's sequences (Charge, melee's seven bullets), Rulings (`Counter` before `Impact`); rulebook p.7 (charge distance, path), p.9 (melee reach, striking back, fatigue), p.10 (who won, consolidation), p.13 `Counter`, `Fear`, p.14 `Impact`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfMeleeTests
{
    static Destinations Into(Sim sim, Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Models.Select((m, i) => new ModelRelocation(m.Id, sim.Model(m.Id).Location,
            new OnTable(new Placement(((OnTable)sim.Model(target.Models[Math.Min(i, target.Models.Count - 1)].Id).Location).Placement.Position with { X = ((OnTable)sim.Model(target.Models[0].Id).Location).Placement.Position.X - 1.3 }, new Yaw(0))))).ToList());

    [Fact]
    public void Pages_9_and_10_warriors_charge_dwarves_who_lose_the_melee_and_are_shaken()
    {
        var warriors = Unit("Dynasty Warriors", "4+", "4+", [], 3, [], Melee("CCW", 1));
        var dwarves = Unit("Dwarves", "4+", "4+", [], 4, [], Melee("Hammer", 1));
        // The charger's half is page 9's worked example verbatim — 3 attacks at Quality 4+ rolling 3,4,5 for two hits, Defense 4+ rolling 3,4 for one wound.
        // The rest is constructed. In draw order: charger hits 3,4,5; blocks 3,4; strike-back hits 6,1,1 (one hit); block 5 (blocked); morale 2 (failed).
        var sim = Sim.Gf(State([warriors], [dwarves], x1: -8, x2: 0), 3, 4, 5, 3, 4, 6, 1, 1, 5, 2);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(warriors.Id, [], [], GfNames.Charge));
        var target = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([dwarves.Id], target.Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([dwarves.Id]));
        Assert.Equal(12.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        sim.Answer(Samples.Team1, Into(sim, warriors, dwarves));
        sim.Answer(Samples.Team2, new Passed());                                                    // no caster, so no checkpoint; the defender declines its 3" close
        Assert.Contains(sim.State.Projection.Stack, f => f.Sequence == GfNames.Melee);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [dwarves.Models[0].Id] = 1 }));   // one dwarf falls
        Assert.Contains(GfNames.Fatigued, sim.Unit(warriors.Id).Conditions);
        Assert.Equal(["strike", "pass"], Assert.IsType<Options>(sim.AskFor(Samples.Team2).Legal).Choices.Select(c => c.Id));
        sim.Answer(Samples.Team2, new Chosen("strike"));                                            // one hit, blocked: no wound to allocate
        var compare = sim.Steps("compare").Single();
        Assert.Equal((1, 0, 2), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));
        Assert.Contains(GfNames.Shaken, sim.Unit(dwarves.Id).Conditions);                           // 3 of 4 alive: Shaken, not routed
        Assert.Equal(1.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);      // nobody destroyed: the charger falls back 1"
        sim.Answer(Samples.Team1, new Passed());
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Counter_strikes_before_impact_and_reduces_impact_dice_and_fatigue_leaves_only_sixes()
    {
        var riders = Unit("Riders", "4+", "4+", [], 2, [P("Impact", 2)], Melee("Lance", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")));
        var sim = Sim.Gf(State([riders], [pikes], x1: -3, x2: 0), 6, 1, 1, 5, 3, 1, 1);           // pike hits 6, rider blocks 1; Impact 1,5,3; pike blocks 1,1
        sim.Start(GfNames.ChargeSeq, new SequenceContext(riders.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([pikes.Id]));
        sim.Answer(Samples.Team1, Into(sim, riders, pikes));
        sim.Answer(Samples.Team2, new Passed());
        var started = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).ToList();
        Assert.Equal(pikes.Id, started[0].Context.ActorUnitId);                                      // Counter first
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [riders.Models[0].Id] = 1 }));  // the pike's hit (6) got through (block 1)
        var impact = sim.Steps("impact").First().Rolls.Single();                                     // the step completes again after pushing resolve-hits
        Assert.Equal(("Impact", 2, 3), (impact.Roll, impact.Threshold, impact.Dice.Count));         // 2 × Impact(2) = 4, −1 for the Counter model
        Assert.Equal(2, sim.Steps("impact").First().Values!["hits"]);                               // 1,5,3 → two at 2+
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [pikes.Models[0].Id] = 2 }));   // both hits wound (1,1): the pike dies
        Assert.Equal(0, sim.Alive(pikes.Id));
        Assert.Equal(2, sim.Steps("compare").Single().Values!["loser"]);                            // a destroyed unit always loses; nobody left to test
        Assert.Equal(3.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);    // consolidate 3"
        sim.Answer(Samples.Team1, new Passed());
        Assert.Contains(GfNames.Fatigued, sim.Unit(riders.Id).Conditions);
        // A second melee this round: fatigued, the riders hit only on unmodified 6s.
        var again = Unit("Again", "4+", "4+", [], 2, [], Melee("Club", 1));
        var sim2 = Sim.Gf(State([riders], [again], x1: -3, x2: 0), 5, 6, 1, 1, 1);
        sim2.Append([new ConditionSet(new Subject(SubjectKind.Unit, riders.Id), GfNames.Fatigued, true)]);
        sim2.Start(GfNames.Melee, new SequenceContext(riders.Id, [again.Id], riders.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        var hit = sim2.Steps("roll-to-hit").First().Rolls.Single();
        Assert.Equal((6, [false, true]), (hit.Threshold, hit.Dice.Select(d => d.Success).ToArray()));
        Assert.Empty(sim2.Steps("impact").Single().Rolls);                                          // Impact does not roll when fatigued
    }

    [Fact]
    public void No_target_in_charge_distance_ends_the_activation()
    {
        var u = Unit("U", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 1);
        // sequence_started, then activation-terrain (no dangerous terrain), then charge-target with nothing in range: Finish.
        Assert.Equal(["step_completed", "step_completed", "sequence_finished"], sim.Start(GfNames.ChargeSeq, new SequenceContext(u.Id, [], [], GfNames.Charge)).Skip(1).Select(EventKinds.Of));
    }

    [Fact]
    public void A_charge_disembarks_measuring_from_the_transport_and_a_unit_inside_one_cannot_be_charged()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var riders = Unit("Riders", "4+", "4+", [], 1, [], Melee("Axe", 1));
        var enemy = Unit("Enemy", "4+", "4+", [], 1, [], Melee("Club", 1));
        var truck = Unit("Truck", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var hidden = Unit("Hidden", "4+", "4+", [], 1, [], Melee("Knife", 1));
        // Armies.State rows: team1's bus and riders and team2's enemy, truck and hidden each 12" apart in y; the enemy is the
        // only enemy model within 6" of the bus, and the truck's row is 12" away.
        var sim = Sim.Gf(State([bus, riders], [enemy, truck, hidden], x1: -4, x2: 0), 1, 1, 1, 1);
        var container = bus.Models[0].Id;
        sim.Append([new ModelsMoved([
            new ModelRelocation(riders.Models[0].Id, sim.Model(riders.Models[0].Id).Location, new Inside(container)),
            new ModelRelocation(hidden.Models[0].Id, sim.Model(hidden.Models[0].Id).Location, new Inside(truck.Models[0].Id))])]);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(riders.Id, [], [], GfNames.Charge));
        // p.14: the reach is 6" from the bus, and a unit inside a container of its own is not a target at all.
        var pick = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([enemy.Id], pick.Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([enemy.Id]));
        var move = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((container, GfNames.ExitWithin), (move.Containment!.ExitingId!.Value, move.Containment.ExitWithin!.Value));
        Assert.Empty(move.Containment.Enterable);                                                   // p.14: a Charge disembarks; it never embarks
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `GfMelee.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMelee
{
    public static Sequence Charge() => new(GfNames.ChargeSeq, [new GfMoves.ActivationTerrainStep(), new ChargeTargetStep(), new GfMoves.MoveStep(GfNames.Charge, ignoreSeparation: true), new DefenderClosesStep(), new FightStep()]);
    public static Sequence Melee() => new(GfNames.Melee, [new MeleeStartStep(), new CounterStep(), new ImpactStep(), new ChargerAttacksStep(), new StrikeBackStep(), new CompareStep(), new ConsolidateStep()]);

    public static IReadOnlyList<Guid> MeleeWeapons(GameState state, Guid unit, Func<Weapon, bool>? filter = null) =>
        Lookup.LivingModels(state, unit).SelectMany(m => state.Projection.Models[m].Slot.Weapons).Where(w => !w.Stats.ContainsKey(GfNames.Range) && (filter?.Invoke(w) ?? true)).Select(w => w.Id).ToList();

    static Guid Charger(StepContext ctx) => ctx.Frame.Context.ActorUnitId!.Value;
    static Guid Defender(StepContext ctx) => ctx.Frame.Context.TargetUnitIds[0];
    /// <summary>By step name, not by index: charge's first step is the activation's dangerous terrain test (p.11).</summary>
    static Guid Target(StepContext ctx) => ((SelectedPieces)ctx.Frame.Frame.Steps.Last(s => s.Step == "charge-target").Answer!).PieceIds[0];
    static bool InReach(GameState state, Guid a, Guid b) => Lookup.LivingModels(state, a).Any(m => Lookup.LivingModels(state, b).Any(t => Battlefield.Distance(state, m, t) <= GfNames.MeleeHorizontal && Battlefield.Vertical(state, m, t) <= GfNames.MeleeVertical));
    static StepOutcome AfterChild(StepContext ctx, Func<StepOutcome> push, IReadOnlyList<StateChange>? onDone = null) =>
        ctx.History.Children.Count == 0 ? push() : ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete(onDone ?? []) : new OpenAsks([]);
    static IReadOnlyList<StateChange> Fatigue(Guid unit) => [new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Fatigued, true)];

    /// <summary>Wounds the target took since melee-start, plus Fear(X) over the attacker's models when it attacked (p.13).</summary>
    public static int WoundsCaused(StepContext ctx, Guid by, Guid to, string beforeKey, bool attacked)
    {
        var wounds = ctx.Projection.Units[to].WoundsThisActivation - ctx.Frame.Value(beforeKey);
        var fear = attacked ? Lookup.LivingModels(ctx.State, by).Sum(m => Lookup.ModelParameter(ctx.State, m, "Fear") ?? 0) : 0;
        return wounds + fear;
    }

    sealed class ChargeTargetStep : IStep
    {
        public string Name => "charge-target";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var unit = Charger(ctx);
            var live = LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, AttackKind.Melee, GfNames.Charge, null, []));
            var distance = StatReader.ActionDistance(live, Side.Attacker, GfNames.Charge);
            var terrain = Battlefield.Terrain(ctx.State);
            var impassable = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
            var team = ctx.ActorTeam;
            // p.14: "Units may use Charge actions to disembark". An occupant has no position, so its reach is the exit radius from
            // the container it is inside — anything it could be placed fully within 6" of is a unit it could then be in contact with.
            var inside = Lookup.ContainerOf(ctx.State, unit);
            var targets = ctx.Projection.Units.Values.Where(u => ctx.TeamOf(u.Id) != team).Select(u => u.Id).Where(t => inside is { } container
                ? Lookup.LivingModels(ctx.State, t).Any(e => Battlefield.Distance(ctx.State, container, e) <= GfNames.ExitWithin)
                : Lookup.LivingModels(ctx.State, unit).Any(m => Lookup.LivingModels(ctx.State, t).Any(e =>
                {
                    var (from, to) = (Battlefield.Placed(ctx.State, m)!.Value, Battlefield.Placed(ctx.State, e)!.Value);
                    var check = Movement.Check(from, to.Placement.Position.Horizontal, distance, impassable, [], null, ctx.State.Board.Table.Size);
                    return Battlefield.Distance(ctx.State, m, e) <= distance && !check.CrossesImpassable;
                }))).ToList();
            if (targets.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("charge_target", "Charge which unit?", team!.Value, new PieceSelection(targets, 1, 1))]);
        }
    }

    /// <summary>p.9: the target's models not in base contact move up to 3" into contact — their models, their Ask.</summary>
    sealed class DefenderClosesStep : IStep
    {
        public string Name => "defender-closes";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, target) = (Charger(ctx), Target(ctx));
            if (ctx.History.LastClosed() is { } closed)
            {
                var moves = (closed.Single as Destinations)?.Moves.Where(m => m.To is OnTable).Select(m => new ModelRelocation(m.ModelId, m.From, new OnTable(Battlefield.Settle(ctx.State, ((OnTable)m.To).Placement)))).ToList() ?? [];
                return new Complete(moves.Count > 0 ? [new ModelsMoved(moves)] : []);
            }
            var free = Lookup.LivingModels(ctx.State, target).Where(t => Lookup.LivingModels(ctx.State, charger).All(m => Battlefield.Distance(ctx.State, m, t) > 0)).ToList();
            if (free.Count == 0 || ctx.TeamOf(target) is not { } team) return new Complete([]);
            return new OpenAsks([ctx.Ask("move", $"Move up to {GfNames.ChargeCloseIn}\" into contact", team, new MoveRegion(free, GfNames.ChargeCloseIn, [], [], null), allowPass: true)]);   // a close-in: no terrain parameters, no cap
        }
    }

    sealed class FightStep : IStep
    {
        public string Name => "fight";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, target) = (Charger(ctx), Target(ctx));
            if (ctx.History.Children.Count == 0 && !InReach(ctx.State, charger, target)) return new Finish([]);
            return AfterChild(ctx, () => new Push(GfNames.Melee, new SequenceContext(charger, [target], MeleeWeapons(ctx.State, charger), GfNames.Charge)));
        }
    }

    sealed class MeleeStartStep : IStep
    {
        public string Name => "melee-start";
        public StepOutcome Run(StepContext ctx) => new Complete([], new Dictionary<string, int>
            { ["charger_before"] = ctx.Projection.Units[Charger(ctx)].WoundsThisActivation, ["defender_before"] = ctx.Projection.Units[Defender(ctx)].WoundsThisActivation });
    }

    sealed class CounterStep : IStep
    {
        public string Name => "counter";
        public StepOutcome Run(StepContext ctx)
        {
            var counters = MeleeWeapons(ctx.State, Defender(ctx), w => w.Properties.Any(p => p.Name == "Counter"));
            if (counters.Count == 0) return new Complete([]);
            return AfterChild(ctx, () => new Push(GfNames.AttackSequence, new SequenceContext(Defender(ctx), [Charger(ctx)], counters, null)), Fatigue(Defender(ctx)));
        }
    }

    /// <summary>p.14 and p.13: X dice per Impact(X) model, unless fatigued, less one per defending model with Counter; hits at the effect's threshold. Two phases: roll (Repeat), then push resolve-hits.</summary>
    sealed class ImpactStep : IStep
    {
        public string Name => "impact";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.Frame.Value("impact_phase") == 1)
                return AfterChild(ctx, () => new Push(GfNames.ResolveHits, new SequenceContext(charger, [defender], [], GfNames.Charge)));
            var live = LiveProperties.Settle(GfAttack.AttackInteraction(ctx, charger, defender, [], AttackKind.Melee, GfNames.Charge));
            var impacts = RollEffects.OwnRolls(live, Side.Attacker, c => c.Kind == EffectKind.AddHit);   // Impact's conditions (charging, not fatigued) were judged in pass one's read
            if (impacts.Count == 0) return new Complete([], new Dictionary<string, int> { ["hits"] = 0 });
            var counterModels = Lookup.LivingModels(ctx.State, defender).Count(m => ctx.Projection.Models[m].Slot.Weapons.Any(w => w.Properties.Any(p => p.Name == "Counter")));
            var dice = Math.Max(0, impacts.Sum(x => x.Roll.EffectiveAmount(x.Source.Parameter)) - counterModels);
            var hits = dice == 0 ? 0 : Dice.RollOutcome.Successes(ctx.Roll(new Dice.RollPlan(impacts[0].Source.Entry.Name, impacts[0].Roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), dice));
            return new Complete([], new Dictionary<string, int> { ["hits"] = hits, ["impact_phase"] = 1 }, Repeat: hits > 0);
        }
    }
```

`RollEffects.OwnRolls` returns only rolls whose conditions hold now (`LiveSet.Effects` judges them), so a fatigued charger has no live `Impact` roll and the step records zero hits without drawing a die.


```csharp
    sealed class ChargerAttacksStep : IStep
    {
        public string Name => "charger-attacks";
        public StepOutcome Run(StepContext ctx) =>
            AfterChild(ctx, () => new Push(GfNames.AttackSequence, new SequenceContext(Charger(ctx), [Defender(ctx)], ctx.Frame.Context.WeaponIds, GfNames.Charge)), Fatigue(Charger(ctx)));
    }

    /// <summary>p.9: the defender may strike back (its Ask), and doing so fatigues it; a destroyed defender cannot.</summary>
    sealed class StrikeBackStep : IStep
    {
        public string Name => "strike-back";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (Lookup.LivingModels(ctx.State, defender).Count == 0 || Lookup.LivingModels(ctx.State, charger).Count == 0) return new Complete([]);
            if (ctx.History.Children.Count > 0) return AfterChild(ctx, () => throw new UnreachableException(), Fatigue(defender));
            if (ctx.History.LastClosed() is { } closed)
                return closed.Single is Chosen { OptionId: "strike" } ? new Push(GfNames.AttackSequence, new SequenceContext(defender, [charger], MeleeWeapons(ctx.State, defender), null)) : new Complete([]);
            return new OpenAsks([ctx.Ask("strike_back", "Strike back?", ctx.TeamOf(defender)!.Value, new Options([new AskOption("strike", "Strike back", [defender]), new AskOption("pass", "Don't", [])]))]);
        }
    }

    /// <summary>p.10: sum wounds caused each way; a destroyed unit always loses; the loser tests morale; a tie tests nobody. Values: charger_wounds, defender_wounds, loser (0 none, 1 charger, 2 defender).</summary>
    sealed class CompareStep : IStep
    {
        public string Name => "compare";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.Frame.Value("compared") == 1)   // second run: the loser tests morale on the melee path
            {
                var loserUnit = ctx.Frame.Value("loser") == 1 ? charger : defender;
                return AfterChild(ctx, () => new Push(GfNames.MoraleTest, new SequenceContext(loserUnit, [], [], "melee")));
            }
            var struck = ctx.Frame.Frame.Steps.Any(s => s.Step == "strike-back" && s.Answer is Chosen { OptionId: "strike" }) || ctx.Frame.Frame.Steps.Any(s => s.Step == "counter" && s.Changes.Count > 0);
            var chargerWounds = WoundsCaused(ctx, charger, defender, "defender_before", attacked: true);
            var defenderWounds = WoundsCaused(ctx, defender, charger, "charger_before", attacked: struck);
            var chargerDead = Lookup.LivingModels(ctx.State, charger).Count == 0; var defenderDead = Lookup.LivingModels(ctx.State, defender).Count == 0;
            var loser = defenderDead ? 2 : chargerDead ? 1 : chargerWounds > defenderWounds ? 2 : defenderWounds > chargerWounds ? 1 : 0;
            var values = new Dictionary<string, int> { ["charger_wounds"] = chargerWounds, ["defender_wounds"] = defenderWounds, ["loser"] = loser, ["compared"] = 1 };
            var testing = loser == 1 ? charger : loser == 2 ? defender : (Guid?)null;
            return new Complete([], values, Repeat: testing is not null && Lookup.LivingModels(ctx.State, testing.Value).Count > 0);
        }
    }

    /// <summary>p.10: a destroyed or routed unit lets the other move 3"; otherwise the charger moves back 1".</summary>
    sealed class ConsolidateStep : IStep
    {
        public string Name => "consolidate";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.History.LastClosed() is { } closed)
            {
                var moves = (closed.Single as Destinations)?.Moves.Where(m => m.To is OnTable).Select(m => new ModelRelocation(m.ModelId, m.From, new OnTable(Battlefield.Settle(ctx.State, ((OnTable)m.To).Placement)))).ToList() ?? [];
                return new Complete(moves.Count > 0 ? [new ModelsMoved(moves)] : []);
            }
            var (chargerAlive, defenderAlive) = (Lookup.LivingModels(ctx.State, charger), Lookup.LivingModels(ctx.State, defender));
            if (chargerAlive.Count == 0 && defenderAlive.Count == 0) return new Complete([]);
            var (mover, distance) = chargerAlive.Count == 0 ? (defender, GfNames.Consolidate) : defenderAlive.Count == 0 ? (charger, GfNames.Consolidate) : (charger, GfNames.FallBack);
            return new OpenAsks([ctx.Ask("move", $"Move up to {distance}\"", ctx.TeamOf(mover)!.Value, new MoveRegion(Lookup.LivingModels(ctx.State, mover), distance, [], [], null), allowPass: true)]);   // fall back / consolidate: no terrain parameters, no cap
        }
    }
}
```

`UnreachableException` is `System.Diagnostics.UnreachableException`: in `StrikeBackStep` the push lambda is never invoked when a child already exists. `GfRules._sequences` gains `GfMelee.Charge()` and `GfMelee.Melee()`.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfMeleeTests`; expect 4 passed. Commit: `Add charge and melee: Counter, Impact, strike back, who won, consolidation`.**

### Task 17: Casting — spell and tokens, the sealed bid, the roll, targets, effects, and once-grants

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfCasting.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`, `server/game/Effects/RollEffects.cs` (`ConsumeGrants`), `server/game/Rules/GrimdarkFuture/GfAttack.cs` (roll steps consume grants; `assign` applies a unit-level `Regeneration` grant to every model)
- Test: `tests/Tabletop.GameServer.Tests/GfCastingTests.cs`

**Interfaces:**
- Consumes: Tasks 9, 11–13; `GrantAdded`, `GrantRemoved`, `Grant`, `SpellTokensSet`, `PieceSelection`, `SelectedPieces`, `Bid`, `BidPlaced` (Protocol); `CatalogEntry.Targeting`, `EffectDuration.Once`.
- Produces: `GfCasting.Casting()` (`casting`: `pick-spell`, `bid`, `roll`, `targets`, `effects`) and `GfCasting.SpellHits()` (`spell-hits`: `hit`, `resolve`); `static IRL<Guid> Casters(GameState, Guid unit)` (models with `Caster(X)` and tokens); `static IRL<string> TriedThisActivation(GameState, Guid unit)` (spells named by a closed `pick-spell` since the activation's `sequence_started` — `OneTryPerSpellScope = "activation"`); `static IRD<Guid,int> BidCapacity(GameState, Guid casterModel)` (per team: tokens of models within 18" and line of sight of the caster's unit, the caster excluded, p.13); `static IRL<Guid> SpellTargets(StepContext, Guid casterModel, CatalogEntry spell)`; `RollEffects.ConsumeGrants(LiveSet live, Side side, RollPlan plan) : IRL<StateChange>` (a `GrantRemoved` for every granted instance whose entry contributed a modifier to the plan and has one use left).
- The option id of `pick-spell` is `"{spell}|{casterModelId}"`, so the paying model is in the Answer; its `Changes` deduct the cost (`SpellTokensSet`) and its `Values` record `bid_max_<team>` for every team `BidCapacity` finds. `bid` is `Windows.SealedBid` then `Windows.Bids`: tokens are deducted from the bidding team's eligible models nearest the caster first; `net_bid` = own team's tokens − others'. `roll` is `GfRolls.CastingPlan(net_bid)`, one die; a failure `Finish`es. `targets`: the spell's four columns — `Side` relative to the caster's team, `Grain` (unit ids or model ids), `Count` as the selection maximum (minimum 1), `Range` from the caster model, line of sight from the caster model (p.13) — as a `PieceSelection`. `effects` runs once per chosen target (`Values["target_index"]`, `Repeat`): every effect of the spell entry whose conditions hold, in ordinal order — `AddHit` with no `RollResult` condition → `Push(spell-hits, (casterUnit, [target unit], [], null))`, whose `hit` step rolls `amount` dice on threshold 1 (every die a hit, so `on rolls of 6` rules read a real die) and writes `granted_<entry>_<amount>` for the hits' nested grants, then pushes `resolve-hits`; any effect with `Duration = Once` → `GrantAdded(targetUnit, new Grant(id, effect.Id, effect.TargetEntry ?? spell.Name, casterUnit, 1))` (a model-grain target's grant lands on its unit — the grants store is the unit's). Nothing else can appear: `Validate` (Task 2) refuses a spell whose top-level effect is neither `add_hit` nor `duration = once`, which is what spell effects read in all forty-eight — *"always with one duration: once"*.
- Consumption: a granted instance (`LiveProperty.GrantId` set) whose entry contributed to a roll plan is consumed by that roll's step (`RollEffects.ConsumeGrants` in `roll-to-hit`/`roll-to-block`); a granted `Regeneration` is consumed by `assign` when it rolls. `Grant.RemainingUses` is 1 for every once-effect (spell effects → The grants store: "remaining uses (one)").

Spec: action sequences → Grimdark Future's sequences (Casting, The token economy), Windows (sealed simultaneous; invoked by the actor); spell effects → Duration, The grants store (written by the effect step's `step_completed`, consumed by the consuming step's, read by both passes and by name checks), Damage spells need nothing new (kind 6 with a nested grant; a hit roll that cannot fail), Target selection (one to `target_count`, each target independently); rulebook p.13 `Caster`.

- [ ] **Step 1: Write the test spell book and the failing tests**

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfCastingTests
{
    static EffectDocument Fx(EffectKind kind, string? targetEntry = null, int? amount = null, EffectDuration duration = EffectDuration.Interaction, params EffectDocument[] children) =>
        new(EffectSubject.Self, 10, kind, null, null, targetEntry, null, null, null, null, amount, false, null, null, null, duration, [], children);
    static readonly EntryDocument Fireball = new("Fireball", CatalogKind.Spell, false, null, 2, "Test Book", "Target enemy unit within 12\" takes 3 hits with AP(1).",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12), [Fx(EffectKind.AddHit, amount: 3, children: [Fx(EffectKind.GrantProperty, "AP", 1)])]);
    static readonly EntryDocument Ward = new("Ward", CatalogKind.Spell, false, null, 1, "Test Book", "Target up to two friendly units within 24\" get Regeneration once.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 2, 24), [Fx(EffectKind.GrantProperty, "Regeneration", null, EffectDuration.Once)]);
    static readonly PropertySet Book = TestProperties.WithSpells(Fireball, Ward);

    static Sim Casting(Tabletop.Content.Unit wizard, Tabletop.Content.Unit[] friends, Tabletop.Content.Unit[] enemies, params int[] dice)
    {
        var state = State([wizard, .. friends], enemies, x1: -4, x2: 4);
        var sim = new Sim(state, GrimdarkFutureRules.Instance, Book, new Dice.ScriptedDice(dice));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        return sim;
    }

    [Fact]
    public void Fireball_spends_tokens_bids_are_sealed_and_the_hits_roll_a_hit_roll_that_cannot_fail()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var enemyCaster = Unit("Warlock", "4+", "4+", [], 1, [P("Caster", 1)]); var grunts = Unit("Grunts", "4+", "5+", [], 3, []);
        var sim = Casting(wizard, [], [enemyCaster, grunts], 5, 6, 1, 1, 2, 3, 4);
        sim.Append([new SpellTokensSet(enemyCaster.Models[0].Id, 2)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        var pick = Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([$"Fireball|{wizard.Models[0].Id}", $"Ward|{wizard.Models[0].Id}"], pick.Choices.Select(c => c.Id).OrderBy(x => x));
        sim.Answer(Samples.Team1, new Chosen($"Fireball|{wizard.Models[0].Id}"));
        Assert.Equal(1, sim.Model(wizard.Models[0].Id).SpellTokens);
        var bid = sim.State.Projection.OpenAsks.Values.Single().Ask;
        Assert.True(bid.Sealed); Assert.Equal([Samples.Team2], bid.Teams);                            // the wizard has no other caster in range; the warlock does
        Assert.Equal(2, sim.Steps("pick-spell").Single().Values![$"bid_max_{Samples.Team2:N}"]);
        sim.Answer(Samples.Team2, new BidPlaced(1));
        Assert.Equal(1, sim.Model(enemyCaster.Models[0].Id).SpellTokens);
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.Equal((GfNames.Casting, 4, 5, 4, true), (roll.Roll, roll.Threshold, roll.Dice[0].Raw, roll.Dice[0].Modified, roll.Dice[0].Success));
        var targets = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((1, 1), (targets.Min, targets.Max)); Assert.Contains(grunts.Id, targets.Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([grunts.Id]));
        var hit = sim.Steps("hit").Single();
        Assert.Equal((GfNames.Hit, 1, 3, 3), (hit.Rolls[0].Roll, hit.Rolls[0].Threshold, hit.Rolls[0].Dice.Count, hit.Rolls[0].Dice.Count(d => d.Success)));
        Assert.Equal(3, hit.Values!["granted_AP_1"]);
        var block = sim.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Contains(block.Modifiers, m => m.Source == "AP" && m.Amount == -1);
        Assert.Equal(3, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);            // 2,3,4 −1 against 5+: three wounds
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [grunts.Models[0].Id] = 1, [grunts.Models[1].Id] = 1, [grunts.Models[2].Id] = 1 }));
        Assert.Equal(0, sim.Alive(grunts.Id)); Assert.Empty(sim.State.Projection.Stack);
        Assert.Contains("Fireball", GfCasting.TriedThisActivation(sim.State, wizard.Id));
    }

    [Fact]
    public void A_failed_roll_ends_casting_and_a_tried_spell_is_not_offered_again_this_activation()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = Casting(wizard, [], [Unit("G", "4+", "4+", [], 2, [])], 2);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Fireball|{wizard.Models[0].Id}"));                    // nobody can bid: straight to the roll
        Assert.Equal(0, sim.Steps("roll").Single().Values!["passed"]);
        Assert.Empty(sim.State.Projection.Stack);                                                     // casting finished; no activation frame in this test, so the scan covers the whole log
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        Assert.Equal([$"Ward|{wizard.Models[0].Id}"], Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal).Choices.Select(c => c.Id));
    }

    [Fact]
    public void Ward_grants_regeneration_once_to_two_units_and_the_grant_is_consumed_when_it_applies()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var a = Unit("A", "4+", "4+", [], 2, []); var b = Unit("B", "4+", "4+", [], 2, []);
        var sim = Casting(wizard, [a, b], [Unit("G", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1))], 4, 6, 1);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Ward|{wizard.Models[0].Id}"));
        var targets = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((1, 2), (targets.Min, targets.Max)); Assert.Equal(new HashSet<Guid> { wizard.Id, a.Id, b.Id }, targets.Eligible.ToHashSet());
        sim.Answer(Samples.Team1, new SelectedPieces([a.Id, b.Id]));
        Assert.All(new[] { a.Id, b.Id }, u => Assert.Equal(("Regeneration", 1, wizard.Id), (sim.Unit(u).Grants.Single().EntryName, sim.Unit(u).Grants.Single().RemainingUses, sim.Unit(u).Grants.Single().CasterUnitId)));
        Assert.True(Effects.Lookup.UnitHas(sim.State, Book, a.Id, "Regeneration"));
        // A wound on A: the granted Regeneration rolls (6: ignored) and is consumed.
        var shooter = sim.State.Board.Armies[1].Snapshot.Units[0];
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(shooter.Id, [a.Id], [], GfNames.Hold)), new StepCompleted(parent, "w", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = 1 } }]);
        sim.Start(GfNames.DealWounds, new SequenceContext(shooter.Id, [a.Id], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [a.Models[0].Id] = 1 }));
        Assert.Equal("Regeneration", sim.Steps("assign").Single().Rolls.Single().Roll);
        Assert.Equal(2, sim.Alive(a.Id));
        Assert.Empty(sim.Unit(a.Id).Grants); Assert.Single(sim.Unit(b.Id).Grants);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `GfCasting.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfCasting
{
    public static Sequence Casting() => new(GfNames.CastingSeq, [new PickSpellStep(), new BidStep(), new RollStep(), new TargetsStep(), new EffectsStep()]);
    public static Sequence SpellHits() => new("spell-hits", [new HitStep(), new GfAttack.PushStep("resolve", GfNames.ResolveHits)]);

    /// <summary>The in-play reading, matching GfHooks.ReactionsAt: a caster inside a container may still cast at what is inside with it (p.14).</summary>
    public static IReadOnlyList<Guid> Casters(GameState state, Guid unit) =>
        Lookup.AliveModels(state, unit).Where(m => Lookup.ModelParameter(state, m, "Caster") is not null && state.Projection.Models[m].SpellTokens > 0).ToList();

    /// <summary>Spells picked by a closed pick-spell since this unit's activation began (OneTryPerSpellScope = activation).</summary>
    public static IReadOnlyList<string> TriedThisActivation(GameState state, Guid unit)
    {
        var log = EventApplier.EffectiveLog(state.Applier.Log).ToList();
        var since = log.FindLastIndex(e => e.Payload is SequenceStarted { Context.ActorUnitId: { } u } s && u == unit && s.Context.Action is GfNames.Hold or GfNames.Advance or GfNames.Rush or GfNames.Charge);
        return log.Skip(Math.Max(0, since)).Select(e => e.Payload).OfType<StepCompleted>().Where(s => s.Step == "pick-spell" && s.Answer is Chosen)
            .Select(s => ((Chosen)s.Answer!).OptionId.Split('|')[0]).ToList();
    }

    /// <summary>
    /// p.13: models within 18" in line of sight of the caster's unit may spend tokens, both sides; the caster itself does not bid.
    /// The positioned reading on both ends, deliberately: an occupant has no line of sight out (p.14), so a caster inside a container
    /// draws no bids at all — `mine` is empty — and a model inside one never bids on a cast outside. A cast from inside still happens;
    /// it is simply unmodified.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> BidCapacity(GameState state, Guid casterModel)
    {
        var unit = state.Projection.Models[casterModel].UnitId;
        var mine = Lookup.LivingModels(state, unit);
        return state.Projection.Models.Values.Where(m => m.Id != casterModel && m.Location is OnTable && m.SpellTokens > 0
                && mine.Any(c => Battlefield.Distance(state, c, m.Id) <= GfNames.CastRange && Battlefield.CanSee(state, m.Id, c)))
            .GroupBy(m => Teams.Of(state, m.UnitId)).Where(g => g.Key is not null).ToDictionary(g => g.Key!.Value, g => g.Sum(m => m.SpellTokens));
    }

    public static IReadOnlyList<Guid> SpellTargets(StepContext ctx, Guid casterModel, CatalogEntry spell)
    {
        var t = spell.Targeting!; var casterUnit = ctx.Projection.Models[casterModel].UnitId; var team = ctx.TeamOf(casterUnit);
        // p.14 again: containment answers before range and sight — no spell crosses a container's wall either way, and a caster
        // inside one reaches the units in there with it and the container itself with nothing to measure.
        var units = ctx.Projection.Units.Values.Where(u => (ctx.TeamOf(u.Id) == team) == (t.Side == TargetSide.Friendly)).Select(u => u.Id)
            .Where(u => Battlefield.InsideReach(ctx.State, casterUnit, u) is { } reaches ? reaches
                : Lookup.LivingModels(ctx.State, u).Any(m => Battlefield.Distance(ctx.State, casterModel, m) <= t.Range && (m == casterModel || Battlefield.CanSee(ctx.State, casterModel, m)))).ToList();
        return t.Grain == TargetGrain.Unit ? units : units.SelectMany(u => Lookup.LivingModels(ctx.State, u)).Where(m => Battlefield.Distance(ctx.State, casterModel, m) <= t.Range).ToList();
    }

    static (string Spell, Guid Caster) Picked(StepContext ctx) { var parts = ((Chosen)ctx.Frame.Frame.Steps[0].Answer!).OptionId.Split('|'); return (parts[0], Guid.Parse(parts[1])); }

    sealed class PickSpellStep : IStep
    {
        public string Name => "pick-spell";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.History.LastClosed() is { Single: Chosen chosen })
            {
                var (spellName, caster) = (chosen.OptionId.Split('|')[0], Guid.Parse(chosen.OptionId.Split('|')[1]));
                var cost = ctx.Properties.Entry(spellName)!.Cost ?? 0;
                var values = BidCapacity(ctx.State, caster).ToDictionary(kv => $"bid_max_{kv.Key:N}", kv => kv.Value);
                return new Complete([new SpellTokensSet(caster, Math.Max(0, ctx.Projection.Models[caster].SpellTokens - cost))], values);
            }
            var tried = TriedThisActivation(ctx.State, unit);
            var options = Casters(ctx.State, unit).SelectMany(m => ctx.Properties.Entries.Where(e => e.Kind == CatalogKind.Spell && !tried.Contains(e.Name) && e.Cost <= ctx.Projection.Models[m].SpellTokens)
                .Select(e => new AskOption($"{e.Name}|{m}", $"{e.Name} ({e.Cost})", [m]))).ToList();
            return options.Count == 0 ? new Finish([]) : new OpenAsks([ctx.Ask("cast", "Cast which spell?", ctx.ActorTeam!.Value, new Options(options))]);
        }
    }

    sealed class BidStep : IStep
    {
        public string Name => "bid";
        public StepOutcome Run(StepContext ctx)
        {
            var max = ctx.Frame.Values.Where(kv => kv.Key.StartsWith("bid_max_")).ToDictionary(kv => Guid.ParseExact(kv.Key["bid_max_".Length..], "N"), kv => kv.Value);
            if (Windows.SealedBid(ctx, GfNames.CastingSeq, max, "Spend tokens for or against the casting roll") is { } open) return open;
            var (_, caster) = Picked(ctx);
            var own = ctx.ActorTeam; var net = 0; var changes = new List<StateChange>();
            foreach (var (team, tokens) in Windows.Bids(ctx, GfNames.CastingSeq).Where(kv => kv.Value > 0))
            {
                net += team == own ? tokens : -tokens;
                var payers = ctx.Projection.Models.Values.Where(m => m.Id != caster && m.SpellTokens > 0 && Teams.Of(ctx.State, m.UnitId) == team && m.Location is OnTable)
                    .OrderBy(m => Battlefield.Distance(ctx.State, caster, m.Id)).ToList();
                var left = tokens;
                foreach (var m in payers) { if (left == 0) break; var spend = Math.Min(left, m.SpellTokens); changes.Add(new SpellTokensSet(m.Id, m.SpellTokens - spend)); left -= spend; }
            }
            return new Complete(changes, new Dictionary<string, int> { ["net_bid"] = net });
        }
    }

    sealed class RollStep : IStep
    {
        public string Name => "roll";
        public StepOutcome Run(StepContext ctx)
        {
            var passed = Dice.RollOutcome.Successes(ctx.Roll(GfRolls.CastingPlan(ctx, ctx.Frame.Value("net_bid")), 1)) == 1;
            var values = new Dictionary<string, int> { ["passed"] = passed ? 1 : 0 };
            return passed ? new Complete([], values) : new Finish([], values);
        }
    }

    sealed class TargetsStep : IStep
    {
        public string Name => "targets";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var (spellName, caster) = Picked(ctx);
            var spell = ctx.Properties.Entry(spellName)!;
            var eligible = SpellTargets(ctx, caster, spell);
            if (eligible.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("spell_targets", $"{spellName}: pick up to {spell.Targeting!.Count}", ctx.ActorTeam!.Value, new PieceSelection(eligible, 1, Math.Min(spell.Targeting.Count, eligible.Count)))]);
        }
    }
```

```csharp
    /// <summary>One target per run (Repeat): hits push spell-hits; once-effects write grants. Each target gets the effect independently (spell effects → Target selection).</summary>
    sealed class EffectsStep : IStep
    {
        public string Name => "effects";
        public StepOutcome Run(StepContext ctx)
        {
            var (spellName, caster) = Picked(ctx);
            var spell = ctx.Properties.Entry(spellName)!;
            var casterUnit = ctx.Frame.Context.ActorUnitId!.Value;
            var chosen = ((SelectedPieces)ctx.Frame.Frame.Steps.Last(s => s.Step == "targets").Answer!).PieceIds;
            var index = ctx.Frame.Value("target_index");
            if (index >= chosen.Count) return new Complete([]);
            var target = chosen[index];
            var targetUnit = ctx.Projection.Units.ContainsKey(target) ? target : ctx.Projection.Models[target].UnitId;
            var hits = spell.Effects.Where(e => e.Kind == EffectKind.AddHit && e.Conditions.All(c => c.Kind != ConditionKind.RollResult)).ToList();
            if (hits.Count > 0 && ctx.History.Children.Count == 0)
                return new Push("spell-hits", new SequenceContext(casterUnit, [targetUnit], [], null));
            if (hits.Count > 0 && !(ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned)) return new OpenAsks([]);
            var changes = spell.Effects.Where(e => e.Duration == EffectDuration.Once)
                .Select(e => (StateChange)new GrantAdded(targetUnit, new Grant(Guid.CreateVersion7(), e.Id, e.TargetEntry ?? spell.Name, casterUnit, 1))).ToList();
            return new Complete(changes, new Dictionary<string, int> { ["target_index"] = index + 1 }, Repeat: index + 1 < chosen.Count);
        }
    }

    /// <summary>A hit roll that cannot fail: one die per hit on threshold 1, so "on rolls of 6" rules read a real die; the hits' nested grants become granted_* values.</summary>
    sealed class HitStep : IStep
    {
        public string Name => "hit";
        public StepOutcome Run(StepContext ctx)
        {
            var parent = ctx.Frame.Parent!;
            var (spellName, _) = (((Chosen)parent.Frame.Steps[0].Answer!).OptionId.Split('|')[0], 0);
            var spell = ctx.Properties.Entry(spellName)!;
            var values = new Dictionary<string, int> { ["extra_hits"] = 0, ["hit_multiplier"] = 1 };
            var total = 0;
            foreach (var hit in spell.Effects.Where(e => e.Kind == EffectKind.AddHit))
            {
                var n = hit.EffectiveAmount(null);
                var record = ctx.Roll(new Dice.RollPlan(GfNames.Hit, 1, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), n);
                total += Dice.RollOutcome.Successes(record);
                foreach (var grant in hit.Children.Where(c => c.Kind == EffectKind.GrantProperty)) values[$"granted_{grant.TargetEntry}_{grant.Amount}"] = values.GetValueOrDefault($"granted_{grant.TargetEntry}_{grant.Amount}") + n;
            }
            return total == 0 ? new Finish([], values) : new Complete([], values);
        }
    }
}
```

`spell-hits`'s `resolve` pushes `resolve-hits` with `ctx.Frame.Context` (caster unit, target unit, no weapons), and `RollToBlockStep` reads `HitsFrom(parent)` from the `spell-hits` frame's hit roll and `granted_AP_1` from its values; `AttackInteraction` with no weapons takes the caster unit's living models as the party, so the spell's hits carry the caster's unit properties and the granted `AP(1)`, nothing else. `GfRules._sequences` gains `GfCasting.Casting()` and `GfCasting.SpellHits()`.

- [ ] **Step 3: Grant consumption — add to `RollEffects.cs` and use it in `GfAttack`**

```csharp
    /// <summary>A once-grant is consumed the first time its effect applies: every granted instance whose entry contributed a modifier to this plan, with one use left.</summary>
    public static IReadOnlyList<StateChange> ConsumeGrants(LiveSet live, Side side, RollPlan plan) =>
        live.On(side).Where(p => p.GrantId is not null && plan.Modifiers.Any(m => m.Source == p.Entry.Name))
            .Select(p => (StateChange)new GrantRemoved(live.Interaction.Of(side).UnitId, p.GrantId!.Value)).ToList();
```

`RollToHitStep` returns `new Complete(RollEffects.ConsumeGrants(live, Side.Attacker, plan), values)` and `RollToBlockStep` accumulates `ConsumeGrants(live, Side.Defender, plan)` per defense roll into its `Changes`. `AssignStep`: a granted `Regeneration` instance has no `ModelId` (it is the unit's), so the `regen` dictionary is built as: per-model instances by model id, plus, when a unit-level instance exists, every living model of the target maps to it; when a unit-level instance rolls, `GrantRemoved(target, grantId)` joins the changes. `GrantAdded` carries no parameter; `Lookup.UnitProperties` reads the granting effect's `Amount` (Task 6), which is null for `Ward`'s `Regeneration` and `1` for a *"gets AP(1)"* grant.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfCastingTests`; expect 3 passed, and the whole project still green. Commit: `Add casting: spell and tokens, the sealed bid, the roll, targets, hits that cannot miss, once-grants`.**

### Task 19: A destroyed container disgorges its occupants

**Numbered 19 but sequenced here, before Task 18**, so that Task 18's whole-tree check counts its tests; the number is last because
this task was added on 2026-09-06, after the plan was reviewed, and renumbering would break every "Task N" reference the other plans
carry.

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfContainers.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`DealWounds()` gains a second step; `assign` leaves an occupied container in place), `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs` (register `GfContainers.TransportDestroyed()`)
- Test: `tests/Tabletop.GameServer.Tests/GfContainersTests.cs`

**Interfaces:**
- Consumes: Tasks 6, 11–14; `Lookup.OccupantUnits`, `Lookup.ContainerOf`, `Lookup.AliveModels`, `Lookup.ModelParameter` (Task 6); `Battlefield.Terrain` (Task 11); `GfMoves.IsContainer`, `GfMoves.DangerousDice`, `GfMoves.DangerousTerrain()`, `GfMoves.ExitPlacement`, `GfNames.ExitWithin` (Task 14); `MoveRegion`, `MoveContainment`, `Destinations`, `Inside`, `OnShelf`, `ConditionSet` (Protocol).
- Produces: `static class GfContainers` with `Sequence TransportDestroyed()` (`transport-destroyed`: `wreck-test`, `place`) and `static IRL<Guid> Wrecks(GameState)`; `GfAttack.DealWounds()` becomes `[assign, disgorge]` with `sealed class DisgorgeStep`.
- **This is p.14's last clause and p.11's third dangerous-terrain trigger**: *"When a transport is destroyed, units inside must take a dangerous terrain test, are Shaken, and must be placed fully within 6" of the transport before removal."* p.20 says the same of a solid building. [Action sequences](../specs/2026-09-03-action-sequences-design.md) → Dangerous terrain names the three invokers — *"moves through it, activating in it, and transport destruction"* — and Task 14 built the first two; this is the third, and it reuses the same `DangerousTestStep` by writing `Values["dangerous_dice"]` on its own frame and pushing `dangerous-terrain`, exactly as `ActivationTerrainStep` does.
- **"Before removal" is why this is two steps in two sequences.** The occupants have to be placed *relative to the container*, so the container must still have a placement while they are placed; and there may be several occupant units, each owing its own test and its own placement Ask. So `assign` (Task 12) stops shelving a container that is holding anybody, `disgorge` runs one `transport-destroyed` frame per occupant unit until none is left inside, and then shelves the wreck. A container destroyed with nobody inside never reaches `disgorge`: `assign` shelved it, and `Wrecks` only sees models still on the table.
- **A nested `deal-wounds` never disgorges.** The occupants' dangerous terrain test pushes `deal-wounds` of its own, whose `disgorge` would find the very wreck being emptied and push `transport-destroyed` for the unit already being processed. The guard is a walk up this frame's own ancestors (`FrameView.Parent`, not the stack, so the outer `disgorge` is unaffected while its child runs): a `deal-wounds` beneath a `transport-destroyed` frame completes `disgorge` at once. A container wrecked by that nested test is not lost — the outer `disgorge` re-reads `Wrecks` after each child and only shelves when no wreck holds anybody.
- **Capacity is still not checked** ([game flow](../specs/2026-08-28-game-flow-design.md) → Inside a container: *"Capacity is not enforced"*). `GfMoves.IsContainer` asks whether `Transport` is on the model; its `(X)` is read nowhere in this plan.

Spec: game flow → Inside a container (a container is a model or a piece of terrain; when it is destroyed the occupants take a dangerous terrain test, are Shaken and are placed within 6"; capacity is not enforced); action sequences → Grimdark Future's sequences → Dangerous terrain (the third invoker); rulebook p.14 `Transport(X)`, p.11 dangerous terrain, p.20 solid buildings.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfContainersTests
{
    /// <summary>Starts deal-wounds under a parent frame that recorded the wounds, the way resolve-hits does (Task 12's harness).</summary>
    static void Wound(Sim sim, Tabletop.Content.Unit attacker, Guid target, int wounds)
    {
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(attacker.Id, [target], [], GfNames.Hold)),
            new StepCompleted(parent, "wounds", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = wounds } }]);
        sim.Start(GfNames.DealWounds, new SequenceContext(attacker.Id, [target], [], GfNames.Hold));
    }

    [Fact]
    public void A_destroyed_transport_tests_shakes_and_places_its_occupants_and_is_removed_last()
    {
        var gunners = Unit("Gunners", "4+", "4+", [], 1, [], Gun("Cannon", "24\"", 1));
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var riders = Unit("Riders", "4+", "4+", [], 3, []);
        // Three wounds kill the Tough(3) bus; the riders then roll three dangerous terrain dice — 1, 4, 4 — for one wound.
        var sim = Sim.Gf(State([gunners], [bus, riders]), 1, 4, 4);
        var container = bus.Models[0].Id;
        sim.Append([new ModelsMoved(riders.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container))).ToList())]);
        Wound(sim, gunners, bus.Id, 3);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [container] = 3 }));
        Assert.IsType<OnTable>(sim.Model(container).Location);                                        // p.14: not removed until the occupants are out
        Assert.Equal(3, sim.Steps("test").Single().Rolls.Single().Dice.Count);                        // one die per rider (p.11)
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [riders.Models[2].Id] = 1 }));
        var seat = ((OnTable)sim.Model(container).Location).Placement.Position;
        var ask = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team2).Legal);
        Assert.Equal((container, GfNames.ExitWithin), (ask.Containment!.ExitingId!.Value, ask.Containment.ExitWithin!.Value));
        Assert.Equal(2, ask.ModelIds.Count);                                                          // the survivors
        sim.Answer(Samples.Team2, new Destinations([
            new ModelRelocation(riders.Models[0].Id, new Inside(container), new OnTable(new Placement(seat with { X = seat.X + 4 }, new Yaw(0)))),
            new ModelRelocation(riders.Models[1].Id, new Inside(container), new OnTable(new Placement(seat with { X = seat.X + 12 }, new Yaw(0)))),
        ]));
        Assert.Equal(seat.X + 4, ((OnTable)sim.Model(riders.Models[0].Id).Location).Placement.Position.X, 6);
        Assert.IsType<OnShelf>(sim.Model(riders.Models[1].Id).Location);                              // 12" out is not "fully within 6""; there is nowhere left to stand
        Assert.IsType<OnShelf>(sim.Model(riders.Models[2].Id).Location);                              // killed by the test
        Assert.IsType<OnShelf>(sim.Model(container).Location);                                        // removed after them
        Assert.Contains(GfNames.Shaken, sim.Unit(riders.Id).Conditions);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void A_container_destroyed_with_nobody_inside_is_an_ordinary_casualty()
    {
        var gunners = Unit("Gunners", "4+", "4+", [], 1, [], Gun("Cannon", "24\"", 1));
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var sim = Sim.Gf(State([gunners], [bus]), 1);
        Wound(sim, gunners, bus.Id, 1);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [bus.Models[0].Id] = 1 }));
        Assert.IsType<OnShelf>(sim.Model(bus.Models[0].Id).Location);                                 // assign shelved it; Wrecks never sees it
        Assert.Empty(sim.Steps("test"));
        Assert.Empty(GfContainers.Wrecks(sim.State));
        Assert.Empty(sim.State.Projection.Stack);
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfContainersTests`; expect compile errors on `GfContainers`. Write `GfContainers.cs`**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// p.14: "When a transport is destroyed, units inside must take a dangerous terrain test, are Shaken, and must be placed fully
/// within 6" of the transport before removal." p.20 says the same of a solid building. One frame per occupant unit, pushed by
/// deal-wounds' disgorge step, which shelves the wreck once the last of them is out — which is what "before removal" requires.
/// </summary>
public static class GfContainers
{
    public static Sequence TransportDestroyed() => new(GfNames.TransportDestroyedSeq, [new WreckTestStep(), new PlaceSurvivorsStep()]);

    /// <summary>
    /// Destroyed containers still on the table. `assign` leaves a container at or past its Tough capacity in place while it holds
    /// anybody, so this is the whole predicate: a container destroyed empty is an ordinary casualty and is already on the shelf.
    /// </summary>
    public static IReadOnlyList<Guid> Wrecks(GameState state) =>
        state.Projection.Models.Values.Where(m => m.Location is OnTable && GfMoves.IsContainer(state, m.Id)
            && m.Wounds >= (Lookup.ModelParameter(state, m.Id, GfNames.Tough) ?? 1)).Select(m => m.Id).ToList();

    /// <summary>p.11's third trigger. Same shape as GfMoves.ActivationTerrainStep: settle the dice on this frame, then push, so the child reads a settled count.</summary>
    sealed class WreckTestStep : IStep
    {
        public string Name => "wreck-test";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.Children.Count > 0) return new Complete([]);                               // the test has been run
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var dice = GfMoves.DangerousDice(ctx.State, Lookup.AliveModels(ctx.State, unit));
            if (dice == 0) return new Complete([]);
            return ctx.Frame.Value("dangerous_dice") > 0
                ? new Push(GfNames.DangerousTerrain, ctx.Frame.Context)
                : new Complete([], new Dictionary<string, int> { ["dangerous_dice"] = dice }, Repeat: true);
        }
    }

    /// <summary>
    /// p.14: the survivors are Shaken and placed fully within 6" of the wreck. The Ask is a move_region whose containment names the
    /// container it is exiting, so the client draws the same region the disembark move draws. A survivor with no placement in the
    /// answer, or one outside the radius, is shelved: the container is about to be removed and there is nowhere else for it to be.
    /// </summary>
    sealed class PlaceSurvivorsStep : IStep
    {
        public string Name => "place";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (Lookup.ContainerOf(ctx.State, unit) is not { } container) return new Complete([]);     // the test killed everyone
            if (ctx.History.LastClosed() is not { } closed)
                return new OpenAsks([ctx.Ask("disembark", $"Place the survivors fully within {GfNames.ExitWithin:0.#}\" of the wreck", ctx.TeamOf(unit)!.Value,
                    // Allowance is unbound on a disembark (see Task 14): ExitWithin is passed so the number
                    // is the exit distance rather than a misleading zero. The client reads Containment, not this.
                    new MoveRegion(Lookup.AliveModels(ctx.State, unit), GfNames.ExitWithin,
                        Battlefield.Terrain(ctx.State).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).Select(v => v.Id).ToList(),
                        [], null, new MoveContainment([], container, GfNames.ExitWithin)), allowPass: false)]);
            IReadOnlyList<ModelRelocation> proposed = (closed.Single as Destinations)?.Moves ?? [];
            var moves = Lookup.AliveModels(ctx.State, unit).Select(m =>
            {
                var to = proposed.FirstOrDefault(r => r.ModelId == m)?.To as OnTable;
                Placement? exit = to is null ? null : GfMoves.ExitPlacement(ctx.State, m, container, to.Placement, GfNames.ExitWithin);
                return new ModelRelocation(m, new Inside(container), exit is { } p ? new OnTable(p) : (Location)new OnShelf());
            }).ToList();
            return new Complete([new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, true), new ModelsMoved(moves)]);
        }
    }
}
```

- [ ] **Step 3: Add `DisgorgeStep` to `GfAttack.cs`, extend `DealWounds()`, and stop `assign` shelving an occupied container**

```csharp
    public static Sequence DealWounds() => new(GfNames.DealWounds, [new AssignStep(), new DisgorgeStep()]);
```

In `AssignStep.Run`, the casualty line becomes:

```csharp
                // p.14: a container holding anybody stays on the table until disgorge has placed them — "before removal".
                if (total >= capacity && !(GfMoves.IsContainer(ctx.State, modelId) && Lookup.OccupantUnits(ctx.State, modelId).Count > 0))
                    moves.Add(new ModelRelocation(modelId, model.Location, new OnShelf()));
```

and the new step goes beside it:

```csharp
    /// <summary>
    /// p.14: a destroyed container is not removed until the units inside are out. `assign` left it on the table; this runs
    /// transport-destroyed once per occupant unit — each takes its dangerous terrain test, is Shaken and is placed — and then
    /// shelves every wreck. Nothing to do is the common case and costs one predicate.
    /// </summary>
    sealed class DisgorgeStep : IStep
    {
        public string Name => "disgorge";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.Children.Count > 0 && !(ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned)) return new OpenAsks([]);
            // The occupants' own dangerous terrain test pushes a deal-wounds of its own; that one must not disgorge the wreck the
            // frame above it is already emptying. The guard walks this frame's own ancestors, not the stack, so the outer disgorge
            // is unaffected while its child runs. A container wrecked by that nested test is still caught: the outer disgorge
            // re-reads Wrecks after every child and only shelves when no wreck holds anybody.
            for (var f = ctx.Frame.Parent; f is not null; f = f.Parent)
                if (f.Frame.Sequence == GfNames.TransportDestroyedSeq) return new Complete([]);
            var wrecks = GfContainers.Wrecks(ctx.State);
            if (wrecks.Count == 0) return new Complete([]);
            foreach (var wreck in wrecks)
                if (Lookup.OccupantUnits(ctx.State, wreck) is [var occupant, ..])
                    return new Push(GfNames.TransportDestroyedSeq, new SequenceContext(occupant, [], [], null));
            return new Complete([new ModelsMoved(wrecks.Select(w => new ModelRelocation(w, ctx.Projection.Models[w].Location, new OnShelf())).ToList())]);
        }
    }
```

`GfRules._sequences` gains `GfContainers.TransportDestroyed()`.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect `GfContainersTests` 2 passed and every earlier sequence test still green — `deal-wounds` now has a second step, so `GfDealWoundsTests` gains a `step_completed` named `disgorge` with no changes in each of its four cases. Commit: `Disgorge a destroyed container: the occupants test, are shaken, are placed within 6", and the wreck goes last`.**

### Task 18: The nine constraints as tests, and the record in `CLAUDE.md`

**Files:**
- Create: `tests/Tabletop.GameServer.Tests/InterfaceConstraintTests.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing new; the tests pin what the generic half must not contain.

Spec: action sequences → What the interface must not assume (the nine); Global Constraints (no ruleset name outside `Rules/GrimdarkFuture/`).

- [ ] **Step 1: Write the tests**

```csharp
using System.Reflection;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Action sequences → What the interface must not assume. Each test names the constraint it pins; 5 (granularity) and 8 (interrupts) are pinned by shape in Tasks 8 and 9.</summary>
public class InterfaceConstraintTests
{
    static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    static IEnumerable<string> GenericSources() => new[] { "Interpreter", "Effects", "Dice", "Engine", "Properties" }
        .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, "server/game", d), "*.cs", SearchOption.AllDirectories));

    [Fact]
    public void Constraint_1_roll_names_are_rows_and_the_generic_half_names_none()
    {
        Assert.Equal(4, TestProperties.Core.Rolls.Count);
        foreach (var file in GenericSources())
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[] { "\"hit\"", "\"defense\"", "\"morale\"", "\"casting\"", "\"Quality\"", "\"Defense\"", "\"Hold\"", "\"Advance\"", "\"Rush\"", "\"Charge\"", "\"fatigued\"", "\"shaken\"", "Regeneration", "Tough", "Transport" })
                Assert.False(text.Contains(forbidden), $"{Path.GetFileName(file)} names {forbidden}");
        }
        // The two weapon stat names StatReader needs are the one exception, whitelisted (Task 7, question 5).
        Assert.Contains("\"Range\"", File.ReadAllText(Path.Combine(RepoRoot, "server/game/Effects/StatReader.cs")));
    }

    [Fact]
    public void Constraints_2_3_4_threshold_is_a_function_and_convention_and_cap_are_per_roll()
    {
        Assert.IsAssignableFrom<Delegate>(typeof(RollSpec).GetProperty(nameof(RollSpec.Threshold))!.GetValue(GrimdarkFutureRules.Instance.RollSpec(GfNames.Hit)));
        var specs = TestProperties.Core.Rolls.Select(r => GrimdarkFutureRules.Instance.RollSpec(r.Name)).ToList();
        Assert.All(specs, s => Assert.Equal(new RollConvention(6, null), s.Convention));
        var capped = new RollPlan("x", 4, [new RollModifier("a", 2), new RollModifier("b", 1)], new RollConvention(6, 1), null, new HashSet<int>());
        Assert.Equal(1, capped.NetModifier);                                                     // the cap is the convention's, applied after summing
        Assert.Equal(3, (capped with { Convention = new RollConvention(6, null) }).NetModifier);
        Assert.DoesNotContain(typeof(Roller).GetMethods(), m => m.Name.Contains("Six") || m.Name.Contains("D6"));
    }

    [Fact]
    public void Constraint_6_deal_wounds_reads_a_capacity_not_a_rule_name_outside_the_ruleset()
    {
        var generic = string.Join("\n", GenericSources().Select(File.ReadAllText));
        Assert.DoesNotContain("Tough", generic);
        Assert.Contains("ModelParameter(ctx.State, modelId, \"Tough\") ?? 1", File.ReadAllText(Path.Combine(RepoRoot, "server/game/Rules/GrimdarkFuture/GfAttack.cs")));
    }

    [Fact]
    public void Constraint_7_morale_is_a_sequence_with_ruleset_owned_triggers()
    {
        Assert.Equal(GfNames.MoraleTest, GrimdarkFutureRules.Instance.Sequence(GfNames.MoraleTest).Name);
        Assert.DoesNotContain(GrimdarkFutureRules.Instance.Sequence(GfNames.AttackSequence).Steps, s => s.Name.Contains("morale"));
        Assert.DoesNotContain(GrimdarkFutureRules.Instance.Sequence(GfNames.ResolveHits).Steps, s => s.Name.Contains("morale"));
        Assert.Contains(typeof(IGameRules).GetMethods(), m => m.Name == nameof(IGameRules.EndOfTurnSequences));
    }

    [Fact]
    public void Constraint_9_an_ask_names_its_team_and_the_interpreter_never_assumes_the_actor()
    {
        Assert.Equal(typeof(IReadOnlyList<Guid>), typeof(Ask).GetProperty(nameof(Ask.Teams))!.PropertyType);
        var interpreter = File.ReadAllText(Path.Combine(RepoRoot, "server/game/Interpreter/SequenceInterpreter.cs"));
        Assert.DoesNotContain("Defender", interpreter); Assert.DoesNotContain("Attacker", interpreter);
        Assert.Contains("Guid team", File.ReadAllText(Path.Combine(RepoRoot, "server/game/Interpreter/StepContext.cs")));   // Ask(..., Guid team, ...)
    }
}
```

- [ ] **Step 2: Edit `CLAUDE.md`**

In **Layout**, in the `server/game/` bullet the skeleton plan wrote, replace "`CommandDispatcher` over the `IRulesEngine` seam (`InertEngine` until the scheduler and interpreter plans), the lobby and setup row writes. No rules yet." (ignoring line wraps) with "`CommandDispatcher` over the `IRulesEngine` seam, the lobby and setup row writes; `Properties/` (the pinned set, its authored `Content/*.properties.json` form, the store and the startup seeder), `Dice/` (the seeded counter-based roll primitive), `Effects/` (the effect grammar's two passes), `Interpreter/` (frames from the log, steps, windows, the battlefield reader), `Engine/` (`RulesEngine`, the one `IRulesEngine`; `IScheduler` is the scheduler's seam), `Rules/GrimdarkFuture/` (OPR's sequences — the only place a rule is named)."

In **Conventions**, add: "The engine never appends or broadcasts and holds no state between calls: a step reads its position, values and history from the log and returns one outcome. Dice are a function of the game id and the count of dice drawn in the effective log. Authored `Properties` are `server/game/Content/*.properties.json`, seeded by hash at startup; editing one makes a new set with a new hash — old games keep theirs."

- [ ] **Step 3: Run the whole check**

```bash
cd /home/cstupi/src/tabletop
docker compose up -d && dotnet build && dotnet test
find server tests -path '*/obj/*' -maxdepth 3 -type d | grep -v Unix
git status --short
```

Expected: build clean; every test project green (this plan adds 74 tests to `Tabletop.GameServer.Tests`); the `find` prints nothing.

- [ ] **Step 4: Commit: `Pin the interpreter's nine constraints and record the engine in CLAUDE.md`.**

---

## Interfaces → Produces

Later plans copy these names verbatim. `IRL<T>` abbreviates `IReadOnlyList<T>`, `IRD<K,V>` abbreviates `IReadOnlyDictionary<K,V>`.

### The shared engine and the scheduler's seam (`Tabletop.GameServer.Engine`)

| Type | Members |
| --- | --- |
| `sealed class RulesEngine(IGameRules, PropertySet, IDiceSource, IScheduler) : IRulesEngine` | **the one `IRulesEngine`**; `SequenceInterpreter Interpreter`; `Judge`/`Execute` for `RerollDice`, `SetDice`, `AddModifier` (everything else `Illegal` / `RefuseCommand(Unknown)`); `Advance(state)`: a framed Ask open → `[]`; stack non-empty → `Interpreter.Advance`; else → `IScheduler.Advance(state, Interpreter)` |
| `sealed class RulesEngineFactory(IPropertiesStore, IDiceSource, ISchedulerFactory) : IRulesEngineFactory` | registered in place of `InertEngineFactory`; loads the set by `game.PropertiesHash`; `PropertiesNotFoundException(string hash)`; calls `ISchedulerFactory.Create(rules, game, set)` |
| `interface IScheduler` | `IRL<EventPayload> Advance(GameState state, ISequenceStarter starter)` — **the scheduler plan implements this** (`SchedulerBase`, two shapes); called only between sequences with no framed Ask open; returns `[]` while an unframed Ask of its own is live |
| `interface ISequenceStarter` (namespace `Tabletop.GameServer.Interpreter`) | `IRL<EventPayload> Start(GameState, string sequence, SequenceContext)` → `[sequence_started]`; `SequenceInterpreter` implements it |
| `interface ISchedulerFactory` | `IScheduler Create(IGameRules, GameInfo, PropertySet)` — the scheduler plan registers `Scheduler.SchedulerFactory` in place of `NoSchedulerFactory` |
| `sealed class NoScheduler`, `sealed class NoSchedulerFactory` | the registration until then |

**How the scheduler's turn Ask goes through this machinery** (the scheduler plan's Tasks 3–5 are written against this). The scheduler emits `AskOpened(new Ask(id, AskQuestions.Activate, prompt, [team], Options(unit × action), AllowPass: true, Sealed: false, WindowKind: null, FrameId: null))` from `IScheduler.Advance`; the dispatcher (skeleton Task 6) logs `ask_answered`/`ask_closed`; the scheduler returns `[]` while it is open; on the next call it reads the `ask_closed` from the effective log, emits its own `ActionRecorded(unit, action)` and, when `rules.ActionSequence(action)` is non-null, `starter.Start(state, sequence, new SequenceContext(unit, [], [], action))`; the interpreter then owns `Advance` until `sequence_finished` for that frame; the scheduler sees it, starts `rules.EndOfTurnSequences(state, properties, turn)` the same way, and when those have finished emits `turn_ended` with `rules.OnTurnEnded(state, properties, turn)`'s changes. `round_started.Changes` = `rules.OnRoundStarted(state, properties, round)`. The roll-off is `starter.Start(state, sequence, new SequenceContext(null, [], [], null))` for the `sequence` in `FirstTeamDecision.RollOffNeeded` (`GfNames.RollOffSeq`), and its result is `Values["place_1"]..["place_n"]` (team ordinals, best first; `winner_ordinal` = `place_1`) on the `step_completed` named `roll-off` — the scheduler's `RollOff.Order` reads them and writes `round_started.Order`. A frame with no actor is never confirm-gated, so the roll-off finishes in one `Advance` under either wait setting (question 8). `Windows.ReactionOrder(state, actorTeam, reactingTeams)` is the one reaction-ordering policy: `IGameRules.OrderReactions` (scheduler plan) overrides it when non-null.

### `IGameRules` additions (`Tabletop.GameServer.Rules`)

| Member | Meaning |
| --- | --- |
| `Sequence Sequence(string name)` | throws `UnknownSequenceException` |
| `string? ActionSequence(string actionName)` | the sequence an `action` row starts (`Hold→hold`, `Advance→advance`, `Rush→rush`, `Charge→charge`); null for a name with none |
| `RollSpec RollSpec(string roll)`; `sealed record RollSpec(string Roll, RollConvention Convention, Func<LiveSet, Side, int?> Threshold)` | convention and threshold derivation, per roll |
| `string HitRoll` | the roll `ToHitPenalties` suppresses |
| `IRL<Reaction> ReactionsAt(GameState, PropertySet, string point, FrameView)`; `sealed record Reaction(Guid TeamId, string OptionId, string Label, string Sequence, SequenceContext Context)` | who may react at a window |
| `IRL<SequenceStart> EndOfTurnSequences(GameState, PropertySet, TurnContext turn)`; `sealed record SequenceStart(string Sequence, SequenceContext Context)` | OPR: morale tests for units at half or less that took wounds (`turn.Activation?.UnitId` is the actor). Merged with the scheduler plan's `EndOfTurn` |
| `IRL<StateChange> OnRoundStarted(GameState, PropertySet, int round)` | OPR: clear `fatigued`; `Caster(X)` tokens capped at 6; round 0 → `[]`. Merged with the scheduler plan's `RoundStartChanges` |
| `IRL<StateChange> OnTurnEnded(GameState, PropertySet, TurnContext turn)` | OPR: `WoundsThisActivation` to 0; an activation spent idle clears `shaken` — an `Idle` activation, and an `Aircraft`'s mandatory `Advance`, which p.13 says does not break the idling requirement. Merged with the scheduler plan's `TurnEndChanges` |
| `GameState.RngSeed : ulong` (added to the skeleton's `GameState`) | SHA-256 of the game id, first eight bytes |
| `GrimdarkFutureRules` (this plan's partial, `Rules/GrimdarkFuture/GfRules.Sequences.cs`; identity in the skeleton's `GfRules.cs`) | `OneTryPerSpellScope = "activation"`, `RecomputeAttacksPerGroup = true`, every member above |
| `GfNames` also carries the scheduler plan's names | `Deployment`, `Scouts`, `Rounds`, `Deploy`, `Scout`, `Ambush`, `Immobile`, `Aircraft`, `Caster`, `Tough`, `Transport`, `SetAside` — one names class |

### `Tabletop.GameServer.Properties`

`StatLevel`, `StatDefinition`, `StatRef`, `ActionDefinition`, `RollDefinition`, `CatalogKind`, `Aggregation`, `TargetSide`, `TargetGrain`, `SpellTargeting`, `CatalogEntry`, `EffectSubject`, `EffectKind`, `TargetQuantity`, `TargetResult`, `TargetModifier`, `CapKind`, `EffectDuration`, `Effect` (+ `EffectiveAmount`), `ConditionKind`, `Comparison`, `EffectCondition` (+ `Compare`), `PropertySet` (`ContentHash`, `Name`, `GameRules`, `Stats`, `Actions`, `Rolls`, `Entries`, `ByName`, `Entry`, `Stat`, `Action`, `HasRoll`, `AllEffects`); `PropertySetDocument`, `EntryDocument`, `EffectDocument`, `ConditionDocument`; `PropertiesJson` (`Parse`, `Canonical`, `Hash`, `Validate`, `ToSet`); `IPropertiesStore` (`LoadAsync`, `SeedAsync`), `InMemoryPropertiesStore`, `PostgresPropertiesStore`, `PropertiesSeeder` (`EmbeddedDocuments`, `SeedEmbeddedAsync`). Embedded resource `Tabletop.GameServer.Content.grimdark-future-core.properties.json`; stat names `Quality`, `Defense`, `Range`, `Attacks`.

### `Tabletop.GameServer.Dice`

`IDiceSource` (`Draw(seed, index, sides)`), `DiceSource`, `ScriptedDice`, `DiceCursor.DrawnSoFar(GameState)`, `RollConvention(int Sides, int? ModifierCap)`, `RollPlan(Roll, Threshold, Modifiers, Convention, RequiredRaw, RerollRaw)` (+ `NetModifier`), `RollResult`, `Roller(IDiceSource, ulong seed, long cursor)` (`Roll`, `Judge`, `Draw`, `Cursor`), `RollOutcome` (`Successes`, `Failures`, `CountRaw`, `Reroll`).

### `Tabletop.GameServer.Effects`

`AttackKind`, `Side`, `Party` (`Whole`, `Of`), `Interaction` (`Of`, `Other`, `Round`), `DieContext`, `Conditions` (`Hold`, `Holds`), `LiveProperty` (`ActedOn`), `LiveSet` (`On`, `Has`, `Parameter`, `Effects`, `WithGranted`, `SuppressedModifiers`, `Applied`), `LiveProperties.Settle`, `StatReader` (`TargetNumber`, `WeaponRange`, `Attacks`, `ActionDistance`), `DieEffects`, `RollEffects` (`Plan`, `PerDie`, `HitMultiplier`, `WoundMultiplier`, `OwnRolls`, `ConsumeGrants`), `Lookup` (`LivingModels`, `AliveModels`, `ContainerOf`, `OccupantUnits`, `UnitProperties`, `ModelProperties`, `WeaponProperties`, `UnitHas`, `UnitParameter`, `ModelParameter`) — name checks that read the grants store, for the scheduler's action-legality hook (`Immobile`, `Shaken`).

### `Tabletop.GameServer.Interpreter`

`IStep`, `ISequenceStarter`, `Sequence`, `StepOutcome` (`Complete(Changes, Values, Repeat)`, `Finish`, `OpenAsks`, `Push`), `AskRecord`, `ChildRecord`, `StepHistory`, `FrameView`, `FrameReader.Read`, `StepContext` (`State`, `Properties`, `Rules`, `Frame`, `History`, `Projection`, `ActorTeam`, `TeamOf`, `Roll`, `Rolls`, `ForcedRerolls`, `Plan`, `Ask`, `SealedAsk`), `SequenceInterpreter` (`Advance`, `Start`) : `ISequenceStarter`, `Teams` (`Of`, `Others`), `Windows` (`ReactionOrder`, `Run`, `SealedBid`, `Bids`), `Battlefield` (`Placed`, `At`, `InsideReach`, `Terrain`, `Blockers`, `CanSee`, `Distance`, `Vertical`, `UnitDistance`, `InCover`, `TerrainUnder`, `Settle`).

### `Tabletop.GameServer.Rules.GrimdarkFuture`

`GfNames` (every OPR name), `GfRolls` (`Spec`, `HitPlan`, `DefensePlan`, `MoralePlan`, `CastingPlan`, `RollOffSequence`), `GfHooks` (`OnRoundStarted`, `OnTurnEnded`, `EndOfTurnSequences`, `UnitsToTestAtEndOfActivation`, `AtHalfOrLess`, `StartingSize`, `FoughtMeleeThisActivation`, `ReactionsAt`), `GfAttack` (`DealWounds`, `ResolveHits`, `AttackSequence`, `AttackInteraction`, `WoundsToDeal`, `EligibleForNextWound`, `KindOf`, `EligibleModels`, `HitsFrom`, `PushStep`), `GfShooting` (`Shooting`, `Groups`, `ValidTargets`, `WeaponGroup`), `GfMoves` (`Hold`, `Advance`, `Rush`, `DangerousTerrain`, `MoveStep`, `ShootOrNotStep`, `CastingWindowStep`, `ActivationTerrainStep`, `DangerousTestStep`, `DangerousDice`, `StandingInDangerous`, `IsContainer`, `ContainerMovesThisActivation`, `ContainersInReach`, `ExitPlacement`), `GfMelee` (`Charge`, `Melee`, `MeleeWeapons`, `WoundsCaused`), `GfMorale` (`MoraleTest`), `GfCasting` (`Casting`, `SpellHits`, `Casters`, `TriedThisActivation`, `BidCapacity`, `SpellTargets`), `GfContainers` (`TransportDestroyed`, `Wrecks`). Sequence names: `hold`, `advance`, `rush`, `charge`, `shooting`, `melee`, `attack-sequence`, `resolve-hits`, `deal-wounds`, `morale-test`, `dangerous-terrain`, `transport-destroyed`, `casting`, `spell-hits`, `roll-off`. Ask questions a client may see: `move`, `shoot_or_not`, `declare_targets`, `remove_casualties`, `charge_target`, `strike_back`, `disembark`, `cast`, `spell_targets`, plus the engine's `confirm`, `react`, `bid`.

### Test infrastructure (`tests/Tabletop.GameServer.Tests/Infrastructure`)

`TestProperties` (`Core`, `CoreDocument`, `WithSpells`), `Armies` (`Unit`, `Gun`, `Melee`, `P`, `State(team1, team2, x1, x2, enforcement, waits, terrain)`, `P2`), `Sim` (`Gf`, `Append`, `Run`, `Start`, `AskFor`, `AskForOrNull`, `Dispatch`, `Answer`, `Steps`, `Unit`, `Model`, `Alive`), `StubRules` (a ruleset made of a test's sequences; implements the whole merged `IGameRules`, the scheduler's members inert), `LambdaStep`; `PostgresFixture.DeletePropertiesAsync`. Two fixture families share the folder on purpose: `Armies`/`Sim` build a two-team playing game around a sequence; the scheduler plan's `TestArmies`/`TestGames`/`SchedulerHarness` build N-team games around turns — `SchedulerHarness` wraps a `Sim` with the real `RulesEngine`, `TestProperties.Core` and a queue-fed `IDiceSource`, and `FakeRules` (its `IGameRules` double: Grimdark Future with settable hooks and empty action sequences) is the one to reach for when a test needs turns around a sequence.

---

## Spec corrections proposed

1. **`docs/architecture.md` → Rules as data**, the sentence *"How authored content reaches those tables — in-repo files loaded as rows, or the Army Forge import spell effects left undesigned — is the one part of this section no design doc has touched."* Replace with: *"Authored content is a JSON document per set under `server/game/Content/*.properties.json` — the eight tables' shape without ids — content-addressed by SHA-256 over a canonical serialisation (`sha256:…`) and seeded as rows by the game server at startup, once per hash, idempotently. Editing a document yields a new hash and a new set; old games keep the set they pinned. `GET /v1/properties` lists what has been seeded. Effect and condition ids are minted at seeding and are stable thereafter within a database. The Army Forge spell import is a later document writer for the same format."* Reason: the interpreter plan needed a mechanism and this is the minimal one (Tasks 2–4; question 1).
2. **`docs/schema.md` → A condition is a row, and its values are rows.** Add: *"A `roll_result` condition's `condition_value` rows are exactly two — the roll it reads (`hit`, `defense`, …) and `raw` or `modified` — and `number` is the face compared. No roll may be named `raw` or `modified`."* Reason: the effect grammar says a roll carries both values and the schema gave the predicate no way to say which roll or which value.
3. **`docs/superpowers/specs/2026-08-29-effect-grammar-design.md` → `Roll` nests.** Add: *"A property's own roll (kind 5) is recorded as a `RollRecord` whose name is the entry's — `Regeneration`, `Impact`, `Fearless` — with the effect row's threshold. It is not a `roll` row because nothing targets it; `AddModifier` therefore cannot name it."*
4. **`docs/superpowers/specs/2026-09-03-action-sequences-design.md` → The resolution stack**, after *"`step_completed` — the step's name, plus everything it produced: choices made, rolls, state changes applied"*. Add: *"and `values`, a flat map of named integers holding the step's settled parameters — attacks counted, a hit multiplier once capped, extra hits, a distance used, a bid, who lost — so that a later step or a nested frame reads them without re-deriving, and so a reroll between steps changes what the next step reads (it reads the record) without changing what was settled (it reads the value). There is no schema per step kind."* And strike the *Still open* item *"Whether `step_completed` payloads need a schema per step kind"* with *"Settled 2026-09-05 by the interpreter plan: a flat `values` map, no per-kind schema."*
5. **`docs/superpowers/specs/2026-09-03-action-sequences-design.md` → A step ends one of four ways.** Add: *"A step may also complete and run again — the same step, a fresh history, the same position — which is how a move takes a second segment after a mid-move cast. In the log this is a second `step_completed` with the same step name; a sequence never lists one name twice in a row."*
6. **`docs/superpowers/specs/2026-09-03-action-sequences-design.md` → Windows**, the sealed variant. Add: *"One sealed Ask is addressed to every eligible team; its `Bid` legal set carries one maximum — the largest team's — and the casting step records each team's own maximum in its values and clamps the revealed bids to it."*
7. **`docs/superpowers/specs/2026-09-03-action-sequences-design.md` → One roll primitive.** Add: *"The RNG is a counter-based generator: the seed is derived from the game id and the cursor is the number of dice drawn in the effective log, so nothing about the RNG is stored and an undo rewinds the dice with the log. A forced reroll (`Poison`) is a `dice_rerolled` payload following the `step_completed`, so the record shows the replaced die and the cursor counts the extra draw."*
8. **`docs/superpowers/specs/2026-09-04-spell-effects-design.md` → The grants store.** Add: *"A grant on a model-grain target lands on the model's unit — the store is the unit's. A grant's parameter (`gets AP(1)`) is the granting effect's `amount`, read from the pinned set by `effect_id`; the grant row itself carries none."* And to **Duration**, after *"always with one duration: **once**"*: *"That is a closed reading of the whole corpus, so it is validated rather than assumed: a `spell` entry's top-level effect must be `add_hit` (a damage spell) or carry `duration = once` (a grant). Anything else would be applied nowhere, and the authored-document validator refuses it."*
9. **`docs/superpowers/plans/2026-09-05-game-server-skeleton.md` → Interfaces needed → Scheduler and interpreter plans.** Applied 2026-09-05 (the skeleton plan now says it): the scheduler's seam is `Tabletop.GameServer.Engine.IScheduler` (interpreter plan, Task 10), implemented by the scheduler plan and registered through `ISchedulerFactory` in place of `NoSchedulerFactory`.
10. **`docs/superpowers/specs/2026-08-28-game-flow-design.md` → Inside a container**, the sentence *"Several things fall out rather than needing rules. An occupant has no position, so it has no line of sight in or out — which is what the rulebook says."* Add: *"The rulebook says one more thing than that: units inside **can** target other units inside, and the container itself (p.14). Having no position gives the first half for free and gets the second half wrong, so containment is asked before any measurement — `Battlefield.InsideReach` — and answers three ways: no, for one unit inside and one out; yes with nothing to measure, for two units in one container or an occupant and its container; and 'measure it' for two units on the table."* Reason: a filter on *has a position* silently forbids the inside-to-inside case the rulebook allows (the coverage gap of 2026-09-06).
11. **`docs/superpowers/specs/2026-08-28-game-flow-design.md` → Inside a container**, after *"Capacity is not enforced."* Add: *"Two readings of a unit's models follow from this being a third place rather than the shelf, and both are needed: the models with a **position** (every distance, sight line, cover and blocker question) and the models still **in play** (how many the unit has, who may be assigned a wound, whether it is destroyed, whether it is at half or less). A transported model is in the second and not the first."* Reason: the section says a transported model is *"very much in play"* but the plans had one predicate doing both jobs, which made a carried unit read as destroyed and left its occupants unable to be wounded when their transport was.
12. **`docs/superpowers/specs/2026-09-03-action-sequences-design.md` → Grimdark Future's sequences → Dangerous terrain.** Current: *"Invoked by moves through it, activating in it, and transport destruction."* Add: *"All three write `dangerous_dice` on the invoking frame and push `dangerous-terrain`, so the test itself is one step and only the affected models are rolled for. Transport destruction is its own sequence, `transport-destroyed`, one frame per occupant unit, pushed by `deal-wounds`' `disgorge` step: the container is destroyed but stays on the table until every occupant has tested, been Shaken and been placed, because p.14 places them *before removal*."*

## Interfaces needed from other modules

1. **Contracts plan (`Tabletop.Protocol`) — applied 2026-09-05 by the compatibility review.** `StepCompleted` gains a non-positional init property `IReadOnlyDictionary<string, int>? Values { get; init; }` (serialised as `values`, omitted when null), and `CompletedStep` in `Projection.cs` gains `IReadOnlyDictionary<string, int>? Values` copied by `EventApplier.ApplyTo`. Assumed exact shape: `new StepCompleted(frameId, step, answer, rolls, changes) { Values = … }`. `FrameReader` reads values from the effective log, so only the event needs it; the projection copy is for the client's log panel.
2. **Geometry plan (`Tabletop.Geometry`)** — consumed as its Produces table states: `Distance.EdgeToEdge`, `Distance.Vertical`, `LineOfSight.Check`, `Cover.InsideCoverVolumes`, `Cover.SightBlockersBetween`, `Containment.VolumesContaining`, `Settling.SurfaceHeightAt(Point3, …)`, `Bodies.AsBlockingVolume`, `Movement.Check(PlacedModel, Point2, double, IRL<WorldVolume> impassable, IRL<WorldVolume> difficult, double? cap, TableSize?)`, `Movement.VolumesCrossed(PlacedModel, Point2, IRL<WorldVolume>)`, `Containment.BaseFullyWithin(PlacedModel model, PlacedModel container, double inches)` (added by the geometry plan 2026-09-06 — p.14's *"fully within 6\" of it"*, which `GfMoves.ExitPlacement` is the only caller of). The last two discharge the obligation the geometry plan states of this one: a dangerous terrain test is owed for `Movement.VolumesCrossed(start, destination, dangerousVolumes)` rather than for the landing position, since p.11 says *"moves in or through"*, and `Containment.VolumesContaining` is *"is activated in it"* (Task 14). `MoveRegion` carries six parameters (contracts plan Task 8; the fifth applied 2026-09-05, the sixth 2026-09-06): `MoveStep` passes the difficult volume ids, `GfNames.DifficultCap` (`null` for `Strider`/`Flying`) and `MoveStep.Containers(…)`; the charge close-in, fall-back and consolidation Asks pass `[]`, `null` and nothing, since no terrain parameter and no container binds them. `MoveContainment(IRL<Guid> Enterable, Guid? ExitingId, double? ExitWithin)` is the sixth parameter's type.
3. **Skeleton (`server/game/`)** — settled: the skeleton's Task 6 already declares `GrimdarkFutureRules` as `sealed partial` in `Rules/GrimdarkFuture/GfRules.cs`, so nothing moves; this plan adds `GfRules.Sequences.cs`. This plan itself modifies three skeleton files, each named in the task that does it: `ConnectionHandshake.RunAsync`'s hydrate catch gains `Engine.PropertiesNotFoundException` (Task 10, close `1011`); `PostgresFixture` gains `DeletePropertiesAsync(string hash)` (Task 4); `Program.cs` registrations (Tasks 4 and 10).
4. **Scheduler plan** — reconciled 2026-09-05: implements `Engine.IScheduler` and `Engine.ISchedulerFactory` (`Create(IGameRules, GameInfo, PropertySet)`) and registers `Scheduler.SchedulerFactory` in place of `NoSchedulerFactory`; adds its own `IGameRules` members (`PhasesBeforeRounds`, `PhasesInTurn`, `FirstTeam`, `LegalTurn`, `ReadAnswer`, `ActivationComplete`, `RoundBoundaryStep`, `OrderReactions`) beside this plan's, and its former `EndOfTurn`/`RoundStartChanges`/`TurnEndChanges` are this plan's `EndOfTurnSequences`/`OnRoundStarted`/`OnTurnEnded` with a `TurnContext` parameter; consumes `ISequenceStarter.Start`, `GfRolls.RollOffSequence` by the name `FirstTeamDecision.RollOffNeeded(GfNames.RollOffSeq)` carries and reads `Values["place_k"]` back, `PropertySet.Actions` for the turn Ask's action rows, `Lookup.UnitHas` for `Immobile`/`Aircraft` legality, `GfNames` (one names class; its `GrimdarkFutureNames` is gone); writes `ActionRecorded` when it starts an action; its `OrderReactions` hook is what `Windows.ReactionOrder` consults — the scheduler plan defines no ordering class of its own. Its `SchedulerHarness` runs the real `RulesEngine` over `TestProperties.Core`.
5. **Management plan** — nothing new: `GET /v1/properties` already lists the seeded set; `PropertiesSummary.GameRules = "grimdark-future"` matches `GrimdarkFutureRules.RulesId`. The import's stat names `Quality`, `Defense`, `Range`, `Attacks` match `Content/grimdark-future-core.properties.json`.
6. **Client shell plan** — renders the ruleset questions listed in Produces (`move`, `shoot_or_not`, `declare_targets`, `remove_casualties`, `charge_target`, `strike_back`, `disembark`, `cast`, `spell_targets`) from their legal-set shapes; the `Ask.Prompt` is display text; `WindowKind` values a player may mute: `before-attacking`, `casting`. Containment adds three things to the `Move` rendering of a `move_region` Ask (contracts plan Task 8, `MoveRegion.Containment`): a listed `Enterable` container is a click target whose answer is a `Destinations` with `To = Inside(container)` for **every** model of the unit, not just the one that reached it; a non-null `ExitingId` means the region is `Containment.BaseFullyWithin` about that container rather than `Movement.Region`, and the models being dragged out have no start position to drag from — their ghosts start on the container; and `Allowance` is not the constraint on a disembark, so the ruler's remaining-allowance readout does not apply. An occupant is not drawn on the table at all: `Location.Inside` has no `Placement`.

## Questions for the project owner

1. **Who seeds `Properties`, and when?** Assumed: the game server, at startup, from documents embedded in its build, idempotently by hash (Task 4). If management should own it (it owns the schema), `PropertiesSeeder` and `PostgresPropertiesStore.SeedAsync` move to `server/management/` behind an admin endpoint or a CLI verb, and the game server keeps only `LoadAsync`; if a CLI is preferred over startup, `Program.cs` gains a `seed-properties` argument and the startup call goes.
2. **"One try per spell" — activation or round?** Assumed the activation (`GrimdarkFutureRules.OneTryPerSpellScope = "activation"`; `TriedThisActivation` scans from the activation's `sequence_started`). If the round, the scan starts at the last `round_started` and the constant reads `"round"`; nothing else changes.
3. **Are a later weapon group's attacks recomputed after an earlier group's casualties?** Assumed yes (`RecomputeAttacksPerGroup = true`: `determine-attacks` counts the target's living models when its group starts). If no, `fire-groups` records each group's attack count in its values before pushing and `determine-attacks` reads it; the rulebook's "all weapons are fired simultaneously" then wins over its group order.
4. **Catalog kinds and aggregation for the core rules the specs do not fix** (Task 3). Settled already and not in question: `Stealth` is `all` and `Tough` is `none` ([identity and enumerations](../specs/2026-08-29-identity-and-enumerations-design.md) → The Stealth case), and `Fearless` is `majority` because its own text says *"a unit where most models have this rule"*. Assumed for the rest: `Fast`/`Slow`/`Flying`/`Strider`/`Immobile`/`Furious`/`Relentless` as `model` with `all`; **`Entrenched` `any`**, which is the loosest of the readings and the one worth confirming — the rulebook says *"models with this special rule"* where `Stealth` says *"units where all models have this rule"*, so `all` is the alternative and `none` is not available (a per-model instance would add its −2 once per entrenched model to a unit-pooled hit roll); `Regeneration`/`Impact`/`Caster`/`Hero`/`Fear`/`Transport` `none`; `Aircraft`/`Ambush`/`Scout` `unit`. Army Forge lists most of these as unit rules; the management plan's import puts a name on every slot when the catalog says `model`, so the choice only changes where the import writes them, not what the engine reads. If any should change, the document changes and the hash with it.
5. **May the generic `Effects/` know the two weapon stat names?** `StatReader` reads `Range` and `Attacks` because `StatKind.Distance`/`Count` need a *which* (Task 7; Task 18 whitelists them). Assumed acceptable while OPR is the only ruleset. If not, `IGameRules` gains `StatRef RangeStat` and `StatRef AttacksStat` and `StatReader` takes them.
6. **One sealed `Bid` legal set for every team.** Assumed the largest team maximum in the Ask and per-team clamping in the step (Task 9; spec correction 6). If each team must see its own maximum, `Bid` needs a per-team map — a Protocol change — or one sealed Ask per team with the engine holding all answers until the last closes, which the dispatcher does not do today.
7. **`Sniper`'s per-model target.** Assumed not modelled: a Sniper group resolves first at Quality 2+ against the target unit (Task 14). Modelling it needs a model-grain `PieceSelection` per Sniper model and `AttackInteraction` over one model as a unit of 1 — the same path a model-grain spell takes — about a task of work.
8. **Confirm gates and the actor.** Assumed the `Confirm` Ask goes to the acting unit's team and a frame with no actor (the roll-off) is never gated (Task 8). If every team should confirm, the gate becomes one Ask per team — the window mechanism — and waits for all. A consequence the scheduler plan records as its question 7: the roll-off, a one-step actor-less sequence, finishes in the batch that started it under either wait setting, so its dice are never editable from the menu (`RerollDice`/`SetDice` need the frame on the stack) and undo re-rolls the same dice (the RNG is counter-based). If the owner wants the roll-off's dice editable, the gate for actor-less frames becomes a `Confirm` to every team and `SequenceInterpreter.Advance` gates position 0 as well for such frames — about ten lines here and the scheduler plan's Task 3 reading the order only after the gate closes.
9. **Can a unit embark into an enemy's transport, or a unit inside one target its own container?** The rulebook's containment text is written for the same-army case: p.20 says a solid building's occupants *"must be from the same army"*, and a `Transport` carries its owner's units. Assumed the narrow reading for the offer and the faithful one for the predicate: `ContainersInReach` lists **friendly** containers only, so no automated move puts a unit inside an enemy vehicle, while `Battlefield.InsideReach` implements p.14 as written and would let an occupant target a fellow occupant or the container if a right-click menu move ever put them there. That leaves the clause latent — `ValidTargets` only offers enemy units, so in an ordinary game an occupant has nothing to shoot at. If the owner wants a captured building, or two armies inside one container, nothing in the engine has to change; if the owner wants it forbidden, `InsideReach` returns false whenever the two units are on the same team and the clause disappears.
10. **May a caster inside a container cast?** Assumed yes, at what is in there with it: `GfHooks.ReactionsAt` reads `Lookup.AliveModels`, so the window opens, and `GfCasting.SpellTargets` goes through `Battlefield.InsideReach`. But `BidCapacity` stays positional on both ends — p.13 needs *"line of sight of the caster's unit"* and p.14 denies an occupant any — so such a cast draws no bids in either direction and resolves at a flat 4+. If a cast from inside should be impossible instead, `ReactionsAt` and `GfCasting.Casters` go back to `LivingModels` and two lines disappear. One edge stays open either way: `SpellTargets`' **model-grain** branch measures per model and an occupant has no position, so a model-grain spell cast inside a container finds nothing to hit. Closing it means the same containment branch a second time, which is not worth writing until a model-grain spell exists.
11. **Where does the disembark radius come from, and is it measured edge to edge?** Assumed `GfNames.ExitWithin = 6` for every container, and measured the way every other distance here is — edge to edge from the container's base, so the region is its footprint grown by 6" rather than a circle about its centre ([the table domain model](../specs/2026-08-27-table-domain-model-design.md) → *"Distance is edge to edge between base shapes, never centre to centre"*). A big vehicle therefore disembarks further from its hull centre than a small one, which is what measuring from the hull means. Geometry's `Containment.BaseFullyWithin` implements exactly that, and it is the only place the number is applied.
12. **A survivor with nowhere legal to stand.** p.14 says the survivors *"must be placed fully within 6" of the transport"* and does not say what happens when they cannot be. Assumed shelved: `PlaceSurvivorsStep` places every survivor the answer put inside the radius and shelves the rest, because the container is removed in the same step and `Inside` would then dangle. The alternative — leaving them inside a removed container, or auto-placing them — needs a rule the rulebook does not give.
