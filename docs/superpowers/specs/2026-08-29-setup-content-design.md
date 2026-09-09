# The table and its setup content

Design session, 2026-08-29. What the board is, what stands on it before play starts, and
what a saved arrangement is.

Seventh module. It fills the gap [game flow](2026-08-28-game-flow-design.md) left when it
named table, regions and terrain as setup configuration without designing any of them, and
the gap [the asset library](2026-08-28-asset-library-design.md) left when it defined `kind`
so terrain could share the upload path but only ever described the miniature profile.

Data only: what records exist and what is on them. Not the setup UI, not the editor, not
endpoints.

## No `Mission` record

`docs/architecture.md` defined a mission as deployment zones, objectives, victory
conditions, round count and team count. That is not what a mission is. **Missions are
published content in OPR and Warhammer** — named scenarios out of a book, with their own
deployment maps and objective setups.

Game flow had already deleted the victory conditions half of that definition: players count
their own points, nothing evaluates the board.

So the app holds no mission. **Players read their book.** What the app provides is built-in
deployment zone helpers and the ability to place objectives, and nothing that knows which
scenario is being played. Automation and enforcement of published missions are a later
addition and nothing here obstructs one.

## The table is a slab

```
Table   width, length    inches
        texture          a built-in name, or one of the user's own assets
```

**A rectangle.** No shape field and no play-area boundary volume, both of which
`architecture.md` called for. It listed a square four-player table as a distinct shape; a
square is a rectangle.

**Thickness is a render constant, not stored.** A zero-depth plane looks wrong when the
camera drops to table level, so the board is drawn as a slab — but nothing measures against
its thickness and nothing settles on its side. It becomes data only if users are ever given
a choice of board depth.

The texture is flat on the top face. **Predefined textures ship with the client**, so they
are not library assets, nobody owns them, and nothing is copied into anybody's account. A
user who wants their own board art uploads it and it is an ordinary asset in their own
library. *Since 2026-09-04, built-in terrain ships the same way, and a placement names a
built-in or a library asset — see [the setup phase](2026-09-04-setup-phase-design.md).*

**Size cannot change after the setup phase.**

**The built-in deployment styles assume a table near 72×48.** Six of the seven take absolute
insets from Grimdark Future p.22's diagrams, and Side Battle scales with the table. Small tables
are out of scope by the owner's ruling of 2026-09-06. Custom and adjustable shapes, a later
expansion, are what a small table would need.

## Deployment zones

A **built-in shape id, plus the team it belongs to.** Deployment zones are team-assigned
geometry and nothing else.

Custom and adjustable shapes are a later expansion. With missions gone, deployment is the
only thing a region is for — the table model's "or mission areas" has nothing behind it.

## The setup phase is snapshotted, not logged

**The setup phase is not recorded as events.** Terrain and markers are placed, moved, added
and removed freely while it runs, and none of that goes in the log. At the end of the
phase, one record captures the result (*since 09-04 that record exists as a live draft row from
the moment setup starts, written on every edit, and becomes the snapshot by the status flip —
[the setup phase](2026-09-04-setup-phase-design.md)*):

```
width, length, texture
terrain placements
marker placements
deployment zones
```

This supersedes `architecture.md`, which made terrain placement a stream of ordinary
Commands so that a predefined map would be "a batch of placement events" and undo during
placement would fall out of undo-by-replay. Setup does not need replay. It needs a settled
result.

**A Layout is this same record**, owned by a user rather than a game. Saving one keeps it;
loading one seeds a new game's setup phase. There is no separate layout format and no
second persistence path — the thing a game ends setup with and the thing a user saves are
one shape.

Because a layout carries the board it was built on, terrain coordinates always mean
something. What happens when a saved layout meets a different table size is left open.

### The lock is structural

After setup, terrain and objectives cannot be moved — the requirement being that nobody
nudges a ruin or an objective while reaching across the board.

This needs no flag and no check, because **this record is the event log's initial state**.
The log begins the moment setup ends, and nothing in it can touch terrain, because terrain
is not in it.

Adding destructible or moveable terrain later is **additive**: terrain-mutation events sit
on top of the same snapshot exactly as model events do, and replay still starts from it.
Nothing here has to be rewritten to allow that.

## Markers

Objectives and tokens are one record.

```
Marker   kind        objective | token
         placement   position, facing
         size        objective = a large circle; token = a small chip
         visual      a built-in, or a user asset
         label
```

The table model insisted an objective was its own kind of thing, "not a token and not
terrain," because it carried whatever the game needed to score it. Scoring is manual, so
that field is empty and the two records are the same shape.

They still differ in the two ways that matter to a player: an objective is a large
predefined circle placed during setup and locked, a token is a small chip dropped during
play and moved freely. That difference is `kind`, size, and which record it lives in — and
*which record* was left unsaid until 09-04: **objectives are rows in the snapshot; a token
dropped in play is an ordinary logged command** (`marker_placed`, `marker_moved`,
`marker_removed`), rebuilt from the log like every other in-play state. A token placed during
setup may sit in the snapshot as well, as initial state.

`kind` is also the hook automatic scoring hangs off if it is ever built, which is why the
collapse costs nothing.

**Markers ignore terrain** — no settling, no collision — as the table model already
decided.

## Terrain

The asset library's second profile.

**Volumes and their declarations live on the asset**, because they are facts about the
mesh in the same way scale, orientation and the modelled base are. They are authored once
and reused in every game.

**A placement may override them.** The pattern this project keeps arriving at — the asset
or the pack supplies a default, the user overrides in place — and here it is what lets a
group decide this forest is impassable today.

**The copy is made when the placement is added.** A placement's volumes are always written on
add, from the asset's volumes or from a built-in's shipped ones, so the draft and the snapshot
are self-contained and the game server never reads the asset's rows. Editing a placement's
volumes in place is the override, and clearing them re-copies the source's.

Declarations split in two, which is `architecture.md`'s closed-schema-open-catalog instinct
applied to terrain:

- **A closed set the engine reads** — impassable, blocks line of sight, see in and out, cover,
  difficult, dangerous. These are the ones that change how the game resolves, so adding to the
  set is a code change, deliberately. This session named four;
  [geometry values](2026-08-29-geometry-values-design.md) added `impassable` and `see in and
  out` against the rulebook, and six is the current set.
- **Open notes it does not read** — free text a player hovers to see. Terrain health, house
  rules, anything nobody anticipated. Never interpreted, never validated, never acted on.

This keeps the table model's rule intact: terrain declares what it does and the game system
interprets it. Terrain still does not know what a forest is.

## Deliberately not decided

- Custom and adjustable deployment shapes.
- ~~What happens when a saved layout's board size does not match the table it is loaded onto.~~
  **Settled 2026-09-04** by [the setup phase](2026-09-04-setup-phase-design.md): the layout
  wins. Loading it replaces the whole draft, size included.
- How a texture's aspect ratio is reconciled with an arbitrary board size.
- The inventories themselves: ~~which built-in shapes~~ (**settled 2026-09-05** by the geometry
  plan: the seven `ZoneStyles` in `Tabletop.Geometry`), and which built-in textures.
- The setup UI, the terrain volume editor, and how a layout is exported.

## Still open

- **Whether terrain can be unlocked mid-game.** The lock is structural rather than a flag,
  so unlocking is not a permission change — it is the point at which terrain becomes
  event-driven state.
- Whether the closed declaration set is complete. Six are named, after
  [geometry values](2026-08-29-geometry-values-design.md) added two to this session's four;
  a second ruleset is what will test it.
