# One HUD frame

Owner, 2026-11-21, after moving Undo and Log took a day of agent time: *"None of that seems like something that should take a long time
in a well designed system."* — and, on the proposal to have one layout model every overlay reads and the browser shares: *"Do it."*

## Problem

- The Godot client places the play HUD with `HudLayout` (Core), but the browser's play page places the same parts with its own CSS
  (`PlayPage.svelte`, `InfoColumn.svelte`, grid and absolute rules). A layout change is made and verified twice and drifts.
- Overlays find the panels each their own way: `GameScreen.Covered()` feeds `RangeRingNode` (ring labels), `UnitActionsNode.Covered()`
  builds its own list, `HudLayout.Below` is applied by hand for the unit card under the roster, `DiceTray.Covered` is a flag set by
  `GameScreen.Layout`, the terrain hover panel and violation labels clamp by their own rules. Moving one panel means finding each.

## Design

- **`HudFrame` (client Core, Godot-free, unit-tested):** the one answer to "where is everything" for a window size and the visible parts.
  `HudFrame.Of(window, visible, measured)` returns the slot of every part (`HudPart`), the order they stack in, and `Obstacles`: the
  rects of every visible panel (actual measured bottom where a part is shorter than its slot, as `Below` does today). Built once per
  layout pass in `GameScreen.Layout`; published as `Session`-level state (`HudFrame.Current`, raised on change).
- **Every overlay reads `Obstacles`:** ring labels (`RingLabels.Place`), the bar over the unit (`UnitActionsPlace`), the terrain hover
  panel, violation/warning labels, move-path labels, toasts' placement, the dice tray's covered state. No overlay computes panel rects
  itself; the `Covered()` methods and per-node lists go.
- **Parts are declared once:** each `HudPart` has its anchor (corner/edge), its size rule (fixed, measured, fill), and its stacking
  neighbour (e.g. the unit card below the roster, the dice tray above the decision box) in one table in `HudFrame`; moving a part is
  changing its row. The setup frame (`HudLayout.Setup`) folds into the same table with the setup parts.
- **The browser reads the same frame:** a TypeScript port `hudFrame.ts` with an agreement fixture written by the client tests (window
  sizes 1280×720, 1366×768, 1920×1080, 2560×1440; log open/closed; replay; setup), and the play page positions each part absolutely from
  the slots (one wrapper per part, `style` from the slot), replacing the play page's layout CSS. Its overlays read `obstacles`.
- **Tests:** no-overlap from 1280×720 up (today's `HudLayout` tests, kept), every overlay's placement against `Obstacles`, the agreement
  fixture, and a test that moving a part's anchor in the table moves its obstacle for every overlay.

## Done when

Moving a HUD part (e.g. Undo/Log between corners) is a one-row change in `HudFrame`, plus its fixture regeneration, with no edit to any
overlay in either app. `client/AGENTS.md` and `viewer/AGENTS.md` carry an "Moving or adding a HUD part" recipe.
