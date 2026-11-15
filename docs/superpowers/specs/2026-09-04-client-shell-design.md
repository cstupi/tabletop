# The client shell

Design session, 2026-09-04. The Godot client's frame: its screens, its connection, what it
holds, and how the play screen is put together.

`architecture.md` fixed the client's job — a renderer, an input surface and a geometry engine
that never holds the rules — and later sessions designed everything the client has to show:
the game list and lobby ([accounts](2026-08-29-accounts-design.md)), the setup phase
([setup phase](2026-09-04-setup-phase-design.md)), presence
([presence](2026-08-30-presence-design.md)), Asks and windows
([action sequences](2026-09-03-action-sequences-design.md)), the turn as an Ask
([scheduler](2026-09-04-scheduler-design.md)), the animate and wait settings
([resolution](2026-08-28-resolution-design.md)), and the menu that edits anything
([game flow](2026-08-28-game-flow-design.md)). This session designs the frame those sit in.
Not the visual design of any screen.

An earlier sketch scoped the shell to table, camera and mouse with no server. That was build
ordering, not a design, and it is superseded here.

## No social login

Twice deferred — by [accounts](2026-08-29-accounts-design.md) and by
[the web app](2026-08-29-web-app-design.md) — was how a session gets from the browser into the
client, loopback or device code. Both existed to serve sign-in with Google and magic links,
the two methods that need a web page to redirect back to. **Neither method is wanted.** Ruled
in this session.

Login is **email plus password**, with an **email one-time code** for the passwordless case and
for verification. Both are Basic-auth endpoints on Stytch that need the project secret, so both
run through management — exactly as accounts already had them. **The client has its own login
screen**, and the web app has the same two methods. Loopback, device code, redirect
registration and the mixed-content constraint all go with the methods that needed them. The web
app's login section is corrected in place: the browser owns login for the web app, the client
owns its own, and both talk to the same two management endpoints. Stytch still appears in
neither.

## Structure

**One session object, one screen at a time.** Three screens, each a Godot scene swapped under
the root. Beneath them a session object outlives every screen and owns the management HTTP
client with the session JWT, the game socket while one is open, the projection, and presence.
Screens read from it and hold no truth of their own beyond what they draw.

Two alternatives were rejected: one scene with panels toggled, which puts the game list and the
table in one tree for no reason; and a scene per game status, which reloads the table on every
status change and fights the server's view that lobby, setup and play are one connection.

## Screens

- **Login.** Email and password, or email and a one-time code. On success the client holds the
  JWT in memory and, if the player chooses, on disk so the next launch skips the screen. The
  stored JWT is the five-minute session token. On launch it goes to
  `POST /v1/auth/session/refresh`, which accepts an expired JWT while the Stytch session lives,
  and the same call runs once a minute while signed in. **Only a `401` signs out** and shows the
  login screen. A `426` is the update-required screen. Any other failure keeps the token, in
  memory and on disk, and blocks the next refresh for one minute from the failed attempt's start.
  Concurrent refreshes share one request. A login or a logout supersedes any refresh in flight,
  whose outcome is then dropped. Logout always finishes locally, whatever the server answers.
  **The store is not an OS credential store**: it is
  `user://session.bin`, a file created owner-only where the platform has file modes, because the
  client may run on more than one operating system (owner ruling, 2026-09-06). It keeps the token
  from other local accounts, not from anything running as this user.
- **Home.** The game list from `GET /v1/games`, every status, filtered by the player. Create
  game: name it (`game.name`, added 09-04), pick the pack from `GET /v1/properties`, the shape,
  the enforcement setting and whether the pipeline waits between steps, and pick one of the
  player's armies from `GET /v1/armies`. Creating or joining leads to the same place. Before
  either, the client sends its version on every request and treats management's *update
  required* response as a screen of its own.
- **Game.** One screen for `lobby`, `setup`, `playing` and `ended`, because all four are one
  socket and one hydrate path. The status decides which tools are bound to the table and which
  panels show; a status change is a change inside the screen. The Home button **disconnects**:
  it closes the socket and the participant row stays, since disconnecting is not leaving.
  **Leaving** is a separate menu action that calls `POST /v1/games/{id}/leave` and removes the
  row, per accounts.

## The connection and the projection

On connect the client sends the JWT and the game id. The server answers with the teams,
participants and configuration; then, from `setup` on, the draft or the snapshot; then the event
log from the start, then live events as they happen. At `lobby` there is no board yet.

The client applies every event to a **projection**: placements, the shelf, containment,
conditions, action records, wounds, spell tokens, scores, the scheduler's position, the
resolution stack, and the current Asks. **The applier is rules-free and lives in
`Tabletop.Protocol`.** Events carry every state change they made — action sequences ruled that
`step_completed` records *"everything it produced"* — so applying is copying, never deriving,
and no ruleset code reaches the player's machine.

**The current Asks come out of the log like everything else.** Three event kinds carry them:
`ask_opened`, `ask_answered` and `ask_closed`, listed in
[action sequences](2026-09-03-action-sequences-design.md) → *The resolution stack*. The projection
rebuilds the open Asks by applying those three, so a reconnect recovers them from the log rather
than from anything the socket held.

