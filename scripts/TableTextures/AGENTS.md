# scripts/TableTextures

The generator for the table's textures (spec `docs/superpowers/specs/2026-10-19-tabletop-look-design.md`): the board prints in
`client/content/builtin/textures/` and the client's own `client/content/table/` (the wood under the board, the terrain surface
grains). Procedural, deterministic C# (hash and gradient noise that repeats once per tile); no Godot, no packages, no
third-party images. Tests: `tests/Tabletop.TableTextures.Tests`.

```bash
./scripts/table-textures                                                      # all of them
./scripts/table-textures client/content/builtin/textures client/content/table grass wood   # or just the named ones
godot --headless --path client --import                                       # a new file needs its .import
```

## Layout

- `Field`: tileable noise over the unit square: gradient noise (one period per axis for streaks), fractal sums, cellular
  distances and ids, per-pixel grain.
- `Texture`: a name, a size and `At(u, v)` → sRGB colour and height; `Render` is RGBA bytes, alpha the height; `Png` writes it.
- `Boards`: the seven prints the catalog lists under `textures`, 1024² for a 12" repeat: `grass`, `desert`, `urban`, `snow`,
  `ash`, `wasteland`, `alien`.
- `Details`: `wood` (1024² for 24"), and the grains `detail-stone`, `detail-ground`, `detail-foliage`, `detail-wood`,
  `detail-metal` (512², grey about 0.88, height in alpha), which `TerrainLook` maps by `SurfaceFamily`.
- `Png`: 8-bit RGBA writer (per-row filter, zlib smallest) and reader.

## Rules

- Alpha is height, never transparency: each print's `.import` is lossless with `process/fix_alpha_border=false` and
  `detect_3d/compress_to=0` (`BuiltinContentTests`).
- The tests fail when a shipped PNG differs from the generator's output by more than a step per channel, when a print shows a
  seam, or over 3 MB; regenerate after any change here.
- A new board is a `Boards` entry and a catalog `textures` name, in the same order.
