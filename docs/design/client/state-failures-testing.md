# Client: state, failure modes and testing

## State and data

The client owns no Postgres tables and writes none, so there is no `erDiagram` here. Every row it
touches belongs to management or to the game server, and it reaches them only through `/v1/*` and
the socket. What it reads out of those rows, by the column names in `docs/schema.md`: `game.name`,
`game.status`, `game.created_by` (which decides whether the creator's two buttons appear),
`game.enforcement`, `game.waits_between_steps`, `game.properties_hash`, `game.game_rules`,
`game.scheduler_shape`; `team.ordinal`; `participant.team_id` (nullable, and a participant with no
team is a watcher) and `participant.army_snapshot`; the whole `table_state` tree, including
`terrain_placement.builtin` beside a nullable `terrain_placement.asset_id` and each placement's
own `terrain_volume` rows; and `asset.hash`, `asset.scale` and `asset.orientation_pitch/yaw/roll`.
Those asset columns reach it through the per-game manifest rather than the asset row, because
`GET /v1/assets/{id}` is owner-only. It writes one owner-side row, through management:
`POST /v1/layouts` from its own copy of the draft.

Local, per-machine state is three files in `UserData.Dir`: a release build's is `OS.GetUserDataDir()`,
which resolves per platform (`%APPDATA%\Godot\app_userdata\Tabletop\` on Windows,
`~/.local/share/godot/app_userdata/Tabletop/` on Linux, `~/Library/Application Support/Godot/app_userdata/Tabletop/` on
macOS); a development build's is the sibling `Tabletop-dev`. Godot's own `logs/` stay under `Tabletop` for both:

| File | Contents | Failure behaviour |
| --- | --- | --- |
| `settings.json` | `Animate`, `MutedWindowKinds`, `RememberMe`, `ManagementUrl`, `GameServerUrl`, `LogFilter`, `LogOpen`, `Theme`, `CameraFollow`, `TurnBanner`, `RangeRings`, `ShowHitboxes`, `ShowTerrainHitboxes`, `MeasureMode`, `WebUrl`; a release build ignores and never writes the three server addresses | missing, unreadable or malformed reads as `ClientSettings.Default`; every field is individually nullable in the DTO, so a partial file loses only its missing fields |
| `session.bin` | the session JWT, in a file created readable and writable by its owner alone | absent, unreadable or not three dot-separated segments reads as null and the login screen shows; an `IOException` or `UnauthorizedAccessException` is caught, not thrown |
| `cache/{hash}.bin` | asset blobs, keyed by SHA-256 | a partial download lives in `.part` and never becomes a cache entry; a hash mismatch deletes the part and the piece keeps its proxy |

The remembered session is deliberately not in an OS keystore. Windows data protection is
Windows-only, which the client's platform position rules out (`architecture.md` → Deliverables), and a
per-platform store behind one interface costs three implementations of which two cannot be
exercised on the development machine, with libsecret depending on a session keyring a minimal
Linux desktop may not have. The file protects the token from other local accounts on a shared
machine, and from a group- or world-readable directory being backed up. It does not protect the
token from anything running as the same user, and it does not stop the file being copied to
another machine, where it refreshes until the Stytch session behind it expires. That last is the
one property data protection had and this does not. What bounds that is the token itself: a five-minute JWT whose only power is
`POST /v1/auth/session/refresh`, cleared on logout and on a `401`. `SessionStore` is three methods
with one call site in `Main._Ready`, so a keystore can replace the file later without touching
`Session`.

In-memory state, all of it on `Session` except the node dictionaries:

| Held | Lifetime | Rebuilt from |
| --- | --- | --- |
| `Applier` + `Projection` | one connection | `Board` + the whole event log |
| `Draft` | `setup` only | the `BoardMessage` after every edit |
| `Others` (presence) | one connection | nothing; it starts empty |
| `_predicted`, `PendingUndoTo` | one pending undo | discarded on answer or on any reset |
| `_desyncFloor` | one player-initiated connection | nothing; it starts null, rises to the projection's `LastSeq` at each desync, and a resync carries it |
| `Game`, `ParticipantId`, `MyTeamId`, `IsCreator` | one connection | `Welcome`, then each `GameInfoMessage` |
| `TableView`'s `_pieces`/`_terrain`/`_markers`/`_zones`/`_ghosts` | one game screen | `Redraw()` diffing the projection or draft |

Nothing in the projection is stored anywhere on the client. A table growing a "current position" of
its own would mean the event spine had been broken.

## Failure modes

| Failure | What happens | Chosen? |
| --- | --- | --- |
| Management unreachable | `Feedback.Error` names it: `Cannot reach {ManagementUrl}: …`, or `{ManagementUrl} did not answer in time.`; the Home screen's list stays stale | yes; no retry loop on HTTP |
| A management `401`/`403`/`404`/`413`/`422`/`429`/`5xx` | `Feedback.Error`: sign in again; `Not allowed`/`Not found` with the detail; `Too large for the server`; the `422` detail as it stands (`name is at most 64 characters.`); the `429` detail or `Too many requests. Wait a minute and try again.`; `The server failed ({title}). Try again shortly.` Login, Home, the lobby and setup all report through it | yes |
| `426` on any request | `UpdateRequiredException` sets `Phase = UpdateRequired`, which is a sink; the screen offers Quit and nothing else | yes |
| JWT expired mid-session | the 60 s refresh normally prevents it; a refresh answered `401` disconnects, clears the store and drops to `LoggedOut`. The game server closes a socket `1008` at its JWT's expiry: the client refreshes first, then reconnects with the new token, and toasts `Your sign-in on the game server expired; reconnecting with a fresh one…` | yes |
| Refresh fails with anything but `401` or `426` | the token stays in memory and on disk; no refresh is sent for one minute from the failed attempt's start | yes; a management outage does not sign anyone out, and the five-minute JWT absorbs one missed tick |
| Socket closes unexpectedly | reconnect with backoff 1, 2, 5, 10, 10… s; `ConnectionLost(willReconnect: true)` toasts `Feedback.Closed` by code (`1001` restarting, `1009` a message too large, `1011` the server failed with its reason, `1013` fell too far behind, no code: cannot reach the server); the corner reads `reconnecting, attempt N; Home stops it…` from the second attempt; the projection is discarded and rebuilt, and the `Welcome` restores the player's team | yes |
| The account's oldest socket past its socket cap (`1000` after `welcome`) | no reconnect; drop to Home, which prints `This game was opened in another window, which took over.` or `Your account has too many game windows open; the oldest closed.` | yes; reconnecting would fight the newer socket |
| Any other server-side `1000` after `welcome` | read as a replacement; the player goes to Home with no retry, and Home prints `LastCloseReason` | the inference is an accepted ambiguity, since no frame distinguishes the two |
| `Rejected` with `InvalidSession` or `NotAParticipant` | no reconnect; `GameId` cleared; Home prints `Feedback.Rejected` (sign out and in again; join the game from the list) | yes |
| Handshake window missed (10 s) | the server closes; the client reconnects, because no `Welcome` arrived | yes |
| A command or edit refused | `RefusedReceived` toasts `Feedback.Refusal`: the kind in words (`Not one of the choices offered`, `Against the rules`, `Not at this point of the game`, `Not allowed`, `Refused`) and the server's detail; a watcher's command reads `You are watching this game.`, and a refused presence or ping (message id empty) is not toasted; the decision box shows an orange line when the `MessageId` matches its last command; nothing is rolled back, because nothing was applied locally except a predicted undo | yes |
| `ProjectionDesyncException` while applying an event | the projection is dropped, the socket is reopened and the log is replayed; `ConnectionLost(willReconnect: true)` toasts it as a rebuild | yes; client shell → The connection and the projection |
| A resync faulting again at or before `_desyncFloor` | no further attempt; `Phase = LoggedIn`, `LastCloseReason` is `This game's log cannot be replayed: {message}`, and Home prints it | yes; the bad event is in the server's own log, and rejoining buys one more attempt |
| Watching: an admin watcher, or from `playing` on a participant with no team (`Session.IsWatching`) | presence and pings are not sent; drags do not start; no piece, team or roster menu; the table menu offers only Leave; Undo is hidden; the header's waiting line reads `Watching: you have no team in this game`; the corner adds `— watching` | yes; the server refuses every change from a watcher |
| A frame kind the client does not act on, `AtRest` included | dropped; `Handle`'s switch has no default arm and deserialization has already succeeded | yes; contracts plan ruling 3 |
| A mesh 404s, its presigned URL has expired, or its hash mismatches | `MeshLoader` logs and returns null; the proxy stays; the game is playable | yes, and load-bearing |
| Presigned URLs expire mid-game (`Manifest.ExpiresAt`, 60 minutes) | the next `LoadAssetAsync` at or after `ExpiresAt` refetches the manifest before it looks the asset up | yes |
| Presence stops without `PresenceEnded` | another player's `held` placement leaves that piece's node displaced from the projection until the next `Redraw` for it | a gap: the server sends `PresenceEnded` when a participant's last socket closes, and the client relies on that frame alone, with no timeout of its own |
| A frame over 4 MiB | the server treats it as a protocol violation; a very large `ReplaceDraft` is the only realistic candidate | not guarded on the client |
| Two players drag the same piece | last drop wins; each saw the other's drag through presence | yes |
| An unknown zone shape id | `ZoneNode` draws nothing and logs a warning | yes; the game server's `DraftEditor` tests use shape ids `ZoneStyles` does not resolve |
| A model placed outside the drawn region | committed as sent; the server re-decides (`LegalSetMembership.Contains` under `strict`, `GfMoves.ExitPlacement` for a disembark) | yes; under `strict` the client holds a commit only for what `MoveRuler.Gate` measures (allowance, minimum, contact marks) |

Concurrency, ordering and lifecycle hazards:

- Frames arrive on the socket read loop's thread. `IDispatcher` marshals every one to Godot's main
  thread with `CallDeferred`, and each handler is gated on `ReferenceEquals(socket, _socket)` so a
  stale socket's frames and close are dropped. `_socket` is written by `ConnectAsync`,
  `TearDownSocket` and `OnClosed`. Reads of it from
  `SendCommandAsync`/`SendEditAsync`/`SendPresenceAsync` are unsynchronised. Every one of those is
  called from the main thread, but nothing enforces it. `ImmediateDispatcher` cannot test this
  hazard.
- `Session` outlives every screen, and a freed Godot object invoked from a live event throws
  `ObjectDisposedException`. Every subscriber holds its handlers in fields and detaches them in `_ExitTree`
  (`Animator` in `Dispose`, which `GameScreen.FreePlayPanels` calls), so a screen swap or the end of play leaves
  no handler behind. A new subscriber that skips the detach reintroduces the fault.
- `GameScreen.Rebuild` runs on every `GameChanged` and `PhaseChanged`, and frees and re-creates
  the `SidePanelHead` with its team columns, the `LobbyPanel` and the `SetupToolbar`. Any lobby edit by any participant
  destroys the setup toolbar mid-interaction, losing its selection and any open field. The play
  Hud is built once (`ShowPlayPanels` returns while `_header` is set). The setup toolbar is not guarded.
- `AskViewModel.Mine` orders open Asks by `Ask.Id`, a v7 GUID, so "the current Ask" is
  approximately the oldest-opened one rather than the innermost frame's. Under nested sequences the
  order that matters is the resolution stack's. The two agree only because ids happen to be
  time-ordered.
- `EventsApplied` fires with the whole log on connect. With `Animate` on, `Animator` tweens one
  `ModelsMoved` at a time over 0.4 s, so reconnecting into a long game replays every historical
  move as an animation. Nothing distinguishes a hydrate batch from a live one.
- `Redraw()` is a full diff of the projection against the node dictionaries, run on every
  `EventsApplied`, every `BoardChanged` and after every animation step. There is no incremental
  path and no measured budget. A 40-model game is fine, and nothing says what is not.

## Testing

Two tiers, split on the Godot dependency.

Headless, `tests/Tabletop.Client.Tests` (xUnit). The project compiles `client/src/Core/**/*.cs`
from source and references the three libraries plus `Tabletop.Content.Tests` and
`Tabletop.Protocol.Tests` for their samples (`TableStateTests.Sample()`, `Samples.Game`,
`Samples.Board`). Every task with logic is test-driven. The plan's running count ends at 45 tests.
What is covered:

| Subject | Test infrastructure | What is pinned |
| --- | --- | --- |
| references and the table frame | `ReferencesTests` | `ZoneStyles.Resolve` and `Table.ContainsPoint` answer in the wire frame, so nothing converts; the sample table state is on the slab |
| settings and the session store | temp directory | defaults on a missing or malformed file; round trip; the session file's Unix mode is owner read and write; a file that is not three dot-separated segments reads as null |
| the management client | `FakeManagement : HttpMessageHandler` scripting responses | the version header and bearer on every request, the exact paths and verbs, `426` → `UpdateRequiredException`, problem+json title and detail, contract types round-tripping under `ContentJson` |
| the socket seam and presence | `FakeGameSocket`, `FakeSocketFactory` | frames delivered, sends recorded, closes injected; presence set, removed and cleared |
| the session object | the two fakes plus `ImmediateDispatcher` | the connect sequence, the projection, local undo and its two clear paths, presence and pings bypassing the projection, reconnect with backoff, a reconnect rejoining the same team with no re-pick, a replaced socket not reconnecting, `Rejected` stickiness, Home keeping the seat while Leave calls management, a desync dropping the projection and rebuilding it from the log, the bound sending the player to Home with a reason, an `AtRest` frame ignored |
| built-in content and generators | seeded `Random` | 38 built-ins present, each naming one of the 33 catalog meshes, volumes copied on `Place`, the five planet tables' fixed and rolled entries, the ruins pack without whole buildings, pieces landing off the slab |
| the asset cache | temp directory, `HttpMessageHandler` | hash verification, `HashMismatchException`, a cached hash never touching the network |
| the view models | protocol samples | log wording, `Touched`, `AskMode` per legal-set kind, `SpendableBy` keeping only my team's spender models, the reveal naming who spent what, a one-model pick naming the model, `Mine` skipping answered and other teams, auto-pass on a muted window kind, `ConfirmLabel`, menu items building the right commands, the ruler's displacement and remaining allowance, `UsedInFrame`, `Region` and `ExitRegion` boundaries, `IsDisembark`, the animation queue's two modes |

`ImmediateDispatcher` raises every event inline, so a test asserts on state immediately after
delivering a frame.

By running the client. Everything in `Screens/`, `Table/` and `Panels/` needs a window, a
viewport, a mouse, `GltfDocument`, tweens and a `SubViewport`. Each task ends with a scripted
manual run against the local servers. Several of them need two clients, either two desktop
accounts or one account in two instances. Presence, free-for-all setup editing, Asks addressed to
another team and "Home disconnects but the seat stays" cannot be observed from one. Docker is
needed for Postgres and the MinIO stand-in only. On the development machine both servers run in
WSL and the client runs on the Windows host over forwarded localhost.

The fitted built-in meshes are among the things verified by eye, at both extremes of each group's
footprint, and there is no automated check that a mesh file fits the volume it decorates.

`scripts/check-client` is the scripted part of the Godot layer's testing: four greps, exit non-zero
on any hit. It is the only automated guard on the boundary rule.

Gaps:

- No automated test of any Godot code. `WorldFrame`'s conversion, `PieceNode`'s proxy geometry,
  `TableView.Redraw`'s diff, `MeshLoader`'s priority queue, `TableInput`'s picking and
  `PresenceOverlay`'s teardown are all verified by eye. `WorldFrame` is pure and could live in
  `Core/`. It does not, because it returns `Vector3`.
- No contract test between `ManagementDtos.cs` and `Tabletop.Management`'s records. The management
  plan states "a change to any of these here is a change there" and nothing enforces it. The
  client's tests serialise its own mirrors, so a renamed property on the server side fails at
  runtime in the client, not at build time in either.
- The decision box's mode dispatch is tested at the view-model level (`AskViewModel`, `DecisionView`), not the box level. `ModeOf` is
  covered. That `RenderMove` swaps three things for a disembark is a manual step in Task 14.
- The desync path is tested with an injected faulting event, and has no manual trigger: nothing a
  player can do makes the server emit an event the applier cannot apply. The two desync arms of the
  `ConnectionLost` toast and Home's reason line are read rather than run.
- Reconnect is tested with an injected close, not a real dropped socket. Kestrel's keep-alive
  timeout, a WSL network hiccup and a server restart mid-hydrate are untested paths.
- No performance test. There is no budget for `Redraw`, for the 20 Hz presence sample, or for a
  game with many models and many volumes.

