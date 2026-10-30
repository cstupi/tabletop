# Phase 5b — Special rules under 3.6.0: S1–S17 and the army-edition refusal (design step 6)

Index: `index.md`. Spec: `2026-10-29-gf-3-6-0-rules-diff.md` → Special rules. Group **F**, after phase 2; uses the `Sim.Gf36`/`State36` helper phase 5a's first task adds (land C1 first, or add the helper in the first F task to land). Worktree grouping by file: `GfMelee` (S11 then S5), `GfAttack`/`RollEffects` (S12, S16, S1), `GfMoves`/containers (S15, S6), casting (S4, S14), data-only (S7/S17, S8, S9) in one branch, S2, S3, S10, S13 alone; the refusal task last. Every data task edits **only** `server/game/Content/grimdark-future-3.6/core.properties.json` (`C36` below) and asserts the 3.5.1 hash test of Task 2.2 still passes. "3.6 ruleText" means the sentence quoted in the spec item.

---

### Task S1: Aircraft: -1 to hit and no contact — **Sonnet** (data)

3.6.0 p.13: *"can’t be moved in contact with or charged, and units targeting them get -12” range and -1 to hit rolls."*

- Modify `C36` Aircraft (`:10-11` in the 3.5.1 file; same position in the copy): `ruleText` → 3.6; effects add `{"subject":"opponent","ordinal":20,"kind":"modify_roll","targetRoll":"hit","amount":-1}`. Charge exclusion (`GfMelee.cs:136`) and `move_separation` already hold.
- Test: `tests/Tabletop.GameServer.Tests/RollEffects36Tests.cs` (new): `Shooting_at_an_aircraft_is_minus_one_to_hit_under_3_6_and_unmodified_under_3_5_1` (paired, over `TestProperties.Core36`/`Core`).
- Steps: failing test → `--filter RollEffects36` FAIL → edit JSON → PASS, `ContentFolders` hash test PASS → commit `"3.6.0 S1: Aircraft -1 to hit"`.

---

### Task S2: Artillery moves; Hold-and-over-12" bonuses — **Opus**

3.6.0 p.13: *"When this model uses Hold actions and shoots at enemies over 12” away, it gets +1 to hit rolls. If most models in this unit have this rule, as long as they haven't moved during this round, enemy units shooting at them from over 12” away get -1 to hit rolls."*

