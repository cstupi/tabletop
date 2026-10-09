# sim

`Tabletop.Sim`: plays a whole game with no Godot client, from a scenario, and writes one recording per run.
Consumed by developers, `tests/Tabletop.Sim.Tests` and `tests/Tabletop.Viewer.Tests`. Not shipped to players and not referenced by a server.

## Layout

- `Program.cs`, `SimOptions.cs`, `Summary.cs`: console entry, option parsing (`UsageException`), the per-run summary table and `summary.json`.
- `Scenarios/`: `Scenario`, `ScenarioBuilder`, `ScenarioCatalog` (the named scenarios), `ScenarioCompiler`, `Expectation`.
- `Answers/`: `IAnswerSource`, `AnswerPolicy` (the default, with the `MoveGoal` delegate), `ObjectiveGoal`, `AnswerScript` (named answers by question and ordinal), `PolicyView`. `Chooser.cs` is the seeded random stream.
- `Drivers/`: `IGameDriver`, `InProcessDriver`, `SocketDriver` with `SocketRowSeeder`, `ScenarioRows`, `RecorderClient`, `IWebSocketConnector`.
- `Run/`: `RunLoop`, `RunOutcome`, `RunReport`, `AskFingerprint` (stuck detection), `HandPlacement` (deployment from the seed).
- `Invariants/`: `Invariants` (the checks), `ProjectionComparer`.
- `Recording/`: `Recording`, `RecordingJson` (`SchemaVersion`), `FrameProjector`, `LogLines`.

## How it works

- `ScenarioCatalog` holds `skirmish`, `terrain`, `player-turn`, `free-for-all`, `stalemate`, `final-round`, `final-round-warn`, `final-round-silent`, `casting`, `army-rules`, `objectives`. `--list` prints them.
- A scenario has a seed, a round cap, a scheduler shape, an enforcement setting, armies, terrain, an optional `AnswerScript` and `Expectation`s.
- `ScenarioCompiler` turns it into a `CompiledScenario`; `Program.PlayAsync` builds `ComposedAnswers`: the script first, then `Scenario.Policy()` unless `ScriptOnly`.
- `RunLoop` asks the driver for open Asks, answers each, drains events, and ends in a `RunOutcome`: `Ended`, `RoundLimit`, `Stuck`, `Failed` or `Paused`.
- `AnswerPolicy` picks uniformly over exactly the legal set the server sent, one case per `LegalSet` shape. It never narrows the set.
- `AnswerPolicy` reads only `PolicyView`'s geometry members. A policy that reads other state is a second `IAnswerSource`.
- The one opt-in exception is a `MoveGoal` (`Scenario.Goal`, `ScenarioBuilder.Seeking`). It names a point per model and only ranks
  the destinations the uniform sampler already found legal: all `MoveSamples` are drawn and the passing one nearest the point wins.
  `ObjectiveGoal.Nearest` (the nearest objective marker the model's team does not control) reads team and control; `objectives`
  is the only scenario that opts in. Build a run's policy with `Scenario.Policy()` so the goal comes along.
- `sim/` holds no second implementation of the rules. It asserts invariants over the log and projection.
- `InProcessDriver` hosts the engine in the process. `SocketDriver` seeds rows in Postgres, mints sessions and plays over the game server's WebSocket.
- `DriverMode.StepAndWait` stops with one Ask open and answers nothing (`RunOutcome.Paused`).
- `Invariants.CheckAll` runs eight checks: replay reproduces the projection, undo and redo reproduce the dice, every Ask answered by an addressed team, no model in two places or off the slab, the round advances only through the boundary Ask, nothing advances after the end, control follows the markers, spells come from the caster's book.
- One file per run: `<scenario>-<seed>.json` in `--out` (default `./recordings`, git-ignored), plus `summary.json`.
- A recording is a projection frame per event, not an event log. It is never replayed into the engine.
- Exit code: 0 when no run failed, 1 otherwise, 2 for usage errors.

## Commands

```bash
dotnet run --project sim -- --list
dotnet run --project sim -- skirmish --seeds 1..20             # in-process, recordings in ./recordings
dotnet run --project sim -- skirmish --seed 7 --rounds 2 --no-recording
dotnet run --project sim -- skirmish --driver socket           # needs the stack below
dotnet test tests/Tabletop.Sim.Tests
```

- Options: `--seed N`, `--seeds A..B`, `--driver in-process|socket`, `--out DIR`, `--rounds N`, `--no-recording`, `--list`.
- `--driver socket` needs Postgres, the game server and management running, with management in the Development environment.
- Sessions come from `POST /v1/dev/sessions`, which exists only in Development.
- Socket environment, from the process or the nearest `.env`: `TABLETOP_DB_CONNECTION` (required), `TABLETOP_SIM_WS` (default `ws://127.0.0.1:5200/ws`), `TABLETOP_SIM_MANAGEMENT` (default `http://127.0.0.1:5100`).

## Conventions and pitfalls

- A scenario passes against both drivers (`DriverAgreementTests`).
- Script keys are the question name and its ordinal in the run. Server-minted Ask ids are not keys.
- A wipe-out does not end a run. The round-4 end depends on the enforcement setting.
- Socket runs seed Postgres with the server's own effect ids, so grants are checked by name, not id.
- `sim/` references `server/game` (`Tabletop.GameServer`). No server references `sim/`.
- `viewer/src/lib/protocol/recording.generated.ts` is generated from the `Recording` types here. Changing them means regenerating (see `tests/AGENTS.md`).
- A change to `FrameProjector` or `LogLines` regenerates `viewer/tests/fixtures/` and ports to `viewer/src/lib/live/`.

## Docs

- `docs/design/simulation/design.md`
- `docs/superpowers/plans/2026-09-06-simulation/README.md`
- `docs/superpowers/specs/2026-09-06-simulation-design.md`
- `docs/superpowers/specs/2026-10-02-spell-library-design.md` (the `casting` scenario), `docs/superpowers/specs/2026-10-02-army-rule-hooks-design.md` (`army-rules`), `docs/superpowers/specs/2026-10-02-objectives-design.md` (`objectives`)
