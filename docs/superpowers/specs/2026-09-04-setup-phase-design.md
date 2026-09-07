# The setup phase

Design session, 2026-09-04. Who places what, and when, between the lobby and the first turn.

[Setup content](2026-08-29-setup-content-design.md) designed the record: a table, terrain
placements, markers and deployment zones, snapshotted once when the phase ends and never
logged. It excluded the phase itself — *"not the setup UI, not the editor, not endpoints."*
[The scheduler](2026-09-04-scheduler-design.md) starts at deployment and handed this session
one question: zones are assigned to team ordinals during setup, but the rulebook has the
deployment roll-off winner pick an edge. This session designs the phase, and settles a
contradiction between two earlier documents that decides who is in the room while it runs.

## Read, not reasoned

- **Grimdark Future v3.4.1**, page 6 (objective markers, the deployment roll-off), page 16
  (the six terrain placement styles), pages 17–18 (the four terrain generators), page 19 (army
  terrain), page 49 (free-for-all deployment).
- **Warhammer 40,000 10th edition core rules** (Wahapedia). The core rules defer terrain and
  deployment to the mission: *"the mission you have selected will guide you"*, and *"unless the
  mission you are playing instructs you otherwise, you should feel free to create an exciting
  battlefield using any terrain features from your collection."* The mission packs were not
  readable and were not read.

Every OPR placement style on page 16 is a roll-off followed by alternating one piece each
under a geometric restriction, except *Full Table*, where the winner places everything and the
loser picks a side. Page 6 has objectives placed the same way: D3+2 markers, roll off, alternate,
outside the zones and over 9" apart.

## Joining is allowed at any status; taking a team is not

[Accounts](2026-08-29-accounts-design.md) settled on 08-30 that a game is joinable at every
status, since joining is a `participant` row plus a socket. [The scheduler](2026-09-04-scheduler-design.md)
said yesterday that team membership is fixed once playing. Both could not stand, and **accounts'
reading holds** for joining a game. Anyone may join at any time. What is fixed once playing is
the scheduler shape, **the team list** and **which armies are in play**: a participant who joins
after deployment brings no army, because deployment has passed. The scheduler document is
corrected in place.

*Corrected 2026-09-06, ruled by the owner.* This section originally ran two things together, and
the heading said so: joining a game and taking a team are not the same act. **Joining stays open
at every status; teams are created and taken only while `lobby` or `setup`.** The scheduler's
"team membership is fixed once playing" was closer to right than this session allowed — what it
got wrong was joining, not teams. A player who was here before the flip **rejoins the slot on
their own `participant` row**: same team, no re-pick, not a spectator. One who arrives after the
flip has no slot, so they have no team and they watch. Stated in full in
[game flow](2026-08-28-game-flow-design.md), *Teams are frozen at the flip, and a team is a
slot*.

**A participant has no team until they pick one** — ruled 2026-09-04 in
[the whole-design review](2026-09-04-whole-design-review.md). `participant.team_id` is nullable; `join` creates the
row with no team, and the lobby's pick assigns one. Teams are created, and participants moved
between them, by lobby commands over the socket, ~~at any status~~ **while status is `lobby` or
`setup`** (corrected 2026-09-06, ruled by the owner). **A participant with no team is
never addressed by an Ask**, and that is what a spectator is — cleaner than presence's "joins a
third team", which is corrected there. During setup, zones re-derive from the zone style whenever
the team list changes; ~~a team created after `playing` has no zone and nothing to deploy~~ — no
team is created after `playing`, so that case does not arise.

## The lifecycle

Four statuses, already enumerated: `lobby`, `setup`, `playing`, `ended`. Two transitions belong
to **the game's creator**, and both are ordinary socket commands the server accepts only from
the creator's participant:

- **Lobby to setup.** In the lobby everyone present picks a team and an army, and the
  configuration game flow listed — pack, scheduler shape, enforcement, wait-between-steps — set
  on `POST /v1/games` may be changed. The creator starts setup, which creates the game's
  `table_state` draft row. Teams and armies stay editable during setup, since deployment has not
  happened.
- **Setup to playing.** The creator ends setup. Off-slab terrain rows are deleted, each
  participant's `army_snapshot` is written from their army as it stands, the draft becomes the
  snapshot by the status flip, the log opens with it as initial state, and the scheduler begins
  deployment. This is where the shape, **the team list** and the armies freeze (the team list
  added 2026-09-06, ruled by the owner).

A client connecting at `lobby` receives the teams, participants and configuration and no board;
the draft arrives with `setup`.

**`game.created_by`** is added to the game row: an account reference set on `POST /v1/games`.
It is read for these two transitions and nothing else. The creator is not a host — cannot kick,
does not own the log — and after `playing` the field is never read again. `end` and delete stay
unowned, as [the management API](2026-08-29-management-api-design.md) has them. A creator who
disconnects is not gone (disconnecting is not leaving); one who leaves may rejoin to end setup;
one who deletes their account destroys the game with everything else they played in, per
accounts. There is no transfer of the role, because nothing else in the model has a role to
transfer to.

