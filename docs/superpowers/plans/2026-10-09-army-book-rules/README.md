# Army-book rules: foundation and batch 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every army-book rule name reaches the catalog: a `played: false` text-only entry, a generated Aura/Buff/Mark
wrapper, or a hand-authored entry. This plan builds the foundation (the played flag, the generator, pattern coverage,
the pick mechanism, the four pattern games) and authors batch 1, the rules the current effect grammar expresses.

**Architecture:** `CatalogEntry.Played` is false only on generated text-only entries; `LiveProperties.Gather` skips
them and the client tags them "not automated". A pick entry is a `model` entry with `targeting` whose rows are `once`
rows at `before-attacking`; `GfHooks.ReactionsAt` offers a `use` reaction per carrying model, and the `pick-rule`
sequence writes one grant per row on the picked unit, spent by the existing grant consumption. `scripts/ArmyBooks`
writes `grimdark-future-army-book-rules.part.json`; the rules suite counts a generated wrapper as covered by its
pattern's game. Batch 1 entries go in `grimdark-future-army-rules.part.json`, each with a suite game and an exact check.

**Tech Stack:** `net10.0`, C# 12, xUnit, ASP.NET Core minimal APIs, Npgsql/Dapper, DbUp migrations, Godot 4.7.2 Mono
(one label change).

**Spec:** [`docs/superpowers/specs/2026-10-09-army-book-rules-design.md`](../../specs/2026-10-09-army-book-rules-design.md):
*Played flag*, *Generator*, *Coverage*, *The four patterns*, and *Authored rules* item 1 (Grammar only).

**Requires landed:** branch `integrate-fixes` at `f8866ac`.

## Global Constraints

- The rules engine lives only in `server/game/`; a rule is named only in `server/game/Rules/GrimdarkFuture/` and the
  content files. `sim/` holds no second rules engine: checks compare the log with literals from the rule text.
- C# only. Test-driven: every behaviour change starts with a failing xUnit test under `tests/`.
- Every hand-authored entry carries its Army Forge `specialRules[].description` verbatim as `ruleText`
  (`.superpowers/armyforge/books/*.json`). Every exact check's name quotes that text.
- A model rule worded *"This model …"* is `aggregation: all`, as `Bane when Shooting`, `Slayer` and `Piercing Hunter`
  are; *"This model and its unit get …"* is `aggregation: any`; *"units where all models have this rule"* is `all`.
- Canonical forms of existing entries do not change: new JSON fields are null-omitted (`played`, `lineOfSight`).
- Schema changes are management migrations (`server/management/Persistence/Migrations/`), with `docs/schema.md`.
- `grimdark-future-army-book-rules.part.json` is never edited by hand: fix `scripts/ArmyBooks` and run
  `./scripts/army-books generate`.
- A text-only entry's kind is the kind the authored entry for that name will have: `weapon` for a name the books
  print only on weapons, `model` otherwise. `PostgresPropertiesStore.SeedAsync` refuses a name whose kind differs
  between two sets of one game rules.
- New suite game lists in `ArmyGames` partial files are expression-bodied properties (`=> [...]`), never field
  initializers: field initializers in different partial files run in an undefined order.
- No file grows past ~500 lines; new games go in new `SuiteCatalog.Army.*.cs` files.
- Tests: `dotnet test <project>`; game-server, management and socket-driver runs need `docker compose up -d`.
- The suite ends at `PASSED` with full coverage on both drivers (Task 15).
- Commit after every task with an imperative one-line subject; the executing session adds its attribution trailer.

## Review Focus

1. **A pick reaction offered with no eligible target.** The window step repeats after a child; a `use` reaction whose
   `rule_target` Ask cannot open would finish, count nothing and be offered again forever. Offers are computed with
   the same `GfCasting.Targets` reading the step uses (Task 4 test `No_use_is_offered_when_no_unit_is_in_range`).
2. **A carried pick entry acting on its carrier.** Its `grant_property` rows are `once` rows meant for the picked
   unit; read as the carrier's own rows they would give the carrier X at every point. A carried instance of an entry
   with `targeting` exposes no rows (Task 3 test `A_carried_pick_entry_exposes_no_rows`).
