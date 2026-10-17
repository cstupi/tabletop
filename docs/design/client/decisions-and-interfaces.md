# Client: key decisions and interfaces

## Key decisions

Contested or re-decided:

| Decision | Reason | Alternative rejected | Source |
| --- | --- | --- | --- |
| No social login. Email plus password, or email one-time code, on the client's own login screen | both methods are Basic-auth calls to Stytch through management and need no page to redirect back to | loopback listener and device code, both twice deferred by accounts and the web app to serve Google sign-in and magic links; neither method is wanted | client shell → No social login |
| Enforcement is three positions, `strict` refuses, and the decision box says so | action sequences and the scheduler both specify a refusing mode | identity and enumerations' boolean (warn or silent, never block) | whole-design review ruling 1 |
| Menu edits are accepted under every enforcement setting | they are the override path, visible to opponents and recorded with an actor | refusing them under `strict`, which would leave no way to fix a mistake | whole-design review ruling 1 |
| Animation is the player's setting, waiting is the game's | animation is presentation only; waiting changes what the server does and what the log records | resolution's "both set at game setup", with no column for either | whole-design review ruling 3 |
| One game screen for four statuses | all four are one socket and one hydrate path, and a status change happens inside the screen | a scene per status, which reloads the table on every change; one scene with panels toggled, which puts the game list and the table in one tree | client shell → Structure |
| Presence is relayed: measurements and ghosts go on the wire | a tape measure on a real table is visible to everyone at it; hidden pre-measuring is a different game | computing them locally and sending nothing | presence → What this corrects in `architecture.md` |
| Setup edits are a third message kind, not commands | setup needs a settled result rather than replay; terrain is then locked after setup with no flag and no check | placement as a stream of ordinary Commands | setup phase → Editing is free for all |
| One table frame, and the client converts nothing | the wire, `Tabletop.Geometry` and the client all use the corner frame | a `TableFrame` conversion layer in the client | geometry plan → Global Constraints |
| A model that is not `OnTable` is not drawn at all | `OnShelf` and `Inside` carry no placement, so there is no position to draw at and `PieceAt` returning null keeps every consumer correct | drawing an occupant at the container's position, which makes it selectable and measurable when it is neither | client shell → The table view; game flow → Inside a container |
| Two region functions, and `ExitRegion` returns a boundary | an occupant has no start to sweep rays from, so `Movement.Region` is meaningless; a boundary lets `MoveRegionNode` draw both with no change | a predicate (`BaseFullyWithin`) evaluated per candidate, which cannot express a ring of impassable terrain and needs a different node | plan Task 8; geometry plan Task 6 |
| The round boundary gets no control of its own | the one decision box is the whole affordance, and a second control would be a second path to the same command | an "End round" button on the game screen or team panel | plan Task 14 |
| A `ProjectionDesyncException` resyncs the client, bounded at one attempt per sequence position | the fault is a divergence in this client's copy, which a rebuild from the log fixes; a fault that made no progress is in the server's log, which a rebuild cannot fix | skipping the change and carrying on, which diverges silently; a fixed retry count, which either strands an unrelated later fault or loops on a batched hydrate; a `Resyncing` phase, which renders identically to `Reconnecting` | client shell → The connection and the projection |
| `MyTeamId` is derived from `Game.Participants` on every read, and a reconnect re-picks nothing | the participant row is the one record of a slot, and it outlives the socket; teams are locked from `playing` on, so a returning player's team cannot have changed while they were away | caching the team across `Reset()`, which would be a second source of truth for a value the `Welcome` already carries; treating a reconnecting player as teamless, which is what a spectator is and would cost a seat on every dropped connection | game flow → Teams are frozen at the flip, and a team is a slot |
| The client drops `AtRest` | nothing here waits for the server to settle, and the only busy state ends at `Welcome` | stopping a busy indicator on it, which would be a second and weaker source of truth for a settled table | contracts plan ruling 3; plan → Interfaces needed item 12 |
| `ManagementDtos.cs` mirrors management's records by hand | keeps `client/` free of a dependency on `server/management`, and the DTOs are stable | a shared DTO assembly in `libs/`, not proposed | plan → Interfaces needed item 4 |

