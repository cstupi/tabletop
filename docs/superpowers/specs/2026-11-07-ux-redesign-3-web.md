# UX redesign, part 3: the web app and the browser viewer

Index and principles: `2026-11-07-ux-redesign-design.md`. The web app (`web/`: sign-in, account, armies, library, download) and the
viewer's hosted build at `/viewer/` (games, lobby, setup, play, replay). The viewer is a page of the website (play-ui spec → Principles):
it takes the website's classes and tokens, and after this spec the same cards as the Godot Hud.

## Shell

Today (`w02-account.png`): the header with the logo, Armies, Library, Download, Play, the account name, the theme switch and Log out.
Size: S.

- The header keeps its tabs with their icons. The theme switch becomes the two swatch tiles of the Godot Settings dialog, in a `▾`
  menu on the account name with Account and Log out, so the header holds the tabs and one menu.
- The Fantasy theme (`w10-armies-fantasy.png`, `v06b-lobby-fantasy.png`) and the Sci-fi theme both carry every class below; both are
  checked on each page.

## Sign-in

Today (`w01-login.png`): a card with a segmented *Password / Email me a code*, labelled fields, **Sign in** primary, *Create account*,
*Forgot password* and two sentences explaining the two links. Size: S.

- The two explanation sentences go: *Create account* and *Forgot password* are what they say.
- The Godot login takes this card's layout (part 1 → Login).

## Account

Today: *Display name* and *Delete this account* as two panels. Size: S. The delete panel's four-line explanation becomes two facts:
`Deletes your armies, models, folders, layouts and every game you created.` and the confirm field; the danger button stays.

## Armies

Today (`w03-armies.png`): a wide *Import from Army Forge* panel with a field, a hint, the Import button and a three-line paragraph on
what imports; under it *Your armies* as a table: name, system, edition, units, models, `Re-import` and `Delete` on each row. Size: M.