**There is no way back from `playing` to `setup`.** The log has started, and unlocking terrain
is the standing open question about terrain becoming event-driven state. A group that wants a
different board starts a new game, or loads the layout they saved.

## Editing is free for all

Any participant may place, move, remove or override anything, at the same time as anyone else,
like a shared editor. **There is no per-team permission and no turn.** The rulebook's roll-off
and alternation are social: a group that wants page 16's *Alternating (Restricted)* reads the
page and takes turns, the way they pick the style in the first place. Setup is unlogged, so any
turn structure here would have been a second, unlogged copy of the scheduler.

**A setup edit is one of a closed set**, each addressed to the game's draft:

- set the table size or texture
- add, move, scale, remove a terrain placement; override its volumes and declarations; swap
  its mesh
- add, move, remove a marker
- set the zone style; swap two teams' zones
- replace the whole draft (how a layout is loaded; saving one is a management call from the
  client, below)

The server applies them in arrival order. Two people dragging the same piece is settled by
whoever drops last, and each sees the other's drag through presence.

**A third message kind.** Commands are logged. Presence is relayed and discarded. Setup edits
are **applied to the draft, broadcast, and persisted, but never logged.** The line presence drew
between relayed and logged gains a middle term, and it exists only during `setup`: after the
flip the server refuses them. Lobby commands — create a team, pick a team, pick an army, change
the configuration — are the same kind: written to the game's rows, broadcast, never logged, and
refused from `playing` on, which is where the team list freezes (2026-09-06, ruled by the
owner).

**The game server writes management's rows for this**, ruled 09-04 in
[the whole-design review](2026-09-04-whole-design-review.md): `game.status`, `team`, `participant.team_id` and
`army_snapshot`, and the game's `table_state` tree, while status is `lobby` or `setup`. From
`playing` on it is read-only, as the management API always said. **Layouts stay management's**:
the client saves a layout by `POST /v1/layouts` from its own copy of the draft, and loads one by
`GET /v1/layouts/{id}` followed by a replace-draft edit, so no owner-side row is ever written by
the game server.

## The draft is the row

The game's `table_state` row — the one with `game_id` set — is the live draft. The game server
holds it in memory, applies each edit, broadcasts the result to every connected client, and
writes the row. Ending setup flips the status and the row is thereby the snapshot, with no copy
step. A server that dies mid-setup recovers the draft from the row through the one hydrate path
accounts made the only recovery path. Two alternatives were rejected: memory only with one
write at the end, which loses an hour of placement when the task dies; and a client-owned draft
pushed whole, which contradicts free-for-all editing.

**Nothing here is eventually consistent, and nothing waits on the database.**

- **Live drag is presence; the drop is the edit.** The table model ruled that live positions
  are broadcast and never logged, and the log carries settled start and stop positions. Setup
  uses the same shape: a piece being dragged streams as a `held` presence placement, relayed
  through the server with no persistence in the path, and its drop is one edit. Persistence sees
  one write per drop, not one per frame.
- **One game lives on one task.** `architecture.md` puts live games in memory on the game
  server and drains to the log on shutdown. The server's memory is the one authority for a
  game; the row write is durability, done after the broadcast, off the path clients wait on.
  Clients see the server's memory at socket latency, which is how the event log already
  behaves. Scaling is more tasks with more games, never two tasks sharing one game. Moving a
  game between tasks mid-setup is the drain-and-hydrate the log already needs, and the draft
  row is what lets setup survive it.

## Layouts

**Loading a layout replaces the whole draft** — size, texture, terrain, markers, zones. Loading
copies, so the layout row is untouched afterwards and the draft belongs to the game. **Saving
copies the draft into a new row owned by the saver**, with a name. Both are edits in the set, so
the same broadcast tells everyone the board changed.

Setup content left open what happens when a saved layout's board size does not match the table.
**The layout wins**: a layout carries its own size, and loading it sets the table's. There is no
table to mismatch against, because the table is whatever the draft says.

## Zones

Setting a zone style creates its zones and assigns them to team ordinals in order: team one gets
zone one. **Any participant may swap two teams' assignments**, as freely as moving terrain.
That is the whole mechanism, and it is what makes page 6's *"the winner picks an edge"*
possible: the group rolls off by any means they like, and the winner's team is swapped into
the edge it wants. **The app runs no roll-off in setup.** The scheduler's logged roll-off at the
start of deployment decides deployment order only, and yesterday's accepted deviation is
reconciled by the swap rather than left as a gap.

Free-for-all's zones (page 49: a corner area each) are one of the built-in shapes. Which shapes
ship is setup content's inventory question, unchanged.

## Terrain: imported, built-in, generated