Decisions the plan took where a spec was silent:

| Decision | Reason | Where it is recorded |
| --- | --- | --- |
| Everything Godot-free goes under `client/src/Core/` and is compiled into the test project | the tests cover the session, the management client, the socket seam, the cache, the built-in content and the view models, beside the projection applier and the geometry library | client shell → Testing |
| An owner-only file at `user://session.bin` for the remembered session | no keystore is portable, so `SessionStore` writes a plain file with `UnixFileMode.UserRead \| UserWrite` where the platform has file modes | client shell → Screens; the plan's ruling 2 |
| 20 Hz presence sampling | presence leaves the rate open; matches the server's relay rate and `architecture.md`'s figure | the plan's ruling 1 |
| Reconnect after any close the client did not ask for, except `Rejected` and `1000` after `welcome` | `Rejected` means the server refused this participant, and `1000` after `welcome` means a newer socket replaced this one; reconnecting after either would fight the server | client shell → The connection and the projection |
| Zones for ordinals with no team are drawn grey during setup | a four-zone style in a two-player lobby shows two coloured and two grey, so the table shows what the style laid out | setup phase → Zones; the plan's ruling 7 |
| A disembarking model takes any facing that fits | p.7 lets a model turn in any direction, so a placement is legal if some facing fits; `ExitRegion` unions over `MoveRuler.ExitYaws` and a circular base tries one | the plan's ruling 8 |
| The disembark region is the lead model's (`ModelIds[0]`) | matches what `Region` already does, and the server refuses outliers individually | the plan's ruling 9 |
| An enterable container is a click target, not a button | it is already highlighted because the legal set names it | the plan's ruling 10 |
| Eleven grouped meshes and five textures ship as built-in art | 35 meshes is not minimal, and the pieces differ by footprint, height, scale and yaw rather than by shape; a missing file still draws the hitbox | the plan's ruling 3 |

## Interfaces

### Produced for other modules

`Tabletop.Client.Core` is consumed only by `tests/Tabletop.Client.Tests` today. Later plans copy
these names verbatim: the scheduler and interpreter plans' end-to-end runs, and a replay viewer.