**Undo is local first.** Undo-to-sequence-N is a command; while it travels the client re-applies
its own copy of the log to N and shows that. The server's answer confirms or corrects it.

**Reconnect is connect.** The projection is discarded and rebuilt from snapshot and log, the
same path a first join and a crashed server use. The client reconnects with backoff after any
close it did not ask for. Two closes are excepted: a `rejected` frame, and a `1000` after
`welcome`, which is how the server closes a socket it has replaced, so reconnecting there would
fight the newer one. The socket's first frame is `connect`, sent inside the server's ten-second
handshake window. **Nothing else is sent before `welcome`.** Presence and pings are dropped, and
commands and setup edits are refused locally.

**A desync is not a network fault.** An event the applier cannot apply, because it names an id the
projection does not hold, is a desync of this client's copy. The client drops the projection,
reopens the socket and rebuilds from the log, which is the reconnect path with no backoff. It does
not skip the event and carry on, because a client that silently diverges from the server is worse
than one that visibly resets. **One attempt per sequence position.** A fault that has made no
progress past the last one is an event in the server's own log, so the player goes to the home
screen with the reason rather than replaying it forever. Ruled by the owner 2026-09-06.

**Presence and setup edits bypass the projection.** Presence is a separate in-memory table
keyed by participant, gone when the socket closes. A setup edit replaces the draft.

**Server wins, always.** The client predicts geometry so that dragging feels immediate; a logged
event that disagrees snaps the piece to where the event says. There is no reconciliation
machinery and no rollback.

## The table view

One scene, used by setup and play. It draws the slab at its real size with its texture, terrain
placements as their meshes, markers, deployment zones as flat polygons, and pieces.

**During setup every zone row is drawn.** A zone whose team ordinal has a team draws in that
team's colour, and one whose ordinal has no team draws grey, so a group can see and swap zones
before every team has formed. From `playing` on, only zones with a team are drawn.

**A piece is drawn as its hitbox first.** The base-and-height cylinder for a model, the volumes
for terrain. The mesh, once loaded, decorates that. A missing, deleted or still-downloading mesh
leaves a translucent proxy and the game is fully playable — the asset library's rule, visible.

**A model that is not on the table is not drawn at all.** Nothing is drawn for a model on the
shelf, and nothing for a model inside a container, because neither location carries a placement to
draw at. The one exception lasts as long as a single Ask: while a unit is disembarking, the client
puts one draggable ghost per model on the container's own node, so the player has something to
place. Those ghosts are not part of the projection and they vanish when the answer is sent.

**Assets load at runtime** through `GLTFDocument` from a presigned URL, never through editor
import. A content-addressed cache on disk means a blob is fetched once per machine: the hash is
the key, so the same mesh in two games is one download. The game's manifest of presigned URLs is
fetched when the table opens, again when play starts because every army is in by then, once on
an asset it does not list, and after its expiry. Built-in terrain, textures and zone
shapes ship inside the client and load through the same path from local files. Loading is
background, nearest to the camera first, so the table is usable before it is pretty.

**Camera:** orbit, pan and zoom over the table; a top-down snap for placement; a table-level
drop for line of sight checks.

**Geometry runs in `Tabletop.Geometry`**: settling by raycast down through volumes, edge-to-edge
distance, line of sight against cylinders and prisms. Godot draws the results and is never asked
for one. **Godot physics is not in the scene** — no collision shapes, no physics bodies, only
meshes.

## Input and presence

- **Select** a unit by clicking any of its models; a modifier narrows to models. Selection is
  presence, never a command.
- **Drag** a selection: positions stream as `held` presence, the piece settles locally as it
  moves, and the drop sends one move command with start and stop positions. Hand moves arrange
  models and never end a turn, per the scheduler.
- **Moving shows its distance.** Whenever a piece is being dragged, declared move or hand move,
  a ruler draws from each model's start position to its current one with the distance in
  inches, measured the way page 7 does — *"no part of their bases move further than the total
  movement distance"* — so the readout is the base's displacement from where it started. In a
  declared move the readout also shows the remaining allowance, and the reachable region is the
  allowance as straight-line reach, cut where the base would enter an impassable volume or leave
  the slab, and capped where the path enters difficult terrain. The geometry library computes
  that region and the client only draws it. After a mid-move checkpoint (a cast, for instance)
  the distance used so far comes from the frame and the region shrinks to what is left. Other
  participants see the same ruler, drawn from the `held` presence position against the start
  position already in their projection, so nothing new goes on the wire.

  A model inside a container has no start position of its own. Its ruler draws from the
  container's placement, which is the point it is disembarking relative to, and it shows a
  distance with no remaining allowance, because the move's allowance constrains nothing about
  where an occupant may be placed. The region there is the container's base grown by the exit
  distance, not a radius about a start.
