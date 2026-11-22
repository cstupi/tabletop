# Options screen

Owner, 2026-11-21: *"The settings should be like every other game and be a full options setting. We need to support more options. Look at
typical video game UI's … for inspiration."* Godot client only; the browser gets no settings. Replaces `Screens/SettingsDialog.cs` (a
dialog of 15 settings in a two-column grid). Spacing follows `2026-11-21-spacing-rules-design.md`; this is a full-screen page, so the
scale's upper steps.

## Approaches

- **Full-screen page with a tab rail (chosen).** The pattern of most PC games' options: a category rail on the left, one category's rows on
  the right, a description pane, a footer bar. Scales to many options; every category is one click.
- Bigger dialog with tabs: still a box over the screen, cramped once Controls lists 40 keys.
- One long scrolling page with headings: no navigation; Controls buries Audio.

## Layout

```
┌───────────────────────────────────────────────────────────────────────────────┐
│  OPTIONS                                                              [ ✕ ]  │
├──────────────┬───────────────────────────────────────────┬────────────────────┤
│ ▌Gameplay    │  Animate moves                  [  On ▾ ] │  Animate moves     │
│  Camera      │  Turn banner                    [  On ▾ ] │                    │
│  Controls    │  Range rings at start           [  On ▾ ] │  Models slide to   │
│  Video       │  Measure mode                   [ Base ▾] │  where they move.  │
│  Audio       │  Model hitboxes                 [  On ▾ ] │  Off: they jump.   │
│  Interface   │  ...                                      │                    │
│  Accessibility│                                          │                    │
├──────────────┴───────────────────────────────────────────┴────────────────────┤
│  [ Reset tab ]                                   [ Revert ]  [ Apply ]  Esc   │
└───────────────────────────────────────────────────────────────────────────────┘
```

- A `CanvasLayer` over every screen (Login, Home, game), opened by the existing gear buttons and by the game menu (setup spec). Esc or ✕
  closes it.
- **Rail:** the categories as tall tabs, icon and word, the open one lit with an accent bar on its left edge.
- **Rows:** label on the left edge, the control right-aligned in a fixed column; 48 px rows, 8 between rows, a dim group heading where a
  category has groups. The rows scroll; the rail and footer do not.
- **Description pane:** the hovered or focused row's name and one or two plain lines (what it changes, its default). Fact only, no
  reassurance (memory: no AI-slop UI text).
- **Footer:** Reset tab (this category to defaults), Revert (pending changes), Apply. Changes are pending until Apply; closing with pending
  changes asks Apply / Discard. A display-mode or resolution change applied shows a 15 s "Keep these settings?" countdown that reverts on
  timeout.
- Keyboard and mouse both: arrows move between rows, left/right change a value, Q/E or Tab move between categories.

## Categories and options

Existing settings keep their `ClientSettings` names and defaults; new ones are added to `ClientSettings` (persisted in `settings.json`,
old files load with defaults).

**Gameplay:** Animate moves; Turn banner; Range rings at start (V still toggles in play); Measure mode (`MeasureMode`); Model hitboxes;
Terrain hitboxes; Wound numbers (new, on); Combat log open at start (`LogOpen`), Detailed log (`LogDetailed`).

**Camera:** Camera follows; Pan speed (new, 50–200 %); Orbit speed (new); Zoom speed (new); Edge scrolling (new, off); Invert orbit (new:
Q/E direction); Invert zoom (new).

**Controls:** every `Intent` with a key, grouped (Decisions, Units, Camera, Measure and sight, Table and setup, Interface), each row
`Name   [ key ]  [ second key ]`. Click a key cell, press a key to bind; Esc cancels; a key already bound elsewhere shows the conflict and
offers Swap. Reset tab restores the defaults. The bindings move from the `Hotkeys.Of` switch into a table (`KeyBindings`: defaults =
today's switch exactly, overrides stored in `ClientSettings`); the key overlay and every key cap read the table, so a rebind shows
everywhere. Option1–9, Confirm and Cancel are listed but not rebindable.

**Video:** Display (window / borderless / fullscreen); Resolution (new, owner 2026-11-21: *"most games have predefined resolutions … that
drop down"*): a dropdown of the common sizes 1280×720, 1366×768, 1600×900, 1920×1080, 2560×1440, 3840×2160 (16:9) and 1280×800,
1440×900, 1680×1050, 1920×1200, 2560×1600 (16:10), only those that fit the current screen, plus the screen's own size marked
"(native)", and "Custom" when the window was resized by hand. Windowed: sets the window size and centres it. Borderless and fullscreen
use the screen's size: the row shows the native size, disabled, with its description saying Render scale sets the 3D resolution; Quality preset; Render scale; VSync;
Frame rate limit; Show frame rate (new, a corner counter).

**Audio:** Master volume; Effects volume (new); Mute when the window is in the background (new, off). Volumes are
Godot audio buses (`Master`, `Effects`); every sound plays on one of them.

**Interface:** Theme (tiles, as today); UI scale (new: 75–150 %, the window's content scale); Tooltip delay (new: 0–1 s, default 0.15);
Toast duration (new).

**Accessibility:** Colour-blind team colours (new: off / a palette whose team colours stay distinct under deuteranopia, protanopia and
tritanopia, applied through `Ui.Team`); Text size (new: 100 / 115 / 130 %, the theme's font sizes scaled); Reduce motion (new: no camera
glides).

## Where it lives

- `client/src/Core/OptionsModel.cs` (Godot-free, unit-tested): categories, rows (label, description, kind, range, default, read/write on a
  `ClientSettings`), pending vs applied, Reset tab, conflict check for bindings.
- `client/src/Core/KeyBindings.cs` (unit-tested): the default table equal to today's `Hotkeys.Of`, overrides, lookup by key and by intent.
- `client/src/Screens/OptionsScreen.cs` (+ partial files per area, each under 500 lines): draws the model. `SettingsDialog.cs` goes.
- `client/AGENTS.md` → an "Adding an option" recipe: the row in `OptionsModel`, the field in `ClientSettings`, where it is read.

## Left to the builder

Exact description wording, row order within a category, icons per category (theme icon keys; add any missing to both themes), the
colour-blind palette values (checked with a simulator, contrast against both themes' panels).
