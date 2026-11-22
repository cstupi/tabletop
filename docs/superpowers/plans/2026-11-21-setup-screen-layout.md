# Setup screen layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lobby and setup in the Godot client laid out as an editor: a tool rail, one tool panel at a time, a top bar with the game's state
and its one action, a game menu on ☰ and Esc, a key hint bar, the table taking the rest.

**Architecture:** Godot-free models in `client/src/Core` (`SetupTools`, `TopBarView`, `GameMenuView`, `HudLayout.Setup`) carry every
decision and are unit-tested; the nodes in `client/src/Panels` draw them. `GameScreen` places the table and the parts from
`HudLayout.Setup`'s slots instead of the old side column. Nothing new on the wire.

**Tech Stack:** Godot 4.7 C#, xUnit (`tests/Tabletop.Client.Tests`).

**Spec:** `docs/superpowers/specs/2026-11-21-setup-screen-layout-design.md`; behaviour kept from
`docs/superpowers/specs/2026-11-07-lobby-and-setup-redesign-design.md`; spacing from `docs/superpowers/specs/2026-11-21-spacing-rules-design.md`.

## Global Constraints

- Spacing only from `Space.XS/S/M/L/XL/XXL` (`client/src/Theme/Space.cs`), never a literal; colours only from `Ui`/`Variation`.
- Every Panels file under 500 lines; `SetupToolbar.cs` and `SidePanelHead.cs` go.
- Game menu's Options calls `SettingsDialog.Open(this, _session)`, exactly what the play header's gear calls.
- Do not touch `SettingsDialog.cs`, `ClientSettings.cs`, `Hotkeys.cs`, `KeyOverlay.cs`; `TeamColumnsPanel` reused unchanged.
- UI text states facts only.

## Review Focus

- One account holding both teams: Join on the second team from the Teams tool adds a seat; both chips read Ready once both armies are picked.
- A non-creator: no End/Start setup button, the wait line "Ann ends setup" in its place; Game tool controls disabled.
- Esc with a selected piece clears the selection first (table), a second Esc opens the menu; Esc in a text field never opens it.
- 1280×720 with four teams: the top bar's items fit beside the corner; the game name clips, nothing overlaps.
- Closing the open tool gives the table the panel's width (rail stays).

---

### Task 1: Core models

**Files:**
- Create: `client/src/Core/SetupTools.cs`, `client/src/Core/TopBarView.cs`
- Modify: `client/src/Core/SetupPanel.cs` (SetupFolds keeps only `PieceMesh`/`PieceVolumes`; `SetupHead`, `SetupLayout` go)
- Modify: `client/src/Core/HudLayout.cs` (`SetupPart`, `SetupVisible`, `HudLayout.Setup`)
- Test: `tests/Tabletop.Client.Tests/SetupToolsTests.cs`, `TopBarViewTests.cs`, `HudLayoutSetupTests.cs`; update `SetupPanelTests.cs`, `SetupRedesignTests.cs`

**Interfaces (Produces):**
- `enum SetupTool { Terrain, Objectives, Table, Zones, Layouts, Teams, Game }`
- `sealed class SetupTools { static IReadOnlyList<SetupTool> Order; static string Icon(SetupTool); static string Name(SetupTool);
  static string Title(SetupTool, TableState?, GameInfo?); static int? Badge(SetupTool, TableState?); SetupTool? Open; void Press(SetupTool); void Show(SetupTool) }`
  Default open Terrain; pressing the open one closes it. Icons `terrain objective table zone layout team settings`. Badges: Terrain pieces,
  Objectives markers, null at zero and for the rest.
- `SetupTools.Hints: IReadOnlyList<KeyHint>` with `record KeyHint(string Keys, string Word)`:
  Drag place · Q E turn · PgUp PgDn level · Del remove · Ctrl measure · Right-click more · Esc menu.
- `record TeamChip(Guid TeamId, int Ordinal, string Title, ColumnState State, string Icon, string Tooltip)`;
  `record TopBarView(IReadOnlyList<TeamChip> Teams, IReadOnlyList<ToDoLine> ToDo, string? ToDoText, string? Action, string? ActionTip, LobbyEdit? Edit, string? Waiting)`;
  `TopBarView.Of(GameInfo, bool isCreator, TableState?)`: null outside lobby and setup; ToDo the Missing lines of `TeamColumns.StillToDo`,
  ToDoText `"N to do"` or null; creator Action `Start setup` / `End setup` / `End setup (N off the table)` with `LobbyView.EndSetupText` as tip;
  others Waiting `"{creator} starts setup"` / `"{creator} ends setup"`.