3. **A granted row never holding because of its own `at_point`.** The grant row keeps its `at_point before-attacking`
   condition; read on the picked unit during an attack it must hold (Task 3 test
   `A_granted_rows_at_point_holds_wherever_the_grant_is_read`).
4. **An Aura of a rule read by name.** `Scout Aura` and `Ambush Aura` grant rules the scheduler reads with
   `Lookup.UnitHas`, which reads no carried grant; the generated wrapper would do nothing while coverage calls it
   covered (Task 5 test `A_carried_aura_gives_its_unit_a_rule_read_by_name`).
5. **A generated text-only entry whose kind later changes.** Authoring `Ignores Cover` as `weapon` after a seeded set
   held it as `model` breaks seeding on every existing database (Task 6b test
   `A_name_printed_only_on_weapons_is_a_weapon_entry`).

## File structure

| File | Responsibility | Task |
| --- | --- | --- |
| `server/game/Properties/PropertySet.cs` | `CatalogEntry.Played`, `SpellTargeting.LineOfSight` | 1, 3 |
| `server/game/Properties/PropertySetDocument.cs` | `EntryDocument.Played`, parse normalisation, validation of played and pick entries | 1, 3 |
| `server/game/Properties/PostgresPropertiesStore.cs` | `played`, `target_line_of_sight` columns | 1, 3 |
| `server/game/Properties/PropertiesSeeder.cs` | `GeneratedPart` name (6b), `EmbeddedPart(name)` (7) | 6b, 7 |
| `server/management/Persistence/Migrations/008_catalog_played.sql`, `009_target_line_of_sight.sql` | schema | 1, 3 |
| `server/game/Effects/LiveProperties.cs` | Gather skips unplayed; carried pick entry exposes no rows | 1, 3 |
| `server/game/Effects/Conditions.cs` | a grant row's `at_point` holds | 3 |
| `server/game/Effects/Lookup.cs` | `UnitHas` reads a carried Aura's grant | 5 |
| `server/game/Effects/StatReader.cs` | a removed distance row is read (Swift) | 12 |
| `server/game/Rules/GrimdarkFuture/GfPick.cs` (new) | offers, usage, the `pick-rule` sequence | 4 |
| `server/game/Rules/GrimdarkFuture/{GfHooks,GfCasting,GfNames,GfRules.Sequences}.cs` | `use` reaction, `Targets`, names, registration | 4 |
| `server/management/Games/GameRepository.cs` | `played` in rule texts | 2 |
| `client/src/Core/{ManagementDtos,RuleTexts}.cs`, `client/src/Hud/StatSheet.cs` | played delivery and tag | 2 |
| `scripts/ArmyBooks/{ArmyForgeBook,RuleTemplates,RuleWriter}.cs` | reading the books' rules, the templates, names in scope | 6a |
| `scripts/ArmyBooks/{RuleWriter,BookWriter,Program}.cs` | writing the rules part, `generate` | 6b |
| `sim/Suite/{Coverage,SuiteReport,SuiteRunner,TestGame}.cs` | not automated, covered by pattern | 7 |
| `sim/Suite/SuiteCatalog.Army.cs` | `Game` options, the `Army()` list | 8–14 |
| `sim/Suite/SuiteCatalog.Army.Patterns.cs`, `.Patterns.Checks.cs` (new) | the pattern games, log placements | 8, 8b |
| `sim/Suite/SuiteCatalog.Army.Batch1.Checks.cs` (new) | batch 1 check helpers | 9–14 |
| `sim/Suite/SuiteCatalog.Army.{Grants,HitAndAp,Defense,Movement,Auras,Unbatched}.cs` (new) | batch 1 games | 9–14b |
| `server/game/Content/grimdark-future-army-rules.part.json` | hand-authored entries | 8–14b |
| `server/game/Content/grimdark-future-army-book-rules.part.json` (generated) | wrappers and text-only entries | 8, 15 |

## Fixed interfaces

Every task codes against these names. A task that needs a change here stops and reports it.

