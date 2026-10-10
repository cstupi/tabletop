# Client UX and themes

Design session, 2026-10-10. The play screen's second pass: a unit card that shows the unit you are working with, a combat
log that shows every roll with its target number and modifiers, selectable themes (fantasy and sci-fi), an army roster,
table markers, range rings, turn banners, camera follow and WASD camera movement. Builds on the in-game UX spec
(`2026-10-08-in-game-ux-design.md`); where the two differ, this one settles it.

## The owner's requirements

- The lower left shows the current unit's stats.
- Dice results are in the log with their modifiers, so a player can see exactly what happened at every step.
- The UI is themeable, with more than one theme; a fantasy and a sci-fi skin to start.
- Overall UX for players of these games: a roster with activations left, status markers on the table, weapon range
  rings, a turn banner, camera follow.
- The camera moves with WASD, not only orbits one fixed point.

## Parts

| Part | Where | Replaces |
| --- | --- | --- |
| Unit card | lower left | `StatSheet` (both tabs) |
| Combat log | lower right drawer | `LogDrawer`'s `LogPanel` in play |
| Dice tray | above the decision box | `DiceTray`'s rendering |
| Army roster | left edge, above the unit card | — |
| Header additions | top centre | — |
| Table markers, floating results | on the table | — |
| Range rings | on the table | — |
| Turn banner | centre, transient | — |
| Camera | `OrbitCamera` | its pan and focus |
| Themes | every screen | hard-coded colours |

Each part is built on a Godot-free model in `src/Core/`, unit-tested in `tests/Tabletop.Client.Tests`; the Godot node only
renders the model.

## Unit card

**Which unit** (`SheetFocus`): the selected unit; with nothing selected, my team's activating unit; otherwise hidden. A
selection stays until the player clears it (clicking empty table) or selects another unit. An activation start
never changes a selection, and an enemy activation never takes the card: the header names it (*Header additions*).

**Contents**, top to bottom:

1. Team colour bar, unit name, `alive/size models`.
2. Quality and Defense as two badges, printed values from the snapshot (`UnitSheet.Stat`). The card prints the army
   list's values; what a rule does to them shows on the roll that uses them (*Target number basis*).
3. State chips: activated and the action, every condition on the unit (Shaken, Fatigued and any other by name), spell
   tokens, each grant with its caster and uses left. A condition's chip carries the theme's icon for that name when the
   theme has one, else the name alone.
4. Wounds: one row per model, pips for wounds left out of `ModelFacts.MaxWounds`; a removed model dimmed. A one-wound
   model is one pip. Clicking a model selects it, as today.
5. Weapons table: Weapon (×count), Range, Attacks, AP, Rules. AP is the `AP` property's parameter taken out of the rules
   column; `-` when absent. Hovering a rule shows its catalog text; *not automated* stays as today.
6. Special rules as chips with hover text.

**Hovered enemy.** With one of my units on the card and the pointer over an enemy unit, a compact strip appears beside
the card: the enemy's name, Defense, models alive, the distance (`HoverView`'s, closest base to closest base) and each of
my weapons' printed range, shown within or beyond that distance. The strip makes no claim about shooting; the sight
check (L) answers that.

The *This round* tab goes; its content is the combat log's *This unit* filter.

## Combat log

`CombatLog` (Core) turns the effective log into entries. `LogText` keeps the one-line wording for every event that is
not a roll.

**Roll entry.** One per `RollRecord` in a `StepCompleted`. The frame's `SequenceContext` (from its `SequenceStarted`)
names the actor, the targets and the weapons; `Names` gives their words, and a weapon id resolves to its snapshot name.
Collapsed:

```
Assault Squad → Orc Boyz · Rifles · Hit 5+ · ⚂ ⚄ ⚅ ⚅ ⚀ · 3 hits
```

Expanded:

- The target number's basis (*Target number basis*): `Quality 4+`, each change with its source, the result.
- Each roll modifier: `+1 cover`, `-1 AP`, `+1 player`, from `RollRecord.Modifiers`.
- Each die: raw face, modified result when it differs, pass or fail; a die judged on its raw face (`RequiredRaw`) is
  marked as such.
