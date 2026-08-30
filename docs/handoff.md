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

**`docs/data-model.md`** — every entity from every session and how they relate, as a mermaid
ER diagram. Conceptual only: no tables, columns, types or indices. It replaced the ASCII sketch
that used to sit in `architecture.md`, and it is a view over the design docs rather than a
source — where they disagree, the design doc wins.

**`docs/architecture.md`** — the shared context every session reads. Mostly settled, but
**not above correction**: the design sessions have now corrected its asset section, its claim
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
versioning or pinning is needed. Assets are a soft reference — the army holds the hitbox,
the mesh is only appearance, so deleting an asset is not blocked. Base shape and height are
per slot, defaulting to the ruleset's values.

This also corrected the table model, which had called an army an immutable snapshot and
said changing one meant copying it. That was an overstatement and is fixed in place.

**The asset library** — two records, Blob and Asset, with dedup per user rather than
global; sharing copies into the recipient's library. Binding is not a third record, just a
field on the model slot. An asset records the base its sculpt was built on, which is a way
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
- **Pinning covers only the data half.** `Properties` is content-addressed and pinned;
  `GameRules` is C# shipped with the build, so a pipeline order changing between deploys can
  make an old game replay differently. Either pin a build version and refuse to replay across
  it, or accept the divergence and say so. `architecture.md` treated pinning as solved.
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
| `ARMY` | `game_system`, `source` | Which ruleset, in what form; what "imported from Army Forge" records |
| `UNIT` | `list_entry` | An Army Forge id, a name, or not needed at all |
| `UNIT` | `abilities`, `upgrades` | Catalog ids with parameters, or resolved values |
| `UNIT`, `MODELSLOT` | `stats` | A named store — but of what. OPR quality is `4+`; 40k has `6"` and `D6` |
| `MODELSLOT` | `loadout` | Weapons carry their own stat blocks |
| ~~`MODELSLOT`~~ | ~~`base`, `height`~~ | Done — geometry values |
| ~~`ASSET`~~ | ~~`kind`, `profile`~~ | Done — identity and enumerations. The texture profile is empty |
| ~~`ASSET`~~ | ~~`scale`, `orientation`~~ | Done — geometry values |
| ~~`TERRAINPLACEMENT`~~ | ~~volumes, declarations~~ | Done — geometry values |
| ~~`MARKERPLACEMENT`~~ | ~~`position`, `facing`, `size`~~ | Done — geometry values |
| ~~`DEPLOYMENTREGION`~~ | ~~`builtin_shape`~~ | Done — geometry values. The default set's contents are still to choose |
| ~~`GAME`~~ | ~~`status`, `scheduler_shape`, `enforcement`~~ | Done — identity and enumerations. Enforcement turned out to be a boolean |
| `ARMYSNAPSHOT` | everything | Has only an id. What a snapshot contains was never said |
| ~~`RULEPACK`~~ | — | Done — gone. A game names a `GameRules` and pins a `Properties` hash; no entity, no registry |
| `GAMEEVENT` | `kind`, `payload` | The event catalog. **Out of scope** — that is `Tabletop.Protocol`, a contract rather than a value type |

### Three sessions, in this order

1. ~~**Geometry values**~~ — **done**, 2026-08-29. Bases are a shape plus dimensions in world
   units; models are yaw-only and upright; collision volumes are extruded prisms with convex
   hulls dropped; six declarations read out of the OPR rulebook; deployment styles are anchored
   polygon sets.
2. ~~**Identity and enumerations**~~ — **done**, 2026-08-29. `RULEPACK` is gone: a ruleset is
   **`GameRules`** (per-ruleset C#) plus **`Properties`** (pinned data). `Properties` holds stat
   definitions and three **disjoint** catalogs — `UnitProperty`, `ModelProperty`,
   `WeaponProperty` — sharing a base, and a name belongs to exactly one kind. Enforcement is a
   boolean. Every enumeration has named values.
3. **Content values** — stats, abilities, upgrades, loadout, `list_entry`, `game_system`, and
   what an `ARMYSNAPSHOT` holds. Feeds `Tabletop.Content`. **This is all that remains.**

   Two things from the last session shape it. **The standing principle is predefined
   properties, not magic strings** — so `stats` as an arbitrary named store is the shape to get
   rid of, and the likely answer is that `Properties` *declares* the stat names, closed within a
   ruleset even though they differ between rulesets. And **the rulebooks are the input**: OPR's
   special rules section is a finite, known vocabulary (`AP(X)`, `Blast(X)`, `Caster(X)`,
   `Impact(X)`, `Tough(X)`, `Stealth`, `Strider`, `Scout`, `Ambush`, …) to design the parameter
   shape against rather than guessing at one.

   One question already surfaced: `architecture.md` lists "unit and weapon definitions" as pack
   data, but armies are imported from Army Forge and hold their own copy of every value. If the
   army is self-contained, a catalog of unit definitions may be redundant — `Properties` may
   only need stat definitions, the three property catalogs, and modifier declarations.

### After that

Tables and columns, turning `docs/data-model.md` into a schema. Then `docs/workstreams/` gets
rewritten against the real model instead of the 08-24 guesses, and the contracts list becomes
worth freezing — `Tabletop.Geometry`, `Tabletop.Protocol`, `Tabletop.Content`, and the join
token.

### The rulebooks

`/mnt/c/Users/clays/Downloads/GF - Advanced Rules v3.4.1.pdf` and
`Age_of_Fantasy_-_Advanced_Rules_v3_5_0.pdf` on the Windows host. **Consult them rather than
guessing** — the geometry session invented a terrain rule that does not exist and repeated it
across three documents before it was caught.

No `pdftotext`, no `poppler-utils`, and headless Chrome cannot launch (missing `libnspr4`).
What works: `pypdf` in a venv for text, and `pypdf` plus Pillow to pull diagrams out as images,
since the zone dimensions live in pictures rather than the text layer.

**Age of Fantasy's terrain section is word-for-word identical to Grimdark Future's**, so it is
not a second example despite being a second game.

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
