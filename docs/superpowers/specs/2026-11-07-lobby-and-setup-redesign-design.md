# Lobby and setup: team columns

Owner, 2026-11-07, the handoff item: *"Redesign lobby and setup so it is clearer and better UX when multiple people are doing game setup: see
who is on each team, each player's army, and who still has to act, at a glance."* and *"The initial game configuration screen doesnt need to
be limited to the side bar since the screen is black before it goes to setup."*

Rulings, 2026-11-07:

- *"The game setup screen should have vertical columns for each team with the user and their army listed underneath it. use lots of spacing,
  icons and coloring to make it clear which team people are on and make it easy to change."*
- *"it needs to be columns listing everyones names. Visuals are worth a thousand words. The current row of leave team 1 join team x watch
  create team. jesus christ that is example of BAD ux"*
- The mockup below: *"Yes that is MUCH better"*.
- *"you removed my single player ability in the client where a user could be on two teams.. why?"*: one account playing several teams is
  the obvious path. A column's Join adds a seat; it never moves the one I hold.

```
  ┌──────── TEAM 1 (red) ───────┐  ┌──────── TEAM 2 (blue) ──────┐  ┌──── WATCHING ────┐
  │  ⚑ icon, colour band header  │  │  ⚑ icon, colour band header  │  │  👁               │
  │                              │  │                              │  │  name            │
  │  👤 Clay  (you)              │  │  👤 Sam                      │  │                  │
  │     🛡 Orc Marauders · 3.6   │  │     🛡 Battle Brothers · 3.6 │  │                  │
  │                              │  │                              │  │                  │
  │  👤 Alex                     │  │  ⚠ no army yet               │  │                  │
  │     🛡 [pick army ▾]         │  │                              │  │                  │
  │                              │  │                              │  │                  │
  │     [ Join ]                 │  │     [ Join ]                 │  │   [ Watch ]      │
  └──────────────────────────────┘  └──────────────────────────────┘  └──────────────────┘
                                   [ + Add team ]   (creator)
```

## Problems this replaces

Screenshots of the Godot lobby and setup before the change, 2 and 4 players, 1280×720 and 1920×1080:

1. A row of buttons, *Leave team 1 · Join team 2 · Join team 3 · Watch · Create team*, is how a player changes team; a held team reads
   "Team 1: Ann, Ben · ask the creator to add you" as a disabled button.
2. Who is on which team is told three times: the seat sentence ("You are on Team 1. Teammates: Ben."), the Players list (a team dropdown per
   name) and the team cards under them. None shows the team's colour.
3. A team with no army reads "Ben · no army yet" inside a card; nothing says which teams still lack a player or an army, or who acts next.
4. The lobby fills the window with one 1900 px column: Start setup is a button the width of the screen, the Game settings fold sits under
   the team cards, below the fold at 1280×720.
5. In setup the teams are the last fold, closed: who plays which side is out of sight while the table is laid.

## Lobby

The lobby has no table (commit 660288cf): the page is the whole window under the corner's Home line.

- **Head**, fixed: `Lobby` and beside it the creator's **Start setup** (a button of its own width); then **Still to do** on one wrapping
  line (below). The corner keeps Home and the game's name.
- **Team columns**, the left of the page, side by side, wrapping onto a second row when the window holds fewer: one column per team by
  ordinal, then **Watching**, then the creator's **+ Add team**, a column-sized button (`CreateTeam`; only the creator adds teams here).
- **Game settings**, a column 380 px wide on the right: the rule set line, name, rule set, activation, enforcement, optional rules, Apply,
  then Leave game. Everyone sees it; only the creator's controls are enabled (unchanged).

### A team column

- A header band in the team's colour (`Ui.Team(ordinal)`), the team icon, `Team N`, and the team's state: **Ready** (a player and an army),
  **Needs a player**, **Needs an army** (players, none with an army; `WaitingForPlayers`' rule, read off the lobby rows). The column's left
  edge and border are the team's colour.
- Its players, one row each with generous spacing: the player icon in the team's colour, the name, `(you)` on my rows (lit faintly in the
  accent colour; compact columns drop `(you)`, the lit row says it); under the name the army icon and `Army name · 3.6` (the edition is the game's: the server refuses and clears an army of
  another edition), or *no army yet*, in the warning colour when the team has no army, dim when a teammate brings one. My own row holds my
  army picker in place of the army line; holding several teams I have a row, and a picker, in each of their columns (`LobbyView.Armies`).
- At its foot one button, **Join**, in a column I am not in: `PickTeam(team)`. It takes the team for my teamless row, else adds a seat for
  my account on it (one account playing several sides, game-flow spec → One account, several teams); it never moves a seat I hold. A team
  another account holds shows Join disabled, "Ann holds this team: the creator adds players to it" as its line, for everyone but the creator.
