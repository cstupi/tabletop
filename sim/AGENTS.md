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
- `Lifecycle/`: the lifecycle suite. `LifecycleShape` (`LifecycleShapes`), `LifecycleRunner` (`LifecycleEndpoints`, `LifecycleResult`), `LifecycleGame` (partials `.Lobby`, `.Play`, `.Leave`, `.Whole` with `WholeGame` and `WholeGameRecord`), `LifecycleScenario`, `ClientSocket`, `ManagementApi`, `LifecycleArmy`, `LifecyclePlayer`.
- `Run/`: `RunLoop`, `RunOutcome`, `RunReport`, `AskFingerprint` (stuck detection), `HandPlacement` (deployment from the seed).
- `Invariants/`: `Invariants` (the checks), `ProjectionComparer`.
- `Recording/`: `Recording`, `RecordingJson` (`SchemaVersion`), `FrameProjector`, `LogLines`.

## How it works

- `ScenarioCatalog` holds `skirmish`, `terrain`, `player-turn`, `free-for-all`, `stalemate`, `final-round`, `final-round-warn`, `final-round-silent`, `casting`, `army-rules`, `objectives`. `--list` prints them.
- A scenario has a seed, a round cap, a scheduler shape, an enforcement setting, armies, terrain, an optional `AnswerScript` and `Expectation`s.
- A scenario plays Grimdark Future v3.6.0 (`Scenario.Rules`, `ScenarioBuilder.Rules(id)`; default `RuleSets.Gf360`) unless it names
  `RuleSets.Gf351`; every catalog scenario plays 3.6.0, its rules named as the 3.6 books print them (`army-rules`: Grounded Defense).
- `ScenarioCompiler` turns it into a `CompiledScenario`; `CorePropertiesOf(rulesId)` gives an edition's shipped set and `CoreProperties` is 3.5.1's; `Program.PlayAsync` builds `ComposedAnswers`: the script first, then `Scenario.Policy()` unless `ScriptOnly`.
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
- A charge move (`MoveRegion.ContactWith`, p.9) answers with `UnitFormation.InContact` over the Ask's own parameters (`ContactVertical` included), the
  search the server's `charge_contact` makes, each landing at the rest height it returns, so a charge that could reach contact in coherency does;
  when there is none it is sampled as any move.
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
  holding it); it stays on the shelf until its rule places it. `HandPlacement` places a Reinforcement copy within 12" of a table edge (3.6: 9", over 9" from enemy models; `GfEdition.ReinforcementEdgeReach`, `ReinforcementClearance`).
- `HandPlacement` anchors an Ambusher over its distance from enemy models (`GfAmbush`), and under 3.6 a Scout in its zone over 9" from
  models of non-Scout enemy units (`GfEdition.ScoutClearance`, read from `CompiledScenario.Info.GameRules`).
