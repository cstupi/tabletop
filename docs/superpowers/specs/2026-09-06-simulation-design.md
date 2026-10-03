# Simulation and the developer viewer

Design session, 2026-09-06. How a full game is played and watched without the Godot client.

The design phase closed on 2026-09-04 and the eight implementation plans followed. This is a
ninth module, added because the existing test harnesses are per-sequence fixtures: the
interpreter's `Sim` drives one sequence, the scheduler's `SchedulerHarness` drives a few turns.
Nothing plays deployment through to an ended game, and nothing shows what happened except a
log of event names.

Two deliverables. `sim/` plays games. `viewer/` draws them.

## `sim/` is a fourth deliverable

The layout rule in `CLAUDE.md` had three shipping directories, `client/`, `server/` and
`web/`, with everything else either consumed (`libs/`) or repo support (`docs/`, `scripts/`).
`sim/` and `viewer/` are a fourth and fifth. Ruled by the project owner on 2026-09-06 rather
than living under `tests/`, because a simulator that only xUnit can start is not a thing you
can point at a problem.

They ship to developers, not to players. Neither is in the client's export, neither is
referenced by a server, and a release that omits both is complete.

One thing the simulator must do that is not an answer to an Ask: **deploy**. Deployment's Ask
names a unit, and putting that unit's models on the table is an ordinary `MoveModels` command
rather than an Ask (the scheduler → *The turn is an Ask*: "Hand moves never end a turn"). So
`sim/` places models itself, inside the team's own deployment zone, resolving the zone's shape
with `Tabletop.Geometry.ZoneStyles`. That is geometry rather than a rule, and nothing judges
whether a placement is good — placement is free under every enforcement setting.

## Two ways to answer an Ask

Every decision in a game arrives as an Ask carrying a server-computed legal set. A simulator is
therefore a function from an Ask to an answer, and there are two useful ones.

- **A script.** Named answers in order, asserted against the log. This is the regression form:
  a rules interaction is pinned as a scenario and fails loudly when the engine changes under
  it.
- **A seeded policy.** Any Ask, answered from its legal set with a seeded random choice. This is
  the soak form: a game plays to completion unattended, and five hundred games with five
  hundred seeds is a fuzz run.

**Who answers a multi-team Ask.** An unsealed Ask addressed to several teams, such as the roll-off `confirm` gate that opens every game or `end_round`, is answered once, by the first listed team that has not answered. A sealed Ask is answered by every addressed team, each in turn.

**Both, and mixed in one run.** A scenario scripts the opening and hands the rest to the
policy, which is how a specific position becomes a whole game. An unscripted Ask under a
script-only run is a failure; under a mixed run it goes to the policy.

The policy is not an AI opponent and must not become one. It chooses legally, not well.

**Ruled 2026-09-06.** The simulator may read anything the engine exposes: properties, stats,
wounds, conditions, teams, the projection, the pinned `PropertySet`. An earlier draft banned all
of it, which was both over-broad and self-contradictory — the invariant checks, the expectations
and the recording all read wounds, conditions, positions and properties. Two prohibitions carry
the argument instead:

1. **It must never narrow the legal set.** An answer is chosen from what the server offered,
   whole. A policy that filtered the set to answers it judged sensible would stop exercising the
   ones that find bugs.
2. **It must never compute a rules outcome the engine should compute.** No deciding for itself
   whether a target is in range, whether a wound lands, or what a modifier totals. A second rules
   implementation inside the simulator can agree with a broken engine and hide the fault.

Separately, and for a different reason: the **default** policy stays uniform over the legal set
and ignorant of state. A cleverer policy explores a narrower, more plausible region and finds
fewer bugs, so a state-reading policy is opt-in and named as such, never the default for a soak
run. An opponent that plays well is a later feature and would live behind the same seam.

## Two drivers behind one interface

A scenario runs against either of two backends, and the assertions do not change.

- **In process.** The session, the engine and an in-memory log, constructed directly. No socket,
  no Postgres, no Docker. This is the default because a full game must cost milliseconds for a
  fuzz run to be worth having.
- **Over the socket.** A real WebSocket to a running game server, a real session JWT, real rows
  and a real event log in Postgres. This exercises the handshake, the wire format, hydration,
  reconnect and the whole path `architecture.md` calls the only path.

The same scenario passing in process and failing over the socket localises the fault to
transport, serialization or persistence, which is the reason both exist. A driver interface with
one implementation would not have been worth the abstraction.

## The recording is the artifact

A run writes one file: the board, then one frame per event, each frame carrying the model
positions, wounds, conditions and the log line that event produced. It is a **recording of a
projection, not a log of events**.

That is the load-bearing decision, and it is what keeps the event applier a single
implementation. A viewer given raw events would have to apply them, which means porting the
applier to TypeScript, and a second applier drifts. Given frames, the viewer interpolates
nothing and decides nothing; it draws what it is handed.

The recording is derived and disposable. It is not a save format, it is not replayed into the
engine, and the event log remains the only thing undo and hydration read.

