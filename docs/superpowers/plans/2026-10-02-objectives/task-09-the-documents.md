# Task 9: The documents

**Files:**
- Modify: `docs/superpowers/specs/2026-08-28-game-flow-design.md`, `docs/architecture.md`, `docs/superpowers/specs/2026-09-04-scheduler-design.md`, `docs/design/scheduler/design.md`, `docs/design/game-server/design.md`, `docs/superpowers/specs/2026-09-06-simulation-design.md`, `docs/design/simulation/design.md`, `docs/design/viewer/design.md` (if it lists frame fields), `docs/handoff.md`

**Interfaces:**
- Consumes: the behaviour Tasks 1 to 8 built, and the enforcement plan's Task 9 edits to the same files (this task runs after it and adds to them).
- Produces: no code. Each document states the current decision once, with no change history.

Spec: objectives spec, its opening paragraph (the owner's ruling reverses the game-flow spec's *Scoring*).

- [ ] **Step 1: Find the old facts**

Run:

```bash
grep -rn -E "no scoring triggers|no victory conditions|no mission logic|Players count their own points|five invariants|six invariants|RecordedMarkerState" docs --include=*.md | grep -v 2026-10-02
```

Expected: the passages below. A hit this task does not list is corrected the same way and named in the commit body.

- [ ] **Step 2: Scoring**

`docs/superpowers/specs/2026-08-28-game-flow-design.md` → *Scoring*: replace the section's body with:

"**The engine scores the core mission; players keep their own counters.** At the end of each round a unit within 3" of an objective marker while no enemy is seizes it; control persists when the unit leaves; units of more than one team make it neutral; Shaken units, Aircraft and units that arrived by Ambush that round neither seize nor contest (GF p.6, p.10, p.13). After round 4 the team controlling most markers wins: under `strict` `game_ended` names it, or the tied teams; under `warn` the warning names the standing ([objectives](2026-10-02-objectives-design.md)). Each team still has one counter it can increment and decrement, and nothing the engine scores writes it. Missions beyond the core one are not modelled."

- [ ] **Step 3: Architecture, the scheduler and the game server**

- `docs/architecture.md` → *Rules enforcement*, after the paragraph the enforcement plan added: "Grimdark Future's core mission is scored by the engine: objectives are seized at each round's end, and the round-4 end names the winner ([objectives](superpowers/specs/2026-10-02-objectives-design.md))."
- `docs/superpowers/specs/2026-09-04-scheduler-design.md`, after *Rounds count up only when a player says so*: "**A round's end comes between the answer and the next round.** Once a player has ended the round, the scheduler asks the ruleset for the round's end changes (`IGameRules.OnRoundEnded`) and writes them as one `round_ended` event when there are any; then the next `round_started` is proposed. Grimdark Future seizes objectives there ([objectives](2026-10-02-objectives-design.md))." `docs/design/scheduler/design.md`: the same step in its description of `SchedulerBase.Advance`, and `PlayerEndedRound` where it names the private `RoundEnded` method.
- `docs/design/game-server/design.md`: the menu's override list gains `SetObjectiveControl → ObjectiveControlSet`; the `IGameRules` member list gains `JudgeAdvance` and `OnRoundEnded`.

- [ ] **Step 4: `sim/` and the viewer**

- `docs/superpowers/specs/2026-09-06-simulation-design.md` → *What it asserts*: the list gains "Nothing the engine writes follows `game_ended` until a round is started by hand" and "Every objective control change names a marker the game holds and a team it has". `docs/design/simulation/design.md`: the invariant count becomes seven, with the two names, and the catalog list gains `final-round`, `final-round-warn`, `final-round-silent` and `objectives`.
- The viewer's design doc, where it lists `RecordedFrame.Markers` or `RecordedMarkerState`: "a frame's markers are the setup markers then the play markers; `controlledBy` names the controlling team".

- [ ] **Step 5: The handoff**

`docs/handoff.md`: the enforcement plan's Task 9 line about this tree names both plans as built; add the owner's open question on automating objective seizing as answered (the engine scores the core mission), and record the `objectives` scenario's seed and winner from Task 7.

- [ ] **Step 6: Check, then commit**

Run: `grep -rn -E "no scoring triggers|five invariants|six invariants" docs --include=*.md | grep -v 2026-10-02`
Expected: no output.

```bash
git add docs
git commit -m "State the scored core mission in every document"
```
