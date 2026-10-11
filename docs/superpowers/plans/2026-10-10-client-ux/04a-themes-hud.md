# Themes, Settings and HUD Frame, Tasks 7–10

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

Part of [`04-themes.md`](04-themes.md), split to keep each file under 500 lines; its Goal, Requires landed, Global
Constraints and Review Focus apply. Tasks 1–6 have landed.

## Task 7: Settings dialog

**Files:** Create `client/src/Screens/SettingsDialog.cs`; modify `Hud/HeaderBar.cs`, `Screens/HomeScreen.cs`, `Hud/LogDrawer.cs`, `Screens/GameScreen.Play.cs`.

- [ ] **Step 1:** `SettingsDialog : AcceptDialog` (`Title = "Settings"`, `OkButtonText = "Close"`) over a `VBoxContainer`: an
`OptionButton` of `UiThemes.All` names, selected at `UiThemes.Resolve(Settings.Theme)`, `ItemSelected += i => session.UpdateSettings(s => s with { Theme = UiThemes.All[(int)i].Id })`;
check boxes `Animate`, `Camera follows the action` (`CameraFollow`), `Turn banner` (`TurnBanner`), `Range rings on at start`
(`RangeRings`), each `Toggled += on => session.UpdateSettings(s => s with { X = on })`. `public static void Open(Node parent, Session session)`
adds it, frees it on `Confirmed` and `CloseRequested`, `PopupCentered(new Vector2I(360, 0))`. The theme applies at once
through `SettingsChanged` (Task 5).
- [ ] **Step 2:** `HeaderBar` gains `event Action? SettingsRequested` and a flat `Button { Icon = Ui.Icon("settings"), TooltipText = "Settings" }`
at the end of its top line (icon set again in `Render`); `GameScreen.Play` wires `_header.SettingsRequested += () => SettingsDialog.Open(this, _session)`.
`HeaderBar.Render`'s `Clear(_top, …)` keeps the button as it keeps `_gameOver`, and moves it last after `_gameOver`.
`HomeScreen`'s top bar gains `Btn("Settings", () => SettingsDialog.Open(this, Main.Instance.Session))`. `LogDrawer.Bind`
loses the Animate `CheckBox` and the comment plan 02a Task 5 left on it.
- [ ] **Step 3:** `dotnet build client/Tabletop.csproj`; `./scripts/client`: Home → Settings → Fantasy reskins Home at once;
relaunch keeps it. Commit `Add the Settings dialog from the header and Home`.

## Task 8: Colour literals to theme tokens, and the gate

