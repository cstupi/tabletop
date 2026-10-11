# Objectives Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The engine scores Grimdark Future's core mission (p.6): at the end of every round it seizes, contests and keeps objective markers, the projection holds who controls each one, and after round 4 `game_ended` names the team with most markers, or the tied teams; the `warn` warning names the standing.

**Architecture:** A round-end step in `SchedulerBase`, between the answered `end_round` question and the next round's `round_started`, asks `IGameRules.OnRoundEnded` for changes and proposes them as one new `round_ended` event when there are any. Grimdark Future answers with `ObjectiveControlSet` changes from `GfObjectives.Seize`. `Projection.Control` maps a marker to its controlling team. The mission-length judge (enforcement plan) reads `GfObjectives.Winners` and `GfObjectives.Standing`, so the winner is counted after round 4's seizures. A menu override (`set_objective_control`) writes the same change.

**Tech Stack:** `net10.0`, C# 12, xUnit; Godot 4.7 C# client layer (run by hand); TypeScript, Svelte 5 and Vitest in `viewer/`.

**Spec:** [`docs/superpowers/specs/2026-10-02-objectives-design.md`](../../specs/2026-10-02-objectives-design.md), the whole document, and [the enforcement spec](../../specs/2026-10-02-enforcement-engine-design.md) for `game_ended`, the judge and the run outcome.

**Requires landed:** [the enforcement-engine plan](../2026-10-02-enforcement-engine/README.md), on the same tree: `GameEnded(Reason, Winners)`, `Projection.Ended`, `IGameRules.JudgeAdvance`, `GrimdarkFutureRules.JudgeAdvance` with an empty winners list, `RunOutcome.Ended`, invariant 6, `FakeRules.JudgeAdvanceHook`. This plan runs after it, before the tree merges.

## Global Constraints

