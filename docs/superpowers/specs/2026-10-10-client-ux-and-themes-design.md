# Client UX and themes

Design session, 2026-10-10. The play screen's second pass: a unit card that shows the unit you are working with, a combat
log that shows every roll with its target number and modifiers, selectable themes (fantasy and sci-fi), an army roster,
table markers, range rings, turn banners, camera follow, WASD camera movement, and physical collision of models and
terrain. Builds on the in-game UX spec
(`2026-10-08-in-game-ux-design.md`); where the two differ, this one settles it.

## The owner's requirements

- The lower left shows the current unit's stats.
- Dice results are in the log with their modifiers, so a player can see exactly what happened at every step.
- The UI is themeable, with more than one theme; a fantasy and a sci-fi skin to start.
- Overall UX for players of these games: a roster with activations left, status markers on the table, weapon range
  rings, a turn banner, camera follow.
- The camera moves with WASD, not only orbits one fixed point.
- Models and terrain collide: nothing is ever inside anything else, models stand on hills, terrain rests on terrain.

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
| Physical collision | every drag; the server's placements | centre-point settling, flagged overlap |

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

**Hovered enemy.** With one of my units on the card and the pointer over an enemy unit, a compact strip appears above
the card, inside the left column: the enemy's name, Defense, models alive, the distance (`HoverView`'s, closest base to closest base) and each of
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

A roll's result word is the server's roll name and the count of successes (`3 hits`, `2 defenses`), or `passed` /
`failed` for one die; the client maps no roll name to other words, since that would name a rule.

**Activation block.** Entries from a `SequenceStarted` with `Context.Action` set through its `SequenceFinished` or
`SequenceAbandoned` group under one header: `Round 2 · Team 1 · Assault Squad · Advance`. A collapsed block shows a
chain summary of its rolls and changes in order, e.g. `Hit 3 → Defense 2 → 2 models removed → Morale passed`,
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

## Physical collision

**The requirement.** Nothing on the table is ever inside anything else. Every model and every terrain piece is a solid
body: dropped over something, it rests on top of it; dragged into the side of something, it stops at contact and slides
along it. A wood dropped on a hill sits on the hill; a model dragged onto a hill stands on it; two models' bases never
share space.

**Today.** Nothing collides. A model on the table never climbs a hill: the drag settles only onto a top within 0.5" above
the model's current height (`TableInput.Hold.cs:50`), and the server settles only at or below the height the client sent
(`Battlefield.Settle`), so a model at Z 0 stays at Z 0 inside the hill. Settling tests the base's centre point, not the
base. Hand moves and deployment are not settled by the server at all. Terrain pieces are never settled: a piece dropped
on another keeps Z 0 and the two meshes interpenetrate. Base overlap is flagged, never prevented (`Overlap`,
`docs/architecture.md`).

**Bodies.** Collision reads the geometry the rules already use: a model is its base outline extruded to its height
(`PlacedModel`); a terrain piece is its volumes (`WorldVolume`). Each mesh is fitted to its piece's volumes
(`MeshLoader`), so what is drawn is what collides.

- A **Solid** volume is solid to everything: models and terrain rest on its top and stop at its sides.
- An **Area** volume (woods, ruins, fields, water) is ground a model stands *in*: models enter it and rest on what is
  under it; terrain dropped on it rests on what is under it. Its own floor, when its piece is lifted onto a hill, is the
  piece's base height.
- **Walls and climbing.** A dragged model stops at the sides of a Solid declared `Impassable` (or listed impassable by
  the open move Ask) and climbs every other Solid. Uploaded terrain keeps its author's declarations.
- **Elevated built-ins are impassable.** p.11: *"Terrain features that are over 3” tall count as elevated terrain, and
  are impassable"*. The built-in `large-sight-blocker` (6") declares `Impassable`.
- **Ruins are walk-in ground.** Ruins are passable and Difficult, seen into and out of but not through (owner ruling).
  The built-in `ruin` is an Area declaring Difficult, Cover, BlocksLineOfSight and SeeInAndOut.
- **Hill meshes are flat-topped.** The built-in hill, steep-hill, small-hill, dunes and lair meshes are rebuilt as
  plateaus at their volume's top with a sloped rim inside the footprint, so a model on the hill stands on the drawn
  surface.

**`Tabletop.Geometry.Collision`** (new; pure values, tested):

- `RestHeight(outline, volumes, ignore)`: the highest Solid top under any part of the outline, else 0. No step-up
  limit; whether a climb is allowed is the server's terrain judgement (p.11), not physics. Replaces the centre-point
  `Settling.SurfaceHeightAt` at every caller.
