# The rules suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An end-to-end suite of themed sim games that exercises every catalog rule and every spell, checks that each
fires and that the core rules give exact results, reports coverage, cleans up after itself, and can be run and
watched from the viewer.

**Architecture:** The server records which catalog entries acted in each step (`StepCompleted.Applied`). The sim
gains a `Suite` layer: a `TestGame` is a `Scenario` plus what it covers, dice it fixes and checks; a runner plays
selected games on either driver, judges them, computes coverage and writes a report. Game content is four files, one
per group, filled in parallel. A `serve` mode hosts the runner over HTTP for the viewer's Tests page.

**Tech Stack:** `net10.0`, C#, xUnit; ASP.NET Core minimal APIs (inside `sim/`); Svelte 5 + Vite + Vitest (`viewer/`).

**Spec:** [`docs/superpowers/specs/2026-10-09-rules-suite-design.md`](../specs/2026-10-09-rules-suite-design.md).

**Plan form:** thin, at the owner's request. Each task states its files, its interfaces and the tests it must add;
the implementer writes the code test-driven against the repo's existing patterns.

## Global Constraints

- The rules engine lives only in `server/game/`; a rule is named only in `server/game/Rules/GrimdarkFuture/`. `sim/`
  holds no second implementation of the rules (`sim/AGENTS.md`): expected values in exact checks are literals taken
  from the rule text, quoted with their page.
- Rulebook pages: `.claude/skills/licensed/skills/grimdark-future-rules/pages/pNN.md`. Quote, never reason from memory.
- Every wire change is additive; `ProtocolVersion.Current` stays 1. After a change reachable from `SocketMessage` or a
  `Recorded*` type: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, `cd viewer && npm run check
  && npm test`. A change to `EventApplier`, `FrameProjector` or `LogLines` ports to `viewer/src/lib/live/`.
- Every suite game passes on both drivers. Socket runs need the compose stack up; never `docker compose down -v`.
- A suite run leaves nothing behind unless `--keep`: no rows, no recordings. The accounts are the fixed pool every run shares.
- Test-driven; xUnit under `tests/`. Commit per task with `Co-Authored-By: Claude <Model> <noreply@anthropic.com>`.

## Waves

Same-wave tasks own disjoint files and run in parallel, each in its own worktree from the integration branch.

