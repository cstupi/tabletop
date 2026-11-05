# Grimdark Future 3.6.0 beside 3.5.1 — Implementation Plan (index)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Executors run on Opus or Sonnet as each task names; never on Fable.

**Goal:** Grimdark Future 3.6.0 runs as a second edition beside 3.5.1, chosen per game (3.6 the default for new games), with armies stamped by edition, a mismatch refused, the rules suite run per edition, and 3.6 army books generated from the 3.6 dump.

**Architecture:** An edition is a game-rules id: `grimdark-future` (3.5.1, untouched) and `grimdark-future-3.6`. Each edition owns its content folder under `server/game/Content/<edition>/`; one C# implementation serves both, reading what differs from `GfEdition.Of(state)` (numbers and yes/no) and from per-edition sequence catalogs where a step's logic changes (the 3.5.1 step is never edited; a 3.6 copy sits beside it). Armies carry `edition`; `ArmyLimits` refuses a mismatch. The suite's `TestGame` carries its edition; coverage is per shipped set.

**Tech Stack:** .NET 10 / C# (game server, management, sim, generator), xUnit, Postgres migrations (plain SQL), SvelteKit + Vitest (web, viewer), Godot 4.7 C# (client; hand-tested nodes, Core unit-tested).

**Specs:** `docs/superpowers/specs/2026-10-29-rule-set-versions-design.md` (structure and build order), `2026-10-29-gf-3-6-0-rules-diff.md` (C1–C8, S1–S17), `2026-10-29-gf-3-6-0-rules-diff-modules.md` (M1–M5, army books, the 35 version-naming files). Ids below refer to them.

## Files of this plan

| File | Phase | Design step |
| --- | --- | --- |
| `index.md` | this file: constraints, review focus, task table, parallel groups | — |
| `phase-1-armies.md` | the 3.5 dump move; `army.edition`, import stamping, `ArmySnapshot.Edition`, edition shown | 1–2 |
| `phase-2-plumbing.md` | the `grimdark-future-3.6` id, `GfEdition`, two rules instances, content folders, seeder/generator/compiler by folder | 3 |
| `phase-3-game-choice.md` | `PropertiesSummary.Default`, create-dialog defaults, lobby picker, session re-resolve, `GameSummary.RuleSet`, headers | 4 |
| `phase-4-skill.md` | the rulebook skill: `v3.5.1/` move, `v3.6.0/pages` transcription, SKILL.md edition step | 5 |
| `phase-5-rules-core.md` | C1–C5 and M1, M3 (core rules, terrain, geometry) | 6 |
| `phase-5-rules-special.md` | S1–S16 (special rules), the army-edition refusal switch-on | 6 |
| `phase-6-suite.md` | `TestGame.Edition`, coverage per set, run-twice, 3.6 copies of changed games, lifecycle, Tests page | 7 |
| `phase-7-army-books.md` | generator `--edition`, 3.6 books and generated part, the 3.6 hand part, units file per edition, `army:3.6:` games | 8 |
| `phase-8-docs.md` | AGENTS.md files, design docs, handoff, the version-naming files | 9 |

## Global Constraints

- "`grimdark-future` stays the 3.5.1 id: every existing game, property set and alias keeps working with no migration." No 3.5.1 content file's composed hash changes (a test pins it); no 3.5.1 C# code path is edited for a 3.6 behaviour.
- "`grimdark-future-3.6` is the 3.6.x id." Display names are the core documents' `name`: `Grimdark Future v3.5.1`, `Grimdark Future v3.6.0`. "UI shows the name, never the id."
- "Existing and unfinished games stay on 3.5.1; new games default to 3.6.0."
- "Armies know their edition": `army.edition text not null` checked in (`'3.5'`, `'3.6'`); existing rows `'3.5'`; `PickArmy` and `EndSetup` refuse a mismatch once phase 5 lands.
- "Copy the data, share the code": a 3.6 difference that is a number or a yes/no is a `GfEdition` value; a step whose logic changes gets a 3.6 copy chosen by edition in the sequence catalog; a rule whose text changes gets its `ruleText` replaced in the 3.6 core file whatever its mark.
- Every 3.6.0 citation names the edition: `3.6.0 p.N`. A bare `p.N` means 3.5.1 and is never renumbered.
- The rules engine lives only in `server/game/`; `sim/` holds no second implementation; expected values in suite checks are literals from the rule text.
- Test-driven, xUnit under `tests/`, C# only. Files over 500 lines are split before a long edit. `*.book.json` and the generated rules part are never edited by hand.
- Army Forge is never called from a test; `fetch` is the only network call and refuses to overwrite a dump of another edition.
- No client, viewer or web string encodes an edition; they show the server's set name and the server's text.

