# The enforcement engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One policy behind `silent | warn | strict` that the dispatcher and the engine both hand a breach to, with the round-4 end (GF p.7) as its first state-time client: `warn` warns, `strict` logs `game_ended` and the game server marks the row ended, `silent` leaves it to the players.

**Architecture:** `EnforcementPolicy.Decide(Enforcement, Breach?)` returns `Accept(warning)`, `Refuse` or a new `EndGame(reason, winners)`. The dispatcher keeps judging commands through the existing `Legality` overload. `RulesEngine.Advance` judges its own proposal through a new `IGameRules.JudgeAdvance` hook and returns `Emit(events, warning)`; Grimdark Future's judge raises `mission_length` on a proposed `round_started` past round 4. `game_ended` is a new event that sets `Projection.Ended`; while it is set `Advance` is silent, and the session writes `game.status = 'ended'` and broadcasts `GameInfoMessage`.

**Tech Stack:** `net10.0`, C# 12, xUnit, Npgsql; TypeScript, Svelte 5 and Vitest in `viewer/`.

**Spec:** [`docs/superpowers/specs/2026-10-02-enforcement-engine-design.md`](../../specs/2026-10-02-enforcement-engine-design.md), the whole document. The winners `game_ended` carries are [the objectives spec](../../specs/2026-10-02-objectives-design.md)'s, built by the next plan, [`2026-10-02-objectives/`](../2026-10-02-objectives/README.md); this plan lands the judge with an empty winners list.

**Requires landed:** everything on `main` at `6f1e0fa` (steps 1 to 6, the simulator and the viewer). This tree branches from `main` now, runs beside the spell-library tree, and **merges first**. The objectives plan follows it on the same tree.

## Global Constraints