- Rerolls (`DiceRerolled`) and hand edits (`DiceEdited`) attach to the roll they name by `RollId`, each listing the
  faces it replaced. A player's `ModifierAdded` is its own line (`LogText`); a `player` modifier on a record shows with
  that roll's modifiers.
- `StepCompleted.Applied`: the rules that acted in the step, as chips with hover text.

A roll's result word is the roll's name and count (`3 hits`, `2 saves`, `passed`, `failed`) from the dice; the roll
names are the server's (`hit`, `defense`, `morale`, `casting`, …), capitalised.

**Activation block.** Entries from a `SequenceStarted` with `Context.Action` set through its `SequenceFinished` or
`SequenceAbandoned` group under one header: `Round 2 · Team 1 · Assault Squad · Advance`. A collapsed block shows a
chain summary of its rolls and changes in order, e.g. `Hit 3 → Defense failed 2 → 2 models removed → Morale passed`,
built from the roll entries and the `StateChange`s (wounds, models to the shelf, conditions set). Events outside any
action frame (round start, deployment, hand moves, edits) are single lines between blocks.

**Filters:** All, Rolls, My units, This unit (the unit on the card: blocks where it is the actor or a target).

**Kept from `LogPanel`:** undo to here, undone events struck through, click an entry to highlight what it touched
(`LogText.Touched`), the reroll and edit actions on a roll. The Animate toggle moves to Settings.

The drawer opens wider (640 px) and remembers its filter and open state in `ClientSettings`.

## Target number basis

**Server and protocol change.** `StatReader.TargetNumber` folds `replace_stat` and `modify_stat` rows into the number
silently; only `modify_roll` rows reach `RollRecord.Modifiers`. The roll record gains the basis:

```csharp
public sealed record ThresholdBasis(string Stat, int Printed, IReadOnlyList<ThresholdChange> Changes);
public sealed record ThresholdChange(string Source, int? SetTo, int Amount);   // SetTo for replace_stat, Amount for modify_stat
public sealed record RollRecord(..., int? RequiredRaw = null, ThresholdBasis? Basis = null);
```

