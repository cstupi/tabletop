# The roll-off result Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** The deployment roll-off opens no Continue Ask for players: its roll and its ranking run in one go and deployment's first
Ask follows. Every client shows "<team> goes first" over the table for 4 s with each team's roll under it, best first.

**Architecture:** `SequenceInterpreter.GateTeams` gates the actorless root frame (the roll-off) only under `Info.WaitsBetweenSteps`,
the development flag every other "Continue" stop already rides; the sim sets it when a suite game fixes dice, so `SetDice` still
reaches the roll-off's dice. A boundary Confirm the gate rule no longer asks for (a log stopped at the old gate) is closed unanswered on
the next `Advance`, and the ranking runs over the dice already rolled. The ranking step (`GfRolls.RollOffOrderStep`) writes each
team's faces as `rolled_<ordinal>_<k>` beside `place_1..place_n`, so the client derives no tie-break. `Core/RollOffBanners` turns the
ranking step in a live batch into a `Banner` with a detail line; `Hud/TurnBanner` queues it ahead of the batch's turn banners.

**Tech Stack:** .NET 10, xUnit, Godot 4.7 C# client.

**Spec:** `docs/superpowers/specs/2026-10-24-roll-off-result-design.md`

## Findings

- The Continue before deployment is the roll-off's gate: `SequenceInterpreter.GateTeams` returns every team for a frame with no actor
  and no parent, whatever `WaitsBetweenSteps` says, and the client labels a `confirm` Ask "Continue" (`AskViewModel.ConfirmLabel`).
- No other Ask stands between the end of setup and the first deploy Ask: the server's Asks are the step gate, `end_round`
  (`SchedulerBase.EndRoundAsk`, a round boundary), the deploy family (`TurnCycle`, `GfRules.Deployment.cs`, `GfRules.Reinforcement.cs`),
  activation, reactions, spends and `game_over`. Zones are configuration; no side or zone choice is asked.
- The sim leans on the gate twice: `DiceFixingDriver` sets the roll-off's dice while its frame is open (every army game's
  `DiceFix("roll-off", 0, …)`, Mission's Scout game), and `RunLoop.SetAsideAsync` sets units aside before the first answer. Suite games
  with dice fixes run with `WaitsBetweenSteps` (`SuiteRunner.PlayAsync`), so the first keeps working; a set-aside sent while the first
  deploy Ask is open is read at once by `SchedulerBase.Withdrawn`, which asks again.

## Global Constraints

- Players never get a roll-off Confirm: the lobby sends `WaitsBetweenSteps: false` (`LobbyPanel`, `HomeScreen`).
- A finished roll-off replays unchanged: no logged step is rewritten; old ranking steps without `rolled_*` keys still give the order.
- The client names no rule: it reads `place_k` and `rolled_<ordinal>_<k>` from a `step_completed`'s values, nothing else.
- The message shows on a live batch only: not while catching up, not in a batch with an undo, not in a replay, not with Animate or the
  Turn banner setting off.

---

### Task 1: The roll-off runs through without a gate

**Files:** `server/game/Interpreter/SequenceInterpreter.cs`; tests `tests/Tabletop.GameServer.Tests/InterpreterTests.cs`,
`AlternatingSchedulerTests.cs`, every `SchedulerHarness.ConfirmRollOff` caller with the wait setting off, `SchedulerEndToEndTests.cs`.

- [ ] Test (`InterpreterTests`): an actorless root frame of two steps with the wait setting off runs `step_completed`, then
  `step_completed`, `sequence_finished`, and opens no Ask; with it on, the boundary is one unsealed Confirm to every team.
- [ ] Test (`AlternatingSchedulerTests`): from an empty log the engine runs the roll-off to its ranking and opens the first deploy Ask
  with no Confirm in between.
- [ ] `GateTeams`: an actorless root frame is gated to every team only when `WaitsBetweenSteps`; doc comment says why (the suite's
  `SetDice` needs the frame on the stack).
- [ ] Drop `ConfirmRollOff` where the harness runs with the wait setting off; `DeploymentTests`' dice-edit test builds its harness
  with `waits: true`. `SchedulerEndToEndTests` waits for the deploy Ask, not the gate.
- [ ] Commit.

### Task 2: A log stopped at the old gate resumes

