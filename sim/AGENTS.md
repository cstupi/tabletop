# sim

`Tabletop.Sim`: plays a whole game with no Godot client, from a scenario, and writes one recording per run. Its `suite`
verb plays the rules suite (themed games that check every catalog rule and spell); `lifecycle` plays whole games against the
stack by the client's path (management over HTTP, the game over the socket); `serve` hosts the suite over HTTP.
Consumed by developers, `tests/Tabletop.Sim.Tests` and `tests/Tabletop.Viewer.Tests`. Not shipped to players and not referenced by a server.

## Layout

- `Program.cs`, `SimOptions.cs`, `Summary.cs`: console entry, option parsing (`UsageException`), the per-run summary table and `summary.json`.
- `Scenarios/`: `Scenario`, `ScenarioBuilder`, `ScenarioCatalog` (the named scenarios), `ScenarioCompiler`, `Expectation`.
- `Answers/`: `IAnswerSource`, `AnswerPolicy` (the default, with the `MoveGoal` delegate), `ObjectiveGoal`, `AnswerScript` (named answers by question and ordinal, plus `ScriptMatch` picks), `AskPicks`, `PolicyView`. `Chooser.cs` is the seeded random stream.
- `Suite/`: the rules suite. `TestGame`, `DiceFix`, `GameCheck`, `KnownDefect` (`TestGame.cs`); `Checks`, `Fired`, `Coverage`, `LogQueries`; `SuiteRunner`, `SuiteReport`, `SuiteReportFiles`, `DiceFixingDriver`, `SuiteSocket`, `SuiteRows`; the games in `SuiteCatalog.Core*.cs`, `SuiteCatalog.Special*.cs`, `SuiteCatalog.Army*.cs`, `SuiteCatalog.Spells.cs` and `SpellGames.cs`.
- `Serve/`: `ServeApp` (the runner's minimal API), `ServeArguments`, `SuiteRun`.
- `Drivers/`: `IGameDriver`, `InProcessDriver`, `SocketDriver` with `ISocketRows` (`SocketRowSeeder`, its Postgres implementation), `ScenarioRows`, `RecorderClient`, `IWebSocketConnector` (`ClientWebSocketConnector` offers permessage-deflate as the Godot client does, `Connect` and `Reauth` uncompressed), `SocketFrames` (the client socket's read loop).
- `Lifecycle/`: the lifecycle suite. `LifecycleShape` (`LifecycleShapes`), `LifecycleRunner` (`LifecycleEndpoints`, `LifecycleResult`), `LifecycleGame` (partials `.Lobby`, `.Play`, `.Leave`), `LifecycleScenario`, `ClientSocket`, `ManagementApi`, `LifecycleArmy`.
- `Run/`: `RunLoop`, `RunOutcome`, `RunReport`, `AskFingerprint` (stuck detection), `HandPlacement` (deployment from the seed).
- `Invariants/`: `Invariants` (the checks), `ProjectionComparer`.
- `Recording/`: `Recording`, `RecordingJson` (`SchemaVersion`), `FrameProjector`, `LogLines`.

## How it works

- `ScenarioCatalog` holds `skirmish`, `terrain`, `player-turn`, `free-for-all`, `stalemate`, `final-round`, `final-round-warn`, `final-round-silent`, `casting`, `army-rules`, `objectives`. `--list` prints them.
- A scenario has a seed, a round cap, a scheduler shape, an enforcement setting, armies, terrain, an optional `AnswerScript` and `Expectation`s.
- `ScenarioCompiler` turns it into a `CompiledScenario`; `Program.PlayAsync` builds `ComposedAnswers`: the script first, then `Scenario.Policy()` unless `ScriptOnly`.
- `RunLoop` asks the driver for open Asks, answers each, drains events, and ends in a `RunOutcome`: `Ended`, `RoundLimit`, `Stuck`, `Failed` or `Paused`.
- `AnswerPolicy` picks uniformly over exactly the legal set the server sent, one case per `LegalSet` shape. It never narrows the set.
  The one Ask it does not draw is `game_over` (warn's End or Continue after round 4), which it answers End, so a `warn` run finishes.
- `AnswerPolicy` reads a `MoveRegion` as the server judges it: once any model's sampled path enters a difficult volume, every model
  sampled further than `DifficultCap` is sampled again within it (p.11); a disembark is sampled until the base is fully within
  `ExitWithin` of the container and fully on the table; a move is judged along its path with `Movement.Check`, as the server
  judges it (a model a hand move left in impassable terrain heads out of it; the cap is read from each model's path,
  `AnswerPolicy.Route`); a `MinDistance` is sampled above where the table has room for it, and where it has none, on the
  sampled bearing at `Movement.RoomAlong` (`GfMoves.Minimum`). When every sample misses under a minimum, the bearing to the farthest
  corner and then each of `Movement.Rays` bearings is tried at its least, every check and `Clear` included; standing still comes only
  when none passes.
- `AnswerPolicy.Clear` mirrors `GfMoves.Contact`: a sampled landing is on no other base, has a path round the standing models within the allowance (`Movement.Check`'s `CrossesModels`, in three dimensions over the terrain; a sample whose straight line crosses a model passes when the way round fits) unless the region `PassesModels`, and keeps the region's `Separation` (`Distance.BodyToBody`). A repair pass after the sample cap redraws any landing no longer Clear. `PolicyView.RestOf(modelId, at, yaw?)` rests a landing on the terrain; `HandPlacement` anchors clear of other bases, within the zone (when 40 random draws miss, a 0.25" scan of the zone's
  bounding box; the zone's centre only when nothing fits), and an Ambusher anywhere over 9" from every enemy model (p.13), or the distance `GfAmbush` gives (Infiltrate 3", Repel Ambushers 12"), within 6" of a friendly Ambush Beacon first.
- A charge move (`MoveRegion.ContactWith`, p.9) answers with `UnitFormation.InContact` over the Ask's own parameters, the search the server's
  `charge_contact` makes, so a charge that could reach contact in coherency does; when there is none it is sampled as any move.
- Coherency (p.7, the server's `move_coherency`; `AnswerPolicy.Coherency.cs`): a sampled answer that takes a unit in coherency out of it
  is formed up (`FormUp`): a sampled model keeps its landing and each other takes its start moved as the lead moved, else a spot beside a
  model of its unit (`Movement.ContactPoints` at 0.3" and 0.7"; with a goal, the passing one nearest it), each sampled model in turn as the
  lead, then the first at half and a quarter of its move; failing all, every model stands.
- `AnswerPolicy` reads only `PolicyView`'s geometry members. A policy that reads other state is a second `IAnswerSource`.
- The one opt-in exception is a `MoveGoal` (`Scenario.Goal`, `ScenarioBuilder.Seeking`). It names a point per model and only ranks
  the destinations the uniform sampler already found legal: all `MoveSamples` are drawn and the passing one nearest the point wins.
  `ObjectiveGoal.Nearest` (the nearest objective marker the model's team does not control) reads team and control; `objectives`
  is the only scenario that opts in. Build a run's policy with `Scenario.Policy()` so the goal comes along.
- `sim/` holds no second implementation of the rules. It asserts invariants over the log and projection.
- `InProcessDriver` hosts the engine in the process. `SocketDriver` plays over the game server's WebSocket, after `ISocketRows` seeds the rows (`SocketRowSeeder`: Postgres) and the sessions are minted.
- `DriverMode.StepAndWait` stops with one Ask open and answers nothing (`RunOutcome.Paused`).
- `Invariants.CheckAll` runs eight checks: replay reproduces the projection, an undo keeps the earlier dice, every Ask answered by an addressed team, no model in two places or off the slab, the round advances only through the boundary Ask (a `game_over` Ask between them is skipped), nothing advances after the end, control follows the markers, spells come from the caster's book.
- One file per run: `<scenario>-<seed>.json` in `--out` (default `./recordings`, git-ignored), plus `summary.json`.
- A recording is a projection frame per event, not an event log. It is never replayed into the engine.
- Exit code: 0 when no run failed, 1 otherwise, 2 for usage errors.
- `Scenario.SelfPlay` (builder `SelfPlay()`): one account holds every team, a participant row each, and the driver connects once
  (`CompiledScenario.Connected`, `ConnectionFor`, `CredentialOf`); every answer names its team (`IGameDriver.AnswerAsync` sends
  `AnswerAsk.TeamId`). `core-self-play` plays it through deployment and four rounds.
- `ScenarioArmy.Team` (builder `team:`) seats an army on a team ordinal, so a team holds several (rulebook p.50 Team Games). `RunLoop`
  has a team's seats take turns answering its Asks (`RunLoop.Seat`), whatever unit an answer picks, and the seat that picked a deploy
  unit places it (owner ruling: either player of a team activates any unit of the team).
  `core-team-game` plays two a side through deployment and four rounds.
- `ScenarioArmy.SetAside` (builder `setAside:`) lists unit ordinals the run sets aside before the first answer: one `SetCondition`
  of the ruleset's set-aside condition per unit, as the unit menu does. A Scout or Ambush unit plays only when set aside.
  `ScenarioArmy.SetAsideAtLastAsk` (builder `setAsideAtLastAsk:`) sets a unit aside instead of answering when its team's deploy Ask
  offers it alone; `special-ambush-set-aside-at-last-ask` plays it.
- `ScenarioArmy.Passengers` (builder `passengers:`) maps a unit ordinal to the Transport unit ordinal it deploys inside: after the
  set-aside conditions, one `MoveModels` per unit from the shelf into the Transport's first model, as the roster's Put in transport
  does (passengers spec). `core-transport-deploy` plays it under strict.
- `ScenarioArmy.Conditions` (builder `conditions:`) lists (unit ordinal, condition) pairs the run sets before the first answer, after the set-aside ones,
  as the unit menu does: a unit Shaken from the start (the rally games).
- `ScenarioArmy.Models` (builder `models:`) gives one model its own rules, stats or weapons (`ScenarioModel(unit, model, ...)`, 1-based):
  a Hero in a unit, a lone caster.
- `ScenarioArmy.Reserves` (builder `reserves:`) marks a unit as an import's reserve unit (`ScenarioReserve`: the rule, and the unit or model
  holding it); it stays on the shelf until its rule places it. `HandPlacement` places a Reinforcement copy within 12" of a table edge.
- `Scenario.OptionalRules` (builder `Options`) is the game's optional-rules map, in `GameInfo` and the socket seeder's row.
  `ScenarioArmy.Sizes` (builder `sizes:`) gives a unit its own model count; `ScenarioArmy.Anchors` (builder `anchors:`) fixes where a unit's
  first model deploys, its row following as `HandPlacement` lays one. `core-perimeter-sight`, `core-hybrid-sight` and
  `core-melee-no-separation` (`SuiteCatalog.Core.Options.cs`) play the optional rules.
- `ScriptBuilder.Match(question, pick)` answers every Ask of that question the pick answers; a keyed answer for the same ordinal wins,
  a null pick falls through to the policy. `AskPicks` picks by label or name, never by id: `Activate(ask, "Red unit 1", "Charge")`,
  `Labeled`, `Spell(ask, projection, "Zap!")`, `Pieces(ask, projection, "Blue unit 1")`. Ids are minted at compile time, so a
  script cannot name them.

## The rules suite

- A `TestGame` is a `Scenario` plus its group (`core`, `special`, `army:<book uid>`, `spells:<book uid>`), `Covers` (catalog entry
  names), `Dice` (`DiceFix`) and `Checks`. `SuiteCatalog.All` holds every game, one partial file per group.
- The runner judges every game by `Checks.Ended()`, `Checks.Invariants()` and `Checks.FiredAll()`, then its own checks. A check returns
  null or the reason; its name quotes the rule and page. A check with `Entries` is an exact check, reported against those entries'
  coverage. A check that can fail for several reasons (`GameCheck.Each`: `invariants`, `fired`, the army exact checks) reports one
  failure line per reason, so a pin on one reason never covers another.
- The army attack checks (extra hits, attacks and wounds, self wounds, AP, per-die AP, re-rolls) take as occasions every attack the game
  names for its rule, from the attack-sequence frames and the army definitions: `By` the holder, `On` the holder, `With` the weapon,
  narrowed by `When.Shooting` or `When.Charging`. They never select by `Applied`, so a rule that stops acting on some occasions fails.
- Fired: the entry is in a step's `StepCompleted.Applied` or a `RollModifier.Source` of the effective log (`Fired.In`). A rule that acts
  where no step completes (Scout, Ambush, Immobile, Aircraft's Advance-only restriction) gets the game's `Evidence` predicate instead.
- `DiceFix(roll, ordinal, faces)`: the ordinal-th roll of that kind in the game gets those faces by `SetDice`. `SetDice` is no player
  command: a game server takes it only with `GameServerOptions.AcceptsSetDice` on, which dev mode sets (Development and `TABLETOP_DEV_MODE=1`: the
  compose `game` service) and the in-process driver passes to its `RulesEngineFactory`; anywhere else it is refused. A game with fixes runs
  with the step gate on (`Scenario.WaitsBetweenSteps`), so a completed step's rolls stay editable (the roll-off's too, whose Confirm opens only then); a fix that never meets its roll fails
  the game. A fix lands before the step that decides from those dice (opr-sequences.md → Dice edits), so the casting result, a
  hit's extra hits and per-die grants, and a morale test's consequence follow the fixed faces; added attack dice were drawn on the
  faces as rolled.
- `LogQueries.Of(run)` reads the effective log for checks: frames with parents and steps, rolls with dice as they stand after edits and
  rerolls, Asks with answers, removals, deployments, activations, moves. It reads the log; it never computes a rule.
- Expected values in checks are literals from the rule text. `sim/` holds no second rules engine.
- A game an engine defect blocks keeps its failing check and is pinned: `game.Pinned("D5", check, reason, driver?)`. A failure is the
  defect's only when it starts with `check` and contains `reason`, text the defect itself produces, so the same check failing for
  another reason is unexpected; `driver` limits a pin to one driver. The report marks matched failures `known defect <id>: `, counts
  the game as failing on a known defect when every failure matched, and lists each pin no failure matched
  (`(pinned Dn did not fail: <check>)`), pass or fail. The defect ids are listed in
  `docs/handoff.md` → Defects the rules suite found.
- A defect exhibit is a game whose only purpose is its pinned check; it covers nothing (`Covers` empty) and its check's `Entries`
  name the rule it shows.
- Coverage: every entry of the shipped set, with the games that cover it, whether it fired in all of them, and its exact checks. A
  run fails on a covered entry that did not fire; a full run also fails on an uncovered one, an `--only` run never does. A text-only
  entry (`played: false`, generated) is listed as not automated and never counted. A generated wrapper (an entry with effects in
  `grimdark-future-army-book-rules.part.json`) is covered by pattern: by the games whose `Pattern` is its name's last word, when
  they passed and its base is covered and fired.
- The report (`SuiteReportFiles`): every `suite` run, either driver, `--only` included, writes `suite-<run id>.json` and
  `suite-<run id>.md` to `--out` (default `./recordings`, git-ignored) and copies them to `suite-latest.json` and `suite-latest.md`.
  The JSON (`SuiteRunDocument`) holds the run id, driver, selection, start and end, `verdict` (`running`, `passed`, `failed`,
  `aborted`), the verdict line as `summary`, every `SuiteGameResult` (failure lines, known, pinned, stale, replay, seed,
  `durationSeconds`) and the coverage table. The Markdown is the verdict line, a Failures section (each failing game's lines and replay
  command), stale pins, a games table, then coverage. Both are rewritten after every game, so a run that throws or is cancelled
  (Ctrl+C) keeps its games under `aborted`, with no coverage; a second Ctrl+C ends the process.
- The console prints the run id and report path, a counter every 25 games, each failing game (and each stale pin) with its lines
  and replay command as it finishes, the coverage entries the run fails on, the verdict line and the report paths. `--verbose` adds
  a line per game and the whole coverage table.
- Socket games play on a fixed account pool: participant i of any game is `sim-player-<i>` (`SocketCredentials.NameFor`,
  `PoolSize` 9: eight armies and the recorder), and every kept game's spectator is `sim-spectator` (`SuiteRows.Spectator`).
  Every run and every concurrent game (the CLI suite beside `sim-runner`) shares them; the dev mint gives a name the same account
  every time, and a 409 from two first mints at once is asked again.
- A socket run holds its lock (`ISocketRows.HoldAsync`; Postgres: an advisory lock), sweeps games an interrupted run left (named `suite-run <run id> <game>`, `SuiteRows`), seeds and deletes each
  game's rows: its log and the game, which cascades team, participant and the table_state tree. It never deletes an account: the
  pool outlives every run, and deleting one would cascade into the participants of games still running. A failed delete is that
  game's failure. `--keep` keeps the game (`suite <run id> <game>`), writes each recording, and adds `sim-spectator` as a participant
  (no team) for the live feed.
- `serve` hosts the runner on `127.0.0.1:5180` (compose service `sim-runner`, socket driver at `ws://game:5200/ws`): `GET /suite`,
  `POST /runs {only, driver, keep}` (409 while one runs), `GET /runs/{id}/events` (server-sent `game` per result, then `done`),
  `GET /runs/{id}/recordings/{game}`, `POST /runs/{id}/games/{game}/spectate` (a fresh session for the kept game's spectator).
  The viewer's Tests page drives it. A kept run writes its report files and recordings to `<serve --out>/<run id>/`; an unkept
  run writes nothing.

## The lifecycle suite

- Spec `docs/superpowers/specs/2026-10-20-lifecycle-suite-design.md`. Every step goes through management's `/v1` (`ManagementApi`) or
  the game socket (`ClientSocket` outside play, `SocketDriver` in it); nothing is written to Postgres, so it needs no database connection.
- Shapes (`LifecycleShapes.All`): `two-players`, `self-play`, `team-game`, `late-joiner`, `ambusher`, `four-teams`, `leave-and-delete`.
  Account 0 creates; `lifecycle-player-0..7` take seats, `lifecycle-spectator` joins every played game and is the driver's recorder. Never deleted.
- Armies come from `POST /v1/dev/armies/import` (dev mode only) with the body `LifecycleArmy.List(book)` builds: book 0 is management's
  Army Forge fixture (embedded from `tests/Tabletop.Management.Tests/Fixtures/`), cut to three units, and the `ambusher` shape adds Ambush
  to its Beast Riders; books 1..3 are `Lifecycle/Armies/<uid>.json`, three units each cut from Human Defense Force, Battle Brothers and Orc
  Marauders. A `Seat` names its book. Nothing calls Army Forge.
- After play every shape is read by `FreeForAllChecks` (`Suite/`; p.50 and p.6): the roll-off ranks every team, deployment and each
  round's turns go round the order one unit a turn, every model stands in its team's zone at round one, round one follows the deployment
  order and later rounds who finished activating first, every team on the table takes turns, and `game_ended` names the teams holding the
  most markers. `core-four-teams` (eight armies on four teams, free-for-all corners) runs the same checks in the rules suite.
- A lobby edit is answered by a broadcast `GameInfo` and a setup edit by a broadcast `Board`, neither naming the edit, so
  `ClientSocket.EditAsync` waits on a condition of the state or the `Refused` with the edit's id. `ExpectRest` makes the next `AtRest`
  the one waited for (EndSetup's, a late army's).
- Play: `LifecycleScenario.From` reads the `CompiledScenario` back from the server's `GameInfo` and `Board` (the board's armies are
  the seats, the recorder last), then `SocketDriver` and `RunLoop` with the default policy, round cap 5: the game ends by its rules.
- Every step asserts the status (`GET /v1/games/{id}` and each participant's `?joined=true` row); a failure names its step. Each shape
  deletes its game and armies in `finally`; a run first sweeps lifecycle games and armies older than an hour.

## Commands

```bash
dotnet run --project sim -- --list
dotnet run --project sim -- skirmish --seeds 1..20             # in-process, recordings in ./recordings
dotnet run --project sim -- skirmish --seed 7 --rounds 2 --no-recording
dotnet run --project sim -- skirmish --driver socket           # needs the stack below
dotnet run --project sim -- suite                              # every suite game, in-process
dotnet run --project sim -- suite --only core,special --driver socket --keep
dotnet run --project sim -- suite --list
dotnet run --project sim -- lifecycle                          # every lifecycle shape against the stack (about 2 min)
dotnet run --project sim -- lifecycle --only late-joiner,ambusher
dotnet run --project sim -- serve                              # the runner service on 127.0.0.1:5180
dotnet test tests/Tabletop.Sim.Tests                           # unit; --settings tests/e2e.runsettings for Postgres, the suite games and the lifecycle shapes
```

- Suite options: `--only A,B` (game names or groups), `--driver in-process|socket`, `--keep`, `--out DIR` (the report and kept
  recordings), `--verbose`, `--list`. Read `recordings/suite-latest.md` after a run; each failing game also prints its lines and replay command.

- Lifecycle options: `--only A,B` (shape names), `--list`. Environment: `TABLETOP_SIM_MANAGEMENT` and `TABLETOP_SIM_WS` (defaults as
  below); no database connection.

- Options: `--seed N`, `--seeds A..B`, `--driver in-process|socket`, `--out DIR`, `--rounds N`, `--no-recording`, `--list`.
- `--driver socket` needs Postgres, the game server and management running, both servers in dev mode (Development and `TABLETOP_DEV_MODE=1`; the game
  server for `SetDice`, management for the session mint).
- Sessions come from `POST /v1/dev/sessions`, which exists only in dev mode, for the pool accounts above.
  `SocketCredentials.Refresh` mints a pool account's session again; `SocketDriver` sends it as a `Reauth` a minute (`renewBefore`) ahead of
  each socket's token `exp`, so a run longer than a session's lifetime keeps its sockets.
- Socket environment, from the process or the working directory's `.env`: `TABLETOP_DB_CONNECTION` (required), `TABLETOP_SIM_WS` (default `ws://127.0.0.1:5200/ws`), `TABLETOP_SIM_MANAGEMENT` (default `http://127.0.0.1:5100`).

## Conventions and pitfalls

- A scenario passes against both drivers (`DriverAgreementTests`, the socket one over the in-memory stores).
- Script keys are the question name and its ordinal in the run. Server-minted Ask ids are not keys.
- A wipe-out does not end a run. The round-4 end depends on the enforcement setting.
- Socket runs seed Postgres with the server's own effect ids, so grants are checked by name, not id.
- `sim/` references `server/game` (`Tabletop.GameServer`). No server references `sim/`.
- `viewer/src/lib/protocol/recording.generated.ts` is generated from the `Recording` types here. Changing them means regenerating (see `tests/AGENTS.md`).
- A change to `FrameProjector` or `LogLines` regenerates `viewer/tests/fixtures/` and ports to `viewer/src/lib/live/`.

## Docs

- `docs/design/simulation/design.md`
- `docs/superpowers/specs/2026-09-06-simulation-design.md`
- `docs/superpowers/specs/2026-10-09-rules-suite-design.md` (the suite)
- `docs/superpowers/specs/2026-10-20-lifecycle-suite-design.md` (the lifecycle suite)
- `docs/superpowers/specs/2026-10-02-spell-library-design.md` (the `casting` scenario), `docs/superpowers/specs/2026-10-02-army-rule-hooks-design.md` (`army-rules`), `docs/superpowers/specs/2026-10-02-objectives-design.md` (`objectives`)
