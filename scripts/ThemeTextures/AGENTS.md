# scripts/ThemeTextures

The generator for the themes' materials (spec `docs/superpowers/specs/2026-11-21-theme-materials-design.md`): per theme a panel, the
button's five faces (`button`, `-hover`, `-pressed`, `-disabled`, `-primary`), an `input` field and the full-screen backdrop (`backdrop`, a
picture stretched over the page, and `backdrop-tile`, repeated), each at 1× and 2× (`name@2x.png`), written to
`client/content/themes/<id>/` and the same bytes to `web/static/themes/<id>/`. Procedural, deterministic C# over `Field` and `Png` from
`scripts/TableTextures`; no Godot, no packages, no third-party images. Tests: `tests/Tabletop.ThemeTextures.Tests`.

```bash
./scripts/theme-textures                                   # all of them
./scripts/theme-textures fantasy/panel scifi/button-hover  # or just these (a bare name does both themes)
godot --headless --path client --import                    # a new file needs its .import
```

## Layout

- `Sheet`: one image designed in 1× pixels: `Relief` (a height, lit from the top left, so bevels, rivets, seams and grooves shade
  themselves; a flat point keeps its albedo exactly) and `At` (a `Surface`: albedo, coverage, shine, glow). `Use` is `NinePatch` (the
  centre and edges repeat with period `Width − 2·Margin`, so every function there is periodic), `Tile` or `Stretch`. `Inset` is the drawn
  frame: text sits inside it, padding comes after it. `Render(scale)` takes 3×3 samples a pixel.
- `Shapes`: rounded and chamfered rectangle distances, segments, domes, bevels, scratches.
- `Fantasy`: oiled walnut (planks, wandering grain, pores, end joints), pitted and rusted iron strips with rivets, aged brass brackets and
  rims (patina, verdigris, scuffs), red lacquer, a parchment groove, a green leather tile under a lamp-lit vignette.
- `SciFi`: brushed gunmetal plates (a seam and bolts inside the rail, an etched schematic), chamfered corners, a cyan light strip and an
  amber lamp, backlit keys (glass face, inner glow), a scanline screen, a tiled starfield over a nebula picture.
- `Themes`: the list, the scales and the two output roots.

## Changing a material

1. Edit the sheet in `Fantasy.cs` or `SciFi.cs`; keep anything in a nine-patch's centre periodic over its repeat, and frame decoration
   (seams, bolts, glows) within the margin minus 2 px of the edge, or the repeat shows a joint.
2. A new margin or inset changes `UiThemes` (`Materials(...)`) and the `--mat-*` tokens and `--frame-inset`/`--control-inset` in
   `web/src/themes.css` (the 2× file is sliced at twice the 1× margin).
3. `./scripts/theme-textures`, `godot --headless --path client --import`, then `dotnet test tests/Tabletop.ThemeTextures.Tests`: it fails
   on a stale or oversize (512 KB) file, a copy that differs, a joint in a repeat, a margin the client reads differently, or text under
   4.5:1 on a panel, button face or field.
4. Look at it in Godot and the browser (`client/AGENTS.md`, `viewer/AGENTS.md` → Looking at a page in a headless browser).