## Review Focus

Inputs the specs imply that no task's tests would otherwise exercise; each line's test is pinned to the owning task.

1. A 3.5.1 game whose log was written before this change hydrates and replays byte-for-byte: `Hydrator` resolves `grimdark-future` to the `V351` instance and the composed 3.5.1 hash is unchanged (phase 2, Task 2.2's hash test and Task 2.1's registry test).
2. A lobby that changes the set after armies were picked: a 3.5 army picked under 3.5.1 must be dropped or refused when the creator switches to 3.6 (phase 3, Task 3.3's `Changing_the_edition_drops_armies_of_the_other_edition`).
3. An army imported between Army Forge's switch (2026-10-29T23:48Z) and the migration is a 3.6 list stamped `'3.5'`: the backfill marks it `'3.6'` by a rule name only 3.6 books use (phase 1, Task 1.2's migration test with an army carrying `Bravery Aura`; `Armor` is not a key, it is in the 3.5 hand part).
4. A Shaken unit striking back "counting as Fatigued" under 3.6: its 6s must not trigger Rending or Furious (phase 5 special, Task C2's second test).
5. An Assault weapon (3.6 books: 48 of 49) must both shoot in the shooting phase and strike in melee, while a weapon of the same shape under 3.5.1 is ranged only; and a Cumbersome unit that advanced must not shoot (phase 5 special, Tasks S3 and S6, paired tests).

Not a review item: Flying under 3.6 (S9) changes no engine behaviour; `TerrainRules.IgnoresDangerous` already covers Flying at activation and on the move under 3.5.1, so S9 is rule text plus tests that pin the shared reading.

## Task table

Each task names its model in its phase file. `Opus` for engine, geometry, the migration with a backfill and design-sensitive work; `Sonnet` for mechanical edits, data copies, transcription and docs. A task adds or updates a module `AGENTS.md` recipe where its phase file says so; a task with no recipe line adds none, and Task 8.1 checks every recipe landed once.

| Phase | Tasks (model) | Count | Group |
| --- | --- | --- | --- |
| 1 armies | 1.1 dump (S), 1.2 migration (O), 1.3 `Army.Edition` (S), 1.4 snapshot (S), 1.5 web (S), 1.6 lobbies (S) | 6 | A |
| 2 plumbing | 2.1 ids/`GfEdition`/instances (O), 2.2 content folders + hash pin (O), 2.3 generator folders (S), 2.4 `ScenarioCompiler` (S) | 4 | B |
| 3 game choice | 3.1 `Default` (S), 3.2 create dialogs (S), 3.3 session re-resolve (O), 3.4 lobby pickers (S), 3.5 `RuleSet` names (S) | 5 | C |
| 4 skill | 4.1 move + edition step (S), 4.2 pp.1–20 (S), 4.3 pp.21–23 diagrams (O), 4.4 pp.24–74 + index (S) | 4 | D |
| 5a core | C1 (O), C2 (O), C3 (O), C4 (O), C5 (O), M1 (O), M3 (S) | 7 | E |
| 5b special | S1 (S), S2 (O), S3 (O), S4 (O), S11 (O), S5 (O), S6 (O), S7/S17 (S), S8 (S), S9 (S), S10 (O), S12 (O), S13 (O), S14 (S), S15 (O), S16 (O), 5.R refusal (O) | 17 | F |
| 6 suite | 6.1 `TestGame.Edition` (O), 6.2 Tests page (S), 6.3 run-twice (S), 6.4 3.6 copies (O), 6.5 lifecycle (S), 6.6 handoff numbers (S) | 6 | G |
| 7 army books | 7.1 generate 3.6 (S), 7.2 byte-identity test (S), 7.3 units file per edition (S), 7.4 hand part renames (O), 7.5 hand part changed/new, batched (O), 7.6 `army:3.6:` and `spells:3.6:` games (O), 7.7 docs (S) | 7 | H |
| 8 docs | 8.1 AGENTS (S), 8.2 design docs (S), 8.3 comments (S), 8.4 handoff/shipped (S) | 4 | I |

60 tasks; 23 rule-change tasks in phase 5 (C1–C5, M1, M3, S1–S6, S7/S17, S8–S16).

## Parallel groups and order

- **Wave 1 (parallel worktrees):** A (phase 1; 1.1 may run in its own worktree beside 1.2–1.6), B (phase 2, sequential), D (phase 4; 4.2–4.4 in parallel after 4.1). A, B and D touch disjoint files (A: management, `libs/Tabletop.Content/Army.cs`, web, client/viewer army labels; B: `server/game` rules, content, seeder, `scripts/ArmyBooks` paths, `sim/Scenarios`; D: `.claude/skills/licensed/`). Both A and B edit `scripts/ArmyBooks/Program.cs` (1.1 the dump folder, 2.3 the content folder): merge A's 1.1 before B's 2.3, or run 1.1 first.
- **Wave 2 (after B merges):** C (phase 3; 3.1, 3.2, 3.5 may run beside 3.3–3.4), E and F (phase 5), 6.1–6.2, 7.1–7.3. Phase-5 worktrees by file: `GfMelee` — C1, S11, S5 (that order); `GfMoves`/containers — C4, C5, S15, S9; `GfAttack`/`RollEffects`/data — C2, S12, S16, S1; casting — S4, S14; data-only — S8, then S7/S17 last of F; alone — C3, M1, S2, S3, S6, S10, S13; M3 after C5; 5.R after phase 3 and every other F task. Phase 4's pages should have merged before phase 5's review so each `3.6.0 p.N` citation is checked against a file.
- **Wave 3 (after E and F):** 6.3, 6.4, 6.5, 7.4, 7.5 and 7.6 (batched together), then 6.6, 7.7, and I.
- Within a group a task's steps are sequential. Each task commits on its own branch; merges go through `superpowers:finishing-a-development-branch`; a full `dotnet test`, `dotnet run --project sim -- suite` and the viewer/web suites run on `main` after each wave.

## Verification commands (the set every task picks from)

```bash
dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~<Type>"
dotnet test tests/Tabletop.Management.Tests --filter "FullyQualifiedName~<Type>"
dotnet test tests/Tabletop.Sim.Tests --filter "FullyQualifiedName~<Type>"
dotnet test tests/Tabletop.ArmyBooks.Tests
dotnet test tests/Tabletop.Geometry.Tests tests/Tabletop.Geometry.Wasm.Tests tests/Tabletop.Content.Tests
dotnet test tests/Tabletop.Client.Tests
dotnet run --project sim -- suite --only <game or group>        # in-process
dotnet run --project sim -- suite                               # full: coverage per edition
dotnet run --project sim -- suite --driver socket --only <game> # stack up
dotnet run --project sim -- lifecycle                           # stack up
cd viewer && npm run check && npm test
cd web && npm test
dotnet build client/Tabletop.csproj && ./scripts/check-client
dotnet test --settings tests/e2e.runsettings --filter "FullyQualifiedName~Migrations"   # stack up
```
