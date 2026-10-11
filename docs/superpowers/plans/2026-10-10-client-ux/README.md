# The client UX and themes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The client UX the spec settles: nothing on the table inside anything else, prevented while dragging and
judged by the server; every roll in a grouped, filterable combat log with its target number's basis, and the dice tray
showing the latest; the unit card in place of the stat sheet; two themes, a Settings dialog and one Hud frame; the army
roster, header additions, unit markers and floating results, range rings, the turn banner and the camera's WASD, orbit,
glide and follow.

**Spec:** [`docs/superpowers/specs/2026-10-10-client-ux-and-themes-design.md`](../../specs/2026-10-10-client-ux-and-themes-design.md),
the whole document.

**Requires landed:** `main` at `7c4ec5d`.

## Plan files, in execution order

| # | File | Tasks | What |
| --- | --- | --- | --- |
| 1 | [`01-collision.md`](01-collision.md) | 1–3 (3) | `Collision` (rest, slide, separate) in `Tabletop.Geometry`; `Movement.Region` goes around models |
| 2 | [`01a-collision-server.md`](01a-collision-server.md) | 4–7 (4) | The server rests every placement; `MoveRegion.Separation/PassesModels`; p.7's three contact breaches; flat-topped hill meshes |
| 3 | [`01b-collision-client.md`](01b-collision-client.md) | 8–12 (5) | `DragBodies`; every drag slides and rests, Alt places freely; the sim samples clear of contact; `Settling` goes; documents; validation |
| 4 | [`02-combat-log.md`](02-combat-log.md) | 1–3, 6–7 (5) | `ThresholdBasis` on the wire and from `StatReader`; `LogFilter`/`LogOpen` settings; the dice tray as the latest roll entry; validation and documents |
| 5 | [`02a-combat-log-client.md`](02a-combat-log-client.md) | 4–5 (2) | `CombatLog`, `RollEntry`, `ActivationBlock` in Core; `CombatLogPanel` in `LogDrawer` |
| 6 | [`03-unit-card.md`](03-unit-card.md) | 1–3 (3) | `UnitSheet` AP column and typed chips; stateless `SheetFocus`; `EnemyStrip` |
| 7 | [`03a-unit-card-hud.md`](03a-unit-card-hud.md) | 4–5 (2) | `UnitCard` in place of `StatSheet` and `RoundHistory`, feeding the log's *This unit* filter; documents |
| 8 | [`04-themes.md`](04-themes.md) | 1–6 (6) | Fonts and art; `UiTheme`/`UiThemes`; the theme and play settings; `ThemeBuilder`, `Ui`, `Variation`, `DiceFaces`; `HudLayout` |
| 9 | [`04a-themes-hud.md`](04a-themes-hud.md) | 7–10 (4) | Settings dialog; colour literals to theme tokens and the `check-client` gate; the Hud placed by `HudLayout`; documents |
| 10 | [`05-table-ux.md`](05-table-ux.md) | 1–4 (4) | Camera, roster and range-ring hotkeys and the `TableUx` fixture; `CameraFocus` and `OrbitCamera`; `Roster`; header additions |
| 11 | [`05-table-ux-2.md`](05-table-ux-2.md) | 5–6 (2) | `UnitMarkers`, `FloatingResults` and their nodes; `RangeRings` and `RangeRingNode` |
| 12 | [`05-table-ux-3.md`](05-table-ux-3.md) | 7–8 (2) | `TurnBanners` and `TurnBanner`; assembly in `GameScreen.Play`, verification and documents |

42 tasks. A lettered or numbered continuation shares its parent's Goal, Global Constraints and Review Focus. Plan 02's
tasks run in number order across its two files: 1–3, then 02a's 4–5, then 6–7.

## Cross-plan interfaces

The earlier plan defines each name; a later plan's code starts from the shape the earlier one leaves.

