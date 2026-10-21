# Architecture

The cross-cutting design decisions for the tabletop wargaming simulator, and why they were made.
`docs/design/README.md` is the system design and owns structure and vocabulary; the module design
documents beside it own per-module structure. Where this document and one of those disagree, the later
document wins.

## Deliverables

Six things ship. Four go to players; two go to developers.

| Deliverable | Directory | Platform | Audience |
| --- | --- | --- | --- |
| Game client | `client/` | Windows first, other desktop platforms possible (Godot export) | Players |
| Game server | `server/game/` | Linux (container) | Players |
| Management API | `server/management/` | Linux (container) | Players |
| Web app | `web/` | Static (Svelte SPA) | Players |
| Simulator | `sim/` | .NET console + library | Developers |
| Viewer | `viewer/` | Static page; hosted build at `/viewer/` in the web image | Developers; admins (Replays) |

`sim/` and `viewer/` are deliverables rather than tests
([simulation](superpowers/specs/2026-09-06-simulation-design.md)): a simulator that only xUnit can
start cannot be pointed at a problem. Neither is in the client's export, neither is referenced by a
server, and a release that omits both is complete. `sim/` plays a game headlessly and asserts the
invariants that hold for any game; it reads game state freely but contains no second implementation
of the rules. `viewer/` draws a run's recording top-down and is separate from `web/`, which owns
preparation for players — a static page with its own `package.json`, plain Vite and Svelte 5,
sharing no code and no build with `web/`.

The recording is `viewer/`'s only input shape. A recording file is read directly; a live game over
the socket is *converted* into the same frames in the browser, so the renderer, the timeline and
the panels have one shape to draw and neither feed is privileged. That second path is the one place
in the project where `Tabletop.Protocol` and the event applier exist twice, and the duplication is
generated away rather than maintained: the TypeScript types come from the C# JSON contracts, and
the ported applier is checked against the C# one by replaying a recording through both and
comparing every frame, on the C# side in `tests/Tabletop.Viewer.Tests` and on the TypeScript side in
`viewer/tests/agreement.test.ts`. Playback is the whole of it — the viewer
sends no command and answers no Ask, and the seam where it could is deliberately empty.

**Windows is the client's first target, not its only one.** The same Godot project exports to
Linux and macOS, so **the client may not take a Windows-only dependency**; the remembered session
uses a portable mechanism rather than Windows data protection. No work is planned for the other
platforms and no export preset for them exists.

There is no mesh prep tool and no language exception for one. STL conversion runs in the browser,
inside the web app's upload flow
([mesh conversion](superpowers/specs/2026-09-04-mesh-conversion-design.md)).

The web app exists and owns **preparation** — armies, assets, the account, and its own login.
The client owns **play**, plus the game list, joining, and its own login screen; both log in
against management's two methods ([the client shell](superpowers/specs/2026-09-04-client-shell-design.md)).
See [the web app](superpowers/specs/2026-08-29-web-app-design.md).

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
| `Tabletop.Geometry` | client, game server | Collision volumes, distance, LoS, collision (rest, slide, separate) |
| `Tabletop.Protocol` | client, both servers | Commands, events, Asks, presence, setup edits, the rules-free event applier, wire format. Management reads only `ProtocolVersion` and the game-row enums `GameStatus`, `SchedulerShape` and `Enforcement` |
| `Tabletop.Content` | client, both servers | Army, unit, model slot and weapon types; the table state (board, terrain placements, markers, deployment zones) |

Geometry is shared because the client draws with it and the server adjudicates with it, and
the two must agree exactly. Protocol is shared by definition. Content is shared because the
client renders armies and both servers store and interpret them. Whether an army reaches the
client over HTTP or over the WebSocket is a transport detail and changes nothing about the
types.

The table state is one record with two owners, and the same three consume it: management stores
it as a user's saved layout, the game server writes it as a game's setup draft and snapshot, and
the client draws and edits it.

The rules engine — pipelines, scheduler, the property sets, modifier system — is
**not** in `libs/`. It lives in `server/game/` and goes nowhere else.

## Command / Event spine

Players never mutate state. They submit **Commands** (intents: `MoveUnit`, an Ask's answer,
`Pass`). The game server validates a command and emits ordered **Events** (facts:
`ModelMoved`, `StepCompleted`, `TurnStarted`). In play, state changes only by applying events.
Before play — lobby and setup — the game server writes rows instead, and nothing is logged; see
[the setup phase](superpowers/specs/2026-09-04-setup-phase-design.md). There is no end-turn
command: the turn ends when the declared action's sequence finishes.

