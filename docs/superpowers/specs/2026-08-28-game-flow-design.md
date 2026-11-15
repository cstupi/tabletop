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

**Turns and units are the team's.** Rulebook p.50 Team Games: "the turn order is on a team basis
and not on a player basis, so for each team's turn only one of the players may activate a single
unit", and "players from the same team must deploy their units in the same deployment zone, placing
a single unit when it's their team's turn to deploy". Owner ruling: "Either player from the team can
take the activation across any unit in their team." The scheduler alternates teams exactly as with
one player a side. On a team's turn any of its players takes the activation (or the deployment) with
any unit of the team, whichever player's army it came from, and any of them answers every later Ask
of that activation (targets, moves, casting, a reaction's pick) and every Ask to the team as defender
(casualties, strike back, the close-in); whoever starts an activation does not lock teammates out of
it. Scoring, objectives and the game's end stay per team.

- A seat may bring its own army, so a team may hold several (p.50: "you must split the amount of
  points that each team is allowed to take between its players"), and a seat may bring none: owner
  ruling, "You need an army on your team." In the lobby each player picks "Your army"; the team card
  lists each member with their army.
- An Ask names teams only. The game server refuses `NotPermitted` an answer from a seat on a team
  the Ask does not address and a second answer from the same team; any seat of an addressed team
  may answer, and any of them places the team's deploy pick.
- The client offers every player of the team every unit of the team, and shows the team's Asks as
  each player's move.
- Menu edits (the override path) stay open to every player of the game, as before.

Joining a team that already has a player is the creator's to do: the creator moves a participant
onto a held team in the lobby's team columns (spec `2026-11-07-lobby-and-setup-redesign-design.md`), and
everyone else sees that team's Join disabled with the reason.
One person may also hold several teams, an army each (*One account, several teams*, below).

### Teams are frozen at the flip, and a team is a slot

**Added 2026-09-06, ruled by the owner.** This session said a team groups players and never said
when the grouping stops changing, so four later documents each guessed and two of them
contradicted each other. The model, stated here once because this is where teams are designed:

- **Teams are created only while the game is `lobby` or `setup`.** At the flip to `playing` the
  team list is frozen along with the scheduler shape, the armies and the layout. No team is
  created, renamed or removed after that.
- **A participant's team is their slot**, recorded on their `participant` row. It is chosen
  before play and does not change during it.
- **Reconnecting returns a player to their own slot.** They are not a spectator, they do not
  re-pick, and nothing about their team is re-derived. This is the ordinary case, and it is why
  hydration reads the participant rows.
- **Someone who never joined before play began has no slot**, so they have no team, are never
  addressed by an Ask, and watch. That is the whole definition of a spectator, and it is
  unchanged.

Joining a game and taking a team are two different things, and running them together is what
generated the contradiction. **Joining stays open at every status**
([accounts](2026-08-29-accounts-design.md)); **taking a team does not**, with one exception: a
team no one holds may be taken while deployment waits for players (below). Every other document
points at this statement rather than restating it.

### Waiting for players

Ruled by the owner 2026-10-17. **Ending setup always opens the game** (`playing`, the board
snapshotted, the log opened). **Deployment, and the roll-off before it, waits while any team has no
participant, or no army with a unit in play** (`WaitingForPlayers`, `libs/Tabletop.Protocol`; owner
ruling: "You need an army on your team."). A team with a participant but no army waits: deployment
needs units on every side, and a side with none would be skipped by every turn. A seat with no army
on a team that has one is no wait, since any player of a team commands every unit of the team. Each
team it waits on reads "Waiting for a player on Team 2" or "Team 2 has no army", to every client, in
the header, the decision box and the side panel, and any player of a team with no army is offered
the army picker.

The wait holds only while the log is empty, so nothing has been rolled, deployed or edited. Two lobby
edits stay open in it, and everything else is frozen (commands and every other edit are refused
`WrongStatus`):

- **`PickTeam` onto a team no participant holds.** Any participant takes it for itself (a
  participant row with no team takes it; an account that already plays a team gets one more row),
  or the creator places a participant with no team on it. A team someone holds stays held, and
  nobody leaves a team.
- **`PickArmy` for a seat of mine with no army on a team that waits** (a team with no army). The army is checked as at the end of setup,
  snapshotted, written to the participant row (`army_snapshot`) and added to the board the empty
  log opens on, so hydration reads the same board. The army is final once it joins.

When no team waits, the engine runs: the roll-off opens and play goes on as after any end of setup.
A teamless participant is not a watcher during the wait, since it may still take the empty team.

### One account, several teams

Ruled by the owner 2026-10-17: one person may play both sides (self-play), from one client or from
two windows. **A participant row is a seat: one team and one army.** An account holds one row per
team it plays, and at most one row with no team (`unique nulls not distinct (game_id, account_id,
team_id)`). Units, armies and turns keep their participant, so nothing in the rules engine changes.

- `PickTeam(team)` adds a team to the sender's teams: its teamless row takes it, else a new row
  does. `LeaveTeam(team)` gives one up; the account's oldest row, which its sockets carry, is never
  deleted while it keeps another (it takes the next row's team and army instead).
  `PickTeam(null)` gives up every team. The empty-team and creator rules apply per team.
- `PickArmy(army, team)` names the team the army goes to.
- A command acts as one of the account's seats: `AnswerAsk(ask, answer, team)` for the team it
  names, or, unnamed, the first of the account's teams the Ask addresses that has not answered; a
  hand move for the team whose model it moves; anything else for the account's first team. The log
  records that seat as the actor.
- A client holding several teams sees each team's sealed answers (it is the same person), and is
  asked a sealed Ask once per team, in turn. A participant with at least one team is not a watcher.
- Two sockets of one account both play; whoever answers an Ask first answers it. The account's
  socket cap (4) closes the oldest.
- The client acts for the team of the first Ask waiting on one of its teams (`Seats.Acting`): the
  decision box and the game-over modal say "Playing as Team 2", and the roster, unit card and
  camera follow that team; the camera goes to the team's side only between activations, never on the Asks inside one.

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

The rulebook says one more thing than that: units inside **can** target other units inside, and
the container itself (p.15). Having no position gives the first half for free and gets the
second half wrong, so containment is asked before any measurement, and it answers three ways.
No, for one unit inside and one out. Yes with nothing to measure, for two units in one container
or an occupant and its container. Measure it, for two units on the table. The container-itself
branch is unreachable in an ordinary game, since a unit cannot enter an enemy transport (ruled
2026-09-06) and valid targets are enemies; only a menu-written state reaches it.

What does not fall out is **where** a placement is legal, and which containers a move may enter.
The wire carries the destination, `Inside`, in a `Destinations` answer, but not the offer, so the
move Ask carries it. `move_region.containment` names either the containers a model of the unit
could reach, one reaching model embarking the whole unit, or the container it is inside together
with the 6" its models must be placed fully within of. Enterable and exiting are never both set,
which is where "never both in one activation" is enforced.

**Transport capacity is p.15's spaces.** `Transport(X)` carries its number as a property parameter; a unit put in a
Transport before deployment (`2026-10-16-passengers-design.md`) and a unit embarking in play (a move offers only a
Transport with room for its living models) are held to p.15's spaces. A building's capacity — which the rulebook
leaves to the players, *"you have to decide how many models can be inside by yourself"* — is not
recorded at all. This is the same position as scoring: players count their own points, and
nothing recorded here is an invariant.

Two readings of a unit's models follow from this being a third place rather than the shelf, and
both are needed. The models with a **position** answer every distance, sight line, cover and
blocker question. The models still **in play** answer how many the unit has, who may be assigned
a wound, whether it is destroyed, and whether it is at half or less. A transported model is in
the second and not the first.

**Buildings can hold models but cannot yet be shot.** The rulebook gives a solid building
`Defense 2+` and a `Tough(X)` the players set, and lets it be targeted and destroyed. That needs
terrain to carry stats and to become event-driven state on top of the setup snapshot, which
[setup content](2026-08-29-setup-content-design.md) says would be additive. It is deliberately
not done here.

## Flow

The game records **round number, current phase, and active team.** That is its
position, and it is all the flow state there is.

**A player ends the round.** When no unspent units remain, every team having activated all it
can, passed, or having nothing left to activate, the scheduler stops at the round boundary and
asks every team that has a participant whether to end it. The next round starts on the first
answer. The player does have to declare it, and that is deliberate: ruled by the owner
2026-09-06 to avoid an accidental auto-advance. Which teams may answer is not a turn-order
question, and any player on any team may end the round. See
[the scheduler](2026-09-04-scheduler-design.md) → *The final round is the ruleset's, and its end follows the enforcement setting*.

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
- **Teams**: who is on which side, and which army each player brings. Set in the lobby and
  editable through setup; frozen at the flip to `playing` (ruled by the owner 2026-09-06, and
  stated above under *Teams are frozen at the flip, and a team is a slot*).
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

**The engine scores the core mission; players keep their own counters.** At the end of each round a unit within 3" of an objective marker while no enemy is seizes it; control persists when the unit leaves; units of more than one team make it neutral; Shaken units, Aircraft and units that arrived by Ambush that round neither seize nor contest (GF p.6, p.10, p.13). After round 4 the team controlling most markers wins: under `strict` `game_ended` names it, or the tied teams; under `warn` the warning names the standing ([objectives](2026-10-02-objectives-design.md)). Each team still has one counter it can increment and decrement, and nothing the engine scores writes it. Missions beyond the core one are not modelled.

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
engine sets, a player can set. A `strict` end is no exception: undo takes it back and the menu's set round plays on.

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
  **The team list is fixed once playing too** — ruled by the owner 2026-09-06, after the same
  day's correction had been read as leaving team creation open. What stays open at every status
  is joining a game, not taking a team; see *Teams are frozen at the flip, and a team is a slot*
  above.
