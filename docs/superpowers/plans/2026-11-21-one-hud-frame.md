# One HUD frame — plan

Spec: `docs/superpowers/specs/2026-11-21-one-hud-frame-design.md`.

## The frame model (`client/src/Core/HudFrame.cs`)

- `HudRow(Part, X, Y, W, H)` with `Beside`, `On`, `In`, `HiddenWhile`, `Gap` (M; 0 for setup's bars), `Overlay`, `Room`, `Always`.
  - `X`: `Left`, `Right`, `Centre` (centred on its container, kept inside the band left free by earlier parts on its rows), `Stretch`.
  - `Y`: `Top` (Gap below every earlier non-overlay `Top` part of its container spanning its columns), `Bottom` (Gap above every
    earlier `Bottom` part spanning it), `Middle`, `Third`.
  - `W`/`H`: `Fixed(n)`, `AtLeast(n)` (the part's measured size, never under n), `Fill(max)` (the room left; a part that measures
    shorter ends at its measured edge).
  - `On`: the stacking neighbour. A `Fill` part splits its room evenly with the later `Fill` parts `On` it (roster/unit card, decision
    box/log panel); a fixed part `On` it is carved from its share (dice tray on the decision box).
  - `Beside`: stands beside that part (left of it when right-anchored); a hidden neighbour passes on to its own `Beside`.
- `HudFrame.Of(w, h, shown, fit, rows)`: `fit(part, slot)` fits the part to its slot and returns its measured size (or null: the slot).
  Result: `Slots`, `Rects` (measured), `Order`, `Obstacles` (shown non-overlay, non-room rects), `Covered(part)`.
- `HudFrame.Play`, `HudFrame.Setup`: the two tables. `HudLayout` is folded in and deleted.

## Tasks

1. `HudFrame` + tests (old HudLayout/HudLayoutSetup tests ported to it; measured-bottom tests; whole pixels).
2. `Session.Frame` + `FrameChanged`; `GameScreen.Layout`/`LayoutSetup` build one frame per pass through `fit` and publish it.
3. Overlays read `Session.Frame.Obstacles`: `RangeRingNode`, `ViolationMarks` (MarkLabels), `UnitActionsNode`, the terrain hover panel
   (`CursorPanel.Place`), `DiceTray.Covered` from `Frame.Covered(Dice)`. Remove `GameScreen.Covered`, `UnitActionsNode.Covered`,
   `AboveBottomStack`, `BelowHeader`, the hand-applied `Below`.
4. Test: moving a part's row (Log/Undo to the bottom left) moves its obstacle for every overlay placement.
5. Fixture `viewer/tests/fixtures/hudFrame.json` (`HudFrameViewerFixtureTests`), port `viewer/src/lib/play/hudFrame.ts`,
   `hudFrame.agreement.test.ts`.
6. Browser play page: one absolutely placed wrapper per part from `frame.rects`; overlays (`placeRect`, rings, mark labels, terrain hover)
   read `frame.obstacles`.
7. "Moving or adding a HUD part" recipe in `client/AGENTS.md` and `viewer/AGENTS.md`; screenshots at 1280×720 and 1920×1080.