- A row I may move (the creator: every row; anyone: their own) carries a **▸** menu, *Move to Team N* for each column it may go to, then
  *Watch*, and can be dragged onto another column. Both send `PickTeam(target, participant)`, `PickTeam(null, participant)` for Watching; a
  target the server would refuse (another account's team for a non-creator, a team the account already holds) is not offered and refuses the
  drop. On my own row while my account holds several teams, *Leave Team N* (`LeaveTeam(team)`) takes Watch's place, in the menu and as the
  drop on Watching: it gives up that one seat.

### Watching

Every participant without a team, the eye icon, a faint band. A row here shows an army line only when it has an army, or on my row with
my picker. Its button is **Watch** for a player holding a team (`PickTeam(null)`). "Watches once play starts" is its state line.

### Still to do

Lines from the lobby rows, none new on the wire, in this order:

- no teams: "Add a team for each side" (creator) or "Ann adds the teams";
- each team by ordinal that needs a player: "Team 2 needs a player";
- each team that needs an army: "Team 1 needs an army";
- the phase's step: "You start setup" / "Ann starts setup"; in setup "You end setup" / "Ann ends setup", after "Anyone may edit the table".

Ann is the creator's name from the participant rows, "the creator" once they left. The lines are a list, not a gate: setup starts and ends
whatever they say, and deployment waits for players as it does today (game-flow spec → Waiting for players).

## Setup

The table stays, with the 360 px side column. Its fixed head: `Setup`, the creator's **End setup** (with the pieces off the table it will
remove), Still to do, then the **same columns, compact**: two to a row, the team band with its state as an icon after the title (the words
in its tooltip), each name with its army line (my picker only while my seat has no army), the ▸ menu and drag, Watching only while someone
watches, and the creator's **+ Team**. A team column I am not in carries **+** in its band, Join's add-a-seat (disabled with Join's line as
its tooltip where Join is); no Watch button. A player whose
presence selects or holds a piece shows the editing icon beside their name in Godot (`Presence.Selection`, `Presence.Placements`, already
sent); the browser keeps no presence and shows none. The folds
under it: Terrain, Objectives, Table, Deployment zones, Layouts, Game settings (Leave game at its foot). The Players fold is gone. The terrain
list's height subtracts the head's height (`SetupLayout.TerrainListHeight(window, head)`).

## Both clients

The browser's lobby and setup show the same columns with the same words, icons and order (viewer `lobby/teamColumns.ts`, a port of
`TeamColumns`; `TeamColumns.svelte`, `TeamIcon.svelte`). The browser's drag uses HTML drag and drop; the ▸ menu is a `<select>` of the same
targets. The lobby page's head holds Start setup (disabled while the settings form has unapplied changes) and Still to do; setup's sidebar
opens with Still to do and the compact columns; the header strip's wait reads "Ann ends setup".

## On the wire

Nothing new. Join, Move and Watch are `PickTeam`; Leave Team N `LeaveTeam`; Add team `CreateTeam`; the army picker `PickArmy`. The server
already accepts `PickTeam(team, own seat)` from a non-creator onto a team no other account holds. The calls fixture's cases are `columnJoin`
and `columnMove` (Watching's target is `LeaveTeam` on my row while I hold several).

## Where it lives

- `client/src/Core/TeamColumns.cs`: `TeamColumns.Of` (columns, members, state, Join, my army seat per row), `TeamColumns.Moves` (a row's
  targets, Leave Team N), `TeamColumns.MoveTo` (a drop), `TeamColumns.StillToDo` (`ToDoLine`), `TeamColumns.Editing` (presence); `SetupHead` carries the
  lines, `SetupLayout.TerrainListHeight(window, head)`.
- `client/src/Panels/TeamColumnsPanel.cs` (the columns, wide or compact; drag and drop), `SidePanelHead` (phase, action, Still to do, the
  compact columns in setup), `LobbyPanel` (Game settings and Leave game). `TeamPanel` goes.
- `client/src/Theme/Ui.cs`: `Ui.TeamColumn(ordinal, highlighted)`, `Ui.TeamBand(ordinal, compact)`, `Ui.MemberRow(mine)` style boxes and
  `Ui.OnBand`; icons `team`, `player`, `army`, `watch`, `warning`, `ready`, `editing` in each theme's `icons/`, drawn white and tinted.
- Viewer: `lobby/teamColumns.ts`, `lobby/TeamColumns.svelte` (replacing `TeamCards.svelte`), `LobbyPage.svelte`, `setup/SetupTools.svelte`.
