# Handoff — 2026-08-29

Where the project stands, and what to pick up next.

## State

Tree is clean; everything is committed. No code has been written beyond the Godot
scaffold — all of the work so far is design.

| When | Produced |
| --- | --- |
| 2026-08-24 | Repo restructure, `docs/architecture.md`, seven workstream prompt sketches |
| 2026-08-27 | [Table domain model](superpowers/specs/2026-08-27-table-domain-model-design.md) |
| 2026-08-28 | [Game flow](superpowers/specs/2026-08-28-game-flow-design.md) |
| 2026-08-28 | [Resolution](superpowers/specs/2026-08-28-resolution-design.md) |
| 2026-08-28 | [The army](superpowers/specs/2026-08-28-army-design.md) |
| 2026-08-28 | [The asset library](superpowers/specs/2026-08-28-asset-library-design.md) |
| 2026-08-29 | [Accounts, and everything outside the game](superpowers/specs/2026-08-29-accounts-design.md) |
| 2026-08-29 | [The table and its setup content](superpowers/specs/2026-08-29-setup-content-design.md) |
| 2026-08-29 | [The web app, and the line between preparation and play](superpowers/specs/2026-08-29-web-app-design.md) |
| 2026-08-29 | `docs/data-model.md` — the entity diagram |
| 2026-08-29 | [Geometry values](superpowers/specs/2026-08-29-geometry-values-design.md) — first of the three definition sessions |
| 2026-08-29 | [Identity and enumerations](superpowers/specs/2026-08-29-identity-and-enumerations-design.md) — second |
| 2026-08-29 | [Content values](superpowers/specs/2026-08-29-content-values-design.md) — third |
| 2026-08-29 | [The effect grammar](superpowers/specs/2026-08-29-effect-grammar-design.md) |
| 2026-08-29 | `docs/schema.md` — the physical Postgres schema |
| 2026-08-29 | [The management API](superpowers/specs/2026-08-29-management-api-design.md) |

## The mode — read this before proposing anything

**We are still ideating. Do not write buildable specs and do not freeze contracts.**

The 08-24 session produced workstream prompts and a "contracts to freeze before parallel
work" list. Those were premature and are on hold. Freezing an interface is only worth
doing when the model behind it has stopped moving, and it has not.

The mode every design session so far has kept, and the one to continue in: **decide meaning,
defer expression.** The design docs deliberately refuse to say how anything is expressed
in C#, how it persists, or what the wire format is. That is not an omission to fix later
by adding detail — it is what makes them cheap to change.

**The workstream prompts in `docs/workstreams/` are sketches. None should be run as
written.** They predate every design session below and describe a data model that no longer
matches. `01-contracts` in particular was flagged as the weakest and most blocking, and
the correct response to that is not to strengthen it yet.

## What is designed

**`docs/schema.md`** — the physical Postgres schema: twenty-four tables, columns and
constraints, with the decisions behind them. Normalised rather than document-based, because
`jsonb` is the magic-string move at the storage layer and would stop the database enforcing
exactly the structure every session insisted on. `jsonb` survives in two columns only —
the event payload, and the frozen army snapshot, which must *not* be reasoned about.

**`docs/data-model.md`** — every entity from every session and how they relate, as a mermaid
ER diagram. Conceptual only: no tables, columns, types or indices. It replaced the ASCII sketch
that used to sit in `architecture.md`, and it is a view over the design docs rather than a
source — where they disagree, the design doc wins.

**`docs/architecture.md`** — the shared context every session reads, and **the least examined
document in the project**. Corrected roughly twenty times, always incidentally. An audit is the
next task. Treat any statement in it that no design doc has touched as a first-pass assertion
rather than a decision. It is **not above correction**: the design sessions have now corrected its asset section, its claim
that armies are immutable, its login story, its `Binding` row, its collections module, and its
claim that auth needed no agreed contract. Where it disagrees with a design doc, the design doc
is newer.

It records: Thin
client with rules only on the server, two servers split on CRUD vs live sessions, no
offline mode, AWS, command/event spine, undo by replay, enumeration rather than
validation, rules as data with a closed schema and open catalog, rule packs pinned per
game.