- Modify `C36` Artillery (`:15-20`): `aggregation` `"majority"`, `ruleText` 3.6; self row: `+1 hit` with conditions `attack_kind shooting`, `range gt 12`, `action_taken eq Hold`; opponent row: `-1 hit` with `attack_kind shooting`, `range gt 12`, `actor_state` **subject self** `neq "moved this round"` (`GfNames.MovedThisRound` is the condition name; confirm in `server/game/Effects/Conditions.cs` that `ActorState` honours `EffectCondition.Subject` for an opponent row's holder — if it reads the roller only, add the subject read and a line in `docs/design/interpreter/effect-grammar.md`).
- Modify `GfRules.Scheduling.cs:63,79`: the Artillery Hold-only branch and `ArtilleryNote` apply only when `GfEdition.Of(state).ArtilleryHoldOnly`; the note text from `edition.ArtilleryNote`.
- Test: `GrimdarkFutureRulesTests.cs:87,99` (3.5.1 unchanged); new `GfArtillery36Tests.cs`: `Artillery_may_advance_under_3_6_and_has_no_note`; `The_plus_one_needs_a_hold_and_over_twelve_inches_under_3_6` (Advance then shoot at 13": no +1; Hold at 13": +1; Hold at 10": none); `Enemies_get_minus_one_only_while_the_artillery_has_not_moved_this_round`.
- Steps: failing → FAIL → implement → PASS (`--filter "GfArtillery36|GrimdarkFutureRules"`) → commit `"3.6.0 S2: Artillery"`.

---

### Task S3: Assault weapons shoot and strike — **Opus**

3.6.0 p.13: *"Counts as both a ranged weapon and a melee weapon."*

- Modify `C36`: add entry `{"name":"Assault","kind":"weapon","ruleText":"Counts as both a ranged weapon and a melee weapon.","effects":[]}` (position after Artillery; the generator's 3.6 hand part must not define it — Task 7.5 checks). Modify `GfMelee.MeleeWeapons` (`GfMelee.cs:19-24`): also keeps a weapon with `Range` when `GfEdition.Of(state).HasAssault` and the weapon carries `Assault`; `GfAttack.KindOf` (`:329`) reads the frame (the melee sequence passes `AttackKind.Melee`) rather than the weapon's `Range` — audit its callers so a melee frame with an Assault weapon stays Melee and a shooting frame stays Shooting.
- Test: new `GfAssault36Tests.cs`: `An_assault_weapon_shoots_in_the_shooting_phase_and_strikes_in_melee_under_3_6` (hits in both; its melee strikes roll at Quality with no range penalties); `Under_3_5_1_an_assault_named_weapon_with_a_range_is_ranged_only`.
- Steps as above → commit `"3.6.0 S3: Assault"`.

---

### Task S4: Caster: one cast window, no sight — **Opus**

3.6.0 p.13: *"Once per activation, before attacking, spend … Pick a target and roll one die, and on 4+ resolve the spell’s effect. Models within 18” of the Caster may spend any number of spell tokens"*.

- Modify `GfCasting.cs:82-96` (`EligibleSpenders`: when `!SpendersNeedSight` drop `CanSee`; when `SpendersMeasuredToCasterModel` measure to the caster model, not its unit), `:107-135` (`Targets`: `CasterTargetNeedsSight` false → the `sight` filter is only the spell's own `LineOfSight == true`), `GfHooks.cs:99-129` (the cast reaction is offered once per activation under 3.6: a `cast_offered_<unit>` mark in the activation frame; today "at any point before attacking" lets a moving caster stop to cast (`CastingWindowStep` in `GfMoves.cs:197`) — under 3.6 the window is still before attacking, so a stop-to-cast stays, but a second window in the same activation is not offered). Decision 2 (`OneTryPerSpellScope = activation`) stands. `C36` Caster `ruleText` 3.6.
- Test: `GfCastingTests.cs:124` (3.5.1); new `GfCasting36Tests.cs`: `A_token_holder_out_of_sight_within_18_of_the_caster_model_may_spend_under_3_6`; `A_spell_target_needs_no_line_of_sight_under_3_6_unless_the_spell_says_so`; `The_cast_window_opens_once_per_activation_under_3_6`.
- Steps → commit `"3.6.0 S4: Caster"`. Update `docs/design/interpreter/decisions.md` decision 2's note with the 3.6 reading.

---

### Task S11: Impact before Counter, no Counter reduction — **Opus** (before S5, same branch)

3.6.0 p.14: *"When charging, before either unit attacks, roll X dice, unless this unit is Fatigued. For each 2+ the target takes 1 hit."*

- Modify `GfMelee.cs:15` (`Melee()` becomes `Melee(GfEdition e)`: `ImpactBeforeCounter` puts `ImpactStep` before `CounterStep`), `GfRules.Sequences.cs:14` (each instance builds `GfMelee.Melee(Edition)`), `GfMelee.cs:236-254` (`ImpactStep`: the Counter subtraction at `:248-249` only when `CounterReducesImpact`). `C36` Impact `ruleText` 3.6 (effects unchanged: the `actor_state neq fatigued` condition is the 3.6 text too).
- Test: `GfMeleeTests.cs:57` (3.5.1 unchanged); new `GfMelee36Tests.cs` (extend C1's file): `Impact_rolls_before_counter_and_loses_no_dice_to_counter_under_3_6` (4 Impact dice vs one Counter model: 4 dice, Impact hits land before Counter's strikes in the log).
- Steps → commit `"3.6.0 S11: Impact before Counter"`.

---

### Task S5: Counter is the defender's choice, lost when Fatigued — **Opus** (after S11)

3.6.0 p.13: *"May strike first with this weapon when charged, unless this unit is Fatigued."*

- Modify `GfMelee.cs:216-231` (`CounterStep`: when `CounterOptional`, open `ctx.Ask("counter", "Strike first with Counter?", defenderTeam, new Options([("strike","Strike first",[defender]),("pass","Strike with the rest",[])]))` as `StrikeBackStep:313` does; when `CounterLostWhenFatigued` and the defender is Fatigued (or Shaken: p.10) skip the step). `C36` Counter `ruleText` 3.6. The 3.5.1 step runs unchanged.
- Test: `GfMeleeTests.cs:57,246-319` (3.5.1); `GfMelee36Tests.cs`: `Counter_asks_the_defender_and_a_pass_strikes_with_the_rest_under_3_6`; `A_fatigued_defender_has_no_counter_under_3_6`.
- Steps → commit `"3.6.0 S5: Counter optional, lost when Fatigued"`. `AnswerPolicy` needs no change (an `Options` Ask).

---

### Task S6: Cumbersome — **Opus**

3.6.0 p.14: *"May only use Hold or Advance actions, but can’t shoot when moving."*

- Modify `C36`: add `{"name":"Cumbersome","kind":"model","aggregation":"any","ruleText":"…","effects":[]}` (position after Counter). `GfRules.Scheduling.cs:58-74` (`ActivationChoices`: when `HasCumbersome` and `Lookup.UnitHas(..., "Cumbersome")`: Hold and Advance only, `edition.CumbersomeNote`); `GfMoves` Advance sequence: when the unit has Cumbersome and moved (any model's move distance > 0) the shooting step is skipped (a `cumbersome_no_shot` value on the frame so the log says why). Aggregation decision: "may only use" is a unit restriction like Immobile's; Immobile uses `all` — Cumbersome's text names the model; `any` restricts the unit when any model has it (a vehicle's unit is one model). Record it in `docs/design/interpreter/decisions.md`.
- Test: new `GfCumbersome36Tests.cs`: `Cumbersome_offers_hold_and_advance_only_with_its_note`; `A_cumbersome_unit_that_advanced_does_not_shoot_and_one_that_held_does`.
- Steps → commit `"3.6.0 S6: Cumbersome"`.

---

### Task S7/S17: Wording-only rule texts in the 3.6 core — **Sonnet** (data)

Replace `ruleText` in `C36` for Fear, Ambush, Bane, Furious, Relentless, Surge, Rending, Strider, Takedown, Thrust, Tough with the 3.6.0 p.13–15 sentences (effects unchanged; S17 lists them; the text comes from `v3.6.0/pages/p13–p15.md` once phase 4 lands, else the PDF).
- Test: `tests/Tabletop.GameServer.Tests/Core36TextTests.cs` (new): `Every_3_6_core_entry_text_is_the_3_6_0_books` — for the entries above and every other special rule, `ruleText` equals the sentence in the skill page (read the page files; a small normaliser for quotes and hyphens). This test also pins S1, S2, S4, S5, S8–S16's texts, so it lands **last** in group F or is written with `[Fact(Skip=…)]` per entry until each task lands — decision: land it last, after every other F task.
- Steps → commit `"3.6.0 S17: rule texts"`.

---

### Task S8: Fearless, Regeneration, Stealth: most models — **Sonnet** (data)

3.6.0: *"most models"* (Fearless p.14, Regeneration p.14, Stealth p.15).
- Modify `C36` the three entries' `aggregation` → `"majority"`, `ruleText` 3.6.
- Test: new `LiveProperties36Tests.cs`: `A_hero_without_regeneration_no_longer_switches_the_units_regeneration_off_under_3_6` (4 of 5 models: 3.5.1 none; 3.6 one unit-level property), same shape for Fearless and Stealth. `docs/design/interpreter/decisions.md` decision 4 gains the 3.6 reading.
- Steps → commit `"3.6.0 S8: most models"`.

---

### Task S9: Flying — **Sonnet** (text and paired tests)

3.6.0 p.14: *"May move through units and terrain, and ignores the effects of Difficult and Dangerous terrain."* Today `TerrainRules` (`GfMoves.Terrain.cs:21-32`) already has Flying ignore difficult and dangerous terrain and pass impassable, at activation too (`GfMoves.cs:177`), so the engine's reading is the same under both editions; Steep is Difficult and Unstable resolves as Dangerous, so both are ignored by the same flags.
- Modify `C36` Flying `ruleText`. Test (`GfMoves36Tests.cs`, new): `A_flying_unit_ignores_steep_and_unstable_under_3_6` (over a `Steep` hill with no cap; over an Unstable volume with a 7" path, no test), `A_flying_unit_activated_in_dangerous_terrain_tests_under_neither_edition` (documents the shared reading).
- Steps → commit `"3.6.0 S9: Flying text and terrain-type tests"`.

---

### Task S10: The Hero's Quality for Morale within 6" — **Opus**

3.6.0 p.14: *"The Hero’s unit, and units within 6” of friendly Heroes with Tough(9) or higher, may use the Hero’s Quality for Morale Tests as long as the Hero isn’t Shaken."*
- Modify `GfRolls.cs:143-153` (`MoralePlan`: when `HeroAuraTough is { } t`, candidates also include every living friendly model with `Hero` and `Tough >= t` in another unit within 6" (`Battlefield.Distance` base to base) whose unit is not Shaken; the joined Hero's Quality is used only while its unit (the Hero) is not Shaken — the joined Hero's unit **is** the testing unit, Shaken units roll no die (`GfMorale.cs:59`), so no new guard; `Applied` lists `Hero`). `C36` Hero `ruleText`.
- Test: `GfRollsTests.cs:134` (3.5.1); new `GfMoraleAura36Tests.cs`: `A_tough_nine_hero_within_six_inches_lends_its_quality_under_3_6_not_3_5_1`; `A_shaken_or_tough_six_hero_lends_nothing`; `The_better_of_several_applies`.
- Steps → commit `"3.6.0 S10: Hero morale aura"`.

---

### Task S12: Indirect only on Hold — **Opus**

3.6.0 p.14: *"When using Hold actions and shooting, may target enemies that are not in line of sight as if in line of sight, and ignores cover from Sight Blockers."*
- Modify `C36` Indirect (`:52-54`): drop the `-1 hit on Advance` row, `ruleText` 3.6. `GfAttack.cs:341-364,372-405` (`EligibleModels`, `Aims`, `CoverOf`: the Indirect reads apply only when `!IndirectNeedsHold || action == Hold`; the action is the activation frame's `action_taken`), `GfRules.Facts`/`Shooters` (the sight query's Indirect read: the same condition, read off the unit's recorded action when one exists, else Hold-eligible → treat as not holding until the activation names its action).
- Test: `GfCoverTests.cs:106,173,262` (3.5.1); new `GfIndirect36Tests.cs`: `Indirect_after_an_advance_needs_sight_and_gives_cover_under_3_6` and `on_a_hold_it_targets_unseen_and_ignores_sight_blocker_cover` (paired with 3.5.1's -1 and always-on reading).
- Steps → commit `"3.6.0 S12: Indirect on Hold"`.

---

### Task S13: Scout 9" and 9" clearance — **Opus**

3.6.0 p.15: *"may be deployed anywhere fully within 9” of their deployment zone, over 9” away from non-Scout enemy units."*
- Modify `GfRules.Deployment.cs:49-55` (`ZoneBreach`: reach from `GfEdition.Of(state).ScoutReach`, message `$"beyond {reach}\" … ({page})"` with the edition's page string: `(p.14)` | `(3.6.0 p.15)`; when `ScoutClearance > 0`, a Scout placed with any base within 9" of an enemy model whose unit is not a Scout is `scout_distance` (new rule id `GfNames.ScoutDistanceRule`), appended as `AmbushBreach` is), `sim/Run/HandPlacement.cs` (a Scout under 3.6 is anchored over 9" from non-Scout enemies, as Ambushers are), `C36` Scout `ruleText`.
- Test: `GfDeploymentZoneTests.cs:71` (3.5.1 message unchanged); new `GfScout36Tests.cs`: `A_scout_deploys_within_nine_inches_of_its_zone_under_3_6` (10" refused, 8" accepted); `A_scout_within_nine_of_a_non_scout_enemy_is_scout_distance`; `An_enemy_scout_does_not_count`.
- Steps → commit `"3.6.0 S13: Scout"`. `server/game/AGENTS.md` placement-breach bullet gains `scout_distance`.

---

### Task S14: Shred and Spell Amplifier in the 3.6 core — **Sonnet** (data)

3.6.0 p.15 Shred: *"For each unmodified result of 1 to block hits, this weapon deals 1 extra wound."*; Spell Amplifier: *"Gets +1 to casting rolls."*
- Modify `C36`: add Shred (the hand part's entry at `grimdark-future-3.5.1/army-rules.part.json:186`, 3.6 text) and `{"name":"Spell Amplifier","kind":"model","aggregation":"none","ruleText":"Gets +1 to casting rolls.","effects":[{"subject":"self","ordinal":10,"kind":"modify_roll","targetRoll":"casting","amount":1}]}`; remove Shred from `grimdark-future-3.6/army-rules.part.json` (else `Compose` refuses two entries of one name).
- Test: `RollEffects36Tests.cs`: `Spell_amplifier_adds_one_to_the_casting_roll`; `Shred_is_a_core_entry_under_3_6` (`Core36.Entries` has it, kind weapon).
- Steps → commit `"3.6.0 S14: Shred and Spell Amplifier in the core"`.

---

### Task S15: No embarking into an unreachable Transport — **Opus**

3.6.0 p.15: *"If the Transport is unreachable (for example because it’s flying), then units can’t embark in it."*
- Modify `GfMoves.cs:118-125` (`ContainersInReach`: when `AircraftTransportUnreachable`, a container unit with `Aircraft` is excluded), `GfRules.Moves`/`MayDeployInside` (deployment into an Aircraft Transport likewise: p.15 "Transports may deploy with units inside" stands — **decision**: deployment inside stays allowed (the book forbids embarking, a move); only the move-time embark is refused). `C36` Transport `ruleText` (C4 wrote it: keep one).
- Test: new `GfTransport36Tests.cs`: `An_aircraft_transport_is_not_in_reach_under_3_6_and_is_under_3_5_1`.
- Steps → commit `"3.6.0 S15: unreachable Transports"`.

---

### Task S16: Unstoppable keeps its own penalties — **Opus**

3.6.0 p.15: *"ignores all negative modifiers to this weapon which are not from this model's special rules."*
- Modify `server/game/Properties/PropertySet.cs:35` (`TargetModifier.ForeignToHitPenalties`: negative hit modifiers whose source property is not held by the roller's own unit), `server/game/Effects/RollEffects.cs:38-43` (the suppression keeps a negative modifier whose `Via`/source grantor is a property of the holder's unit when the kind is `ForeignToHitPenalties`), `PropertiesJson` enum parsing (`foreign_to_hit_penalties`), `C36` Unstoppable: `suppress_modifiers to_hit_penalties` → `foreign_to_hit_penalties`, `ruleText` 3.6; `docs/design/interpreter/effect-grammar.md` gains the kind.
- Test: `RollEffectsTests.cs:25` (3.5.1); `RollEffects36Tests.cs`: `Unstoppable_ignores_stealth_but_keeps_its_own_units_minus_one_under_3_6` (holder with a self `-1 hit` rule, e.g. a granted debuff from its own army rule vs an enemy's Stealth).
- Steps → commit `"3.6.0 S16: Unstoppable"`.

---

### Task 5.R: Armies of the other edition are refused — **Opus** (last in F; after phase 3)

Design: *"`PickArmy` and `EndSetup` refuse an army whose edition differs from the game's (`ArmyLimits` gains the check; a refusal names both editions). The army pickers list only the game's edition, the rest greyed with the reason."*

- Modify `server/game/Setup/ArmyLimits.cs` (`public static string? EditionBreach(string armyEdition, string gameRules) => RuleSets.EditionOf(gameRules) == armyEdition ? null : $"this army is a {armyEdition} list; the game plays Grimdark Future {RuleSets.EditionOf(gameRules)}"`), `GameSession.Lobby.cs:139` (`PlayableArmyAsync` refuses `NotPermitted` with the message), `GameSession.EndSetup.cs:26-27` (same check per participant; the refusal names the participant), `HandleWaitingEditAsync`'s `PickArmy` (the late join), `client/src/Panels/LobbyPanel.cs:51`, `WaitingPanel.cs`, `viewer/src/lib/lobby/TeamCards.svelte:55-58`, `WaitingPanel.svelte` (an army of another edition is listed disabled with the reason; `LobbyView.ArmyOptions(game, armies)` / `lobbyView.ts armyOptions` return `(label, id, enabledReason?)` tested), `HomeScreen.CreateDialog`/`CreateGameDialog.svelte` (the army list follows the selected set). The disabled option's reason for a 3.5 army in a 3.6 game is "3.5 list: re-import it from Army Forge on the website" (the web armies page's Re-import creates a new army from `army.source`, which Army Forge now serves as 3.6.0; the 3.5 army is kept); for a 3.6 army in a 3.5.1 game it is "3.6 list: Army Forge no longer serves 3.5 lists".
- Test: `tests/Tabletop.GameServer.Tests/ArmyLimitsTests.cs`, `LobbyEditionTests.cs` (`A_3_5_army_is_refused_in_a_3_6_game_and_the_reverse`, `EndSetup_refuses_a_mismatched_army`), `LobbyViewTests.cs`, `viewer/tests/lobbyView.test.ts`.
- Steps → all suites → `dotnet run --project sim -- lifecycle` (3.5 fixture armies on 3.5.1 games: still pass) → commit `"Armies of the other edition are refused"`. `server/game/AGENTS.md` lobby-authority bullet and `docs/design/game-server/design.md` note it.
