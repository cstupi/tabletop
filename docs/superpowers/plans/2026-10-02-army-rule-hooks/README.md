# Army-rule hooks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The fifteen army-book rules the spell library authors as names (*Hit & Run Fighter*, *Hit & Run Shooter*, *Harassing* and its Boost, *Quick Shot*, *Grounded Reinforcement*, *Teleport*, *Rapid Blink* and its Boost, *Unpredictable Fighter*, *Unpredictable Shooter*, *Primal*, *Clan Warrior* and their Boosts) work in play, innate or granted by a spell.

**Architecture:** One new predicate, `at_point`, names the moments a rule acts at (activation start, before attacking, after shooting, after melee, attack start); the engine asks for a point's effects through the built reaction window (`GfHooks.ReactionsAt` and `Windows.Run`). A `move` row or a new `place` row there offers a `reposition` sequence. `usage_count` gains a per-round and per-activation scope. A `roll` row at `attack-start` records its face for the attack's interactions, `add_hit` adds attacks, `position_terrain` gains `near`, and *Quick Shot* is a name check on the `rush` sequence's new shooting step.

**Tech Stack:** `net10.0`, C# 12, xUnit, Npgsql, DbUp.

**Spec:** [`docs/superpowers/specs/2026-10-02-army-rule-hooks-design.md`](../../specs/2026-10-02-army-rule-hooks-design.md), the whole document.

**Requires landed:** the [spell library](../2026-10-02-spell-library/README.md) plan, on this same tree, through its Task 15 (and so with the enforcement branch merged in at its Task 14).

## Global Constraints

