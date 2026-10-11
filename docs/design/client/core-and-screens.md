# Client: Core, the Godot layer and screens

## Core and the Godot layer

`client/src/Core/**` is `Tabletop.Client.Core`: plain C# that may reference `Tabletop.Geometry`,
`Tabletop.Content`, `Tabletop.Protocol` and the BCL, and nothing else. `tests/Tabletop.Client.Tests`
compiles those files directly:

```xml
<Compile Include="../../client/src/Core/**/*.cs" LinkBase="Core" />
```

A Godot dependency in a `Core` file breaks the test project's build. There is no assembly boundary
to hide behind: the Godot SDK produces one assembly for `client/`, and a second project cannot
reference it without referencing Godot.

Above it sit three Godot namespaces with one job each. `Tabletop.Client.Screens` swaps scenes and
composes panels. `Tabletop.Client.Table` draws the world and reads the mouse.
`Tabletop.Client.Panels` are the controls the game screen assembles by status. They call into
`Core`, hold node references, and keep nothing that is not on screen.

Some things inside Godot cannot be tested by compiling. The spec asks for two scripted checks. The
plan implements four greps in `scripts/check-client`, each exiting non-zero on a hit:

| Check | Pattern | Why a grep |
| --- | --- | --- |
| No physics body in a scene | `type="(StaticBody3D\|RigidBody3D\|CharacterBody3D\|Area3D\|CollisionShape3D\|CollisionPolygon3D)"` over `client/scenes` | scene files are editor-owned data; nothing else reads them |
| No physics in a script | the same class names plus `PhysicsServer3D`, `PhysicsBody3D` over `client/src` | Godot physics would be an answer the rules depend on |
| No rules type in a script | `Tabletop.GameServer`, `IGameRules`, `GameRulesRegistry`, `IRulesEngine`, `EnforcementPolicy`, `LegalSetMembership`, `CommandDispatcher`, `Legality` | there is no compile-time barrier: both build in one solution |
| `Core` is Godot-free | `using Godot` under `client/src/Core` | belt and braces over the compile check |

The bare word `GameRules` is not matched. It is a string field on `GameInfo` and
`PropertiesSummary` that the Home screen displays. A denylist costs maintenance as the rules
engine grows type names. A reflective escape passes it, such as `ClassDB.Instantiate("StaticBody3D")`
or a scene loaded from `user://`. It is a tripwire, not a sandbox.

## Screens and the session object

```mermaid
flowchart TD
  main["Main : Node<br/>static Instance, Session, Cache, Http, Show(path)"]
  session["Session<br/>outlives every screen"]
  login["LoginScreen"]
  home["HomeScreen"]
  update["UpdateRequiredScreen"]
  gamescr["GameScreen<br/>lobby, setup, playing, ended"]
  lobby["LobbyPanel"]
  setup["SetupToolbar"]
  hud["Hud: HeaderBar, Roster, UnitCard, DecisionBox, DiceTray,<br/>LogDrawer, CombatLogPanel, TurnBanner, EndMessage, KeyOverlay, Toasts"]
  table["TableView + TableInput + PresenceOverlay<br/>+ HoverLabels, SightLines, TargetLines, Lasers, RangeRingNode,<br/>UndoMoveButton, UnitMarkerNodes, FloatingText"]

  main -- owns --> session
  main -- "one at a time, by Phase" --> login
  main --> home
  main --> update
  main --> gamescr
  gamescr -- "by game.Status" --> lobby
  gamescr --> setup
  gamescr -- "playing, ended" --> hud
  gamescr -- "always" --> table
  login -- reads --> session
  home -- reads --> session
  gamescr -- reads --> session
  table -- reads --> session
```

A status change happens inside the game screen. It is never a scene swap. `GameScreen` creates
one `TableView` and binds it once for the whole game, and a second `Bind` throws. It draws the
draft in `lobby` and `setup` and the projection's board from `playing` on.

`Main.Route` maps `Session.Phase` to a scene path and returns early when the path is unchanged, so
`Connecting → Connected → Reconnecting` all stay on `Game.tscn` and the table is not rebuilt.
The spec rejected two alternatives. One scene with panels toggled puts the game list and the table
in one tree for no reason. A scene per game status reloads the table on every status change, and
contradicts the server's view that lobby, setup and play are one connection.

`Session` is the single owner of everything that outlives a screen:

| Owns | Member |
| --- | --- |
| the management client and the JWT | `Management`, and `ManagementClient.Jwt` set by `Complete()` |
| the socket while one is open | private `_socket`, created through `IGameSocketFactory` |
| the projection | `Applier : EventApplier`, `Projection`, `Log` |
| the setup draft | `Draft : TableState?` |
| others' presence | `Others : PresenceTable` |
| the undo prediction | `_predicted`, `PendingUndoTo`, `_pendingUndoCommand` |
| reconnect and resync policy | `_attempt`, `_backoff`, `_userClosed`, `_welcomed`, `_desyncFloor` |
| local settings | `Settings`, persisted through the `persistSettings` delegate |

