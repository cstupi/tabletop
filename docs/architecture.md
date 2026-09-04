# Architecture

High-level design decisions for the tabletop wargaming simulator. This document records
*what was decided and why*; individual modules are still planned separately before being
built.

## Deliverables

Five things ship, and they have different target platforms:

| Deliverable | Directory | Platform |
| --- | --- | --- |
| Game client | `client/` | Windows (Godot export) |
| Game server | `server/game/` | Linux (Fargate) |
| Management API | `server/management/` | Linux (Fargate) |
| Web app | `web/` | Static (Svelte SPA) |
| Mesh prep tool | `tools/mesh-prep/` | The user's machine |

The mesh prep tool is easy to forget because it is not a service, but it **ships to users** —
conversion left the product on the understanding that they run this instead. It is undesigned;
`docs/workstreams/11-mesh-prep-tool.md` is a sketch.

The web app exists and owns **preparation** — armies, assets, account, and login. The client
owns **play**, plus the game browser and joining. See
[the web app](superpowers/specs/2026-08-29-web-app-design.md).

Everything except the client is headlessly testable. Everything except the client is
Godot-free.

## The load-bearing seam

**The client does not contain the rules.** `server/game/` owns state, dice, turn order,
resolution, and final legality. The client is a renderer, an input surface, and a geometry
engine — it may compute anything, but may only ever *ask*.

The rules therefore exist exactly once. There is no second implementation on the client to
drift out of sync, and no ruleset shipped to a machine the player controls.

What the client *does* own locally is geometry, because that is the part which cannot
tolerate a round trip.

## What is actually shared

Three libraries in `libs/`, and nothing else:

| Library | Shared by | Contents |
| --- | --- | --- |
| `Tabletop.Geometry` | client, game server | Collision volumes, distance, LoS, surface settling |
| `Tabletop.Protocol` | client, game server | Commands, events, presence, wire format |
| `Tabletop.Content` | client, both servers | Army, unit, model slot, weapon types |

Geometry is shared because the client draws with it and the server adjudicates with it, and
the two must agree exactly. Protocol is shared by definition. Content is shared because the
client renders armies and both servers store and interpret them. Whether an army reaches the
client over HTTP or over the WebSocket is a transport detail and changes nothing about the
types.

The rules engine — pipelines, scheduler, the property sets, modifier system — is
**not** in `libs/`. It lives in `server/game/` and goes nowhere else.

## Command / Event spine

Players never mutate state. They submit **Commands** (intents: `MoveUnit`, `DeclareShoot`,
`EndActivation`). The game server validates a command and emits ordered **Events** (facts:
`ModelMoved`, `DiceRolled`, `WoundsApplied`). State changes only by applying events.

One pattern serves as: the network protocol, undo, save/load, replay, the test harness, the
client's animation queue, and the audit trail for explaining a result.

RNG lives in server state as a seed. Every roll is an event.

## How the client stays responsive without the rules

The client must draw a movement range and grey out an illegal target without waiting on a
round trip. It does this without owning the rules, by distinguishing two kinds of question:

- **Discrete actions** (legal targets, available abilities) — the server sends the legal set
  when the team's input window opens. Prefetched, so there is no latency at click time.
- **Continuous actions** — movement is *anywhere* within an allowance, not an enumerable
  list. The server sends **parameters**, not an answer: move allowance, terrain effects, and the
  ids of volumes that cannot be crossed. `Tabletop.Geometry` renders the region from those
  parameters. *"Terrain multipliers" was an invention* — OPR's difficult terrain caps a unit's
  whole move at 6" rather than scaling it, so the parameter is whatever `GameRules` computes.

The second is the important one. It is why the client needs geometry and only geometry: the
server describes the constraint, the client draws its consequence.

**Measuring and planning are never commands.** Pre-measuring, range checks, and movement
ghosts are pure geometry, computed locally — so waiting players stay busy and the mouse never
feels laggy.

