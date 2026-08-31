# Handoff — 2026-08-30

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
| 2026-08-29 | Audit of `docs/architecture.md`, and a layout correction |
| 2026-08-30 | [Presence](superpowers/specs/2026-08-30-presence-design.md) — the last gap in the model |
| 2026-08-30 | The join token dropped, then the `session` table with it — auth is Stytch's JWT |
| 2026-08-30 | `Properties` given tables: the pinned data half of a ruleset had no home |
| 2026-08-30 | Containment, spells and actions, all found by reading the rulebook |
| 2026-08-30 | Review of the whole data layer, and the twelve findings it produced |

**Read in this order from cold:** this file, then `docs/architecture.md`, then
`docs/data-model.md` and `docs/schema.md`, then whichever design doc covers what you are
touching.

## The mode — read this before proposing anything

**The data layer is done, and it was reviewed on 08-30.** Ideation ended on 08-29; the day
after was spent expressing the model and then auditing the expression. Nothing in the data
layer is open.

**Modules are planned next, one at a time, and each gets its own prompt.** Do not plan two at
once and do not start writing code because a module looks small.

Four things govern how to work here, and three of them were learned the hard way:

- **Read the rulebook; do not reason about the rules.** Every substantive gap found on 08-30
  came from reading the Grimdark Future PDF — that actions carry the movement value and so
  need a row, that `Transport` and buildings are the same containment rule, that spells are a
  fourth kind of catalog entry. None was found by thinking about the model.
- **Check whether a question is already answered before answering it.** Several "Still open"
  bullets on 08-30 turned out to have been closed by later sessions and never struck out. The
  join token was rediscovered as a blocker when `schema.md` had settled it the same afternoon.
  **A later doc beats an earlier one, including an earlier doc's own open list.**
- **`docs/workstreams/` is stale.** Those prompts predate every design session and describe a
  data model that no longer exists. **None should be run as written.** Workstream 04's auth
  section was corrected on 08-30 because it was actively wrong; the rest was left alone.
- **Nothing in `architecture.md` is above correction.** Written before any data model existed,
  corrected around thirty times, audited statement by statement on 08-29 and corrected again on
  08-30. Treat anything no design doc has touched as a first-pass assertion.

## What is designed

**`docs/schema.md`** — the physical Postgres schema: thirty-two tables, columns and
constraints, with the decisions behind them. Normalised rather than document-based, because
`jsonb` is the magic-string move at the storage layer and would stop the database enforcing
exactly the structure every session insisted on. `jsonb` survives in two columns only —
the event payload, and the frozen army snapshot, which must *not* be reasoned about.

Six of those tables are `Properties`, added on 08-30: the pinned data half of a ruleset, which
`game.properties_hash` had been pointing at with nothing on the other end. Stat declarations,
the property catalog, and each property's effects and their conditions — the shape
[the effect grammar](superpowers/specs/2026-08-29-effect-grammar-design.md) derived from the
Grimdark Future rulebook, given tables and checked back against that book.

**`docs/data-model.md`** — every entity from every session and how they relate, as a mermaid
ER diagram. Conceptual only: no tables, columns, types or indices. It replaced the ASCII sketch
that used to sit in `architecture.md`, and it is a view over the design docs rather than a
source — where they disagree, the design doc wins.

**`docs/architecture.md`** — the shared context every session reads, and **the least examined
document in the project**. Corrected roughly twenty times, always incidentally, then audited
statement by statement on 08-29. Treat any statement in it that no design doc has touched as a
first-pass assertion rather than a decision. It is **not above correction**: the design sessions have now corrected its asset section, its claim
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
are destroyed. Creating a game is a plain endpoint; the client then opens its socket with its
Stytch session JWT and the game id, and the game server hydrates when it arrives, so starting,
reconnecting and crash recovery are one path. Ending is a flag, not a terminal state, because
restarting an ended game is the same replay.

That last point removed a would-be lifecycle state. The session also invented a **join token**
for the client-to-game-server handoff, which was **dropped on 08-30** — a game has an id, the
JWT says who you are, and the `participant` row records who is allowed in. The token
restated all three and added an expiry that could lapse mid-game, stranding a player who
dropped at hour three. It is gone from the schema, the API and the contracts list.

**The table and its setup content** — the board, what stands on it before play, and what a
saved arrangement is. The table is a rectangle: width, length, texture, no shape and no boundary
volume. Deployment zones are a built-in shape plus a team. **The setup phase is not logged** —
only its result is, and that record *is* the event log's initial state, which makes terrain and
objectives locked after setup with no flag to check. A Layout is that same record owned by a user
rather than a game, so there is one shape rather than two. Objectives and tokens collapse into one
marker with a `kind`. Terrain volumes and declarations live on the asset and may be overridden per
placement, with a closed set the engine reads and free text for everything else. That set is
**six**: impassable, blocks LoS, see in and out, cover, difficult, dangerous. This session named
four and geometry values added two more from the rulebook.

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

