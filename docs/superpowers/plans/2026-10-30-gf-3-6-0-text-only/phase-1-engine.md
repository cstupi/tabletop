# Phase 0–1: the engine

Spec: `docs/superpowers/specs/2026-10-30-gf-3-6-0-text-only-rules.md` → The additions. Every task here: Opus except 0.1; tests in `tests/Tabletop.GameServer.Tests/` as `Gf<Rule>36Tests.cs` or `Grammar36Tests.cs`; a 3.6 test runs through `Sim.Gf36(state, dice…)` and a behaviour that must not change under 3.5.1 is a paired `[Theory]` over `RuleSets.Gf351` and `RuleSets.Gf360` (`server/game/AGENTS.md` → Tests). No content file is edited in this phase: each task proves its point with `TestProperties` entries built in the test, so the hand part lands in phase 2 against a finished engine. Each task's commit also adds one line to `server/game/AGENTS.md` → How it works naming the new read, in the file's voice.

---

### Task 0.1: split `GfAttack.cs` — **Sonnet**, group A

`server/game/Rules/GrimdarkFuture/GfAttack.cs` is 818 lines. Partials, no behaviour change:

- `GfAttack.cs`: the sequences (`DealWounds`, `AttackSequence`, `ResolveHits`), `AttackInteraction`, `KindOf`, `StartedInMelee`, `FiredLimited`, `ListRuleWeapons`, `IsTakedownGroup`, `PushStep`, the key parsers (`ParseGrantKey`, `GrantKey`, `ParseGrantSet`, `HitsFrom`, `ByQuality`, `RangeKey`).
- `GfAttack.Wounds.cs`: `WoundsToDeal`, `TakedownModel`, `GrantedOf`, `AtCapacity`, `EligibleForNextWound`, `AssignStep`, `DisgorgeStep`.
- `GfAttack.Sight.cs`: `EligibleModels`, `Aims`, `CoverOf`, `WriteCover`, `ReadCover`, `InCoverAgainst`, `AttacksAgainst`, `IndirectApplies`.
- `GfAttack.Steps.cs`: `DetermineAttacksStep`, `PickTargetModelStep`, `RollToHitStep`, `ScoreHitsStep`.
- `GfAttack.Block.cs`: `RollToBlockStep` and its helpers (`ExtraWoundsKey`, `Settle`).

- [ ] Move members verbatim; keep every `public`/`internal` signature; each file under 300 lines.
- [ ] `dotnet build` 0 errors; `dotnet test tests/Tabletop.GameServer.Tests` and `tests/Tabletop.Sim.Tests` unchanged counts; `dotnet run --project sim -- suite --only core,special` passes.
- [ ] `server/game/AGENTS.md` → Layout names the partials; `docs/handoff.md` → Known rule gaps drops "Next structural task".

---

### Task 1.1: `weapon_range`, `remaining_size`, migration 027 — **Opus**, wave 1

Files: `server/game/Properties/PropertySet.cs` (`ConditionKind.WeaponRange`, `RemainingSize`; `EffectKind.CountWounds`, `RemoveSpellTokens`; `TargetModifier.PositiveDefense`; `EffectDuration.OneMarker`; `Effect.Optional`; `Targeting.OncePerRound`, `CountFromParameter`), `PropertySetDocument.cs` (the document fields, `Validate`: `weapon_range` takes no subject and a number; `remaining_size` takes a subject, a number and one value), `PostgresPropertiesStore.cs` (read and write the new columns and values), `Effects/Conditions.cs` (the two predicates), `Effects/Lookup.cs` (`RemainingSize(state, unitId, sizeRule) → (Remaining, Starting)`), `Rules/GrimdarkFuture/GfHooks.cs` (`AtHalfOrLess` reads `Lookup.RemainingSize`; gains an `extraWounds` parameter laid in Tough order, for 1.3), `Effects/StatReader.cs` if `WeaponRange` needs a per-weapon overload, `server/management/Persistence/Migrations/027_text_only_rules_grammar.sql`, `docs/schema.md`, `tests/Tabletop.Management.Tests` `MigrationTests` (E2E: 027 applies; the check constraints admit each new value).

