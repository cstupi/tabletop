# client

The Godot 4.7 / C# game client and the only project with a Godot dependency. A renderer plus a geometry engine over the server's wire protocol; it has no offline mode and holds no rules.

## Layout

- `Tabletop.csproj`: `Godot.NET.Sdk/4.7.2`, `net10.0`, references `Tabletop.Geometry`, `Tabletop.Content`, `Tabletop.Protocol` only.
- `src/Main.cs`: root node. Builds `Session`, `AssetCache`, `ClientSettings`, `SessionStore`; routes by `SessionPhase` to a scene.
- `src/Core/`: Godot-free logic.
  - `Session`: the one object that outlives screens: login, `ManagementClient`, the socket, `EventApplier`, draft, presence.
  - `ManagementClient`: the one HTTP client over management's `/v1`; `ManagementDtos`.
  - `IGameSocket`/`ClientGameSocket`: the socket seam and its `ClientWebSocket` implementation.
  - `IDispatcher`: posts to the main thread; `ImmediateDispatcher` in tests, `GodotDispatcher` in `src/Screens/`.
  - `AskViewModel`: `LegalSet` to `AskMode`, and the first open Ask for my team.
  - `AnimationQueue`, `MoveRuler`, `DeployView`, `SelectionView`, `PendingDrops`, `PresenceTable`, `MenuModel`, `LogText`, `Names`.
  - The play screen's models: `HeaderView`, `RoundHistory`, `UnitSheet`, `SheetFocus`, `DecisionView`, `TargetAssignment`, `CasualtyPick`, `HandMoveUndo`, `HoverView`, `SightSummary`, `ShotLines`, `Formation`, `Hotkeys` (`HotKey`, `Intent`), `RuleTexts`, `MarkerLocks`.
  - `AssetCache` (content-addressed), `BuiltinContent`, `TerrainGenerators`, `ClientSettings`, `SessionStore`.
- `src/Screens/`: `LoginScreen`, `HomeScreen`, `GameScreen` (+ `GameScreen.Play.cs`), `UpdateRequiredScreen`, `GodotDispatcher`.
- `src/Hud/`: the play Hud: `HeaderBar`, `StatSheet`, `DecisionBox` (+ `.Move`, `.Picks`), `DiceTray`, `LogDrawer`, `EndMessage`, `KeyOverlay`.
- `src/Table/`: the 3D table: `TableView`, `TableInput` (+ `.Keys`, `.Hold`), `HoverLabels`, `SightLines`, `TargetLines`, `Lasers`, `UndoMoveButton`, `OrbitCamera`, `WorldFrame`, `Animator`, `MeshLoader`, `PieceNode`, `TerrainNode`, `MarkerNode`, `SlabNode`, `ZoneNode`, `MoveRegionNode`, `RulerNode`, `PresenceOverlay`, `Palette`.
- `src/Panels/`: `LobbyPanel`, `LogPanel`, `PieceMenu`, `SetupToolbar`, `TeamPanel`, `Guarded`.
- `scenes/`: `Main.tscn` is the root that swaps `Login`, `Home`, `Game`, `UpdateRequired`; `TableView.tscn` is the table.
- `content/builtin/`: `textures/` (five table textures: ash, desert, grass, snow, urban) and `terrain/` (eleven grouped `.glb` meshes from `scripts/terrain-meshes`; `hill` is round, `hill-rect` square). A missing file draws the piece's volumes.

## How it works

