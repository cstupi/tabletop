# Readable rings

The threat outlines (hovering a unit of another team, `2026-11-07-threat-range-on-hover.md`) and the V range rings drew as 1 px lines
with `Label3D` text in world space, too small to read at the default camera. Owner: "Visuals are worth a thousand words"; "use lots
of spacing, icons and coloring to make it clear".

Both layers get one look, in the Godot client and the browser viewer:

| Ring | Fill | Outline | Label |
| --- | --- | --- | --- |
| Threat `Charge N"` | team colour, alpha 0.22 | 3 px, team colour | charge icon, `Charge N"` |
| Threat `Shoot N"` | team colour, alpha 0.12 | 3 px, team colour | crosshair icon, `Shoot N"` |
| Range (V) `N"` | team colour, alpha 0.08 | 2.5 px, team colour | crosshair icon, `N"` |

Nested fills add up, so the nearer reach reads darker.

## Core (`client/src/Core`, Godot-free)

- `RangeRing` gains `RingKind Kind` (`Range`, `Charge`, `Shoot`), set by `RangeRings.Of` and `ThreatRings.Of`; the label icon and
  the fill strength follow it (`RingLook.Of(kind)`: fill alpha, outline width in px, icon key).
- `RingShape.Of(segments)`: the outline's segments chained into closed loops (end to start within 1e-3"), each loop's signed area;
  `Triangles`: the fill, outer loops (counter-clockwise) with the holes inside them (clockwise) bridged in, ear-clipped; `Candidates`:
  at most 48 points along the loops, evenly spaced, where a label may stand.
- `RingLabels.Anchor(candidates on screen, viewport)`: the candidate lowest on the screen among those inside the viewport, else the
  one nearest it, clamped; `RingLabels.Place(labels, viewport)`: in order, a label that would overlap one already placed moves above
  it (then below, if the top is reached), and every label stays inside the viewport. Two labels never overlap.

`RangeRingCache` and `ThreatRingCache` are unchanged. The shape (loops, triangles, candidates) is built once per ring set, when the
layer rebuilds its mesh; a hover that returns to a cached unit rebuilds nothing.

## Godot (`client/src/Table`)

- `RingMesh`: one `ArrayMesh` per layer: the fill triangles (vertex colour with the kind's alpha) and the outline as a ribbon, two
  triangles per segment carrying the segment's other end in `UV2`; `content/shaders/ring_line.gdshader` offsets each vertex
  perpendicular to the segment on screen by half the width, so the line is the same number of pixels at any distance.
- The fill and the solid outline depth-test (models and terrain stand over them; fill 0.04" and line 0.08" above the board); a
  second pass of the outline at 0.3 opacity skips the depth test, so a line behind a building still shows faintly.
- `RangeRingNode` labels are `Control`s in a `CanvasLayer` of the table's viewport: a theme panel holding the kind's icon (the theme's
  `charge` and `shoot` icons, white and modulated to the team colour) and the label in the team colour. Placed each frame by
  `RingLabels.Anchor` and `Place` from the camera; a frame projects at most 48 points per ring.
- Icons: `content/themes/{scifi,fantasy}/icons/charge.svg` and `shoot.svg`, drawn for this project (each `LICENSES.md` already says
  `icons/` is); `UiThemes` lists them.

## Viewer (`viewer/src/lib/play`)

- `ringShape.ts` ports `RingShape` (loops, candidates) and `RingLabels` (anchor, place); the fill is the loops filled `evenodd` on the
  canvas, so holes stay open.
- `ringsOverlay` draws fills, the kind's outline width over a dark halo, and a pill label (icon glyph drawn on the canvas, team colour
  on a dark pill) at the anchor, decluttered across the threat and range layers by one `placeRingLabels` call (`ringsOverlay` takes
  both layers, the threat's first).

## Validation

- Core tests (`RingShapeTests`): one round base's loop and fill area (π r²); two overlapping bases' fill area (the union's); a square
  with a square hole (area outer minus hole); candidates count and spread. `RingLabelsTests`: anchor is the lowest visible point; an
  off-screen ring clamps; two labels at the same anchor are placed apart and inside the viewport; three stacked at the top go below.
- `dotnet test tests/Tabletop.Client.Tests`; `./scripts/check-client` empty; `dotnet build client/Tabletop.csproj` clean.
- Viewer: `ringShape.test.ts` (the same cases); `npm run check && npm test`.
- On-screen: before and after at the default camera, 1280×720 and 1920×1080, enemy hover on and V on.
- Frame times: `PerfProbe` CSV while a script sweeps the hover across every unit; before and after.

## Results

- Core: `RingShapeTests` (7: one base's loop and its disc's area, two overlapping bases fill their union once, two apart are two
  loops, a hole stays open, segments out of order close, candidates spread, each kind's look) and `RingLabelsTests` (4).
  `dotnet test tests/Tabletop.Client.Tests`: 987 passed. `./scripts/check-client` empty; `dotnet build client/Tabletop.csproj` clean.
- Viewer: `ringShape.test.ts` (7); `npm run check` 0 errors; `npm test` 1705 passed, 6 skipped.
- On-screen: this worktree's game server on `127.0.0.1:5291` (dev mode, the shared Postgres), a `skirmish` game played one round by
  `sim skirmish --driver socket --rounds 1`, the Linux client (opengl3, WSLg) signed in as `sim-player-0` (team 1) through
  `TABLETOP_PERF_GAME`, Red unit 3 selected with V on and Blue unit 3 hovered (synthetic X events to the client's own window). Before:
  1 px lines, the labels a few pixels tall in world space. After: the charge area filled, the `Charge 12"` and `Shoot 30"` outlines
  3 px in team 2's blue with their labels (sword and crosshair icons) under each outline's lowest point, the selected unit's `24"`
  ring in red with its crosshair label; no two labels overlap; at 1920×1080 the `Shoot 30"` label is pulled inside the window.
- Cost per rebuild (10 round bases, a throwaway timing test): `RangeOutline.Of` 8.4–9.3 ms, `RingShape.Of` 0.3–0.6 ms on top. Per
  frame: at most 48 projected points per ring and the label placement; no mesh is built.
- Frame times (`PerfProbe`, 1280×720, V on, the hover swept across the three enemy units and the empty table every 0.25 s, 10 rounds;
  other agents' Godot clients shared the GPU, so runs of one build differ up to 2×): old build sweep dt p50 9.7 ms, p95 13.9, max
  36.0; new build, two runs, p50 10.5 and 6.6 ms, p95 25.5 and 8.7, max 112.6 and 27.8. Nothing attributable to the rings.
