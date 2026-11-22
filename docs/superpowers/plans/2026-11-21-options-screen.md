# Options screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `SettingsDialog` with a full-screen Options page (seven categories, every new option wired), with rebindable keys.

**Architecture:** Godot-free `KeyBindings` and `OptionsModel` in `client/src/Core` (unit-tested); `ClientSettings` gains the new fields; `OptionsScreen` (a `CanvasLayer`, partial files) draws the model; Godot-side appliers read the settings.

**Tech Stack:** Godot 4.7 / C# (.NET 10), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-21-options-screen-design.md`; spacing `docs/superpowers/specs/2026-11-21-spacing-rules-design.md`.

## Global Constraints

- Spacing only through `Space.XS..XXL`; colours only through `Ui`/`Variation`; `./scripts/check-client` prints nothing.
- Existing settings keep their `ClientSettings` names and defaults; an old `settings.json` loads with defaults for every new field.
- `KeyBindings.Default` gives the same intent as the old `Hotkeys.Of` switch for every `HotKey` × Shift × Ctrl.
- Option1–9, Confirm and Cancel are listed but not rebindable.
- Each `OptionsScreen*.cs` under 500 lines. UI text states facts only.
- Do not touch `GameScreen.cs`, `Panels/SetupToolbar.cs`, `SidePanelHead.cs`, `LobbyPanel.cs`, `HudLayout.cs`.

## Review Focus

- A rebind to a key bound elsewhere: the conflict is named and Swap moves the other intent's binding to the old key (OptionsModelTests).
- Closing with pending changes: Apply / Discard asked; Discard leaves settings as applied (OptionsModelTests: `Revert`).
- A display or resolution change: 15 s keep-or-revert (OptionsModel `NeedsConfirm`).
- A rebind with Shift held (Shift+Tab-style chords) round-trips through `settings.json` (SettingsTests).
- Reset tab on Controls drops every override and nothing else (OptionsModelTests).

---

### Task 1: KeyBindings

**Files:** Create `client/src/Core/KeyBindings.cs`; modify `client/src/Core/Hotkeys.cs`, `client/src/Core/KeyOverlayView.cs`, `client/src/Core/DecisionCard.cs`; test `tests/Tabletop.Client.Tests/KeyBindingsTests.cs`, `HotkeysTests.cs`, `KeyOverlayViewTests.cs`.

**Interfaces (produces):**
- `HotKey` gains the remaining letters, `Key0`, `F2`–`F12`, arrows, `Insert`, punctuation (Godot names), appended after `Pagedown`.
- `record KeyBinding(HotKey Key, bool? Shift = null, bool? Ctrl = false)`: null matches either state.
- `sealed class KeyBindings`: `static Default`; `With(IReadOnlyDictionary<Intent, IReadOnlyList<KeyBinding>> overrides)`; `Intent? Of(HotKey, bool shift, bool ctrl)` (the most specific matching binding); `IReadOnlyList<KeyBinding> Of(Intent)`; `static IReadOnlyList<string> Caps(KeyBinding)` (`⇧`, `Ctrl`, the key's cap); `string Cap(Intent)`; `static bool Rebindable(Intent)`; `static Groups` (Decisions, Units, Camera, Measure and sight, Table and setup, Interface).
- `Hotkeys.Bindings` (static, settable; Main sets it from settings); `Hotkeys.Of` delegates to it.
- `KeyOverlayView.Of(mode, title, stoppable, KeyBindings? keys = null)` and `Camera(keys)`, `Measure(keys)`, `Table(keys)` read caps from the table.

- [ ] Test `Default_equals_the_old_switch_for_every_key_and_modifier`: the old switch copied into the test as `Legacy`, compared over every `HotKey` × Shift × Ctrl.
- [ ] Test the default overlay rows equal today's literal rows; a rebind of `ToggleRangeRings` to `J` shows `J` in `Table(keys)`.
- [ ] Implement; run `dotnet test tests/Tabletop.Client.Tests`; commit.

### Task 2: ClientSettings fields

**Files:** modify `client/src/Core/ClientSettings.cs`, `client/src/Core/GraphicsSettings.cs` (`DisplayMode.Exclusive`); test `tests/Tabletop.Client.Tests/SettingsTests.cs`.

**Produces (name, default, range):** `WoundNumbers` true; `PanSpeed`, `OrbitSpeed`, `ZoomSpeed` 100 (50–200); `EdgeScroll` false; `InvertOrbit`, `InvertZoom` false;
`KeyOverrides` empty (`IReadOnlyDictionary<Intent, IReadOnlyList<KeyBinding>>`, saved as `keys`); `ShowFps` false; `EffectsVolume`, `InterfaceVolume` 100 (0–100);
`MuteInBackground` false; `UiScale` 100 (75–150); `TooltipMs` 150 (0–1000); `ToastSeconds` 4 (2–10); `ColourBlind` false; `TextSize` 100 (100, 115, 130); `ReduceMotion` false.
`Volume` stays the master volume.

- [ ] Test `An_old_file_loads_every_new_field_at_its_default` (today's JSON, no new keys) and `New_fields_round_trip` (including a `Shift+Tab`-style override and an out-of-range value clamped).
- [ ] Implement; run tests; commit.

### Task 3: OptionsModel and the theme's accessibility variants

**Files:** create `client/src/Core/OptionsModel.cs`; modify `client/src/Core/UiThemes.cs`; test `tests/Tabletop.Client.Tests/OptionsModelTests.cs`, `UiThemesTests.cs`.

**Produces:**
- `enum OptionCategory { Gameplay, Camera, Controls, Video, Audio, Interface, Accessibility }`, `enum OptionKind { Toggle, Choice, Slider, Themes }`.
- `record OptionRow(Id, Category, Label, Description, Kind, Get, Set)` with `Choices`, `Min`, `Max`, `Step`, `Text(int)`; every value an `int`.
- `OptionsModel(ClientSettings applied)`: `Rows`, `In(category)`, `Value(row)`, `Set(row, v)`, `Pending`, `Applied`, `Dirty`, `ResetTab(category)` (Controls: clears `KeyOverrides`), `Revert()`, `Apply()`,
  `static NeedsConfirm(before, after)` (display mode or window size changed), `Keys`, `Conflict(intent, binding)`, `Bind(intent, slot, binding, swap)`.
- `UiThemes.Adjusted(UiTheme, int textSize, bool colourBlind)`: font sizes × textSize/100; `ColourBlindTeams` replaces `Teams`.
- `OptionsModel.Resolutions`: 1280×720, 1366×768, 1600×900, 1920×1080, 2560×1440, 3840×2160, 1280×800, 1440×900, 1680×1050, 1920×1200, 2560×1600; `Fitting(screen)` keeps those that fit plus the screen's own size; the row (`Choices`, `Value`, `Set`, `Enabled`, `Description`) marks it "(native)", adds "Custom (W×H)" for a hand-sized window, centres the window on a pick, and outside a window shows native, disabled, its description naming Render scale.

- [ ] Tests: every category but Controls has rows; Set is pending until Apply; Revert restores; ResetTab touches only its tab; a conflicting Bind without swap reports, with swap gives the other intent the old key; Quality resets Render scale; NeedsConfirm on display/resolution only; colour-blind palette distinct under deuteranopia, protanopia and tritanopia simulation and ≥ 3:1 against both themes' panels, none within 40° hue of Success.
- [ ] Implement; run tests; commit.