One pattern serves as: the network protocol, undo, save/load, replay, the test harness, the
client's animation queue, and the audit trail for explaining a result.

RNG lives in server state as a seed. Every roll is recorded in the step event that made it —
threshold, modifiers, and each die's raw and modified result — never re-derived on replay.

## How the client stays responsive without the rules

The client must draw a movement range and grey out an illegal target without waiting on a
round trip. It does this without owning the rules, by distinguishing two kinds of question:

- **Discrete actions** (legal targets, available abilities) — the server sends the legal set
  when the team's input window opens. Prefetched, so there is no latency at click time.
- **Continuous actions** — movement is *anywhere* within an allowance, not an enumerable
  list. The server sends **parameters**, not an answer: move allowance, terrain effects, and the
  ids of volumes that cannot be crossed. `Tabletop.Geometry` renders the region from those
  parameters. OPR's difficult terrain caps a unit's whole move at 6" rather than scaling it, so
  the parameter is whatever `GameRules` computes, not a multiplier.

The second is the important one. It is why the client needs geometry and only geometry: the
server describes the constraint, the client draws its consequence.

**Measuring and planning are never commands.** Pre-measuring, range checks, and movement
ghosts are pure geometry, computed locally — so waiting players stay busy and the mouse never
feels laggy.

They are **relayed**, which is not the same thing:
[presence](superpowers/specs/2026-08-30-presence-design.md) puts measurements and ghosts on the
ephemeral channel, because a tape measure on a real table is visible to everyone at it. Nothing
waits on them, nothing validates them, and nothing logs them.

## Rules enforcement

**Three positions, set per game: `silent | warn | strict`**
([the whole-design review](superpowers/specs/2026-09-04-whole-design-review.md)). Under `silent`
and `warn` the server accepts every
command and every Ask answer, logging a warning under `warn` when one is outside the legal set.
Under `strict` an out-of-set Ask answer or an illegal command is refused. **The menu's edits are
accepted under every setting** — they are the override path, visible to opponents and recorded
in the event log, never silent. Model placement follows the same rule: the client prevents base
overlap, an Impassable Solid and a path through a model while dragging (Alt overrides), and the server judges a move answer under the
enforcement setting: `move_overlap`, `move_through_models`, `move_impassable`, and `move_separation` for p.7's 1" between units, model against model in three dimensions (a model passing above another does not move through it). Every
placement rests on the highest Solid top under its base, so no model stands inside a Solid. Coherency is not judged.

**One policy behind the three positions.** Any system hands it a breach: what was broken, the sentence players read, and what `strict` does about it. The dispatcher hands it a command's legality; the engine hands it what Grimdark Future's judge finds in the engine's own proposal. Under `warn` the warning rides on the event; under `strict` a command breach is refused and a state-time breach ends the game. The first state-time breach is the end of round 4 (GF p.7): `silent` lets round 5 start, `warn` asks every team End or Continue (End ends the game as `strict` does), `strict` logs `game_ended` and the game server marks the row ended ([the enforcement engine](superpowers/specs/2026-10-02-enforcement-engine-design.md)).

Grimdark Future's core mission is scored by the engine: objectives are seized at each round's end, and the round-4 end names the winner ([objectives](superpowers/specs/2026-10-02-objectives-design.md)).

The legality API is **enumeration, not validation** — `EnumerateLegalCommands()` rather than
only `IsLegal(command)`. Validation falls out of enumeration but not the reverse, and
enumeration is what the client needs anyway to render affordances. An AI opponent later gets
its action space for free.

A modified client submitting illegal commands gets exactly what an honest one gets: refused
under `strict`, accepted and flagged otherwise. What authority protects is state coherence
rather than fair play.

## Undo

A first-class primitive, because friendly play means out-of-order or mistaken actions are
normal. Undo is **replay to sequence N from the initial state**, not inverse events. The initial state
is the setup snapshot — the record the setup phase ends with — so there is nothing else to
replay from and no separate checkpoint mechanism. An undone roll's dice stay drawn, so an undo leads to
fresh dice and cannot be used to see the coming dice or fish for rolls.

## Abilities