| Wave | Task | Owns | Depends on | Model |
| --- | --- | --- | --- | --- |
| 1 | 1. `Applied` on the wire | `libs/Tabletop.Protocol/Events.cs`, viewer generated types | — | sonnet |
| 2 | 2. The server records `Applied` | `server/game/Effects/`, `server/game/Interpreter/`, `server/game/Rules/GrimdarkFuture/` (recording calls only) | 1 | opus |
| 2 | 3. The suite framework and `suite` command | new `sim/Suite/` (except the four `SuiteCatalog.*` group files' contents), `sim/Program.cs`, `sim/SimOptions.cs`, `sim/Scenarios/` extensions | 1 | opus |
| 3 | 4. Core games | `sim/Suite/SuiteCatalog.Core.cs` | 2, 3 | opus |
| 3 | 5. Special-rule games | `sim/Suite/SuiteCatalog.Special.cs` | 2, 3 | opus |
| 3 | 6. Army-rule games | `sim/Suite/SuiteCatalog.Army.cs` | 2, 3 | opus |
| 3 | 7. Spell games | `sim/Suite/SuiteCatalog.Spells.cs` | 2, 3 | opus |
| 3 | 8. The runner service | new `sim/Serve/`, `docker-compose.yml` (`sim-runner`) | 3 | sonnet |
| 4 | 9. The viewer's Tests page | `viewer/src/` (new `tests` route and `lib/suite/`) | 8 | sonnet |
| 5 | 10. Full run, findings and documents | docs, `AGENTS.md` files, `docs/handoff.md` | 4–9 | opus |

Tasks 4–7 fill one file each; a rule engine defect a game exposes is not fixed there: the game keeps the failing check
and the task reports it (Task 10 lists them).

## Fixed interfaces

```csharp
// Task 1 — libs/Tabletop.Protocol/Events.cs
public sealed record StepCompleted(Guid FrameId, string Step, AskAnswer? Answer, IReadOnlyList<RollRecord> Rolls,
    IReadOnlyList<StateChange> Changes, IReadOnlyList<string>? Applied = null) : EventPayload;
// Projection: CompletedStep gains IReadOnlyList<string> Applied (empty when null).

// Task 3 — sim/Suite/ (namespace Tabletop.Sim.Suite)
public sealed record DiceFix(string Roll, int Ordinal, IReadOnlyList<int> Faces);   // the Ordinal-th roll of that kind in the game gets these faces by SetDice
public sealed record GameCheck(string Name, Func<SuiteGameRun, string?> Failure, IReadOnlyList<string>? Entries = null);  // null = pass, else the reason; Entries: what an exact check is about
public sealed record TestGame(string Name, string Group, Scenario Scenario, IReadOnlyList<string> Covers,
    IReadOnlyList<DiceFix> Dice, IReadOnlyList<GameCheck> Checks);
public sealed record SuiteGameRun(TestGame Game, RunOutcome Outcome, IReadOnlyList<GameEvent> EffectiveLog,
    Projection Final, IReadOnlyList<string> InvariantFailures);
public static partial class SuiteCatalog
{
    public static IReadOnlyList<TestGame> All { get; }               // Core() ++ Special() ++ Army() ++ Spells()
    static partial IEnumerable<TestGame> Core();                     // Task 4 — sim/Suite/SuiteCatalog.Core.cs
    static partial IEnumerable<TestGame> Special();                  // Task 5
    static partial IEnumerable<TestGame> Army();                     // Task 6
    static partial IEnumerable<TestGame> Spells();                   // Task 7
}
public static class Fired { public static bool In(IReadOnlyList<GameEvent> effectiveLog, string entry); }   // Applied or RollModifier.Source
public static class Coverage { public static IReadOnlyList<CoverageEntry> Of(PropertySet set, IEnumerable<SuiteGameRun> runs, bool fullRun); }
public sealed record CoverageEntry(string Name, string Kind, IReadOnlyList<string> Games, bool Fired, IReadOnlyList<ExactResult> Exact);   // Kind: rule | spell
public sealed record ExactResult(string Game, string Check, bool Passed);
// TestGame also carries Evidence: entry -> predicate over the effective log, for rules that act outside a step (Scout, Ambush, Aircraft, Immobile).
// The runner always applies Ended, Invariants and FiredAll to every game.
public static class Checks                                           // helpers games use; each returns a GameCheck
{
    public static GameCheck Ended();                                  // outcome ended or the round cap
    public static GameCheck Invariants();
    public static GameCheck FiredAll();                               // every Covers entry fired
    public static GameCheck Event(string name, IReadOnlyList<string> entries, Func<IReadOnlyList<GameEvent>, string?> failure);   // an exact check over the log, quoted rule in name
}
public sealed class SuiteRunner { public Task<SuiteReport> RunAsync(SuiteOptions options, IProgress<SuiteGameResult>? progress, CancellationToken ct); }
public sealed record SuiteOptions(IReadOnlyList<string> Only, DriverKind Driver, bool Keep, string OutDir);
public sealed record SuiteGameResult(string Name, string Group, bool Passed, IReadOnlyList<string> Failures, ulong Seed, string Replay, string? RecordingPath, Guid? GameId);
public sealed record SuiteReport(Guid RunId, IReadOnlyList<SuiteGameResult> Games, IReadOnlyList<CoverageEntry> Coverage, bool Passed);
```

```
Task 8 — sim serve (127.0.0.1:5180)
GET  /suite                          → { games: [{ name, group, covers[] }] }
POST /runs { only[], driver, keep }  → 202 { runId } ; 409 while a run is active
GET  /runs/{id}/events               → text/event-stream: one `game` event per SuiteGameResult, then `done` with the SuiteReport
GET  /runs/{id}/recordings/{game}    → a kept recording (404 otherwise)
```

## Review Focus

1. **An interrupted socket run leaves rows.** The next run sweeps games named with an earlier run id (Task 3 test
   `A_run_sweeps_games_left_by_an_interrupted_run`).
2. **A dice fix that never matches.** A `DiceFix` whose roll never happens fails the game with that reason rather
   than passing silently (Task 3 test `An_unapplied_dice_fix_fails_the_game`).
3. **"Fired" from a refused or undone step.** Only the effective log counts (Task 3 test
   `An_undone_step_does_not_count_as_fired`).
4. **A partial run judged as uncovered.** `--only` never fails on coverage; a full run does (Task 3 test
   `Only_runs_do_not_fail_on_coverage`).
5. **A hook rule missing from `Applied`.** Every `Rules/GrimdarkFuture/` hook that changes a result records its name
   (Task 2 test per hook family: deployment, movement, attack, morale, casting).

---

### Task 1: `Applied` on the wire

**Files:** `libs/Tabletop.Protocol/Events.cs`, `Projection.cs` (`CompletedStep.Applied`), `EventApplier.cs`;
`tests/Tabletop.Protocol.Tests`; viewer generated types and, if `CompletedStep` is ported, `viewer/src/lib/live/`.

- [ ] Tests: `StepCompleted` round-trips `Applied` through `WireJson`; an old event without it reads as null and
  `CompletedStep.Applied` is empty; the applier copies it onto the frame's completed step.
- [ ] Implement; regenerate viewer types; viewer `npm run check && npm test`; commit.

### Task 2: The server records `Applied`

**Files:** the effects layer (`server/game/Effects/`: where an effect row applies to a roll, a hit, a wound, a
re-roll, a grant, a movement or stat change), `SequenceInterpreter` (writes the step's list onto `StepCompleted`),
`Rules/GrimdarkFuture/` hooks (each hook that changes a result records its rule name), casting (a cast spell records
its own name). Tests in `tests/Tabletop.GameServer.Tests`.

**Interfaces:** consumes Task 1. Produces: every `StepCompleted` whose result a catalog entry changed lists that
entry once, in order of first application.

- [ ] Tests, one per effect kind in the effect grammar (`docs/design/interpreter/effect-grammar.md`) and one per hook
  family (Review Focus 5): the entry appears in `Applied`; a step no entry changed has `Applied` null or empty; an
  entry that is present but whose condition fails is not listed.
- [ ] Implement with one recorder threaded through the step's evaluation (no global state; the engine holds nothing
  between calls). Full GameServer and Sim suites pass; commit.

### Task 3: The suite framework and `suite` command

**Files:** `sim/Suite/` — `TestGame.cs`, `Checks.cs`, `Fired.cs`, `Coverage.cs`, `SuiteRunner.cs`, `SuiteReport.cs`
(table + `suite.json`), `SuiteCatalog.cs` with the four group files each holding an empty partial body;
`sim/Program.cs`, `SimOptions.cs` (`suite`, `--only`, `--driver`, `--keep`, `--list`, `--out`);
`sim/Scenarios/` only if a test game needs per-unit stats or weapons the builder lacks (add them additively).
Tests in `tests/Tabletop.Sim.Tests/Suite/`.

**Interfaces:** consumes Task 1 (`Applied`). Produces everything in *Fixed interfaces* under Task 3.

- [ ] Tests: `--only` selects by game name and by group (`core`, `special`, `army:<book>`, `spells:<book>`);
  `DiceFix` applies `SetDice` to the right roll on both drivers and the outcome follows; Review Focus 1–4;
  `Fired.In` reads `Applied` and `RollModifier.Source`; `Coverage.Of` marks uncovered entries and catalog entries with
  no effect in play; the report's exit code; a socket run deletes its rows unless `--keep`; `--keep`
  writes recordings; each result's `Replay` is a command line that reruns that game alone with `--keep`.
- [ ] One sample game in a test fixture (not in the catalog) exercises the whole path on both drivers.
- [ ] Implement; Sim suite passes; commit.

### Tasks 4–7: The games

Each task fills its `SuiteCatalog.<Group>.cs`. Every game: a theme in its name, `Covers`, a script that brings each
covered entry into play, `Checks.Ended()`, `Checks.Invariants()`, `Checks.FiredAll()`, plus the exact checks below.
Seeds are fixed. Each game must pass on both drivers; a game blocked by an engine defect keeps its failing check and
the task's report names the defect with the log evidence.

- **Morale dice cannot be fixed** (the morale frame finishes in the step that rolls): a morale exact check reads the logged morale roll and asserts the p.10 consequence for its result; pick seeds that give both a pass and a fail.
- **Task 4, `core`** — pp.4–12, about ten games: the four actions and their moves; terrain (difficult, dangerous,
  cover, LoS-blocking, p.11–12); shooting incl. split fire; charge and melee incl. strike back; morale, Shaken,
  Fatigued; deployment with Scout and Ambush; transports embark/disembark/destroyed; objectives seize and contest;
  the round-4 end under silent, warn and strict. Exact checks with `DiceFix` for hit rolls, defense rolls, morale tests
  and casualties, each quoting its page.
- **Task 5, `special`** — all 33 core special rules (`grimdark-future-core.properties.json`), three to five a game,
  chosen to collide. Exact checks for every one whose result a number states (AP, Blast, Deadly, Rending, Tough,
  Regeneration, Bane, Furious, Counter, Impact, Relentless, Reliable, Surge, Thrust, Unstoppable, Fear, Fearless,
  Stealth, Indirect, Fast, Slow, Strider, Flying, Limited, Takedown, Artillery, Aircraft, Caster, Transport, Scout,
  Ambush, Immobile, Hero).
- **Task 6, `army:<book>`** — the 73 entries of `grimdark-future-army-rules.part.json`, grouped by the book that
  prints them (`CatalogEntry.Faction`); fired checks for all, exact checks where the rule states a number.
- **Task 7, `spells:<book>`** — every spell of every book in `server/game/Content/army-books/`, generated: per book
  a game whose casters (`Caster(X)`, `ArmyBook` = the book) cast each spell in turn at a target its `targeting`
  allows, over as many rounds as the tokens need (several games per book when one does not fit). Fired checks for
  every spell; one exact check per effect kind across the whole group (choose the spell, fix its dice, assert the
  effect from its `ruleText`).

### Task 8: The runner service

**Files:** `sim/Serve/` (minimal API hosted by `dotnet run --project sim -- serve [--port 5180]`), `sim/Program.cs`
(the `serve` verb only), `docker-compose.yml` (`sim-runner`: the dotnet image like `game`, its own build slot,
`127.0.0.1:5180`, after `game` is healthy, socket driver pointed at `ws://game:5200/ws` and `http://management:5100`).
Tests with `WebApplicationFactory` in `tests/Tabletop.Sim.Tests/Serve/`.

- [ ] Tests: the four endpoints of *Fixed interfaces*; one run at a time (409); events stream one `game` per result
  then `done`; recordings served only for kept games; CORS allows the viewer's origin `http://127.0.0.1:5174` (or the
  viewer proxies `/suite` and `/runs` like `/v1` — pick one and state it).
- [ ] Implement; `docker compose up -d` brings it up; `curl` each endpoint; commit.

### Task 9: The viewer's Tests page

**Files:** `viewer/src/` — a Tests view and `lib/suite/` (client for Task 8's API); Vitest tests.

- [ ] Tests: the client parses `/suite`, posts a run, reads the event stream into per-game results and the final
  coverage; the page model groups games, tracks selection, Keep, driver.
- [ ] The page: grouped checklist with what each game covers; Run; live results; coverage table; **Watch** loads a
  kept recording into the existing recording feed; **Open live** opens the live feed on a kept socket game.
- [ ] `npm run check && npm test`; check the page in the browser against the compose stack; commit.

### Task 10: Full run, findings and documents

- [ ] `dotnet run --project sim -- suite` (in-process) and `--driver socket`; record totals, coverage and every
  failing game with its defect.
- [ ] Documents: `sim/AGENTS.md` (the suite, `serve`), `docs/design/simulation/design.md`, `viewer/AGENTS.md`, root
  `AGENTS.md` (the `sim-runner` service), `docs/handoff.md` (how to run the suite; the engine defects it found as
  next work). State the current design only.