They are still **relayed**, which is not the same thing. This document originally said they
were never sent anywhere; [presence](superpowers/specs/2026-08-30-presence-design.md) puts
measurements and ghosts on the ephemeral channel, because a tape measure on a real table is
visible to everyone at it. Nothing waits on them, nothing validates them, and nothing logs
them.

## Rules enforcement

**A boolean, set per game: the server warns, or it says nothing.** It never blocks — see
[identity and enumerations](superpowers/specs/2026-08-29-identity-and-enumerations-design.md),
which found there is no third position that does not contradict the product. This document
originally called it a dial. The server validates and warns rather than gating. A command may
carry an override; overrides are **visible to opponents and recorded in the event log**, never
silent. Model placement follows the same rule: free placement, with base overlap, terrain
intersection, and broken coherency flagged rather than prevented.

The legality API is **enumeration, not validation** — `EnumerateLegalCommands()` rather than
only `IsLegal(command)`. Validation falls out of enumeration but not the reverse, and
enumeration is what the client needs anyway to render affordances. An AI opponent later gets
its action space for free.

A modified client submitting illegal commands simply has them rejected. Given enforcement is
deliberately permissive with visible overrides, what authority protects is state coherence
rather than fair play.

## Undo

A first-class primitive, because friendly play means out-of-order or mistaken actions are
normal. Undo is **replay to sequence N from the initial state**, not inverse events. The initial state
is the setup snapshot — the record the setup phase ends with — so there is nothing else to
replay from and no separate checkpoint mechanism. Because RNG state is part of game state, rewinding and redoing the same action
reproduces the same dice — so undo cannot be used to fish for rolls.

## Abilities

Resolution is a **stack of sequences**. **Corrected 2026-09-03 by
[action sequences](superpowers/specs/2026-09-03-action-sequences-design.md)**: the sketch this
section used to carry (shooting: declare target → range/LoS → hit rolls → defense rolls →
apply wounds → morale) put targeting and morale inside the pipeline, and both live outside —
targeting is the action's Ask, morale fires from ruleset-owned triggers, and Grimdark Future's
shooting and melee are one four-step attack sequence invoked from two contexts. A sequence is a
list of C# steps run over a frame stack; a step completes, asks a team for input, pushes a
nested sequence, or opens an interrupt window, and the stack is rebuilt from the log the way
the action record is. Properties declaratively attach to a roll or step with a condition and
contribute an effect — one of the twelve kinds in
[the effect grammar](superpowers/specs/2026-08-29-effect-grammar-design.md).

The hard part is stacking and ordering — replacement vs additive effects, caps, floors.
Modifier priority must be explicit rather than relying on registration order. **This survived
being challenged**: the resolution session argued that restricting modifiers to arithmetic made
them commute and removed the need, and
[the effect grammar](superpowers/specs/2026-08-29-effect-grammar-design.md) found that holds
for one of twelve effect kinds. Ordering is a field on the effect.

An ability is not only a modifier. Twelve kinds cover Grimdark Future, four of which act on
*other properties* — granting, suppressing, or altering a parameter — so resolving an
interaction is two passes: settle which properties and parameters are live, then apply them.

## Turn scheduling

The ruleset owns a **scheduler** answering "whose input is needed next, and what is legal in
this window?". Windows are **scoped to a team, not a player** — teams alternate activations
and any member of the active team may act. Coordination inside a team is social.

- 2 players = two teams of one.
- Free-for-all = N teams of one, differing only in the order over teams.
- Player-turn rulesets (Warhammer style) = a different scheduler, same interface.

No engine code branches on ruleset identity; the scheduler is the firewall.

## Rules as data

The goal is **extensibility and multi-ruleset support**. JSON is chosen because it iterates
faster than C# — adding a custom rule is a data edit, and supporting a game beyond OPR is new
data rather than new engine code.

**In-repo now, user-authored eventually.** This document originally said the goal was "not end
users uploading rules"; that is true of the current phase and not the intent. The order matters
because OPR's rulebook gives a finite, known vocabulary to design against. While `Properties` is
authored in-repo and ships with the build there is no upload endpoint, no sandbox and no
moderation surface; when users can author it, that is additive.