It raises eight events: `PhaseChanged`, `GameChanged`, `BoardChanged`, `EventsApplied`,
`PresenceChanged`, `Pinged`, `RefusedReceived`, `ConnectionLost`. Every one goes through
`IDispatcher.Post`. `GodotDispatcher` posts via `Callable.From(action).CallDeferred()`, so socket
frames arriving on the read loop's thread are handled on Godot's main thread.
`ImmediateDispatcher` runs them inline for tests. That seam and its two implementations are the
client's whole threading model.

One session object costs size: roughly forty public members across login, connection, projection
and presence. Accepted because the spec's structure requires a single owner, and because the type
is Godot-free and so the most heavily tested one in the client.

## Themes and settings

A theme is data. `UiTheme` (Core) holds the id and name, the palette (`Background`, `Panel`, `PanelBorder`, `Text`,
`DimText`, `ButtonText`, `Accent`, `Success`, `Failure`, `Warning`), six team colours, three fonts (`Display`, `Body`,
`Numerals`, each a `ThemeFont` of path, size and weight), a `PanelShape`, a `DiceStyle` and an icon map keyed by
condition or marker kind. `Rgba` stands in for Godot's `Color`, which Core cannot name. `UiTheme.Team(ordinal)` wraps
the six colours by team ordinal; `Icon(name)` is null when the theme has none. `PanelShape` is a corner radius, border
width, padding, a `Clipped` flag (corner detail 1, cut corners) and optional nine-patch textures for panels and buttons.
`DiceStyle` is `Pips` (a face texture per value) or `Numerals`. `UiThemes` holds `Fantasy` (parchment, Cinzel and EB
Garamond, nine-patch panels, pip dice) and `SciFi` (dark glass, Orbitron, Rajdhani and JetBrains Mono, clipped flat
panels, numeral dice); `Resolve(id)` falls back to `SciFi`, the `DefaultId`.

`Main` is a `Node`, so a `Theme` set on it reaches no screen. `Main.ApplyTheme` calls `Ui.Apply(theme)`, which has
`ThemeBuilder.Build` make the Godot `Theme`, and assigns it to the tree root and to the current screen's root `Control`
(again on every screen swap). It also sets the default clear colour to `Background`. `ThemeBuilder` (`src/Theme/`,
namespace `Tabletop.Client.Theming`) styles `Panel`, `PanelContainer`, popups and dialogs, `Button` in four states,
`LineEdit`, `ItemList`, `TabContainer`, label and button colours and the default font. A nine-patch button is
modulated from grey, lightened for hover and pressed and darkened when disabled. `CheckBox` draws no button stylebox and
takes `Text`. It also builds the type variations: `Variation.FlatButton` over `Button` (`Text`, `Accent` when pressed,
`DimText` when disabled), which every `Flat` button sets because it draws on the panel and not on a button face;
`Variation.Badge`, `Chip` and `DieFace` over `PanelContainer`; and over `Label` `Dim`, `Accent`, `Success`,
`Failure`, `Warning`, `Display`, `Numerals` and `Banner` (outlined, 48 px). Variable fonts are applied through a
`FontVariation` at the theme's weight.

`Ui` is the static view of the current theme for Godot code: `Ui.Theme`, `Ui.Current`, `Ui.Of(Rgba)`, `Ui.Team(ordinal)`,
`Ui.Accent/Success/Failure/Warning`, `Ui.Icon(name)` (cached, cleared on a change) and the `Changed` event. Hud, panel
and screen code names a colour through `Ui` or sets a `ThemeTypeVariation`; it never writes a colour literal.
A team colour on a label is a `font_color` override, never `Modulate`, which would multiply the theme's text colour.
On `Ui.Changed` the Hud parts that draw from the theme re-render themselves (`HeaderBar`, `Roster`, `UnitCard`, `EndMessage`,
`DiceTray`, `CombatLogPanel`), the table overlays that do too (`UnitMarkerNodes`, `FloatingText`, `RangeRingNode`), and `GameScreen` re-tints the team panels, redraws the table when bound and gives the
undo button `Ui.Theme`; nothing is recreated, so typed input survives. `DiceFaces.Of(Die)` makes a die as a
`DieFace` panel with the theme's face texture or a numeral, the modified value after an arrow when it differs, and
dimmed when the die failed.

`ClientSettings` holds `Theme` (an id, resolved on load so an unknown one reads as the default), `CameraFollow` (true),
`TurnBanner` (true) and `RangeRings` (false), each nullable in the DTO. `SettingsDialog` (`Screens/`), opened from the
header and from Home, is an `AcceptDialog` with a theme `OptionButton` and checkboxes for `Animate`, camera follow,
turn banner and range rings; each change goes through `Session.UpdateSettings` and applies at once, and a theme change
reaches `Main` through `Session.SettingsChanged`.

`scripts/check-client` is the colour gate: `new Color(`, `new Godot.Color(`, `Colors.`, `Color.FromHtml`, `Color.Color8`,
`Color.FromHsv`, `Color x = new(` and target-typed `Modulate`, `SelfModulate` or `Color = new(` under `client/src/Hud`,
`Panels` and `Screens` fail the check. `Table/` draws in world colours through `Palette`, whose team colours come from
the theme. Assets sit in `client/content/themes/<id>/`: `fonts/`, `icons/`, and for `fantasy` also `panel.svg`,
`button.svg` and `dice/1.svg` to `6.svg`; each folder carries its `LICENSES.md`.
