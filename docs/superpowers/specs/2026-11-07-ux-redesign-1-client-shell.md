# UX redesign, part 1: the client shell

Index and principles: `2026-11-07-ux-redesign-design.md`. Screens outside play in the Godot client: Login, Home, Create game, Settings,
the setup side panel and its tools, the terrain and marker cards, waiting for players, loading. The lobby is the lobby spec's.

## Login

Today (`01-login.png`): a title, two fields, a checkbox and three equal buttons in a row, *Sign in · Sign up · Email me a code*. Which one
is the action is not shown; the web's sign-in page already answers this with a segmented choice and one primary button.

Size: S.

```
                 ┌──────────────────────────────────────┐
                 │  ◎ TABLETOP                          │
                 │                                      │
                 │  [ Password ] [ Email me a code ]    │   segmented, as the web
                 │                                      │
                 │  Email                               │
                 │  ┌──────────────────────────────┐    │
                 │  └──────────────────────────────┘    │
                 │  Password                            │
                 │  ┌──────────────────────────────┐    │
                 │  └──────────────────────────────┘    │
                 │  ☐ Remember me on this machine       │
                 │                                      │
                 │  [ Sign in ⏎ ]      Create account   │   primary; the second a flat link
                 │                                      │
                 │  Version 1.2.0             ⚙         │   gear opens Settings (theme, graphics) before sign-in
                 └──────────────────────────────────────┘
```

- *Password* holds email, password, Remember me, **Sign in** (primary, Enter) and a flat **Create account**; *Email me a code* holds the
  email field and **Send code** (primary), then the code field and **Sign in**. Same calls as today (`Session.LoginAsync`,
  `SignUpAsync`, the magic-code pair).
- A failure is a line in failure colour under the field it concerns (`Feedback` words), not a toast.
- The settings gear is reachable here, so a player can pick the theme and Quality before seeing a game.

## Home

Today (`02-home.png`, `11-home-p0.png`): the games as one text list, `name — status, N in (you), date · rule set`, a filter row above it,
and `Join · Replay · End game · Delete game · More · Refresh` at the foot acting on the selected row, grey until a row is picked. Whether
a game is mine, whether it waits for me, and what I can do with it are not visible without selecting it. The viewer's games page
(`v01-games.png`) already separates *Your games* from *Open games* and puts the action on the row; Home takes that further.

Size: L (new `HomeView` in Core: the cards, their state and actions; `HomeScreen` redrawn; the viewer's `GamesPage` takes the same cards).

```
┌ ◎ TABLETOP ───────────────────────────────────────────────  ⚙   👤 Clay ▾ ┐    menu: Rename…, Sign out
│                                                                            │
│  [ + New game ]                          🔍 name          ☐ Ended too       │
│                                                                            │
│  YOUR GAMES                                                                │
│  ┌──────────────────────┐ ┌──────────────────────┐ ┌──────────────────────┐│
│  │▌● PLAYING  ·  3.6.0  │ │▌◐ SETUP    ·  3.6.0  │ │▌○ LOBBY    ·  3.5.1  ││   band in the status colour
│  │ Friday night skirmish│ │ Ruins run            │ │ UX audit             ││   name, display font
│  │ 👤 Clay  👤 Sam       │ │ 👤 Clay              │ │ 👤 Clay  👤 Ann  +2   ││   players, "+N" past three
│  │ ⏳ your move          │ │ 2 days ago           │ │ 1 of 2 teams ready   ││   one state line
│  │ [ ▶ Play ]        ▸  │ │ [ ▶ Continue ]    ▸  │ │ [ ▶ Open ]        ▸  ││   primary on the card; ▸ the rest
│  └──────────────────────┘ └──────────────────────┘ └──────────────────────┘│
│                                                                            │
│  ENDED  (collapsed until "Ended too")                                       │
│  │ ✓ TestAmbush        3.5.1   👤 clay  👤 Sam           19 Oct   [ Replay ]│
│                                                                            │
│  OPEN GAMES                                                                │
│  ┌────────────────────────────────────────────────────────────────────────┐│
│  │ ○ Lobby lsr4        3.6.0   👤 Ann  👤 Ben  +2        23 min   [ Join ] ││   a row per game, the action on the row
│  │ ◐ Setup screen 2    3.5.1   👤 setup-shot             11 days  [ Join ] ││
│  └────────────────────────────────────────────────────────────────────────┘│
└────────────────────────────────────────────────────────────────────────────┘
```

- **Your games** are `GET /v1/games?joined=true` not ended, and those ended in the last seven days (`GameSummary.EndedAt`): a card each, newest first, the status band and icon (`○` lobby, `◐` setup, `●` playing,
  `✓` ended), the rule set as its edition, the players (`ParticipantNames`, three then `+N`), one state line, one primary button and a
  `▸` menu. The primary is **Play** (playing), **Continue** (setup), **Open** (lobby), **Replay** (ended). The menu holds Replay (a
  playing game), End game and Delete game (the creator, asked first, as today), Leave.
- **Open games** are the rest of `GET /v1/games` not ended: rows, **Join** on the row (today's Join). **Ended** (my other ended games) is
  folded until *Ended too*, Replay on the row, and sits after Your games, before Open games. *No games yet* shows only when I have no game
  at all. The *Any status* dropdown, *Created by me* and *Games I'm in* checkboxes go: the sections answer them; the name filter stays.
- The state line: lobby `k of N teams ready` (teams with a player and an army, from the game's rows the list already carries only as
  counts); setup `laying the table`; playing `your move` in warning colour when an open Ask is mine, else `Round N · Team k's turn`; ended
  `Team 1 wins`. Needs server data: `GameSummary` gains `Round`, `ActingTeam`, `WaitingOn` (account ids) and `Outcome`, written by the
  game server as it writes the status today; without them the line is the date.
- The header: the gear opens Settings; the account name opens Rename… and Sign out. *Signed in as* goes.
- Refresh stays as the list's pull: a refresh icon beside the search, and the list refreshes on focus as today.

## Create game

Today (`03-create-game.png`): a dialog of five labelled fields; *A game name is required.* shows in warning before anything is typed;
the army field reads *No army (pick one in the lobby)*. Size: S.

- The name's problem shows after the first keystroke or an attempt, under the field, as the web does (`nameProblem`).
- Rule set, Activation and Enforcement keep their dropdowns; the enforcement's one-line meaning under it (the viewer's `ENFORCEMENT_HINT`,
  the same words).
- *Your army* lists my armies of the set's edition, others disabled with the reason (today's `ArmyOptions`); its empty label is *Choose in
  the lobby*. **Create game** is the primary, Enter.
