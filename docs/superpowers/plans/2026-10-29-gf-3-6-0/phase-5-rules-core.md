# Phase 5a — Core rules and terrain under 3.6.0: C1–C5, M1, M3 (design step 6)

Index: `index.md`. Specs: `2026-10-29-gf-3-6-0-rules-diff.md` (C1–C5), `2026-10-29-gf-3-6-0-rules-diff-modules.md` (M1, M3). Group **E**, after phase 2. Each task is its own branch; C1, C2, C3 are independent; C4 before S9's tests (phase 5b); C5 before M3 and before M1's label work; M1 independent. Every task: the 3.5.1 path is not edited; a 3.6 difference is read from `GfEdition.Of(state)` or is a 3.6 step beside the 3.5.1 one; every new test runs the same scenario under both editions where the behaviour differs (helper below); the 3.6 core file's `ruleText` is replaced where the rule's text changed; the suite game that quotes the changed rule gets its 3.6 copy in phase 6 (Task 6.4 lists them), not here.

**Test helper (added by Task C1; every other phase-5 task uses it):** `tests/Tabletop.GameServer.Tests/Infrastructure/`: `TestProperties.Core36` and `CoreDocument36` (the 3.6 set and document), `Sim.Gf36(GameState state, params int[] dice)` (runs the state under `GrimdarkFutureRules.V360` and `Core36`), `Sim.Gf(string rulesId, GameState state, params int[] dice)` (the one a paired test calls; `Gf351` or `Gf360`, anything else throws), and `TestGames.Under(this GameState state, string rulesId)` (swaps `Info.GameRules` and `Rules`; the board's army snapshots keep their edition, so a setup-path test under 3.6 builds its armies with `Edition = ArmyEditions.V36`). A paired test is a `[Theory]` with `[InlineData(RuleSets.Gf351, expected351)] [InlineData(RuleSets.Gf360, expected360)]`.

---

### Task C1: Melee reach 3" any direction — **Opus**

Rule: 3.6.0 p.9 *"All models that are within 3” of an enemy model from the target unit (horizontally or vertically), may attack it."* (3.5.1 p.9: 2" horizontally and 4" vertically.)

**Files:**
- Modify: `server/game/Rules/GrimdarkFuture/GfMelee.cs:45` (`InReach(state, a, b)` reads `GfEdition.Of(state).MeleeHorizontal/MeleeVertical`), `server/game/Rules/GrimdarkFuture/GfAttack.cs:351` (`EligibleModels` melee bound, same read), `server/game/Content/grimdark-future-3.6/core.properties.json` (no entry: core text, nothing to replace)
- Test: `tests/Tabletop.GameServer.Tests/GfMeleeTests.cs:140,299` (unchanged, 3.5.1), new `GfMelee36Tests.cs`

- [ ] **Step 1: Failing tests**: `A_model_two_and_a_half_inches_back_strikes_under_3_6_not_3_5_1` (second rank at 2.5" horizontal: 3.5.1 no, 3.6 yes); `A_model_three_and_a_half_inches_above_strikes_under_3_5_1_not_3_6` (vertical 3.5: 3.5.1 yes (≤4), 3.6 no (>3)).
- [ ] **Step 2:** `dotnet test tests/Tabletop.GameServer.Tests --filter GfMelee36` — FAIL. **Step 3:** implement (`GfNames.MeleeHorizontal/Vertical` stay as `GfEdition.V351`'s source values). **Step 4:** PASS; `--filter GfMelee` all PASS. **Step 5:** commit `git commit -am "3.6.0 C1: melee reach 3\" in any direction"`.

---

### Task C2: Fatigued 6s trigger nothing — **Opus**

Rule: 3.6.0 p.9 *"After a melee where they attacked, units hit on unmodified rolls of 6 in melee until the end of that round, and those hits don’t count as rolls of 6 for special rules."*

**Files:**
- Modify: `server/game/Rules/GrimdarkFuture/GfRolls.cs:61-64` (`HitPlan`: when fatigued and `!GfEdition.Of(ctx.State).FatiguedSixesTrigger`, the plan carries `NoFaceTriggers: true`), `server/game/Dice/RollPlan.cs` (`bool NoFaceTriggers = false`), `server/game/Rules/GrimdarkFuture/GfAttack.cs:634-637` (`RollToHitStep` passes `null` triggers when the plan says so) and `:669-671` (`ScoreHitsStep`'s per-die `add_hit`/grant reads skip when the plan said so — the record carries `FaceTriggers` null, so `PerDie` must be gated by the same flag read off the roll record: add `RollRecord.NoFaceTriggers` through `Roller`)
- Test: new `GfFatigue36Tests.cs`

- [ ] **Step 1: Failing tests**: `A_fatigued_rending_six_gives_no_ap_under_3_6` (charger strikes back fatigued with Rending; 3.5.1: the 6 carries Rending's AP(4) (`Die.Triggered` has Rending); 3.6: `Triggered` null, no AP row on the block); `A_shaken_unit_striking_back_counts_as_fatigued_so_its_furious_six_adds_no_hit_under_3_6` (Review Focus 4; p.10 unchanged: Shaken strike-back is fatigued).
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS; `GfRollsTests`, `GfMeleeTests`, `RollEffectsTests` PASS. **Step 5:** commit `git commit -am "3.6.0 C2: fatigued sixes trigger no special rule"`.

---

### Task C3: Melee loss by wounds taken — **Opus**

Rule: 3.6.0 p.10 *"must compare the number of wounds each unit took during this activation instead. The unit with the highest total loses"*.

**Files:**
- Modify: `server/game/Rules/GrimdarkFuture/GfMelee.cs:318-339` (`CompareStep`: when `MeleeLossByWoundsTaken`, each side's total is the wounds it **took** in the activation's frame, the charge move's dangerous-terrain wounds included (`GfMoves` writes `took_<model>` into deal-wounds frames under the activation: sum them from `ctx.History` across the activation, not only the melee frame); Fear still adds to the opponent's taken total (3.6.0 p.14 Fear: *"enemies count as having taken +X wounds"*); `loser` is the higher total), `grimdark-future-3.6/core.properties.json` Fear `ruleText`
- Test: new `GfMeleeCompare36Tests.cs`

- [ ] **Step 1: Failing test**: `Wounds_from_the_charge_moves_dangerous_test_count_against_the_charger_under_3_6` (charger crosses Dangerous ground, loses 1 to the test, melee is 1–1 in wounds caused: 3.5.1 a draw (`loser` 0); 3.6 the charger loses (`loser` 1)). A second: `Fear_adds_to_the_enemys_taken_total_under_3_6` (same result as 3.5.1's caused reading in a plain two-unit melee).
- [ ] **Step 2:** FAIL. **Step 3:** implement as a 3.6 branch inside `CompareStep` reading the flag (no new step: the step's inputs and outputs are unchanged). **Step 4:** PASS. **Step 5:** commit `git commit -am "3.6.0 C3: melee loss by wounds taken"`.

---

### Task C4: The Dangerous Terrain Test is the unit's — **Opus**

Rule: 3.6.0 p.12 *"If any model in a unit moves into or through Dangerous terrain, or activates in it, then the entire unit must take a Dangerous Terrain Test. To take a Dangerous Terrain Test, roll as many dice as the number of wounds it would take to fully destroy the unit, and for each result is 1 the unit takes one wound."*

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfMoves.Dangerous36.cs` (`DangerousTerrain36()` sequence: `UnitDangerousTestStep` rolls `WoundsToDestroy(unit)` = Σ over living models (Tough or 1 − wounds taken) dice, threshold 2 as today, counts 1s; `DangerousDealStep` shared), `GfMoves.cs:19,69-83,155-194,219-277` (the 3.6 path: `TestedThisActivation` is read per **unit** (`dangerous_tested_unit`) when `UnitDangerousTest`; `ActivationTerrainStep` and `ApplyMove` push `DangerousTerrain36` when the edition says so; `DangerousDice` unchanged for 3.5.1), `GfRules.Sequences.cs:14` (`GfMoves.DangerousTerrain36()` added to both catalogs: a sequence name `dangerous-terrain-36`), `GfContainers.cs` (`transport-destroyed` uses the edition's test), `grimdark-future-3.6/core.properties.json` (Transport `ruleText`: 3.6.0 p.15)
- Test: new `GfDangerous36Tests.cs`

- [ ] **Step 1: Failing tests**: `One_model_crossing_dangerous_ground_rolls_the_whole_units_remaining_wounds_under_3_6` (5 models Tough(1), one crosses: 3.5.1 one die; 3.6 five dice; a unit of 3 Tough(3) with 2 wounds taken: 7 dice); `The_unit_tests_once_per_activation_under_3_6` (two models cross at different points: one test); `A_destroyed_transports_passengers_test_as_a_unit_under_3_6`.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** PASS; `GfShootingTests:214,317`, `GfRushChargeDangerousTests` (3.5.1) PASS. **Step 5:** `docs/design/interpreter/opr-sequences.md` gains the `dangerous-terrain-36` sequence beside the 3.5.1 one. **Step 6:** commit `git commit -am "3.6.0 C4: the unit takes one Dangerous Terrain Test"`.

---

### Task C5: Terrain types — Steep, Unstable, Dense, Sight Blocking — **Opus**

Rule: 3.6.0 p.12 Unstable *"If any model in a unit moves over 6” when moving into or through unstable terrain, then it counts as Dangerous terrain."*; Steep *"Units moving onto steep terrain count it as being in Difficult terrain, but units moving off it count it as open terrain."*; Dense, Sight Blocking as in the diff (label changes only: geometry already reads `SeeInAndOut` as "into and out of, not through").

Decisions: `Steep` is one reading in both editions (3.5.1 p.12 Hills "Difficult when moving up" and 3.6.0 "Difficult moving onto, open moving off" are the same geometry for a solid hill), so it is edition-free in movement and only its **word** differs. Unstable is the 3.6 reading of `DangerousOnRushCharge`: a model's path counts the volume as Dangerous when that model's path length exceeds 6" and enters or crosses it. Terrain words come from the server (`IGameRules.TerrainWords` → `Welcome.TerrainWords` and `RecordingMeta.TerrainWords`), so no UI encodes an edition.

**Files:**
- Modify: `libs/Tabletop.Geometry/Volumes.cs:35-48` (`Steep = 128`, doc: both books' words), `libs/Tabletop.Content/BuiltinTerrain.cs:15-21` (`steep`), `libs/Tabletop.Geometry/Movement.cs:47` (`Check(...)` gains `IReadOnlyList<WorldVolume>? steep = null`: a steep volume is difficult for this move when the start is not inside its footprint), `libs/Tabletop.Protocol/Asks.cs:98` (`MoveRegion.SteepVolumeIds` after `DifficultVolumeIds`, default `[]`), `libs/Tabletop.Protocol/Wire.cs:42` (`Welcome.TerrainWords: IReadOnlyDictionary<string,string>`), `server/game/Rules/IGameRules.cs` (`IReadOnlyDictionary<string,string> TerrainWords => Empty`), `GfRules.cs` (from `Edition.TerrainWords`, snake_case keys), `GfEdition.cs` (the two `Steep` words), `server/game/Rules/GrimdarkFuture/GfMoves.cs:35-37` (`DangerousFor(terrain, rushOrCharge)` kept; new `DangerousFor36(terrain, pathLength)`; `ApplyMove` picks by `UnstableByDistance`, per model with its own path length), `GfMoves.Terrain.cs:51-56` (`RegionVolumes` lists `Steep` ids), `GfMoves.Breaches.cs:30-60` (`Route`/`DifficultCapBreach` pass steep volumes the model starts outside), `server/game/Persistence/RowMapping.cs:21-36` (a `steep` column), `server/management/Content/VolumeRows.cs`, `server/management/Content/Layouts/LayoutRules.cs:48,64`, `server/game/Setup/DraftLimits.cs:15,39`, `server/management/Content/Assets/AssetEndpoints.cs:199-206` ("eight terrain declarations"), create `server/management/Persistence/Migrations/024_steep_terrain.sql` (`alter table management.asset_volume add column steep boolean not null default false; alter table management.terrain_volume add column steep boolean not null default false;`), `server/game/Interpreter/Battlefield.cs:89-91` (`steep` name), `client/src/Core/MoveRegions.cs:34-70` and `ContactCheck.cs:51` and `libs/Tabletop.Geometry/Contacts.cs:14` (`ContactMove.Steep`), `sim/Answers/AnswerPolicy.Coherency.cs:76-98` (`FormationTerms` steep), `viewer/src/lib/play/contactCheck.ts:25`, `viewer/src/lib/geometry/types.ts:19`, `libs/Tabletop.Geometry.Wasm/Facade.cs:82` (`ContactTerms.Steep`), `client/src/Core/TerrainLabel.cs:12-17`, `TerrainGroups.cs:28-33`, `viewer/src/lib/setup/terrainCard.ts:20-23`, `viewer/src/lib/play/terrainLabel.ts:25` (words from `Welcome.TerrainWords` with today's words as the fallback when a key is absent), `web/src/lib/units/volumes.ts:4-7` and `web/src/lib/library/volume-edit.ts` (`steep` editable), `sim/Recording/Recording.cs:20` (`RecordingMeta.TerrainWords`)
- Test: `tests/Tabletop.Geometry.Tests/MovementTests.cs` (steep), `tests/Tabletop.Content.Tests/BuiltinTerrainTests.cs`, `tests/Tabletop.GameServer.Tests/GfTerrain36Tests.cs` (new), `tests/Tabletop.Management.Tests/MigrationTests.cs` (`steep` on both tables), `tests/Tabletop.Client.Tests/TerrainLabelTests.cs`, `viewer/tests/terrainLabel.test.ts`, `tests/Tabletop.Geometry.Wasm.Tests/AgreementCases.cs` (a `steep` contacts case), fixture regenerations (`reference-move.expected.json` unchanged: no steep volume there; `geometry.cases.json` regenerated; `wire.generated.ts`, `recording.generated.ts`)

- [ ] **Step 1: Failing tests**: Geometry `A_steep_volume_is_difficult_when_entered_and_open_when_left`; server `An_advance_with_fast_over_six_inches_through_ruins_tests_under_3_6_and_a_five_inch_charge_does_not` (3.5.1: the charge tests, the Advance does not; 3.6 the reverse); `Terrain_words_follow_the_edition` (`V351.TerrainWords["see_in_and_out"] == "See in and out"`, `V360` → `"Dense"`; `Welcome.TerrainWords` on connect); label tests: the client label uses the words given, falls back to today's.
- [ ] **Step 2:** FAIL. **Step 3:** implement (the long list above; split `GfMoves.cs` if it passes 500 lines: `GfMoves.Dangerous.cs`). **Step 4:** every project's unit tests, `./scripts/check-client`, viewer check and tests, web tests — PASS; migration E2E PASS; `dotnet run --project sim -- suite --only core-ruins,core-round-the-terrain` PASS (3.5.1 unchanged).
- [ ] **Step 5: Recipes**: `server/game/AGENTS.md` (terrain declarations are eight; the two readings of `DangerousOnRushCharge`; `TerrainWords`); `client/AGENTS.md` and `viewer/AGENTS.md` (terrain words come from `Welcome`); `docs/design/geometry/design.md` (Steep).
- [ ] **Step 6:** commit `git commit -am "3.6.0 C5: Steep terrain, Unstable by distance, terrain words per edition"`.

---

### Task M1: Side Battle legs per edition — **Opus**

Rule: 3.6.0 p.21 diagram: legs 36" (short edge) and 48" (long edge); 3.5.1 p.22: 30" and 54".

**Files:**
- Modify: `libs/Tabletop.Geometry/ZoneStyles.cs:22-96` (`ZoneLegs(double SideBattleLong, double SideBattleShort)` record with `Default = (0.75, 0.625)`; `Resolve(shapeId, table, ZoneLegs? legs = null)`, `Contains`, `BaseFullyWithin` overloads take it), `libs/Tabletop.Protocol/Wire.cs:42` (`Welcome.Zones: ZoneLegs`), `server/game/Rules/IGameRules.cs` (`ZoneLegs Zones => ZoneLegs.Default`), `GfRules.cs` (from `Edition`), `GfRules.Deployment.cs:49-52` (pass the edition's), `client/src/Table/ZoneNode.cs:20`, `client/src/Core/CameraFocus.cs:61` (from the session's `Welcome.Zones`), `libs/Tabletop.Geometry.Wasm/Facade.cs:115-127` (`ZonePolygons`/`ZoneContains`/`ZoneHoldsBase` requests gain optional legs), `viewer/src/lib/board/*` zone drawing (pass the welcome's legs), `sim/Run/HandPlacement.cs:78,85` and `sim/Suite/FreeForAllChecks.cs:61` (legs from the compiled scenario's rules), `sim/Recording/Recording.cs` (`RecordingMeta.Zones`)
- Test: `tests/Tabletop.Geometry.Tests/ZoneStyleTests.cs:37-48` (3.5.1 test unchanged; add `Side_battle_legs_36_and_48_under_the_3_6_0_diagram` with `new ZoneLegs(2.0/3, 0.75)` → vertices (48,0) and (0,36) on 72×48), `tests/Tabletop.GameServer.Tests/GfDeploymentZoneTests.cs` (a model at (46,2) is inside zone A under 3.6, outside under 3.5.1), `tests/Tabletop.Geometry.Wasm.Tests/AgreementCases.cs` (`zone_polygons_side_battle_36`), viewer fixture regeneration

- [ ] **Step 1: Failing tests** as listed. **Step 2:** FAIL. **Step 3:** implement. **Step 4:** Geometry, Wasm, GameServer, Client, Sim tests; `cd viewer && npm test` — PASS. **Step 5:** `docs/design/geometry/design.md` (Side Battle diagram: both editions' legs). **Step 6:** commit `git commit -am "3.6.0 M1: Side Battle legs per edition"`.

---

### Task M3: Built-in terrain under 3.6.0 — **Sonnet** (after C5)

Rule: 3.6.0 p.30 *"5 = Ruins or Debris Counts as Cover and Unstable terrain. 6 = Forest or Plantation Counts as Difficult, Cover, and Dense terrain. … 8 = Hill Counts as Cover and Steep terrain. … 12 = Steep Hill Counts as Cover, Steep, and Unstable terrain."*

**Files:**
- Modify: `client/content/builtin/catalog.json`: `hill`, `large-hill`, `small-dunes`, `steep-hill` gain `steep`; `steep-hill` gains `dangerous_on_rush_charge` (3.6.0 "Unstable"; under 3.5.1 it reads as Dangerous on Rush/Charge, which 3.5.1 p.18 does not say for steep hills — **decision**: leave `steep-hill` without it; p.18's 3.5.1 line has no danger and a shared piece must be right in both books; the 3.6.0 generator's steep hill is not a built-in piece); the hill `notes` become "3.5.1 p.12: units on top may ignore one unit/terrain for line of sight (not enforced; 3.6.0 drops it)"; `steep-hill`'s note drops "Difficult terrain for units moving up" (now the `steep` flag).
- Modify: `client/content/builtin/tables/*.json` where hills appear (declaration strings gain `steep`).
- Test: `tests/Tabletop.Client.Tests/BuiltinContentTests.cs` (hills carry `Steep`), `tests/Tabletop.Content.Tests/PlanetTerrainTests.cs:31-46` (counts unchanged: steep is not sight/cover/difficult), `tests/Tabletop.Content.Tests/BuiltinTerrainTests.cs:41`

- [ ] **Step 1: Failing tests**: `Hills_are_steep`. **Step 2:** FAIL. **Step 3:** edit the JSON. **Step 4:** Content and Client tests PASS; `TableSides`/`RandomTables` tests PASS. **Step 5:** commit `git commit -am "3.6.0 M3: hills are Steep; hill notes name their edition"`.
