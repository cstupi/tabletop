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

`docs/workstreams/05-client-shell.md` scoped the shell to table, camera and mouse with no
server. That was build ordering, not a design, and it is superseded here.

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
  JWT in memory and, if the player chooses, in the OS credential store so the next launch skips
  the screen.
- **Home.** The game list from `GET /v1/games`, every status, filtered by the player. Create
  game: name it, pick the pack, shape and enforcement setting, and pick one of the player's
  armies from `GET /v1/armies`. Creating or joining leads to the same place.
- **Game.** One screen for `lobby`, `setup`, `playing` and `ended`, because all four are one
  socket and one hydrate path. The status decides which tools are bound to the table and which
  panels show; a status change is a change inside the screen. Leaving returns to Home and closes
  the socket; the participant row stays, since disconnecting is not leaving.

## The connection and the projection

On connect the client sends the JWT and the game id. The server answers with the setup
snapshot — or the current draft, during setup — then the event log from the start, then live
events as they happen.

The client applies every event to a **projection**: placements, the shelf, containment,
conditions, action records, wounds, spell tokens, scores, the scheduler's position, the
resolution stack, and the current Asks. **The applier is rules-free and lives in
`Tabletop.Protocol`.** Events carry every state change they made — action sequences ruled that
`step_completed` records *"everything it produced"* — so applying is copying, never deriving,
and no ruleset code reaches the player's machine.

**Undo is local first.** Undo-to-sequence-N is a command; while it travels the client re-applies
its own copy of the log to N and shows that. The server's answer confirms or corrects it.

**Reconnect is connect.** The projection is discarded and rebuilt from snapshot and log, the
same path a first join and a crashed server use.

**Presence and setup edits bypass the projection.** Presence is a separate in-memory table
keyed by participant, gone when the socket closes. A setup edit replaces the draft.

**Server wins, always.** The client predicts geometry so that dragging feels immediate; a logged
event that disagrees snaps the piece to where the event says. There is no reconciliation
machinery and no rollback.

## The table view

One scene, used by setup and play. It draws the slab at its real size with its texture, terrain
placements as their meshes, markers, deployment zones as flat polygons, and pieces.

**A piece is drawn as its hitbox first.** The base-and-height cylinder for a model, the volumes
for terrain. The mesh, once loaded, decorates that. A missing, deleted or still-downloading mesh
leaves a translucent proxy and the game is fully playable — the asset library's rule, visible.

**Assets load at runtime** through `GLTFDocument` from a presigned URL, never through editor
import. A content-addressed cache on disk means a blob is fetched once per machine: the hash is
the key, so the same mesh in two games is one download. Built-in terrain, textures and zone
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
  declared move the readout also shows the remaining allowance, and the reachable region is
  drawn at that radius. After a mid-move checkpoint (a cast, for instance) the distance used so
  far comes from the frame and the region shrinks to what is left. Other participants see the
  same ruler, drawn from the `held` presence position against the start position already in
  their projection, so nothing new goes on the wire.
- **The move button** on the selected unit's action bar starts the declared action. The Ask's
  answer returns the allowance and the impassable volume ids; the client draws the reachable
  region and streams the planned position as a `proposed` ghost until the player commits.
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
nothing outside the set is offered, and at loose enforcement a menu edit is how a player steps
outside it. Any team member may answer, and the panel shows who did. Asks addressed to other
teams show as a waiting indicator naming the team, never as controls.

**Windows** render in the same panel as react or pass. A **mute list per player**, in local
settings, auto-passes muted window kinds; the pass is still sent as that player's answer, as
the scheduler ruled. **Sealed bids** render as a private input; the panel shows who has answered
without showing what, then reveals all at once when the server does. **Confirm gates**, when
wait-between-steps is on, are one button in the same panel.

## The log, undo and the menu

A **log panel** lists every event with its actor and sequence, newest at the bottom, in words:
*Clay moved Dynasty Warriors 5"*, *the Dwarves blocked 1 of 2 hits*. Clicking an entry
highlights what it touched. **Undo to here** on any entry sends the undo command; the panel
greys the entries above the target until the server confirms. Presence never appears in it.

The **menu** is the right-click on any piece, unit or team panel, and it is where *every
property is editable* lives: mark activated or clear the action record, add or remove a
condition, set wounds, set spell tokens, move to the shelf or back, put inside a container or
take out, change a team's score, set the active team or round. Each item is one ordinary
command, logged with the actor, through the same path automation uses. **No admin mode, no
confirmation dialog.** The log is the accountability.

## Animation

The projection applies an event the moment it arrives. Whether the table then *plays it out* is
the game's animate setting: dice rolling on the table and models falling, or the result simply
appearing. Animation is a queue between the projection and the renderer and nothing else. It may
lag the projection and it never delays an Ask — a player may answer while the dice are still
rolling. Presence is never animated, only drawn.

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
is thin over them and is tested by running the game. Two things inside Godot are worth a
scripted check, and both are a grep: no scene contains a physics body, and no client script
references a rules type.

## Corrections made in place

- **Web app** — the login section and its two open items: no social login, so no loopback and
  no device code; the client has its own login screen.
- **Architecture** — the "auth is easier in a browser" bullet, and build step 5's *"geometry
  only, no server."*
- **Accounts** — the 08-29 revision note gains a 09-04 line.

## Deliberately not decided

- The visual design of every screen, and the Godot scene tree beneath each.
- Which socket frames carry which message kind — the protocol contract.
- Whether the client edits armies — open since the web app session, unchanged.

## Still open

Nothing of its own.