- `StatReader.TargetNumber` returns the number and its basis; every caller that plans a roll passes the basis to
  `StepContext.Plan`, which puts it on the `RollPlan` and `Roller` writes it on the record. A threshold not read from a
  stat (casting's fixed 4+) carries none.
- A joined hero's own Defense or Quality (p.14) reads `Printed` from the slot it was read from; the morale test's best
  of the unit and its heroes records the winning one.
- Null in logs written before it; the client then shows the threshold alone.
- `EventApplier` does not read it. The viewer's generated types regenerate (`TABLETOP_WRITE_GENERATED=1`).
- `ProtocolVersion.Current` does not change: the field is optional and older clients ignore it.

## Dice tray

The latest roll as the combat log's collapsed roll entry, with dice faces from the theme. Clicking it opens the log at
that entry. With Animate on, the faces tumble for 0.4 s before settling; off, they appear settled. It still sits above
the decision box (`DiceTray.Follow`).

## Army roster

Left edge, above the unit card: my team's units, then each other team's, under team headers. A row is the unit name,
an activated tick, wound pips summed over the unit (`alive models` for one-wound units), and condition icons. A unit
with every model on the shelf is dimmed: off the table (not deployed, set aside, or destroyed by the log's moves).
Clicking a row selects the unit and centres the camera on it (`OrbitCamera.Centre`). The roster collapses to its team
headers with **R**.

`RosterView` (Core) reads only the projection: `UnitInPlay.Actions`, `Conditions`, model locations and wounds.

## Header additions

- After each team's score: `k not activated`, the team's units with at least one model on the table and no action
  recorded this round (`UnitInPlay.Actions` empty). The words say what the count is: the scheduler, not the client,
  decides who may activate.
- Objective markers as icons in the team colour of whoever controls each (`ObjectiveControlSet`), neutral uncoloured.
- When another team's unit is activating: `Team 2 activating: Orc Boyz · Charge` on the action line, as today's step
  strip already places it.

## Table markers and floating results

**Markers.** Over each unit, at its models' centroid above the tallest model, a billboard row of icons: activated this
round, each condition, and for a unit with any model above one wound, the wounded model's pips. Hidden while the camera
is at table level (End) so the sight check stays clean. Icons come from the theme by condition name, with a text chip
for a name the theme has no icon for.

**Floating results.** After a `StepCompleted` whose changes remove models, wound a model or set a condition, a label rises
over the unit for 1.2 s: `-2 models`, `-1 wound`, `Shaken`. Paced by the `AnimationQueue` with Animate on; with Animate
off they show together.

`UnitMarkers` (Core) computes each unit's icon list and floating results from the projection and the applied events.

## Range rings

With a unit of mine selected and **V** toggled on (off by default; remembered), one ring per distinct weapon range
around each living model's base, drawn as the union outline per range, labelled with the range in inches. Ranges are
the printed `Range` stat; melee weapons draw none. A rule that changes a range is not shown; the label reads the
printed range so a player can tell. `RangeRings` (Core) computes the outlines with `Tabletop.Geometry`, as `MoveRuler`
does for the move circle.

## Turn banner

A banner across the upper third for 1.5 s: `Round 2` on `RoundStarted`, and `Team 1's turn` on `TurnStarted` when the
team differs from the previous `TurnStarted`'s. Under the alternating scheduler that is each change of activating
team, kept short by the shorter text and a 1 s duration for turn banners. Clicks pass through. Off with Animate off, or
by the banner setting.

## Camera

`OrbitCamera` orbits a focus point; the focus point moves.

| Input | Does |
| --- | --- |
| **W A S D** | move the focus across the table, relative to the camera's heading; speed scales with distance; clamped to the table plus 12" |
| Right-drag | orbit, as today |
| Middle-drag | move the focus, as today |
| Wheel | zoom, as today |
| **Q / E** | turn the held unit while holding (unchanged); otherwise orbit left and right |
| Home, End | top-down, table level, as today |
| Space, Tab | centre on a unit, gliding 0.3 s instead of jumping |

WASD and Q/E read held keys every frame (`_Process`), so movement is continuous. They are ignored while a text field has
focus. `Hotkeys` gains `CameraForward`, `CameraBack`, `CameraLeft`, `CameraRight`; Q/E map to `TurnLeft`/`TurnRight` and
`TableInput` reroutes them to the camera when nothing is held. The key overlay (F1) lists the new keys.

**Follow.** When my team's or another team's action frame starts, the focus glides to the actor unit's centroid, unless
the player moved the camera in the last 2 s or is holding a unit. Off by the follow setting.

## Settings

A **Settings** dialog, opened from a gear button in the header and on the Home screen:

- Theme: Fantasy, Sci-fi. Applies at once.
- Animate (moved from the log drawer).
- Camera follow, turn banner, range rings by default.

`ClientSettings` gains `Theme` (default `scifi`), `CameraFollow`, `TurnBanner`, `RangeRings`, `LogFilter`. A missing or
unknown value reads the default.

## Themes

**A theme is data.** `UiTheme` (Core) is a record:

- Colours: background, panel, panel border, text, dim text, accent, success, failure, warning, team colours (one per
  team ordinal, falling back by cycling).
- Fonts: display (headings, badges) and body, as `res://` paths to bundled font files, with sizes.
- Panel shape: corner radius, border width, padding; an optional nine-patch texture path for panels and buttons.
- Dice: face style (pips or numerals), face and pip colours, an optional face texture.
- Icons: a map from condition name and marker kind (`activated`, objective) to an icon path.

`UiThemes` (Core) holds the built-in two by id (`fantasy`, `scifi`) and resolves an unknown id to `scifi`.

**`ThemeBuilder`** (Godot, `src/Theme/`) turns a `UiTheme` into a Godot `Theme`: `StyleBoxFlat` or `StyleBoxTexture` for
`PanelContainer`, `Button` states, `TabContainer`, `LineEdit`, `ItemList`, `TooltipPanel`; fonts and sizes; and named type
variations the Hud uses (`Badge`, `Chip`, `DieFace`, `Banner`, `Dim`). `Main` sets it on the root control, so every
screen inherits it, and rebuilds it when the setting changes. Hud code names variations and reads `UiTheme` colours; it
holds no colour literal. `Table/Palette` reads team colours from the theme.

**Assets** live in `client/content/themes/<id>/` (fonts, nine-patches, dice faces, icons). Fonts are SIL OFL; textures
and icons are drawn for this project (SVG where it can be). Each folder carries a `LICENSES.md` naming each font and its
licence.

**Fantasy:** parchment panels with a dark wood border, ivory text on dark wood for buttons, brass accent, a serif display
face (Cinzel) and a readable serif body (EB Garamond); bone dice with dark pips; icons in an engraved line style.

**Sci-fi:** dark translucent panels (85 % opacity) with a thin cyan border and clipped corners, amber warnings, a
geometric display face (Orbitron) and a condensed body (Rajdhani), numerals in a monospace (JetBrains Mono); dark dice
with lit numerals; icons in a thin glowing line style.

Adding a theme is a `UiTheme` value in `UiThemes` and its asset folder.

## HUD frame

One layout for every Hud part: an 8 px margin, the parts anchored to their corner or edge, and no two overlapping at
1280×720 and up. The roster and the unit card share the left column and split its height; the log drawer and the
dice tray never cover the decision box. Toasts use one style and stack top right. `HudLayout` (Core) computes the
rectangles from the viewport size and which parts are visible, and is tested at 1280×720, 1920×1080 and 2560×1440.

## Client structure

| Core (tested) | Godot |
| --- | --- |
| `SheetFocus` (changed), `UnitSheet` (AP column, wound pips) | `Hud/UnitCard`, replacing `StatSheet` |
| `CombatLog`, `RollEntry`, `ActivationBlock` | `Hud/CombatLogPanel` in `LogDrawer` |
| `RosterView` | `Hud/Roster` |
| `UnitMarkers` | `Table/UnitMarkerNodes`, `Table/FloatingText` |
| `RangeRings` | `Table/RangeRingNode` |
| `HudLayout` | `Screens/GameScreen.Play` places parts by it |
| `UiTheme`, `UiThemes` | `Theme/ThemeBuilder`, `Screens/SettingsDialog` |
| `Hotkeys` (new intents), `CameraFocus` (pan clamp, glide, follow rule) | `Table/OrbitCamera` |

`RoundHistory` is removed with the *This round* tab. `LogPanel` stays for lobby and setup.

## Testing

- Core models, each against projections and logs built in tests: which unit the card shows across selection and
  activations; `CombatLog` grouping, roll context, reroll and edit attachment, filters, chain summaries; roster counts;
  marker lists and floating results; ring outlines; `HudLayout` at three sizes; `CameraFocus` clamp and follow rule;
  `UiThemes` resolution and that every theme's paths are set.
- Server: `GfRolls` tests assert `Basis` on hit, defense and morale records, including a `modify_stat` row, a
  `replace_stat` row, a joined hero's own Defense and the morale best-of; `Roller` writes it; JSON round trip in
  `Tabletop.Protocol.Tests`; the viewer's generated types regenerated.
- End to end: a sim socket game (`dotnet run --project sim -- suite --driver socket --only core`) and a check that its
  records carry `Basis`.
- `./scripts/check-client`, and the owner's on-screen check of both themes.

## Out of scope

Hit odds or expected results before an attack (the client holds no rules; it would need a server question like the sight
check). Themes loaded from the user's folder. Edge-of-screen camera panning. Lobby and setup layouts beyond inheriting the
theme. 3D dice physics.
