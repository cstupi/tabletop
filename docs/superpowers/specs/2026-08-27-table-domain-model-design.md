# Table domain model

Design session, 2026-08-27. What is on the table, what it is made of, and what the
server records about it.

This is the first module designed in earnest. It supersedes nothing in
`docs/architecture.md`, but it does make that document's talk of `Tabletop.Content`
concrete for the first time, and the workstream prompts in `docs/workstreams/` predate it
— they are sketches, not specs, and none should be run as written.

Scope is deliberately narrow: **the table and the things standing on it.** Not the rules
engine, not resolution, not the management API, not the wire format beyond the one
decision the table forces.

## Shape of the model

Two halves, and the line between them is ownership.

**A user owns armies.** An army is self-contained and belongs to no game. It outlives
every game played with it, and the user may edit it whenever they like.

**A game owns a table.** The table holds things, and those things point back at the army
it loaded for anything that does not change.

### Owned by a user, persisted

**Asset** — an uploaded mesh, glTF only. Geometry and nothing else.

**Army** — a name, a game system, the source it was imported from, and its units. It
bundles the list *and* the model-to-mesh assignments into one thing that stands alone.

**Self-containment is the load-bearing choice here** — not immutability, which an earlier
draft of this document overstated. An army holds its own copy of every value rather than
referring out to the source it came from. Army Forge going away, a list being edited, or an
asset being deleted cannot reach into it. A game that loads an army takes what it needs at
that moment, so editing the army afterwards does not disturb a game in progress.

The user may edit an army at any time. See
[the army](2026-08-28-army-design.md) for how that works.

**Unit** (inside an army) — a name, the stats and properties that belong to the whole squad,
and an ordered set of model slots.

*Corrected 2026-08-29 by [content values](2026-08-29-content-values-design.md):* this also
listed "the link back to its list entry" and "upgrades". Both are gone — an upgrade's effect is
already applied by import time, and nothing reads a source identifier.

Squad-wide properties live here and are not copied onto each model.

**Model slot** (inside a unit) — what it is (Space Marine), its stats, its loadout, its
base shape and height, and the asset that represents it.

This is where two different sources meet. The **list** determines composition and loadout:
ten models, one sergeant, one flamer, eight plain. That is fully deterministic and needs no
input. The **user** determines appearance, one slot at a time, because the models in a unit
are often made from different meshes. Default is filling every slot with the same mesh;
overriding individual slots is expected, not exceptional.

### Belonging to a game

**Game** — the armies in it, the table, the event log, the current phase.

**Table** — dimensions in inches, and its regions.

**Region** — a shape, whose it is, and what it is for: deployment zones~~, mission areas~~
(*mission areas have nothing behind them; there is no mission record —
[setup content](2026-08-29-setup-content-design.md)*).
Defined at game setup, with a sensible default. Drawn as visible lines during deployment
so players can see where they may deploy, hidden once play moves past that phase.

Regions belong to the table. They are not things standing on it: they have shape but no
mesh, and nothing rests on them.

**Placement** — position and facing. Held by anything currently on the table, and the only
thing the geometry ever needs to be handed.

*Corrected 2026-08-29:* this also listed "what it is resting on."
[Geometry values](2026-08-29-geometry-values-design.md) drops it — settling derives the surface
whenever it is needed, and terrain is locked after setup, so nothing a model stands on ever
moves.

**Unit in play** — points at its army unit, holds its models and whatever unit-level state
changes during a game.

**Model in play** — belongs to a unit, points at its model slot, and carries only what
changes: wounds taken, current condition. It is **either placed on the table or sitting in
a graveyard.**

A model is not defined by being on the table. A placement is something it has right now.

**Graveyard** — one per player, holding models removed from play. Models come back out of
it; some mechanics return casualties, and the model that returns is the same model.

*Superseded:* [game flow](2026-08-28-game-flow-design.md) generalises this to a **shelf**
that also holds undeployed models and reserves, with the difference carried as a
condition.

**Terrain piece** — a placement, its volumes, and what those volumes declare.

**Token** — a placement, a mesh, a label. Placed and moved like anything else, seen by
everyone, meaning whatever the players decide.

**Objective** — a placement, a size, a visual, and whatever the game needs to score it.
~~Its own kind of thing, not a token and not terrain.~~ *Corrected by
[setup content](2026-08-29-setup-content-design.md): objectives and tokens are one marker with a
`kind`, since scoring is manual and the field this line reserved is empty.*

## Decisions and why

**Position is per-model, never per-unit.** Each miniature has its own placement. This is
how the game is physically played: you measure from the closest model, remove casualties
one at a time, and tuck a model behind a wall deliberately. A unit is a grouping, not a
location.

**A miniature is a base and a height.** Distance is edge to edge between base shapes,
never centre to centre. Line of sight is settled against the cylinder that the base and
height describe. The visual mesh is never tested — a banner pole cannot block a shot, and
an outstretched wing cannot be shot at. This is cheap, predictable, and unarguable, and it
means mesh conversion never has to produce collision geometry.