**Importing terrain and setting its properties is already designed, end to end.** A terrain
model is uploaded through the web app as an asset of kind `terrain` ([asset
library](2026-08-28-asset-library-design.md)). At upload it gets a collision volume from its
bounding box, so a rock or a wall needs no authoring; the web app's volume editor is for when
that default is wrong ([geometry values](2026-08-29-geometry-values-design.md)). The six
declarations — impassable, blocks line of sight, see in and out, cover, difficult, dangerous —
sit on each volume with free notes for the rest, set on the asset in the web app and
**overridable per placement in setup**, which is one of the edits above. This session adds
nothing to that path.

**Built-in terrain ships with the client**, the way predefined textures and marker visuals
already do: a set of meshes with their volumes and declarations, owned by nobody, in no
library. A placement therefore references **either a built-in name or a library asset,
exactly one** — the pattern `texture_builtin | texture_asset_id` and `visual_builtin |
visual_asset_id` already use. `terrain_placement.asset_id` becomes nullable and
`terrain_placement.builtin` is added beside it, with the same check constraint. Which pieces
ship is the inventory question; it must at least cover every entry the generators name.

**Generators** are the rulebook's four planet tables, pages 17–18: nine fixed pieces plus 4D3
rolled on 2D6, each entry a name, a size class from page 17 (scatter 1–3", large 4–8", up to
12"), a built-in mesh, and the declarations as the text gives them — *Forest: difficult and
cover, see in and out; Large Building: impassable and blocking* — with notes for what the six
cannot say, such as *dangerous when using Rush or Charge*.

- **Running one is a batch of adds.** The client rolls — nothing in setup is logged, so no
  seeded RNG is owed — and sends one add-terrain edit per piece, so everyone sees the list
  arrive. The pieces land **beside the table, off the slab**, and players drag them on. A piece
  still off the slab when setup ends is dropped from the snapshot — the end-setup command deletes
  those rows before flipping the status — which is the rulebook's own line: *"if it's impossible
  to place the terrain piece, simply remove it."*
- **Swapping the mesh keeps the piece.** Anyone may swap a generated piece's built-in mesh for
  an asset from their library, or another built-in. **The placement keeps its volumes and
  declarations**, because the generator's declarations are the point and the mesh is
  appearance — the army's rule that the hitbox is the data and the mesh only decorates it.
  A user who wants the new mesh's own volumes clears the overrides. **For a built-in placement
  the placement's volume rows are the volumes, not overrides**: there is no `asset_volume` row
  to fall through to, so they are always written on add, and "clearing" re-copies the built-in's
  shipped volumes from client content.
- **The tables ship with the client**, beside the zone shapes and textures. The server never
  reads them, so the game server stays ruleset-blind in setup, and 40k simply has none.
- A hand-placed built-in piece is a generated piece without the dice. It is the same add.

## What the rulebook has that stays social

- **Placement styles** (page 16). Not run by the app. A style helper that warns on spacing is a
  later convenience and nothing here obstructs one.
- **Objectives** (page 6): D3+2, alternating, outside the zones, over 9" apart. The app places
  markers and does not count or space them.
- **Generic Planet's dangerous picks** (page 17): after placement, roll off and alternate
  picking D3 pieces each as dangerous. Players toggle the declaration on the pieces they pick.
- **Army terrain** (page 19) is placed *during deployment as if it were a regular unit*. That is
  terrain arriving after the snapshot, so it falls with buildings as targets: recorded, not
  designed, additive when it comes.

## Corrections made in place

- **Scheduler** — *team membership is fixed once playing* becomes *the shape and the armies in
  play are fixed once playing; participants may join at any status.* *Amended 2026-09-06, ruled
  by the owner: the team list is fixed once playing too, so the scheduler's original line was
  only half wrong. What stays open at every status is joining a game, not taking a team, and a
  returning player rejoins their own slot.*
- **Game flow** — *table size, terrain placement, and deployment are phases like any other,
  driven by the same scheduler* is struck to deployment only. Terrain is setup, and setup has no
  scheduler.
- **Setup content** — the layout-size open item closes as *the layout wins*; built-in terrain
  joins built-in textures.
- **Schema** — `game.created_by`; `terrain_placement.builtin` beside a nullable `asset_id`.

## Testing

The draft is a value and the edits are a closed set, so the game server's setup handling is
headless logic: edits in, draft and broadcasts out, row written after. Second candidate for the
test project, after the scheduler.

## Deliberately not decided

- The setup UI and the terrain volume editor — client shell and web app respectively.
- The inventories: which built-in terrain, zone shapes and textures ship.
- Which socket frames carry which message kind — the protocol contract.
- Terrain becoming event-driven state, which army terrain, buildings as targets and unlocking
  terrain mid-game all wait on.

## Still open

Nothing of its own. Closed here: setup content's layout-size question, and the scheduler's
zone-assignment handoff.
