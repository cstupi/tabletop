# Setup screen layout

Owner, 2026-11-21: *"The big issues are the client … how the setup is organized. Its so information dense and so hard to navigate."*
*"The entire UI has been gone over but it is still well below the bar for organization and usability."* *"Look at typical video game
UI's and game setups for inspiration."* Godot client only. Keeps the team columns (`2026-11-07-lobby-and-setup-redesign-design.md`, owner
approved) and their behaviour; changes where things sit. Spacing follows `2026-11-21-spacing-rules-design.md`.

## Problems this replaces

- One 360 px column holds a head (phase, End setup, Still to do, a line per team, the compact team columns) and seven folds under it
  (Terrain, Objectives, Table, Zones, Layouts, Game settings, the selected piece's Mesh and Volumes). Everything is visible or one fold
  away at once, so nothing stands out, and the terrain list shrinks with the head.
- Teams show twice (head lines and compact columns). Leave game is in three places. Game settings sit in the lobby and again in a closed
  fold at the bottom of setup. No help line for the setup keys.

## Approaches

- **Editor modes (chosen).** The pattern of RTS and city-builder map editors and Tabletop Simulator's tools: a slim tool rail, one tool's
  panel open at a time, the table as large as possible, a top bar for the game's state and its one primary action, a key hint bar at the
  foot. Each panel holds one job, with room to breathe.
- Wizard steps (Table → Terrain → Objectives → Zones → Done): clear order, but setup is edited together by several players in any order,
  and the server has no step; a wizard would hide tools others are using.
- Tidy the one column (spacing, fewer words): keeps the density the owner calls the problem.

## Layout

```
┌────┬──────────────────────────────────────────────────────────────────────────────┐
│ ⌂  │ Game name  [SETUP]   ● Team 1 ✓  ● Team 2 ⚠      2 to do ▾      [ End setup ] ☰ │  top bar
├────┼──────────────┬───────────────────────────────────────────────┬───────────────┤
│ ▦  │ TERRAIN      │                                               │ Selected piece│
│ ◎  │ [Random ▾]   │                                               │ (terrain card)│
│ ▭  │ [search    ] │                  the table                    │               │
│ ⇄  │ ● ● ● legend │                                               │               │
│ ▤  │ tile tile    │                                               │               │
│ ⚑  │ tile tile    │                                               │               │
│ ⚙  │ ...          │                                               │               │
├────┴──────────────┴───────────────────────────────────────────────┴───────────────┤
│  Drag  place · Q E  turn · Del  remove · Right-click  more              (key caps)│
└───────────────────────────────────────────────────────────────────────────────────┘
```

- **Tool rail** (left edge, 56 px): icon buttons with tooltips, top to bottom: Terrain, Objectives, Table, Zones, Layouts, Teams, Game.
  One is open; clicking the open one closes its panel and gives the table the width. The last open tool is remembered per session.
- **Tool panel** (beside the rail, 360 px): the open tool's heading and its controls only, as today's fold bodies (same controls, same
  wire). Terrain's list takes the full panel height. A small badge on a rail icon carries a count where it helps (objectives placed).
- **Teams tool:** the compact team columns, Watching, + Team (unchanged behaviour). The head lines and columns no longer sit above every
  tool.
- **Game tool:** the game settings (same controls as the lobby's Game settings; creator-only enabled). One place in setup.
- **Top bar** (across the top, right of the corner's Home): game name, status chip, one chip per team (team colour, state icon: ready /
  needs a player / needs an army; click opens Teams), **Still to do** as a count that opens its lines in a dropdown (none: hidden), the
  creator's **End setup** (primary, with the pieces off the table it will remove, as today), and **☰** the game menu.
- **Game menu** (☰, and Esc when nothing else takes Esc: no text focus, no open overlay, no open Ask's Cancel, no selection, held piece,
  picked models or measurement): Resume, Options, Leave game, Home. In lobby, setup and play, replay and watching too; in play ☰ takes the
  header's gear place (Options is in the menu). Leave game only for a seated player who is not watching. The only Leave game in a game; the
  right-click table menu and the LobbyPanel foot lose theirs. The Home ▸ menu keeps Leave.
- **Selected piece:** the terrain card stays in its right-hand slot; its Mesh and Volumes folds stay on the card.
- **Key hint bar** (bottom, one line, quiet band, key caps per the shared vocabulary): the setup keys; hidden while text has focus.

## Lobby

Keeps the team columns across the page and the Game settings column on the right. Game settings gains its own grouping: **Game** (name,
rule set, activation, enforcement) then **Optional rules** (preset, options), with the spacing scale's section gaps; Apply sits at the
column's foot. Leave game moves to the top bar's ☰ (same top bar as setup, without End setup: the creator's **Start setup** takes its
place).

## Where it lives

- `client/src/Core/SetupTools.cs` (Godot-free, unit-tested): the tool list, order, icons, which one is open, badges; replaces the fold
  order for setup (`SetupFolds` keeps the card's Mesh and Volumes folds). `TopBarView` (Core, unit-tested): chips, to-do count, primary
  action per phase and role.
- `client/src/Panels/SetupRail.cs`, `SetupTopBar.cs`, `GameMenu.cs`, `KeyHintBar.cs`; `SetupToolbar.cs` splits into one file per tool
  (`SetupTerrainTool.cs`, … each under 500 lines); `SidePanelHead.cs` goes (its parts move to the top bar and Teams tool).
- `HudLayout`: setup slots for the rail, panel, top bar and hint bar; no overlaps from 1280×720 up (unit-tested as today).

## Left to the builder

Icons for each tool (theme icon keys `terrain`, `objective`, `table`, `zone`, `layout`, `team`, `settings` exist in the vocabulary; add any
missing to both themes), the hint bar's exact keys, the badge rules beyond the objective count.