**Files:** `server/game/Interpreter/SequenceInterpreter.cs`; test `tests/Tabletop.GameServer.Tests/RollOffResumeTests.cs`.

- [ ] Test: a harness (wait setting off) whose log holds the roll-off's `sequence_started`, its roll's `step_completed` and an open
  every-team Confirm (written as the old interpreter wrote them) advances to `ask_closed` with no answers, the ranking's
  `step_completed` over the dice already rolled, `sequence_finished`, `round_started` with that order, and the winner's deploy Ask.
- [ ] Test: the same through `GameSession` hydration (`GameServerFactory`, seeded log): the first client sees the deploy Ask.
- [ ] `Advance`: when the top frame waits only on boundary Confirms and `GateTeams` gives none now, emit `AskClosed(id, [])` for each.
- [ ] Commit.

### Task 3: The ranking step names each team's faces

**Files:** `server/game/Rules/GrimdarkFuture/GfRolls.cs`, `server/game/Scheduler/RollOff.cs` (doc); test
`tests/Tabletop.GameServer.Tests/GfRollsTests.cs`.

- [ ] Test: dice 4, 4 then 6, 2 for two teams give `place_1 = 1`, `place_2 = 2`, `rolled_1_1 = 4`, `rolled_1_2 = 6`, `rolled_2_1 = 4`,
  `rolled_2_2 = 2`; three teams 5, 3, 5 then 1, 2 give team 2 one face (`rolled_2_1 = 3`) and teams 1 and 3 two.
- [ ] `RollOffOrderStep` writes `rolled_<ordinal>_<k>` (k from 1) from the keys its tie loop already builds.
- [ ] Rewrite the docs on `RollOffSequence`, `RollOffOrderStep` and `Scheduler.RollOff` for the gate that runs only under the wait
  setting.
- [ ] Commit.

### Task 4: The message model

**Files:** `client/src/Core/RollOffBanners.cs`, `client/src/Core/TurnBanners.cs` (`Banner.Detail`); test
`tests/Tabletop.Client.Tests/RollOffBannersTests.cs`.

- [ ] Test: a live batch holding a `step_completed` with `place_1 = 2, place_2 = 1, rolled_1_1 = 3, rolled_2_1 = 5` gives
  `Banner("Team 2 goes first", 4.0, "Team 2 rolled 5 · Team 1 rolled 3")`; tie and re-roll faces join with ", "
  ("Team 1 rolled 4, 6 · Team 2 rolled 4, 2").
- [ ] Test: nothing while catching up, with the setting off, in a batch with an undo, or for a `step_completed` without `place_1`.
- [ ] `Banner(string Text, double Seconds, string? Detail = null)`; `RollOffBanners.Of(batch, names, enabled, catchingUp)`; team names
  through `Names.Team`, as the header's.
- [ ] Commit.

### Task 5: The Godot side

**Files:** `client/src/Hud/TurnBanner.cs`.

- [ ] `TurnBanner` queues `RollOffBanners.Of` ahead of the batch's turn banners (one Banner area, so the message and "Team k's turn"
  show one after the other), passes `enabled` false in a replay, and shows `Detail` on a second, smaller label in the same panel.
- [ ] `dotnet build client` (the Godot project) builds; commit.

### Task 6: The sim and the suite

**Files:** `sim/Run/RunLoop.cs` (doc), `sim/Suite/DiceFixingDriver.cs` (doc), `sim/Scenarios/Scenario.cs` (doc),
`tests/Tabletop.Sim.Tests/InProcessDriverTests.cs`.

- [ ] `InProcessDriverTests`: the first open Ask is the deploy Ask; tests that confirmed the gate first answer the deploy Ask.
- [ ] `dotnet run --project sim -- suite` passes in process. The socket driver needs a game server built from this branch in dev mode.
- [ ] Commit.

### Task 7: Docs

- [ ] `grep -rn "roll-off" docs server/game/AGENTS.md client/AGENTS.md sim/AGENTS.md` for the gate: `server/game/AGENTS.md`,
  `docs/design/scheduler/design.md`, `docs/design/interpreter/*`, and the client's description of the Continue Ask say the roll-off
  is gated only under the wait setting. `docs/handoff.md` is not edited.
- [ ] Commit.
