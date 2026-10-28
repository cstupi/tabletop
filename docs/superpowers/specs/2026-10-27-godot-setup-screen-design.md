# The Godot setup screen

Owner, 2026-10-27: *"things get very lost with the clutter"*. A setup is: add and place terrain, sometimes pick optional rules, then
start. The browser's setup (spec `2026-10-25-play-ui-design.md` → Setup) is the reference; both clients show the same sections in
the same order with the same words.

## What a setup uses

| Control | Use |
| --- | --- |
| Add a built-in piece, drag it, turn it (Q/E), remove it | every setup |
| Random table | most setups |
| Start setup, End setup | every game, once each |
| Optional rules, activation, enforcement, game name | some games, in the lobby |
| Objective markers | some games |
| Clear terrain, Add my terrain | some setups |
| Table size, surface, deployment zones and swap, Load and Save layout | rarely |
| Teams, players and armies | in the lobby; in setup only to check |
| Selected piece: facing, ⟲/⟳, Remove | every piece turned or removed by card |
| Selected piece: scale, mesh swap, my asset, Reset volumes, volume kind, footprint, heights, declarations, notes | rarely |

## Side panel

The side panel is a column 360 px wide: a fixed head and a body that scrolls under it.

- **Head**, never scrolled: the phase (`Lobby` or `Setup`), then the one main action: the creator's **Start setup** in the lobby, **End
  setup** in setup (with the count of pieces off the table it will remove); anyone else reads what they wait for.
- **Body**: sections under headings that fold (▾ open, ▸ closed); a heading carries its count.
  - Setup: **Terrain** (open), **Objectives**, **Table**, **Deployment zones**, **Layouts**, **Game settings**, **Players**, in that order,
    the rest closed. A section opened or closed stays so for the rest of the game screen, across rebuilds.
  - Lobby: **Players** and **Game settings**, both open.
- **Terrain**: a search field, then the built-in catalog grouped by what a piece does (Blocks line of sight, Impassable, Dangerous,
  Cover, Difficult, Open ground: a piece's group is the first of these it carries), each group sorted by name; a click on a row adds that
  piece beside the table; a row's tooltip says what it does. The list scrolls inside a box sized to the window, so the headings below it
  stay in view at 1280×720. Under it: Random table with its planet, Add my terrain…, Clear terrain (asked first).
- Headings: `Terrain 17/64`, `Objectives 0/32`, `Table 72 × 48" · Grass`, `Deployment zones`, `Layouts`, `Game settings`, `Players 2`.
- Players holds the team buttons, the Players list, my army pickers, the team cards and Leave game.

## Terrain card

Shown only while a terrain piece or objective is selected, in the unit card's slot. In setup, top down:

1. The name, then the source and height on one dim line.
2. Facing field, ⟲ 15°, ⟳ 15°, Remove.
3. Scale.
4. **Mesh** (closed): the swap list and Swap, Use my terrain…, Reset volumes.
5. **Volumes** (closed; `Volume` for one, `Volumes 2` for more): each volume's kind, footprint, heights, declarations and notes.

Mesh and Volumes keep their state from piece to piece. In play the card stays read-only as it is.

## Where it lives

- `client/src/Core/`: `TerrainGroups` (the grouped, searched catalog and a piece's words, as the browser's `terrainGroups.ts`),
  `SetupFolds` (each section's heading, order and open state), `SetupHead` (the head's phase, action and note), `SetupLayout` (the
  terrain list's height for a window height).
- `client/src/Panels/`: `SidePanelHead`, `SetupToolbar`, `LobbyPanel`, `Fold` (a heading over a body, over `SetupFolds`).
- Every edit is the one `SetupEdits` or `LobbyEdits` builds today; no new message.
