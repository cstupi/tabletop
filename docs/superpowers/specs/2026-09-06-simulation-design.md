# Simulation and the developer viewer

Design session, 2026-09-06. How a full game is played and watched without the Godot client.

The design phase closed on 2026-09-04 and the eight implementation plans followed. This is a
ninth module, added because the existing test harnesses are per-sequence fixtures: the
interpreter's `Sim` drives one sequence, the scheduler's `SchedulerHarness` drives a few turns.
Nothing plays deployment through to a decided game, and nothing shows what happened except a
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

## Two ways to answer an Ask

Every decision in a game arrives as an Ask carrying a server-computed legal set. A simulator is
therefore a function from an Ask to an answer, and there are two useful ones.

- **A script.** Named answers in order, asserted against the log. This is the regression form:
  a rules interaction is pinned as a scenario and fails loudly when the engine changes under
  it.
- **A seeded policy.** Any Ask, answered from its legal set with a seeded random choice. This is
  the soak form: a game plays to completion unattended, and five hundred games with five
  hundred seeds is a fuzz run.

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

Top-down and schematic on purpose. A grid in inches, a circle per model coloured by team,
terrain as outlines carrying their declarations, deployment zones, and a timeline with play,
step and scrub. The log sits beside the board and clicking a line jumps to that sequence.
Nothing is rendered in 3D and no mesh is ever loaded, so a model is its base and its facing.

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

### Interactive debugging is a door, not a room

Playback is the whole of the first version. The viewer sends no command and answers no Ask.

The design leaves the seam for it: pausing a simulation and answering an open Ask from the
browser is the obvious next thing, so the recording carries the open Ask at every frame, the
driver interface has a step-and-wait mode, and the viewer's feed abstraction has room for a
control channel. None of that is built yet, and building it makes the viewer a second thin
client, which is a larger surface than a renderer.

## What a full game means

A run reaches one of three ends, and a run that reaches none of them is a bug the simulator
exists to find.

| End | Condition |
| --- | --- |
| Decided | One team has models and the others do not |
| Round limit | A configured round cap, since OPR has no round limit and a stalemate must still terminate |
| Stuck | No Ask is open and the engine emits nothing, or an Ask is answered and reopens unchanged |

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
the interpreter's and scheduler's own full-game tests use.

`viewer/` has Vitest over its pure parts: the recording reader, the frame-to-geometry
projection, the timeline reducer, and the generated applier against a recording. The rendering
is checked by looking at it.

## Deliberately not decided

- The recording's exact schema and whether it is versioned. It is derived, so a breaking change
  costs a re-run.
- Whether the policy prefers to act over passing when both are legal. That it is *uniform* over
  the legal set is no longer open: it was ruled on 2026-09-06 — see *Two ways to answer an Ask*.
- How the viewer draws a model inside a container, which has no position.
- Whether a scenario is C# or a text format. C# costs nothing to start and keeps the assertions
  typed.

## Still open

- Whether the live feed needs authentication beyond a session JWT for a spectator with no team.
- Whether a recording should carry the dice so a viewer can show a roll, or only its result.
- Whether the round cap belongs in the scenario or in `GameRules`.
- Whether generating TypeScript types from the C# records is worth a build step, or whether a
  checked-in generated file is enough.
