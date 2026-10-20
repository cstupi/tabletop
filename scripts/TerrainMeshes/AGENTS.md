# scripts/TerrainMeshes

The generator for the client's built-in terrain meshes, `client/content/builtin/terrain/*.glb`. Procedural, deterministic C#
(seeded `System.Random` and hash noise); no Godot, no packages. Tests: `tests/Tabletop.TerrainMeshes.Tests`.

```bash
./scripts/terrain-meshes                         # writes all 30 into client/content/builtin/terrain/
./scripts/terrain-meshes client/content/builtin/terrain dunes quicksand   # or just the named ones
godot --headless --path client --import          # then refresh the .import files
```

## Layout

- `Catalog`: name → builder, the 30 meshes `client/content/builtin/catalog.json` lists under `meshes`, in its order, each
  painted by `Bake` at its authored inches (a unit-box mesh at its `Typical` size); `All` builds each once per process,
  `Generate` afresh. `Program` writes each (or the ones named after the output directory) and prints its triangles and size.
- `Bake`: the painter's finish over the vertex colours (spec `docs/superpowers/specs/2026-10-19-tabletop-look-design.md` →
  Terrain): a warm-brown wash by ambient occlusion (24 cosine rays against the model's opaque triangles and the table, 1.6"
  reach, averaged over a welded point's faces within 60°; foliage washed less) and a pale drybrush on convex edges a
  brush-width (0.25") wide. Translucent parts are left as built.
- `Model`, `Part`, `Material`: a mesh in the unit box (footprint x, z in [−0.5, 0.5], within r = 0.5 when `Round`; height y in
  [0, 1]; glTF Y-up), one `Part` per `Material`. Every vertex is clamped into the box. Colours are sRGB per vertex. A model
  given `Inches` is authored in inches at its catalog entry's footprint and top (x across `a`, z along `b`, a circle's
  diameter on both), centred, and each vertex is divided by them before the clamp; `Model.Inch` maps a Grid colour
  function's unit point back.
- `Shapes`: boxes, prisms and cones under a transform, limbs and chains of them between points, star-outlined slabs, icosphere
  blobs (lumpy, flat or smooth), blade tufts, `Ground` and `Disc` height fields in inches; `Part.Grid` for smooth height
  fields. `Paint`: colour mixing and value noise.
- `Foliage` (with `Quill`, a pen authoring in inches into a part, scaled for unit-box models): the vegetation and water pieces'
  detail, all flat-shaded: bark tubes, branching trees with clump foliage of single-triangle or diamond leaf cards
  (`Broadleaf`), conifers of drooping needle sprays (`Conifer`), fronds, blades, grass tufts, faceted stones, leaf litter, and
  faceted ground (`Field`, `Disc`, `Rect`). Leaves, blades and fronds go on `DoubleSided` materials.
- `Crag` (with `Pen`, flat triangles authored in inches, divided by a scale for unit-box models): the stone pieces' detail.
  `Stack` builds a stratified outcrop from rows by height and columns by angle: layers of random thickness, each set back,
  shifted, grooved at its bedding plane and lipped at its top, dipping; flat joint planes through every layer and through one,
  chips (joint planes through a band of height), cracks; a flat top or a broken dome, a tone per joint face and layer. Its
  `Outline`, `Profile` and `Band` make a cliff band round a hill's riser; `Carve` cuts a cave. `Chunk` is a broken stone (the
  convex hull of a few points), `Scree` a scatter of them, `Base` a flocked polar base, `Tufts` static grass. `Rock`,
  `Crumbly`, `Hill`, `Crater`, `Lair` and `Quicksand` use it; `rock` and `crater` are `Inches` models, the hills author in
  inches at 8" × 2" (`hill`) and 6" × 2" (`hill-rect`).
