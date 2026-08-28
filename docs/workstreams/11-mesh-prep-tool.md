# 11 — Mesh prep tool

**Owns:** `tools/mesh-prep/`
**Depends on:** nothing. Fully standalone — it can be built at any time, by anyone, in parallel
with everything else.
**Branch:** `ws/11-mesh-prep-tool`

Read `docs/workstreams/README.md` and `CLAUDE.md` first. You do **not** need
`docs/architecture.md` for this workstream — this tool is not part of the game.

## Goal

A command-line tool that turns 3D-printing STLs into game-ready glTF.

The product deliberately does **not** convert meshes. `server/management/` accepts glTF/GLB
only and rejects STL. This tool is how a user gets from what they downloaded to what they can
upload. It runs on their machine, offline, before they ever touch the web app.

## Why this exists

One Page Rules and most miniature creators distribute **STL**, via MyMiniFactory, Patreon and
Cults3D. Three things make those files unusable as-is:

- **Godot cannot load STL** — not in the editor, not at runtime. The game loads glTF via
  `GLTFDocument`.
- **Print meshes are 50–100× too dense.** A print-ready 32mm miniature is routinely 500k–2M
  triangles and 50–200MB, meshed for ~0.05mm layer height. A table holds 100 models.
- **STL carries no materials, UVs, colour, or units** — it is triangle soup with per-face
  normals.

## Deliverable

A CLI that takes one or more STL files and emits a single game-ready `.glb`:

```
mesh-prep model.stl -o model.glb
mesh-prep body.stl arms.stl weapon.stl head.stl -o marine.glb
```

It must be usable by someone who is not technical. Good defaults, few flags, clear errors.

## What it must do

**Convert** STL (ASCII and binary) to glTF 2.0 binary (`.glb`).

**Merge multi-part kits.** STL miniatures ship as separate files — body, arms, weapon options,
heads — for kitbashing. Accept several inputs and emit one mesh. Assume parts are already
positioned correctly relative to each other in their own coordinate space; do not attempt
automatic fitting.

**Decimate to a triangle budget.** Default to roughly 15k triangles per model. A tabletop holds
around 100 models at once, and they are small on screen. Make the budget a flag, and report
before/after counts.

**Recompute smooth normals** with an angle threshold. STL stores per-face normals only, so a
naive conversion renders completely faceted. This default being wrong is the single most
common way converted minis look bad.

**Normalise scale and orientation.** STL has no unit metadata; these files are almost always
millimetres, and up-axis varies by exporter. The game's world unit is one inch. Emit something
sane and report what was assumed, so the user can correct it at upload — `scale` and
`orientation` are fields on the Asset for exactly this reason.

## What it must warn about

**Pre-supported files.** Most creators ship both raw and pre-supported versions. Supported files
have rafts, scaffolding and sprues baked into the mesh — they render as a forest of spikes, and
stripping them automatically is not reliably possible. Detect the likely case if you can, and
either way say plainly in `--help` and in errors: **use the unsupported file.**

Do not attempt automatic support removal.

## Out of scope — do not build

Texturing, UV generation, material authoring, auto-rigging. A GUI. Support removal. Uploading
anywhere. Any dependency on the game, its servers, `libs/`, or an account. Base size selection —
that is set at upload time in the web app, not here.

This tool knows nothing about armies, units, stats, or games. It converts a mesh.

## Decisions left to you

Record each in a `## Decisions` section appended to this file:

- **Language and dependencies.** Python with `trimesh`/`pymeshlab` has the strongest mesh
  ecosystem; a thin wrapper over `assimp` and `gltfpack` is far less code but requires users to
  install those binaries. Either is acceptable — pick one and say why. Note this introduces a
  language that is not C# or TypeScript, which is a deliberate exception for this tool only.
- Decimation algorithm and how quality is judged.
- Whether to bundle dependencies or document installing them.

## Done when

1. A real OPR STL converts to `.glb` and opens correctly in Godot.
2. A multi-part kit merges into one mesh.
3. Output lands within the triangle budget, and before/after counts are reported.
4. Normals are smooth, not faceted — verified visually, and say so.
5. `--help` is comprehensible to a non-technical user and mentions the pre-supported trap.

Paste the commands and their output, and a screenshot of the result in Godot.

## Decisions

Record here, one line of reasoning each.