The migration carries every new value of the whole plan (spec → The additions), so later tasks add none: `effect_kind_check` + `count_wounds`, `remove_spell_tokens`; `effect_condition_kind_check` + `weapon_range`, `remaining_size`; `effect_target_modifier_check` + `positive_defense`; the duration check + `one_marker`; `alter table management.effect add column optional boolean not null default false`, `effect_optional_check (optional = false or kind = 'add_hit')`; `catalog_entry` targeting columns `targeting_once_per_round boolean not null default false`, `targeting_count_from_parameter boolean not null default false` (or wherever `Targeting` is stored; read `011_pick_rows.sql`).

- [ ] Tests first (`Grammar36Tests.cs`): `weapon_range gt 50` holds at 13" on a 24" weapon, fails at 12" and with no weapon on the source; `remaining_size opponent gt 50 [Tough]` over a 10-model unit at 6 and at 5 alive, a Tough(6) model at 2 and at 3 wounds, a shelved single model; `GfHooks.AtHalfOrLess` equals `!remaining_size gt 50` on the same states.
- [ ] `PropertySetDocument` round-trips every new field; `Validate` refuses a subject on `weapon_range`, a missing value on `remaining_size`, `optional` off `add_hit`, `one_marker` on a row without `targeting` on its entry.
- [ ] `ContentFoldersTests` hash unchanged; `dotnet test tests/Tabletop.GameServer.Tests`, `tests/Tabletop.Management.Tests` (unit) pass; the E2E assertion is written and marked for the first stack-up run in the ledger.

---

### Task 1.2: `positive_defense` — **Opus**, group B

Files: `Effects/RollEffects.cs` (`Attributed` takes `defenseRollName`; suppressed `PositiveDefense` removes every modifier with a positive amount on that roll, listing each in `RollPlan.Ignored` by the suppressor; the `hitRollName` parameter's sibling), `Interpreter/StepContext.cs` (`Plan` passes `ctx.Rules.RollSpec`'s defense name; the `IGameRules` surface already names rolls, so no new member unless the name is not reachable), `Rules/GrimdarkFuture/GfRolls.cs` if `DefensePlan` must hand the verdict's `IgnoredBy`.

- [ ] Tests (`GfPierce36Tests.cs`): a weapon entry with `suppress_modifiers positive_defense` (opponent) against a target in cover holding a +1 defense row: the plan has no positive modifier, AP(-1) stays, `Ignored` names the suppressor twice; with the suppressor absent both apply. Under 3.5.1 nothing names the class, paired `[Theory]` shows the same plan with no suppressor.
- [ ] `docs/design/interpreter/effect-grammar.md` → `RollEffects.Plan` gains the fourth suppression line (one sentence; 4.1 does the rest).

---

### Task 1.3: `count_wounds` — **Opus**, group C

Files: `Effects/RollEffects.cs` (`CountedWounds(live, Side.Defender, die, roll)`: the `count_wounds` rows with a `roll_result` on the defense roll, summed per die, pure), `Rules/GrimdarkFuture/GfAttack.Block.cs` (`roll-to-block` writes `counted_wounds` and a per-face map `counted_<roll>_<face>` as `ExtraWoundsKey` does; a `WoundsToDeal`-style reader `CountedWounds(frame)` reads the faces as they stand), `GfMelee.cs` (`WoundsTaken` adds the `counted_wounds` of every `resolve-hits` frame under the melee whose context's target is the unit; the `CompareStep` log line names the rule as applied), `GfActivationEnd.cs` (the units to test: `WoundsThisActivation + counted > 0` and `GfHooks.AtHalfOrLess(…, extraWounds: counted)` where `counted` sums the `resolve-hits` frames of shooting attack-sequences at the unit since the acting unit's activation began), `GfHooks.AtHalfOrLess(extraWounds)` (from 1.1), `PropertySetDocument.Validate` (`count_wounds` needs a `roll_result` condition and `subject opponent`).

