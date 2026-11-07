# UX redesign: the play Hud (header, roster, unit card, log and undo, dice tray, banners, game over)

Spec: `docs/superpowers/specs/2026-11-07-ux-redesign-2-play-hud.md` (Header, Roster, Unit card, Dice tray, Log and undo, Banners,
Game over). Not here: the decision box, the replay bar, the key overlay, the terrain card (other plans). Nothing new on the wire.

## Tasks

1. Icons, both themes, white line art tinted at draw time (`client/content/themes/<id>/icons/`): `round`, `turn`, `wait`, `activation`,
   `hold`, `advance`, `rush`, `quality`, `defense`, `dice`, `log`, `undo`, `copy`, `home`; `UiThemes` icon list and `UiThemesTests` counts.
2. `client/src/Hud/HudBits.cs`: the Hud's shared drawing (icon, team band bar, key cap, pips label, a button with icon, word and key cap).
3. `HeaderView` (test first): `Tiles` (one `TeamTile` per team: score, units in play, units acted, acting, mine), `RoundText`,
   `ActionIcon` (the action word to `hold`/`advance`/`rush`/`charge`, else `activation`), `Wait` (`WaitLine`: mine → `Your move`; else the
   first waiting team's ordinal and `Team 2 · remove casualties`). `HeaderBar` redrawn: round, tiles with band, VP numeral, dots or `k/N`,
   `▶` turn mark and brighter border on the acting team, `player` icon on mine, objectives, gear; the acting unit with the action's icon and
   the step strip; the waiting line with its band, or `▶ Your move` in my team's colour; after the end `✓ Game over · Team 1 wins`.
4. `RosterView` (test first): rows sort may act, activated, off the table, each by name; `RosterRow.Acting`; `RosterTeam.Mine`.
   `Roster` redrawn: team header band with icon, `★ you`, collapse chevron; rows with `▶` on the acting unit, `○`/`✓`/`—`, pips,
   condition icons, a `▸` on hover opening the roster menu.
5. `UnitCardContent`/`ModelGroup` (test first): `Pips` per model, `CountNote` (`5 models · 2 wounds left on Sgt`), a group's pips, name
   and wound pips; `ChipText` for the activated chip. `UnitCard` redrawn: band with the name alone and the eye, pips and note, stat
   badges with `quality`/`defense` icons, state chips with icons, model rows with pips; the enemy strip takes the enemy's band.
6. Log (test first: `CombatLogRows.Band`): each block's band in the actor's team colour; Copy and Undo to here as `copy`/`undo` icons
   shown on hover (always on a touch screen), words in the tooltip; the panel's Copy the `copy` icon; dice faces framed pass/fail.
   `UndoButton` and the log toggle: icon, word, key cap (`↶ Undo ⌃Z`, `≡ Log G`); the undo dialog `[ Undo ]` primary, `[ Cancel ]` flat.
7. `DiceTray`: the `dice` icon and the title, faces at 40 px framed pass/fail, the result in the outcome's colour.
8. `TurnBanners` (test first): a turn banner carries the team's ordinal; `TurnBanner` draws the band and the team icon.
9. `GameOverView` (test first): `Rounds`, `WaitingFor` (the teams yet to answer). `EndMessage`: `✓ GAME OVER` / `END OR CONTINUE?`, the
   result with the rounds, the header's tiles larger, the reason, `[ Stay ]` `[ ⌂ Leave ]`, `Waiting for Team 2` with its band.
10. Viewer parity (`viewer/src/lib/play/`): port each Core change with its test (`headerView.ts`, `rosterView.ts`, `unitCard.ts`,
    `logRows.ts`, `gameOverView.ts`) and draw the same in `HeaderBar`, `Roster`, `UnitCard`, `CombatLogPanel`, `DiceTray`, `UndoButton`,
    `GameOverModal`.
11. Docs: `client/AGENTS.md`, `viewer/AGENTS.md` to the current behaviour.

## Validation

| Check | Pass |
| --- | --- |
| `dotnet build` | 0 errors |
| `dotnet test tests/Tabletop.Client.Tests` | all pass |
| `./scripts/check-client` | prints nothing |
| `cd viewer && npm run check && npm test` | 0 errors, all pass |
| Screenshots at 1280×720 and 1920×1080, Sci-fi and Fantasy, of a game in play and an ended game | header, roster, card, log, tray, game over as the spec's mockups; no Hud part overlaps |

Before: `/tmp/claude-1000/ux-audit/12-play.png`, `13-play-unit-card.png`, `14-play-log.png`, `17-undo-dialog.png`, `19-ask-2.png`,
`29-ended-game.png`.

## Results

| Check | Result |
| --- | --- |
| `dotnet build` | 0 errors |
| `dotnet test tests/Tabletop.Client.Tests` | 1049 passed |
| `./scripts/check-client` | empty |
| `cd viewer && npm run check && npm test` | 0 errors; 1814 passed, 6 skipped |
| Screenshots | Godot: Sci-fi 1280×720 in play with a unit selected and the game over modal, Fantasy 1280×720 and 1920×1080 with the log open; viewer: Sci-fi 1280×720, Fantasy 1920×1080. Header, roster, card, log, tray, undo and log buttons and game over as the mockups; no part overlaps. |

Not done: the viewer's unit card has no eye (the board has no camera to centre); the viewer has no per-activation Copy (none before); toasts
and floating results keep their look (outside this plan's files).
