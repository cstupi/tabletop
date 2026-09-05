# Game flow

Design session, 2026-08-28. How a game proceeds, who acts, and what a game records
about its own progress.

Sequel to [the table domain model](2026-08-27-table-domain-model-design.md). That
document designed the nouns — what stands on the table. This one designs the verbs.

Scope is the flow spine and the state it touches: participants, what a unit records
about what it has done, where models are when they are not on the table, and how the
game tracks its position. Not resolution — no dice, no wounds, no modifier stacking.

The framing that governs everything below: **this is a tabletop simulator.** The
players are in charge. Rules and automation exist to save them work, never to stop
them.

## Participants

**A player owns armies.** An army stays attached to the player who brought it, which
is what makes provenance work: two players on a side each brought their own list, and
the record keeps them apart.

**A team groups players.** The scheduler only ever sees teams. Solo play is a team of
one, so nothing in the flow branches on player count.

**Activation authority is team-wide.** Either player on a side may activate anything
on that side. Teams share a single activation and function as one force; coordination
inside a team is social, not enforced.

Ownership and authority are therefore different axes. An army belongs to a player.
Acting with it belongs to the team.

## What a unit in play records

Three stores, kept separate because they have three different lifetimes. *A fourth, **grants**,
was added 2026-09-04 by [spell effects](2026-09-04-spell-effects-design.md): effects a spell
granted with a duration, consumed when they apply. Conditions stay names.*

**Action record** — the named actions this unit has taken this round: moved, shot,
charged. Cleared at the boundary the scheduler shape defines — the round for alternating
activation, the team's turn end for player-turn (below).

**Conditions** — shaken, fatigued, and anything else the unit currently is. Persist
until something removes them.

**Placement** — on the table, or not.

Shaken and having-moved are not the same kind of fact, and collapsing them into one
store would mean the reset rule had to know which keys survive a round. Keeping them
apart makes the lifetime a property of the store rather than of each key.

**Activation is not a data concept.** It is a ruleset's reading of the action record.
OPR activates a unit and it does everything; Warhammer moves the whole army and then
shoots the whole army. Both are the same recorded facts, read differently — one
ruleset treats any entry as spent, the other checks entries independently. Nothing in
the data layer has to know which game is being played.

Actions are named, like stats. A ruleset with an action nobody anticipated needs
content, not a code change.

## The shelf

The graveyard generalises. There is **one off-table place per player**, and models sit
on it whether they have not been deployed yet, have been killed, or are waiting in
reserve.

Why a model is on the shelf is a **condition**, not a property of the shelf. A model
waiting to deep strike carries a condition saying it can still arrive; a casualty does
not.

This collapses three mechanisms into one. Deployment, arriving from reserve, and a
mechanic returning a casualty are all the same operation: take a model off the shelf
and give it a placement. The reverse is equally uniform.

A model is therefore always in exactly one place, and moving between them is a single kind of
event.

## Inside a container

**Added 2026-08-30, from the Grimdark Future rulebook.** This session said a model is in one of
**two** places, table or shelf. It is three: a model may be **inside a container**.

A container is **a model or a piece of terrain** — `Transport(X)` holds models inside a
model, and a solid building holds them inside terrain. The two rules are near enough the same
text to be one concept: enter or exit with any move action, never both in one activation, exit
within 6", and when the container is destroyed the occupants take a dangerous terrain test, are
Shaken, and are placed within 6".

**It is a location, not a condition.** Conditions are named states a unit *is*; this one has to
name *what it is inside*, and it decides where the model is — an occupant has no table position
of its own, moves when the container moves, and disembarks relative to it. Making it a
condition would put a reference in a store built for names.

**It is not the shelf**, even though an occupant has no placement. The shelf is off-table and
its occupants are out of play; a transported model is very much in play, and the shelf has no
link to a container that could kill or displace it.

Several things fall out rather than needing rules. An occupant has no position, so it has no
line of sight in or out — which is what the rulebook says. The container moves and nothing
about its occupants updates. Disembarking is the operation that already exists: give a model a
placement.

**Capacity is not enforced.** `Transport(X)` carries its number as a property parameter because
every property may, but nothing checks it, and a building's capacity — which the rulebook
leaves to the players, *"you have to decide how many models can be inside by yourself"* — is not
recorded at all. This is the same position as scoring: players count their own points, and
nothing recorded here is an invariant.

**Buildings can hold models but cannot yet be shot.** The rulebook gives a solid building
`Defense 2+` and a `Tough(X)` the players set, and lets it be targeted and destroyed. That needs
terrain to carry stats and to become event-driven state on top of the setup snapshot, which
[setup content](2026-08-29-setup-content-design.md) says would be additive. It is deliberately
not done here.

## Flow

The game records **round number, current phase, and active team.** That is its
position, and it is all the flow state there is.

**Rounds advance automatically** when no unspent units remain. The player does not
have to declare it.

**Phase structure stays ruleset C#**, per `docs/architecture.md` — pipeline step order,
scheduler policy, and dice conventions are the three things that stay code until a
second ruleset exists to generalise against. The game records where it is; the ruleset
decides what comes next.

### Two schedulers

Nearly every tabletop wargame is one of two shapes, so both get built rather than one
generic scheduler parameterised into knots.

- **Player-turn** (Warhammer style) — the active team does everything with its whole
  force, then passes.
- **Alternating activation** (OPR style) — teams take turns activating one unit at a
  time.