- [ ] Tests (`GfScourge36Tests.cs`): a weapon with `count_wounds 2` on defense raw 1: three blocks rolled 1, 1, 6 give `counted_wounds` 4; in a melee where the charger dealt 0 real wounds and the defender dealt 1, the charger wins (4 > 1) and the defender tests; a shooting that wounded no model but rolled two 1s against a 5-model unit of 5 single-wound models triggers the general morale test (5 − 4 = 1 ≤ 2.5), and the same against a 10-model unit does not; `WoundsThisActivation` and every model's wounds unchanged; under 3.5.1 the paired test shows no count.
- [ ] `dotnet run --project sim -- suite --only core,special` passes under both editions.

---

### Task 1.13: `parry` — **Opus**, group C, after 1.3 in the same worktree

Files: `Rules/GrimdarkFuture/GfParry.cs` (new: `ParryStep`, `GfNames.Parry`), `GfAttack.cs` (`ResolveHits` = `[new GfParry.ParryStep(), new RollToBlockStep(), …]`), `GfAttack.Block.cs` (`roll-to-block` reads hits less `parried`, and the per-grant hit split less the parried hits taken from the groups with the most grants first), `GfNames.cs`.

- [ ] Tests (`GfParry36Tests.cs`): under a melee, a 3-model defender with Hand Weapon Parry(1) and Master Hand Weapon Parry(3) on one model rolls min(1+1+3, hits) dice named `Parry`; two 6s ignore two hits, the Rending-granted hits first; Impact's `resolve-hits` parries too; a shooting attack-sequence (no melee frame) parries nothing; a 3.5.1 game with a weapon named Parry (no entry) rolls nothing (paired).
- [ ] `suite --only core,special` passes both editions.

---

### Task 1.4: `optional` on `add_hit`, the `extra_attacks` Ask — **Opus**, group D

Files: `Rules/GrimdarkFuture/GfAttack.Steps.cs` (`RollToHitStep`: the base pool rolls and records; when optional `attacks` rows set off N and no answer is recorded, `OpenAsks` `extra_attacks` to the roller's team, `Options` Yes/No, `allowPass` (pass is No); Yes adds the N dice on the step's repeat, No or pass adds none; non-optional rows add as today), `Effects/RollEffects.cs` (`PerDie` returns optional and mandatory extra attacks apart), `GfNames.cs` (`ExtraAttacks` question), `PropertySetDocument.Validate` (`optional` only with `target_quantity attacks`), `sim/Answers/AskPicks.cs` (a `Yes(ask)` pick for scripts; the policy already draws over `Options`).