- The rules engine lives only in `server/game/`; a rule is named only in `server/game/Rules/GrimdarkFuture/`. The engine never appends, never broadcasts and holds no state between calls (`CLAUDE.md` → Conventions). A breach is a value the engine returns, never an action it takes.
- No Protocol type is renamed. Every wire change is additive: one `EventPayload` kind (`game_ended`) and one `Projection` property (`Ended`). `ProtocolVersion.Current` stays `1`, and so does the viewer's `CLIENT_VERSION`.
- Menu commands (`CommandClass.Override`) and `Undo` never reach the policy, under any setting.
- `game.status` is written by the game server from `playing` on in exactly one case: `playing → ended` when a `game_ended` it appended becomes live, with `update management.game set status = 'ended' where id = $1 and status = 'playing'`. No migration.
- The final round is fixed at `4` (`GrimdarkFutureRules.FinalRound`). The end-round question's wording does not change.
- An undone `strict` end leaves the row `'ended'`; `GameSession.StartAsync` advances whenever `Projection.Ended` is unset, whatever the row says.
- `sim/`: a wipe-out is not a win (p.6). `RunOutcome.Decided` is removed; `RunOutcome.Ended(int Round, IReadOnlyList<Guid> Winners)`, kind `ended`, replaces it.
- After any change to a type reachable from `SocketMessage` or a `Recorded*` record: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then `cd viewer && npm run check && npm test`.
- Tests: xUnit, test-driven. Server tests marked `[Collection("Postgres")]` need `docker compose up -d`.
- Commit after every task, imperative one-line subject, ending with the `Co-Authored-By` trailer naming the model that produced it, e.g. `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A judge raised twice.** A judge that keys on something its own proposal does not change warns on every `Advance` and spins the session's 10 000-iteration guard. Every judge test asserts the second `Advance` after the breach returns nothing (Task 5).
2. **`WithSequences` swallowing the hook.** It forwards each `IGameRules` member by hand; a missed forward makes a wrapped Grimdark Future fall back to the default `JudgeAdvance => null` and silently lose the round-4 end. Task 5 adds a test through `WithSequences`.
3. **A restart after an undone `strict` end.** The row says `ended`, the log's end is undone; the session must still advance on hydration (Task 6 `An_undone_end_still_advances_after_a_restart`).
4. **The crash between the append and the row write.** A log that holds a live `game_ended` with the row still `playing` is reconciled at hydration and not advanced (Task 6).
5. **`set_round` after a `strict` end.** It must clear `Ended`, carry round 5's changes, and lead to a second end at the next boundary (Tasks 4 and 5).

---

## File Structure

```
libs/Tabletop.Protocol/Events.cs                 GameEnded and its [JsonDerivedType]
libs/Tabletop.Protocol/Projection.cs             GameEnded? Ended
libs/Tabletop.Protocol/EventApplier.cs           game_ended sets Ended; round_started clears it
server/game/Dispatch/EnforcementPolicy.cs        Breach, StrictConsequence, EndGame, Decide(Breach?), EndGamePayloads
server/game/Dispatch/CommandDispatcher.cs        EndGame unreachable; set_round carries RoundStartChanges
server/game/Rules/IRulesEngine.cs                Emit.Warning; Advance returns Emit; RoundStartChanges
server/game/Rules/IGameRules.cs                  GameEndingBreach; JudgeAdvance (default member)
server/game/Rules/InertEngine.cs                 Advance returns Emit
server/game/Engine/IScheduler.cs                 RoundStartChanges (default member)
server/game/Engine/RulesEngine.cs                silent while ended; judge; policy; RoundStartChanges
server/game/Scheduler/SchedulerBase.cs           RoundStartChanges, called by BeginRound
server/game/Rules/GrimdarkFuture/GfRules.MissionLength.cs   FinalRound, MissionLength, JudgeAdvance   (new)
server/game/Persistence/IGameRows.cs             EndGameAsync
server/game/Persistence/PostgresGameRows.Writes.cs   EndGameAsync
server/game/Session/GameSession.cs               AdvanceAsync warning; AppendAsync end; StartAsync from the log
client/src/Core/LogText.cs                       the game_ended line
sim/Run/RunOutcome.cs, RunLoop.cs, RunLoop.Recording.cs, RunReport.cs, Summary.cs   Ended replaces Decided
sim/Drivers/InProcessDriver.cs, SocketDriver.cs  Accepted.Warning
sim/Drivers/ScenarioRows.cs                      EndGameAsync over a mutable status
sim/Invariants/Invariants.cs                     invariant 6, NothingAdvancesAfterTheEnd
sim/Invariants/ProjectionComparer.cs             Ended
sim/Scenarios/Expectation.cs                     Warnings
sim/Scenarios/ScenarioCatalog.cs                 final-round, final-round-warn, final-round-silent
sim/Recording/LogLines.cs, Recording.cs          the game_ended line; the Outcome doc comment
viewer/src/lib/protocol/wire.generated.ts        regenerated
viewer/src/lib/live/apply.ts, logLines.ts        ended; the game_ended line
tests/...                                        per task
docs/...                                         Task 9
```

## Tasks

1. [The `game_ended` event, everywhere it is read](task-01-the-game-ended-event.md)
2. [The breach and the policy](task-02-the-breach-and-the-policy.md)
3. [The engine judges its own proposal](task-03-the-engine-judges-its-proposal.md)
4. [A menu round start carries the round-start changes](task-04-set-round-carries-round-start-changes.md)
5. [Grimdark Future's mission length](task-05-grimdark-future-mission-length.md)
6. [The row, the broadcast and hydration](task-06-the-row-the-broadcast-and-hydration.md)
7. [`sim/`: a wipe-out is not a win](task-07-sim-ended-replaces-decided.md)
8. [`sim/`: warnings, invariant 6 and the final-round scenarios](task-08-sim-warnings-invariant-scenarios.md)
9. [The documents](task-09-the-documents.md)

Tasks 1 and 2 are independent of each other; every later task needs both. Task 6 needs Task 3. Tasks 7 and 8 need Tasks 5 and 6.

---

## Interfaces → Produces

`IRL<T>` abbreviates `IReadOnlyList<T>`.

### `Tabletop.Protocol`

| Type | Members |
| --- | --- |
| `sealed record GameEnded(string Reason, IRL<Guid> Winners) : EventPayload` | wire kind `game_ended`; one winner is a win, several a tie, empty no result |
| `Projection` | `GameEnded? Ended` (set by `game_ended`, cleared by `round_started`) |

### `Tabletop.GameServer.Dispatch`

| Type | Members |
| --- | --- |
| `sealed record Breach(string Rule, string Detail, StrictConsequence Strict)` | |
| `abstract record StrictConsequence`; `sealed record StrictRefuses(RefusalReason Reason)`; `sealed record StrictEndsGame(IRL<Guid> Winners)` | |
| `sealed record EndGame(string Reason, IRL<Guid> Winners) : Verdict` | beside `Accept(string? Warning)` and `Refuse(RefusalReason, string)` |
| `static class EnforcementPolicy` | `Verdict Decide(Enforcement, Breach?)`, `Verdict Decide(Enforcement, Legality, string)`, `Breach? BreachOf(Legality, string)`, `IRL<EventPayload> EndGamePayloads(Projection, string, IRL<Guid>)`, `const string OutsideLegalSetRule = "outside_legal_set"`, `const string IllegalRule = "illegal"` |

### `Tabletop.GameServer.Rules` and `.Engine`

| Type | Members |
| --- | --- |
| `sealed record Emit(IRL<EventPayload> Events, string? Warning = null) : EngineResult` | `static Emit None` |
| `interface IRulesEngine` | `Legality Judge(...)`, `EngineResult Execute(...)`, `Emit Advance(GameState)`, default `IRL<StateChange> RoundStartChanges(GameState, int round) => []` |
| `interface IScheduler` | `IRL<EventPayload> Advance(GameState, ISequenceStarter)`, default `IRL<StateChange> RoundStartChanges(GameState, int round) => []` |
| `sealed record GameEndingBreach(string Rule, string Detail, IRL<Guid> Winners)` | in `IGameRules.cs` |
| `interface IGameRules` | default `GameEndingBreach? JudgeAdvance(GameState, PropertySet, IRL<EventPayload> proposed) => null` |
| `SchedulerBase` | `public IRL<StateChange> RoundStartChanges(GameState, int round)` |
| `GrimdarkFutureRules` | `const int FinalRound = 4`, `const string MissionLength = "mission_length"`, `GameEndingBreach? JudgeAdvance(...)`, `static string MissionLengthDetail(int roundsPlayed)` |

### `Tabletop.GameServer.Persistence`

| Type | Members |
| --- | --- |
| `interface IGameRows` | `Task EndGameAsync(Guid gameId, CancellationToken ct)` |

### `Tabletop.Sim`

| Type | Members |
| --- | --- |
| `abstract record RunOutcome` | `Ended(int Round, IRL<Guid> Winners)` (kind `ended`), `RoundLimit`, `Stuck`, `Failed`, `Paused`; `Decided` removed |
| `abstract record Expectation` | `Warnings(string Kind, int Count)` added |
| `static class Invariants` | `NothingAdvancesAfterTheEnd(IRL<GameEvent>)`, sixth in `CheckAll` |
| `ScenarioCatalog` | `final-round`, `final-round-warn`, `final-round-silent` |

### What the spell-library tree takes in by merge

The spell tree branches before this plan merges and takes it in by merge (or rebases onto it):

- **`Emit` from `IRulesEngine.Advance`.** Any engine test the spell tree writes against `engine.Advance(state)` reads `.Events`. `Infrastructure/Sim.cs` keeps its public surface (`Run()` still returns `IRL<EventPayload>`), so tests through `Sim` and `SchedulerHarness` need nothing.
- **`ended` as a run outcome.** Under `strict` every default run that is not stuck or failed ends `ended` at round 4. The spell tree's `casting` expectations, `RunLoopTests` and `ProgramTests` that read `decided` read `ended`.
- **Invariant numbering.** This plan adds invariant 6 (`NothingAdvancesAfterTheEnd`), and the objectives plan adds 7 (`ControlFollowsTheMarkers`); `CheckAll` returns seven results. A spell-tree invariant is 8.
- **Regenerated wire types.** After the merge: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then `cd viewer && npm run check`. `wire.generated.ts` is regenerated, never merged by hand.
- **Textual overlaps:** `server/game/Rules/IGameRules.cs`, `sim/Invariants/Invariants.cs`, `sim/Scenarios/ScenarioCatalog.cs`, docs. Nothing here touches `server/game/Content/`, `Effects/`, `Properties/`, `GfCasting.cs`, `GfNames.cs`, `GfRules.Sequences.cs`, `GfRules.Scheduling.cs` or `server/management/`.

## Rulings

1. **The judge's result type cannot refuse.** `JudgeAdvance` returns `GameEndingBreach`, which the engine wraps as `StrictEndsGame`; a ruleset cannot ask the engine to drop a proposal it made itself (review finding).
2. **`EndGamePayloads` emits `game_ended` alone** and throws when the stack is not empty or an Ask is open (manager ruling). The round-4 end meets both by construction.
3. **The dispatcher never sees `EndGame`.** Legality breaches are always `StrictRefuses`; both call sites throw `UnreachableException` on `EndGame`, and a test over every (`Enforcement`, `Legality`) pair pins it (review finding).
4. **`set_round` carries the round-start changes** through `IRulesEngine.RoundStartChanges`, which `RulesEngine` delegates to the scheduler's (manager ruling). The `RoundStartChanges` members on `IScheduler` and `IRulesEngine` are default members returning none, so `OneAsk`, `FixedAskEngine`, `DesyncingEngine` and `ThrowingEngine` need no new member.
5. **`ProtocolVersion` stays 1** under the pre-release assumption that every client is built from the same commit as the servers, recorded in `docs/architecture.md` → *Transport* by Task 9 (manager ruling).

---

## Self-review

**Spec coverage.** *The breach*, *The policy* → Task 2. *Who raises a breach* → Tasks 2 (dispatcher) and 3 (engine). *How a warning is recorded and shown* → Task 3 (`Emit.Warning`, the session) and Task 1 (the log lines). *The round-4 end*: detection and settings → Task 5; the event and `Ended` → Task 1; silence while ended → Task 3; *A menu round start* → Task 4; *The row and the broadcast*, hydration, after an undo → Task 6; *What clients see* → Tasks 1 and 6; *Open Asks at the end* → Task 2's assertion; *Playing on* → Task 5's tests; *Existing games* needs no code. *The wire* → Task 1. *sim* → Tasks 7 and 8. *Statements this reverses* → Task 9. *Files touched* matches the file structure above.

**Placeholders.** None: every code step carries its code and every run step its command and expected result.

**Type consistency.** `GameEnded(string Reason, IReadOnlyList<Guid> Winners)`, `Emit(Events, Warning)`, `GameEndingBreach(Rule, Detail, Winners)`, `EndGame(Reason, Winners)` and `RunOutcome.Ended(Round, Winners)` are spelled the same in every task. `JudgeAdvance` takes `(GameState, PropertySet, IReadOnlyList<EventPayload>)` everywhere.
