# The 3.6.0 text-only rules played — Implementation Plan (index)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Executors run on Opus or Sonnet as each task names; never on Fable. Opus for engine and suite work, Sonnet for data, generator and docs. Worktrees under `.worktrees/<task>` on branches `wt/<task>` from `gf-3-6-0` (helper `.superpowers/sdd/index/wt.sh`); at most three executors at once; a spec review and a quality review per task (Opus on engine).

**Goal:** every 3.6.0 catalog entry plays: the 19 text-only rules and the 24 spells that grant them, plus Brace and Parry from Army Forge's common-rules text, with a suite game each; the 3.6 coverage line reads `0 not automated`. Three half-plays fixed on the way: Thrust in Melee Buff's AP(+1), the Marks' second half, Predator's "may".

**Spec:** `docs/superpowers/specs/2026-10-30-gf-3-6-0-text-only-rules.md` (the design of every addition and the rulings; this plan does not restate them). Ids in the phase files refer to its sections.

**Tech Stack:** .NET 10 / C# (game server, generator, sim), xUnit, one plain-SQL migration, no client or web change (both clients answer `Options` and `PieceSelection` Asks generically).

## Files of this plan

| File | Tasks |
| --- | --- |
| `index.md` | this file: constraints, review focus, task table, parallel groups, verification |
| `phase-1-engine.md` | 0.1 the `GfAttack.cs` split; 1.1–1.13 grammar points and engine reads |
| `phase-2-content.md` | 2.1–2.5 generator (common rules, token loss, empty attack type), hand entries, regeneration |
| `phase-3-suite.md` | 3.1–3.4 the `army:3.6:` games, the spell tables, the full run and coverage |
| `phase-4-docs.md` | 4.1–4.2 design docs, AGENTS recipes, handoff |

## Global constraints