**Presence** — what players see of each other between events, and the last gap. Nothing in it
is persisted or logged: a presence value lives in memory while a socket is open, so `schema.md`
is untouched and a reconnecting client rebuilds it from nothing. **Connectedness is not a
field** — a value exists while a participant is connected, so who is connected is which values
exist, with the persisted `PARTICIPANT` row as the roster underneath. One value per participant
carries cursor (a world point plus the piece under it), selection, placements, and a
measurement; a ping is a separate one-shot because presence is state and a ping is an instant.
Drag and ghost collapse into one placement with a `kind`, held or proposed, since they differ
only in what the viewer draws. There is no spectator identity: someone who wants to watch joins
a third team and does not act.

This overrode `architecture.md`'s claim that pre-measuring, range checks and movement ghosts
are "never sent anywhere". They are relayed and still never commands — **relayed and logged are
different things**, and that document predates the ephemeral channel that separates them.

**The ruleset's data half, and the rulebook it came from.** `Properties` had been pinned by a
content hash with nothing on the other end; it now has seven tables. Stat declarations, one
catalog of named rules whose kinds are `unit`, `model`, `weapon` and `spell`, each entry's
effects, and each effect's conditions. A spell is not a property — it attaches to nothing and
carries a token cost — which is why the catalog is not called `property_definition`.

Actions are a row of a name and a distance, because **Grimdark Future has no movement stat**:
movement comes from the action taken, and `Fast` and `Slow` modify it, so an effect has to be
able to point at it. That gives the line between an action's data and its behaviour a rule
rather than a judgement call — **what an effect can target must be data**, and what only the
engine reads (may it shoot, must it reach base contact) stays in `GameRules`.

**Containment.** A model is in one of three places: a table placement, the shelf, or **inside a
container**. `Transport(X)` holds models inside a model and a solid building holds them inside
terrain, and the two rules are near enough the same text to be one concept. It is a location
rather than a condition, because it names *what a model is inside* and decides where it is.
Nothing about it persists.

**Auth is Stytch's session JWT and nothing of ours.** Two token tables were designed and both
dropped: the join token, because a game has an id and `participant` records who is allowed in;
then `session`, because the JWT carries our account UUID in `external_id` and both services
verify it locally against Stytch's public JWKS. No call on the request path, no shared signing
key, no row to look up. Revocation is a matter of minutes rather than instant, which is
accepted.

Two things govern the rest: **this is a tabletop simulator**, so nothing recorded is an
invariant and every property is editable from a menu through the same write path
automation uses; and **players count their own points**, so there are no victory
conditions.

## What is not designed

**Nothing in the data layer.** Presence was the last known gap, it closed on 08-30, and a
review that day found and fixed twelve more problems without finding a thirteenth. Every noun
has a home and every deliverable has a job.

Missions turned out not to be a module — they are content in a book, and automating them is a
later feature rather than a missing noun.

**Everything below the data layer is unplanned**, and that is the work. In `architecture.md`'s
build order: the action sequences (shooting and melee, step by step out of the rulebook), the
scheduler, the setup phase, the client shell, the mesh prep tool, and deployment. `GameRules`
is C# and stays C# until a second ruleset exists.

**Two things from the rulebook were deliberately left out**, both recorded where they belong:

- **Buildings as targets.** A solid building has `Defense 2+` and a player-set `Tough(X)` and
  can be destroyed. That needs terrain to carry stats and to become event-driven state on top
  of the setup snapshot — additive, per setup content, and not done.
- **Action boosting.** The optional rules are written as percentages — boosted `Rush` moves
  *"+50%"* — and `amount` is an integer. None of the twelve effect kinds expresses a
  proportion.

## Open questions

Carried from the design docs:

- Whether setup configuration can change once a game is loaded. Pinning answers it for
  `Properties`, and the setup snapshot answers it for board size and texture; the scheduler
  shape and the teams are undecided.
- Conflicts between two abilities reading the same result — whether both apply or one
  wins. Deferred deliberately: every example is specific to one ruleset.
- Whether a paused action can be abandoned, and what happens to a half-applied result. **Not
  answerable yet**: `architecture.md` names resolution "pipelines" but no step list for any
  action has ever been written, and the resolution session excluded them deliberately. This
  waits on OPR's actual shooting and melee sequences.
- The triangle-count threshold at upload. Agreed in principle, never chosen.
- Whether an objective ever needs game data again. Setup content deleted the field because
  scoring is manual; reopening it needs a reader.
- Whether terrain can be unlocked mid-game. Not a permission change — it is the point at which
  terrain becomes event-driven state on top of the setup snapshot.
- Whether the closed terrain declaration set is complete.
- What happens when a saved layout's board size does not match the table it is loaded onto.
- Whether the client ever needs to edit an army rather than only pick one.
- Loopback or device code for getting a browser session into the client.
- Whether a thirteenth effect kind, or a flavour on kind 1, carries proportions. Only the
  optional boosting rules need it.
- Whether `GameRules` ever becomes data. Not before a second ruleset exists, per
  `architecture.md`; Warhammer is what would force it.

**Do not treat this list as live without checking.** Several entries were closed by later
sessions and left standing until 08-30. Each design doc's own *Still open* section is the
authority, and a later doc beats an earlier one.

