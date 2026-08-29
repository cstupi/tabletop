# The army

Design session, 2026-08-28. What a user's army is, where its data comes from, and how
appearance gets attached to it.

Fourth module, and the first that touches no game. It follows
[the table domain model](2026-08-27-table-domain-model-design.md), which described what
an army *is*; this covers where it comes from and what a user does to it.

Scope is the army and the assets it points at. Not the management API, not upload
mechanics, not the web app.

## Self-contained, not immutable

**An army holds its own copy of every value.** The list is pulled from Army Forge once and
the values are saved. Nothing is fetched later, and nothing refers back out.

This is the load-bearing property, and it is **independence, not immutability**. The table
model called the army an immutable snapshot and said changing one meant copying it — that
was an overstatement, now corrected there. What actually matters is that no outside thing
can reach into an army and change it: not Army Forge going down, not the source list being
edited, not an asset being deleted.

**The user may edit their own army whenever they like.** It is theirs. This is the same
principle as everything else in the project — the player is in charge, and the system's job
is to make that easy rather than to protect the data from them.

**A game takes what it needs when it loads an army.** That is what keeps a game in progress
safe from an edit, and it needs no versioning, no pinning, and no copy-on-write. Rule packs
are content-addressed and pinned per game because undo-by-replay needs the *exact* ruleset
that produced the log; an army is loaded once at setup, so the same machinery is not
required.

## Where the values come from

Two sources meet in an army, and they are not the same kind of thing.

**The list determines composition and loadout.** Ten models, one sergeant, one flamer,
eight plain. Fully deterministic, imported, and needing no input from the user.

**The user determines everything about appearance and physical presence.** Which sculpt
stands in which slot, and what base each one is on.

The recurring shape, appearing here for the third time in this project: **the pack or the
import supplies a default, the user overrides per slot.** It governs mesh binding, base
shape and height, and the setup configuration in
[game flow](2026-08-28-game-flow-design.md).

### Binding

Per model slot. The default fills every slot in a unit with the same mesh; overriding
individual slots is expected rather than exceptional, because ten Boyz are ten sculpts
someone painted individually and they want that one in that spot.

This settles the binding-granularity question left open by the earlier planning notes —
unit default with per-model override, which the table model had already described without
naming it as the resolution.

### Base shape and height

**Set by the user, per slot, defaulting to whatever the ruleset says.** OPR's defaults fill
them in; anyone who bases their models differently changes them.

The list does not carry this. Nothing in an Army Forge export says a Space Marine is on a
32mm base, so it cannot be imported, and it is too load-bearing to guess from the mesh.

## Assets are a soft reference

All geometry runs on base and height. The visual mesh is never tested for anything — the
table model settled that, so a banner pole cannot block a shot.

**The army holds the hitbox. The asset is only ever appearance.** That makes the reference
to an asset soft: if a user deletes a mesh an old army points at, or replaces it with a
different sculpt, the army stays playable and only looks different.

So **deleting an asset is not blocked** and does not need to be. There is no referential
integrity problem to solve, because the reference was never carrying anything the rules
depend on.

## Editing against a new list

When a list changes in Army Forge, the user **copies an existing army into the editor** and
applies the new list to the copy. Bindings and bases survive because it is literally the
same army being edited, rather than a fresh import that has to reconstruct a match.

Freezing is not a concept here; there is nothing to freeze. The editor works on the army,
and saving keeps it.

## Deliberately not decided

- **How a copy matches old slots to a changed list** — by entry id, by name, by position.
  This is a heuristic in the copy operation and changes nothing about what is stored, so
  choosing now would be premature.
- Import mechanics: how a list is fetched, authenticated, or paged.
- The asset library: how assets are organised, found, or uploaded.
- Whether armies can be shared between users.

## Still open

- What a slot shows when its asset is missing — a placeholder mesh, the base alone, or
  something else. Only a presentation question, but it has to have an answer.