```csharp
// server/game/Properties/PropertySet.cs (Tasks 1, 3)
public sealed record SpellTargeting(TargetSide Side, TargetGrain Grain, int Count, double Range, bool? LineOfSight = null);   // null: in sight (p.13)
public sealed record CatalogEntry(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction,
    string RuleText, SpellTargeting? Targeting, IReadOnlyList<Effect> Effects, bool Played = true);
// server/game/Properties/PropertySetDocument.cs (Task 1)
public sealed record EntryDocument(string Name, CatalogKind Kind, bool TakesParameter, Aggregation? Aggregation, int? Cost, string? Faction, string RuleText,
    SpellTargeting? Targeting, IReadOnlyList<EffectDocument> Effects, bool? Played = null);   // null: played; Parse turns true into null
// server/game/Properties/PropertiesSeeder.cs (GeneratedPart: Task 6b; EmbeddedPart: Task 7)
public const string GeneratedPart = "grimdark-future-army-book-rules.part.json";
public static PropertySetDocument? EmbeddedPart(string fileName);
// server/game/Rules/GrimdarkFuture/GfPick.cs (Task 4)
public static class GfPick {
    public static string Key(string entry, Guid model);                       // "<entry>|<model:N>", the reaction option id and the frame's Action
    public static (string Entry, Guid Model)? Parse(string? key);
    public static IReadOnlyList<(CatalogEntry Entry, Guid Model)> Offers(GameState state, PropertySet props, Guid unit);
    public static int Uses(GameState state, Guid unit, string key);           // completed picks this activation
    public static Sequence PickRule(); }                                     // steps: "rule-target"
// GfNames (Task 4)
public const string PickRuleSeq = "pick-rule", RuleTarget = "rule_target", RuleTargetStep = "rule-target";
// GfCasting (Task 4)
public static IReadOnlyList<Guid> Targets(GameState state, Guid casterModel, SpellTargeting t);
// sim/Suite (Task 7)
public enum CoverageState { Own, Pattern, NotAutomated }
public sealed record CoverageEntry(string Name, string Kind, IReadOnlyList<string> Games, bool Fired, IReadOnlyList<ExactResult> Exact,
    CoverageState State = CoverageState.Own) { public bool Uncovered { get; } public bool Counts { get; } }
public static IReadOnlyList<CoverageEntry> Of(PropertySet set, IEnumerable<SuiteGameRun> runs, bool fullRun,
    IReadOnlySet<string>? generated = null, IReadOnlySet<string>? passed = null);   // Coverage
public string? Pattern { get; init; }                                        // TestGame: "Aura", "Buff", "Mark" or "Debuff"
// sim/Suite/SuiteCatalog.Army*.cs, ArmyGames (Tasks 8, 9, 12, 13)
static TestGame Game(..., IReadOnlyList<ScenarioModel>? carriers = null, int gunRange = 60, double? length = null, Action<ScriptBuilder>? more = null);
sealed record PatternLog(...) { public static PatternLog For(SuiteGameRun run); }   // per attack: the target's grants and every placement; rule_target Asks
static double UnitApart(SuiteGameRun run, IReadOnlyDictionary<Guid, Placement> at, Guid a, Guid b);
static Occasion Within(Occasion occasion, double inches, bool within = true);
static Occasion Carrying(string entry, When when = When.Attacking);
static GameCheck AuraModifier(string wrapper, string source, string roll, int amount, string quote);
static GameCheck Once(string entry, string quote, Func<LoggedAttack, bool?> shows);
static GameCheck PickWithin(string entry, double inches, bool friendly, string quote);
static GameCheck RushShotMarked(string entry, string quote);                                // Task 8b
static GameCheck NoCover(string entry, Occasion occasion, string quote);
static GameCheck NoPenalty(string entry, Occasion occasion, string quote);
static GameCheck NoRoll(string entry, Occasion occasion, string roll, string quote);
static GameCheck RangeBonus(string entry, Occasion occasion, double printed, double bonus, string quote);
static GameCheck GrantedModifier(string entry, Occasion occasion, string source, string roll, int amount, string quote);
// scripts/ArmyBooks (Tasks 6a, 6b)
public enum RulePattern { Aura, Buff, Mark, Debuff }
public static class RuleTemplates { public static RulePattern? PatternOf(string name); public static string? Base(RulePattern p, string name, string description);
    public static EntryDocument Wrapper(RulePattern p, string name, string baseName, string description); }
public sealed record RuleReport(int Generated, IReadOnlyList<(string Group, int Count)> Groups, IReadOnlyList<string> NoDescription);
public static class RuleWriter { public static IReadOnlyDictionary<string, bool> NamesInScope(AfBook book);   // true: carried as a rule; false: on weapons only
    public static (PropertySetDocument? Part, RuleReport? Report, IReadOnlyList<string> Problems)
    Build(IReadOnlyList<AfBook> books, PropertySetDocument core, PropertySetDocument hand); public static string Serialize(PropertySetDocument part); }
```