Two concrete implementations behind one interface is the second real example that
`docs/architecture.md` says generalisation should wait for. Having both from the start
is what keeps the interface honest.

**Each scheduler owns when the action record clears.** A player-turn game clears a
team's records when its turn ends; an alternating-activation game clears at the end of
the round. There is no global round boundary the two have to agree on.

Not built, but worth not designing out: **reactive systems** — Infinity-style overwatch
and interrupts — are a third shape, where a window opens for the team that is not
active. The interface should not assume the next window belongs to the active team.

~~Setup is not special. Table size, terrain placement, and deployment are phases like
any other, driven by the same scheduler.~~ **Corrected 2026-09-04.** Deployment is a phase the
scheduler drives ([the scheduler](2026-09-04-scheduler-design.md)). Table size and terrain are
the setup phase, which is unlogged, free for all, and has no scheduler
([the setup phase](2026-09-04-setup-phase-design.md)).

## Setup is configuration, before the game loads

Everything above describes a game in progress. What decides its shape is chosen ~~before
players load into it~~ **in the lobby, before setup** — corrected 2026-09-04: clients connect at
`lobby` ([accounts](2026-08-29-accounts-design.md)), pack, shape and enforcement are set on
`POST /v1/games` and may be changed in the lobby, and teams and armies stay editable through
setup ([the setup phase](2026-09-04-setup-phase-design.md)). As a configuration on the game
record:

- **The rule pack** — content-addressed and pinned, per `docs/architecture.md`, because
  undo-by-replay needs the exact ruleset that produced the log.
- **The scheduler shape** — player-turn or alternating activation.
- ~~**Table dimensions**, and the regions defined on it.~~ These are the setup phase's, not
  configuration ([setup content](2026-08-29-setup-content-design.md)).
- **Teams**: who is on which side, and which army each player brings.
- **The enforcement setting** — `silent | warn | strict`, per
  [identity and enumerations](2026-08-29-identity-and-enumerations-design.md) as reopened on
  09-04.

The pack supplies defaults for these; setup is where they are confirmed or changed.
Consistent with players being in charge, a group that wants alternating activation in a
game whose pack defaults to player turns can simply choose it.

This is configuration, not play. It is not the setup phase — table size, terrain, markers,
zones — which is unlogged free-for-all editing, nor deployment, which is the first thing the
scheduler drives and the first thing logged. *Corrected 2026-09-04: this paragraph said the
setup phases "happen inside a loaded game as ordinary commands"; setup edits are not commands.*

## Scoring

**Players count their own points.** Each team gets one counter it can increment and
decrement.

There are no victory conditions in the model, no scoring triggers, and no mission
logic evaluating the board. Scoring is the part of a wargame players are most reliably
able to do themselves and least willing to have argued with, and automating it would
mean encoding every mission in every ruleset before anyone can play a game.

## Nothing is an invariant

Automation is a convenience layer. It follows that **no recorded state can be treated
as a guarantee.**

The model has to hold configurations the rules call impossible: a unit that has already
moved moving again, a dead model standing on the table, a condition cleared for no
reason the ruleset recognises. Code that reads this state may not assume the rules were
followed to produce it.

**Every property is addressable and editable at any time**, through a menu, as an
ordinary command. Marking a unit activated, clearing shaken, putting a casualty back on
the table — all ordinary edits, logged with an actor like anything else.

Automation and the menu write **the same fields through the same path**. There is no
separate override mechanism and no second write path to keep consistent. What the
engine sets, a player can set.

This is the same instinct as the table model's "anyone can move anything, and the record
says who." Accountability comes from a visible log, not from a gate in the way of play.

## Undo needs no checkpoints

Already settled by the table model, and worth restating because it is easy to
reinvent: **live movement is broadcast but never logged.** The log carries debounced
start and stop positions.

Every logged action therefore ends at a settled board state. Undo is stepping back to
the previous one. There is no mid-drag sequence number to land on, so there is nothing
for a checkpoint concept to protect against.

## Deliberately not decided

- How any of this is expressed in C#.
- Anything about resolution: dice, wounds, modifier stacking, pipeline internals.
- ~~How the scheduler decides what is legal in a window, beyond that it reads the action
  record.~~ **Settled 2026-09-04** by [the scheduler](2026-09-04-scheduler-design.md): the turn is
  an Ask whose legal set is the activatable units paired with their actions, and out-of-set
  answers are what the enforcement dial governs.
- Persistence layout.

## Still open

- ~~Game lifecycle.~~ **Settled.** `GAME.status` is `lobby | setup | playing | ended`
  ([identity and enumerations](2026-08-29-identity-and-enumerations-design.md)), and `ended` is
  reversible rather than terminal, because restarting an ended game is the same hydrate-from-log
  as recovering a crashed task. There is no `abandoned`. Disconnecting is answered too:
  [accounts](2026-08-29-accounts-design.md) says **disconnecting is not leaving** — the
  `participant` row stays and reconnecting rehydrates.
- ~~Whether any setup configuration can be changed once a game is loaded. Pinning the
  rule pack says no for that one; the rest is undecided.~~ **Settled 2026-09-04** by
  [the scheduler](2026-09-04-scheduler-design.md): nothing changes once playing. The pack is
  pinned, the board is the setup snapshot, and the scheduler shape and the armies in play are
  fixed once playing (first written as "team membership", corrected the same day by
  [the setup phase](2026-09-04-setup-phase-design.md): joining stays open at every status).
