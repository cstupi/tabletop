# The asset library

Design session, 2026-08-28. What a user's meshes are, how they are organised, and what
the rest of the system is allowed to assume about them.

Fifth module. It follows [the army](2026-08-28-army-design.md), which established that
an asset is a soft reference the rules never touch.

`docs/architecture.md` already settled the parts this does not revisit: glTF and GLB only
with STL rejected, conversion as an offline tool rather than a server-side job queue,
content addressing in S3, loading at runtime through `GLTFDocument` rather than Godot's
editor import, `kind` making the library generic so terrain uses the same upload, and a
missing mesh degrading to a proxy shape with the game fully playable.

Three things in it were wrong and are corrected here and there.

## A mesh carries no game semantics

Restating the one rule everything else depends on: an asset does not know what it
represents, what stats it has, or what army it is in. It is a file a user points at
things.

Nothing validates that a mesh "matches" what it stands for. Proxying a model for a
different race entirely is ordinary use, not an error case.

## Two records

```
Blob    hash, bytes in S3                       the file
Asset   owner, blob, name, kind                 the library item
        + scale, orientation, modelled base     facts about the sculpt
```

**Dedup is per user.** `architecture.md` deduped blobs across all users; that is
corrected. Nobody shares storage with anybody else, so deleting an asset never has to
check whether another account still points at the bytes.

**Sharing, when it arrives, copies.** A model shared with another user lands in their
library as their own asset over their own blob. This is the same principle as the army
holding its own copy of every value — the recipient owns what they received, and nothing
reaches back into the sender's library.

## Binding is not a third record

`architecture.md` had a third row, `Binding {army_id, target_ref, asset_id}`, existing so
that bindings would survive a list being re-imported.

**That reason is gone.** [The army](2026-08-28-army-design.md) updates a list by copying
the army into the editor and applying the new list to the copy, so bindings survive by
construction rather than by keying on a stable reference.

So the asset reference is a field on the model slot, and the library has two records
rather than three. The same document also printed a three-row table under the sentence
"two layers, not three," which is where the confusion started.

## The modelled base suggests; the slot's base is the hitbox

An asset records **the base the sculpt was built on**. A slot records **the base used in
play**, set by the user and defaulting to the ruleset's value.

`architecture.md` had one base size, on the asset, winning for geometry, with any mismatch
against the list surfaced as a warning. Corrected: they are two different facts with two
different jobs.

- The **slot's** base is what geometry runs on. Distance and line of sight use it, and it
  is what the ruleset supplies a default for.
- The **asset's** modelled base is a property of the file, and its job is **finding
  meshes**: given a slot on a 32mm base, the library can offer the assets built for one.

This turns what was an error to warn about into a way to search. A user whose orks are all
based differently to the sculpt is doing something normal, and the system has no reason to
comment on it.

## Folders

Users organise their own library. Folders **nest**, and **an asset can be in several of
them** — an ork sculpt belongs in the orks folder and the winter-themed folder at once.

They are logical groupings, not a filesystem. Nothing derives from where an asset sits,
nothing else in the system reads them, and an asset in no folder is fine.

This is the answer to a user with five hundred meshes finding one, together with the
modelled base as a filter.

## Deliberately not decided

- Upload mechanics: how bytes arrive, how progress is reported, how failures retry.
- Whether folders are shared or copied when a model is shared.
- Search beyond folders and modelled base.
- What `scale` and `orientation` are measured against, and whether the mesh prep tool
  writes them or the user does.

## Still open

- The triangle-count threshold at upload. Agreed in principle in `architecture.md` as the
  only guardrail once conversion left the product; the number has never been chosen.
- What a slot draws when its asset is missing — `architecture.md` says a proxy shape from
  the collision volume, but a miniature's proxy would have to come from the base and
  height instead.