- `Masonry`: the built pieces' detail, authored in inches: `Block` (a box with every edge chamfered so the drybrush catches it;
  optionally chipped corners and rough, shifted corners for broken stone; `corners: false` drops the corner facets on trim
  seen from afar), `Girder` (I-section), `Rebar`, `Leaf` cards, `Rubble` (a grit mound under individual blocks, bricks and
  slabs), and `Unit`, which copies an inches-authored model into the unit box. `Ruins`, `Building`, `Huts`, `Tower`, `Wall`,
  `Barricade` and `Mines` use it; `Building.Window` (a pointed-arch window in a stone frame) is shared with `Tower`. All but
  the ruins are `Inches` models; `ruins` and `overgrown-ruins` are authored at 6" × 6" × 4" and copied into the unit box with
  `Masonry.Unit` (the 4" ruin shares `ruins`).
- `Glb`: one primitive per material, identical vertices shared; POSITION, NORMAL (float), COLOR_0 (linear, normalized bytes), 16- or 32-bit indices;
  PBR factors (base colour white, the colour is the vertex colour). Opaque materials in one node, alpha-blended ones in a
  second, neither transformed.
- Pieces, by catalog mesh: `Hill.Round` (`hill`), `Hill.Square` (`hill-rect`), `Woods` (`woods`), `Ruins.Build` and
  `Ruins.Overgrown` (`ruins`, `overgrown-ruins`), `Building` (`building`), `Huts` (`huts`, the group of small buildings),
  `Tower` (`tower`, the large sight blocker), `Wall` (`wall`), `Barricade` (`barricade`), `Rock` (`rock`), `Crumbly.Rock` and
  `Crumbly.Dunes` (`crumbly-rock`, `dunes`), `Field` (`field`, low wheat), `Crops.Tall` and `Crops.Plantation` (`tall-crops`,
  `plantation`), `Water.River` and `Water.Lake` (`water`, `lake`), `Rivers.Stream` and `Rivers.Toxic` (`stream`,
  `toxic-river`, both on `Water.River`'s styled form), `Sludge.Canal` and `Sludge.Field` (`sludge-canal`, `sludge-field`),
  `Crater`, `Haze` (`haze`: an inches model at 8"×6", a stained base with sludge pools, crust plates, pipe stubs, a drum, rubble and dead tufts under translucent billows), `Marsh` (`death-marsh`), `Mines` (`mine-field`), `Quicksand`, `Vegetation.Deadly` and `Vegetation.Jungle`
  (`deadly-vegetation`, `jungle`), `Lair` (`beast-lair`).

## Rules the meshes keep

- `MeshLoader.LoadBuiltin` scales the box non-uniformly onto the placement's first volume (a hill is fitted 3–4 times wider
  than tall, a wall 12 times longer than deep), so details meant to look round are authored tall in the box.
- A mesh is shared only by the same terrain at another size (`hill`, `woods`, `ruins`); every other entry has its own. The ones
  with an `Inches` model are authored at their entry's footprint and top, so a 0.1" mine field keeps its mines, wire and plates within 0.1".
- What is drawn matches what collides (client UX spec → Physical collision): every mesh a Solid entry draws reaches y = 1; the
  hills' plateau is flat at y = 1 inside `Hill.Top`; the rocks' and the beast lair's tops are flat at their volume's top; the
  ruins' floor stays at ground level (an Area: models stand on the table inside).
- At most 50 000 triangles and 3 MB per file, 36 MiB for all of them (`Budget`); `GlbPreflightTests` also checks the shipped files. The tests judge winding in the
  authored inches, where a thin piece's slopes are as drawn.
- Godot's runtime glTF import leaves vertex colours off on a mesh's first primitive; `MeshLoader` turns them on, and takes a
  node whose surfaces are all alpha-blended out of the shadow pass.
- A full run into `client/content/builtin/terrain` also writes `meshes.sha256` (`Stamp`): the hash of this folder's source
  and of each shipped mesh. The tests fail when either differs, so run the full generator after any change here. They do not
  compare against a fresh build: MathF's last bits differ between C runtimes (WSL against the release's SDK container), and a
  welded vertex or a colour byte on a rounding boundary then lands differently.