## `viewer/` is a developer web app, separate from `web/`

A static page, its own top-level directory, sharing no code and no build with `web/`. Ruled
2026-09-06: `web/` owns preparation for players, and a debugging surface for the person writing
the engine does not belong in it.

Top-down and schematic on purpose. A grid in inches, each model's base outline (circle, oval or rect) coloured by team,
terrain as outlines carrying their declarations, deployment zones, and a timeline with play,
step and scrub. The log sits beside the board and clicking a line jumps to that sequence.
Nothing is rendered in 3D and no mesh is ever loaded, so a model is its base and its facing.

**Canvas rather than SVG, ruled by the viewer plan 2026-09-06.** Frames carry whole state, so
moving one step invalidates every model at once and there is no partial update for a
retained-mode tree to exploit; a few hundred SVG nodes rewritten per frame pays style
recalculation and layout for the whole document at pointer rate while scrubbing. The two things
SVG gives away free — hit testing and text layout — are cheap here, because a base is a circle, a
square or a convex 64-gon in table space, so picking is a point test in inches.

### Two feeds

- **A recording**, loaded from a file. The primary case, and the only one that works with no
  server running.
- **A live game**, over the socket, attached as a participant with no team. Presence, the
  scheduler and the interpreter already treat that participant as a spectator who is never
  Asked, so watching a real game between two clients needs no new server concept.

The live feed costs what the recording feed was designed to avoid: the socket carries events,
so the viewer must apply them, so `Tabletop.Protocol`'s message types and the event applier
exist a second time in TypeScript. **Accepted, with the drift generated away where possible**:
the TypeScript types are generated from the C# records rather than hand-written, and the
applier is checked against the C# one by replaying a recording through both and comparing
projections. A hand-maintained second applier was rejected.

The check is stronger than comparing final projections, and it needs no separate export: a
recording's frames **are** the C# applier's output, canonicalised, so the test hands TypeScript
the three socket frames of a run (`welcome`, `board`, `events`) and requires it to reproduce
every frame of that run's recording field for field. Two checked-in fixtures, a couple of hundred
intermediate states each. Collection order is normalised on both sides before comparison — a
`Dictionary` and a `Map` can diverge once an entry is removed and another added, and that is not
a drift in the applier.

### Interactive debugging is a door, not a room

Playback is the whole of the first version. The viewer sends no command and answers no Ask.

The design leaves the seam for it: pausing a simulation and answering an open Ask from the
browser is the obvious next thing, so the recording carries the open Ask at every frame, the
driver interface has a step-and-wait mode, and the viewer's feed abstraction has room for a
control channel. None of that is built yet, and building it makes the viewer a second thin
client, which is a larger surface than a renderer.

## What a full game means

A run reaches one of five ends. Three are the game ending; two are the harness. A run that
reaches none of them is a bug the simulator exists to find.

| End | Condition |
| --- | --- |
| Ended | The game logged `game_ended`; a wipe-out is not a win (p.6) |
| Round limit | A configured round cap, since a run under `warn` or `silent` plays past the final round and a stalemate must still terminate. A cap of *n* means *n* whole rounds: the run ends on round *n*'s boundary Ask rather than part-way through round *n* + 1 |
| Stuck | No Ask is open and the engine emits nothing, or an Ask is answered and reopens unchanged |
| Failed | An Ask the script does not cover in a script-only run, a failed scenario expectation, or a failed invariant. This is an assertion breach rather than a game ending. A failed invariant or expectation shows as `failed` in the recording only for an otherwise ended or round-capped run, with the first failure as the detail. Stuck and Failed runs keep their own kind in the recording. `RunReport.Passed` decides the console's exit code, which is non-zero for a run with any failure |
| Paused | The step-and-wait seam: the loop stopped with an Ask open and answered nothing, waiting for a person or a test to answer it. A paused run is not a failure and is resumed rather than ended |

Stuck is the interesting one. A scheduler or interpreter that cannot make progress is a hang in
production and a failed assertion here.

## The seed is stored, so a run sets it

**Ruled 2026-09-06.** The RNG seed is its own stored value on the management-owned game row —
`rng_seed`, not null, written once when the game is created — and it reaches the engine through
`GameInfo` like everything else. It is not derived from the game id.

An earlier design hashed the game id to get the seed, which made the dice a function of the id and
forced the simulator to derive its ids from its run seed to be reproducible at all. That is
withdrawn: a run sets the seed explicitly and takes ordinary `Guid.CreateVersion7()` ids. In
process the driver hands the engine a `GameInfo` carrying the seed; over the socket it writes the
column, because it makes a row rather than a `GameInfo`.

Reproducibility therefore means: **the same scenario with the same seed gives the same dice and the
same answers, and the ids differ between runs.** Nothing asserts that two runs share an id, and
nothing compares two recordings byte for byte. Undo and replay stay exact, because the value never
changes for a game.

## What it asserts

