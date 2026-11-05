# Phase 6 — The rules suite per edition (design step 7)

Index: `index.md`. Spec: `2026-10-29-rule-set-versions-design.md` → "Sim rules suite". Group **G**: 6.1–6.2 after phase 2; 6.3–6.6 after phases 5a and 5b. `sim/AGENTS.md` → "Writing a suite game that holds" governs every new game.

Decisions:

- `TestGame.Edition` is a game-rules id string (default `RuleSets.Gf351`), and `TestGame.Editions` (`IReadOnlyList<string>`, default `[Edition]`) lists the editions a game runs under; `SuiteCatalog.All` expands a multi-edition game into one `TestGame` per edition named `<name>@3.6` (the 3.5.1 run keeps the bare name, so every existing `--only`, pin and report row is unchanged), group unchanged for core and special, `army:<edition>:<uid>` / `spells:<edition>:<uid>` for books (today's `army:<uid>` becomes `army:3.5:<uid>`; the viewer's Tests page and `--only` take the new spelling; the design names it).
- Coverage runs per shipped set: `Coverage.Of` is called once per edition with that edition's `PropertySet`, the generated part and the games of that edition; the report's coverage table gains an `edition` column; a full run fails on an uncovered entry of either set. Until phase 7 lands, the 3.6 set is a copy of 3.5.1 plus the phase-5 entries, so a full run's 3.6 coverage fails on every entry no 3.6 game covers — expected and the reason Task 6.3 runs the unchanged core/special games under both editions.

---

### Task 6.1: `TestGame.Edition`, `ScenarioCompiler` by edition, snapshots stamped — **Opus**

**Files:** `sim/Suite/TestGame.cs:76` (`string Edition = RuleSets.Gf351`, `IReadOnlyList<string>? Editions = null` as init properties), `sim/Scenarios/Scenario.cs` (`Scenario.Rules` id, builder `.Rules(id)`), `sim/Scenarios/ScenarioCompiler.cs:74-82,117` (`props = CorePropertiesOf(scenario.Rules)`, `GameInfo.GameRules = scenario.Rules`, `ArmySnapshot { Edition = RuleSets.EditionOf(scenario.Rules) }`), `sim/Suite/SuiteCatalog.cs:11,20-30` (`All` expands `Editions`; `Select` matches `name`, `name@3.6`, group), `sim/Suite/SuiteRunner.cs:~25,44,68` (`Generated` per edition; `Coverage.Of` per edition over `CorePropertiesOf(edition)`), `sim/Suite/Coverage.cs:28` (`CoverageEntry.Edition`), `sim/Suite/SuiteReport.cs`, `SuiteReportFiles.cs:~102` (`Edition` column on `SuiteGameResult` and the coverage table), `sim/Drivers/SocketRowSeeder.cs:44-50` (already from `scenario.Info`).
**Test:** `tests/Tabletop.Sim.Tests/Suite/SuiteSelectionTests.cs` (`A_two_edition_game_expands_to_a_bare_and_an_at_3_6_name_and_only_selects_either`), `ScenarioCompilerTests` (`A_3_6_scenario_compiles_against_the_3_6_set_and_stamps_its_snapshots_3_6`), `SuiteReportFilesTests` (the edition column), `FiredAndCoverageTests` (coverage of two sets reports both).
Steps: failing → FAIL → implement → `dotnet test tests/Tabletop.Sim.Tests`; `dotnet run --project sim -- suite --only core` (every game still 3.5.1, verdict unchanged) → recipe in `sim/AGENTS.md` → The rules suite ("A `TestGame` runs under `Edition`; `Editions: [Gf351, Gf360]` runs it twice, the 3.6 run named `<name>@3.6`; groups `army:<edition>:<uid>`") → commit.

---

### Task 6.2: The runner service and the Tests page show and filter by edition — **Sonnet**