Packs are loaded and interpreted **only by the game server**. The client never sees a rule
pack; it receives the consequences of one.

- **Rules as data** — `Properties`: typed stat definitions and one catalog of named rules.
- **Rules as engine** — the pipeline interpreter that executes that data.

### Closed schema, extensible content

The *shape* of `Properties` is validated and unknown keys are errors; the property sets extend
freely. Adding a rule means adding an entry and referencing it by id.

The rejected alternative is an open property bag where the engine reads keys it recognises: it
authors just as fast but a mistyped key silently does nothing. With a closed schema, typos fail
loudly. `Properties` is validated against JSON Schema in tests, so a malformed file fails
`dotnet test` rather than a game.

### Stat blocks are named stats

The unit stat block cannot be a fixed C# type. OPR uses Quality/Defense/Tough; Warhammer uses
M/T/Sv/W/Ld/OC with weapons carrying BS/A/S/AP/D; skirmish games differ again. A `Quality`
property in the engine would hardcode OPR into it.

So stats are named and defined by `Properties`, and **typed**: each declaration carries a kind
from a closed set — target number, distance, count, dice expression — so `4+` and `6"` are never
confused and `D6` has somewhere to live. See
[content values](superpowers/specs/2026-08-29-content-values-design.md). The ergonomic cost of a
named store rather than `unit.Quality` is paid back by ruleset-specific C# supplying typed
accessors over it.

### Where data stops, for now

**What is edited every day is JSON; what is edited once per ruleset stays C#.** The two halves
have names — see
[identity and enumerations](superpowers/specs/2026-08-29-identity-and-enumerations-design.md).

- **`Properties`** — stat definitions, and one catalog of named rules with four kinds:
  `unit`, `model` and `weapon` — which differ in where they attach — plus `spell`, which attaches
  to nothing and carries a token cost. Stats are typed and declared per level; every catalog
  entry carries its effects as data. Stored normalised in Postgres, not as JSON; see
  [`docs/schema.md`](schema.md).
- **`GameRules`, C# behind an interface** — turn structure, action definitions, pipeline step
  order, scheduler policy, dice conventions.

Neither is called a "rule pack", and the generic interpreter that runs one through the other
needs no name at all.

Generalising `GameRules` before a second ruleset exists means designing an abstraction for games
not yet understood. Turn structure, pipeline order, scheduler policy and dice conventions are
where the pressure will show when one lands, and that is the moment to move them to data — with
a real second example to design against. This follows the OPR-first-then-generalise decision.

Age of Fantasy is **not** that second example: its terrain rules are word for word identical to
Grimdark Future's, and it is the same engine. Warhammer is the test.

### Pinning

**`Properties` are content-addressed and pinned per game, snapshotted like armies.** Undo-by-replay
requires the exact ruleset that produced the event log; a pack changing mid-game or between
sessions makes replay diverge and undo silently produce wrong state. Distribution uses the same
mechanism as assets — hashed and pinned in the game record.

## Geometry

Full 3D math in inches (one world unit = one inch), including settling a model onto the surface
it lands on by raycasting down through terrain volumes.

Line of sight uses **simplified collision volumes**, never the visual mesh.
[Geometry values](superpowers/specs/2026-08-29-geometry-values-design.md) narrows those to
extruded prisms — a circular or rectangular footprint with a bottom and a top. **Convex hulls
are dropped**, so every test is a 2D shape intersection plus a vertical range.

This keeps determinism and headless tests, and deliberately lets an uploaded model carry visual
flair outside its hitbox. The Godot layer is purely a renderer; it is never asked a question the
rules depend on.

Distances are measured edge-to-edge between bases, not centre-to-centre.

Both the client and the game server run this library, but they run it for different reasons, and
only one of them is right.

**Client-side geometry is cosmetic.** It exists to remove the feeling of latency, not to be
correct. It is a prediction of what the server will say. If the two disagree, the server wins and
the client resyncs from server state — a model snaps back, and that is the entire cost.