- The 3.5.1 content stays byte-identical (`ContentFoldersTests` pins the hash; `GeneratedOutputTests` pins the 3.5 generator output) and its suite stays 545 of 545 with no pin. An engine change that reaches a 3.5.1 game is a rule fix; a 3.5.1 *behaviour* change that is no fix is refused (the `spent_marker` duration keeps its meaning; `one_marker` is new).
- `server/game/Effects/` names no OPR rule; a rule name lives in `GfNames` or a content file. Grammar names are the spec's.
- Printed text is the authority: every `ruleText` byte for byte from the 3.6 dump, or from `common-rules.json` for Brace and Parry; every check quotes it. Every 3.6.0 citation is `3.6.0 p.N`.
- `*.book.json` and `army-book-rules.part.json` are never edited by hand; a name is authored in `grimdark-future-3.6/army-rules.part.json` and the generator is run.
- Test-driven, xUnit under `tests/`; files over 500 lines are split before a long edit (Task 0.1 first).
- Docker is down: E2E, the socket driver and `sim -- lifecycle` do not run. Migration `027` is reviewed and gets its `MigrationTests` assertion; the first stack-up run of `dotnet test --settings tests/e2e.runsettings --filter "FullyQualifiedName~Migrations"` is a step of the final verification.
- No executor runs `npm ci` in a worktree; never touch `main`, never push, never tag.
- Every commit message ends with:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01RAGAqYwxWVQSdxiFRyNNjr
  ```

## Review focus

Inputs the spec implies that no task's own tests would exercise unless named; each is pinned to its task.

1. A 3.5.1 Piercing Tag (`spent_marker`, two markers) still gives AP(+2) once and spends both: Task 1.6's paired `[Theory]` over both editions (`one_marker` spends one, `spent_marker` all).
2. A once grant read in a *morale test* is still consumed in that frame (no attack root), and a grant read in an attack whose frame is undone leaves no `GrantRemoved`: Task 1.7.
3. Pierce's `positive_defense` drops cover's +1 and Shielded's +1 but not AP, and `RollRecord.Ignored` names Pierce for each: Task 1.2.
4. Scourge's counted wounds trigger a general morale test on a unit that took no real wound, and never change `WoundsThisActivation` or a model: Task 1.3.
5. Brace with five Spears takes one die (collapse by weapon name), none when the defender is Shaken: Task 1.12.
6. Versatile Defense's condition is cleared at the attack's end, so the second attack-sequence of the same activation asks again and may choose differently: Task 1.8.
7. A Mind Control target whose test passed is still eligible to a second carrier this round; one moved is not: Task 1.6.

## Task table

| Phase | Tasks (model) | Count | Group |
| --- | --- | --- | --- |
| 0 prep | 0.1 `GfAttack.cs` split (S) | 1 | A |
| 1 engine | 1.1 predicates + migration (O), 1.2 `positive_defense` (O), 1.3 `count_wounds` (O), 1.4 optional `add_hit` (O), 1.5 `remove_spell_tokens` (O), 1.6 targeting shapes + `one_marker` (O), 1.7 once grants at attack end (O), 1.8 `attacked` point (O), 1.9 dice count from parameter (O), 1.10 Ravage from grants (O), 1.11 Quick Throw (O), 1.12 Brace (O), 1.13 Parry (O) | 13 | B–H |
| 2 content | 2.1 common rules in fetch/generate (S), 2.2 translator: token loss, empty attack type (S), 2.3 hand entries, weapon rules (S), 2.4 hand entries, the rest (S), 2.5 Predator/Primal optional, regenerate, zero text-only (S) | 5 | I |
| 3 suite | 3.1 games: weapon rules, Brace, Parry (O), 3.2 games: picks, choices, moves, Auras, Quick Throw (O), 3.3 spell tables + the three fixes' games (O), 3.4 full run, coverage test, seeds (O) | 4 | J |
| 4 docs | 4.1 design docs and schema (S), 4.2 AGENTS recipes and handoff (S) | 2 | K |

25 tasks.

## Parallel groups and order

- **Wave 1:** A (0.1) alone first: every engine task after it edits a partial it creates. Beside it, 2.1 (generator fetch and descriptions; no engine dependency) and 1.1 (Effects/ and the migration; touches no `GfAttack` file).
- **Wave 2 (after 0.1 and 1.1 merge), by file:**
  - B `RollEffects`/`GfRolls`: 1.2.
  - C `GfAttack.Steps` roll-to-block and `GfMelee`/`GfActivationEnd` counts: 1.3, then 1.13 (both edit `resolve-hits`), in one worktree in that order.
  - D `GfAttack.Steps` roll-to-hit: 1.4; then 1.11 (`IndirectApplies`, small) in the same worktree.
  - E `GfCasting`/`GfPick`/`GfMarkers`/`LiveProperties.Gather`: 1.5, then 1.6.
  - F `StepContext`/`SequenceInterpreter` and the attack roots: 1.7, then 1.8 (its clearing rides 1.7's `attack-end`).
  - G `GfMoves`/`GfHooks`: 1.9; `GfStrikeRolls36`: 1.10; `GfMelee.ImpactStep`: 1.12. Three small tasks, one worktree each or one after another.
  - `PropertySetDocument.Validate` is edited by 1.1, 1.3, 1.4, 1.5, 1.6: each adds its own lines; merges are line-wise.
- **Wave 3 (after the engine tasks a batch needs):** 2.2 after 1.5; 2.3 after 1.1, 1.2, 1.3, 1.12, 1.13; 2.4 after 1.5–1.11; 2.5 last of phase 2. 3.1 may start when 2.3 merges, 3.2 when 2.4 merges, 3.3 when 2.5 merges; 3.4 after all of phase 3.
- **Wave 4:** 4.1 and 4.2 after 3.4, in parallel.
- Each task commits on its own branch and merges `--no-ff` into `gf-3-6-0` after its reviews; a full `dotnet build`, the unit suites it touched, and `dotnet run --project sim -- suite --only army:3.6:<uid>,…` run before the merge, the whole suite after each wave.

## Verification (the final run on `gf-3-6-0`)

```bash
dotnet build                                                       # 0 errors
dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.Sim.Tests && dotnet test tests/Tabletop.ArmyBooks.Tests \
  && dotnet test tests/Tabletop.Content.Tests && dotnet test tests/Tabletop.Management.Tests && dotnet test tests/Tabletop.Client.Tests
./scripts/check-client                                             # prints nothing
dotnet run --project sim -- suite                                  # PASSED; 3.5 545/545, 0 not automated; 3.6 N/N, 0 not automated
dotnet run --project scripts/ArmyBooks -- generate --edition 3.5   # byte-identical output
dotnet run --project scripts/ArmyBooks -- generate --edition 3.6   # reproduces the committed output; 0 text-only rules, 0 text-only spells, no "no description"
cd viewer && npm run check && npm test; cd ../web && npm test
# with the stack up (not while Docker is down):
dotnet test --settings tests/e2e.runsettings --filter "FullyQualifiedName~Migrations"
dotnet run --project sim -- suite --driver socket --only army:3.6:04z57ua0bwth37zh
```