- [ ] Tests (`GfPredator36Tests.cs`): an entry with an optional `add_hit attacks` on raw 6: two 6s open `extra_attacks` naming 2; Yes rolls two more dice marked as added (`attacks: false` triggers); No rolls none; a non-optional row (Furious's shape) asks nothing; `HitsTriggerNothing` (fatigued melee) asks nothing.
- [ ] `suite --only core,special` passes; `viewer/tests/fixtures/calls.json` needs no case (an `Options` answer is an existing message), noted in the report.

---

### Task 1.11: Quick Throw — **Opus**, group D, after 1.4 in the same worktree

Files: `Rules/GrimdarkFuture/GfAttack.Sight.cs` (`IndirectApplies(live)`: `… || live.Has(Side.Attacker, GfNames.QuickThrow)`; where Indirect is recorded on an action other than Hold, `live.Record(Side.Attacker, GfNames.QuickThrow)`), `GfNames.cs`.

- [ ] Tests (`GfQuickThrow36Tests.cs`, 3.6 only): an Advance with an Indirect weapon and a Quick Throw model sees a target out of sight and ignores sight-blocker cover; without Quick Throw it does not; `Applied` names both rules.

---

### Task 1.5: `remove_spell_tokens` — **Opus**, group E

Files: `Rules/GrimdarkFuture/GfCasting.cs` (`EffectsStep`: a spell row of the kind takes N tokens off the picked unit's models, most tokens first, `SpellTokensSet` each; `spell-test`'s `ConsequenceStep`: a child of the kind takes N off the picked model, or off its unit when the pick was a unit), `PropertySetDocument.Validate` (`amount` ≥ 1, no `target_*`), `GfNames.cs` nothing.

- [ ] Tests (`GfSpellTokens36Tests.cs`): a spell `remove_spell_tokens 2` on a unit with models holding 1 and 2 tokens leaves the 2-holder at 0 and the 1-holder at 1 (most first); a `take_test` child on a failed test takes 2 off the model and on a passed test nothing; a target with no tokens records the step with no change.

---

### Task 1.6: `targeting.oncePerRound`, `countFromParameter`, `one_marker` — **Opus**, group E, after 1.5

Files: `Rules/GrimdarkFuture/GfCasting.cs` (`Targets(state, model, targeting)`: with `OncePerRound`, leave out units a `pick-rule` frame of the same entry moved since `round_started`: a `move` step's `ModelsMoved` under such a frame whose models belong to the unit), `GfPick.cs` (`TargetStep`: `PieceSelection(eligible, 1, X)` when `CountFromParameter`, X the carrier's parameter; markers: one unit picked takes X, several one each), `Effects/LiveProperties.cs` (`Gather` keeps one `OneMarker` grant instance per entry and side; `LiveSet.Spend` of a `OneMarker` grant marks only that grant), `Effects/RollEffects.cs` (`Consume`: a `OneMarker` grant goes alone, not with its fellows), `GfMarkers.cs` (`Rows` includes `OneMarker`), `PropertySetDocument` (`Targeting` fields; a `one_marker` row only on an entry with `targeting`).

- [ ] Tests (`GfTags36Tests.cs`): a `one_marker` AP(+1) row with three tokens on a unit: the first attack reads AP(+1), not (+3), and leaves two tokens; the paired 3.5.1-shape test with `spent_marker` and two tokens reads +2 and leaves none (review focus 1). `countFromParameter` with X 2: picking one unit places two tokens, picking two places one each; `oncePerRound`: a unit moved by a `pick-rule` of the entry this round is not offered, one whose test passed is (review focus 7), and both are offered next round.

---

### Task 1.7: once grants released at the attack's end — **Opus**, group F

Files: `Interpreter/StepContext.cs` (`Consume(LiveSet)`: when `Frame` or an ancestor is `attack-sequence`, `spell-hits` or `rule-hits`, return `[]` and add `held_<unit>_<grant>` to a `Held` dictionary the interpreter writes into the step's Values; else `RollEffects.Consume`), `Interpreter/SequenceInterpreter.cs` (merges `ctx.Held` into `StepCompleted.Values`), `Rules/GrimdarkFuture/GfAttack.cs` (`AttackEndStep` closes `AttackSequence`; also appended to `GfCasting.SpellHits` and `GfRuleHits.RuleHits`: emits `GrantRemoved` for each `held_` grant in the frame's and its descendants' `StepCompleted` values still on its unit, with every other grant of that entry on the unit), every step under those roots that calls `RollEffects.Consume` (14 call sites across `GfAttack.*`, `GfCasting`, `GfRuleHits`, `GfMelee`, `GfMoves`, `GfMorale`: switch to `ctx.Consume`; the ones outside an attack root keep immediate behaviour through the same method), `GfMorale.TestStep` keeps its own `held_` scheme (inside a morale frame, not an attack root).

- [ ] Tests (`GfOnceGrants36Tests.cs`, paired over both editions since the fix moves 3.5.1 games too): a once grant of a two-row entry (+1 hit, AP(+1) grant) is read at `roll-to-hit` and still live at `roll-to-block`, and `GrantRemoved` appears once, in `attack-end`; a once grant with `suppress_property Regeneration` and a defense re-roll is still live at `assign`; a once morale modifier is consumed in the morale frame (review focus 2); a `fire-groups` activation with two groups gives the second group nothing, as before.
- [ ] `dotnet run --project sim -- suite` (full, both editions) passes with no pin: the 3.5.1 Marks and Buffs games read the same results.

---

### Task 1.8: the `attacked` point — **Opus**, group F, after 1.7

Files: `Rules/GrimdarkFuture/GfChoice.cs` (`DefenderChoiceStep`: settle the attack interaction with `Point = attacked`; for each entry whose `set_condition` rows at the point act on `Side.Defender` with their other conditions holding, ask the defender's team `choose_effect`, set the chosen condition on the defending unit and clear the entry's others; `ClearedAtPoint(state, props, unit, "attacked")`), `GfAttack.cs` (`AttackSequence` = determine-attacks, **defender-choice**, pick-target-model, roll-to-hit, score-hits, resolve, self-wounds, attack-end; `AttackEndStep` also emits the defender's `attacked` clears), `GfHooks.ReactionsAt` unchanged (the point is never a reaction window), `GfNames.cs` (`Attacked`).

- [ ] Tests (`GfVersatileDefense36Tests.cs`): an entry with two `set_condition` rows at `attacked` (conditions `attack_kind shooting, range gt 9` / `action_taken opponent Charge, attack_kind melee, range gt 6`) and the two effect rows reading them: a shot from 10" asks the defender's team, the +1 Defense choice reaches the block plan, the -1 to hit choice the hit plan; a shot from 8" asks nothing; the condition is gone after `attack-end`, and a second attack-sequence in the activation asks again (review focus 6); a pass sets nothing.

---

### Task 1.9: a dice move's count reads the parameter — **Opus**, group G

Files: `Rules/GrimdarkFuture/GfMoves.cs` (`RollDistance(ctx, entry, source, row)` rolls `row.EffectiveAmount(null) + (source.Parameter ?? 0)` dice; `Distance` unchanged), `GfHooks.cs` (`Label(source, row)` shows `2D3` when the parameter adds one).

- [ ] Tests (`GfEnhancedMoves36Tests.cs`): a `move 1 d3` entry with a sibling entry `modify_parameter <base> +1` conditioned `holds_property self <base>` on a unit where two of three models carry the base: the reposition rolls two dice and moves the two carriers only; without the sibling one die; the label reads `2D3"`.

---

### Task 1.10: Ravage from grants — **Opus**, group G

Files: `Rules/GrimdarkFuture/GfStrikeRolls36.cs` (`Dice(ctx, striker, rule)`: settle `AttackInteraction(striker, target, melee)`; Ravage = Σ over living models of own parameter + Σ granted Ravage instances (ModelId null: amount × living models; ModelId set: amount); Regenerative Strength unchanged; the applied list names the granting rule through `live.Record`).

- [ ] Tests (`GfRavageAura36Tests.cs`, 3.6 only): a 3-model unit, one model Ravage(2), a unit grant `Ravage 1` (an Aura's row): 2 + 3 = 5 dice; the grant alone on a unit with no Ravage: 3 dice; once per round still holds; 3.5.1 `RavageStep` untouched (`git diff` shows none).

---

### Task 1.12: Brace — **Opus**, group G

Files: `Rules/GrimdarkFuture/GfMelee.cs` (`ImpactStep`: under `ImpactBeforeCounter`, build the defender party with `MeleeWeapons(defender)`, `brace = live.Parameter(Side.Defender, GfNames.Brace) ?? 0` when the defender is neither Fatigued nor Shaken, `dice = max(0, Σ impact − brace)`, `ctx.Applied.Add(Brace)` when it took a die), `GfNames.cs`.

- [ ] Tests (`GfBrace36Tests.cs`, 3.6 only): a charger with three Impact(2) models against five Spear (Brace(1)) models rolls 5 dice (review focus 5); against one Brace(1) and one Brace(3) model (two weapon names) rolls 2; a Shaken or Fatigued defender takes none; the 3.5.1 path still takes Counter's die per model (paired theory shows 3.5.1 unchanged).