This is why bit-exact cross-platform determinism is *not* a requirement here, even though the
client is Windows and the server is Linux. Divergence cannot compound, because the client holds
no authoritative state to diverge *into*. That constraint belongs to lockstep architectures,
which this deliberately is not.

## Table and layout

Superseded in part by [the table and its setup content](superpowers/specs/2026-08-29-setup-content-design.md),
which designs these as data.

- **Table** — width and length in inches, and a texture. A rectangle: the "shape" and
  "play-area boundary as a volume" this document originally called for do not exist, and the
  four-player square is a rectangle like any other. Named presets (40k 60×44, OPR 72×48,
  skirmish 30×22, 4-player 72×72) plus arbitrary custom sizes. Never a constant.
- **Layout** — a saved setup snapshot: board size and texture, terrain placements, marker
  placements, deployment zones. Physical only, no rules.
- **Mission** — **not a record.** This document defined one as deployment zones, objectives,
  victory conditions, round count and team count, which is not what a mission is. Missions are
  published content in OPR and Warhammer, players read them out of a book, and game flow had
  already removed victory conditions from the model.

A deployment zone names a **built-in style**, which resolves to a *set* of polygons with
measurements anchored to table features. It names a **team ordinal**, not a team, because a
saved layout has zones but no game to have teams in.

A terrain piece is a mesh plus collision volumes plus declarations. The engine reads a closed
set of six — impassable, blocks LoS, see in and out, cover, difficult, dangerous — and carries
the rest as free text nobody interprets. Those six are OPR's own terrain types, read from the
rulebook; `climbable`, which this document previously listed, is not among them. Same asset
pipeline as models.

### Terrain placement is a game phase, not an editor

Terrain is placed in a setup phase rather than in a separate map editor application, and there
is no second persistence path. The setup sequence is: table size → terrain placement →
deployment → rounds.

**The setup phase is not logged.** This reverses what this document originally claimed. It made
placement a stream of ordinary Commands so that a predefined map would be "a batch of placement
events," undo during placement would fall out of undo-by-replay, and alternating placement
would come free from team-scoped input windows. Setup does not need replay; it needs a settled
result. Only the state at the end of the phase is recorded, and that record is the event log's
initial state.

What that buys instead: **terrain and objectives are locked after setup** with no flag and no
check, because nothing in the log can reach them. Nobody nudges a ruin while reaching across the
board.

- A **predefined map is a saved snapshot**, not a batch of events; loading one seeds a setup
  phase. Saving works in reverse — the thing a game ends setup with and the thing a user saves
  are one record.
- **Destructible or moveable terrain** later is additive: terrain events on top of the same
  snapshot, replay still starting from it.

## Content and assets

Unit stats come from **imported One Page Rules Army Forge lists**; the internal format is
treated as authoritative so a native list builder can be added later without engine changes.

An army brought into a game is **snapshotted** into the game record, so editing a saved list
cannot mutate a live match.

### Assets are a library, not army content

A mesh carries **zero game semantics**. It does not know what it represents, what stats it has,
or what army it is in. It is a file a user points at things.

```
Blob      hash, bytes in S3                     deduped within one user's library
Asset     account_id, blob_hash, name, kind     the library item
          + scale, orientation, modelled base   physical facts about the miniature
```

Two records in the library, and the reference to one of them is a field on the model slot
inside an army — see [the asset library](superpowers/specs/2026-08-28-asset-library-design.md).
There is no per-army asset row and no FK from `Asset` to any army: armies reach *out* to
assets, so deleting an army touches nothing in the library, and deleting an asset degrades
appearance without affecting play.

**Dedup is per user, not global.** Nobody shares storage with anybody, so cross-user reference
counting never has to exist. This is unaffected by game participants downloading each other's
assets, because that creates no library record on either side — it is a download, not a share.

**Scale, orientation and the base it was modelled on live on the Asset** because they are
facts about the mesh, not about a game. A space marine is a space marine in every army —
what varies is the stat block it stands in for, and someone may well proxy it for a
different race entirely. Nothing validates that a mesh "matches" what it represents.

