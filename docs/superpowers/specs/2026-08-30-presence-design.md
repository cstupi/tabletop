# Presence

Design session, 2026-08-30. What players see of each other between events.

Fifteenth module, and the only gap the design sessions **missed** rather than deferred.
Every other module was reached by asking what a noun was; presence was never reached at all,
because nothing persists and so nothing in [`docs/data-model.md`](../../data-model.md) or
[`docs/schema.md`](../../schema.md) pointed at it.

Data only: what values exist and what is on them. Not the relay, not the sample rate, not
the rendering.

## The channel already exists

[The table domain model](2026-08-27-table-domain-model-design.md) settled that movement is
watched live, and that the event log is the wrong place for it — replaying to a sequence
number would land mid-drag. So it defined two channels: ephemeral live positions broadcast
while dragging and never recorded, and logged move events carrying debounced start and stop.

**Presence is that same ephemeral channel with more on it.** No new transport, no new
mechanism, and no second decision about what "live" means.

## Nothing here is written down

Not one value in this document is persisted or logged. No tables, no columns, no event log
entries, no blobs. A presence value exists in memory on the game server while a socket is
open and is gone when it closes. A reconnecting client rebuilds it from nothing, because
there is nothing to rebuild.

The boundary is worth stating precisely, because five of these fields sit next to things
that *are* durable:

| Value | What is logged |
| --- | --- |
| Cursor, ping, measurement, `proposed` placement | Nothing, ever. They end by being discarded. |
| Selection | Nothing. **Selecting a unit is not activating it** — activation is a named action on the unit's action record, and that is a command. |
| `held` placement | Nothing from presence. The move is logged, by the move event the table model already defined. The live positions streaming past are not what gets written. |

Two consequences: account deletion has nothing to reach into here, and a replay viewer will
show games without cursors, pings or measurements. **A replay is where things went, not what
people pointed at.**

## The presence value

One per participant, sampled and broadcast:

```
Presence   participant     id
           cursor          point + target       target is a piece id, or none
           selection       unit id + [model id]     empty list means the whole unit
           placements      [piece id, position, yaw, kind]     kind = held | proposed
           measurement     point, point         or none
```

**There is no spectator, so there is no second identity.** Games are public and anyone
logged in may join, which means someone who wants to watch joins a third team and does not
act — indistinguishable from a spectator without being a second kind of thing. Presence
therefore keys on the participant, and every connection has one.

**Selection is a unit, and optionally models within it.** The unit is the default grain
because that is what moving and rotating operate on; a modifier key narrows the selection to
individual models. A flat list of piece ids could not express the difference between *this unit*
and *these two models of it*, and the two mean different things to everyone watching.

It lines up with `placements` being per model: the selection says which models a drag applies
to, and `placements` then carries one entry for each of them.

**Connectedness is not a field.** A presence value exists while a participant is connected
and stops when they are not, so who is connected is which presence values exist. The
persisted `PARTICIPANT` row is the roster and the presence value is the live overlay on it,
which is what [accounts](2026-08-29-accounts-design.md) already implied in saying
**disconnecting is not leaving**: the seat stays, the presence goes.

**No sequence number.** The log carries `seq` because it is replayed. Presence is never
replayed, arrives in order, and the latest value wins.

## The cursor is where the ray lands

A cursor is a point in the world plus the piece under it, not a point on the table plane.

Pointing at a model standing on a hill is the case pointing exists for, and a table-plane
cursor renders buried inside the hill at exactly that moment. The raycast this costs is one
the client already does — models settle by raycasting down.

The target id is carried rather than re-derived because it is what makes hovering legible:
everyone can see that a player is on *that unit*, not merely near it. Deriving it on the
receiving side would mean every client raycasting every other client's cursor, every sample,
to reach an answer the sender already had.

## Drag and ghost are one value

`architecture.md` listed model dragging and movement ghosts as separate client concerns, and
they are separate *interactions*. They are not separate values.

Both are a piece, a position it is not committed to, and a yaw. The difference is only what
other clients draw, so that is a `kind`:

- **`held`** — the participant has the piece. Others render the piece there.
- **`proposed`** — a preview. Others render a translucent copy and leave the piece where it
  is.

Both end the same way: replaced by a logged move event, or discarded.

This is the same collapse [setup content](2026-08-29-setup-content-design.md) made when
objectives and tokens became one marker with a `kind`, and for the same reason — two records
that differ only in what they mean to the viewer are one record.

**It is a list because placement is per model, never per unit.** Dragging a unit is several
model placements moving at once, which the table model settled and which nothing here
changes.

## A measurement is two points

Distance is edge-to-edge between bases, and the participant measuring has already resolved
that against the two bases. So what goes on the wire is the two points it resolved to. The
receiver draws a line between them and computes the length itself.

No anchor union, no piece references, and no re-resolution on the receiving side against
bases it would have to look up.

## A ping is silent, and is not a field

**Silent**, because nothing in this product carries text between players and a ping does not
change that. A ping is a pointer with no words: **sender, point, target** — the cursor's
shape, stamped.

**Not a field**, because presence is state and a ping is an instant. As a field it would need
a timestamp and a lifetime, and every subsequent sample would re-send a ping already shown.
So it is a one-shot message on the same channel.

A ping is also unambiguously the ephemeral kind of pointing, because the durable kind already
exists: a thing placed on the table and left there is a marker with a `kind`.

## What this corrects in `architecture.md`

**Measuring and ghosts are relayed.** `architecture.md` says pre-measuring, range checks and
movement ghosts are "computed locally, never sent anywhere." Three of this document's fields
override that.

The argument it was making still stands, and is not what changed: measuring and planning are
**never commands**. Nothing waits on them, nothing validates them, nothing logs them, and the
mouse does not go through the server. Relayed and logged are different things, and that
document was written before there was an ephemeral channel to distinguish them.

The reason to relay them is that a tape measure on a real table is visible to everyone at it.
Hidden pre-measuring is a different game from the one being simulated.

**"Presence has never been designed"** is also now false.

## Deliberately not decided

- The sample rate, and whether cursor and placements share one. The table model already
  deferred the live-position broadcast rate and the debounce interval; this adds no new
  answer.
- How a client is told that a presence value has stopped, versus timing it out.
- Ping visuals, cursor colours, and whether a player can mute another's cursors.
- Camera and view direction. Considered and dropped: it is the one field with no physical
  table behind it.
- Whether a participant can have two connections at once, and what presence does if so.

## Still open

- **Whether presence exists before a game does.** The game browser lists open games, and
  nothing yet says whether a lobby shows who is in it. That depends on whether a game
  already playing can be joined, which [accounts](2026-08-29-accounts-design.md) left open.
- ~~Whether selection distinguishes a unit from the models in it.~~ **Settled on 08-30**: it
  does. A unit id with a list of models inside it, empty meaning all of them.