Resolution is a **stack of sequences**
([action sequences](superpowers/specs/2026-09-03-action-sequences-design.md)). Targeting and
morale live outside the sequence: targeting is the action's Ask, morale fires from ruleset-owned
triggers, and Grimdark Future's shooting and melee are one four-step attack sequence invoked from
two contexts. A sequence is a
list of C# steps run over a frame stack; a step completes, asks a team for input, pushes a
nested sequence, or opens an interrupt window, and the stack is rebuilt from the log the way
the action record is. Properties declaratively attach to a roll or step with a condition and
contribute an effect — one of the eighteen kinds of
[the effect grammar](superpowers/specs/2026-08-29-effect-grammar-design.md) as
[the spell library](superpowers/specs/2026-10-02-spell-library-design.md) and
[army-rule hooks](superpowers/specs/2026-10-02-army-rule-hooks-design.md) extend it.

The hard part is stacking and ordering — replacement vs additive effects, caps, floors.
Modifier priority must be explicit rather than relying on registration order. Restricting
modifiers to arithmetic would make them commute and remove the need, but that holds for one of
the twelve effect kinds ([the effect grammar](superpowers/specs/2026-08-29-effect-grammar-design.md)).
Ordering is a field on the effect.

An ability is not only a modifier. Eighteen kinds cover Grimdark Future and its army books, four of which act on
*other properties* — granting, suppressing, or altering a parameter — so resolving an
interaction is two passes: settle which properties and parameters are live, then apply them.

## Turn scheduling

A **scheduler** answers "whose input is needed next, and what is legal in this window?".
Windows are **scoped to a team, not a player** — under the alternating shape teams alternate
activations, and any member of the active team may act. Coordination inside a team is social.

The ruleset does not own the scheduler. The scheduler is a generic engine component with two
implementations selected per game, and
`GameRules` supplies its policies — who goes first each round, which units may activate, the
phases inside a turn, the round-boundary hooks. See
[the scheduler](superpowers/specs/2026-09-04-scheduler-design.md).

- 2 players = two teams of one.
- Free-for-all = N teams of one, differing in the order over teams and in the deployment zones
  (a 2'x1' corner area each, GF page 50). The first-team policy is the same as for two teams:
  round one goes to whoever deployed first and every later round to whoever finished activating
  first.
- Player-turn is the second scheduler shape behind the same interface, chosen per game rather
  than per ruleset — OPR's `GameRules` runs under both.

No engine code branches on ruleset identity; the scheduler is the firewall.

## Rules as data

The goal is **extensibility and multi-ruleset support**. The data half is rows in eight Postgres
tables with check constraints, per [`docs/schema.md`](schema.md). Adding a custom rule is a data
edit, and supporting a game beyond OPR is new data rather than new engine code.

**How authored content reaches those tables.** A set is one JSON document under `server/game/Content/*.properties.json`, carrying the eight
tables' shape without ids. It is content-addressed by SHA-256 over a canonical serialisation
(`sha256:…`), and the game server seeds it as rows at startup, once per hash and idempotently.
Editing a document yields a new hash and so a new set. Every game that has not ended moves to the set the running server seeded for its game rules at its next hydration; an ended game keeps its set ([games follow the current rules](superpowers/specs/2026-10-19-games-follow-the-current-rules-design.md)).
`GET /v1/properties` lists, per ruleset and name, the set seeded most recently (`properties.seeded_at`), newest first, so the set the running server seeded leads; an older set stays for the ended games that name it. Effect and condition ids are minted at seeding
and are stable thereafter within a database. The Army Forge import of
[spell effects](superpowers/specs/2026-09-04-spell-effects-design.md) is a later writer of the
same format.

**In-repo now, user-authored eventually.** The order matters because OPR's rulebook gives a finite, known vocabulary to design against. While `Properties` is
authored in-repo and ships with the build there is no upload endpoint, no sandbox and no
moderation surface; when users can author it, that is additive.

Packs are loaded and interpreted **only by the game server**. The client never sees a rule
pack; it receives the consequences of one.

- **Rules as data** — `Properties`: typed stat definitions and one catalog of named rules.
- **Rules as engine** — the pipeline interpreter that executes that data.

### Closed schema, extensible content

The *shape* of `Properties` is closed and the content extends freely. Adding a rule means adding
a catalog entry and the effect rows that say what it does.

The rejected alternative is an open property bag where the engine reads keys it recognises: it
authors just as fast but a mistyped key silently does nothing. With a closed schema, typos fail
loudly. The closure is the database's: foreign keys into the same set and per-kind check
constraints.

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

