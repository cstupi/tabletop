# Theme materials and button padding

Owner, 2026-11-21: *"The padding around buttons does not fit with our spacing rules. Also the 'sci fi' theme is just kinda blue - we
want it to have a real space feel and not just a color swap. Fantasy should FEEL like fantasy - wooden textures, metal inlays with
blemishes etc."* Applies to the Godot client, the web app and the viewer (one theme reads the same in all three). Spacing per
`2026-11-21-spacing-rules-design.md`.

## Button padding

Every button's padding is a step of the scale and the same in all three apps: `Space.M` (16) left and right, `Space.S` (8) top and
bottom for a text button; `Space.S` all round for an icon-only button; a tile (icon over word) `Space.S` all round. Buttons in a row sit
`Space.S` apart, a primary beside its Cancel `Space.L` (dialogs). A button's height comes from its padding and font, the same for every
text button; a full-width primary (Start setup, End setup) keeps the same padding. A theme's drawn frame (a nine-patch's border) adds
its own inset outside the padding, so text never sits on the frame.

## Fantasy: a war table

The panels and buttons are made of materials, not colour fills:

- **Panels:** dark oiled wood, visible grain running along the panel's long side, slight colour variation between planks, a bevelled
  edge; iron or aged brass corner brackets and edge strips (inlays) with small rivets, the metal scuffed, pitted and darkened in recesses
  (blemishes), never clean or uniform. Panels tile the grain; corners stay crisp at every size (nine-patch with tiled centre).
- **Buttons:** a raised wooden plaque with a brass rim; hover warms the wood, pressed sinks it (darker, inset shadow); the primary is
  the same plaque inlaid with brass or a red-lacquer face, clearly the one lit action; disabled is unvarnished, dull.
- **Inputs and dropdowns:** a recessed groove cut into the wood, parchment-toned field inside.
- **Full-screen pages** (Home, lobby, Options, the website): a darker wood or leather-topped table behind the panels, vignetted.
- **Type and icons** keep the theme's fonts; icons read as engraved or branded into the material.

## Sci-fi: a starship console

- **Panels:** dark gunmetal hull plates, brushed-metal texture, panel seams and recessed bolts, chamfered (clipped) corners; a thin
  light strip along one edge in the accent colour, faint and steady (no animation on panels); a subtle grid or etched schematic in the
  plate, low contrast so text reads.
- **Buttons:** a backlit console key: dark glass face over metal, an inner glow in the accent on hover, brighter and a pressed inset on
  press; the primary glows steadily; disabled is unlit.
- **Inputs and dropdowns:** a recessed screen with a faint scanline or dot-matrix texture and an accent caret.
- **Full-screen pages:** deep space behind the panels: a starfield with a faint nebula, stars of a few sizes and brightnesses, still or
  with an imperceptibly slow drift; the HUD over the table never draws it.
- **Accents:** cyan stays the accent, joined by a warm amber for warnings and a second cool hue for chrome, so the theme reads as a
  machine, not a recolour.

## Constraints

- Text contrast on every panel and button at least 4.5:1 (normal text) against the material's lightest area under text; a test checks
  it from the palette and the textures' measured luminance where text sits.
- Textures are generated, reproducible and checked in (a script under `scripts/` with tests, as `scripts/TableTextures`), or authored
  as SVG; small (each under 512 KB, nine-patches at 1× and 2×); the web app and viewer use the same images (`web/static/themes/<id>/`
  or CSS `border-image`) so all three match.
- Performance: no per-frame shader on Hud panels; the starfield is a static image (or one cheap shader on full-screen pages only).
- The play Hud over the table stays readable and compact: materials, not extra padding.

## Left to the builder

Exact textures, colours and nine-patch margins; the generator; screenshots of Home, Create game, lobby, setup, play and Options in both
themes at 1280×720 and 1920×1080, and the website's games and armies pages, checked against this spec.
