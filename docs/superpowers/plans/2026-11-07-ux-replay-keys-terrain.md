# UX redesign: replay bar, key overlay, menus, terrain information

Spec: `docs/superpowers/specs/2026-11-07-ux-redesign-2-play-hud.md` → Replay bar, Key overlay, Menus; part 1 → Terrain and marker cards
(play, read-only). Owner, 2026-11-07: "We also want to have clearer terrain tooltips when they show up. right now they are hard to read.
Also when terrain is SELECTED during play it should better show the effects of status effects."

## Tasks

1. Server: `IGameRules.TerrainRules`, `GfEdition.TerrainRules` (one sentence per declaration of what it does to a unit, 3.5.1 pp.11–12 and
   3.6.0 p.12, no page in the text), sent in `Welcome.TerrainRules` and `RulesOptionsMessage.TerrainRules`; `Session.TerrainRules`;
   `StubRules` forwards it. Tests: `GfTerrain36Tests.Terrain_rules_follow_the_edition_and_name_every_declaration`, the welcome and lobby
   edition tests, `SessionTests`. Viewer types regenerated.
2. Core `TerrainEffects`: the one declaration → icon and colour mapping (`All` in the label's order, `Open`, `Primary`, `Rows`).
   `TerrainLabel.HoverOf` (name, height, one chip per effect), `TerrainCardContent.Effects` and `TerrainCardVolume.Colour`;
   `HitboxLook.Selected` (the selected piece draws its volumes at full strength in setup and play); `Palette.ForDeclarations` reads
   `TerrainEffects.Primary`.
3. Core `ReplayBarView` (labelled buttons, round ticks, caption = latest live block header) and `KeyOverlayView` (Now · the Ask, Camera,
   Measure, Table); `MenuItem.Icon` on lock, unlock, remove, transport and undo items.
4. Icons, both themes: `skip-back`, `step-back`, `step`, `skip`, `play`, `pause`, `key`, `camera`, `measure`, `table`, `cover`, `difficult`,
   `dangerous`, `unstable`, `steep`, `impassable`, `blocks-sight`, `see-in-out`, `open-ground`, `lock`, `unlock`, `remove`, `undo`;
   `UiThemes` list and counts. `Variation.KeyCap`; `Ui.EffectChip`, `Ui.EdgeBand`, `Ui.FloatingPanel`.
5. Godot nodes: `HoverLabels` terrain panel beside the cursor in a layer over the Hud; `TerrainCard` play view (band, effect rows,
   sentences); `ReplayBar` (icon buttons with words, ticks, caption); `KeyOverlay` (columns of groups, key caps, `KeyOverlay.Request` for
   the decision card's ⌨, any key closes); `PieceMenu` item icons; `HudBits` (icon, key caps, effect chip).
6. Viewer: `terrainEffects.ts` (palette reads it), `terrainHover.ts` + `TerrainHoverPanel.svelte`, play `TerrainCard.svelte` with effects,
   board fill of the selected piece, `ConnectionState.terrainRules`, `replayBarView.ts` + `ReplayBar.svelte`, `HudIcon.svelte` over
   `iconPaths.ts`. Tests: `terrainEffects.test.ts`, `terrainLabel.test.ts`, `replayBarView.test.ts`, `boardStyle.test.ts`.
7. Docs: `client/AGENTS.md`, `viewer/AGENTS.md`, `server/game/AGENTS.md`, `docs/design/client/{play-screen,replay}.md`,
   `docs/design/viewer/design.md`.

## Validation

| Check | Pass |
| --- | --- |
| `dotnet build` | 0 errors |
| `dotnet test tests/Tabletop.Client.Tests` | all pass |
| `dotnet test tests/Tabletop.GameServer.Tests` | all pass |
| `dotnet test tests/Tabletop.Viewer.Tests` | all pass (types current) |
| `./scripts/check-client` | prints nothing |
| `cd viewer && npm run check && npm test` | 0 errors, all pass |
| `dotnet run --project sim -- suite` | every game passes |
| Screenshots, Godot and viewer, 1280×720 and 1920×1080, both themes | hover panel on ruin, forest, hill, sight blocker, toxic smog; selected piece card and table tint; replay bar; F1 overlay |

## Results

2026-11-07:

- `dotnet build`: 0 errors. `./scripts/check-client`: nothing printed.
- `Tabletop.Client.Tests` 1043 passed; `Tabletop.GameServer.Tests` 1862 passed; `Tabletop.Viewer.Tests` 16 passed.
- `viewer`: `npm run check` 0 errors; `npm test` 1810 passed, 6 skipped.
- Rules suite (in-process): 685 of 685 games passed, 1122 of 1122 entries covered.
- Screenshots (a game with a ruin, forest, hill, large sight blocker, toxic smog and ruins, run against this branch's game server):
  Godot sci-fi 1280×720 hover panels, the selected ruins and sight blocker with the card and the table tint, the F1 overlay; Godot fantasy
  1280×720 replay bar, hover, selected, F1; Godot fantasy 1920×1080 replay bar and hover; viewer sci-fi 1280×720 and fantasy 1920×1080
  hover on each piece, the selected sight blocker, the replay bar. Before: `/tmp/claude-1000/ux-audit/15-key-overlay.png`,
  `32-replay-fantasy.png`, `12-play.png`, `v04-replay.png`, `v03b-play-selected.png`.
- The right-click menu's icons are not in a screenshot: the menu is its own window, which the viewport capture does not hold.
