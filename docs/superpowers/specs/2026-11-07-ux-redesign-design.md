# UX redesign

Design session, 2026-11-07. Every player-facing screen of the Godot client, the web app and the browser viewer, rethought against
the owner's lobby feedback of the same day. The lobby and setup team columns are settled by `2026-11-07-lobby-and-setup-redesign-design.md`
(the mockup the owner approved); this spec makes everything else consistent with it and does not redraw it.

Parts:

| File | Screens |
| --- | --- |
| `2026-11-07-ux-redesign-1-client-shell.md` | Login, Home, Create game, Settings, the setup side panel and tools, the terrain and marker cards, waiting for players, loading |
| `2026-11-07-ux-redesign-2-play-hud.md` | Header, roster, unit card, decision box per Ask, dice tray, log and undo, banners and toasts, game over, replay bar, key overlay, menus |
| `2026-11-07-ux-redesign-3-web.md` | Sign-in, account, armies and the army page, library, upload, offers, download; the viewer's games, lobby, setup, play and replay pages |

Screenshots of today's screens are under `/tmp/claude-1000/ux-audit/` (not committed); each section names its file.

## Principles, in the owner's words

- *"Visuals are worth a thousand words."* Structure is shown: columns, cards, colour bands, icons, layout. A sentence that explains a
  state ("You have no team: join one, or stay without one to watch once play starts.") is replaced by the thing in that state.
- *"The current row of leave team 1 join team x watch create team … is example of BAD ux."* Actions live on the thing they act on: Join
  inside the team's column, Play on the game's card, Undo on the log row, Remove on the piece's card. No row of buttons at the foot of a
  list that act on "the selected row".
- *"use lots of spacing, icons and coloring to make it clear"*: generous spacing, strong hierarchy, the team's colour and icon wherever a
  team is named, few words. A key is a key cap on its button, never `(Enter)` in the label and never repeated in a hint line under it.
- Easy to change: the common action is one obvious click or drag where the thing is; the rare action is behind a `▸` menu on the thing.
- A page of one kind of thing reads the same in all three clients: the Godot client, the viewer and the web app show the same cards, the
  same words, the same icons and the same order (play-ui spec → Principles; lobby spec → Both clients).

## What stays settled

- Rules live only on the server; the client draws what the server sends. Where a redesign wants a fact the wire does not carry, the
  section says so in one line, and the screen degrades to today's words without it.
- `HudLayout` keeps its slots and its no-overlap guarantee from 1280×720 up; the redesign changes what each slot draws, not where it is.
- Themes: Fantasy and Sci-fi stay; every colour is a `Ui` token or a `Variation`, never a literal (`check-client`); every icon is a file
  under `client/content/themes/<id>/icons/` and a CSS token in `web/src/themes.css`. The icon names below are the data keys.
- No invented rule names: action names on tiles are the option labels the server sends, split at the colon the scheduler already prints
  (`Red unit 1: Hold`). Words the rulebook uses (Hold, Advance, Rush, Charge, VP, Shaken, Fatigued) are the words shown.
- UI text stops at the fact. A state line is a noun phrase (`Needs an army`), never a sentence that reassures or instructs.

## Shared vocabulary

Every client draws these the same way. The Godot names are `Variation`s in `UiTheme`; the web's are classes in `app.css`.

| Element | Looks | Godot | Web |
| --- | --- | --- | --- |
| Team band | a 6 px bar in `Ui.Team(ordinal)` on the left edge of a card or row, the team icon, `Team N` | `Variation.TeamBand` | `.team-band` |
| Status chip | a small capsule: lobby (dim), setup (warning), playing (accent), ended (dim) with the status icon | `Variation.StatusChip` | `.chip.status-*` |
| Key cap | the key in `Ui.Numerals`, boxed, right-aligned inside its button | `Variation.KeyCap` | `kbd` |
| Card | a panel with a 16 px inset, a title row, its one primary action at the foot | `Variation.Card` | `.card` |
| Tile | a square or wide button with an icon over a label; a grid of them replaces a column of full-width buttons | `Variation.Tile` | `.tile` |
| Wound pips | `●●○` per model (filled alive), as the roster draws them | `Variation.Pips` | `.pips` |
| Quiet band | a one-line panel in dim text with an icon: what the game waits for, never a control | `Variation.Quiet` | `.quiet` |