- **The move button** on the selected unit's action bar answers the activate Ask, which starts
  the action's sequence. The sequence's move step opens a `move_region` Ask, and its answer is
  the destinations. That Ask carries the allowance and the impassable volume ids. When the move
  may enter a container it also carries the containers it may enter; when the move is leaving one
  it carries that container instead, with the distance its models must be placed fully within of.
  The client draws the straight-line reachable region from the first, or the container-relative
  region from the second, and streams the planned position as a `proposed` ghost until the player
  commits. Clicking an offered container embarks the whole unit at once, which is one answer and
  not a move.
- **Measure:** two clicks or a drag between pieces, resolved edge to edge locally, relayed as a
  presence measurement.
- **Ping:** a keypress at the cursor, one-shot.
- **Cursors** of other participants draw where their ray landed, with the hovered piece
  highlighted, from their presence value.

Every other participant's presence draws in their team's colour.

## Asks and windows

One **Ask panel**, always in the same place, rendering whatever Ask is currently addressed to the
player's team: activate, deploy, target, strike back, remove casualties, react or pass,
confirm. The legal set from the server becomes the panel's choices and the table's highlights;
nothing outside the set is offered, and under `warn` or `silent` enforcement a menu edit is how
a player steps outside it; under `strict` the server refuses an out-of-set answer and the panel
says so. Any team member answers the team's Asks, for any unit of the team (game flow →
*Participants*), and the panel shows who did. Asks addressed to other
teams show as a waiting indicator naming the team, never as controls.

**Windows** render in the same panel as react or pass. Every window Ask is shown; the client
never passes one for the player, and a pass is that player's answer, as the scheduler ruled. **Sealed token spends** render as a private input; the panel shows who has
answered without showing what, then reveals all at once when the server does. *Renamed
2026-09-06: this read "sealed bids"; the note in
[action sequences](2026-09-03-action-sequences-design.md) records why the name was invented.*
**Confirm gates**, when wait-between-steps is on, are one button in the same panel.

## The log, undo and the menu

A **log panel** lists every event with its actor and sequence, newest at the bottom, in words:
*Clay moved Dynasty Warriors 5"*, *the Dwarves blocked 1 of 2 hits*. Clicking an entry
highlights what it touched. **Undo to here** on any entry sends the undo command; the panel
greys the entries above the target until the server confirms. Presence never appears in it.

The **menu** is the right-click on any piece, unit or team panel, and it is where *every
property is editable* lives: mark activated or clear the action record, add or remove a
condition, set wounds, set spell tokens, move to the shelf or back, put inside a container or
take out, change a team's score, set the active team or round. A model with no placement, on the
shelf or inside a container, has no piece to right-click, so the team panel's menu lists each
off-table model with an item that places it on the table at a clicked point. The action names of the pinned
`Properties` are not on the wire, so the menu offers the names it has seen, which are the actions
already recorded in the log and on the units, and free text beside them. The activate Ask's option
labels are display text and are not parsed. Each item is one
ordinary command, logged with the actor, through the same path automation uses. **No admin mode,
no confirmation dialog.** The log is the accountability.

## Animation

The projection applies an event the moment it arrives. Whether the table then *plays it out* is
**the player's own animate setting**, kept in local settings — ruled 09-04; it is presentation
only, so it is not on the game row — models falling, or the result simply appearing. Animation is
a queue between the projection and the renderer and nothing else. It may lag the projection and it
never delays an Ask — a player may answer while a roll is still being read out. Presence is never
animated, only drawn.

**A roll is a readout, not an object**, ruled by the owner 2026-09-06. Text beside the unit gives
the faces rolled, the threshold, the modifiers applied and which dice passed. No die is placed on
the table and nothing tumbles. Models falling stays as written, by tween.

## What must not be in the client

- **No ruleset code, no legality, no dice, no turn order.** The client renders prefetched
  answers.
- **No Godot physics as an answer.** Geometry is the shared library; Godot only draws.
- **No truth outside the projection.** A screen may not keep state of its own.
- **No hidden-information handling.** The server filters streams if it ever needs to; the
  client shows what it receives.
- **No army editing.** A wrong mesh sends the player to the web app; that open item stays open
  where it lives.

## Testing

The projection applier and the geometry library are headless and get the tests. The Godot layer
is thin over them and is tested by running the game. Everything Godot-free in the client lives
under `client/src/Core/`: the session object, the management client, the socket seam, the cache,
the built-in content and generators, and the view models. That directory is compiled from source
into `tests/Tabletop.Client.Tests`, which is what proves it needs no engine. Two things inside
Godot are worth a scripted check, and both are a grep: no scene contains a physics body, and no
client script references a rules type.

## Corrections made in place

- **Web app** — the login section and its two open items: no social login, so no loopback and
  no device code; the client has its own login screen.
- **Architecture** — the "auth is easier in a browser" bullet, and build step 5's *"geometry
  only, no server."*
- **Accounts** — the 08-29 revision note gains a 09-04 line.

## Deliberately not decided

- The visual design of every screen, and the Godot scene tree beneath each.
- ~~Which socket frames carry which message kind — the protocol contract.~~ **Settled 2026-09-05**
  by the contracts plan's socket message set and the game server plan's connect sequence.
- Whether the client edits armies — open since the web app session, unchanged.

## Still open

Nothing of its own.