**Table domain model** — the nouns. Armies as immutable user-owned snapshots bundling
list and mesh assignments; placement per model, never per unit; distance and line of
sight against base-and-height cylinders, never the visual mesh; terrain declaring named
effects the game system interprets; stats as a named store rather than typed fields. Plus
the one wire decision the table forced: live drag positions broadcast but never logged,
the log carrying debounced start and stop positions so replay lands on a settled state.

**Game flow** — the verbs. Teams as the only thing the scheduler sees, with armies
attached to players but activation authority team-wide. A unit records named actions
separately from conditions, because the two have different lifetimes; activation is
therefore a ruleset's reading of the action record, not a data concept. Both scheduler
shapes get built — player-turn and alternating activation. The graveyard generalises to a
shelf holding undeployed models, casualties, and reserves alike. Setup configuration
(pack, scheduler, table, teams, enforcement dial) is settled before the game loads.

**Resolution** — what happens inside an action. Two orthogonal settings: whether the
client animates the steps, and whether the pipeline waits between them. Waiting allows
interrupts, which makes a half-resolved pipeline durable state in the log rather than a
function call. Modifiers and abilities split on *when*, not what: a modifier is numeric
plus or minus applied before the roll, an ability reads the result and changes its
meaning. Restricting modifiers to arithmetic makes them commute, which removes the
explicit priority scheme `architecture.md` called for. Players get reroll and hand-added
modifiers, and those are the same operations abilities use.

**The army** — the first module touching no game. The load-bearing property is
self-containment, not immutability: an army holds its own copy of every value, so Army
Forge, the source list, and the asset library cannot reach into it. Users edit their own
armies freely; a game is safe because it took what it needed when it loaded, so no
versioning or pinning is needed. **Armies are not long-lived objects to maintain** — a changed
list means a re-import, which loses per-slot bindings and is accepted, so there is no copy
operation and no reconciliation. Assets are a soft reference — the army holds the hitbox,
the mesh is only appearance, so deleting an asset is not blocked. Base shape and height are
per slot, defaulting to the ruleset's values.

This also corrected the table model, which had called an army an immutable snapshot and
said changing one meant copying it. That was an overstatement and is fixed in place.

**The asset library** — two records, Blob and Asset, with dedup per user rather than
global; sharing copies into the recipient's library. Binding is not a third record, just a
field on the model slot. An asset records the base its mesh was made for, which is a way
to find meshes that fit a slot, while the slot's own base is the hitbox. Folders nest and
an asset can be in several.

**Accounts** — and with them everything outside the live game. An account is two fields, id
and name; Stytch answers only "is this session valid, and whose," runs *through* management
rather than from the client, and is replaceable without a client release. No graph — no friends,
no handles, no sharing — so two accounts meet in a game and nowhere else, found through a public
game browser in the client. Deletion is total: an account's content and every game it played in
are destroyed. Creating a game is an endpoint returning a join token; the game server hydrates
when a client arrives, so starting, reconnecting and crash recovery are one path. Ending is a
flag, not a terminal state, because restarting an ended game is the same replay.

That last point removed a would-be lifecycle state, and the join token added a contract that
`architecture.md` had explicitly said would not exist.

**The table and its setup content** — the board, what stands on it before play, and what a
saved arrangement is. The table is a rectangle: width, length, texture, no shape and no boundary
volume. Deployment zones are a built-in shape plus a team. **The setup phase is not logged** —
only its result is, and that record *is* the event log's initial state, which makes terrain and
objectives locked after setup with no flag to check. A Layout is that same record owned by a user
rather than a game, so there is one shape rather than two. Objectives and tokens collapse into one
marker with a `kind`. Terrain volumes and declarations live on the asset and may be overridden per
placement, with a closed set the engine reads — blocks LoS, cover, difficult, dangerous — and free
text for everything else.

There is **no `Mission` record.** `architecture.md` had defined one as deployment zones,
objectives, victory conditions, round count and team count, which is not what a mission is:
missions are published content in OPR and Warhammer and players read them out of a book.

It also corrected the asset library and accounts: **a library is private for discovery, not for
access.** Game participants download the assets that game uses; nothing is copied and no record is
created, so the "sharing copies into the recipient's library" rule is gone.

**The web app** — it exists, and the line is **preparation versus play**. The browser owns army
import and editing, mesh binding, base shapes, asset upload and the library, the account, and
login. The client owns creating a game, picking an army, the game browser, joining, the setup
phase and play. The client does not edit armies.

