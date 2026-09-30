# Client Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Godot client's frame in `client/`: three screens over one session object, the management HTTP client, the game socket, the projection built by `Tabletop.Protocol.EventApplier`, presence, the table view with hitbox-first pieces and runtime GLB loading into a content-addressed cache, the setup-phase tools and the four terrain generators, the Ask panel, the log panel with undo-to-here, and the right-click menu — with every Godot-free part extracted under `client/src/Core/` and tested headlessly.

**Architecture:** `client/src/Core/**` is Godot-free C# (`Tabletop.Client.Core`): `Session` owns `ManagementClient`, `IGameSocket`, the `EventApplier`, the draft and a `PresenceTable`, and handles every socket frame and close through an `IDispatcher`. Its other events are raised on the thread of the awaited call that caused them, so each Godot consumer posts its handler to the main thread. `tests/Tabletop.Client.Tests` compiles those sources directly against the three `libs/`, which is what proves they are Godot-free. Godot code (`Tabletop.Client.Screens`, `.Table`, `.Panels`) is thin: it draws from the projection, converts frames with `WorldFrame`, and forwards input to `Session`. Godot physics is not in the scene; picking is ray-versus-AABB over proxy bounds, and every measurement is `Tabletop.Geometry`'s.

**Tech Stack:** Godot 4.7.2 Mono (Windows host), .NET 10 (`net10.0`, C# 12), `System.Net.Http`, `System.Net.WebSockets.ClientWebSocket`, xUnit 2.9.3; `Tabletop.Geometry`, `Tabletop.Content`, `Tabletop.Protocol` from the contracts and geometry plans.

**Spec:** `docs/superpowers/specs/2026-09-04-client-shell-design.md` (primary); `2026-08-30-presence-design.md`; `2026-09-04-setup-phase-design.md`; `2026-08-29-setup-content-design.md`; `2026-08-27-table-domain-model-design.md`; `2026-08-28-game-flow-design.md`; `2026-08-28-resolution-design.md`; `2026-09-03-action-sequences-design.md`; `2026-09-04-scheduler-design.md`; `2026-08-28-asset-library-design.md`; `2026-08-29-geometry-values-design.md`; `2026-09-04-whole-design-review.md` (newest, wins); `docs/architecture.md`; Grimdark Future v3.5.1 pages 7 and 17–18. Earlier plans obeyed verbatim: `docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md`, `2026-09-05-geometry.md`, `2026-09-05-management-api/README.md`, `2026-09-05-game-server-skeleton.md`.

**Requires landed:** the contracts, geometry, management and game-server skeleton plans. The rules plans are not required: Task 14's Step 4 exercises the Ask panel against a scratch registration of the skeleton's test-only `FixedAskEngineFactory`, or waits for the scheduler's server, and marks the interpreter- and scheduler-dependent checks as conditional.

## Global Constraints

- `client/Tabletop.csproj` stays `Godot.NET.Sdk/4.7.2`, `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`, `RootNamespace Tabletop`; the SDK globs `**/*.cs` so no file needs registering. `Directory.Build.props` puts `obj/` under `obj/$(OS)/`; the Windows Godot build of the referenced libs produces `obj/Windows_NT/` beside WSL's `obj/Unix/`.
- C# only; no GDScript. `.tscn`/`.tres`/`.godot`/`.import`/`.uid` files are editor-owned: this plan writes minimal `.tscn` files by hand (a root node and its script) and builds every control tree in `_Ready`; nothing reformats a scene the editor has saved.
- **No rules type, physics body or legality check in `client/`** (`architecture.md` → The load-bearing seam; client shell → What must not be in the client). No reference to `Tabletop.GameServer`; no `StaticBody3D`, `RigidBody3D`, `CharacterBody3D`, `Area3D` or `CollisionShape3D` in any scene or script. `scripts/check-client` greps for both (client shell → Testing). Drawing a region the server offered is not a legality check: `MoveRuler.Region` and `MoveRuler.ExitRegion` render parameters the Ask carried, through `Tabletop.Geometry`, and the server re-decides every answer — `LegalSetMembership.Contains` (`server/game/Dispatch/LegalSetMembership.cs`) under `strict`, and `GfMoves.ExitPlacement` for a disembark (interpreter plan Task 14). The client never withholds an answer on its own geometry.
- **No truth outside the projection**: screens read `Session` and keep only what they draw. The projection is `Tabletop.Protocol.Projection`, written only by `EventApplier`.
- One table frame everywhere — on the wire, in `Tabletop.Geometry` (`Table`, `ZoneStyles`, `Movement(table:)`) and in this client: origin at the table's corner, `0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up (geometry plan → Global Constraints, game server plan → `DraftEditor.OnSlab`). Nothing converts between table frames. Yaw 0 faces +Y, positive yaw counter-clockwise from above (geometry plan). Godot: `x = X`, `y = Z`, `z = −Y`, `rotation.y = yaw` in radians (`WorldFrame`).
- One world unit = one inch; Godot units are inches too. The slab is drawn 1" thick (setup content → Thickness is a render constant).
- The namespace `Tabletop.Client.Table` shadows the type `Tabletop.Geometry.Table` wherever both are in scope, so every use of the on-slab check is written `Tabletop.Geometry.Table.ContainsPoint(...)` outside `tests/`. Nothing else in the client names a bare `Table` type.
- Every HTTP request carries `Tabletop-Client-Version: {ProtocolVersion.Current}`; a `426` shows the update-required screen and nothing else runs (management API → The version check). The JWT is refreshed with `POST /v1/auth/session/refresh` every 60 s (management plan → Interfaces needed → Client-shell plan). Only a `401` signs out; any other failure keeps the token and the next refresh waits a full minute (Task 5).
- Server wins, always: a `models_moved` event snaps the piece; `Undo` shows `EventApplier.ProjectTo(n)` until the server's `undone` or `refused` arrives; reconnect discards the projection and rebuilds from `Welcome`/`Board`/`Events` (client shell → The connection and the projection). A `ProjectionDesyncException` discards it the same way and resyncs immediately, once per sequence position, and the bound sends the player Home with the reason.
- Presence is sent at 20 Hz while something changed and never logged; setup drags stream `held` placements and the drop is one edit; play drags the same and the drop is one `MoveModels` (presence; setup phase → The draft is the row).
- Local settings (animate, muted window kinds, remember me, server URLs) live in `user://settings.json`; the remembered session in `user://session.bin` (a plain file, owner-only where the platform has file modes; no Windows-only mechanism, because the client may run on other operating systems); the mesh cache in `user://cache/` keyed by SHA-256 hash.
- **The client is not Windows-only.** Windows is the first export target and the only one this plan builds a preset for, but no design decision here may depend on Windows (`architecture.md` → Deliverables). `OS.GetUserDataDir()` and `res://`/`user://` are portable, and the remembered session is a plain file rather than `ProtectedData` (Task 2). WSL plus a Windows Godot editor is the description of the development machine, a fact about the current machine and not a platform commitment.
- Nothing but `Connect` is sent before `Welcome`. Presence and pings drop, and commands and edits throw `InvalidOperationException` (Task 5).
- Godot-layer rules, in every node and panel:
  - Session subscriptions are fields, removed in `_ExitTree`.
  - No exception escapes `async void`; handlers catch `Exception` last, or run through `Guarded` (Task 13).
  - After an await, check `IsInstanceValid(this)` before touching a node.
  - Never read a global transform before the node is in the tree.
  - A Session event may arrive off the main thread; the handler posts through `IDispatcher`, as `Main` does.
- Identifiers are `Guid.CreateVersion7()` (client-generated ids for terrain placements, markers, commands, edits).
- Local only: management at `http://localhost:5100`, the game server at `ws://localhost:5200/ws`, MinIO presigned URLs at `http://localhost:9000`, all over WSL's forwarded localhost (contracts plan → Local development).
- Test-driven for everything with logic: xUnit in `tests/Tabletop.Client.Tests` over `client/src/Core/**`; the Godot layer is tested by running `./scripts/godot` against the local servers. Commit after every task, one-line imperative message, with the `Co-Authored-By` trailer naming the model.

---

## File Structure

```
client/Tabletop.csproj                       modified: ProjectReferences to the three libs
client/scenes/Main.tscn                      modified: root Node (Main.cs) that swaps screens
client/scenes/Login.tscn  Home.tscn  Game.tscn  UpdateRequired.tscn  TableView.tscn   minimal: root + script
client/src/Main.cs                           modified: owns Session, builds the dispatcher, swaps screens
client/src/Core/                             Godot-free; compiled into the test project by source inclusion
  IDispatcher.cs           where Session handles socket frames; ImmediateDispatcher for tests
  ClientSettings.cs        settings.json: animate, muted window kinds, remember me, URLs
  SessionStore.cs          session.bin: the remembered JWT in an owner-only file, no keystore
  ManagementDtos.cs        the management API's request/response shapes the client reads
  ManagementClient.cs      HttpClient over /v1/*: version header, bearer, 426 -> UpdateRequiredException
  IGameSocket.cs           the socket seam: Send, frames in, close reason
  ClientGameSocket.cs      ClientWebSocket over WireJson
  PresenceTable.cs         others' presence by participant
  Session.cs               the session object: login, connect sequence, projection, undo prediction, reconnect, edits
  BuiltinContent.cs        built-in terrain (volumes + declarations), textures, marker visuals, zone styles
  TerrainGenerators.cs     the four planet tables (GF p.18-19) -> a batch of AddTerrain edits beside the slab
  AssetCache.cs            content-addressed disk cache: download, verify hash, path by hash
  Names.cs                 ids -> words (participants, teams, units, models, pieces)
  LogText.cs               GameEvent -> words, with a naming context
  AskViewModel.cs          Ask -> what the panel shows, who answered, mute decisions
  MenuModel.cs             right-click items -> commands
  MoveRuler.cs             ruler readout: distance, remaining allowance, the move region, the disembark region, distance used from the frame
  AnimationQueue.cs        events between the projection and the renderer
client/src/Screens/  GodotDispatcher.cs  LoginScreen.cs  UpdateRequiredScreen.cs  HomeScreen.cs  GameScreen.cs  GameScreen.Play.cs
client/src/Table/    WorldFrame.cs  TableView.cs  SlabNode.cs  PieceNode.cs  TerrainNode.cs  MarkerNode.cs
                     ZoneNode.cs  OrbitCamera.cs  MeshLoader.cs  TableInput.cs  RulerNode.cs  MoveRegionNode.cs  PresenceOverlay.cs
                     Palette.cs  Animator.cs
client/src/Panels/   Guarded.cs  LobbyPanel.cs  SetupToolbar.cs  AskPanel.cs  LogPanel.cs  PieceMenu.cs  TeamPanel.cs
client/content/builtin/                      textures/*.png (5) and terrain/*.glb (10 group meshes); absence draws the proxy
tests/Tabletop.Client.Tests/                 xUnit; <Compile Include="../../client/src/Core/**/*.cs" />
scripts/check-client                         the two greps: no physics body in a scene, no rules type in a script
scripts/client                               runs the client (./scripts/godot) after building the libs
CLAUDE.md                                    modified: layout, commands, the Core rule
```

Files split by responsibility: `Core/` holds every decision that can be tested without an engine; `Table/` draws; `Panels/` are the UI the game screen composes; `Screens/` are the three scenes plus the update screen. A `Core` file may reference `Tabletop.Geometry`, `Tabletop.Content`, `Tabletop.Protocol` and the BCL, and nothing else.

## Tasks

One file per task, in plan order, and one file per group of steps where a task runs long; each task ends green and committed.

- [Task 1: Reference the libs, the client test project and the two greps](task-01-reference-libs-client-test.md)
- [Task 2: Local settings and the remembered session](task-02-local-settings-remembered-session.md)
- [Task 3: The management client](task-03-management-client.md)
- [Task 4: The socket seam, the real socket, and the presence table](task-04-socket-seam-real-socket.md)
- Task 5: The session object
  - [5a: interfaces and the session tests](task-05a-session-tests.md)
  - [5b: the refresh and sign-out tests](task-05b-refresh-sign-out-tests.md)
  - [5c: `IDispatcher` and `Session`](task-05c-session-object.md)
- [Task 6: Built-in content and the four terrain generators](task-06-built-content-terrain-generators.md)
- [Task 7: The content-addressed asset cache](task-07-content-addressed-asset-cache.md)
- Task 8: View models: log text, the Ask view, the menu, the ruler, the animation queue
  - [8a: interfaces and the tests](task-08a-view-model-tests.md)
  - [8b: `Names` and `LogText`](task-08b-names-log-text.md)
  - [8c: `AskViewModel` and `MenuModel`](task-08c-ask-view-menu.md)
  - [8d: `MoveRuler` and `AnimationQueue`](task-08d-ruler-animation-queue.md)
- [Task 9: `Main`, the dispatcher, screen swapping, the login and update-required screens](task-09-main-dispatcher-screen-swapping.md)
- [Task 10: The Home screen — the game list, create game, join](task-10-home-screen-game-list.md)
- Task 11: The table view: frame, slab, proxies, terrain, markers, zones, camera, mesh loading
  - [11a: interfaces, the frame and the nodes](task-11a-table-view-nodes.md)
  - [11b: `MeshLoader` and `TableView`](task-11b-mesh-loader-table-view.md)
- [Task 12: Input and presence — select, drag, drop, the ruler, measure, ping, others' cursors and ghosts](task-12-input-presence-select-drag.md)
- [Task 13: The Game screen — status routing, the lobby panel and the setup toolbar](task-13-game-screen-status-routing.md)
- [Task 14: The Ask panel and the declared move](task-14-ask-panel-declared-move.md)
- [Task 15: The log panel with undo-to-here, the right-click menu, animation, Leave](task-15-log-panel-undo-here.md)
- [Task 16: The run script, `CLAUDE.md`, and the whole-tree check](task-16-run-script-claude-md.md)

## Interfaces → Produces, per namespace

Later plans (the scheduler and interpreter plans' end-to-end runs, a replay viewer, the web app where it shares a DTO) copy these names verbatim. `IRL<T>` abbreviates `IReadOnlyList<T>`.

### `Tabletop.Client.Core` (Godot-free; `client/src/Core/`, compiled into `tests/Tabletop.Client.Tests`)

| Type | Members |
| --- | --- |
| `sealed record ClientSettings(bool Animate, IReadOnlySet<string> MutedWindowKinds, bool RememberMe, string ManagementUrl, string GameServerUrl)` | `static Default`, `static Load(path)`, `Save(path)`, `Mute(kind)`, `Unmute(kind)` |
| `sealed class SessionStore(string path)` | `string? Read()`, `Write(jwt)`, `Clear()` |
| management DTO mirrors | `AccountRow`, `SessionResponse`, `OtpSentResponse`, `Page<T>`, `GameSummary`, `CreateGameRequest`, `PropertiesSummary`, `ArmySummary`, `LayoutSummary`, `AssetKind`, `AssetDto`, `DownloadResponse`, `ManifestEntry`, `Manifest` (Task 3 shapes); `ManagementException(Status, Title, Detail)`, `UpdateRequiredException` |
| `sealed class ManagementClient(HttpClient, Uri baseUrl)` | `string? Jwt`; `SendOtp`, `VerifyOtp`, `LoginWithPassword`, `SignUp`, `Refresh`, `Logout`, `GetAccount`, `ListGames(after, limit)`, `CreateGame`, `GetGame`, `JoinGame`, `LeaveGame`, `ListProperties`, `ListArmies`, `GetArmy`, `ListLayouts`, `GetLayout`, `SaveLayout`, `GetManifest`, `ListAssets(kind, after)`, `GetAssetVolumes`, `GetDownload` |
| `sealed record SocketClosed(int? Code, string? Reason, Exception? Error)`; `interface IGameSocket`; `interface IGameSocketFactory` | `FrameReceived`, `Closed`, `ConnectAsync(Uri, ct)`, `SendAsync(SocketMessage)`, `CloseAsync()`; `Create()` |
| `sealed class ClientGameSocket : IGameSocket`; `ClientGameSocketFactory` | over `ClientWebSocket`, one `SocketMessage` per text frame |
| `sealed class PresenceTable` | `Set`, `Remove`, `Of`, `All`, `Clear` |
| `interface IDispatcher { Post(Action) }`; `ImmediateDispatcher` | |
| `enum SessionPhase { LoggedOut, LoggedIn, Connecting, Connected, Reconnecting, UpdateRequired }` | |
| `sealed class Session(ManagementClient, IGameSocketFactory, SessionStore, ClientSettings, Action<ClientSettings> persist, IDispatcher, Func<int,TimeSpan>? backoff, TimeProvider? time)` | state: `Phase`, `Account`, `Settings`, `Management`, `GameId`, `ParticipantId`, `Game`, `Draft`, `Applier`, `Projection` (prediction while undo pending), `Log`, `Others`, `PendingUndoTo`, `LastCloseReason`, `MyTeamId`, `IsCreator`; events: `PhaseChanged`, `GameChanged`, `BoardChanged`, `EventsApplied(IRL<GameEvent>)`, `PresenceChanged(Guid)`, `Pinged(Ping)`, `RefusedReceived(Refused)`, `ConnectionLost(SocketClosed, bool willReconnect)`; `LoginWithPasswordAsync`, `SignUpAsync`, `SendOtpAsync`, `VerifyOtpAsync`, `TryResumeAsync : Task<bool>`, `RefreshAsync : Task<bool>`, `LogoutAsync`, `UpdateSettings`, `ConnectAsync(gameId)`, `DisconnectAsync`, `LeaveAsync`, `SendCommandAsync(Command) : Task<Guid>`, `SendCommandAsync(Command, Guid id) : Task<Guid>`, `SendEditAsync(Edit) : Task<Guid>`, `UndoToAsync(seq)`, `SendPresenceAsync`, `SendPingAsync` |
| `sealed record BuiltinTerrain(Name, Label, Volume, MeshFile?)`; `static class BuiltinContent` | `Terrain` (35 names, Task 6), `Textures` (5), `MarkerVisuals`, `TerrainMeshGroups` (10), `ZoneStyleIds`, `Place(name, Point3, Yaw, scale)`, `VolumesOf(name)`, `TextureFile(name)`, `TerrainMeshFile(name)` |
| `enum Planet { Generic, Hospitable, Wasteland, Death }`; `static class TerrainGenerators` | `Generate(Planet, TableSize, Random) : IRL<AddTerrain>`, `Fixed(Planet)`, `Rolled(Planet, int twoD6)` |
| `sealed class AssetCache(root, HttpClient)`; `HashMismatchException(Expected, Actual)` | `PathFor(hash)`, `Has(hash)`, `FetchAsync(hash, url, ct)`, `static Sha256Hex` |
| `sealed class Names(GameInfo, Projection?)` | `Participant(Guid?)`, `Team`, `Unit`, `Model`, `UnitOfModel`, `TeamOfModel`, `Piece`, `static Short` |
| `static class LogText` | `Describe(GameEvent, Names)`, `Answer(AskAnswer, Names)`, `Touched(GameEvent)` |
| `enum AskMode`; `sealed record AskView(Ask, Mode, Highlights, AnsweredTeams, AutoPass)`; `static class AskViewModel` | `Mine(Projection, GameInfo, Guid? team, muted)`, `WaitingFor(...)`, `AnsweredBy(log, askId, names)`, `ModeOf(LegalSet)`, `SpendableBy(Projection, GameInfo, Guid team, TokenSpend)`, `ConfirmLabel(Ask)` |
| `enum MenuInput { None, Integer, Text, PieceId, TablePoint }`; `sealed record MenuItem(Label, Input, Build)`; `static class MenuModel` | `ForUnit`, `ForModel`, `ForTeam`, `OffTable(Projection, teamId, Names)`, `ForMarker`, `ForFrame`, `KnownActionNames` |
| `sealed record RulerReadout(Distance, Remaining, OverAllowance)`; `static class MoveRuler` | `Read(start, current, allowance)`, `UsedInFrame(Frame, modelId)`, `Region(PlacedModel start, MoveRegion, IRL<WorldVolume> volumes, TableSize) : IRL<Point2>`, `ExitRegion(ModelBody body, PlacedModel container, MoveRegion, IRL<WorldVolume> volumes, TableSize) : IRL<Point2>`, `IsDisembark(MoveRegion) : bool` |
| `sealed class AnimationQueue` | `Enqueue`, `Take(animate)`, `Count` |

### Godot layer (`Tabletop`, `Tabletop.Client.Screens`, `.Table`, `.Panels`)

| Type | Members |
| --- | --- |
| `Main : Node` | `static Instance`, `Session`, `Cache`, `Http`, `Show(scenePath)`; routes on `PhaseChanged` |
| `GodotDispatcher : IDispatcher` | `Post` via `CallDeferred` |
| `LoginScreen`, `UpdateRequiredScreen`, `HomeScreen`, `GameScreen : Control` | `GameScreen.Table`, `.Input`, `Toast(text)`; `partial void ShowPlayPanels(bool)` |
| `static class WorldFrame` | `ToGodot(Point3)`, `FromGodot(Vector3)`, `Radians(Yaw)`, `YawFrom(float)` |
| `static class Palette` | `Team(ordinal)`, `ForDeclarations`, `Proxy`, `Selected`, `Ghost`, `Unassigned`, `Translucent(Color)` |
| `TableView : Node3D` | `Bind(Session)` (once), `Redraw()`, `PieceAt`, `TerrainAt`, `MarkerAt`, `Pieces`, `Volumes`, `Size`, `Camera`, `ShowExitGhosts(IRL<Guid>, Guid container)`, `ClearExitGhosts()`, `IsExitGhost(Guid)` |
| `SlabNode`, `PieceNode`, `TerrainNode`, `MarkerNode`, `ZoneNode`, `RulerNode`, `MoveRegionNode`, `OrbitCamera`, `PresenceOverlay` | Tasks 11, 12, 14 |
| `sealed class MeshLoader(AssetCache, ManagementClient, Guid gameId)` | `RefreshManifest() : Task` (refetched at `playing`, on a miss and past `ExpiresAt`), `LoadAssetAsync(assetId, camera, piece)`, `LoadBuiltin(name, fit)`, `static Orient(scene, scale, Orientation)` |
| `TableInput : Node` | `Bind(TableView, Session)`, `Tool`, `Selected`, `Proposing`, `Allowance`, `PieceRightClicked`, `TableRightClicked`, `SelectionChanged`, `PieceLeftClicked`, `ModelDragged`, `PickNextPiece`, `PickNextPoint`, `CancelDrag()` |
| `static class Guarded` | `Run(Func<Task>, Action<string> report)`, `Send(Session, Edit)`, `Command(Session, Command, Action<string> report)`, all `async void` and catching every exception |
| `LobbyPanel`, `SetupToolbar(GameScreen)`, `TeamPanel(Team)`, `AskPanel`, `LogPanel` (`Bind(Session, TableView, report)`), `PieceMenu(Session, report)` (`ItemChosen`) | Tasks 13–15 |
| `sealed class Animator(Session, TableView) : IDisposable` | Task 15 |

### Files and scripts

`tests/Tabletop.Client.Tests` (xUnit, compiles `client/src/Core/**` from source); `scripts/check-client`; `scripts/client`; `user://settings.json`, `user://session.bin`, `user://cache/{hash}.bin`; `client/content/builtin/textures/{name}.png`, `client/content/builtin/terrain/{name}.glb` (optional).

---

## Interfaces needed from other modules

1. **The table frame.** One frame, the corner (`0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up), on the wire and in `Tabletop.Geometry`; the geometry plan's `Table`, `ZoneStyles` and `Movement` tests pin it, and this plan has no conversion layer. `Table.ContainsPoint` is the client's on-slab check and `ZoneStyles.Resolve` draws without conversion.
2. **Contracts plan, `MoveRegion` and `MoveContainment`.** `MoveRegion(IRL<Guid> ModelIds, double Allowance, IRL<Guid> ImpassableVolumeIds, IRL<Guid> DifficultVolumeIds, double? DifficultCap, MoveContainment? Containment = null)` with `MoveContainment(IRL<Guid> Enterable, Guid? ExitingId, double? ExitWithin)` (contracts plan Task 8; the sixth member is optional, so every five-argument construction compiles). `MoveRuler.Region` resolves both id lists against the board's volumes and passes the cap through to `Movement.Region`; `MoveRuler.ExitRegion` reads `Containment.ExitWithin` and draws the container-relative region instead when `ExitingId` is set; `AskPanel.OfferEmbark` turns `Enterable` into click targets that answer `Destinations` with `Inside(container)` for every model of the unit. Consumed exactly as the contracts plan states: `Enterable` and `ExitingId` are never both set (that is where *"never both in one activation"* lives), `Allowance` binds nothing on a disembark, and a null `Containment` means containment plays no part in this move.
3. **Management plan, `ManifestEntry`.** `ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation)`, read from the owner's `asset` row by management plan Task 12. The client needs the import corrections to draw another participant's mesh, which `GET /v1/assets/{id}` (owner-only) cannot supply; `MeshLoader.Orient` applies them (Task 11).
4. **Management plan, the client's endpoints.** Its *Client-shell plan* obligations list everything this plan calls: `/v1/auth/*`, `/v1/account`, `/v1/games*` and the manifest, `/v1/properties`, `/v1/armies` (list and get), `/v1/layouts` (list, get, save), plus `GET /v1/assets?kind=terrain|texture` (the setup toolbar's "Add my terrain", own textures), `GET /v1/assets/{id}/volumes` (a library terrain's volumes on add and on "Reset volumes") and `GET /v1/assets/{id}/download` (an own asset not yet in the manifest) — all owner-only. The DTO mirrors in `ManagementDtos.cs` must stay byte-compatible with `Tabletop.Management`'s records under `ContentJson`; a shared DTO assembly is not proposed.
5. **Contracts plan, action names.** The `action` rows (name, distance) never reach the client, so the menu's "mark as having taken …" derives names from `ActionRecorded` events and `UnitInPlay.Actions`, with free text beside them. It reads nothing from the activate Ask: its options are labelled `"{unit name}: {action}"` with `Id` `"{unitId:N}/{action}"` (scheduler design → The turn is an Ask; interpreter plan Task 8 `ActivationOption.For`), the client renders `Label` and `Pieces` and parses neither, and `GameInfo` gains no `Actions` list. The first activation of a game records its action, so the menu is empty only until then.
6. **Contracts plan, `AskQuestions.EndRound`.** A player ends the round (scheduler → There is no round limit), which needs one more engine-owned question name, `EndRound = "end_round"`, beside `Activate`, `ActOrEndPhase`, `Deploy`, `DeployScouts`, `DeployAmbusher`, `Confirm`, `React`, `SpendTokens`. The client reads it in exactly two places — `AskViewModel.ConfirmLabel` (the button says "End the round", not "Continue") and nothing else, since `LogText`'s new line keys on the `Confirmed` answer rather than the question. Nothing else in the Protocol changes: the Ask is the existing `Ask` with a `Confirmation` legal set, `AllowPass` false, `Sealed` false, `FrameId` null, addressed to every team that has a participant.
7. **Game server plan, consumed as stated:** `Connect` first within ten seconds; `Welcome`, then `BoardMessage` from `setup` on, then `EventsMessage`s (an empty one when the log is empty), then live frames; `Rejected` then close; `1000` on a replaced socket; `Refused.MessageId` echoing `CommandId`/`EditId`; `PresenceEnded`; lobby edits refused with `WrongStatus` from `playing` on; `BoardMessage` with the whole draft after every setup edit; the default draft 72×48 with texture `grass` (which `BuiltinContent.Textures` therefore ships).
8. **Management plan, consumed as stated:** `Tabletop-Client-Version` on every request, `426` as `application/problem+json`; `POST /v1/auth/session/refresh` accepting an expired JWT; `Page<T>(Items, Next)` with `?limit&after`; another owner's row is `404`.
9. **Geometry plan, consumed as stated:** `ZoneStyles.ShapesOf(style)` as `SetZoneStyle.Shapes`; `ZoneStyles.Resolve(shape, size)` for drawing; `Distance.ClosestPoints` for measurements; `Movement.Region(...).Boundary`; `Movement.Check(...)`'s `CrossesImpassable` and `LeavesTable`, asked of a single placement by passing that placement as both start and destination; `Containment.BaseFullyWithin(model, container, inches)` — edge to edge from the container's base, not a circle about its centre (geometry plan Task 6), which `MoveRuler.ExitRegion` marches into a boundary; `Settling.SurfaceHeightAt(Point3, volumes)` for a dragged model; yaw 0 faces +Y, counter-clockwise positive; an oval's length along the facing axis.
10. **Sample data, not a contract:** the contracts plan's `TableStateTests.Sample()` names `hill-small`, `objective-ring`, `grass` and the zone shapes `frontline/1`, `frontline/2` (which `ZoneStyles` resolves); the same plan's `Samples` adds `chip` (`Samples.Token`) and `ruin` (its `SwapTerrainMesh` sample); the game server plan's `DraftEditor` tests add the texture `desert` and its default draft the texture `grass`. `BuiltinContent` ships all six so every plan's samples render, and an unknown zone shape (the game server plan's `DraftEditor` tests use `long-edges/*` and `corners/*`, plain strings that name no `ZoneStyles` shape) draws nothing with a warning. `Samples.Game` and `Samples.Board` are expression-bodied properties that mint new Guids per read, so a test that needs one game reads it into a `static readonly` first (Task 5).
11. **Interpreter plan, consumed as stated:** the move step's Ask is question `move` with a `move_region` whose `Containment` is non-null exactly when the move may embark or is disembarking (Task 14); `transport-destroyed`'s `place` step opens a second question, `disembark`, with a `move_region` whose `Containment.ExitingId` is the wreck and whose `Allowance` is the exit distance (Task 19); `GfNames.ExitWithin` is 6" in Grimdark Future and reaches the client only inside `MoveContainment.ExitWithin`, never as a constant here; a `Destinations` answer is applied as sent, with the server dropping an `Inside` whose container is off the table and leaving inside any model placed beyond the exit radius. No ruleset question name is added to `AskQuestions`.

12. **Contracts plan, `ProjectionDesyncException` and `AtRest`.** `ProjectionDesyncException`, thrown by `EventApplier` in place of `KeyNotFoundException` and carrying the event kind and the unknown id, is caught in `Session` at the two places that apply events (Task 5). The client reads only the type and `Message`; it does not branch on the kind or the id, and the exception's own members are the contracts plan's to shape. `AtRest(long Seq)`, wire kind `at_rest`, server to client, is a `SocketMessage` this client deserialises and drops: `Handle`'s switch has no default arm. When it is sent, whether it is per connection or broadcast, and what it means when nothing was appended are the game server's to decide, and no client behaviour depends on any of those answers.

13. **Contracts plan, `TokenSpend` and the Takedown pick.** A team's spell token spend arrives as `TokenSpend(IReadOnlyDictionary<Guid,int> MaxByModel)`, wire kind `token_spend`, `Sealed`, `AllowPass`, `WindowKind` `casting`, question `spend_tokens`, answered by `TokensSpent(IReadOnlyDictionary<Guid,int> Counts)`, wire kind `tokens_spent` (p.13). Seven legal-set kinds and eight answer kinds, unchanged in number: the contracts plan does not reuse `Allocation`, because an allocation's counts sum exactly to its amount, which is right for wounds and wrong for *"any number"* up to a maximum, and because one total with no per-target bound cannot distinguish five tokens across two models holding one and four from five across two holding three and two. The client consumes it as stated. The cap is the map's value, the team's maximum is the sum of its own entries, and the map covers every addressed team's models because p.13's window addresses both — the caster's side spends for +1 and the other side for -1. That discloses nothing, since every projection already carries each model's `SpellTokens`; `AskViewModel.SpendableBy` narrows the map to my own team's models for both the spin boxes and the highlights. `AskMode.TokenSpend` has its own control rather than borrowing `AskMode.Allocate`'s: the allocation widget is one total split exactly across its targets, and this is per-model bounds with no total at all. A Takedown's chosen model arrives as `PieceSelection` over the target unit's model ids with `Min` and `Max` both 1 — no wire change at all, no new type, and no new question name read here, since the panel keys on the legal set and `AskViewModel.ConfirmLabel` is still the only place a question name is read.

## Rulings

1. **Presence sample rate: 20 Hz** for the client's own samples (`TableInput`'s timer), matching the game server plan's relay rate and `architecture.md`'s figure ([open questions](../specs/2026-09-09-open-questions-design.md)). A different figure changes one `Timer.WaitTime`.
2. **Where the remembered session lives: `user://session.bin`**, a plain file created owner-only (`UnixFileMode.UserRead | UserWrite`) where the platform has file modes and covered by the user profile's ACL on Windows, because the client may run on other operating systems and so takes no Windows-only mechanism. It protects the token from other local accounts on a shared machine and from a group- or world-readable directory being backed up; it does not protect it from anything running as the same user, nor from the file being copied to another machine, where it refreshes until the Stytch session expires. A per-platform store behind one interface is rejected in Task 2 for cost and for libsecret's keyring dependency. If an OS keystore is required later, `SessionStore` is three methods with one call site.
3. **Built-in art: a minimal set ships**, so a generated table looks like a table. **The files are art, and this plan cannot produce them**: ten `.glb` group meshes and five `.png` textures under `client/content/builtin/`, listed with their format and budget in Task 6 Step 4, which is a step for a person with a modelling tool and not a step code performs. What the plan does produce is the grouping (each of the 35 built-ins names one of the ten meshes through `BuiltinTerrain.MeshFile`) and the fit-to-volume scaling in `MeshLoader.LoadBuiltin` (Task 11) that lets one mesh serve a group. Hitbox-first rendering is the degraded path for every file that is missing.
4. **Generator entries with alternatives** (*"Hill, Forest, or Field"*, *"2 Large Forests / Large Fields"*): **the client's RNG picks one alternative.** Rolling a planet table produces a finished board in one action, and a player who dislikes a piece swaps or deletes it during setup, which the toolbar offers (`SwapTerrainMesh`, `RemoveTerrain`). `TerrainGenerators.Rolled` returns the alternatives an entry names and `Generate` chooses from them (Task 6).
5. **Dice on the table: a roll is a readout, not dice objects.** Text beside the unit gives the faces rolled, the threshold, the modifiers applied and which dice passed, and nothing tumbles; models still fall by tween. Godot physics is out of the scene (`architecture.md` → The load-bearing seam), so a die on the table would be a canned animation pretending to be a roll whose result the server has already decided, and the readout says more, because it shows the threshold and the modifiers a tumbling die cannot. `Animator` reads out every roll on the step (Task 15).
6. **The game list's filters: status, name and "created by me"**, all three read off the `GameSummary` rows `GET /v1/games` returns, so the filtering is client-side over a list the browser has. **"Games I am in" is not built: `GameSummary` carries no participants**, so the filter would need either a new field on it or a `GET /v1/games/{id}` per row to look the participants up; the first is a one-column change in the management plan and the second is a request per row, and neither is taken (Task 10).
7. **Zones for ordinals with no team: drawn grey during setup** (setup phase → Zones; client shell → The table view). A four-zone style in a two-player lobby shows two coloured zones and two grey, so the table shows what the style laid out rather than hiding half of it until teams form (Task 11).
8. **The facing a disembarking model takes: any facing that fits.** p.7 says *"Models may move and turn in any direction regardless of their facing"*, and a unit exits by using an ordinary move action, so a placement is legal if **some** yaw fits — `MoveRuler.ExitRegion` and `GfMoves.ExitPlacement` try more than one ([the 3.5.1 rebase](../specs/2026-09-06-ruleset-3-5-1-design.md) §4). `Location.Inside` carries no yaw, because nothing is preserved. Only oval bases and models with no base are affected; a circular base's footprint does not depend on its yaw (Tasks 8 and 14).
9. **Whose base the disembark region is drawn for: the lead model's**, exactly as `MoveRuler.Region` does: one polygon, from `ModelIds[0]` ([open questions](../specs/2026-09-09-open-questions-design.md)). A unit with mixed base sizes sees the lead's region, and the server refuses an outlier individually (`GfMoves.ExitPlacement` leaves it inside). If every distinct base should get its own boundary, `MoveRegionNode.Show` takes a list of polygons and nothing else changes.
10. **Whether an offered container is a click target or a button: a click on the container** ([open questions](../specs/2026-09-09-open-questions-design.md)), which is highlighted because `AskViewModel.Mine` puts `Enterable` in `Highlights` — *"the legal set from the server becomes the panel's choices and the table's highlights"* — with the container's name in the panel's note so the affordance is discoverable. One control for one command, as for the round boundary: a button per container in the panel would be a second control for the same command. If buttons are wanted, `OfferEmbark` adds one `Btn` per container and drops the `PieceLeftClicked` hook, and `TableInput.CancelDrag` is then unused.
