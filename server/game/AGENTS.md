# server/game

`Tabletop.GameServer` (`net10.0`, ASP.NET Core): the WebSocket game server. Clients, `sim/` and `viewer/` connect to `/ws`.
It reads management's rows, owns each game's append-only event log, and hosts the rules engine.

## Layout

- `Program.cs`: DI, `PropertiesSeeder` at startup (not in the `Test` environment), `/ws`, listens on :5200.
- `Auth/SessionVerification.cs`: `ISessionVerifier`, `StytchJwtVerifier`, `HttpJwksSource` (JWKS cached 10 minutes).
- `Configuration/`: `GameServerOptions` (from `TABLETOP_*`), `DotEnv`.
- `Session/`: `ConnectionHandshake`, `SessionRegistry`, `Hydrator`, `GameSession` (partials Connect, Lobby, Setup, EndSetup, Frames, Facts), `SocketConnection`, `PresenceTable`, `SessionHost`.
- `Dispatch/`: `CommandDispatcher`, `EnforcementPolicy` (silent/warn/strict), `HandMoves` (the `hand_move` breach), `LegalSetMembership`, `MenuTranslation`, `EventFilter`.
- `Log/`: `IEventLog`, `PostgresEventLog`, `InMemoryEventLog`, `Replay`.
- `Persistence/`: `IGameRows`, `PostgresGameRows` (+ `.Writes.cs`), `RowMapping`.
- `Setup/DraftEditor.cs`: edits to the setup draft.
- `Rules/`: `IRulesEngine` and `GameState`, `IGameRules`, `InertEngine`.
- `Rules/GrimdarkFuture/`: `GrimdarkFutureRules` and its `Gf*` partials (sequences, rolls, hooks, names).
- `Engine/`: `RulesEngine`, `RulesEngineFactory`, `IScheduler`/`ISchedulerFactory`.
- `Properties/`: `PropertySet` (pinned set), `PropertySetDocument`, `IPropertiesStore`, `PostgresPropertiesStore`, `PropertiesSeeder`.
- `Content/`: `grimdark-future-core.properties.json`, `grimdark-future-army-rules.part.json`, `army-books/<uid>.book.json`. All embedded resources.
- `Dice/`: `DiceSource`, `ScriptedDice` (tests), `DiceCursor`, `Roller`, `RollPlan`.
- `Effects/`: `LiveProperties`, `RollEffects`, `Conditions`, `StatReader`, `Lookup`, `Interaction`, `AppliedEntries`.
- `Interpreter/`: `SequenceInterpreter`, `FrameReader`, `Steps`, `StepContext`, `Windows`, `Battlefield`, `Sight`.
- `Scheduler/`: `SchedulerFactory`, `AlternatingActivationScheduler`, `PlayerTurnScheduler`, `SchedulerBase`, `RollOff`, `TurnCycle`, `ActivationOption.cs`, `SchedulerTypes.cs`.

## How it works

