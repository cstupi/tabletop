# The asset library

Design session, 2026-08-28. What a user's meshes are, how they are organised, and what
the rest of the system is allowed to assume about them.

Fifth module. It follows [the army](2026-08-28-army-design.md), which established that
an asset is a soft reference the rules never touch.

`docs/architecture.md` already settled the parts this does not revisit: glTF and GLB only
with STL rejected, conversion outside the server rather than a job queue (an offline tool
then; in the browser since [mesh conversion](2026-09-04-mesh-conversion-design.md), 09-04),
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
        + scale, orientation, modelled base     facts about the mesh
```

**Dedup is per user.** `architecture.md` deduped blobs across all users; that is
corrected. Nobody shares storage with anybody else, so deleting an asset never has to
check whether another account still points at the bytes.

**The library is private for discovery, not for access.** *Corrected 2026-08-29; this section
originally said that sharing copies a model into the recipient's library.* Nobody can browse or
search another account's assets. But the participants in a game download the assets that game
uses and render them locally, and that creates no record on either side — it is a download, not
a share, and nothing enters anybody's library. Per-user dedup is unaffected, because there is
still nothing pointing across accounts.

## Binding is not a third record

`architecture.md` had a third row, `Binding {army_id, target_ref, asset_id}`, existing so
that bindings would survive a list being re-imported.

**That reason is gone**, though not for the reason first given here. This section originally
said [the army](2026-08-28-army-design.md) updates a list by copying the army and applying the
new list to the copy, so bindings survived by construction. *Revised 2026-08-29:* there is no
copy operation. A changed list means a **re-import**, and a re-import **does not preserve
bindings** — armies are cheap to rebuild rather than objects to maintain.

So `Binding` still is not a record, because nothing is trying to preserve a binding across a
re-import any more. The original justification was to survive exactly that, and the requirement
itself has been dropped.

So the asset reference is a field on the model slot, and the library has two records
rather than three. The same document also printed a three-row table under the sentence
"two layers, not three," which is where the confusion started.

## The modelled base suggests; the slot's base is the hitbox

An asset records **the base the mesh was made for**. A slot records **the base used in
play**, set by the user and defaulting to the ruleset's value.

`architecture.md` had one base size, on the asset, winning for geometry, with any mismatch
against the list surfaced as a warning. Corrected: they are two different facts with two
different jobs.

- The **slot's** base is what geometry runs on. Distance and line of sight use it, and it
  is what the ruleset supplies a default for.
- The **asset's** modelled base is a property of the file, and its job is **finding
  meshes**: given a slot on a 32mm base, the library can offer the assets built for one.

This turns what was an error to warn about into a way to search. A user whose orks are all
based differently to the mesh is doing something normal, and the system has no reason to
comment on it.

## Folders

Users organise their own library. Folders **nest**, and **an asset can be in several of
them** — an ork mesh belongs in the orks folder and the winter-themed folder at once.

They are logical groupings, not a filesystem. Nothing derives from where an asset sits,
nothing else in the system reads them, and an asset in no folder is fine.

This is the answer to a user with five hundred meshes finding one, together with the
modelled base as a filter.

The web app filters folders client-side over the whole library, loaded page by page. A folder
view includes its sub-folders' assets.

## The terrain profile

Designed later, in [the table and its setup content](2026-08-29-setup-content-design.md): a
terrain asset carries collision volumes and their declarations, which a placement may override.
That is the second profile `kind` was introduced for.

## Deliberately not decided

- Upload mechanics: how bytes arrive, how progress is reported, how failures retry.
- Search beyond folders and modelled base.
- ~~What `scale` and `orientation` are measured against, and whether the mesh prep tool
  writes them or the user does.~~ **Settled 2026-09-04** by
  [mesh conversion](2026-09-04-mesh-conversion-design.md): the converter proposes, the user
  confirms in the preview, and the fields hold the confirmed value. Measured against the
  table's axes, per geometry values.

## Still open

- ~~The triangle-count threshold at upload. Agreed in principle in `architecture.md` as the
  only guardrail once conversion left the product; the number has never been chosen.~~
  **Settled 2026-09-04** by [mesh conversion](2026-09-04-mesh-conversion-design.md): warn above
  100,000 triangles; the converter aims for 15,000 (miniature) and 50,000 (terrain).
- ~~What a slot draws when its asset is missing.~~ **Settled on 08-30: it draws its own
  hitbox** — the slot's base shape extruded to its height, rendered semi-transparent green. A
  circle base gives a cylinder, an oval or rect its own prism.

  This needs nothing the slot does not already hold, and it shows the shape the rules actually
  use. A miniature has no collision volumes to fall back on, unlike terrain, and the base and
  height are exactly what distance and line of sight are measured against — so the proxy is not
  a stand-in for the mesh, it is the thing the mesh was always only decorating.
