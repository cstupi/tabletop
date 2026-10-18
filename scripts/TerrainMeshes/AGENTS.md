# scripts/TerrainMeshes

The generator for the client's built-in terrain meshes, `client/content/builtin/terrain/*.glb`. Procedural, deterministic C#
(seeded `System.Random` and hash noise); no Godot, no packages. Tests: `tests/Tabletop.TerrainMeshes.Tests`.

```bash
./scripts/terrain-meshes                         # writes all thirteen into client/content/builtin/terrain/
godot --headless --path client --import          # then refresh the .import files
```

## Layout

- `Catalog`: name → builder, the thirteen meshes `client/content/builtin/catalog.json` lists under `meshes`. `Program` writes each and prints
  its triangles and size.
- `Model`, `Part`, `Material`: a mesh in the unit box (footprint x, z in [−0.5, 0.5], within r = 0.5 when `Round`; height y in
  [0, 1]; glTF Y-up), one `Part` per `Material`. Every vertex is clamped into the box. Colours are sRGB per vertex.
- `Shapes`: boxes, prisms and cones under a transform, icosphere blobs (lumpy, flat or smooth), blade tufts; `Part.Grid` for
  smooth height fields. `Paint`: colour mixing and value noise.
- `Glb`: one primitive per material; POSITION, NORMAL (float), COLOR_0 (linear, normalized bytes), 16- or 32-bit indices;
  PBR factors (base colour white, the colour is the vertex colour). Opaque materials in one node, alpha-blended ones in a
  second, neither transformed.
- Pieces: `Hill` (`Round`, `Square`), `Woods`, `Ruins`, `Building`, `Wall`, `Rock`, `Field`, `Water` (`River`, `Lake`), `Pad`
  (`Plaza`, `Crater`), `Haze`.

## Rules the meshes keep

- `MeshLoader.LoadBuiltin` scales the box non-uniformly onto the placement's first volume (a hill is fitted 3–4 times wider
  than tall, a wall 12 times longer than deep), so details meant to look round are authored tall in the box.
- What is drawn matches what collides (client UX spec → Physical collision): the Solid pieces (`hill`, `hill-rect`,
  `building`, `wall`, `rock`) reach y = 1; the hills' plateau is flat at y = 1 inside `Hill.Top`; the rock's top is cut flat at
  y = 1; the ruins' floor stays at ground level (an Area: models stand on the table inside).
- At most 20 000 triangles and 2 MB per file; `GlbPreflightTests` also checks the shipped files.
- Godot's runtime glTF import leaves vertex colours off on a mesh's first primitive; `MeshLoader` turns them on, and takes a
  node whose surfaces are all alpha-blended out of the shadow pass.
- The tests fail when a shipped `.glb` differs from the generator's output: regenerate after any change here.