## Tasks

1. [Played flag in the catalog](task-01-played-flag.md): `Played` on entries, validation, Gather skip, store, migration, schema.
2. [Played delivery](task-02-played-delivery.md): `GET /v1/properties/{hash}/rules` returns `played`; the client tags it.
3. [Pick entries in the grammar](task-03-pick-grammar.md): `lineOfSight`, validation of pick entries, carried and granted rows.
4. [The pick mechanism](task-04-pick-mechanism.md): `use` reaction, `pick-rule` sequence, `rule_target` Ask, per-model usage.
5. [A carried Aura's rule by name](task-05-aura-by-name.md): `Lookup.UnitHas` reads a carried Aura's grant.
6. [6a. The generator reads the books' rules and the templates](task-06a-generator-templates.md): rule texts, units, upgrades, names in scope, the three templates;
   [6b. The generator writes the rules part](task-06b-generator-writer.md): wrappers, text-only entries, the report, `generate`.
7. [Coverage: not automated and by pattern](task-07-coverage.md): the suite's coverage states and report.
8. [Generated part and the pattern games](task-08-pattern-games.md): Defense Debuff, four games, the generated file, suite run;
   [8b. Quick Shot Mark](task-08b-quick-shot-mark.md): a server test and a Mark game for a marked enemy shot after Rush, once.
9. [Batch 1: weapon rules in melee or when shooting](task-09-batch1-grants.md): ten rules.
10. [Batch 1: hit and AP rules](task-10-batch1-hit-and-ap.md): nine rules.
11. [Batch 1: defense rules](task-11-batch1-defense.md): five rules.
12. [Batch 1: movement and range](task-12-batch1-movement.md): six rules and the removed-row read.
13. [Batch 1: written-out Auras that grant a rule](task-13-batch1-grant-auras.md): twelve Auras.
14. [Batch 1: written-out Auras with a number](task-14-batch1-number-auras.md): nine Auras;
    [14b. Batch 1: unbatched rules, Banner and Musician](task-14b-batch1-unbatched-and-command-group.md): Bad Shot, Bloodborn, Brutal Fighter, Knightborn, Unpredictable, Wolfborn, Unique, Banner, Musician.
15. [Regenerate, both drivers, documents](task-15-regenerate-and-documents.md): the bases' wrappers, `PASSED` twice, docs.

## Moved to a later batch

Every name the spec's batch 1 lists, every base a template wrapper names, and every name no spec batch named was read
against the effect grammar (`docs/design/interpreter/effect-grammar.md`, `EffectKind`, `ConditionKind`). Those it
expresses are authored in Tasks 9–14b. The rest go to the spec batch that builds their mechanic; Task 15 writes them
into the spec's batch lists. Their wrappers stay text-only until then.

| Rule | Army Forge text | Why not now | Batch |
| --- | --- | --- | --- |
| Armor | "Counts as having Defense X+." | `replace_stat` writes its literal `replacement`; nothing writes the parameter. | 8 (new) |
| Heavy Impact | "Counts as having Impact(X) with hits that have AP(1)." | Impact's hits resolve weapon-less (`GfMelee.ImpactStep`), so no row gives them AP; `grant_property` passes a fixed amount, not the holder's X. | 8 |
| Melee Shrouding | "Enemies get -3\" movement to a min. of 6\" when trying to charge units where all models have this rule." | No floor on a modified distance (`StatReader.ActionDistance` floors at 0); the charge distance is read before the target is an interaction side. | 8 |
| Ranged Shrouding | "Enemies get -6\" range to a min. of 6\" when trying to shoot units where all models have this rule." | No floor on a modified range (`StatReader.WeaponRange` floors at 0). | 8 |
| Darkborn | "Enemies get -4\" range to a min. of 6\" when trying to shoot at units where all models have this rule, and -2\" movement to a min. of 6\" when trying to charge units where all models have this rule." | The two Shroudings' floors. | 8 |
| Grounded Precision | "If a unit where all models have this rule has most of them within 1\" of terrain, they get +1 to hit rolls when attacking." | `position_terrain` reads the defender's terrain (or a lone party's), never the attacker's. | 8 |
| Entrenched | "As long as a unit where all models have this rule hasn't moved during the round, …" | `moved` is cleared at every activation's end (`GfHooks.OnTurnEnded`); nothing records a move this round. | 8 |
| Mobile Artillery | "When this model uses a Hold action and shoots at enemies over 9\" away, it gets +1 to hit rolls. As long this model hasn't moved during the round, when enemy units shoot at it from over 9\" away, they get -2 to hit rolls." | Its second sentence is Entrenched's "hasn't moved during the round". | 8 |
| Quick Readjustment | "This model ignores penalties from shooting after moving when using Indirect weapons." | Suppression is of a whole rule (`suppress_property`) or of every negative modifier (`suppress_modifiers to_hit_penalties`); nothing removes one rule's one row. | 8 |
| No Retreat | "… fails a morale test that causes it to be Shaken or Routed, the test counts as passed instead. Then, roll as many dice as …" | `override_result` passes the test; wounds per die after it are a new kind. | 8 |
| Steadfast, Battleborn, Honor Code | "If a unit where all models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken." | No round-start point and `set_condition` only sets; batch 6 builds the round-start trigger (Defensive Growth's "at the beginning of each round"). | 6 |
| Speed Feat | "Once per game, when this unit moves …, you may use this rule …" | A once-per-game choice at the move. | 4 |
| Inquisitorial Agent, Martial Prowess | "Once per game, if all models in this unit have this rule, it may be activated even if it had already activated this round …" | A second activation is the scheduler's. | 4 |
| Watchborn | "When this unit is activated, pick one effect: until the end of the activation, … either get AP(+1) or get +1 to hit rolls." | A choice at activation lasting to its end. | 4 |
| Versatile Defense, Versatile Reach | "… pick one effect … until …" | A choice at activation or deployment lasting to a later point. | 4 |
| Fanatic | "After this model is deployed, it may be placed anywhere fully within 9” of its position." | A placement at deployment. | 3 |
| Caster Group | "Pick one model with this rule in this unit to have Caster(X), where X is the total number of models with this rule in this unit. …" | A caster chosen and moved between models, with token transfer. | 7 |

Batch 8 is new: the grammar additions the spec's batches 2–7 do not build (a stat from the parameter, AP on Impact
hits, floors on modified distances, the attacker's terrain, a move this round, a one-row suppression, wounds after a
passed test). Bases already in a later spec batch, whose Aura wrappers stay text-only until then: Infiltrate (3),
Bounding and Dash (4), Protection Feat (5), Defensive Growth (6), Reanimation (7).

The spec's list of bases names Precision Shooter, Piercing Fighter and Thrust in Melee. No book prints or describes
them, and no template wrapper names them: the written-out Auras that read like them are authored with their own rows
in Tasks 13 and 14, and Precision Shooter Buff is a written-out Buff (batch 2). They are not authored.

## Banner and Musician

The books' dump has no text for them: each upgrade gain carries only `{"id": "ywXKQKPOLjTF", "name": "Banner", "type":
"ArmyBookRule"}` (and `qrG_DXJez5Dg` for Musician), no book's `specialRules` lists them, and the rulebook pages have only
p.20's *Army Banner* and p.36's *Mystic Banners*, other rules. Army Forge's common rules for Grimdark Future hold them:
`GET https://army-forge.onepagerules.com/api/rules/common/2` returns `{"rules": [...], "traits": [...]}` with the same ids:

- Banner: *"This model and its unit get +1 to morale test rolls."*
- Musician: *"This model and its unit moves +1" when using move actions."*

Both are Aura-shaped and authored in Task 14b.
