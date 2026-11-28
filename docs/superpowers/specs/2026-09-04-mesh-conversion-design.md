# Mesh conversion

Design session, 2026-09-04. How a 3D-printing STL becomes a game-ready GLB in a user's
library. Formerly "the mesh prep tool".

`architecture.md` ruled that upload accepts GLB and rejects STL, and that conversion is not
server-side — that would mean a job queue, a worker and an async upload state. It assumed the
alternative was **an offline tool the user installs and runs**, sketched as a
command line before the design sessions. This session keeps every behaviour that sketch wanted and moves it **into the web app's upload flow**. There is no tool.

## Conversion runs in the browser

The user drops one or more `.stl` files on the library page. The page parses them, decimates
with a WebAssembly mesh simplifier, exports a GLB, shows the result, and uploads it by the
presigned path [the management API](2026-08-29-management-api-design.md) already has. The server
still never touches conversion; its only involvement is the ranged header read it already does.

Why here and not a program: the sketch itself said the tool must be usable by someone who is not
technical, then handed them a terminal. A standalone desktop app is a second install and a
second codebase to keep in step with the upload rules. The browser already owns upload
([the web app](2026-08-29-web-app-design.md)), already needs a mesh preview for binding, and can
hold a two-million-triangle STL in memory. The language exception the sketch carried goes with
the tool.

**Built from existing libraries** — an STL parser, a WebAssembly simplifier, a glTF exporter —
not from a WebAssembly build of `Tabletop.Geometry`. That library has no mesh-processing code
and should not grow any.

## The flow

1. **Drop.** One or more `.stl`, or a `.glb`. A GLB skips conversion and goes to the preview.
2. **Convert**, in the page, as below.
3. **Preview.** The mesh stands on a ring of the chosen modelled-base size, with the proposed
   scale and orientation applied, and the triangle count before and after. The user picks the
   kind, the modelled base, and corrects scale or facing if the guess is wrong. The budget is
   adjustable here.
4. **Confirm.** Upload follows the presigned path, and the confirmed scale, orientation and
   modelled base are written to the asset row with it.

**Nothing uploads before the user has seen it.**

## What conversion does

Each behaviour is from the sketch, kept:

- **Parse** ASCII and binary STL.
- **Merge** several files into one mesh, positioned as they come. Kits ship as parts already
  placed relative to each other — body, arms, heads, weapon options — and no fitting is
  attempted.
- **Decimate** to the budget for the chosen kind: **50,000 triangles for a miniature, 150,000
  for terrain.** A table holds around a hundred miniatures and the client draws them through levels
  of detail (graphics settings → Mesh LOD threshold); terrain
  is larger and fewer. Quality is the simplifier's own error metric, and the preview is the
  user's check.
- **Recompute smooth normals** with an angle threshold. STL carries only per-face normals, and
  a faceted mini is the most common way a conversion looks wrong.
- **Guess units and up-axis.** STL has neither. The guess is millimetres, and the up-axis is
  whichever of Y or Z gives a footprint that fits a base and a height taller than it is wide.
  The guess becomes the proposed scale and orientation.
- **Warn about pre-supported files.** Many thin, tall, disconnected components at the bottom of
  the mesh is the signature of print supports. The page says so in words and asks for the
  unsupported file. **No support removal, ever** — it is not reliably possible.
- **Emit GLB**: one file, meshes and normals only. No textures, no materials beyond a default, no
  rigging.
- **Centre.** The mesh is translated so its footprint is centred on the origin and its lowest
  point along the up-axis the user confirms (the guess by default) is at zero, so it stands on
  its base in the preview and in play. Scale and orientation are not baked in: the GLB keeps the file's units and axes, and the
  asset row carries the confirmed scale and orientation.

## What it writes

**Scale, orientation and modelled base, all confirmed by the user.** This closes the question
open in both [the asset library](2026-08-28-asset-library-design.md) and
[geometry values](2026-08-29-geometry-values-design.md) — whether the tool or the user writes
them: the converter proposes, the user confirms in the preview, and the fields hold the
confirmed value. A GLB dropped straight in is read by the glTF standard (metres, Y up, front +Z): scale 39.37 and
`{ pitch: 90, yaw: 180, roll: 0 }`, which the user confirms like any proposal.

## The threshold

**The upload warning fires above 250,000 triangles.** Closing the asset library's open item. It
still never blocks, per the management API. The converter's budgets sit well below it, so the
warning fires only for a file that skipped the converter — a dense export straight from Blender —
or a piece the user deliberately kept dense.

## What this removes

`tools/mesh-prep/` is no longer a deliverable. It leaves the layout, `architecture.md`'s table
of deliverables and build order, and `CLAUDE.md`. Workstream 11 is stale like every other
workstream prompt. Everything the sketch wanted survives inside the web app.

## What it does not do

- Terrain collision volumes beyond the default. **The browser computes the bounding-box volume**
  from the mesh it already holds — a converted STL or a dropped GLB alike — and writes it with
  `PUT /v1/assets/{id}/volumes` at confirm, which is the author geometry values' "at upload"
  never named. The volume editor is for when that default is wrong, unchanged.
- Base size in play, which is the slot's, not the asset's.
- Anything on the server. Management still sees a GLB and reads a header.

## Testing

The pipeline is plain JavaScript over typed arrays and runs headless under Node against a small
set of real STLs: one ASCII, one binary, one multi-part kit, one pre-supported. Asserted:
triangle count within budget, normals present, the up-axis guess, and the support warning firing.

## Deliberately not decided

- ~~Which parser, simplifier and exporter — implementation, with the web app's other framework
  choices.~~ **Settled 2026-09-05**: the pipeline's own STL reader (the format is 50 bytes per
  facet or `vertex x y z` lines), `meshoptimizer`'s WebAssembly simplifier, and
  `@gltf-transform/core` as the GLB writer and reader. three.js draws the preview only.
- ~~The angle threshold for smooth normals, and the exact support-detection heuristic. Tuned
  against the test STLs.~~ **Settled 2026-09-05 as defaults, tuned against the fixtures**: a 60°
  crease angle; a component is support-like when its horizontal extent is under 6% of the
  model's, it is at least 3× taller than wide, and its lowest point is within 2% of the model's
  height from the bottom; the warning fires at six such components.

## Still open

Nothing of its own. Closed here: the asset library's triangle threshold and the scale-and-
orientation question it shared with geometry values.
