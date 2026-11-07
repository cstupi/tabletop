# Lobby and setup: team columns — plan

Spec: `docs/superpowers/specs/2026-11-07-lobby-and-setup-redesign-design.md`. Godot first, the browser to match. No wire change.

## Tasks

1. **Core: `TeamColumns`** (`client/src/Core/TeamColumns.cs`), test first (`tests/Tabletop.Client.Tests/TeamColumnsTests.cs`): `Of` (columns
   by ordinal then Watching; state Ready / Needs a player / Needs an army; members with `Army · edition`, `NoArmyWarns`, `You`, my
   `ArmySeat`, `Moves`; Join moves my one seat, takes a team when I hold none, adds when I hold several; Also play this side; a held team's
   Join disabled with its line for a non-creator; Watch on Watching; compact drops an empty Watching), `Moves`, `MoveTo`, `StillToDo`
   (`ToDoLine`), `Editing`.
2. **Core: the head and folds** (`SetupPanel.cs`): `SetupHeadView` carries `ToDo`; `SetupFolds` loses `Players` and the lobby's folds;
   `SetupLayout.TerrainListHeight(window, head)`. `LobbyView` keeps the army pickers, `EndSetupText`, `CreatorOnly`; its seat line, team
   buttons, Players list, team cards and wait line go. Tests in `SetupPanelTests`, `LobbyViewTests`.
3. **Theme**: `Ui.TeamColumn`, `Ui.TeamBand`, `Ui.MemberRow`, `Ui.OnBand`, `Ui.Text`, `Ui.DimText`, `Ui.Background`; icons `team`,
   `player`, `army`, `watch`, `warning`, `ready`, `editing` in both themes (white, tinted in code), registered in `UiThemes`, imported
   (`godot --headless --import`). `UiThemesTests` counts them.
4. **Panels**: `TeamColumnsPanel` (wide and compact; `ColumnBox` takes drops, `MemberRowBox` drags; army pickers filled from
   `ListArmies`; editing icons follow `Session.PresenceChanged` in compact), `SidePanelHead` (phase, Start setup beside it in the lobby,
   Still to do, compact columns in setup), `LobbyPanel` (Game settings only, Leave game at its foot; a heading in the lobby, the fold in
   setup). `TeamPanel` deleted.
5. **GameScreen**: lobby body is an `HBoxContainer` of the wide columns and a 380 px settings column; setup body is `SetupToolbar` then
   `LobbyPanel`; `FitList(window, head)` and a relayout when the head resizes.
6. **Calls fixture**: `teamButton` and `place` become `columnJoin`, `columnAlsoPlay`, `columnMove` (`CallCases.EditControls`,
   `CallCases.Edits`); regenerate `viewer/tests/fixtures/calls.json`; `viewer/tests/callRig.ts` replays them through `teamColumns.ts`.
7. **Viewer**: `lobby/teamColumns.ts` (port, test `tests/teamColumns.test.ts`), `TeamColumns.svelte`, `TeamIcon.svelte`; `LobbyPage.svelte`
   (head with Start setup and Still to do, columns, Game settings); `SetupTools.svelte` (Still to do and compact columns first, Players
   section gone); `header.ts` (setup wait "Ann ends setup"); `lobbyView.ts` and `lobbyPage.ts` trimmed; `TeamCards.svelte` deleted.
8. **Docs**: `client/AGENTS.md`, `viewer/AGENTS.md`, `docs/superpowers/specs/2026-10-27-godot-setup-screen-design.md` to the current
   design.

## Validation

- Core view-model tests: `TeamColumnsTests`, `SetupPanelTests`, `LobbyViewTests`, `UiThemesTests`.
- `dotnet test tests/Tabletop.Client.Tests` passes.
- `./scripts/check-client` prints nothing (no colour literal in Hud, Panels or Screens: team colours come from `Ui`).
- Outbound messages are unchanged in kind, but the controls that send them changed: `TABLETOP_WRITE_FIXTURES=1 dotnet test
  tests/Tabletop.Client.Tests --filter CallsViewerFixture`, then `CallsViewerFixture` passes without the variable.
- Viewer: `cd viewer && npm run check && npm test` (`calls.agreement.test.ts` replays the new column cases; `./scripts/geometry-wasm` first).
- Screenshots, before and after, at 1280×720 and 1920×1080, with 2 players and with 4 (3 on 2 teams plus a watcher; and 3 teams with one
  empty), creator and player views, lobby and setup: dev sessions, games created and joined over HTTP, teams and armies set over the
  socket, the Godot client run with `--write-movie` and `TABLETOP_PERF_GAME`, a fresh `settings.json` each run (a saved window rect
  overrides `--resolution`). Every game made is deleted.
- No server or wire change, so no lifecycle run.