```
┌ ◎ ARMIES ───────────────────────────────────────────────────────────┐
│  [ 🔗 Army Forge share link or list id ………………………… ] [ ⬇ Import ]      │   one row; the paragraph becomes the field's tooltip
│                                                                      │
│  ┌──────────────────────┐ ┌──────────────────────┐ ┌───────────────┐ │
│  │ 🛡 Voidys    ⟨3.6⟩    │ │ 🛡 Orc Marauders ⟨3.6⟩│ │ 🛡 Elves ⟨3.5⟩ │ │   a card per army: icon, name, edition chip
│  │ 4 units · 13 models  │ │ 9 units · 41 models  │ │ 6 · 22        │ │
│  │ ●●●○ meshes 9 of 13  │ │ ●●●● all meshes      │ │ ○○○○ no meshes│ │   how many slots have a mesh (the army's own rows)
│  │ [ Open ]          ▸  │ │ [ Open ]          ▸  │ │ [ Open ]   ▸  │ │   ▸: Re-import, Delete (asked first)
│  └──────────────────────┘ └──────────────────────┘ └───────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

- `grimdark-future` is not shown as a word: the system's icon on the card and its name in the tooltip (`Grimdark Future`, `Age of
  Fantasy`, the import's two systems). The edition is the chip.
- The meshes line counts slots whose `assetId` is set, from the army rows the page already fetches.

## Army page

Today (`w04-army.png`): the name as a huge field, a line of facts, then a panel per unit: a collapsed heading `REAPER CHAMPION [1] ·
Quality 3+ · Defense 4+`, then `Mesh for every model in Reaper Champion [All folders ▾] [— none (draws the hitbox) — ▾]`. The page is
read top to bottom to find a unit; every picker repeats the unit's name in a sentence. Size: M.

```
┌ ‹ Armies     🛡 VOIDYS  ⟨3.6⟩  Army Forge ↗                                 [ ↻ Re-import ] ┐
│  4 units · 13 models · meshes 9 of 13                 Mesh folder [ Starter ▾ ]              │   the folder once for the page (today's shared choice)
│                                                                                              │
│  ┌▌ Reaper Champion ─────────────────────────────────────────────── 🎯 3+  🛡 4+ ─────────┐ │
│  │ [ ▣ ] Reaper Champion     Figure, 32mm round ▾   base ◯ 32 mm   height 1.9"            │ │   a row per slot: thumbnail, name, mesh picker, base, height
│  └──────────────────────────────────────────────────────────────────────────────────────────┘ │
│  ┌▌ Flayer Cavalry ×5 ───────────────────────────────────────────── 🎯 3+  🛡 4+ ─────────┐ │
│  │ [ ▢ ] every model         — none (draws the hitbox) ▾   base ◯ 40 mm   height 2.4"     │ │   one row when every model shares a slot
│  │   ▸ per model                                                                            │ │   opens a row per model
│  └──────────────────────────────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

- The name is a heading with an edit icon, not a field. The facts line and the Army Forge link stay; *Stats, weapons and rules are the
  list's; meshes, bases and heights are yours* becomes the page's tooltip.
- A unit is a card with its band (the army's colour is not a team's: a neutral band), its name, count and the two stat badges with
  icons. Each slot is a row: a thumbnail of the chosen mesh, the mesh picker (the library folder chosen once for the page), the base
  picker and the height, as the `PatchArmyRequest` builders already patch them. Needs server data: an asset thumbnail (`GET
  /v1/assets/{id}/thumbnail`, rendered once at upload from the GLB the browser already previews); without it the row shows the `mesh`
  icon.

## Library

Today (`w05-library.png`): a folder tree on the left (All assets, New folder, Starter, four icon buttons), the asset grid on the right
with *Add STL / GLB / texture*, Kind and Made-for-base filters, `Select all 6` and a sentence on how to multi-select, tiles of
`Figure, 25mm round · miniature · 25 mm round` with a checkbox, and a usage line. Size: S.

- The multi-select sentence goes: the checkbox on each tile and *Select all* are the affordance; the long-press and Shift range stay.
- Each tile draws the asset's thumbnail (the same server data as the army page), its name and its kind icon; `miniature · 25 mm round`
  becomes the base icon and `25 mm`.
- The tree's four icon buttons get tooltips with the words (Add, Rename, Move, Delete) and the danger colour on Delete; a folder row's
  count stays.
- The usage line becomes a thin bar `━━━━━━━━░░░ 155 KB of 1 GB · 6 of 500` under the grid.

## Upload

The dialog (`UploadDialog`) keeps its stages (hash, create, put, confirm, volumes) and its preview. Size: S: the Scale and Orientation
fieldsets take the `tile` style for the up-axis and unit choices (mm / cm / m as tiles, Y up / Z up as tiles), and the stage list draws
as a vertical stepper with the `ready` icon on done stages.

## Offers

Today (`w06-offered.png`): *Offered to you* and *Sent* tabs; empty states as a sentence. Size: S. An offer is a card with the sender's
`player` icon and name, the assets as thumbnails, Accept (primary, with the folder picker), Decline, Block in a `▸`; the sent tab's
state as a status chip (`waiting`, `accepted`, `declined`) with Withdraw on a waiting one. The empty state is the `offer` icon and `No
offers`.

## Download

Today (`w08-download.png`): a panel with the download icon and `No client build is published yet.` (dev). Size: S. With a build: the
detected platform's download as the primary tile, the others as flat tiles, the first-run steps as a numbered list with the `warning`
icon on the unsigned-build step. Unchanged otherwise.

## Viewer

The hosted viewer is the browser's play client, so every play screen of part 2 and the lobby and setup of part 1 apply to it with
the website's classes. What differs from the Godot Hud is only where the parts sit: columns beside the board instead of slots over the
table.

**Games** (`v01-games.png`): already *Your games* and *Open games* with a status chip, the players and one primary action. It takes
part 1 → Home's cards (status band, edition, players, state line, primary, `▸`) so the two clients list games the same way; the
viewer's `GamesPage` is the reference implementation the Godot `HomeScreen` copies.

**Lobby** (`v06-lobby.png`): the lobby spec's columns (`TeamColumns.svelte`); the Game panel stays on the right with Start setup at its
foot.

**Setup** (`v02-setup.png`): the sidebar takes part 1 → Setup: Still to do and the compact columns at its head, the terrain tiles with
the group legend and drag-to-table (HTML drag and drop onto the canvas, `placeBuiltin` at the drop point), Random table as the primary
tile, the zone diagram (`ZoneDiagram` ported), the board print swatches; the selected piece's card as part 1's terrain card.

**Play** (`v03-play.png`): the header takes part 2 → Header's tiles (today the teams are already tiles with a band: they gain the
activation dots and the turn mark; the waiting line takes the band and the hourglass). The roster takes the sort and the acting mark.
The right column: the decision card (part 2 → Decision box, the same modes, tiles and key caps as `kbd`), then the unit card, then the
log with hover icons and the dice faces; Undo with its key cap. A board click, drag and the menus are unchanged.

**Replay** (`v04-replay.png`): the playback bar takes part 2 → Replay bar: the labelled groups, the round ticks on the scrubber, the
caption as the position's block header; the header `Replay` chip and the team tiles.

## Where it lives

- `web/src/app.css`: `.team-band`, `.chip.status-*`, `kbd`, `.card`, `.tile`, `.pips`, `.quiet`; `web/src/themes.css`: the icon tokens
  per theme (`--icon-<name>` as `url()` of the SVG under `static/themes/<id>/icons/`, the same files as the client's, copied by the
  build). `web/src/lib/components/Icon.svelte` draws by name.
- `web/src/routes/armies/+page.svelte` (cards), `armies/[id]/+page.svelte` (unit cards, slot rows, the page folder), `library/` (tiles
  with thumbnails, the usage bar), `library/offered`, `library/sent` (offer cards), `login/+page.svelte` (the two sentences removed),
  `account/+page.svelte`.
- Management: `GET /v1/assets/{id}/thumbnail` and the thumbnail's write at upload confirm (the one server change this part needs);
  `GameSummary`'s `Round`, `ActingTeam`, `WaitingOn`, `Outcome` (part 1 → Home).
- Viewer: `games/GamesPage.svelte` (cards), `lobby/` (lobby spec), `setup/SetupTools.svelte`, `setup/terrainTiles.ts` and
  `zoneDiagram.ts` (ports of the Core models), `play/HeaderBar.svelte`, `Roster.svelte`, `DecisionBox.svelte`, `UnitCard.svelte`,
  `CombatLogPanel.svelte`, `replay/ReplayBar.svelte`, `play/KeyOverlay.svelte` (new, the port of `KeyOverlayView`). Each port has its
  test beside the Godot Core test it mirrors; `calls.agreement.test.ts` stays green because no send changes.