That reverses the previous session on login: the browser owns it and the client acquires a
session, which restores `architecture.md`'s original position. Confinement is unaffected —
Stytch's OTP and password endpoints require the project secret and cannot be called from a
browser, so they run in management either way, while OAuth and magic links need a page to
redirect back to, which is what the web app supplies. Getting the session into the client is
loopback or a device code, both deferred.

An embedded webview serving one shared UI was considered and rejected: it does not remove a UI
stack, it adds a bridge to one — a browser engine bundled into the client plus a second renderer
inside it for mesh preview.

Two things govern the rest: **this is a tabletop simulator**, so nothing recorded is an
invariant and every property is editable from a menu through the same write path
automation uses; and **players count their own points**, so there are no victory
conditions.

## What is not designed

Nothing, at this altitude. Every noun has a home and every deliverable has a job.

Missions turned out not to be a module — they are content in a book, and automating them is a
later feature rather than a missing noun.

## Open questions

Carried from the design docs:

- Whether setup configuration can change once a game is loaded. Pinning answers it for
  `Properties`, and the setup snapshot answers it for board size and texture; the scheduler
  shape and the teams are undecided.
- What state a model returns in when a mechanic brings it back off the shelf. A rules
  question, but the model has to support the answer.
- Conflicts between two abilities reading the same result — whether both apply or one
  wins. Deferred deliberately: every example is specific to one ruleset.
- What a roll records beyond its results, and whether a waiting pipeline can be abandoned.
- What a slot draws when its asset is missing. `architecture.md` says a proxy from the
  collision volume, but a miniature's would have to come from base and height instead.
- The triangle-count threshold at upload. Agreed in principle, never chosen.
- What is in a join token and how long it lives. It is now a contract between the two servers.
- Whether a game already playing can be joined, or only one still in its lobby.
- Whether terrain can be unlocked mid-game. Not a permission change — it is the point at which
  terrain becomes event-driven state on top of the setup snapshot.
- Whether the closed terrain declaration set is complete.
- What happens when a saved layout's board size does not match the table it is loaded onto.
- Whether the client ever needs to edit an army rather than only pick one.
- Loopback or device code for getting a browser session into the client.

## Next — pick up here

Eight modules are designed: the table, game flow, resolution, the army, the asset library,
accounts, the setup content, and the web app. Every noun has a home and every deliverable has
a job, and `docs/data-model.md` draws all of it.

**The current phase is putting real definition on the placeholder types in that diagram.**
Its attribute types were written to keep it honest about what exists, not to be precise —
`stats stats`, `list abilities`, `ref list_entry`, `profile profile`. Those are the fields that
would be nebulous at execution time, and they are the work now. This comes *before* tables and
columns, and well before designing consumers, so gaps surface while they are still cheap.

### What is underdefined

| Entity | Field | What is undefined |
| --- | --- | --- |
| ~~`ARMY`~~ | ~~`game_system`, `source`~~ | Done — content values. `game_system` is the `GameRules` id |
| ~~`UNIT`~~ | ~~`list_entry`~~ | Done — **dropped**. Nothing reads it, and its only candidate reader was deferred work |
| ~~`UNIT`~~ | ~~`abilities`, `upgrades`~~ | Done — `upgrades` dropped entirely; properties are entries with parameters |
| ~~`UNIT`, `MODELSLOT`~~ | ~~`stats`~~ | Done — typed and declared at three levels |
| ~~`MODELSLOT`~~ | ~~`loadout`~~ | Done — a weapon on the slot, with its own stats and properties |
| ~~`MODELSLOT`~~ | ~~`base`, `height`~~ | Done — geometry values |
| ~~`ASSET`~~ | ~~`kind`, `profile`~~ | Done — identity and enumerations. The texture profile is empty |
| ~~`ASSET`~~ | ~~`scale`, `orientation`~~ | Done — geometry values |
| ~~`TERRAINPLACEMENT`~~ | ~~volumes, declarations~~ | Done — geometry values |
| ~~`MARKERPLACEMENT`~~ | ~~`position`, `facing`, `size`~~ | Done — geometry values |
| ~~`DEPLOYMENTREGION`~~ | ~~`builtin_shape`~~ | Done — geometry values. The default set's contents are still to choose |
| ~~`GAME`~~ | ~~`status`, `scheduler_shape`, `enforcement`~~ | Done — identity and enumerations. Enforcement turned out to be a boolean |
| ~~`ARMYSNAPSHOT`~~ | ~~everything~~ | Done — a deep copy, no edge back, no partial fields |
| ~~`RULEPACK`~~ | — | Done — gone. A game names a `GameRules` and pins a `Properties` hash; no entity, no registry |
| `GAMEEVENT` | `kind`, `payload` | The event catalog. **Out of scope** — that is `Tabletop.Protocol`, a contract rather than a value type |