- `Main` swaps one screen at a time on `Session.PhaseChanged`: `LoggedOut` to Login, `LoggedIn` to Home, `UpdateRequired` to UpdateRequired, anything else to Game.
- Management: `ManagementClient` sends the version header on every request and the bearer once `Jwt` is set. `Session` refreshes the JWT every 60 s from `Main`; a failed refresh backs off one minute.
- Game socket: `ClientGameSocket` raises `FrameReceived`/`Closed` on its own thread. `Session` posts them through `IDispatcher` and ignores frames from a socket that is no longer current.
- `Session.Handle` takes `Welcome`, then the board, then events. Events go into an `EventApplier`; `Session.Projection` is its `Projection`, or the `ProjectTo(seq)` prefix while an undo preview is pending.
- A `ProjectionDesyncException` triggers one resync per log position (`_desyncFloor`). A lost socket reconnects with backoff 1, 2, 5, 10 s unless the user closed it.
- `Session.EventsApplied`, `GameChanged`, `BoardChanged`, `RefusedReceived` are what panels subscribe to. Session outlives panels: every subscription comes off in `_ExitTree`.
- Commands: `SendCommandAsync(command, id)` takes the caller's message id so a `Refused` arriving before the send's continuation can still be matched. `SendEditAsync` is for setup edits.
- Animation: `Animator` feeds `EventsApplied` into `AnimationQueue`. The projection is already current; the queue only paces playback. `Settings.Animate` false plays everything at once, true one event per call.
- Ask: `AskViewModel.Mine` picks the lowest-id open Ask addressed to `MyTeamId` that the team has not answered; a watcher (no team) gets none. `ModeOf(LegalSet)` maps `Options`, `PieceSelection`, `Allocation`, `Assignment`, `MoveRegion`, `TokenSpend`, `Confirmation` to `AskMode`. `DecisionBox`, bottom centre over `DecisionView`, renders the mode and answers with `SendCommandAsync(new AnswerAsk(ask.Id, answer), id)`. A `Refused` for its last command shows as an orange refusal line and re-renders.
- Input: `TableInput` turns mouse and keys into presence (20 Hz timer), commands and edits. Picking casts the camera ray and tests each piece's `Bounds` (`Aabb`) with its own slab-method `RayHit`; the table plane is `Plane(Vector3.Up, 0)`. Geometry (collision, containment, movement) is `Tabletop.Geometry`; Godot only casts the ray.
- A drag is `DragBodies` (`HeldBody`, `Stationary`, `Walls`, `Models`, `Piece`) fed to `Collision.Slide`/`Separate`: a model slides along Impassable volumes, stops at other models' bases and rests on the terrain under it. During a move Ask `TableInput.Region` is the Ask's region and `MoveRuler.Region(…, others)` draws it around the other models. Alt places freely: no sliding, no stopping, the server judges (`move_overlap`, `move_through_models`, `move_separation`) under the enforcement setting; the F1 overlay and the move hint say so. Built-in `large-sight-blocker` is Impassable (p.11); built-in `ruin` is an Area: Difficult, Cover, BlocksLineOfSight, SeeInAndOut.
- During a move Ask, `TableInput.Proposing` holds the models being positioned: dragging streams proposed ghosts, release sends nothing, and the decision box commits on Enter. Outside one a drag is a hand move, judged under the game's enforcement; under `strict` it does not start. From play on an objective marker is locked (`TableInput.Locks`, a `MarkerLocks`): its drag does not start until the marker menu's local Unlock item frees it, and Lock locks it again.
- Play: `GameScreen.Play` builds the Hud over the table and the overlays in it; lobby and setup keep the side panel. Hud parts take keys in `_Input`, before the table's viewport; `TableInput` maps the rest through `Hotkeys.Of` and raises `IntentPressed`. Max wounds, sight and who can shoot come from the server (`ModelFacts`, `SightAnswer`); rule texts from `GET /v1/properties/{hash}/rules`.
- `WorldFrame` converts the wire frame to Godot: x = X, y = Z, z = -Y; yaw 0 faces +Y (Godot -Z), positive yaw counter-clockwise from above.
- `MeshLoader` fits meshes to a piece's volume. A glTF is Y-up and stands upright at pitch 90; a Z-up STL conversion at pitch 0.

## Commands

```bash
dotnet build client/Tabletop.csproj       # or `dotnet build` from the root
./scripts/check-client                    # grep checks, below
./scripts/client                          # build, check-client, launch Godot on Windows
./scripts/godot -e                        # open the editor on the Windows host
./scripts/godot                           # run without building
dotnet test tests/Tabletop.Client.Tests   # the Godot-free core
```

## Conventions and pitfalls

- `src/Core/` is compiled into `tests/Tabletop.Client.Tests` by `<Compile Include="../../client/src/Core/**/*.cs">`. A `using Godot` there breaks the tests; `check-client` fails on it. Core may reference only the three libs and the BCL.
- `check-client` also fails on: a physics body or collision shape in `client/scenes`; Godot physics types in `client/src`; a rules-engine type (`Tabletop.GameServer`, `IRulesEngine`, `CommandDispatcher`, `Legality`, and similar) in `client/src`.
- No Godot physics; picking is ray versus AABB.
- Godot's import step wipes `client/.godot/mono/temp/bin/`. Build after an import; otherwise every script fails with "class could not be found". `./scripts/client` builds in WSL first.
- `Directory.Build.props` gives each OS its own `obj/Unix` or `obj/Windows_NT`; a shared `obj/` breaks restore. `TabletopBuildSlot` adds a suffix to `obj/` and `bin/` for the compose server containers; the client does not set it.
- The Godot editor runs on the Windows host, so the client reads and writes under `OS.GetUserDataDir()` there: `settings.json` (`ClientSettings`: `Animate`, `MutedWindowKinds`, `RememberMe`, `ManagementUrl`, `GameServerUrl`, `LogFilter`, `LogOpen`), `session.bin` (the remembered JWT, `SessionStore`) and `cache/` (`AssetCache`). Settings are presentation only and never reach the server.
- `ClientSettings.Default` URLs use `127.0.0.1`, not `localhost`: Windows tries `::1` first, which does not reach WSL, and .NET waits 21 s before falling back.
- `Session` raises events on the main thread only through `IDispatcher`; tests use `ImmediateDispatcher`.
- `SessionStore.Read` returns null for anything that is not three non-empty dot-separated segments, so a corrupt file never reaches an Authorization header.
- `.tscn`, `.tres`, `.godot` and `.import` files are owned by the editor: hand-edit only when necessary, never reformat. Scenes are written minimal and owned by the editor afterwards.
- No scene may contain a physics body; dev tooling stays out of `client/`, since the editor scans everything under the project root.
- C# only, no GDScript. One world unit is one inch.
- `net10.0` runs under Godot's `net8.0` host because `GodotPlugins.runtimeconfig.json` sets `rollForward: LatestMajor`. If a Godot upgrade breaks C# loading, drop the TFM to `net8.0` first.

## Docs

- `docs/design/client/design.md`
- `docs/superpowers/plans/2026-09-05-client-shell/`
- `docs/superpowers/specs/2026-09-04-client-shell-design.md`
- `docs/superpowers/specs/2026-08-30-presence-design.md`
