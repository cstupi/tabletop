# Shift-drag of one model, the unit card's pick mark, Home's gear

In-game UX spec (`2026-10-08-in-game-ux-design.md`) → Moves: "Shift (held) | one model: click selects, drag moves". Client UX
spec (`2026-10-10-client-ux-and-themes-design.md`) → Settings: "opened from a gear button in the header and on the Home screen".

## Behaviour

- A Shift press is decided once it travels past the drag threshold or comes up (`ShiftGesture`, `ModelPicks.ShiftPress(onMyModel,
  travelled)` in `client/src/Core/ModelPicks.cs`):
  - released without travelling: `Click`, the toggle as now;
  - travelled, pressed on a model of my team: `HoldOne`, that model alone in hand, the rest of its unit standing
    (`DragBodies.Stationary`), as a drag of a picked model; the picks after it are `ModelPicks.ShiftHeld(unit, model)`: kept when the
    model is picked, else that model alone;
  - travelled from anywhere else (empty table, terrain, another team's model): `Box`, as now.
- Unit card: the row of each group holding a picked model is marked (`ModelGroup.Marked(picked)`; `UnitCardContent.Picked`, so a
  change of picks rebuilds the card). Godot draws the row in a `Variation.Chip` panel (the accent border).
- Home: the Settings button is the theme's `settings` icon (`content/themes/<id>/icons/settings.svg`; both themes have it, drawn for
  the project per each `LICENSES.md`), tooltip "Settings", text only when the theme has no icon, as `HeaderBar`; refreshed on
  `Ui.Changed`.

## Files

- `client/src/Core/ModelPicks.cs`: `ShiftGesture`, `ModelPicks.ShiftPress`, `ModelPicks.ShiftHeld`.
- `client/src/Table/TableInput.cs`, `TableInput.Picks.cs`: the Shift press keeps the press point; `OnMotion` past the threshold asks
  `ShiftPress`, and `HoldOne` starts a drag of the one model from the press (`BeginDrag`, the drag start factored out of `OnLeftDown`).
- `client/src/Core/UnitCardContent.cs`, `client/src/Hud/UnitCard.cs`: `Picked`, `Marked`, the marked row.
- `client/src/Screens/HomeScreen.cs`: the gear.
- Viewer: `viewer/src/lib/play/modelPicks.ts` (`shiftPress`, `shiftHeld`), `viewer/src/lib/board/pointer.ts` (a Shift press on a hit
  offers the drag first, then the box), `viewer/src/lib/play/playDrag.ts` (`onDragStart` with Shift takes only `holdOne`),
  `viewer/src/lib/play/unitCard.ts` (`groupMarked`), `UnitCard.svelte` (the marked row).
- Docs: `client/AGENTS.md` → Picking models, `viewer/AGENTS.md` → the game page.

## Validation

- `tests/Tabletop.Client.Tests/ModelPicksTests.cs`: the gesture for click, box from empty table, box from an enemy model, hold one
  from my model; `ShiftHeld` keeps picks holding the model, starts over otherwise. `UnitCardContentTests`: `Marked`, picks in equality.
- Viewer: `modelPicks.test.ts`, `pointer.test.ts` (a Shift press on a hit the driver drags drags; one it refuses boxes),
  `playDrag.test.ts` (a Shift drag on my model holds it alone; on an enemy refuses), `unitCard.test.ts` (`groupMarked`).
- `dotnet test tests/Tabletop.Client.Tests`, `./scripts/check-client` prints nothing, `cd viewer && npm run check && npm test`.
- On screen, the Linux Godot client against the stack: Shift+drag of one model of a unit moves it alone; Shift+click marks a card
  row; Shift+drag from empty table draws the box; Home shows the gear.

### Results

2026-11-07:

- `dotnet test tests/Tabletop.Client.Tests`: 960 passed. `./scripts/check-client`: no output. `dotnet build client/Tabletop.csproj`:
  no new warnings.
- `cd viewer && npm run check`: 0 errors, 0 warnings; `npm test`: 1694 passed, 6 skipped (112 files, 3 skipped).
- On screen (Linux Godot 4.7.2, a game in round 1 under warn, one account per team, deleted after):
  - Home's Settings is the scifi gear, tooltip "Settings": `/tmp/claude-1000/-home-cstupi-src-tabletop/40149d81-a71f-4e81-bd07-aa9c8f897801/scratchpad/shiftdrag-proof/home-gear.png`, `/tmp/claude-1000/-home-cstupi-src-tabletop/40149d81-a71f-4e81-bd07-aa9c8f897801/scratchpad/shiftdrag-proof/home-gear-tooltip.png`.
  - Shift+click on one Veteran: the model lit, the card's "5/5 Veterans" row framed: `/tmp/claude-1000/-home-cstupi-src-tabletop/40149d81-a71f-4e81-bd07-aa9c8f897801/scratchpad/shiftdrag-proof/shift-click-card-mark.png`.
  - Shift+drag from another Veteran: that model alone in hand with its ruler, the rest of the unit standing:
    `/tmp/claude-1000/-home-cstupi-src-tabletop/40149d81-a71f-4e81-bd07-aa9c8f897801/scratchpad/shiftdrag-proof/shift-drag-one-model-in-hand.png`; dropped, the hand move stands, the others unmoved: `/tmp/claude-1000/-home-cstupi-src-tabletop/40149d81-a71f-4e81-bd07-aa9c8f897801/scratchpad/shiftdrag-proof/shift-drag-one-model-dropped.png`.
  - Shift+drag from empty table draws the box: `/tmp/claude-1000/-home-cstupi-src-tabletop/40149d81-a71f-4e81-bd07-aa9c8f897801/scratchpad/shiftdrag-proof/shift-drag-empty-table-box.png`; on release the models inside are picked:
    `/tmp/claude-1000/-home-cstupi-src-tabletop/40149d81-a71f-4e81-bd07-aa9c8f897801/scratchpad/shiftdrag-proof/shift-box-picked.png`.
