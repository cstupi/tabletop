# scripts/TerrainMeshes

The generator for the client's built-in terrain meshes, `client/content/builtin/terrain/*.glb`. Procedural, deterministic C#
(seeded `System.Random` and hash noise); no Godot, no packages. Tests: `tests/Tabletop.TerrainMeshes.Tests`.

```bash
./scripts/terrain-meshes                         # writes all 30 into client/content/builtin/terrain/
./scripts/terrain-meshes client/content/builtin/terrain dunes quicksand   # or just the named ones
godot --headless --path client --import          # then refresh the .import files
```

## Layout

- `Catalog`: name → builder, the 30 meshes `client/content/builtin/catalog.json` lists under `meshes`, in its order. `Program`
  writes each (or the ones named after the output directory) and prints its triangles and size.
- `Model`, `Part`, `Material`: a mesh in the unit box (footprint x, z in [−0.5, 0.5], within r = 0.5 when `Round`; height y in
  [0, 1]; glTF Y-up), one `Part` per `Material`. Every vertex is clamped into the box. Colours are sRGB per vertex. A model
  given `Inches` is authored in inches at its catalog entry's footprint and top (x across `a`, z along `b`, a circle's
  diameter on both), centred, and each vertex is divided by them before the clamp; `Model.Inch` maps a Grid colour
  function's unit point back.
- `Shapes`: boxes, prisms and cones under a transform, limbs and chains of them between points, star-outlined slabs, icosphere
  blobs (lumpy, flat or smooth), blade tufts, `Ground` and `Disc` height fields in inches; `Part.Grid` for smooth height
  fields. `Paint`: colour mixing and value noise.
- `Glb`: one primitive per material; POSITION, NORMAL (float), COLOR_0 (linear, normalized bytes), 16- or 32-bit indices;
  PBR factors (base colour white, the colour is the vertex colour). Opaque materials in one node, alpha-blended ones in a
  second, neither transformed.
- Pieces, by catalog mesh: `Hill.Round` (`hill`), `Hill.Square` (`hill-rect`), `Woods` (`woods`), `Ruins.Build` and
  `Ruins.Overgrown` (`ruins`, `overgrown-ruins`), `Building` (`building`), `Huts` (`huts`, the group of small buildings),
  `Tower` (`tower`, the large sight blocker), `Wall` (`wall`), `Barricade` (`barricade`), `Rock` (`rock`), `Crumbly.Rock` and
  `Crumbly.Dunes` (`crumbly-rock`, `dunes`), `Field` (`field`, low wheat), `Crops.Tall` and `Crops.Plantation` (`tall-crops`,
  `plantation`), `Water.River` and `Water.Lake` (`water`, `lake`), `Rivers.Stream` and `Rivers.Toxic` (`stream`,
  `toxic-river`, both on `Water.River`'s styled form), `Sludge.Canal` and `Sludge.Field` (`sludge-canal`, `sludge-field`),
  `Crater`, `Haze`, `Marsh` (`death-marsh`), `Mines` (`mine-field`), `Quicksand`, `Vegetation.Deadly` and `Vegetation.Jungle`
  (`deadly-vegetation`, `jungle`), `Lair` (`beast-lair`).

## Rules the meshes keep

- `MeshLoader.LoadBuiltin` scales the box non-uniformly onto the placement's first volume (a hill is fitted 3–4 times wider
  than tall, a wall 12 times longer than deep), so details meant to look round are authored tall in the box.
- A mesh is shared only by the same terrain at another size (`hill`, `woods`, `ruins`); every other entry has its own. The ones
  with an `Inches` model are authored at their entry's footprint and top, so a 0.1" mine field keeps its mines, wire and plates within 0.1".
- What is drawn matches what collides (client UX spec → Physical collision): every mesh a Solid entry draws reaches y = 1; the
  hills' plateau is flat at y = 1 inside `Hill.Top`; the rocks' and the beast lair's tops are flat at their volume's top; the
  ruins' floor stays at ground level (an Area: models stand on the table inside).
- At most 20 000 triangles and 2 MB per file; `GlbPreflightTests` also checks the shipped files. The tests judge winding in the
  authored inches, where a thin piece's slopes are as drawn.
- Godot's runtime glTF import leaves vertex colours off on a mesh's first primitive; `MeshLoader` turns them on, and takes a
  node whose surfaces are all alpha-blended out of the shadow pass.
- The tests fail when a shipped `.glb` differs from the generator's output: regenerate after any change here.
