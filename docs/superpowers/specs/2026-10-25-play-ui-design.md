# The play UI

Owner, 2026-10-25, on the browser viewer: *"The dev viewer ui seems completely broken and has horrific ux. get rid of the paste
jwt thing. The play button doesnt work on [a replay] its in a weird spot. You have design skills to build a usable and clean UI.
DO it"*.

## Principles

- **A page of the website.** The viewer loads the website's `themes.css` and `app.css` (`viewer/src/main.ts`) and its header
  (`shell/SiteHeader.svelte`). Use the site's classes and tokens only: `.panel`, `button` / `.button` with `.primary`, `.danger`,
  `.quiet`, `.small`; `.seg`; `.chip`; `.empty`, `.loading`, `[role='alert']`, `.notice`; `h1`/`h2`; `--space-1`…`--space-6`, `--text`,
  `--dim`, `--accent`, `--rule`, `--panel`, `--panel-solid`, `--success`, `--warning`, `--failure`, `--font-display`, `--font-num`.
  No hard-coded colours outside the board's canvas. Both themes (Sci-fi, Fantasy) must read well; check both.
- **An app that fits the window.** `main.viewer` fills the space under the header; a page never scrolls the window. Board pages
  are a grid: the board takes what is left, side columns scroll inside themselves, bars never move. Nothing overlaps the board
  except small, deliberate overlays (a hover label, a banner).
- **One obvious next action.** Each state shows its main action as the one `.primary` button. Secondary actions are plain
  buttons; destructive ones `.danger`; minor ones `.quiet`.
- **Words, not ids.** Never show a raw id, an event kind, a declaration's snake_case name or a log line meant for developers.
  Units and models by name (`play/names.ts`), terrain by its catalog name, declarations as words ("Blocks line of sight").
- **The board reads as a tabletop.** Models are their bases in their team's colour (a round base a circle, an oval or a rectangle its outline at its facing, so bases that touch never look overlapped); terrain is filled shapes by kind (a soft fill
  and an outline), labelled with its name only when zoomed in enough or selected; deployment zones as a tinted band; objective
  markers as clear discs. Details (declarations, notes) live in the hover label and the cards, not on the board.

## Layouts

**Games** (built, `games/GamesPage.svelte`): a document page; *Your games* and *Open games* panels of rows, each with its
status chip, players, age and one main action.

**Lobby**: a header strip (back to Play, game name, status chip, Leave). Left, the teams: a card per team with its players
and their armies, a Join button on a team with room, the creator's placing controls. Right, a *Game* panel: name, activation,
enforcement, optional rules as labelled controls, and the creator's **Start setup** as the primary action at its foot. A
non-creator sees the settings read-only and what they are waiting for.

**Setup**: header strip as the lobby's, with **End setup** as the primary action for the creator. Left, a tools sidebar
(~18rem): Table, Terrain (search box over the catalog, grouped), Markers, Zones, Layouts (predefined first, random by planet,
saved), each a collapsible section. Centre, the board. Right, the selected piece's card when something is selected.

**Play**: a top bar over the board (round, whose turn, each team's score as `3 VP` and, from round 1, `k not activated`). Left, the roster, collapsible.
Centre, the board. Right column (~22rem): the decision box at the top, prominent, styled as the panel that matters; then the
unit card; then the log, which takes the rest and scrolls. Undo and dice sit with the log.

**Replay**: the play page read-only, from the same parts: top bar, board with names and bases, the unit card on selection,
the combat log (`play/combatLog.ts` words). A playback bar fixed at the foot: step back and forward by round, turn and action,
Play/Pause, speed (0.5–8×), a scrubber with the position, all always visible. Play must work.

**Watch and Tests** (admins and developers): the developer tools; tidy them with the same classes, no redesign needed.