| Type / member | Defined in | Consumed by |
| --- | --- | --- |
| `Collision.RestHeight/Slide/Separate`, `Collision.Step` | 01 Tasks 1–2 | 01a Task 4, 01b Tasks 8–10 |
| `Movement.Region(…, models)` | 01 Task 3 | 01b Task 8 (`MoveRuler.Region`) |
| `Battlefield.Settle(state, modelId, placement)` | 01a Task 4 | 01a Task 6 |
| `MoveRegion.Separation`, `MoveRegion.PassesModels` | 01a Task 5 | 01a Task 6, 01b Tasks 8, 10 |
| `GfMoves.ContactBreach`, `IGameRules.PlacementBreach` | 01a Task 6 | 01b Task 10 |
| `HeldBody`, `DragBodies`, `TableInput.Region`, `TableInput._stationary` | 01b Tasks 8–9 | 05 Task 2 (`TableInput.cs` line budget) |
| `ThresholdBasis`, `ThresholdChange`, `RollRecord.Basis` | 02 Task 1 | 02 Task 2, 02a Task 4 |
| `enum LogFilter { All, Rolls, Mine, Unit }`, `ClientSettings.LogFilter/LogOpen` (positional, after `GameServerUrl`) | 02 Task 3 | 02a Tasks 4–5, 03a Task 4 (`LogFilter.Unit`), 04 Task 4 (record, `Dto`, `Equals`, `GetHashCode`) |
| `CombatLog.{Build, Filter, Latest, Pip}`, `RollEntry`, `DieView`, `ActivationBlock` | 02a Task 4 | 02 Task 6, 02a Task 5, 04a Task 8 |
| `CombatLogPanel.FocusUnit/Reveal`, `LogDrawer.CombatLog/OpenAt/Toggle/SetOpen`, the Animate checkbox in `LogDrawer` | 02a Task 5 | 02 Task 6, 03a Task 4, 04a Tasks 7 (checkbox goes), 9 (drawer split) |
| `StatSheet.UnitId`, `StatSheet.FocusChanged` | 02a Task 5 | 03 Task 2 (adapted), 03a Task 4 (`UnitCard` keeps the names) |
| `DiceTray.RollClicked`, `Render`/`Paint` over `CombatLog.Latest` | 02 Task 6 | 04a Task 8 (faces), 9 (`Follow` and `Place` go) |
| `ChipKind`, `SheetChip(Kind, Text, Condition)`, `SheetWeapon.Ap/ApRule`, `UnitSheet.ApLabel` | 03 Task 1 | 03 Task 3, 03a Task 4, 04a Task 8 (`SheetChip.Condition` → `Ui.Icon`) |
| `SheetFocus.Of/TeamOf` | 03 Task 2 | 03 Task 3, 03a Task 4 |
| `UnitCard` (`UnitId`, `FocusChanged`, `ModelPicked`), `CardColours` | 03a Task 4 | 04a Tasks 8 (`CardColours` deleted), 9 (`_card` sized by its slot) |
| `UiTheme`, `UiThemes.{All, Resolve, DefaultId}` | 04 Task 3 | 04 Tasks 4–5, 04a Task 7 |
| `ClientSettings.Theme/CameraFollow/TurnBanner/RangeRings` (`init`), `Session.SettingsChanged` | 04 Task 4 | 04a Task 7; 05 Tasks 6 (`RangeRings`), 7 (`TurnBanner`), 8 (`CameraFollow`) |
| `Ui.{Icon, Changed, Faded, Accent, …}`, `Variation.*`, `DiceFaces.Of(Die)`; namespace `Tabletop.Client.Theming` | 04 Task 5 | 04a Task 8; 05 Tasks 3–5, 7 |
| `Palette.Team(int)` reading `Ui.Team` | 04 Task 5 | 03a Task 4, 05 Tasks 3, 4, 6 |
| `HudLayout.Of`, `HudPart`, `HudVisible`, `HudRect` | 04 Task 6 | 04a Task 9, 05 Task 8 |
| `HeaderBar.SettingsRequested`, the settings button kept last in `_top` | 04a Task 7 | 05 Task 4 |
| `HudPlacement.Top/Bottom/Fill`, `Toasts`, `GameScreen.Layout()` in `GameScreen.Play.cs`, `LogDrawer.Open/OpenChanged/Place` | 04a Task 9 | 05 Task 8 |
| `Intent.Camera*/ToggleRoster/ToggleRangeRings`, `HotKey.W/A/S/D/R/V` | 05 Task 1 | 05 Tasks 2, 3, 6 |
| `Roster.Icon(key)`, `Roster.Chip(text)` | 05 Task 3 | 05 Tasks 4, 5 |

Files edited by more than one plan, in order: `ClientSettings.cs` (02 → 04); `GameScreen.Play.cs` (02a → 02 → 03a →
04a → 05-3); `LogDrawer.cs` (02a → 04a); `DiceTray.cs` (02 → 04a); `StatSheet.cs` (02a → 03 → deleted in 03a);
`HeaderBar.cs` (04a → 05); `Palette.cs` (04 → read by 05); `TableInput.cs` (01b → 05); `Hotkeys.cs`, `KeyOverlay.cs`,
`OrbitCamera.cs` (05 only).

## Global Constraints

- One world unit is one inch; the table frame's origin is the table's corner, Z up. `WorldFrame` converts for Godot.
- C# only, no GDScript. Test-driven, xUnit under `tests/`. `client/src/Core/` is Godot-free and compiled into
  `tests/Tabletop.Client.Tests`; Godot nodes render Core models only.
- A rule is named only in `server/game/Rules/GrimdarkFuture/`; `Tabletop.Geometry` and the client name none.
  `./scripts/check-client` prints nothing.
- Every wire change is additive; `ProtocolVersion.Current` does not change. After one:
  `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then `cd viewer && npm run check && npm test`.
- `.tscn`, `.tres` and `.import` files are editor-owned; nodes and the Godot `Theme` are built in C#.
- After plan 04a Task 8, `client/src/Hud`, `Panels` and `Screens` hold no colour literal: colours come from `Ui`,
  `Variation` and `Palette.Team`.
- Every file stays under 500 lines, plan files included.
- Server and sim socket tests need `docker compose up -d`. Never `docker compose down -v`.
- Commit after every task; the message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and
  `Claude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW`.

## How to run

Read [`docs/handoff.md`](../../../handoff.md) → *How to work here* first.

- One worktree and branch per plan group (01, 02, 03, 04, 05), each under one controller agent (`opus`) running
  `superpowers:subagent-driven-development`. A group starts from `main` after the previous group has merged: each
  edits files the one before it created.
- Inside a group, tasks run in the order the plan files give; plan 01's Tasks 4 and 7 may run beside Task 3, and plan
  05's order is in its File map.
- Every subagent names its model. A task with a **Model:** line uses it; for the rest the controller names one at
  dispatch, `opus` at most and `sonnet` for document and mechanical edits.
- Each group's last task validates and levels the documents. The on-screen checks a plan lists are the owner's;
  findings become follow-up tasks, not edits to these plans.