**What is edited every day is data; what is edited once per ruleset stays C#.** The two halves
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

**`Properties` are content-addressed, and a game follows its game rules' current set.** Owner ruling: "If a rule FIX is
released it shouldnt publish a new version. everything should move to it.. its a fix". A game that has not ended is moved to the
set the running server seeded at hydration, and grants minted under the old set resolve through effect aliases
([games follow the current rules](superpowers/specs/2026-10-19-games-follow-the-current-rules-design.md)). Replay applies recorded dice and results, so undo stays faithful across the move.
An ended game keeps its set.

## Geometry

Full 3D math in inches (one world unit = one inch), including `Collision`: resting a model on the
surface it lands on, sliding it along impassable terrain and separating it from other models.

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
client is a desktop build and the server is Linux. Divergence cannot compound, because the client holds
no authoritative state to diverge *into*. That constraint belongs to lockstep architectures,
which this deliberately is not.

## Table and layout

[The table and its setup content](superpowers/specs/2026-08-29-setup-content-design.md) designs
these as data.

- **Table** — width and length in inches, and a texture. A rectangle, with no shape field and no
  play-area volume; the four-player square is a rectangle like any other. Arbitrary sizes, never a
  constant. Setup content's inventory of named presets is undecided.
- **Arbitrary sizes are the table's property, not the deployment styles'.**
  `Tabletop.Geometry.ZoneStyles` ships seven built-in styles read from Grimdark Future p.22's
  diagrams on a 72×48 table, six of them anchored at absolute insets of 12" and 24". Those six
  therefore assume a table near that size. Small tables, the 30×22 skirmish size among them, are
  out of scope by the owner's ruling: below a 48" long edge or a 24" short edge the anchored styles
  overlap each other or cover the whole board. That is an accepted limit rather than a defect.
  `Table`, `TableSize` and every measurement outside `ZoneStyles` take any positive size.
- **Layout** — a saved setup snapshot: board size and texture, terrain placements, marker
  placements, deployment zones. Physical only, no rules.
- **Mission** — **not a record.** Missions are published content in OPR and Warhammer, players
  read them out of a book, and the model holds only the core mission's objectives.

A deployment zone names a **built-in style**, which resolves to a *set* of polygons. Six of the
seven styles anchor their measurements to table features; Side Battle scales its two legs with
the edges they run along. A zone names a **team ordinal**,
not a team, because a saved layout has zones but no game to have teams in.

A terrain piece is a mesh plus collision volumes plus declarations. The engine reads a closed
set of six — impassable, blocks LoS, see in and out, cover, difficult, dangerous — and carries
the rest as free text nobody interprets. Those six are OPR's own terrain types, read from the
rulebook. Same asset pipeline as models.

### Terrain placement is a game phase, not an editor

Terrain is placed in a setup phase rather than in a separate map editor application, and there
is no second persistence path. The sequence is: setup (size, terrain, markers, zones — unlogged,
free for all, per [the setup phase](superpowers/specs/2026-09-04-setup-phase-design.md)) → the
status flip → deployment (logged, driven by the scheduler) → rounds.

**The setup phase is not logged.** Logging placement as ordinary Commands would make a predefined
map a batch of events, give undo during placement for free, and get alternating placement from
team-scoped input windows. Setup needs none of that; it needs a settled result. Only the state at
the end of the phase is recorded, and that record is the event log's initial state.

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
served by presigned URL straight from S3 — management issues the URLs and the bytes never pass
through it ([the management API](superpowers/specs/2026-08-29-management-api-design.md)). **A library is private for discovery, not for access**: nobody
can browse or search another account's assets, but participants in a game download the assets that
game uses, straight from wherever they live. Nothing enters anyone else's library.

**Upload accepts GLB and rejects STL.** Narrowed from "glTF/GLB" by
[the management API](superpowers/specs/2026-08-29-management-api-design.md): a `.gltf` with
external buffers is several files with relative references, which breaks single-object
presigned upload. GLB is the same format in one file and is Blender's default export.

The miniature ecosystem distributes STL, which Godot cannot load and which is 50–100× too dense
for realtime — a print-ready 32mm mini is routinely 500k–2M triangles. Converting it server-side would mean a job queue, a worker, an
async upload state machine, and a source/derived blob split.