- Created, the client opens the lobby at once (today: the list refreshes and the row must be found and joined).

## Settings

Today (`04-settings.png`): a theme dropdown, eight checkboxes, a volume slider, then Graphics. Size: S.

```
┌ Settings ──────────────────────────────────────┐
│  THEME     [ ▣ Sci-fi ]  [ ▢ Fantasy ]           │   two swatch tiles in each theme's own colours
│  PLAY      ☑ Animate   ☑ Camera follows   ☑ Turn banner   ☐ Range rings at start │
│  TABLE     ☐ Model hitboxes  B    ☐ Terrain hitboxes  ⇧B                         │
│  SOUND     ☑ On   ━━━━●━━━ 70                                                    │
│  GRAPHICS  Display [Windowed ▾]  Quality [Medium ▾]  Render scale ━━●━ 100 %  ☑ VSync │
│                                                   [ Close ]                       │
└───────────────────────────────────────────────────────────────────────────────────┘
```

Groups with a caption, the keys as key caps, the theme as two tiles. Nothing new in `ClientSettings`.

## Lobby

The lobby spec. From it, three things carry into every screen below: the **Still to do** lines, the **compact team columns**, and the rule
that a team is always named with its band and icon.

## Setup

Today (`06-setup.png` to `10-setup-table-zones.png`): the 360 px column holds *Setup / End setup*, then **Terrain 0/64** with a search
field, a flat `ItemList` of every catalog name under grey group headings (*Sight blocking*, …), a planet dropdown beside *Random table*,
*Add my terrain… · Clear terrain*, then five closed folds of words (*Objectives 0/32 · Table 72 × 48" · Grass · Deployment zones · Layouts ·
Game settings · Players 1*). A piece's group is told only by the heading it scrolls under; what a piece does only by its tooltip. Table
and zones are numbers and dropdowns with nothing drawn.