- A rule is named only in `server/game/Rules/GrimdarkFuture/`. The engine never appends, never broadcasts and holds no state between calls.
- Additive wire changes only: `round_ended` (`EventPayload`), `objective_control_set` (`StateChange`, and on `EventPayload`'s list as `score_set` is), `set_objective_control` (`Command`), `Projection.Control`, the optional `RecordedMarkerState.ControlledBy`. No rename. `ProtocolVersion` stays 1. `RecordingJson.SchemaVersion` stays.
- The rule (p.6, with p.10 and p.13): at the end of each round a unit within 3" of a marker while no enemy is seizes it; control persists when the unit leaves; units of more than one team make it neutral; Shaken units, Aircraft, and units that arrived by Ambush in the round that is ending neither seize nor contest. Measured base edge to marker edge (`Distance.EdgeToMarker`), height ignored, plus 1e-6 (the geometry library's internal `Tolerance.Distance`).
- An objective marker is a `Marker` with `Kind = Objective` among the setup markers (`Board.Table.Markers`) and those placed in play (`Projection.Markers`), the projection's entry winning on a shared id.
- Seizing runs under every enforcement setting. Only `strict` ends the game; `warn` names the standing in its warning; `silent` records nothing beyond `Projection.Control`.
- `round_ended` is written only when control changes, so a game with no objective markers has the log it has today and the checked-in viewer fixtures are not regenerated.
- The manual score counters (`set_score`) are untouched.
- After any change to a type reachable from `SocketMessage` or a `Recorded*` record: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then `cd viewer && npm run check && npm test`.
- Commit after every task, imperative one-line subject, ending with the `Co-Authored-By` trailer naming the model that produced it.

## Review Focus

1. **A setup objective.** Today's frames and `Projection.Markers` hold only markers placed in play; a marker from setup must be seized, counted, overridden from the menu and drawn (Tasks 2, 3, 6, 8 each test a setup marker).
2. **A free-for-all marker reached by two teams.** Neutral, not seized by the larger group (Task 3).
3. **An Ambusher that arrived last round.** Excluded only in the round it arrived; the next round it seizes (Task 3).
4. **The order at the boundary after round 4.** `round_ended` before `game_ended`, so the winner counts round 4's seizures (Task 5).
5. **A removed marker.** Its control goes with it and it counts for nobody (Task 1).

---

## File Structure

```
libs/Tabletop.Protocol/Events.cs                 RoundEnded; ObjectiveControlSet on EventPayload's list
libs/Tabletop.Protocol/StateChanges.cs           ObjectiveControlSet
libs/Tabletop.Protocol/Commands.cs               SetObjectiveControl
libs/Tabletop.Protocol/Projection.cs             IReadOnlyDictionary<Guid, Guid> Control
libs/Tabletop.Protocol/EventApplier.cs           round_ended, objective_control_set, marker_removed clears control
server/game/Dispatch/CommandDispatcher.cs        Classify: SetObjectiveControl is an override
server/game/Dispatch/MenuTranslation.cs          SetObjectiveControl -> ObjectiveControlSet
server/game/Rules/IGameRules.cs                  OnRoundEnded (default member)
server/game/Scheduler/SchedulerBase.cs           the round-end step
server/game/Rules/GrimdarkFuture/GfObjectives.cs        Markers, Seize, Winners, Standing   (new)
server/game/Rules/GrimdarkFuture/GfRules.Objectives.cs  OnRoundEnded                         (new)
server/game/Rules/GrimdarkFuture/GfRules.MissionLength.cs   winners and standing
client/src/Core/Names.cs, Core/LogText.cs, Core/MenuModel.cs, Screens/GameScreen.Play.cs
client/src/Table/MarkerNode.cs, Table/TableView.cs, Panels/TeamPanel.cs
sim/Recording/Recording.cs, FrameProjector.cs, LogLines.cs
sim/Invariants/Invariants.cs, ProjectionComparer.cs
sim/Scenarios/Expectation.cs, ScenarioCatalog.cs
viewer/src/lib/protocol/*.generated.ts           regenerated
viewer/src/lib/live/apply.ts, project.ts, logLines.ts
viewer/src/lib/board/frame.ts, draw.ts; viewer/src/lib/components/Scoreboard.svelte
docs/...                                         Task 9
```

## Tasks

1. [Control on the wire and in the projection](task-01-control-on-the-wire.md)
2. [The menu override](task-02-the-menu-override.md)
3. [Seizing, in Grimdark Future](task-03-seizing-in-grimdark-future.md)
4. [The round-end step](task-04-the-round-end-step.md)
5. [The winner on `game_ended`](task-05-the-winner-on-game-ended.md)
6. [Control in the recording and the viewer](task-06-control-in-the-recording-and-viewer.md)
7. [`sim/`: invariant 7 and a scored winner](task-07-sim-invariant-and-scored-winner.md)
8. [The client shows control](task-08-the-client-shows-control.md)
9. [The documents](task-09-the-documents.md)

Task 1 comes first. Tasks 2, 3 and 6 need only Task 1. Task 4 needs 3; Task 5 needs 4; Task 7 needs 5 and 6; Task 8 needs 2.

---

## Interfaces → Produces

| Type | Members |
| --- | --- |
| `sealed record RoundEnded(int Round, IReadOnlyList<StateChange> Changes) : EventPayload` | wire kind `round_ended` |
| `sealed record ObjectiveControlSet(Guid MarkerId, Guid? TeamId) : StateChange` | wire kind `objective_control_set`; `TeamId` null is neutral |
| `sealed record SetObjectiveControl(Guid MarkerId, Guid? TeamId) : Command` | wire kind `set_objective_control`; `CommandClass.Override` |
| `Projection` | `IReadOnlyDictionary<Guid, Guid> Control` (marker id → team id; neutral absent) |
| `IGameRules` | default `IReadOnlyList<StateChange> OnRoundEnded(GameState, PropertySet, int round) => []` |
| `static class GfObjectives` | `const double SeizeRange = 3`, `IReadOnlyList<Marker> Markers(GameState)`, `IReadOnlyList<StateChange> Seize(GameState, PropertySet)`, `IReadOnlyList<Guid> Winners(GameState)`, `string Standing(GameState)` |
| `GrimdarkFutureRules` | `OnRoundEnded(GameState, PropertySet, int)` → `GfObjectives.Seize` for round ≥ 1; `JudgeAdvance` now carries `GfObjectives.Winners` and `MissionLengthDetail(int) + " " + Standing` |
| `FakeRules` | `Func<GameState, PropertySet, int, IReadOnlyList<StateChange>>? RoundEndedHook { get; set; }` |
| `RecordedMarkerState` | trailing `Guid? ControlledBy = null` |
| `Expectation` | `Won(int TeamOrdinal)`, `MostMarkersWin` |
| `Invariants` | `ControlFollowsTheMarkers(IReadOnlyList<GameEvent>, CompiledScenario)`, seventh in `CheckAll` |
| `ScenarioCatalog` | `objectives` |

### What the spell-library tree takes in by merge

Beyond the enforcement plan's list: invariant 7 (`ControlFollowsTheMarkers`), so `CheckAll` returns seven; the regenerated `wire.generated.ts` and `recording.generated.ts`; and `round_ended` in any log where a team holds a marker. Textual overlaps: `IGameRules.cs`, `SchedulerBase.cs` (only if the spell tree edits it), `Invariants.cs`, `ScenarioCatalog.cs`.

## Rulings

1. **"Both sides" is more than one team.** p.50 plays a free-for-all by "all of the regular rules", and p.6 seizes only "whilst no enemies are" within 3", every other team being an enemy (objectives spec → *The rule*).
2. **A frame's markers are the setup markers then the play markers**, the play entry winning on a shared id, in `FrameProjector` and `project.ts` alike: a setup objective otherwise appears in no frame (objectives spec → *What players see*).
3. **`round_ended` only when something changes.** It keeps every log without objectives identical to today's (objectives spec → *Where the seize runs*).
4. **The private `SchedulerBase.RoundEnded(...)` is renamed `PlayerEndedRound(...)`**, because `Tabletop.Protocol.RoundEnded` is now a type the same class pattern-matches on.

---

## Self-review

**Spec coverage.** *The rule* → Task 3. *Where the seize runs* → Task 4. *Events, changes and the projection* → Task 1; *The menu* → Task 2. *The seize, in Grimdark Future* → Task 3; the judge's winners and standing → Task 5. *Under each setting* → Task 5. *What players see*: client → Tasks 1 (log) and 8; viewer → Tasks 1 (log) and 6. *The wire* → Tasks 1 and 6. *sim* → Tasks 6 and 7. *Files touched* matches the file structure.

**Placeholders.** None. Task 7's seed and winning ordinal are found by running the scenario, as the catalog's seeds were; the step says how and what to write.

**Type consistency.** `ObjectiveControlSet(MarkerId, TeamId)`, `RoundEnded(Round, Changes)`, `Projection.Control`, `GfObjectives.Markers/Seize/Winners/Standing` and `OnRoundEnded(GameState, PropertySet, int)` are spelled the same in every task.