The **modelled base is a suggestion axis, not the hitbox.** The base used for geometry is
set per model slot, defaulting to the ruleset's value — see
[the army](superpowers/specs/2026-08-28-army-design.md). What the asset records is the base
the mesh was made for, which is how the library offers meshes that fit a slot.

`kind` makes the library generic beyond miniatures. Terrain pieces use the same upload, the same
dedup, and the same library — only their profile differs, carrying collision volumes instead of
a base size.

### GLB only; conversion is not in the product

Uploads are per-user, persistent across games, and load at runtime via `GLTFDocument` — never
through Godot's editor import step. Assets are content-addressed by hash, stored in S3, and
served through the management API. **A library is private for discovery, not for access**: nobody
can browse or search another account's assets, but participants in a game download the assets that
game uses, straight from wherever they live. Nothing enters anyone else's library.

**Upload accepts GLB and rejects STL.** Narrowed from "glTF/GLB" by
[the management API](superpowers/specs/2026-08-29-management-api-design.md): a `.gltf` with
external buffers is several files with relative references, which breaks single-object
presigned upload. GLB is the same format in one file and is Blender's default export.

The miniature ecosystem distributes STL, which Godot cannot load and which is 50–100× too dense
for realtime — a print-ready 32mm mini is routinely 500k–2M triangles. Converting it server-side would mean a job queue, a worker, an
async upload state machine, and a source/derived blob split.

Instead, conversion is an **offline tool the user runs before uploading** (`tools/mesh-prep/`).
The product gains a validation rule and loses an entire subsystem.

The residual risk is that nothing stops a user uploading an undecimated GLB straight out of
Blender, which then lands on every client in the game. A triangle-count check is the only
guardrail once the pipeline is out of the product, and it runs as a **ranged read of the GLB's
JSON header** on upload confirmation — see
[the management API](superpowers/specs/2026-08-29-management-api-design.md). It warns; it does
not block.

**A missing or undownloadable mesh must degrade to a proxy shape, with the game fully playable.**
Free, given the rules never touch the mesh — but only if built that way from the start.

What the proxy is drawn from differs by kind, which this document originally missed: terrain has
collision volumes to draw, a **miniature has only its base and height**. Still open.

## Two servers

The fault line is **CRUD versus live sessions**, and it is drawn from the start rather than
deferred, because the two differ in transport, state, and deploy risk.

### `server/management/` — ASP.NET Core, HTTP

| Module | State |
| --- | --- |
| Identity | Accounts, sessions; the only module that knows Stytch exists |
| Content | Armies, layouts, the asset library and its folders (Postgres) |
| Asset store | Content-addressed blobs (S3) |
| Games | Game records, teams, participants, the public game browser |

Ordinary CRUD. Holds no live state, so it can restart freely and deploy at any time. Both the
web app and the game client talk to it over HTTP, and it is the only thing that talks to Stytch.

There is no `Collections` module and no collection entity. The word was never given a meaning
and is dropped.

### `server/game/` — WebSocket

Live games in memory, plus the append-only event log. **The only module holding live state.**
It needs graceful shutdown that drains to the event log, and it is what constrains deploys.

Authentication does not run through Stytch here. The user logs in through the web app and the
client acquires a session from it; creating or joining a game is an ordinary management
endpoint, and the client then opens its socket with **that same session JWT and a game id**.
The game server verifies the signature locally against Stytch's public JWKS, reads our account
UUID from the claim, and checks for a `participant` row. Reconnect
matters — a four-player game runs hours, and the event log makes resuming a solved problem.

### Transport

**WebSockets carrying Commands up and Events down.** Godot's high-level multiplayer
(`MultiplayerAPI`, `[Rpc]`, `MultiplayerSynchronizer`) is not used: it assumes Godot on both
ends and node-tree replication, which is the wrong model for discrete board state.

Turn-based play means no prediction and no rollback netcode.

## Deployment

**ECS Fargate for both servers, RDS Postgres, S3 for blobs.** One deployment model, because
the game server holds live WebSocket sessions and cannot be serverless — so making the
management API serverless would mean maintaining two models for no gain.