## Next — pick up here

**Plan modules, one prompt each.** The data layer is done and reviewed; what is not designed is
everything the modules do with it. This was agreed explicitly on 08-30: rules execution is
separate planning, and no code gets written because a module looks small.

### The order that is already argued for

`architecture.md` holds the build order and it still stands. What each step needs before it can
be planned:

1. **Contracts** — `Tabletop.Geometry`, `Tabletop.Protocol`, `Tabletop.Content`. Three, not
   four: the join token was dropped on 08-30. Nothing is waiting on design. The one thing to
   settle while writing them is whether `Tabletop.Protocol` may reference the other two, which
   decides whether a command carries a `Placement` or its own copy of position and yaw.
2. **Geometry** — volumes, distance, line of sight, settling. Fully specified by
   [geometry values](superpowers/specs/2026-08-29-geometry-values-design.md). Headless, no
   dependencies, and the first place a test project is worth having.
3. **Management API and web app** — fully specified by
   [the management API](superpowers/specs/2026-08-29-management-api-design.md) and
   [the web app](superpowers/specs/2026-08-29-web-app-design.md).

### The ones that need design before they can be built

- **The action sequences.** Shooting and melee, step by step, out of the rulebook. This is what
  `architecture.md` calls a "pipeline" and it has never been written down — the resolution
  session excluded step lists deliberately. It blocks the abandonment question, the effect
  grammar's `order` field, and the whole resolution machinery.
- **The scheduler.** [Game flow](superpowers/specs/2026-08-28-game-flow-design.md) designed the
  action record and teams and left *how the scheduler decides what is legal in a window*
  undecided. Both shapes get built.
- **The setup phase.** Its output record is designed; who places terrain and when is not.
- **The client shell**, the **mesh prep tool** — `architecture.md:20` says outright it is
  undesigned — and **deployment**.

### Rewrite `docs/workstreams/`

Seven prompts from 08-24 describing a model that no longer exists. Workstream 04's auth section
was corrected on 08-30 because it was actively wrong; nothing else was touched. They are the
last artefacts in the repo never brought forward, and they are worth rewriting **after** the
contracts exist, so the prompts can name real types rather than describe them.

## Two things worth carrying into any session

**Consult the rulebooks; do not reason about the rules.** They are on the Windows host at
`/mnt/c/Users/clays/Downloads/GF - Advanced Rules v3.4.1.pdf` and
`Age_of_Fantasy_-_Advanced_Rules_v3_5_0.pdf`. Reading them produced better answers than
designing did, six times running — the terrain declaration set, the effect grammar's twelve
kinds, the discovery that OPR's statline is two stats with no movement value, and then on 08-30
that actions must be rows, that `Transport` and buildings are one containment rule, and that
spells are a fourth catalog kind. Guessing produced "blocks sight past three inches", a rule in
no ruleset, which then propagated through three documents as fact.

No `pdftotext` and no `poppler-utils`; headless Chrome cannot launch (missing `libnspr4`). What
works: `pypdf` in a venv for text, and `pypdf` with Pillow to extract diagrams as images, since
the deployment zone dimensions live in pictures rather than the text layer.

```bash
python3 -m venv venv && ./venv/bin/pip install pypdf
./venv/bin/python -c "
from pypdf import PdfReader
r = PdfReader('/mnt/c/Users/clays/Downloads/GF - Advanced Rules v3.4.1.pdf')
print(r.pages[12].extract_text())"        # 68 pages; special rules are 13-14
```

Useful page numbers in Grimdark Future v3.4.1: **7** actions and their distances, **8-10**
shooting, charging and melee, **11-12** terrain, **13-14** every special rule, **16** placement
styles, **20** buildings, **21-23** deployment styles, **24-28** missions, **29-30** extra
actions and boosting.

Army books, which hold the per-faction spell lists, are at
<https://army-forge.onepagerules.com/army-books/grimdark-future>.

**Age of Fantasy is not a second ruleset for testing generalisation.** Its terrain section is
word for word identical to Grimdark Future's — same engine, same text. Warhammer is the real
test, and several decisions are explicitly waiting on it: whether the six terrain declarations
are complete, whether twelve effect kinds survive, and when `GameRules` should move from C# to
data.

## Where to look at the model

Both diagrams render at <https://claude.ai/code/artifact/f5d2d64c-d819-4553-b03f-e23271ac0f63>,
zoomable, generated from `docs/schema.md` and `docs/data-model.md`. **It is a copy, not a
source** — it goes stale the moment either document changes, and it is regenerated by pulling
the mermaid block out of each and republishing to the same URL.

## Environment notes

Stytch MCP is authenticated and works. Project `TabletopWar`, vertical `CONSUMER`, test
project `project-test-437432c7-798d-49d2-9dc4-eff103937e0f`. There is no production
environment — both redirect URLs are in the test project. The redirect config was edited
during the 08-24 session, so re-read it rather than trusting older quoted values.

The available MCP tools are project-configuration only — redirect URLs, SDK config,
tokens, email templates. There are no user-management tools, so anything about the
Consumer user schema has to come from the docs.