Size: L (`TerrainTiles` in Core: the catalog as tiles by group with each group's icon and colour; `ZoneDiagram`: the table outline and
its zones as a small drawing; `SetupToolbar` redrawn; the viewer's `SetupTools` takes the same).

```
┌ SETUP ─────────────────────────────── [ End setup ] ┐  fixed head; End setup (creator) with "2 off the table" when it will remove pieces
│ STILL TO DO   Team 2 needs an army · You end setup    │  lobby spec's lines, one per row
│ ▌TEAM 1▐ Clay · Orcs 3.6    ▌TEAM 2▐ Sam · ⚠ no army   │  compact columns (lobby spec), two to a row
├───────────────────────────────────────────────────────┤  the body scrolls from here
│ 🏔 TERRAIN  19/64                                       │
│ ┌───────────────────────────────────────────────────┐ │
│ │ 🎲 Random table          Generic planet ▾        │ │  one primary tile: the common action; the planet inside it
│ └───────────────────────────────────────────────────┘ │
│ 🔍 search                                              │
│  ■ Blocks sight  ■ Impassable  ■ Dangerous  ■ Cover  ■ Difficult  ■ Open    │  the six groups as a legend of colour dots; a dot toggles its group
│ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐            │
│ │ 🌲     │ │ 🏚     │ │ ⛰      │ │ 🧱     │            │  tiles: the group's icon, the name under it,
│ │ Forest │ │ Ruin   │ │ Hill   │ │ Wall   │            │  a dot in the group's colour at the corner;
│ └────────┘ └────────┘ └────────┘ └────────┘            │  hover: its declarations as chips
│ ┌────────┐ ┌────────┐ …  (scrolls inside its box)       │
│ [ + My terrain… ]                         [ 🗑 Clear ]  │  Clear asks first, as today
│                                                        │
│ ◎ OBJECTIVES  3/32                      [ + Objective ] │  a row per marker: ◎ label · (x, y) · ▸ (Rename, Remove)
│ ▭ TABLE  72 × 48" · Grass                            ▸ │  closed: the facts; open: size fields, Set, and the seven prints as swatches
│ ◧ ZONES  Long edges · 12"                            ▸ │  closed: the style; open: a diagram and the style tiles
│ 🗂 LAYOUTS                                           ▸ │  Load (predefined first), Save, my layouts as rows with ▸ (Load, Delete)
│ ⚙ GAME SETTINGS                                      ▸ │  the lobby's Game settings column, read-only for a non-creator; Leave game at its foot
└────────────────────────────────────────────────────────┘
```

- **Terrain tiles.** One tile per catalog entry, grouped as `TerrainGroups` groups them (Blocks line of sight, Impassable, Dangerous,
  Cover, Difficult, Open ground: a piece's first declaration decides), the group's icon on the tile and its colour dot. The legend's dots
  filter; the search field filters by name. A click adds the piece beside the table (today's `SetupEdits`); a **drag from the tile onto
  the table** adds it where it lands (`SetupEdits.Add` at the drop's table point, then the same drag as a placed piece: lifted, lands
  clear, Q/E turn). The box holds four tiles a row at 360 px and scrolls inside itself (`SetupLayout` keeps the height rule).
- **Random table** is the section's primary and holds the planet picker; it sends what today's button sends.
- **Objectives**: a row per marker with its label and position; `+ Objective` adds one beside the table; `▸` renames or removes. A
  selected marker's card (below) carries the same.
- **Table**: closed reads `72 × 48" · Grass`; open holds the width and length fields with **Set** and the seven board prints as swatches
  (each print at 48 px, fetched by hash as the slab fetches it, the packed one in a development run), the current one framed.
- **Zones**: a diagram of the table outline at the section's width with each team's zone filled in its team colour and the style's name
  under it (`SetupWords`); the deployment styles as tiles under the diagram, the swap-sides control as a `⇄` button on the diagram.
  `ZoneDiagram` (Core) gives the outline and each zone polygon in section coordinates from `Session.Zones`, the same legs the table draws.
- **Layouts**: Load lists the predefined tables first (`GET /v1/layouts/builtin`) then mine, each a row with its name and size; Save
  asks the name; a row's `▸` loads or deletes.
- **Game settings** and **Leave game** as the lobby spec places them; the Players fold is gone (the compact columns are in the head).
- Headings keep their counts (`Terrain 19/64`) and the fold rule (`SetupFolds`).

## Terrain and marker cards

Today (`09-setup-terrain-card.png`): the selected piece's card in the unit card's slot: name, `Built-in large-sight-blocker · 0–6" high`,
Facing with ⟲ ⟳ and Remove, Scale, then the Mesh and Volume folds. Size: S.

```
┌▌ Large Sight Blocker ─────────────────────────── 🗑 ┐   a band in the piece's group colour; Remove as the danger icon at the right
│  ■ Blocks sight  ■ Impassable        0–6" high        │   its declarations as chips, the height
│  Facing  ⟲ [ 315° ] ⟳        Scale [ × 1.0 ]          │   ⟲/⟳ 15°, the field types a facing
│  ▸ Mesh      ▸ Volumes 1                              │   folds as today
└───────────────────────────────────────────────────────┘
```

- The band colour and the chips are the piece's group and declarations (`TerrainLabel`, `TerrainGroups`), so a piece on the table reads
  as its tile did. The dim source line (`Built-in large-sight-blocker`) goes into the Mesh fold.
- A marker's card: `◎ Objective 2` with its label field, position, and the danger icon to remove; in play the lock state with its icon
  and Lock / Unlock in place of Remove (`MarkerLocks`).
- In play the card is read-only, as today.

## Waiting for players

Today: the side panel's `WaitingPanel` lists one text line per team with a Join button or an army dropdown, and the decision box repeats
the lines. Size: S once the lobby spec's columns exist.

The decision slot shows the compact team columns (lobby spec → Setup), each column's state band reading *Ready*, *Needs a player* or
*Needs an army*, with Join or my army picker inside the column, under the quiet band `⏳ Deployment waits for every team`. The side
panel is not shown. Same sends (`PickTeam`, `PickArmy`).

## Loading

The curtain and strip (`2026-10-25-loading-design.md`) stay as they are: the words are already facts. One change: the curtain's game
name line carries the status chip and the two team bands, so the first thing a player sees is who plays.

## Update required

Unchanged: the version line and the download button.