The workload is undemanding: turn-based, small state, and **no simulation tick** — the server
never advances state on a timer, only when a command arrives.

**There is still a continuous relay.** Cursors, pings and live drag positions are broadcast
between players at a fixed rate and never logged, because a virtual tabletop is largely people
pointing at things. Four players at 20Hz is a few hundred messages a second, which is nothing —
but it is not nothing per second, and this document previously said "no tick loop" without the
distinction. See [presence](superpowers/specs/2026-08-30-presence-design.md) for what the relay
carries; none of it is persisted or logged.

The real constraints are statefulness, not throughput:

| Constraint | Resolution |
| --- | --- |
| ALB idle timeout drops long-idle sockets | App-level ping/pong heartbeat |
| Deploys kill tasks holding live games | ECS `stopTimeout`, drain to the event log on SIGTERM |
| Reconnect may land on a different task | Rehydrate from the event log — needed for crash recovery anyway |

That last row is why Fargate works at all: the event log makes the game server
stateless-on-recovery, and horizontal scaling falls out of the same mechanism.

## There is no offline mode

The client always connects to a game server. There is no bundled server, no in-process
authority, and no local-only play.

This was considered and rejected. Bundling would mean either a second transport implementation
that skips serialization — so wire-format bugs surface only in online play — or shipping and
managing a second process on the player's machine. Neither is worth it for a product whose
normal mode is multiplayer.

The consequence is that **the network path is the only path**, exercised on every playtest
rather than discovered late.

## Client and server responsibilities

The client owns: camera, rendering, input, animation, measuring, movement ghosts,
affordance drawing, UI, and asset loading and caching. Measuring and ghosts are computed
client-side and broadcast to the other players, never logged.

It never owns: dice, state transitions, turn order, wound allocation, or legality.

Three distinct kinds of checking, and they live in different places:

1. **Geometry** — is that in range, where does this model settle, is LoS clear. Computed on both,
   but the client's copy is a **prediction for responsiveness only**. The server's is the answer.
   A disagreement is resolved by resyncing the client, not by reconciling the two.
2. **Rules legality** — is this action legal under the ruleset. **Server only.** The client
   renders a prefetched answer; it does not compute one.
3. **Authorization** — is it this team's window, is that unit theirs, do they own that army.
   Server only, and a session and identity concern rather than a rules one.

Because the client does not hold the full rules state, the server *can* send **per-player
filtered event streams** if hidden information ever matters. The mechanism falls out of the thin
client rather than needing to be designed in later.

Its original two justifications are both gone: there are no victory conditions, so secret
objectives do not exist, and hidden deployment is `Ambush`, a `GameRules` hook. Nothing in the
product needs filtering today.

## Web frontend

Settled by [the web app](superpowers/specs/2026-08-29-web-app-design.md): it exists, and the
line is **preparation versus play**. The browser owns armies, assets, the account and login; the
client owns the game browser, joining, setup and play. The client does not edit armies — it
creates a game and picks one.

Account and army management are a **Svelte SPA**, not part of the game client. Users manage
their own persistent things in a browser: identity, armies imported from Army Forge, mesh
binding per model slot, and asset uploads.

This removes real work from the hard-to-iterate side — the Godot client needs no list-building
UI, no upload UI, and no login form — and makes the HTTP API a genuine API surface rather than an
internal detail of one client.

- **Auth is easier in a browser.** Stytch's prebuilt components are web components, and OAuth
  and magic links both need a page to redirect back to. OTP and password are Basic-auth
  endpoints that run in management either way. The browser owns login and the client acquires a
  session from it — by loopback or device code, both deferred.
- **Uploads belong in the browser.** Dragging a `.glb` onto a page beats a file dialog in Godot.
  There is no server-side mesh processing to speak of: conversion is the offline prep tool, and
  the only server-side step is a ranged read of the GLB header to count triangles.
- **Collision volume authoring is the web's too.** Base sizes are a preset dropdown (25/32/40mm
  round, oval sizes) covering effectively every model. Terrain volumes were held back for the
  in-game editor because they "need real 3D manipulation" — that was true of convex hulls, which
  are gone. A footprint and two heights is a top-down task over a model preview, so both live in
  the browser.