| Type | Members that matter |
| --- | --- |
| `sealed record ClientSettings(bool Animate, IReadOnlySet<string> MutedWindowKinds, bool RememberMe, string ManagementUrl, string GameServerUrl, LogFilter LogFilter, bool LogOpen)` with init properties `Theme`, `CameraFollow`, `TurnBanner`, `RangeRings` | `static Default`, `static Load(path)`, `Save(path)`, `Mute(kind)`, `Unmute(kind)` |
| `sealed class SessionStore(string path)` | `string? Read()`, `Write(jwt)`, `Clear()` |
| `sealed class ManagementClient(HttpClient, Uri baseUrl)` | `string? Jwt`; `SendOtp`, `VerifyOtp`, `LoginWithPassword`, `SignUp`, `Refresh`, `Logout`, `GetAccount`, `ListGames(after, limit)`, `CreateGame`, `GetGame`, `JoinGame`, `LeaveGame`, `EndGame`, `DeleteGame`, `ListProperties`, `ListArmies`, `GetArmy`, `ListLayouts`, `GetLayout`, `SaveLayout`, `GetManifest`, `ListAssets(kind, after)`, `GetAssetVolumes`, `GetDownload`, `GetRuleTexts(propertiesHash)` |
| `interface IGameSocket : IAsyncDisposable` | `event Action<SocketMessage> FrameReceived`, `event Action<SocketClosed> Closed`, `ConnectAsync(Uri, ct)`, `SendAsync(SocketMessage)`, `CloseAsync()` |
| `interface IGameSocketFactory` | `IGameSocket Create()` |
| `sealed record SocketClosed(int? Code, string? Reason, Exception? Error)` | |
| `sealed class ClientGameSocket : IGameSocket`, `ClientGameSocketFactory` | over `ClientWebSocket`, one `SocketMessage` per text frame |
| `sealed class PresenceTable` | `Set`, `Remove`, `Of`, `All`, `Clear` |
| `interface IDispatcher { void Post(Action) }`, `ImmediateDispatcher` | |
| `enum SessionPhase { LoggedOut, LoggedIn, Connecting, Connected, Reconnecting, UpdateRequired }` | |
| `sealed class Session(ManagementClient, IGameSocketFactory, SessionStore, ClientSettings, Action<ClientSettings>, IDispatcher, Func<int,TimeSpan>?, TimeProvider?)` | state `Phase`, `Account`, `Settings`, `Management`, `GameId`, `ParticipantId`, `Game`, `Draft`, `Applier`, `Projection`, `Log`, `Others`, `PendingUndoTo`, `LastCloseReason` (player text, `Feedback`), `MyTeamId`, `IsCreator`, `IsWatching`, `ReconnectAttempt`, `Rules` (`RuleTexts`, empty until fetched for the game's hash), `MaxWounds` (from the latest `ModelFacts`; an absent id is 1), `DeployInside` (from the latest `ModelFacts`), `CatchingUp` (from `Reset` or a board until `ModelFacts` or an empty events page), `Frame` (`RenderScheduler`), `CombatLog` (`CombatLogState?`, null without a game); events `PhaseChanged`, `GameChanged`, `BoardChanged`, `EventsApplied(IReadOnlyList<GameEvent>)`, `PresenceChanged(Guid)`, `Pinged(Ping)`, `RefusedReceived(Refused)`, `ConnectionLost(SocketClosed, bool)`, `FactsChanged`, `SightAnswered(SightAnswer)`, `AccountChanged`; methods `LoginWithPasswordAsync`, `SignUpAsync`, `SendOtpAsync`, `VerifyOtpAsync`, `TryResumeAsync`, `RefreshAsync`, `LogoutAsync`, `RenameAsync(name)`, `UpdateSettings`, `ConnectAsync(gameId)`, `DisconnectAsync`, `LeaveAsync`, `SendCommandAsync(Command) : Task<Guid>`, `SendEditAsync(Edit) : Task<Guid>`, `UndoToAsync(seq)`, `SendPresenceAsync`, `SendPingAsync`, `QuerySightAsync(queryId, observerUnitId, targetUnitId)` |
| `sealed record RuleTextDto(Name, Kind, TakesParameter, RuleText)`, `sealed class RuleTexts` | `static From(rows)`, `static Empty` (not loaded), `Loaded`, `TextOf(name)`, `IsAutomated(name)` (a catalog entry exists), `NotAutomated(name)` (loaded and no entry: the unit card's tag) |
| `sealed record BuiltinTerrain(string Name, string Label, Volume Volume, string? MeshFile)`, `static class BuiltinContent` | `Terrain`, `Textures`, `MarkerVisuals`, `ZoneStyleIds`, `Place(name, Point3, Yaw, scale)`, `VolumesOf(name)`, `TextureFile(name)`, `TerrainMeshFile(name)` |
| `enum Planet { Generic, Hospitable, Wasteland, Death }`, `static class TerrainGenerators` | `Generate(Planet, TableSize, Random) : IReadOnlyList<AddTerrain>`, `Fixed(Planet)`, `Rolled(Planet, int twoD6)` |
| `sealed class AssetCache(string root, HttpClient)`, `HashMismatchException(Expected, Actual)` | `PathFor(hash)`, `Has(hash)`, `FetchAsync(hash, url, ct)`, `static Sha256Hex` |
| `sealed class Names(GameInfo, Projection?)` | `Participant`, `Team`, `Unit`, `Model`, `UnitOfModel`, `Piece`, `static Short` |
| `static class LogText` | `Describe(GameEvent, Names)`, `Answer(AskAnswer, Names)`, `Touched(GameEvent)` |
| `enum AskMode { Choose, PickPieces, Allocate, Assign, Move, TokenSpend, Confirm }`, `sealed record AskView(Ask, AskMode, Highlights, AnsweredTeams, AutoPass)`, `static class AskViewModel` | `Mine(Projection, GameInfo, Guid? team, IReadOnlySet<string> muted)`, `WaitingFor`, `AnsweredBy`, `ModeOf(LegalSet)`, `SpendableBy(Projection, GameInfo, Guid team, TokenSpend)`, `ConfirmLabel(Ask)` |
| `enum MenuInput { None, Integer, Text, PieceId, TablePoint }`, `sealed record MenuItem(string Label, MenuInput Input, Func<string?, Command?> Build)`, `static class MenuModel` | `ForUnit`, `ForModel`, `ForTeam`, `ForMarker`, `ForFrame`, `KnownActionNames` |
| `sealed record RulerReadout(double Distance, double? Remaining, bool OverAllowance)`, `static class MoveRuler` | `Read`, `UsedInFrame`, `Region`, `ExitRegion`, `ExitYawAt`, `IsDisembark`, `IsShelfPlacement`, `TooClose`, `Gate` |
| `sealed class AnimationQueue` | `Enqueue`, `Take(bool animate)`, `Count` |
| `[Flags] enum RenderCause { None, Events, Game, Board, Facts }`, `sealed class RenderScheduler(IDispatcher)` | `Mark(RenderCause)`, `event Action<RenderCause> Render` (once per dispatcher turn) |
| `sealed class CombatLogCache`, `sealed class CombatLogState` | `Of(log, pendingUndoTo, GameInfo, Projection?) : CombatLogState`; `Names`, `Entries`, `Latest`, `Undo` (each built once) |

Godot layer, for completeness: `Main` (`static Instance`, `Session`, `Cache`, `Http`,
`Show(scenePath)`), `GodotDispatcher`, `WorldFrame` (`ToGodot`, `FromGodot`, `Radians`, `YawFrom`),
`Palette`, `TableView` (`Bind`, `Redraw`, `PieceAt`, `TerrainAt`, `MarkerAt`, `Pieces`, `Volumes`,
`Size`, `Camera`, `ShowExitGhosts`, `ClearExitGhosts`, `IsExitGhost`), `MeshLoader`
(`RefreshManifest`, `LoadAssetAsync`, `LoadBuiltin`), `TableInput` (`Bind`, `Tool`, `Selected`,
`Proposing`, `Eligible`, `Holding`, `Allowance`, `Select`, `PieceRightClicked`, `TableRightClicked`, `SelectionChanged`,
`PieceLeftClicked`, `UnitClicked`, `TargetClicked`, `ModelClicked`, `HoveredUnitChanged`, `HandMoved`, `IntentPressed`,
`ModelDragged`, `PickNextPiece`, `PickNextPoint`, `PlacementPoint`, `CancelDrag`), the panels, the Hud parts and the table overlays, each
of the last two with `Bind(Session, TableView, TableInput)`.

### Consumed from other modules

`Tabletop.Protocol` is the whole wire contract, and the only module the client depends on for game
semantics.

| Group | Names |
| --- | --- |
| version | `ProtocolVersion.Current`, `.Header` (`Tabletop-Client-Version`), `.UpgradeRequiredStatusCode` (426) |
| envelope | `SocketMessage`; up: `Connect(SessionJwt, GameId, ClientVersion)`, `CommandMessage(CommandId, Command)`, `EditMessage(EditId, Edit)`; down: `Welcome(ParticipantId, GameInfo)`, `GameInfoMessage(GameInfo)`, `BoardMessage(Board)`, `EventsMessage(IReadOnlyList<GameEvent>)`, `PresenceEnded(ParticipantId)`, `Refused(MessageId, RefusalReason, Detail)`, `Rejected(RejectReason)`, `AtRest(long Seq)` (dropped); both: `PresenceMessage`, `PingMessage`. `WireJson.Serialize/Deserialize` |
| game | `GameInfo`, `GameStatus`, `SchedulerShape`, `Enforcement`, `Team`, `Participant`, `Board(TableState Table, IReadOnlyList<ArmyInPlay> Armies)`, `ArmyInPlay` |
| projection | `EventApplier(Board, GameInfo)`: `Projection`, `Log`, `Apply`, `ApplyAll`, `ProjectTo(seq)`, `static EffectiveLog`; `ProjectionDesyncException`, thrown on an event naming an id the projection does not hold and carrying the event kind and that id; `Projection`: `Board`, `Units`, `Models`, `Markers`, `Scores`, `Scheduler`, `Stack`, `OpenAsks`, `LastSeq`; `UnitInPlay`, `ModelInPlay`, `SchedulerPosition`, `Frame`, `CompletedStep`, `OpenAsk` |
| location | `Location` = `OnTable(Placement)` \| `OnShelf` \| `Inside(Guid ContainerId)`; `ModelRelocation(ModelId, From, To)` |
| events | `GameEvent(Seq, Actor, Payload, Warning)`, every `EventPayload` and `StateChange`; the client reads `ModelsMoved`, `StepCompleted`, `ActionRecorded`, `MarkerPlaced`, `AskOpened/Answered/Closed`, `Undone`, `RoundStarted`, `TurnStarted/Ended` by name |
| asks | `Ask(Id, Question, Prompt, Teams, Legal, AllowPass, Sealed, WindowKind, FrameId)`, `AskOption`, `AskQuestions` (reads `EndRound` only); `LegalSet` = `Options` \| `PieceSelection` \| `Allocation` \| `Assignment` \| `MoveRegion` \| `TokenSpend` \| `Confirmation`; `MoveRegion(ModelIds, Allowance, ImpassableVolumeIds, DifficultVolumeIds, DifficultCap, MoveContainment? Containment)`, `MoveContainment(Enterable, ExitingId, ExitWithin)`; `AskAnswer` = `Chosen` \| `SelectedPieces` \| `Allocated` \| `Assigned` \| `Destinations` \| `TokensSpent` \| `Confirmed` \| `Passed` |
| commands | `AnswerAsk`, `MoveModels`, `PlaceMarker`, `MoveMarker`, `RemoveMarker`, `SetWounds`, `SetCondition`, `RecordAction`, `ClearActionRecord`, `AddGrant`, `RemoveGrant`, `SetSpellTokens`, `SetWoundsThisActivation`, `SetScore`, `SetActiveTeam`, `SetRound`, `Undo`, `AbandonSequence` |
| presence | `Presence(ParticipantId, Cursor?, Selection?, Placements, Measurement?)`, `Cursor(Point, Target)`, `Selection(PieceId, ModelIds)`, `PresencePlacement(PieceId, Position, Yaw, Kind)`, `PlacementKind { Held, Proposed }`, `Measurement(From, To)`, `Ping(Sender, Point, Target)` |
| edits | lobby: `CreateTeam`, `PickTeam`, `PickArmy`, `SetConfiguration`, `StartSetup`, `EndSetup`; setup: `SetTableSize`, `SetTexture`, `AddTerrain`, `MoveTerrain`, `ScaleTerrain`, `RemoveTerrain`, `OverrideTerrainVolumes`, `SwapTerrainMesh`, `AddSetupMarker`, `MoveSetupMarker`, `RemoveSetupMarker`, `SetZoneStyle`, `SwapZones`, `ReplaceDraft` |
| reasons | `RejectReason { UpgradeRequired, InvalidSession, NotAParticipant }`, `RefusalReason { OutsideLegalSet, Illegal, WrongStatus, NotPermitted, Unknown }` |

`Tabletop.Geometry`:

| Member | Used for |
| --- | --- |
| `Table.ContainsPoint(TableSize, Point2)` | the on-slab check; also the game server's `DraftEditor.OnSlab` rule |
| `ZoneStyles.All`, `.ShapesOf(style)`, `.Resolve(shapeId, TableSize)` | the toolbar's style list, `SetZoneStyle.Shapes`, and `ZoneNode`'s polygons |
| `Movement.Region(start, allowance, impassable, difficult, cap, table)` → `MovementRegion.Boundary` | the declared move's drawn region |
| `Movement.Check(...)` → `Distance`, `EffectiveAllowance`, `CrossesImpassable`, `LeavesTable` | the ruler readout, and single-placement legality of a disembark candidate (distance 0) |
| `Movement.Rays` (180), `Movement.Step` (0.25) | `ExitRegion`'s march-and-bisect loop |
| `Containment.BaseFullyWithin(model, container, inches)` | the disembark region, edge to edge from the container's base rather than a circle about its centre |
| `Collision.Slide`, `Collision.Separate`, `Collision.RestHeight(…)` | sliding and resting a dragged model or terrain piece; `DragBodies` names the walls and the other models |
| `Movement.Region(…, models, surfaces)` | the move region's boundary around the other models on the mover's level, the mover rested on the board's volumes (`MoveRuler.Region(…, others)`) |
| `Distance.ClosestPoints(PlacedModel, PlacedModel)` | a measurement's two points |
| `Point2`, `Point3`, `Yaw`, `Orientation`, `TableSize`, `Base`, `BaseShape`, `ModelBody`, `PlacedModel`, `Placement`, `Volume`, `VolumeKind`, `Footprint`, `FootprintKind`, `TerrainDeclarations`, `WorldVolume`, `Polygon` | values throughout |

`Tabletop.Content`: `Army`, `Unit`, `ModelSlot`, `Weapon`, `ArmySnapshot`, `TableState`,
`TerrainPlacement`, `TerrainVolume`, `Marker`, `MarkerKind`, `DeploymentZone`, `VisualRef`
(`OfBuiltin`, `OfAsset`), and `ContentJson.Options`. The client serialises every HTTP body with
`ContentJson` and every socket frame with `WireJson`.

Management HTTP: `GET /healthz`; `POST /v1/auth/otp`, `/otp/verify`, `/password`,
`/password/signup`, `/session/refresh`, `DELETE /v1/auth/session`; `GET /v1/account`;
`GET /v1/properties`; `GET /v1/games`, `POST /v1/games`, `GET /v1/games/{id}`,
`POST /v1/games/{id}/join`, `POST /v1/games/{id}/leave`, `POST /v1/games/{id}/end`, `DELETE /v1/games/{id}`, `GET /v1/games/{id}/assets`;
`GET /v1/armies`, `GET /v1/armies/{id}`; `GET /v1/layouts`, `GET /v1/layouts/{id}`,
`POST /v1/layouts`; `GET /v1/assets?kind=terrain|texture`, `GET /v1/assets/{id}/volumes`,
`GET /v1/assets/{id}/download`. `Page<T>(Items, Next)` with `?limit&after`; problem+json on
errors; another owner's row is `404`; a missing version header is accepted but the client always
sends one.

Game server socket: `ws://localhost:5200/ws`. `Connect` first inside the ten-second handshake
window; `Welcome`, then `BoardMessage` from `setup` on, then `EventsMessage`s (batched at 500,
empty when the log is empty); `Rejected` then close; `1000` on a replaced socket; `1001` on
shutdown; `Refused.MessageId` echoing `CommandId`/`EditId`; `PresenceEnded` when a participant's
last socket closes; lobby edits refused with `WrongStatus` from `playing` on; a `BoardMessage`
with the whole draft after every setup edit and a `GameInfoMessage` after every lobby edit; the
default draft 72×48 with texture `grass`. The JWT is verified once, at connect. An open socket is
never re-authenticated, so the once-a-minute refresh matters only for the next connect. Keep-alive
is Kestrel's WebSocket ping/pong (20 s interval, 60 s timeout). There is no protocol heartbeat
message and the client implements none. `AtRest(seq)` arrives when the server has nothing further
to emit; this client deserialises it and drops it.