- `/ws` accepts a socket; `ConnectionHandshake` reads the first frame, which must be `Connect`.
- Handshake order: client version against `MinimumClientVersion` (`Rejected(UpgradeRequired)`), `ISessionVerifier` (`Rejected(InvalidSession)`), `IGameRows.FindParticipantAsync` (`Rejected(NotAParticipant)`), `SessionRegistry.GetOrHydrateAsync`, `GameSession.ConnectAsync`, then the read loop.
- A verifier or database exception closes the socket 1011; it is not a rejection.
- `StytchJwtVerifier` checks RS256 against Stytch's JWKS, issuer `stytch.com/{project}`, audience the project id, and the top-level `account_id` claim (our account UUID). An unknown `kid` refetches the keys.
- `Hydrator` builds a `GameSession` from the rows, the `game.game_event` log and `GameRulesRegistry.Resolve(info.GameRules)`. The engine comes from `IRulesEngineFactory.Create(rules, info)`.
- `GameSession` is one game in memory. All mutation runs on its mailbox loop (a `Channel`), so there is no lock. A newer socket for a participant replaces the older.
- A command frame goes to `CommandDispatcher.Dispatch`, which returns `Applied(events, warning)` or `Refused`. It never appends or broadcasts.
- `Classify` sorts commands: `Override` (menu edits: moves, markers, wounds, conditions, score, round), `Undo`, `Abandon`, `Answer`, `Engine` (dice commands).
- The session appends the events to the log first, then broadcasts, then calls `AdvanceAsync`: `IRulesEngine.Advance` runs repeatedly, each batch appended, until it returns nothing (cap 10000 iterations).
- An exception in `Advance` faults the session: every socket closes 1011.
- `RulesEngine.Advance` is `SequenceInterpreter.Advance` while frames are on the stack; the `IScheduler` runs only when no framed Ask is open and the stack is empty.
- Undo is `Undone(toSeq)`: events are never removed. `EventApplier.EffectiveLog` is the live log; `Replay.To` rebuilds a projection to a seq.
- Hydration decides from the log, not the row: an ended log over a `playing` row writes the row and advances nothing; any other log catches up through `AdvanceAsync`.
- Rows written: in `lobby` and `setup`, teams, participant team/army, configuration, `StartSetupAsync` (draft tree, status to `setup`), `WriteDraftAsync` (one write per drop), `EndSetupAsync` (final tree, `army_snapshot` per participant, status to `playing`).
- From `playing` on, the only row write is `EndGameAsync` (`playing` to `ended`, guarded on `playing`), after the log ends the game. A failed write is logged and the next hydration retries it.
- The engine holds no state between calls. A step reads position, values and history from the log (`FrameReader`) and returns one outcome: `Complete`, `Finish`, `OpenAsks`, or `Push`.
- `RulesEngineFactory` loads the game's pinned `PropertySet` by `game.PropertiesHash` (cached per hash) and calls `ISchedulerFactory`. A missing hash throws `PropertiesNotFoundException`.
- `SchedulerFactory` picks `AlternatingActivationScheduler` or `PlayerTurnScheduler` from `game.SchedulerShape`.
- Dice: die `i` of a game is a pure function of (`rng_seed`, `i`) in `DiceSource.Draw`. `DiceCursor.DrawnSoFar` counts dice in the effective log (roll records plus rerolls); edits draw nothing.
- Effects run two passes over live properties. Pass one (`LiveProperties`, to a fixed point): which properties are live, grants, suppressions, parameter changes, suppressed modifier classes. Pass two (`RollEffects` and readers): what the live properties do to a roll or stat.
- `StepCompleted.Applied` lists the catalog entries that acted in that run of the step, each once, in order of first application;
  null when none did. Each `StepContext` owns an `AppliedEntries`: a live set settled through `StepContext.Settle` records every
  effect row that applies, and a ruleset hook that changes a result adds the rule it names (`ctx.Applied.Add`). A rule that acts in
  the scheduler or in action legality (Scout, Ambush, Immobile) completes no step and is not listed; Aircraft's action restriction is
  not listed either, but its move (p.13: +30", no terrain) is, on the move step. The rules suite reads it
  to judge which rules fired.
- `SequenceInterpreter` opens a "Continue: {step}" confirm Ask between steps when `Info.WaitsBetweenSteps` is on, gated to the actor's team. The client never offers the flag; it serves development. A frame with no actor and no parent (the roll-off) is always gated, to every team.
- A `MoveModels` goes through `HandMoves.Judge` and `EnforcementPolicy.Decide`: placing the unit the actor's team last picked at a deploy Ask from the shelf onto the table, or inside a container the rules allow (`IGameRules.MayDeployInside`), is legal; anything else is `hand_move`, which `silent` accepts, `warn` warns and `strict` refuses.
- An Ask answer inside its legal set is also asked of `IGameRules.AnswerBreach`; a breach is outside the legal set under the enforcement
  setting. Grimdark Future's is `GfMoves.DifficultCapBreach`: p.11 holds every model of a `MoveRegion` with a `DifficultCap` to it once any model's path enters a listed difficult volume.
- `SightQuery` is answered to the asker alone from `Sight.Line` per model pair and `IGameRules.Shooters`: nothing appended or broadcast, no `AtRest`. `ModelFacts` (`IGameRules.MaxWounds`) goes to a client on connect once playing, and to everyone after a batch that changes a value. `Shooters` reads the game's `PropertySet` through `IRulesEngine.Properties`; Grimdark Future's calls `GfAttack.AttackInteraction`'s frame-optional core with no frame, the core the fire groups share.
- `IStep.Label` puts a step in its sequence's outline; `SequenceStarted.Outline` carries the labelled steps in order.
- `Windows` offers interrupting reactions as one Ask per eligible team, in reaction order (`GameRules.OrderReactions`, else turn order after the actor's team).
- `Rules/GrimdarkFuture/` is the only place a rule is named. The interpreter, effects, dice and generic scheduler know no ruleset; the scheduler's Grimdark Future hooks are `GfRules.Scheduling.cs` and `GfRules.Deployment.cs`.

## Commands

```bash
docker compose up -d                                        # Postgres, MinIO, management :5100, game :5200
dotnet run --project server/game/Tabletop.GameServer.csproj # host run; stop the container first: docker compose stop game
dotnet test tests/Tabletop.GameServer.Tests                 # needs Postgres up
dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~GfAttack"
./scripts/army-books fetch                                  # downloads books from Army Forge (the only network call)
./scripts/army-books generate                               # rewrites Content/army-books/ from the fetched copy
```

- Under compose, `game` runs `dotnet watch run` over the bind-mounted repo with `TabletopBuildSlot=-game`, so it builds into `obj/Unix-game/` and `bin/-game/`. A host `dotnet build` never disturbs it.
- `game` starts after management is healthy, which has run the migrations.
- Both servers bind `127.0.0.1` under compose.
- `dotnet run` reads `.env` through `DotEnv` and needs `TABLETOP_DB_CONNECTION` and `TABLETOP_STYTCH_PROJECT_ID`.

## Conventions and pitfalls

- The engine never appends or broadcasts. The session does both.
- Dice are never drawn from a clock or a global generator. Anything random goes through `Roller` and `DiceCursor`.
- Authored properties are `Content/*.properties.json`, every `*.part.json`, and every `army-books/*.book.json` of the same game rules. `PropertiesSeeder.Compose` folds them into one document per core file, ordered by name and uid.
- Seeding is by content hash at startup. Editing any of these files makes a new set with a new hash; games already created keep their hash.
- Two sources carrying an entry of the same name must have equal canonical forms, else `Compose` throws.
- A `*.book.json` is never edited by hand. Fix `scripts/ArmyBooks` and run `./scripts/army-books generate`.
- New content files need the `EmbeddedResource` globs in `Tabletop.GameServer.csproj`; the three patterns there are `*.properties.json`, `*.part.json`, `army-books/*.book.json`.
- The game server writes a game's rows only in `lobby` or `setup`, plus `playing` to `ended` once. Management writes at creation, end, leave and delete.
- `WaitsBetweenSteps` is the per-game flag behind every "Continue" Ask (`SequenceInterpreter.cs`). Players never see it.
- Every `IGameRules` wrapper that forwards by hand (the tests' `WithSequences`) forwards `MaxWounds`, `Shooters`, `MayDeployInside` and `AnswerBreach`, or a wrapped ruleset answers 1, nobody, never and nothing broken.
- `ProjectionDesyncException` at hydration is a server bug: every reconnect replays the same event.
- The first `Connect` frame has `HandshakeTimeout` (10 s by default).
- Schema changes are management migrations, never made here.

## Tests

- `tests/Tabletop.GameServer.Tests`, xUnit. Classes that touch the database carry `[Collection("Postgres")]` (`Infrastructure/PostgresFixture.cs`); they read `TABLETOP_DB_CONNECTION` and need the Docker Postgres with management's schema.
- `Infrastructure/` holds the fixtures: `GameServerFactory`, `SchedulerHarness`, `ScriptedStarter`, `FixedAskEngine`, `TestProperties`, `FakeRules`/`StubRules`, `RecordingClient`.
- `ScriptedDice` fixes faces. Rules tests are named `Gf*Tests.cs`.
- `EventLogContract.cs` is run against both `InMemoryEventLog` and `PostgresEventLog`.

## Docs

- `docs/design/game-server/design.md`, `docs/design/interpreter/design.md` (`effect-grammar.md`, `opr-sequences.md`), `docs/design/scheduler/design.md`.
- Plans: `docs/superpowers/plans/2026-09-05-game-server-skeleton.md`, `2026-09-05-interpreter-and-effects/`, `2026-09-05-scheduler/`, `2026-10-02-army-rule-hooks/`, `2026-10-02-enforcement-engine/`.
- Specs: `docs/superpowers/specs/2026-09-04-scheduler-design.md`, `2026-10-02-enforcement-engine-design.md`, `2026-10-02-army-rule-hooks-design.md`, `2026-10-02-spell-library-design.md`, `2026-08-29-effect-grammar-design.md`, `2026-09-03-action-sequences-design.md`, `2026-09-04-spell-effects-design.md`, `2026-08-28-game-flow-design.md`, `2026-08-28-resolution-design.md`, `2026-09-04-setup-phase-design.md`, `2026-09-06-ruleset-3-5-1-design.md`, `2026-10-02-objectives-design.md`.