**Files:** Modify every match of the Step 1 grep (`Hud/{DiceTray,DecisionBox,DecisionBox.Picks,HeaderBar}.cs`,
`Panels/{LobbyPanel,LogPanel}.cs`, `Screens/{GameScreen,HomeScreen,LoginScreen}.cs`, plan 02a's `Hud/CombatLogPanel.cs`
and plan 03a's `Hud/UnitCard.cs`); delete plan 03a's `Hud/CardColours.cs`; `Hud/EndMessage.cs`, `Panels/TeamPanel.cs`;
`scripts/check-client`.

- [ ] **Step 1: The gate, red first.** Add to `scripts/check-client` before `exit`:
`if grep -rnE 'new Color\(|\bColors\.|\bColor\s+\w+\s*=\s*new\(' client/src/Hud client/src/Panels client/src/Screens; then echo "check-client: a colour literal in Hud, Panels or Screens; use Ui or Variation" >&2; status=1; fi`.
Run `./scripts/check-client`: expected, the matches listed and exit 1.
- [ ] **Step 2: Replace by role.** Orange errors, refusals, the waiting line → `ThemeTypeVariation = Variation.Warning`;
dim or grey text, struck-through log rows, not-automated rules → `Variation.Dim`; the lit step → `Variation.Accent`; ticked
step → `Variation.Success`; `Colors.White` modulates go; a die face → `DiceFaces.Of(die)`; the casualty `Label3D` →
`Modulate = Ui.Failure`. A `Button` takes no `Label` variation: a dimmed button gets `Modulate = Ui.Faded`. Team colours
stay `Palette.Team`. `HeaderBar`, `EndMessage` and `UnitCard` re-render on `Ui.Changed` (unsubscribed in `_ExitTree`);
`GameScreen` on `Ui.Changed` calls `Rebuild()` and `Table.Redraw()` and sets the table layer's undo button
`Theme = Ui.Theme` (a `SubViewport` breaks inheritance). What plans 02 and 03 leave:
  - `CombatLogPanel`: `Struck` and `Pending` rows → `Ui.Faded`; `Lit` (the piece highlight) → `Ui.Accent`; `Plain` goes;
    a struck amendment label → `Variation.Dim`; each die line in `Details` becomes an `HBoxContainer` of
    `DiceFaces.Of(new Die(d.Raw, d.Modified, d.Success))` and a label of `d.Text` in `Variation.Success` or
    `Variation.Failure`.
  - `DiceTray`: `Pass`, `Fail` and `Tumbling` go. `_line` keeps the click and holds an `HBoxContainer` (children
    `MouseFilter = Ignore`): a label of `r.Title`, one `DiceFaces.Of(new Die(face, face, true))` per die while tumbling
    (`face` from `_faces`) or `DiceFaces.Of(new Die(d.Raw, d.Modified, d.Success))` settled, and a label of `r.Result`
    in `Variation.Success` when `r.Successes > 0`, else `Variation.Dim`; `Paint` rebuilds that row.
  - `UnitCard`: `CardColours` goes. Labels: `Text` → no variation, `Dim` → `Variation.Dim`, `Range(within)` →
    `Variation.Success` (within), `Variation.Warning` (beyond), none (null). The wound-row button's `Dim` → `Ui.Faded`.
    `Chip(text, colour, tooltip)` becomes `Chip(text, variation, tooltip, icon)`: a `PanelContainer` in `Variation.Chip`
    around an `HBoxContainer` of the optional icon and the label; a condition chip passes `Ui.Icon(chip.Condition!)`
    and `Variation.Warning`, an activated chip and a not-automated rule `Variation.Dim`; the Quality and Defense badges
    use `Variation.Badge`.
- [ ] **Step 3:** `./scripts/check-client` prints nothing; `dotnet build client/Tabletop.csproj`. Commit `Move Hud, panel and screen colours to the theme`.

## Task 9: Place the Hud by `HudLayout`

**Files:** Create `client/src/Hud/{HudPlacement,Toasts}.cs`; modify `Screens/GameScreen.cs`, `Screens/GameScreen.Play.cs`, `Hud/LogDrawer.cs`, `Hud/DiceTray.cs`.

- [ ] **Step 1:** `HudPlacement.Top/Bottom/Fill(Control c, HudRect r)`: anchors `TopLeft`, width `r.W`, height `r.H` (Fill)
or `Math.Min(c.GetCombinedMinimumSize().Y, r.H)` aligned to the slot's top or bottom. `Toasts : VBoxContainer`: `Show(text)` adds
a `PanelContainer` with a `Warning` autowrapped label, keeps the newest three, frees each after 4 s; `GameScreen.Toast` calls it.
- [ ] **Step 2:** `LogDrawer` (as plan 02a Task 5 left it: `_drawer`, `_toggle`, `CombatLog`, `Toggle`, `OpenAt`, `SetOpen`,
`Reported`, G in `_Input`) becomes a full-rect `Control` (`MouseFilter = Ignore`) with `_drawer` (the panel) and `_toggle`
(the button) as children; its anchors and the drawer's 640×420 minimum go. It gains `bool Open => _drawer.Visible`,
`event Action? OpenChanged` (raised by `SetOpen`) and `Place(HudRect button, HudRect? panel)`: `HudPlacement.Bottom(_toggle, button)`,
`HudPlacement.Fill(_drawer, panel)` when the panel shows. `CombatLog`, `Toggle`, `OpenAt` and `Reported` keep their names.
`DiceTray` (as plan 02 Task 6 left it) loses `Follow`, `Place`, `_below`, `_place`, the anchors in `Bind`, `Render`'s
`Place()` call and their `_ExitTree` lines; `GameScreen.Play` drops `_dice.Follow(_decision)`. `UnitCard`'s `_card` loses
its 440×360 minimum: the slot sets the width.
- [ ] **Step 3:** `GameScreen.Layout()`, in `GameScreen.Play.cs`: width is `_hud.Size.X`, less `_side.Size.X` while the side panel shows;
`HudLayout.Of(w, _hud.Size.Y, new(false, card.Visible, decision.Visible, dice.Visible, logDrawer.Open))`; corner and
toasts `Top`, header `Top`, card and decision `Bottom`, dice `Bottom` in its slot moved to end 8 px above the decision's
actual top, log `Place`, end message `Fill`. Plan 05 Task 8 passes the roster's visibility and places the roster and the banner. It runs deferred, once per frame, on
`_hud.Resized`, each part's `MinimumSizeChanged` and `VisibilityChanged`, and `OpenChanged`; never on `Resized` of a part
it sizes (a feedback loop).
- [ ] **Step 4:** `./scripts/client` at 1280×720 and 1920×1080 (Project Settings → window size, or drag the window):
open the log, take a decision; nothing overlaps. Commit `Place the play Hud by HudLayout with stacked toasts`.

## Task 10: Verify and document

- [ ] **Step 1:** `dotnet test tests/Tabletop.Client.Tests` (all pass), `dotnet build` (0 errors), `./scripts/check-client` (no output).
- [ ] **Step 2:** `client/AGENTS.md`: `src/Theme/` in Layout; `UiTheme`, `UiThemes`, `HudLayout` under Core; the new
settings keys; the colour gate under check-client; `content/themes/`. `docs/design/client/design.md`: a Themes and Hud
frame section stating the structure above. Commit `Document themes, settings and the Hud frame`.
- [ ] **Step 3: The owner's check**: a game played in each theme at 1280×720, read for legibility of panels, buttons,
dice and team colours on both skins. Owner findings become a follow-up task, not edits to this plan.