**Files:** `sim/Serve/ServeApp.cs:34-37` (`GET /suite` games carry `edition`), `viewer/src/lib/suite/client.ts` (`SuiteGame.edition`, `SuiteGameResult.edition`, `CoverageEntry.edition`), `viewer/src/lib/suite/model.ts` (`editionsOf`, `toggleEdition(page, edition)`; `groupsOf` keyed by group then edition), `viewer/src/lib/components/TestsPage.svelte` (an edition filter row above the groups; `Edition` column in the games and coverage tables).
**Test:** `tests/Tabletop.Sim.Tests/Serve/ServeTests.cs`, `viewer/tests/suite.test.ts` (an edition toggle selects that edition's games; the coverage cell shows both sets).
Steps → `cd viewer && npm test` → commit.

---

### Task 6.3: Unchanged core and special games run under both editions — **Sonnet**

Every core and special game whose checks quote no changed rule gets `Editions: [Gf351, Gf360]`. Changed-rule games (listed in 6.4) stay 3.5.1-only. From the sim trace: 3.5.1-only are `core-charge-and-strike-back` (fatigue, p.9 strike), `core-morale-break` (p.10 compare), `core-ruins` (p.11/p.12 dangerous), `core-rough-ground` (p.11 dangerous test), `core-transport-run` (Transport text — unchanged behaviour; **runs both**), `core-deployment` (Scout 12"), `core-hold-the-markers` (runs both), `special-charge-into-spears` (Counter/Impact), `special-swift-and-slow` (Flying p.13 → runs both: S9 is text), `special-banner-of-eternal-flame` (p.36 → runs both; its check names 3.5.1's page: the 3.6 run's failure line would quote p.36 — acceptable, a check name is not a page citation of the run), `special-command-squad` (Caster → 3.5.1 only), `special-regrowth-under-rending-fire` (Regeneration `all` → 3.5.1 only), `special-long-range-gunline` (Stealth → 3.5.1 only), `special-artillery-emplacement` (3.5.1 only), `special-stealth-at-nine-and-a-half` (3.5.1 only), `special-cast-mid-move` (3.5.1 only), `special-scouts-and-ambushers` (3.5.1 only), `special-aircraft-overhead` (S1 → 3.5.1 only), `special-blast-deadly-takedown`, `special-takedown-picks-one`, `special-ambush-*`, `core-four-actions`, `core-over-a-lower-model`, `core-firing-line`, `core-overkill`, `core-cover-and-sight`, `core-fence-cover`, `core-four-teams`, `core-perimeter-sight`, `core-hybrid-sight` (its check string names p.56: the 3.6 run passes the same check), `core-melee-no-separation`, `core-transport-deploy`, `core-round-the-terrain`, `core-team-game`, `core-self-play` (all run both).
**Files:** `sim/Suite/SuiteCatalog.Core*.cs`, `SuiteCatalog.Special*.cs` (the `Editions` init on each listed game).
**Verify:** `dotnet run --project sim -- suite --only core,special` — every `@3.6` run passes; a failure is a real 3.6 engine defect (fix it in the owning phase-5 task's code, or pin with `Pinned("D17", …)` and list it in `docs/handoff.md`). Commit.

---

### Task 6.4: 3.6 copies of the changed-rule games — **Opus**

One new game per changed rule, 3.6-only (`Edition = Gf360`), check names quoting `3.6.0 p.N` and the 3.6.0 sentence, built by construction (`sim/AGENTS.md` steps 1–5), in new files `sim/Suite/SuiteCatalog.Core36.cs` and `SuiteCatalog.Special36.cs`:

| Game | Covers / checks | From |
| --- | --- | --- |
| `core-charge-and-strike-back@3.6` | reach 3" (C1), fatigued 6s trigger nothing (C2) | `Core.Melee.cs:20` |
| `core-morale-break@3.6` | loser by wounds taken incl. a charge's dangerous wound (C3) | `Core.Melee.cs:106` |
| `core-ruins@3.6` | Unstable: an 8" Advance tests, a 5" Charge does not; the unit tests once (C4, C5) | `Core.Ruins.cs` |
| `core-rough-ground@3.6` | unit test dice = wounds to destroy (C4) | `Core.Actions.cs:62` |
| `core-deployment@3.6` | Scout 9" and 9" clearance (S13) | `Core.Mission.cs:71` |
| `core-side-battle@3.6` (new, both editions) | zones 36"/48" vs 30"/54" (M1): `Zones(ZoneStyles.SideBattle)`, every model deployed inside its zone | — |
| `special-charge-into-spears@3.6` | Impact before Counter, no reduction, Counter asked and lost when fatigued (S5, S11) | `Special.Melee.cs:14` |
| `special-command-squad@3.6` | spenders out of sight within 18" of the caster model (S4) | `Special.Melee.cs:152` |
| `special-cast-mid-move@3.6` | one cast window per activation, target out of sight (S4) | `Special.Casting.cs` |
| `special-regrowth-under-rending-fire@3.6` | Regeneration with a Hero lacking it (S8) | `Special.Shooting.cs:14` |
| `special-long-range-gunline@3.6` | Stealth majority, Unstoppable keeps own penalties (S8, S16) | `Special.Shooting.cs:175` |
| `special-artillery-emplacement@3.6` | Artillery moves; Hold +1 over 12"; -1 while unmoved (S2) | `Special.Shooting.cs:228` |
| `special-aircraft-overhead@3.6` | -1 to hit at an Aircraft; no embark into an Aircraft Transport (S1, S15) | `Special.Deployment.cs:168` |
| `special-indirect-on-hold@3.6` (new) | Indirect after an Advance needs sight; on a Hold ignores sight-blocker cover (S12) | — |
| `special-hero-aura@3.6` (new) | a Tough(9) Hero within 6" lends Quality; Shaken lends nothing (S10) | — |
| `special-assault-and-cumbersome@3.6` (new) | an Assault weapon shoots and strikes; Cumbersome holds/advances, no shot after moving (S3, S6) | — |
| `special-shred-and-amplifier@3.6` (new) | Shred extra wound on a 1; Spell Amplifier +1 casting (S14) | — |

**Verify:** each on seeds 1–5 (`sim/AGENTS.md` step 4); `dotnet run --project sim -- suite` full: both coverage tables complete except the 3.6 army/spell entries phase 7 covers. Commit per game or per file.

---

### Task 6.5: Lifecycle per edition — **Sonnet**

`sim/Lifecycle/LifecycleGame.Lobby.cs:15-19`: `LifecycleShape` gains `Rules` (default `Gf351`); `two-players` gains a `two-players-3.6` twin on `Gf360` whose armies are 3.6 lists: `LifecycleArmy.List(book)` builds the import body from `Lifecycle/Armies/<uid>.json` (3.5 shapes) — add `Lifecycle/Armies/3.6/<uid>.json` for one book cut from the 3.6 dump (three units) and `DevArmyImportRequest` stamps `Served` (3.6) — so the 3.6 shape's armies are 3.6 and the 3.5 shapes' fixture armies must be stamped 3.5: `POST /v1/dev/armies/import` gains an optional `edition` field (dev only) that the lifecycle sets to the shape's edition. The lobby step asserts `first.Default` for the 3.6 set.
**Test:** `tests/Tabletop.Sim.Tests/Lifecycle/LifecycleTests.cs` (E2E) gains the shape; `tests/Tabletop.Management.Tests/DevArmyImportTests.cs` (`edition` stamps). Verify `dotnet run --project sim -- lifecycle` 8 of 8. Commit; `sim/AGENTS.md` → The lifecycle suite names the shape.

---

### Task 6.6: Handoff numbers — **Sonnet**

`docs/handoff.md` → "The rules suite" and "Checks": the game count per edition, coverage per set, the `@3.6` naming, `--only army:3.5:<uid>`. Commit.