Instead, conversion runs **in the browser, inside the web app's upload flow**. The product gains
a validation rule, loses an entire subsystem, and has nothing to install.

The residual risk is that nothing stops a user uploading an undecimated GLB straight out of
Blender, which then lands on every client in the game. A triangle-count check is the only
guardrail once the pipeline is out of the product, and it runs as a **ranged read of the GLB's
JSON header** on upload confirmation — see
[the management API](superpowers/specs/2026-08-29-management-api-design.md). It warns; it does
not block.

**A missing or undownloadable mesh must degrade to a proxy shape, with the game fully playable.**
Free, given the rules never touch the mesh — but only if built that way from the start.

What the proxy is drawn from differs by kind: terrain has collision volumes to draw, a
**miniature has only its base and height**, so a slot draws its own hitbox, the base shape
extruded to its height, semi-transparent
([the asset library](superpowers/specs/2026-08-28-asset-library-design.md)).

## Two servers

The fault line is **CRUD versus live sessions**, and it is drawn from the start rather than
deferred, because the two differ in transport, state, and deploy risk.

### `server/management/` — ASP.NET Core, HTTP

| Module | State |
| --- | --- |
| Identity | Accounts, and login by password or email code; the only module that knows Stytch exists. No session rows — the JWT is the session |
| Content | Armies, layouts, the asset library and its folders (Postgres) |
| Asset store | Content-addressed blobs (S3) |
| Games | Game records, teams, participants, the public game browser |

Ordinary CRUD. Holds no live state, so it can restart freely and deploy at any time. Both the
web app and the game client talk to it over HTTP, and it is the only thing that talks to Stytch.

### `server/game/` — WebSocket

Live games in memory, plus the append-only event log. **The only module holding live state.**
It needs graceful shutdown that drains to the event log, and it is what constrains deploys.

Authentication does not run through Stytch here. The client logs in against management from
its own login screen; creating or joining a game is an ordinary management endpoint, and the
client then opens its socket with **that same session JWT and a game id**.
The game server verifies the signature locally against Stytch's public JWKS, reads our account
UUID from the claim, and checks for a `participant` row. Reconnect
matters — a four-player game runs hours, and the event log makes resuming a solved problem.

### Transport

**WebSockets carrying Commands up and Events down** — plus, on the same socket, Asks down,
presence both ways and discarded, and during setup only the setup edits that write the draft
([presence](superpowers/specs/2026-08-30-presence-design.md),
[the setup phase](superpowers/specs/2026-09-04-setup-phase-design.md)). Godot's high-level multiplayer
(`MultiplayerAPI`, `[Rpc]`, `MultiplayerSynchronizer`) is not used: it assumes Godot on both
ends and node-tree replication, which is the wrong model for discrete board state.

Turn-based play means no prediction of authoritative state and no rollback netcode. The
client's geometry is a cosmetic prediction, overwritten by the event, per Geometry above.

**`ProtocolVersion` is 1 and moves only for an installed client.** Additive wire kinds (`at_rest`, `game_ended`) do not move it, because every client is built from the same commit as the servers. That holds before release; the first release moves the version on the next addition an older client cannot read.

## Deployment

**One Linux host behind Caddy.** `deploy/` is the production path (`deploy/README.md` has the steps):
both servers run from their Release images (`server/*/Dockerfile`) under `deploy/docker-compose.prod.yml`, or
as published dlls under the `deploy/systemd/` units, listening on `127.0.0.1` only. Caddy with a WAF, configured by the owner outside this repo, is the only
public listener; `deploy/README.md` lists what it fronts (the static web build, `/v1`, `/ws`, the blob endpoint). S3 or R2 is the preferred blob
store; MinIO with a bucket-scoped service user is the self-hosted one. The repo-root `docker-compose.yml` is
development only.

**What the environment allows is enforced at startup** (`StartupGuard` in each server). Development
behaviour (the dev-session mint, `SetDice`, startup migrations) needs `ASPNETCORE_ENVIRONMENT=Development`
and `TABLETOP_DEV_MODE=1` together; any other environment refuses the test Stytch URL, http or loopback
public origins and the dev stack's credentials, and never reads a `.env`. The servers connect as runtime
roles with DML only; the owner role is used only by `Tabletop.Management --migrate`.

One deployment model for both servers, because the game server holds live WebSocket sessions and
cannot be serverless.

The workload is undemanding: turn-based, small state, and **no simulation tick** — the server
never advances state on a timer, only when a command arrives.