### A web game client stays possible

`Tabletop.Geometry` and `Tabletop.Protocol` have no Godot dependency, so they compile to WASM.
A future browser client is unblocked either way — Blazor WASM, or Godot's own web export. Not a
target for the finished game, which needs to look good, but the seam protects the option.

## Repository layout

One repo, one solution. `libs/` is shared by client and servers, so separate repos would mean
publishing versioned packages — a significant tax for a single developer and a direct hit to the
iteration loop. Different deployment artifacts from one source tree is normal.

```
client/                  Godot game client (the only Godot dependency)
server/game/             live sessions, rules engine, event log
server/management/       ASP.NET Core CRUD API
web/                     Svelte SPA
tools/mesh-prep/         offline STL to GLB conversion, run by the user
libs/                    Tabletop.Geometry, Tabletop.Protocol, Tabletop.Content
docs/  scripts/          repo-level support
```

Top-level directories are deliverables, except `libs/` (consumed by them) and `docs/`/`scripts/`
(neither). The Godot project lives in `client/` rather than at the repo root so its `**/*.cs`
glob cannot reach sibling projects.

The Svelte app shares no code with the .NET projects, but tracks the HTTP API closely enough
that versioning it separately would create drift for no benefit.

## Game management data model

The organising line: **management owns everything *about* a game; the game server owns what
*happened* in it.** One shared `game_id`, two schemas, no joins across the boundary.

The game server **reads** management's schema at hydration — a read is not a join, so the line
holds, but the two services do share a database and a management migration can break the game
server.

**[`docs/data-model.md`](data-model.md) holds the entity diagram** — every entity from every
design session and how they relate. It supersedes the sketch that used to sit here.

Everything in it is in the management schema except one entity. The game schema holds exactly
one thing: `GameEvent (game_id, seq, kind, payload)`.

`Participant` is an ordinary reference to `Account`, not a copy of a name, because deleting an
account deletes every game it was in.

**A game *is* a lobby.** Not `Room` plus `Game`. One entity with a status, because a room is just
a game that has not started. Two entities would mean a handoff, a copy, and two sources of truth
for who is playing. **Ended is a flag, not a terminal state** — restarting an ended game is the
same hydrate-from-the-log operation as recovering a crashed task.

**The game server's interface is commands and events.** It exposes no HTTP resources; it reads
a game's row at hydration and writes only events.

*Corrected 2026-08-29.* This previously read "the game server has no CRUD surface" and claimed
that was "what keeps management restartable at will and confines deploy risk to one service."
Neither follows. Management is restartable because it **holds no live state**; deploy risk is
confined to the game server because it **holds live WebSocket sessions**. Both are true for
reasons that have nothing to do with CRUD.

**An army snapshot is a frozen copy, not a version column.** Written once at game load, never
updated, so editing a saved army cannot reach into a live match. It is a `jsonb` column on
`participant` rather than an entity — see [`docs/schema.md`](schema.md) — and it is opaque on
purpose: normalised, a later migration would silently alter what a past game was played with.

**A game names a `GameRules` and pins a `Properties` hash.** Undo is replay, and replay needs
the exact ruleset that produced the log. There is no `RulePack` entity — see
[identity and enumerations](superpowers/specs/2026-08-29-identity-and-enumerations-design.md).
The `properties` table is the pinned set itself, keyed by the hash of its contents, not a
registry that resolves packs by identity.

**Pinning covers only the data half, and that is accepted.** `GameRules` is C# shipped with the
build, so a pipeline order changing between deploys can make an old game replay differently.
Content-addressing solves this for `Properties` and not for `GameRules`. Pinning a build version
would make every deploy a wall old games cannot cross, which is worse than a rare divergence in
a friendly-play tabletop. Undo within a session is unaffected — the engine cannot change while a
session is live.