### The three definition sessions — all done

1. **Geometry values**, 2026-08-29. Bases are a shape plus dimensions in world units; models
   are yaw-only and upright; collision volumes are extruded prisms with convex hulls dropped;
   six declarations read out of the OPR rulebook; deployment styles are anchored polygon sets.
2. **Identity and enumerations**, 2026-08-29. `RULEPACK` is gone: a ruleset is **`GameRules`**
   (per-ruleset C#) plus **`Properties`** (pinned data). `Properties` holds stat definitions
   and three **disjoint** property sets — `UnitProperty`, `ModelProperty`, `WeaponProperty` —
   and a name belongs to exactly one. Enforcement is a boolean. Every enumeration has named
   values.
3. **Content values**, 2026-08-29. Stats are **typed** — target number, distance, count, dice
   expression — and declared at unit, model and weapon level. A weapon lives on the model slot,
   resolved in place, with no shared definition. `upgrades` is dropped: an upgrade's effect is
   already applied by import time. `list_entry` is dropped — nothing reads it, and the flow that
   might have is gone too. `ARMYSNAPSHOT` is a deep copy. A property entry is name, kind, at most one integer parameter, and **an effect carried
   as data**.

### The effect grammar — done, 2026-08-29

An entry is a name, kind, at most one integer parameter, an aggregation, and a list of effects
that **may be empty**. An effect is a subject (`self` or `opponent`), a **flat conjunction** of
predicates from eight closed kinds, an ordering field, and one of **twelve effect kinds**.
`Roll` nests an effect per success.

Three findings worth carrying:

- **Four of the twelve act on other properties** — grant, suppress, modify a parameter,
  suppress modifiers. So resolving an interaction is **two passes**: settle which properties
  and parameters are live, then apply.
- **Explicit ordering comes back.** The resolution session removed it on the grounds that
  arithmetic modifiers commute; that holds for one kind of twelve, and the rulebook orders
  `Deadly` and `Blast` by hand. Corrected in place there.
- **`GameRules` handles a set of properties by name**, grouped by where the check happens:
  deployment (`Ambush`, `Scout`), movement (`Flying`, `Strider`), action legality (`Immobile`),
  usage (`Limited`), melee strike order (`Counter`), interrupt (`Caster`). A property may be in
  both places — `Aircraft` has data effects *and* a hook.

### Done: tables and columns

`docs/schema.md`. Twenty-four tables. Three things it surfaced that the conceptual model had
not:

- **Stats are rows, not columns**, forced by stats being declared per ruleset. A key-value
  child table at each of the three levels. The one unavoidable smell, and it follows from a
  decision that is right.
- **A deployment zone names a `team_ordinal`, not a team.** A saved layout has zones but no
  game, so no team rows exist to point at.
- **Deleting an account cannot be a foreign key.** Deleting every game it played in cascades
  *upward* — participant to team to game — and then into the other schema's event log. It is a
  deliberate multi-step operation in the delete path.

### Done: the management API

`docs/superpowers/specs/2026-08-29-management-api-design.md`. The full endpoint surface, and
the two mechanics earlier sessions had deferred.

- **Asset bytes never pass through the API.** Presigned S3 URLs both ways, because every
  participant pulls every mesh in a game. The triangle-count check survives anyway: **a GLB's
  first chunk is its JSON**, so a ranged read on `confirm` gives an exact count without the
  binary. It warns rather than blocks, like everything else.
- **GLB only**, narrowed from "glTF/GLB". External-buffer `.gltf` is several files with
  relative references, which breaks single-object upload and the header read. Nothing is lost;
  it is Blender's default export.
- **Two opaque token tables**, `session` and `join_token`, added to the schema. Not JWTs:
  instant revocation, which account deletion requires, and no key shared between services.
- **Keyset pagination** — UUIDv7 is time-ordered, so the id is the cursor.
- **The game server reads the game's row itself** after looking up the join token, which closes
  the schema's open question. A read is not a join, so the boundary holds — but the two
  services now share a database.

### Done: the `architecture.md` audit

2026-08-29. First pass statement by statement rather than incidentally. Fixed what was wrong —
"no tick loop", "no CRUD surface" and its two non-following consequences, `ArmySnapshot` as an
entity, "terrain multipliers", the dead "art profile"/"art binding" terms, "mesh processing
stays server-side", deployment zones as a shape plus a team, a proxy drawn from a collision
volume a miniature does not have, and the claim that end users authoring rules is not the goal.
Repaired three run-on paragraphs and a dangling antecedent left by this session's own edits.

Deleted six first-pass assertions that no session had ever touched and none of which was a
decision: undo replaying from "a checkpoint", a dev-mode pack file watcher, symmetry mirroring
as "the feature that makes fair multi-player tables practical", enemies resolving through "a
per-pair relationship query", abilities being able to "replace the step", and the two
justifications for per-player filtered event streams. The filtering mechanism is kept; secret
objectives do not exist because there are no victory conditions, and hidden deployment is a
`GameRules` hook.

### Superseded: audit `architecture.md`

**`architecture.md` is the most-cited document in the project and the least examined.** Every
session reads it and quotes it. Fourteen sessions have corrected it roughly twenty times, but
always *incidentally* — because a session happened to collide with a claim. Nothing has ever
gone through it statement by statement asking whether each is still true and whether its
stated reason holds.

The evidence that this is overdue is the list of things found by accident: the mission
definition, the table as a boundary volume, convex hulls, global blob dedup, the `Binding` row,
the browser owning login, the ruleset registry, ordering removed and then restored, pinning
treated as solved when it covers only half.

And a worked example of the failure mode, traced this session. `architecture.md` says:

> **The game server has no CRUD surface.** It reads game metadata once at session hydration and
> writes only events. That is what keeps management restartable at will and confines deploy risk
> to one service.

It comes from `6bbc66e`, the 08-27 first planning session, whose own commit message says the
notes are "early and not settled" and that "the data models have not been worked through."
**Neither consequence follows from the claim.** Management is restartable because it holds no
live state; deploy risk is confined to the game server because it holds live WebSocket
sessions. Strip the justification and what remains is "the game server's interface is commands
and events" — true, stated elsewhere, and not a principle. It was nonetheless treated as a
constraint for fourteen sessions.

**The audit is a pass over every statement**, asking: is it still true after fourteen sessions;
does its stated reason actually support it; and was it a decision or a first-pass assertion
that nobody has revisited.

### After the audit

**Presence.** Cursors, pings and who is connected. The first thing the design sessions **missed
rather than deferred** — `architecture.md` says the workload has "no tick loop", which is true
of simulation and false of the relay a virtual tabletop needs. Players point at things. The
table domain model already broadcasts live drag positions and never logs them; presence is the
same channel carrying cursors and pings, and nothing about it persists.

Then `docs/workstreams/`, which still describes the 08-24 model and is stale in every
particular, and the contracts list becomes worth freezing — `Tabletop.Geometry`,
`Tabletop.Protocol`, `Tabletop.Content`, and the join token.

### Is the model still moving?

Worth re-asking before freezing anything. It survived eight sessions, and the last four each
corrected the ones before — including the web app session, which reversed the session
immediately preceding it on login. The nouns stopped moving after the army; what kept moving
was *where things live*.

One consequence worth carrying forward: `architecture.md` holds pipeline step order,
scheduler policy, and dice conventions in C# until a second ruleset exists to design
against. Building both schedulers puts that second example at the scheduler seam
immediately, so of the three, that is the first where "should this move to data" becomes
answerable.

## Environment notes

Stytch MCP is authenticated and works. Project `TabletopWar`, vertical `CONSUMER`, test
project `project-test-437432c7-798d-49d2-9dc4-eff103937e0f`. There is no production
environment — both redirect URLs are in the test project. The redirect config was edited
during the 08-24 session, so re-read it rather than trusting older quoted values.

The available MCP tools are project-configuration only — redirect URLs, SDK config,
tokens, email templates. There are no user-management tools, so anything about the
Consumer user schema has to come from the docs.