**There is still a continuous relay.** Cursors, pings and live drag positions are broadcast
between players at a fixed rate and never logged, because a virtual tabletop is largely people
pointing at things. Four players at 20Hz is a few hundred messages a second. See
[presence](superpowers/specs/2026-08-30-presence-design.md) for what the relay carries; none of
it is persisted or logged.

The real constraints are statefulness, not throughput:

| Constraint | Resolution |
| --- | --- |
| A proxy's idle timeout drops long-idle sockets | Kestrel's WebSocket ping/pong control frames (`KeepAliveInterval` 20 s, `KeepAliveTimeout` 60 s); no protocol message kind is involved |
| Deploys kill processes holding live games | A 30 s shutdown timeout (compose `stop_grace_period`, systemd `TimeoutStopSec` 40 s), drain to the event log on SIGTERM |
| Reconnect may land on a different process | Rehydrate from the event log — needed for crash recovery anyway |

That last row is what makes a restart safe: the event log makes the game server
stateless-on-recovery, and horizontal scaling would fall out of the same mechanism.

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

The core mission has no secret objectives, and hidden deployment is
`Ambush`, a `GameRules` hook. One thing does use filtering: casting's simultaneous token spend,
where every eligible team answers concurrently and the answers are withheld until the window
closes ([action sequences](superpowers/specs/2026-09-03-action-sequences-design.md)).

## Web frontend

The line is **preparation versus play** ([the web app](superpowers/specs/2026-08-29-web-app-design.md)).
The browser owns armies, assets and the account; the client owns the game list, joining, setup
and play. Each has its own login screen against the same two management methods. The client does
not edit armies — it creates a game and picks one.

Account and army management are a **Svelte SPA**, not part of the game client. Users manage
their own persistent things in a browser: identity, armies imported from Army Forge, mesh
binding per model slot, and asset uploads.

This removes real work from the hard-to-iterate side — the Godot client needs no list-building
UI and no upload UI; its login is two fields — and makes the HTTP API a genuine API surface
rather than an internal detail of one client.

- **Login needs no browser.** There is no OAuth and no magic link
  ([the client shell](superpowers/specs/2026-09-04-client-shell-design.md)), so no login method
  needs a page to redirect to. Password and email one-time code run through management,
  and both the web app and the client have their own login form. Nothing crosses from the
  browser to the client.
- **Uploads belong in the browser.** Dragging a `.glb` onto a page beats a file dialog in Godot.
  There is no server-side mesh processing to speak of: conversion runs in the browser, and
  the only server-side step is a ranged read of the GLB header to count triangles.