- Every constraint of the spell library plan's *Global Constraints* holds here.
- Every new field on an existing record is a trailing optional parameter: `Effect`/`EffectDocument.AmountDice`, `Interaction.Point` and `Interaction.Faces`, `LiveProperty.Chosen`, `DieEffects.ExtraAttacks`.
- **No wire kind is added; `ProtocolVersion` stays 1; `wire.generated.ts` does not change.** The hooks travel as a `react` `Options` Ask (its `WindowKind` the point's name), a `move` `MoveRegion`, `ModelsMoved`, `RollRecord`s and `step_completed` values.
- A Shaken unit is offered nothing at any point (p.10: *"can't take any actions, or use any active special rules, such as casting spells, using buffs/debuffs on units, re-positioning, etc."*).
- This plan adds `006_army_rule_hooks.sql`; after Task 1 start management once (`./scripts/management`) to apply it to the compose database.
- Commit after every task, imperative one-line subject, ending with the `Co-Authored-By` trailer naming the model that produced it.

## Review Focus

- **A unit that both shoots and is in melee in one activation with *Harassing*** (a Charge after which it is not shot at): it is offered the move once, at `after-melee`, and not again that round. Task 2 pins once per round across both points.
- **A placement onto an impassable volume or off the table**: refused, the model stays. Task 3 pins it.
- ***Rapid Blink* declined**: no die is rolled, so the dice cursor is unchanged and a replay agrees. Task 3 pins it.
- **An attack-start face read back after an undo**: the face is a `RollRecord` and a step value, so replay reproduces it. Task 6 pins it with the simulator's replay invariant.
- **A *Primal* extra attack that rolls a 6**: it adds no further attack. Task 7 pins it.

---

## File Structure

```
server/management/Persistence/Migrations/006_army_rule_hooks.sql
server/game/Properties/PropertySet.cs, PropertySetDocument.cs, PostgresPropertiesStore.cs   place, amount_dice, at_point, attacks, own-roll validation
server/game/Effects/Interaction.cs, Conditions.cs, LiveProperties.cs, RollEffects.cs         Point, Faces, usage scope, Chosen, ExtraAttacks
server/game/Interpreter/Battlefield.cs                                                        TerrainUnder's near
server/game/Rules/GrimdarkFuture/GfHooks.cs, GfMoves.cs, GfShooting.cs, GfMelee.cs, GfAttack.cs, GfNames.cs, GfRules.Sequences.cs
server/game/Content/grimdark-future-army-rules.part.json                                     the fifteen entries' rows
tests/Tabletop.GameServer.Tests/ArmyRuleHookTests.cs
sim/Scenarios/ScenarioCatalog.cs                                                             the army-rules scenario
```

## Tasks

1. [The grammar: `at_point`, usage scopes, `place`, attacks, migration 006](task-01-the-grammar.md)
2. [A move after attacking: *Hit & Run* and *Harassing*](task-02-a-move-after-attacking.md)
3. [A placement: *Teleport* and *Rapid Blink*](task-03-a-placement.md)
4. [Shooting after Rush: *Quick Shot*](task-04-quick-shot.md)
5. [Near terrain: *Grounded Reinforcement*](task-05-grounded-reinforcement.md)
6. [A die at the start of the attack: *Unpredictable*](task-06-unpredictable.md)
7. [Extra attacks: *Primal* and *Clan Warrior*](task-07-extra-attacks.md)
8. [Every granted rule works, and the `army-rules` scenario](task-08-every-grant-and-the-scenario.md)

## Against the enforcement tree

This plan starts after the library plan's Task 14 merged the enforcement branch, so there is no merge left to make. It edits `ScenarioCatalog.cs` and `GfNames.cs`, which the enforcement tree also edited; on this tree those edits already sit on top of enforcement's, so they conflict with nothing. If the enforcement tree lands a later change to either file before this tree merges back to `main`, the conflict is textual (a scenario or a constant beside another).

---

## Interfaces → Produces

| Type | Members | Task |
| --- | --- | --- |
| `EffectKind` | `+ Place` | 1 |
| `TargetQuantity` | `+ Attacks` | 1 |
| `ConditionKind` | `+ AtPoint` | 1 |
| `Effect`, `EffectDocument` | trailing `string? AmountDice = null` (`"d3"`) | 1 |
| `Interaction` | trailing `string? Point = null`, `IReadOnlyDictionary<Side, IReadOnlyDictionary<string, int>>? Faces = null` | 1, 6 |
| `LiveSet` | `IEnumerable<(LiveProperty Source, Effect Effect)> AtPoint(Side side, string point)` | 1 |
| `GfNames` | `ActivationStart = "activation-start"`, `AfterShooting = "after-shooting"`, `AfterMelee = "after-melee"`, `AttackStart = "attack-start"`, `RepositionSeq = "reposition"`, `QuickShot = "Quick Shot"` | 1, 2, 4 |
| `GfMoves` | `RepositionSeq` sequence `Reposition()`; `ActivationStartStep`; `PointWindowStep(string point)`; `ApplyPlacement(StepContext, Guid unit, double distance, IReadOnlyList<Guid> models, IReadOnlyList<ModelRelocation>)` | 2, 3 |
| `GfShooting.Shooting()` | `[attack-start, declare-targets, fire-groups, after-shooting]` | 2, 6 |
| `GfMelee.Melee()` | `[melee-start, counter, impact, charger-attacks, strike-back, unwieldy, compare, consolidate, after-melee]` | 2 |
| `LiveProperty` | trailing `IReadOnlyList<Effect>? Chosen = null` | 6 |
| `DieEffects` | trailing `int ExtraAttacks = 0` | 7 |

## Interfaces needed from other modules

1. **Spell library plan** — `LiveProperty.GrantRow`, `LiveSet.Read`, `RollEffects.Consume` (Tasks 5, 6); `ConditionKind.HoldsProperty` (Task 9); `GfMoves.Idle()`, `ApplyForcedMove` (Task 10); the part file and its fifteen name-only entries (Task 11); `SpellLibraryTests.EverySpell` (Task 13); `TestProperties.WithSpells` taking rule entries (Task 1).

## Rulings

1. **`activation-start` is a step of its own**, `GfMoves.ActivationStartStep`, second in every action sequence and in `idle`, because `Windows.Run` counts the step's children to know which reaction it pushed, and `activation-terrain`'s dangerous terrain child would be counted among them.
2. **A window reopens after any child it pushed**, not only after a cast: `MoveStep`'s window phase and `CastingWindowStep` repeat when `ctx.History.Children.Count > 0`, so *Teleport* and casting share the checkpoint in either order.
3. **A placement is validated at the landing only**: displacement at most the distance, the base touching no impassable volume (p.11 *"may not end their move partially overhanging gaps or terrain"*; `GfMoves.LandsInImpassable`). Under strict, dispatch also refuses a landing off the table (`GfMoves.PathBreach`, p.7); p.7's contact clauses judge the landing (`GfMoves.ContactBreach`: `move_overlap`, and `move_separation` by the region's `Separation`; the region sets `PassesModels`, so no path through models is judged), as for every move (`architecture.md` → *Rules enforcement*).
4. **The Boost of a `move` rule counts its own uses.** Its base is suppressed while it is live, so the unit still moves once per round.

## Self-review

**Spec coverage.** *Hook points* → Task 1 (predicate, usage scope) and Tasks 2, 3, 6 (each point's step). *A move after attacking* → Task 2. *A placement* → Task 3. *Shooting after Rush* → Task 4. *Near terrain* → Task 5. *A die at the start of the attack* → Task 6. *Extra attacks* → Task 7. *The entries* → the part rows in Tasks 2, 3, 5, 6, 7. *Grammar, schema and wire* → Task 1. *Testing* → each task and Task 8.

**Placeholders.** None: every part row is given as the JSON that replaces its `"effects": []`.

**Type consistency.** `Interaction.Point`, `Interaction.Faces`, `LiveProperty.Chosen`, `DieEffects.ExtraAttacks` and the `GfNames` points are spelled the same in every task.