**Identifiers are UUIDv7 throughout — no integer ids anywhere.** Generated in application code
(`Guid.CreateVersion7()`), stored as Postgres `uuid`. v7 rather than v4 because its leading 48
bits are a timestamp, so inserts append near the end of the index instead of scattering across
it. Application-generated rather than a column default because ids must exist before external
systems are told about them — Stytch's `external_id` is set to `Account.id`.

Nothing here stores model positions, wounds, or whose activation it is. That is never a row; it
is events, reconstructed into memory. A table growing a "current position" column means the
event spine has been broken.

## Persistence

Postgres on RDS, and **[`docs/schema.md`](schema.md) holds the tables**. Accounts, assets,
armies, layouts and the whole army tree are ordinary relational tables — normalised rather than
documents, so the closed sets and typed values the design argued for are constraints the
database enforces.

The game event log is append-only — `(game_id, seq, kind, payload jsonb)` with a unique
constraint on `(game_id, seq)`; `jsonb` means adding an event type is not a migration.

Blobs (meshes, textures) do not live in Postgres. They go in S3, content-addressed by hash, with
metadata rows pointing at them.

`libs/` never learns about persistence. No ORM entities, no attributes, no `DbContext`
awareness; the servers map to rows.

## Local development

The repo lives in WSL; the Godot editor runs on the Windows host. Because there is no offline
mode, running the game means running a server:

- Both servers and Postgres run in WSL.
- The Godot client runs on Windows and connects over forwarded localhost.

This is the only configuration, which means it is the one that is always tested. Deploying
changes the address, not the shape.

## Contracts to freeze before parallel work

These are the seams. Work behind them can proceed independently; work across them cannot start
until they are settled.

| Contract | Between |
| --- | --- |
| `Tabletop.Geometry` API surface | client, game server |
| `Tabletop.Protocol` command, event and presence schema | client, game server |
| `Tabletop.Content` army snapshot types | client, both servers |

**Auth is not on this list, and the first version of this document was right about that** —
though not for the reason it gave. It argued Stytch was the token authority for both servers,
so there was no bespoke format to agree on. Confining Stytch to management looked like it
reversed that, and [accounts](superpowers/specs/2026-08-29-accounts-design.md) added a join
token as a fourth contract on those grounds. That token was dropped on 08-30: a game has an id,
the session JWT says who you are, and the `participant` row records who is allowed in, so
there was no format left to agree. We issue no token of our own at all. There are no management-to-game-server calls, and therefore
no M2M.

**Who instantiates a game session is settled: nobody does.** A client opens a socket naming a
game, and if no session for that game is in memory the server hydrates it from the log.
Starting, reconnecting, and crash recovery are one path.

## Build order

1. **Contracts** — the four above, as types and schemas with no implementations behind them.
2. **Geometry** — volumes, distance, LoS, surface settling. Headless tests, zero dependencies.
3. **Management API and web app** — Stytch auth, signup, accounts. No game concepts at all.
4. **Game server skeleton** — WebSocket, session lifecycle, event log, command dispatch.
5. **Client shell** — camera, table, model dragging, measuring, ghosts. Geometry only, no server.
6. **Setup phase** — table sizing and terrain placement, ending in a snapshot. Not commands;
   see [setup content](superpowers/specs/2026-08-29-setup-content-design.md).
7. **Scheduler and OPR skeleton** — deployment, rounds, team-alternating activation, moves.
8. **Resolution pipelines** — dice, the modifier system, shooting and melee.
9. **Army management** — Army Forge import, mesh binding per model slot.
10. **Assets** — content-addressed upload direct to S3 by presigned URL, from the web app.
11. **Objectives, LoS and cover refinement, replay viewer, AI.** Not missions — those are
    content in a book, and automating them is a later feature rather than a module.

Steps 2 and 3 have no dependency on anything and no overlap with each other, so they run in
parallel immediately after step 1. Steps 4 and 5 run in parallel once the contracts exist.

Networking is no longer deferred — it is the only transport, so it is exercised from step 6
onward rather than bolted on late.

The first playable seats two, but nothing in the code says "two": players are a list and teams
are a separate concept, so a free-for-all is N teams of one and differs only in the order over
teams.