- `Slide(moving, from, to, obstacles)`: the farthest point toward `to` the moving outline reaches without its interior
  entering an obstacle's outline at an overlapping height, then the remainder of the drag projected along the contact
  edge. Obstacles are other models' bases and the sides of the Solid volumes the caller names as walls: during a
  move Ask the Ask's impassable volumes, otherwise every Solid declared `Impassable`. Every other Solid is climbed.
- `Separate(bodies)`: for a group drag, each dragged body slides against the stationary ones and against the dragged
  bodies already placed, in drag order, so a formation compresses against an obstacle instead of overlapping.

**Client.**

- Every drag of a model (a unit's move, deployment, a hand move) slides and rests through `Collision`. Holding **Alt**
  while dragging places freely, for corrections; the dropped position still rests on what is under it.
- Every drag of a terrain piece during setup rests the piece on the Solid tops under its footprint (`TerrainTransform`
  Z) and slides it against Solid pieces it cannot rest on. Alt places it freely. Models are not on the table in setup.
- The move-range circle (`Movement.Region`) takes other models' bases as obstacles, as it takes impassable volumes, so
  the reach drawn goes around models.

**Server.**

- `Battlefield.Settle` rests every placement it applies through `Collision.RestHeight` over the model's base and ignores
  the client's Z. Hand moves (`MoveModels`) and deployment placements are settled the same way, so a stored position is
  never inside terrain whatever the client sent. Setup terrain edits rest the piece the same way (`DraftEditor`).
- `GfMoves.ContactBreach` judges p.7's clauses, decided by the enforcement setting like every other breach:
  - `move_through_models`: *"may never move through other models or units (friendly or enemy), even if they are taking
    a Charge action"*: the straight path from start to destination crosses another model's base.
  - `move_overlap`: *"or be placed in physically impossible locations"*: the destination overlaps another model's base.
  - `move_separation`: *"Models may never be within 1\" of models from other units, unless they are taking a Charge
    action"*: the destination is within 1" of another unit's model, except on a Charge (`MoveStep.ignoreSeparation`).
- `HandMoves.Judge` adds `move_overlap` to a hand move's breach, so `warn` names it and `strict` refuses it.
- `docs/architecture.md`'s "base overlap, terrain intersection … flagged rather than prevented" becomes: the client
  prevents them while dragging, Alt overrides, and the server judges them under the enforcement setting.

**Out of scope.** p.11's climb cost (*"instead of having to climb up 1\" and down 1\""*): a climb is not charged against
the move distance. Collision of models with the trees or walls drawn inside an Area volume: an Area is entered as a
whole, as p.11 plays it.

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
| `Tabletop.Geometry.Collision` (shared with the server) | `Table/TableInput.Hold` drags, `Panels/SetupToolbar` terrain drags |

`RoundHistory` is removed with the *This round* tab. `LogPanel` is removed: lobby and setup never showed a log.

## Testing

- Core models, each against projections and logs built in tests: which unit the card shows across selection and
  activations; `CombatLog` grouping, roll context, reroll and edit attachment, filters, chain summaries; roster counts;
  marker lists and floating results; ring outlines; `HudLayout` at three sizes; `CameraFocus` clamp and follow rule;
  `UiThemes` resolution and that every theme's paths are set.
- Server: `GfRolls` tests assert `Basis` on hit, defense and morale records, including a `modify_stat` row, a
  `replace_stat` row, a joined hero's own Defense and the morale best-of; `Roller` writes it; JSON round trip in
  `Tabletop.Protocol.Tests`; the viewer's generated types regenerated.
- Geometry: `Collision` rest height under a base half over a hill, slide along a model and a wall, a climbable top
  climbed, a group compressing against an obstacle; terrain resting on terrain.
- Server: a hand move, a deployment and a move answer sent at Z 0 over a hill are stored on the hill; the three new
  breaches under each enforcement setting, the Charge exemption for separation.
- End to end: a sim socket game (`dotnet run --project sim -- suite --driver socket --only core`) and a check that its
  records carry `Basis`.
- `./scripts/check-client`, and the owner's on-screen check of both themes.

## Out of scope

Hit odds or expected results before an attack (the client holds no rules; it would need a server question like the sight
check). Themes loaded from the user's folder. Edge-of-screen camera panning. Lobby and setup layouts beyond inheriting the
theme. 3D dice physics.