Beyond a scenario's own expectations, every run checks the invariants that hold for any game:

- Replaying the log from the initial state reproduces the final projection.
- Undoing to an arbitrary sequence and redoing reproduces the same dice, because the seed never
  changes for a game.
- Every Ask is answered by a team that Ask addressed.
- No model is in two places, and no model on the table is outside the slab.
- The round only advances through the boundary Ask, per the ruling of 2026-09-06.

## Testing

`sim/` has xUnit tests of its own: the policy answers every legal-set shape, a script fails on
an unscripted Ask, the two drivers agree, and a recording round-trips. It is then the harness
the interpreter's and scheduler's own full-game tests use. Those tests live in
`tests/Tabletop.Sim.Tests` rather than in `tests/Tabletop.GameServer.Tests`, because `sim/`
references the game server and a test project that `sim/` referenced back would be a cycle.
`sim/` therefore reaches the core `Properties` set through the production path —
`PropertiesSeeder.EmbeddedDocuments()` — rather than through `TestProperties`, and builds its
armies with `ScenarioArmy` rather than `TestArmies`.

`viewer/` has Vitest over its pure parts: the recording reader, the frame-to-geometry
projection, the timeline reducer, and the generated applier against a recording. The rendering
is checked by looking at it.

## Deliberately not decided

- **Settled 2026-09-06 by the simulation plan.** The recording's schema is the record set in
  `sim/Recording/Recording.cs`, and it carries `schemaVersion` — one integer, so a viewer can
  refuse a file it does not understand rather than mis-draw it. It stays derived, so a breaking
  change still costs only a re-run.
- **Settled 2026-09-06 by the simulation plan: the policy prefers to act.**
  `PolicyOptions.PassProbability` defaults to `0`, so a pass is taken only when the legal set
  admits nothing else. It stays a knob rather than a decision, because tuning it toward plausible
  play is a step toward an opponent that plays well and that is a later feature. That it is
  *uniform* over the legal set was ruled on 2026-09-06 — see *Two ways to answer an Ask*.
- **Settled 2026-09-06 by the viewer plan: the viewer does not draw a model inside a container on
  the board at all.** A model whose `where` is `inside` or `shelf` has no position, and inventing
  one — beside the container, or on it — would be the viewer deciding something, which is exactly
  what handing it frames was meant to prevent. Such models are listed off the board with what they
  are inside (`Scene.offTable`, carrying the container's name), and the count is shown. Drawing a
  badge on the container is a later refinement and needs no schema change.
- Whether a scenario is C# or a text format. C# costs nothing to start and keeps the assertions
  typed.

## Still open

- ~~Whether the live feed needs authentication beyond a session JWT for a spectator with no
  team.~~ **Settled 2026-09-06 by the viewer plan: no.** A session JWT is the whole of it, because
  the game server already answers the question — `Connect` carries the JWT, the JWT carries
  `account_id`, and the server matches it against a `participant` row for that game or sends
  `rejected: not_a_participant`. A spectator is a participant with no team, so "may this account
  watch" is already "has this account joined", and the viewer's only auth code is
  `POST /v1/auth/password` and `POST /v1/auth/session/refresh` to obtain the token. Joining is
  `POST /v1/games/{id}/join` on the management API and is not the viewer's.
- ~~Whether a recording should carry the dice so a viewer can show a roll, or only its result.~~
  **Settled 2026-09-06 by the simulation plan: it carries the dice** — `RecordedRoll` holds every
  die's raw and modified face, whether it succeeded, the threshold and the modifiers. The result
  alone would make a viewer that cannot show why a unit died, which is the first thing a person
  debugging the engine wants to see, and the cost is a few hundred integers per game.
- ~~Whether the round cap belongs in the scenario or in `GameRules`.~~ **Settled 2026-09-06 by the
  simulation plan: the scenario.** The cap is not the ruleset's final round, which the enforcement engine reads; the
  cap is a property of a *run*, not of a ruleset, and putting it in `GameRules` would put a testing
  concern inside the rules. `Scenario.RoundCap`, overridable from the console with `--rounds`.
- ~~Whether generating TypeScript types from the C# records is worth a build step, or whether a
  checked-in generated file is enough.~~ **Settled 2026-09-06 by the viewer plan: checked in,
  guarded by a test rather than a build step.** `tests/Tabletop.Viewer.Tests/TypeScriptEmitter.cs`
  reflects over `System.Text.Json`'s own contract metadata — `JsonTypeInfo.Properties[i].Name` for
  the wire name, `JsonPolymorphismOptions.DerivedTypes` for every `kind` string, and a
  serialisation of each enum member for its spelling — and writes
  `viewer/src/lib/protocol/{recording,wire}.generated.ts`. A plain `dotnet test` fails if the
  checked-in files are stale; `TABLETOP_WRITE_GENERATED=1 dotnet test` rewrites them. So drift is
  caught in the check every plan already ends with, and `viewer/`'s own `npm ci && npm run build`
  needs no .NET SDK.