- `Scenario.OptionalRules` (builder `Options`) is the game's optional-rules map, in `GameInfo` and the socket seeder's row.
  `ScenarioArmy.Sizes` (builder `sizes:`) gives a unit its own model count; `ScenarioArmy.Anchors` (builder `anchors:`) fixes where a unit's
  first model deploys, its row following as `HandPlacement` lays one. `core-perimeter-sight` (the core game's perimeters), `core-per-model-sight`, `core-hybrid-sight` and
  `core-melee-no-separation` (`SuiteCatalog.Core.Options.cs`) play the optional rules.
- `ScriptBuilder.Match(question, pick)` answers every Ask of that question the pick answers; a keyed answer for the same ordinal wins,
  a null pick falls through to the policy. `AskPicks` picks by label or name, never by id: `Activate(ask, "Red unit 1", "Charge")`,
  `Labeled`, `Spell(ask, projection, "Zap!")`, `Pieces(ask, projection, "Blue unit 1")`, and for `declare_targets` `ShooterOf`, `AllAt(ask, projection,
  "Blue unit 2")` (every weapon group at one unit) and `Aimed(ask, projection, shooter => target)`. Ids are minted at compile time, so a
  script cannot name them.

## The rules suite

- A `TestGame` is a `Scenario` plus its group (`core`, `special`, `army:<edition>:<book uid>`, `spells:<edition>:<book uid>`), `Covers` (catalog entry
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
- `DiceFix(roll, ordinal, faces, key?)`: the ordinal-th roll of that kind in the game gets those faces by `SetDice`. With a `RollKey`
  (actor, target, weapon, action, by name), only the rolls whose frame's context matches are counted, so the fix lands on that unit's
  roll however many others the seed rolled first; a game that needs a situation places and scripts its units into it and keys its fixes. `SetDice` is no player
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
  `docs/handoff.md` → Rules-suite gaps.
- A defect exhibit is a game whose only purpose is its pinned check; it covers nothing (`Covers` empty) and its check's `Entries`
  name the rule it shows.
- A `TestGame` runs under `Edition` (a game-rules id, `RuleSets.Gf351` unless the game names one: `.In36()`, `.BothEditions()`, a 3.6
  book's game, `SpellGames.Game` over a set; the scenario's own `Rules` does not choose it): the
  scenario compiles against that edition's core set and its armies' snapshots carry the edition. `Editions: [Gf351, Gf360]` runs it
  twice (`SuiteCatalog.Expand`), the 3.6 run named `<name>@3.6`, the 3.5.1 one keeping the bare name; a game under 3.6 alone is
  `<name>@3.6` too. Groups `army:<edition>:<uid>` and `spells:<edition>:<uid>` (`SuiteCatalog.ArmyGroup`, `SpellsGroup`); `--only <name>` selects the 3.5.1 run, `--only <name>@3.6` the 3.6 one, a group both. Core and special games whose checks quote no rule the 3.6 set changes run both (`.BothEditions()` in their catalog list); the rest are 3.5.1-only. Army and spell games are single-edition (`Expand` throws on more than one: the books' uids differ per edition), and `BookOf` takes the three-part group only.
- `--seed N` plays every selected game on seed N instead of its scenario's own (`SuiteOptions.Seed`; the policy, the hand placement and the
  server's dice all follow it), and each failing game's replay line carries it. Without it every game keeps its own seed. A full run on
  seeds 1–5 (`suite --seed 1` … `--seed 5`, about 4.5 min with all five in parallel, each with its own `--out`) is the robustness check.
- Spell games deploy every unit on its lane (`SpellSetup.Anchored`, on by default) and set each caster's first casting die to 6
  (`SpellGames.Game`'s `firstCastsPass`; off for the repeated-spell and edited-casting-die games), so every spell resolves at least once.
- `spells:3.6:<uid>`: `SpellGames.Books(set)` over the 3.6 set, one game per book with a played spell; a text-only spell (`played: false`) is
  neither cast nor covered. The tables are per edition (`SpellTables` 3.5.1, `SpellTables36` in `SuiteCatalog.Spells36.cs`), each check quoting
  its edition's text. A `SpellSetup`'s `Rules` and `TargetRules` add rules to every model of the book's army or the targets (Caster for a spell that takes tokens, Wolfborn for one that boosts it). `TargetModels`, `TargetTough`, `TargetDefense` and `Anchored` shape the targets and deploy every unit on its lane. `SuiteCatalog.RepeatedSpellGames36` (Decimate, Destructive, Smash, Pierce, Scourge) are no book's game: six units on fixed lanes cast one spell, each aimed at a set target (`Aimed`) so casts land on both sides of the rule's line (half the target's size, half the spell's range), with fixed casts and blocks (`Passes`, `Blocks`) where the check needs one; they cover nothing. `army:3.6:<uid>` (`SuiteCatalog.Army36.cs`): a 3.5.1 game whose rule 3.6 keeps word for word (quote and case changes included; `Requote36` gives its check names the 3.6 text) plays again on the 3.6 book
  that prints it (`Kept36`, `Books36`), a renamed rule gets its game under the 3.6 name, and `SuiteCatalog.Army36<batch>.cs` cover
  the rules 3.6 rewrites.
- `SuiteCatalog.Core36.cs` and `SuiteCatalog.Special36*.cs` hold the 3.6-only games, one per rule whose 3.6.0 text changed or is new: a game declared `.In36()` (`Scripts36`, `Edition = RuleSets.Gf360`) runs under 3.6.0 alone as `<name>@3.6`, its checks quoting `3.6.0 p.N`; `core-side-battle` there runs both (`.BothEditions()`).
- The 3.6.0 game files are `SuiteCatalog.Core36.cs`, `Special36*.cs`, `Spells36.cs`, `Army36.cs` and the army batches `Army36A.cs`, `Army36B.cs`, `Army36C.cs`, `Army36F.cs`, `Army36G.cs`, `Army36H.cs`/`Army36H2.cs`, `Army36I.cs`/`Army36I2.cs`, `Army36J.cs`/`Army36J2.cs`, `Army36K.cs`, `Army36L.cs`, `Army36M.cs`, `Army36N.cs`, `Army36P.cs` (a trailing `2` splits a batch; `Books36.cs` holds the 3.6 book table); `SuiteCatalog.All` reads them all. A `special` game carries three to five rules (rules-suite spec); the bound holds for 3.5.1 games only, a 3.6.0 game covers as many entries as its rule needs. A game cannot show a refused placement (the run loop reads a refusal as Stuck), so `GfScout36Tests` proves Scout's refusals. A `DiceFix` lands after the roll it names, so Bane's re-rolls inside roll-to-block and Shred's `Applied` are decided on faces as rolled.
- Coverage: per shipped set (`SuiteRunner.CoverageOf`), every entry of each set with the games of its edition that cover it, whether it fired in all of them, and its exact checks. A
  run fails on a covered entry that did not fire; a full run also fails on an uncovered one, an `--only` run never does. A text-only
  entry (`played: false`, generated) is listed as not automated and never counted. A generated wrapper (an entry with effects in
  `army-book-rules.part.json`) is covered by pattern: by the games whose `Pattern` is its name's last word, when
  they passed and its base is covered and fired.
- The report (`SuiteReportFiles`): every `suite` run, either driver, `--only` included, writes `suite-<run id>.json` and
  `suite-<run id>.md` to `--out` (default `./recordings`, git-ignored) and copies them to `suite-latest.json` and `suite-latest.md`.
  The JSON (`SuiteRunDocument`) holds the run id, driver, selection, start and end, `verdict` (`running`, `passed`, `failed`,
  `aborted`), the verdict line as `summary`, every `SuiteGameResult` (failure lines, known, pinned, stale, replay, seed,
  `durationSeconds`, `edition`) and the coverage table (each entry with its set's `edition`). The Markdown is the verdict line, a Failures section (each failing game's lines and replay
  command), stale pins, a games table (with an Edition column), then coverage per set. Both are rewritten after every game, so a run that throws or is cancelled
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
- `serve` hosts the runner on `127.0.0.1:5180` (compose service `sim-runner`, socket driver at `ws://game:5200/ws`): `GET /suite` (each game's name, group, covers, `edition` and `ruleSet` name),
  `POST /runs {only, driver, keep}` (409 while one runs), `GET /runs/{id}/events` (server-sent `game` per result, then `done`),
  `GET /runs/{id}/recordings/{game}`, `POST /runs/{id}/games/{game}/spectate` (a fresh session for the kept game's spectator).
  The viewer's Tests page drives it. A kept run writes its report files and recordings to `<serve --out>/<run id>/`; an unkept
  run writes nothing.

### Writing a suite game that holds

Owner, 2026-10-26: *"Why would dice seeds need to change because charge has a leeway?!"* A game must reach its checked situation by
construction, never by the seed's luck: one extra die anywhere earlier (a model drawn into contact, a dangerous-terrain test on another
path) shifts every later die, and a seed-dependent game breaks with no rule wrong.

1. Place what the check needs: `Army(...)` sizes, `unitRules`/`modelRules`/`unitStats`, `setAside`, `Table(...)`, terrain, and
   `Seeking(...)` for where the policy moves.
2. Script every decision the check depends on with `AnswerScript`: `.Match(question, pick)` by unit name (`AskPicks.Activate`,
   `AskPicks.Pieces`) rather than `.At(question, ordinal, ...)`, which shifts like a dice index.
3. Pin only the dice the check reads (`DiceFix`), and only where the roll is reached by steps 1–2, keyed to the roll with a `RollKey`
   (actor, target, weapon, action) so its ordinal counts that unit's rolls alone: `new DiceFix("morale", 0, [1], new RollKey(Actor: "Red unit 3"))`.
   `special-artillery-emplacement` and `special-command-squad` are worked examples.
4. Check the game on several seeds: `suite --only <game> --seed N` for seeds 1–5 and a few others. It is done when every seed passes.
5. Never fix a broken game by changing its seed.

### A game left at an Ask, for a look on screen

The compose `game` service runs `main`; a worktree's server change is looked at on its own server:

1. Run the worktree's game server on a free port: the `TABLETOP_*` lines of `.env` exported, `ASPNETCORE_ENVIRONMENT=Development`,
   `TABLETOP_DEV_MODE=1`, `Kestrel__Endpoints__Http__Url=http://127.0.0.1:<port>`, `dotnet run --no-launch-profile --project server/game`.
2. Add a throwaway scenario (never committed) with `SelfPlay()`, the table and terrain wanted, a script that answers up to the Ask and a
   `.Match(question, …)` that throws there; run it with `--driver socket --no-recording`, `TABLETOP_DB_CONNECTION` from `.env`,
   `TABLETOP_SIM_WS=ws://127.0.0.1:<port>/ws`. The run fails at that Ask and the game stays open on it.
3. Sign in `sim-player-0` (`POST /v1/dev/sessions`) and open the game: Godot with `gameServerUrl` pointing at the port (client
   `AGENTS.md` → Driving a game by eye; Home then Space then the wheel frames the acting unit from above).
4. Delete the game's rows after (`game.game_event` by `game_id`, then `management.game`).

## The lifecycle suite

- Spec `docs/superpowers/specs/2026-10-20-lifecycle-suite-design.md`. Every step goes through management's `/v1` (`ManagementApi`) or
  the game socket (`ClientSocket` outside play, `SocketDriver` in it); nothing is written to Postgres, so it needs no database connection.
- Shapes (`LifecycleShapes.All`): `two-players`, `two-players-3.5`, `self-play`, `team-game`, `late-joiner`, `ambusher`, `four-teams`,
  `goblins-vs-disciples`, `hdf-vs-ratmen`, `self-play-ratmen-hdf`, `leave-and-delete`.
  Account 0 creates; `lifecycle-player-0..7` take seats, `lifecycle-spectator` joins every played game and is the driver's recorder. Never deleted.
- Armies come from `POST /v1/dev/armies/import` (dev mode only) with the body `LifecycleArmy.List(book, ambush, edition)` builds. 3.6
  (the default): `Lifecycle/Armies/3.6/<uid>.json`, three units of each book at their default loadout, cut from the 3.6 Army Forge dump:
  book 0 Daemons of War (Blood Warriors, Blood Hounds, Blood Beast Riders), then Human Defense Force, Battle Brothers and Orc Marauders.
  3.5: book 0 is management's Army Forge fixture (embedded from `tests/Tabletop.Management.Tests/Fixtures/`), cut to three units; books
  1..3 are `Lifecycle/Armies/<uid>.json`. The `ambusher` shape adds Ambush to book 0's riders (`LifecycleArmy.AmbushUnit(edition)`). A
  `Seat` names its book, or a whole list (`Seat.List`, `LifecycleArmy.Lists`: `Lifecycle/Armies/3.6/lists/<name>.json`, 1000-point Army
  Forge export bodies imported as they stand, joined Heroes, combined units and upgrades included); `LifecycleArmy.For(seat, …)` gives the
  body and the lobby asserts the import holds `LifecycleArmy.UnitsOf(body)` units besides reserve copies (entries that join no other). Nothing calls Army Forge.
- A shape with a whole-list seat is `Whole`: after EndSetup every unit the creator socket's `ModelFacts.MaySetAside` names is set aside
  (Scout, Ambush, an Ambush Aura), it is played by `LifecyclePlayer` (the uniform policy over each Ask's legal set, taking a charge or a shot
  when offered and moving toward the nearest enemy model, a `MoveGoal`), `FreeForAllChecks.DeployedInZones` is skipped (a Scout deploys
  outside the zone, a Vanguard moves out of it; the server judges both), and `WholeGame` asserts: every non-reserve unit came on by its own
  deploy Ask (set aside: `deploy_scouts`, or `deploy_ambusher` from round two), four rounds and `game_ended`, at least one shooting attack, one
  melee attack and one morale test, no event accepted with a warning, each Reinforcement copy offered (`deploy_reinforcement`) in the round
  after its original was destroyed and no other, placed only by that Ask, and bodied on its original's slots in the board's snapshot. Its
  `WholeGameRecord` (rounds, attacks, wounds as failed defense dice, casualties, units destroyed, copies and placed, morale tests, Asks
  answered by question) is printed as the shape finishes. `TABLETOP_SIM_LIFECYCLE_SEED` sets the policy's and placement's seed (default 1).
- `LifecycleShape.Rules` (default `Gf360`) is the game-rules id the game is created on and `Edition` the army edition it takes; the import
  body carries `edition`, and the lobby step asserts a `Gf360` game's set is the default. `two-players-3.5` is `two-players` on `Gf351`
  with the 3.5 lists, the one shape that keeps 3.5.1 covered.
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

### Adding a real Army Forge list as a lifecycle shape

1. Save the list's Army Forge export body (the JSON management's import reads; fetched by hand, never by a test) as
   `sim/Lifecycle/Armies/3.6/lists/<name>.json`; the `lists/*.json` glob in `Tabletop.Sim.csproj` embeds it.
2. Add `<name>` to `LifecycleArmy.Lists`, and an `InlineData` (entries, units that join no other) to
   `LifecycleArmyTests.A_whole_list_is_the_army_forge_export_with_joined_entries_counted_once`.
3. Add a shape seating it: `new Seat(account, team, List: "<name>")`, a name of at most 21 characters (the game name's 64). `Whole` follows.
4. `dotnet run --project sim -- lifecycle --only <shape>` against the stack (`TABLETOP_SIM_MANAGEMENT=http://127.0.0.1:5173` on WSL), on
   `TABLETOP_SIM_LIFECYCLE_SEED` 1..3. A refusal, stall or warning names the game, Ask and frames; that is the server's finding, not the script's.

## Commands

```bash
dotnet run --project sim -- --list
dotnet run --project sim -- skirmish --seeds 1..20             # in-process on 3.6.0, recordings in ./recordings
dotnet run --project sim -- skirmish --seed 7 --rounds 2 --no-recording
dotnet run --project sim -- skirmish --driver socket           # needs the stack below
dotnet run --project sim -- suite                              # every suite game, in-process
dotnet run --project sim -- suite --only core,special --driver socket --keep
dotnet run --project sim -- suite --seed 3                     # every game on seed 3 (robustness)
dotnet run --project sim -- suite --list
dotnet run --project sim -- lifecycle                          # every lifecycle shape against the stack (about 2 min)
dotnet run --project sim -- lifecycle --only late-joiner,ambusher
dotnet run --project sim -- serve                              # the runner service on 127.0.0.1:5180
dotnet test tests/Tabletop.Sim.Tests                           # unit; --settings tests/e2e.runsettings for Postgres, the suite games and the lifecycle shapes
```

- Suite options: `--only A,B` (game names or groups), `--driver in-process|socket`, `--keep`, `--out DIR` (the report and kept
  recordings), `--verbose`, `--list`, `--seed N`. Read `recordings/suite-latest.md` after a run; each failing game also prints its lines and replay command.

- Lifecycle options: `--only A,B` (shape names), `--list`. Environment: `TABLETOP_SIM_MANAGEMENT` and `TABLETOP_SIM_WS` (defaults as
  below), `TABLETOP_SIM_LIFECYCLE_SEED`; no database connection.

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