**Terrain declares what it does; the game system interprets it.** A piece carries named
declarations on its volumes — gives cover, slow to cross, blocks line of sight. Terrain does
not know what a forest is. This is what lets a user build a piece nobody anticipated, and lets
a second wargame read the same declarations differently.

*Corrected 2026-08-29:* this passage originally offered "blocks sight past three inches" as an
example declaration. **That is not a rule in either OPR game** — it was invented here, and then
repeated in later sessions as though it were established. See
[geometry values](2026-08-29-geometry-values-design.md) for the six declarations actually read
out of the rulebook.

*Terminology note:* these are tags in the domain sense — names with meaning attached. No
particular language construct is implied.

**Stats belong to what a model is, not to the miniature.** A Space Marine has stats; the
thing on the table is one of those. What a model *is* stays separate from what it
*currently is*, so a buff, a debuff, or a degrading statline never destroys the original
and everything stays recomputable.

**The list is the join, at unit level.** A unit in play points at its army unit; a model in
play points at its model slot. Because the army holds its own resolved values, the slot
already carries stats and loadout — a model in play never copies them and never needs a
second reference back to the list. Casualties do not break the link.

**Nothing arbitrary goes on the table.** Everything present traces to game state — models
deployed from an army, terrain placed during setup, objectives from the mission. But
tokens are first-class placeables, not a special case: real meshes, moved independently,
synced to everyone. They are how players track anything the rules engine does not yet
model, which early on is most things.

**Anyone can move anything, and the record says who.** No permission locks. This matches a
real table, where you routinely shift an opponent's model to reach past it. Accountability
comes from a visible log rather than from a gate in the way of ordinary play.

**Containment is computed, never stored.** Whether a model is standing in a forest is a
question the geometry answers when asked. (*Being inside a container — a transport or a
building — is a different thing with the same word: a stored location since 08-30, see
[game flow](2026-08-28-game-flow-design.md).*) Only position and facing are recorded, and what a
model rests on is derived the same way.

**Having a placement does not imply obeying terrain.** A miniature settles onto terrain and
is affected by it. An objective sits where it is put and ignores terrain entirely — no
settling, no collision, no declarations applied. Participation in terrain varies by kind.

**The table layer is not OPR-specific.** Models, units, wounds, weapons and abilities are
universal across wargames and are modelled as such. What differs between systems is data —
what the stats are called, what a declaration means, how a wound resolves — not the shape
of what a unit is. Stats are therefore a named store rather than typed fields, so a system
with a stat nobody anticipated needs content, not a code change.

## The one wire decision the table forces

Movement is watched live: every player sees the hand moving a model, not just where it
ended up. That is a requirement, and the event log is the wrong place for it — replaying to
a sequence number would land mid-drag.

So there are two channels:

- **Ephemeral live position**, broadcast while dragging, never recorded.
- **Logged move events**, carrying the start and the stop position, debounced so a
  fiddled-with model does not generate forty entries.

Replay snaps to where things stopped. Undo therefore always lands on a clean state.

Start position is recorded as well as stop because undo needs somewhere to go back to.

## Deliberately not decided

- How any of this is expressed in C#. Whether the kinds of thing on the table are distinct
  types or one type carrying different meaning is an implementation question, and it does
  not need answering to agree the model.
- How stats are keyed, validated, or named.
- Persistence layout, tables, indices.
- Anything about resolution: dice, wounds, phases, activations.
- The debounce interval and live-position broadcast rate.

## Still open

- ~~What a returning model's state is when a mechanic brings it back out of the graveyard.~~
  **Settled** by [game flow](2026-08-28-game-flow-design.md), which generalised the graveyard to
  a shelf: **why a model is on the shelf is a condition, not a property of the shelf.** Coming
  off it means being given a placement, and the state it returns in is whatever conditions it
  is carrying. Deployment, arriving from reserve and a mechanic returning a casualty are one
  operation.
- ~~Who places terrain, and when. Terrain setup is a game phase; that phase has not been
  designed.~~ **Settled 2026-09-04** by [the setup phase](2026-09-04-setup-phase-design.md):
  anyone, at any time during setup, free for all; the rulebook's alternation is social.
- ~~Whether an objective's "game data" is generic or system-specific.~~ **Answered by
  deletion.** [Setup content](2026-08-29-setup-content-design.md) collapsed objectives and
  tokens into one `Marker` with a `kind`, because scoring is manual — players count their own
  points — so the field this session described as "whatever the game needs to score it" had
  nothing in it. There is no game data on an objective to be generic or specific.

  If it ever comes back, the stated direction is **generic options that conforming systems
  define themselves against**, rather than a free-form per-system blob. Nothing reads such a
  field today, and that is what would have to change first.