- `enum GameMenuItem { Resume, Options, Leave, Home }`, `enum EscPress { Pass, Open, Close }`;
  `GameMenuView.Items(bool canLeave)`, `GameMenuView.Text(GameMenuItem)`, `GameMenuView.OnEsc(bool menuOpen, bool textFocus, bool tableBusy)`.
- `enum SetupPart { TopBar, Corner, Rail, Panel, Body, Card, Toasts, Loading, Hints }`, `record SetupVisible(bool Rail, bool Panel, bool Hints)`;
  `HudLayout.Setup(float w, float h, SetupVisible v, float topH = TopBarH, float hintH = HintH)`; constants `TopBarH 56, RailW 56, PanelW 360, HintH 32, SetupCornerW 360`.
  TopBar and Hints full width at the edges; Corner inside the top bar's left; Rail and Panel under it on the left; Body the rest; Card the
  right column inside Body; Toasts left of the card at Body's top; Loading at Body's foot.

- [ ] Write the tests: tool order and icons, Press toggles, badges; TopBarView for creator/non-creator in lobby and setup, solo account on two teams; GameMenuView items and Esc; HudLayout.Setup no overlap among Corner, Rail, Panel, Card, Toasts, Loading, Hints for every visibility at 1280×720, 1920×1080, 2560×1440, Card/Toasts/Loading inside Body, Body flush with the panel, whole pixels.
- [ ] Run `dotnet test tests/Tabletop.Client.Tests --filter "SetupTools|TopBarView|HudLayoutSetup"`: fails to compile.
- [ ] Implement; move fold titles into `SetupTools.Title`; drop `SetupHead`, `SetupLayout`, `SetupFolds.Order/TeamsOpen`.
- [ ] Run the client tests: pass. Commit.

### Task 2: Tool panels split out of SetupToolbar

**Files:**
- Create: `client/src/Panels/SetupTool.cs` (base `SetupToolBase : VBoxContainer` with `Heading` actions, `Fills`, shared `Gated`, `Icon`, `Framed`, `Confirm`),
  `SetupTerrainTool.cs` (+ `TerrainTileButton`), `SetupObjectivesTool.cs`, `SetupTableTool.cs` (+ `Swatches`), `SetupZonesTool.cs` (+ `ZoneDiagramControl`),
  `SetupLayoutsTool.cs`, `SetupTeamsTool.cs` (compact `TeamColumnsPanel`), `SetupPieceKeys.cs` (right-click menu and Delete over the table, from SetupToolbar).
- Delete: `client/src/Panels/SetupToolbar.cs`, `client/src/Panels/SidePanelHead.cs`.
- Modify: `client/src/Panels/LobbyPanel.cs`: no fold, no Leave; groups **Game** (rule set line, name, rule set, activation, enforcement) and **Optional rules**, Apply at the foot.

- [ ] Build `dotnet build client/Tabletop.csproj` 0 errors (after Task 3 wires them). Commit with Task 3.

### Task 3: Rail, top bar, game menu, hint bar, GameScreen

**Files:**
- Create: `client/src/Panels/SetupRail.cs`, `SetupToolPanel.cs`, `SetupTopBar.cs`, `GameMenu.cs`, `KeyHintBar.cs`
- Modify: `client/src/Screens/GameScreen.cs`, `GameScreen.Play.cs` (Layout places the setup frame; the table box at `Body`; the table right-click menu's Leave goes)

- `SetupRail`: one toggle button per tool with its icon, tooltip `Name`, badge; `Pressed(SetupTool)`.
- `SetupToolPanel`: heading (icon, `SetupTools.Title`, the tool's heading actions), body: Terrain fills, others scroll.
- `SetupTopBar`: team chips (click: Teams tool, setup only), to-do `MenuButton`, action (`Variation.Primary`) or wait line, ☰.
- `GameMenu`: backdrop and a card of Resume, Options, Leave game, Home; `_Input` Esc per `GameMenuView.OnEsc`.
- `KeyHintBar`: `Variation.Quiet` band of `HudBits.KeyCaps` and words; hidden while text has focus.

- [ ] `dotnet build`, `dotnet test tests/Tabletop.Client.Tests`, `./scripts/check-client`. Commit.

### Task 4: On screen and docs

- [ ] Visual harness (3.6 game): lobby, setup, every tool, game menu, solo on both teams, at 1280×720 and 1920×1080.
- [ ] `dotnet run --project sim -- lifecycle` against the stack.
- [ ] `client/AGENTS.md`: the setup screen paragraph and the Panels list. Commit.