- **Collision volume authoring is the web's too.** Base sizes are a preset dropdown (25/32/40mm
  round, oval sizes) covering effectively every model. A terrain volume is a footprint and two
  heights, a top-down task over a model preview, so it lives in the browser too.

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
sim/                     headless full-game simulator (developers)
viewer/                  recording viewer, a static page (developers; admin Replays at /viewer/)
libs/                    Tabletop.Geometry, Tabletop.Protocol, Tabletop.Content
tests/                   xUnit projects: one per library, per server, and for the client's Godot-free core
docs/  scripts/          repo-level support
```

Top-level directories are deliverables, except `libs/` (consumed by them) and `tests/`,
`docs/` and `scripts/` (neither). The Godot project lives in `client/` rather than at the repo
root so its `**/*.cs` glob cannot reach sibling projects.

The Svelte app shares no code with the .NET projects, but tracks the HTTP API closely enough
that versioning it separately would create drift for no benefit.

## Game management data model

The organising line: **management owns everything *about* a game; the game server owns what
*happened* in it.** One shared `game_id`, two schemas, no joins across the boundary.

The game server **reads** management's schema at hydration — a read is not a join, so the line
holds, but the two services do share a database and a management migration can break the game
server.

**[`docs/data-model.md`](data-model.md) holds the entity diagram** — every entity from every
design session and how they relate.

Everything in it is in the management schema except one entity. The game schema holds exactly
one thing: `GameEvent (game_id, seq, kind, payload)`. The game server nonetheless **writes**
management's rows for a game while its status is `lobby` or `setup` — status, teams, each
participant's team and army snapshot, and the `table_state` draft — and from `playing` on
writes only events and one row: `game.status = 'ended'` when its own log ends the game
([the whole-design review](superpowers/specs/2026-09-04-whole-design-review.md)).

`Participant` is an ordinary reference to `Account`, not a copy of a name, because deleting an
account deletes every game it hosted and leaves every game it joined, so no `participant` row outlives it.

**A game *is* a lobby.** Not `Room` plus `Game`. One entity with a status, because a room is just
a game that has not started. Two entities would mean a handoff, a copy, and two sources of truth
for who is playing. **Ended is a flag, not a terminal state** — restarting an ended game is the
same hydrate-from-the-log operation as recovering a crashed task.

**The game server's interface is commands and events.** It exposes no HTTP resources. From
`playing` on it reads a game's rows at hydration and writes events and one row, `ended`, when its
own log ends the game; before that it writes the game's own rows, as above.

Management is restartable at will because it **holds no live state**; deploy risk is confined to
the game server because it **holds live WebSocket sessions**. Neither follows from which service
has a CRUD surface.

**An army snapshot is a frozen copy, not a version column.** Written once when setup ends —
the same moment the scheduler shape freezes — never updated, so editing a saved army cannot
reach into a live match. A participant who joins after that has none. It is a `jsonb` column on
`participant` rather than an entity — see [`docs/schema.md`](schema.md) — and it is opaque on
purpose: normalised, a later migration would silently alter what a past game was played with.

**A game names a `GameRules` and a `Properties` hash.** The hash is its game rules' current set until the game ends
([games follow the current rules](superpowers/specs/2026-10-19-games-follow-the-current-rules-design.md)). There is no `RulePack` entity — see
[identity and enumerations](superpowers/specs/2026-08-29-identity-and-enumerations-design.md).
The `properties` table is the set itself, keyed by the hash of its contents, not a
registry that resolves packs by identity.

**Neither half is pinned across a release, and that is accepted.** `GameRules` is C# shipped with the
build, and a game that has not ended moves to the current `Properties` set, so a change between deploys can make an old log
replay differently; replay applies recorded dice and results. Pinning a build version
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

Postgres, and **[`docs/schema.md`](schema.md) holds the tables**. Accounts, assets,
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

- Both servers run in WSL; Postgres and the local blob store that stands in for S3 run in
  Docker, per the hosting ruling above.
- The Godot client runs on Windows and connects over forwarded localhost.

This is the only configuration, which means it is the one that is always tested. Deploying
changes the address, not the shape.

## Contracts to freeze before parallel work

These are the seams. Work behind them can proceed independently; work across them cannot start
until they are settled.

| Contract | Between |
| --- | --- |
| `Tabletop.Geometry` API surface | client, game server |
| `Tabletop.Protocol` command, event, Ask, presence and setup-edit schema, and the event applier | client, game server; management for `ProtocolVersion` and the game-row enums |
| `Tabletop.Content` army snapshot types | client, both servers |

**Auth is not on this list.** A game has an id, the session JWT says who you are, and the
`participant` row records who is allowed in, so there is no token format of our own to agree and
we issue none ([accounts](superpowers/specs/2026-08-29-accounts-design.md)). There are no
management-to-game-server calls, and therefore no M2M.

**Who instantiates a game session is settled: nobody does.** A client opens a socket naming a
game, and if no session for that game is in memory the server hydrates it from the log.
Starting, reconnecting, and crash recovery are one path.

## Build order

One plan per module, six stages. `docs/design/README.md` → *Build order and why* is the
dependency graph with its reasons and the owner's checkpoints, and every plan's *Requires landed*
line states its own prerequisites.

1. **Contracts and local dev** — the three libraries as types, the event applier, Postgres and
   the blob store in Docker.
2. **Geometry, management API, game server skeleton** — in parallel; none depends on another.
3. **Web app, client, interpreter** — in parallel. The interpreter precedes the scheduler: it
   declares the scheduler's seam records, and the scheduler consumes its Ask machinery, roll
   primitive and sequence starter.
4. **Scheduler** — branched from the interpreter's tree once its Tasks 1–11 are in, and merged after it.
5. **Simulator** — needs both rules modules; their full-game tests then run through it.
6. **Viewer** — needs the simulator's recording schema.

Networking is the only transport, so it is exercised from the skeleton onward rather than bolted
on late. Missions are content in a book; automating them is a later feature, not a module.

The first playable seats two, but nothing in the code says "two": players are a list and teams
are a separate concept, so a free-for-all is N teams of one and differs only in the order over
teams.