Icons (theme data, keys): `team`, `player`, `army`, `watch`, `warning`, `ready`, `editing`, `add` (lobby spec), and new: `round`, `vp`,
`activation`, `wound`, `dice`, `log`, `undo`, `camera`, `key`, `terrain`, `objective`, `table`, `zone`, `layout`, `settings`, `replay`,
`play`, `step`, `turn`, `search`, `remove`, `copy`, `lock`, `unlock`, `menu`, `home`, `sign-out`, `import`, `upload`, `folder`, `offer`,
`download`, `mesh`, `hitbox`, `hold`, `advance`, `rush`, `charge`, `shoot`, `melee`, `cast`, `morale`, `cover`, `difficult`, `dangerous`,
`impassable`, `blocks-sight`, `open-ground`. Each theme draws the set in its own style (engraved line, glowing line).

## Priority

Ordered by how much a player suffers today. S is a small change inside one part; M reshapes a part; L is structural (new Core model, new
nodes, both clients).

| # | Screen | Today | Size | Part |
| --- | --- | --- | --- | --- |
| 1 | Decision box | one stack of full-width buttons per Ask, `(Enter)` in labels, a hint line of keys under them, an Ask for the other team drawn as if it were mine | L | 2 → Decision box |
| 2 | Home (Godot) | a dense text list, status inside the text, six grey buttons at the foot acting on the selected row | L | 1 → Home |
| 3 | Play header | four lines of text; the acting team and the waiting line in the same orange | M | 2 → Header |
| 4 | Setup side panel | a flat `ItemList` of 38 names under grey group headings, Random table beside a planet dropdown, five closed folds of plain words | L | 1 → Setup |
| 5 | Lobby | settled by the lobby spec; the Still to do lines and compact columns carry into setup and waiting for players | — | lobby spec; 1 → Waiting |
| 6 | Replay bar | `«R «T ‹A ‹ Play › A› T» R»` | M | 2 → Replay bar |
| 7 | Roster and unit card | words where pips and icons do; the acting unit not marked; `Red unit 1 · 3/3 models · Wounds 3/3` | M | 2 → Roster, Unit card |
| 8 | Log drawer | `Copy` and `Undo to here` as text on every row; no team colour on a block | S | 2 → Log |
| 9 | Key overlay | a full-screen text table | M | 2 → Key overlay |
| 10 | Web armies and army page | a table with two buttons a row; each unit a panel of one long sentence and two dropdowns | M | 3 → Armies |
| 11 | Login (Godot), Create game, Settings | three equal buttons; a warning before the first keystroke; a flat checkbox list | S | 1 |
| 12 | Viewer play and replay | already a column layout; takes the same cards as the Godot Hud | M | 3 → Viewer |
| 13 | Library, offers, download, account, sign-in (web) | already cards and tiles; words to trim, thumbnails to add | S | 3 |
| 14 | Dice tray, banners, toasts, game over | fine; the shared vocabulary applied | S | 2 |

## Build order

1. The shared vocabulary (`Variation`s, icons, `app.css` classes) and the key cap rule, applied to every existing button.
2. Decision box (part 2), then the header, roster and unit card.
3. Home (part 1), with the viewer's games page as its reference.
4. Setup side panel and tools (part 1), after the lobby spec's compact columns land.
5. Replay bar, log, key overlay (part 2).
6. The web pages (part 3), then the viewer's parity pass.

Each step is its own plan; every Core model it adds is unit-tested, and a Godot node only draws it.
