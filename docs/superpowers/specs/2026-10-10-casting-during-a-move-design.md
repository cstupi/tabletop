# Casting during a move

p.13 Caster(X): *"At any point before attacking, spend as many tokens as the spell's value to try casting one or more
spells (only one try per spell)."* The Buff, Mark and Debuff picks have the same timing (*"Once per activation, before
attacking, pick one …"*). A unit may stop part-way through an Advance, Rush or Charge, cast or pick, and then move the rest
of its distance. It may also do this before it moves at all.

## What the engine does now

`GfMoves.MoveStep` (`opr-sequences.md` → MoveStep) runs Advance, Rush and Charge, one segment per committed `MoveRegion`
answer:

1. Phase 0 asks for a segment and applies it. Phase 1 runs dangerous terrain.
2. Phase 2 opens the before-attacking window (`Windows.Run`, offers from `GfHooks.ReactionsAt`): Cast a spell, the unit's
   Buff/Mark/Debuff picks, and its live move rows.
3. If a reaction ran and distance remains, the step goes back to phase 0 with what is left (`distance_used_tenths`, the
   difficult cap carried across segments). If the player passes, or nothing is offered, the step ends.

The `GfMovesTests` cases under *"A move a cast splits"* cover the second segment. Hold casts from `CastingWindowStep`.

This leaves four gaps:

- **The client has no way to stop.** Its only control is **Commit move (Enter)**. A short commit opens the window only when
  something is on offer, and a pass then ends the move. Nothing tells the player that a commit is also the moment to cast.
- **Passing a stop ends the move.** The server can't tell "this is my whole move" from "stop here so I can cast".
  Changing your mind at the window costs the rest of the move.
- **Casting before moving.** This needs a zero-distance commit or the move Ask's **Pass**, and both read as "don't
  move". An Aircraft can do neither, because its move is mandatory and at least 30" (`MinimumBreach`), so it can only cast
  after moving.
- **The window's title** is `React at before-attacking?`, an internal point name.

## Design

### Wire

- `MoveRegion` gains `bool Stoppable`. It is true when the before-attacking window would offer the moving unit's team
  something at the moment of the Ask (`GfHooks.ReactionsAt` non-empty for this frame) and the move may take more than one
  segment (not an Aircraft).
- `Destinations` gains `bool Continues`, default false. True means "a stop: the move goes on after the window".

### Server: `MoveStep`

- **Phase 2, after the window.** Return to phase 0 when distance remains and either a reaction ran (today's rule) or the
  segment's answer had `Continues`. Otherwise the step ends as now. A plain commit therefore still ends the move after the
  window, and a stop followed by a pass resumes it.
- **A stop the window has nothing for.** Offers are judged again after the segment. If they are now empty, a
  `Continues` answer returns to phase 0 with no window.
- **A zero-distance stop.** A `Continues` answer whose models stay where they are is the "before moving" stop. It is not a
  move: it sets nothing `moved`, uses no distance, and starts no dangerous terrain test. The window then opens as usual.
- **Aircraft.** The step opens the before-attacking window once before phase 0 (a cast or pick there repeats it, and a
  pass goes on to the move). The window after its single segment stays as it is. That covers both sides of the only move
  p.13 gives it: *"moving in a straight line"*.
- **Charge.** A stop is allowed on a charge. p.9's *"move by up to 12" to get into base contact … or as close as
  possible"* is about where the whole move ends.
- `Windows.Run`'s prompt for `before-attacking` becomes **Before attacking**. Other points keep theirs.

### Client: move box (in-game UX spec → Decision box, `move`)

- With `Stoppable`, the box shows **Stop here (C)** next to **Commit move (Enter)**. It sends the current ghosts as
  `Destinations` with `Continues: true`. With no drag that is the zero-distance stop, which is how a unit casts before
  moving.
- The hint line adds `C: stop here to cast or pick` when `Stoppable`.
- Commit, Esc and the move-range circle are unchanged. After a stop the next move Ask's region is the remaining distance,
  as the client-shell spec already draws it.
- **C** joins the hotkey table: *stop the move here (cast or pick), when the move box offers it*.

## Tests

Server (`GfMovesTests`, alongside the split-move cases):

- A stop, then a pass, gives a second segment with the remaining distance.
- A zero-distance stop, then a cast, gives a full-distance segment, and the unit is not `moved` until it moves.
- A stop whose window turns out empty returns to the move with no window.
- A plain commit followed by a pass ends the step (today's behaviour, pinned).
- An Aircraft is asked at the window before its move, then moves 30"+ in one segment, then gets the window again.
- A Charge split by a cast keeps its 12" allowance across both segments.
- `Stoppable` is false for a non-caster with no picks or move rows, for a Shaken unit, and for an Aircraft.

Client core (`tests/Tabletop.Client.Tests`): `DecisionView` hint and buttons for a `Stoppable` move Ask, and the answer the
stop sends.

Rules suite (`sim/Suite/SuiteCatalog.Special.Casting.cs`, `special-cast-mid-move`, both drivers): a Slow caster unit Advances
and a Fast one Rushes. Each activation's first move Ask is a stop before moving, answered at the window with a cast. The next
is a 1" stop, answered with a pass. Checks: the stop before moving opens *Before attacking*, moves nothing and keeps the whole
allowance. A cast runs between a stop and the rest of the move. After the 1" stop and a pass, the next move Ask holds what is
left.
