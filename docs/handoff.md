# Handoff — 2026-09-04

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
| 2026-08-30 | [Presence](superpowers/specs/2026-08-30-presence-design.md) |
| 2026-08-30 | The join token dropped, then the `session` table with it — auth is Stytch's JWT |
| 2026-08-30 | `Properties` given tables: the pinned data half of a ruleset had no home |
| 2026-08-30 | Containment, spells and actions, all found by reading the rulebook |
| 2026-08-30 | Review of the whole data layer, and the twelve findings it produced |
| 2026-09-03 | [Action sequences](superpowers/specs/2026-09-03-action-sequences-design.md) — the interpreter and OPR's sequences, 40k read as the falsifier |
| 2026-09-03 | Rolls given a row; `schema.md`, `data-model.md` and `architecture.md` corrected to match |
| 2026-09-04 | [The scheduler](superpowers/specs/2026-09-04-scheduler-design.md) — both shapes, the turn as an Ask, deployment and the round boundary; three earlier open items closed |
| 2026-09-04 | [The setup phase](superpowers/specs/2026-09-04-setup-phase-design.md) — free-for-all editing, the draft is the row, terrain generators and built-in terrain, the creator's two transitions; joining at any status restored |

**Read in this order from cold:** this file, then `docs/architecture.md`, then
`docs/data-model.md` and `docs/schema.md`, then whichever design doc covers what you are
touching.

## The mode — read this before proposing anything

**The data layer is done, and it was reviewed on 08-30.** Nothing in the data layer is open.

**Design sessions continue, one module per prompt, until every module is designed. Then the
whole design gets a review. Only after that review does anything move to implementation
planning.** Ruled explicitly on 09-03. Do not write an implementation plan, do not scaffold,
and do not start code because a module looks small or "isn't waiting on design" — the contracts
included. Do not design two modules in one prompt.

Four things govern how to work here, and three of them were learned the hard way:

- **Read the rulebook; do not reason about the rules.** Every substantive gap found on 08-30
  came from reading the Grimdark Future PDF, and 09-03 repeated the result: the mandatory-input
  finding, the one-attack-sequence finding, and the rolls-as-rows correction all came from the
  page, not the model. Guessing once produced "blocks sight past three inches", a rule in no
  ruleset, which propagated through three documents as fact.
- **Check whether a question is already answered before answering it.** Several "Still open"
  bullets on 08-30 turned out to have been closed by later sessions and never struck out.
  **A later doc beats an earlier one, including an earlier doc's own open list.** When a
  session closes another doc's open item, strike it there and then — 09-03 did.
- **`docs/workstreams/` is stale.** Those prompts predate every design session and describe a
  data model that no longer exists. **None should be run as written.** Workstream 04's auth
  section was corrected on 08-30 because it was actively wrong; the rest was left alone.
- **Nothing in `architecture.md` is above correction.** Written before any data model existed,
  corrected around thirty times, audited statement by statement on 08-29 and corrected again on
  08-30 and 09-03 (its resolution sketch). Treat anything no design doc has touched as a
  first-pass assertion.

## What is designed

**`docs/schema.md`** — the physical Postgres schema: thirty-three tables, columns and
constraints, with the decisions behind them. Normalised rather than document-based, because
`jsonb` is the magic-string move at the storage layer and would stop the database enforcing
exactly the structure every session insisted on. `jsonb` survives in two columns only —
the event payload, and the frozen army snapshot, which must *not* be reasoned about.

Eight of those tables are `Properties`: the pinned data half of a ruleset, which
`game.properties_hash` had been pointing at with nothing on the other end. Stat declarations,
actions, rolls, the catalog of named rules, and each entry's effects and their conditions — the
shape [the effect grammar](superpowers/specs/2026-08-29-effect-grammar-design.md) derived from
the Grimdark Future rulebook, given tables and checked back against that book.

**`docs/data-model.md`** — every entity from every session and how they relate, as a mermaid
ER diagram. Conceptual only: no tables, columns, types or indices. It replaced the ASCII sketch
that used to sit in `architecture.md`, and it is a view over the design docs rather than a
source — where they disagree, the design doc wins.

**`docs/architecture.md`** — the shared context every session reads, and **the least examined
document in the project**. Corrected roughly twenty times, always incidentally, then audited
statement by statement on 08-29. Treat any statement in it that no design doc has touched as a
first-pass assertion rather than a decision. It is **not above correction**: the design
sessions have now corrected its asset section, its claim that armies are immutable, its login
story, its `Binding` row, its collections module, its claim that auth needed no agreed
contract, and its resolution sketch. Where it disagrees with a design doc, the design doc is
newer.

It records: Thin client with rules only on the server, two servers split on CRUD vs live
sessions, no offline mode, AWS, command/event spine, undo by replay, enumeration rather than
validation, rules as data with a closed schema and open catalog, rule packs pinned per game.

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

**Resolution** — the shape of what happens inside an action. Two orthogonal settings: whether
the client animates the steps, and whether the pipeline waits between them; a half-resolved
pipeline is durable state in the log rather than a function call. Players get reroll and
hand-added modifiers, and those are the same operations abilities use. Two of its conclusions
were later corrected by reading real rulesets: commutativity holds for one of twelve effect
kinds (effect grammar, 08-29), and waiting is not only a configuration — some input is
mandatory (action sequences, 09-03).

**Action sequences** — the machinery resolution left open, and OPR's sequences out of the
rulebook. The interpreter is a **resolution stack**: frames rebuilt from the log like the
action record, never stored; a step completes, **asks** a team for input against a
server-computed legal set, **pushes** a nested sequence, or **opens a window** — an Ask about
interrupting, offered to other teams or invoked by the actor (casting can checkpoint a move
mid-path). One sealed-simultaneous window variant exists for casting's token bid. One roll
primitive: the engine owns the seeded RNG, per-die raw and modified results, and the effect
grammar's two passes in `ordinal` order; `GameRules` supplies each roll's dice convention,
threshold derivation and modifier cap. Grimdark Future's shooting and melee are **one
four-step attack sequence** invoked from two contexts, built on three shared sub-sequences
(attack-sequence → resolve-hits → deal-wounds); morale fires from ruleset-owned triggers, never
from inside the attack machinery. Abandonment settled: pop the frames, applied effects stay,
logged with an actor. `Counter` before `Impact` was ruled; the 40k core rules were read as a
falsifier and produced nine explicit constraints on what the interpreter must not contain.
**Only OPR's `GameRules` gets built.**

**The scheduler** — whose input is needed next, and what may they do with it. A generic engine
component with two implementations behind one interface, selected by `game.scheduler_shape`;
everything ruleset-specific is a hook `GameRules` supplies — who goes first each round, which
units may activate (a reading of the action record), which actions they may take, the phase
before round one, the round-boundary hooks, the phases inside a turn. Its state — round, order
over teams, whose turn, who has finished — is **rebuilt from the log and never stored**, carried
by four events: `round_started`, `turn_started`, `turn_ended`, `phase_started`. **The turn is
an Ask** through the interpreter's own Ask type, with the activatable units and their actions as
the legal set; the turn ends when the declared action's sequence finishes, and moving models by
hand never ends one — that exists to arrange models inside a unit. A team with an empty legal
set is skipped, which is how a spectator's team never sees a prompt. Deployment is the phase
before round one, driven by the same scheduler after a logged roll-off; Scouts and Ambushers
follow pages 13–14. **Reactions resolve in turn order starting after the actor**, replaceable by
a hook; muted window kinds are per player; the scheduler shape and the armies in play are fixed
once playing (this first said team membership, and the setup phase corrected it the same day). Page 49's team games and free-for-all were read: turn order is team-based, and
free-for-all rolls off every round — a first-team policy, not a scheduler shape. Six constraints
on the interface were derived from 40k's battle round, Fight phase and Reinforcements. The
roll-off does not pick a table edge, and that deviation from page 6 is accepted and handed to the
setup phase session — which then reconciled it with a zone swap.

**The setup phase** — who places what, and when, between the lobby and the first turn. **Editing is
free for all**: any participant places, moves, removes or overrides anything, at the same time as
anyone else, and the rulebook's roll-off-and-alternate placement styles are social, because setup
is unlogged and any turn structure there would be a second, unlogged scheduler. Setup edits are a
**third message kind** — applied, broadcast, persisted, never logged — and **the game's
`table_state` row is the live draft**, written through by the game server after each broadcast,
so a dead task recovers it by the one hydrate path; live drag is presence and the drop is the
edit, so persistence sees one write per drop. **The creator starts and ends setup** and has no
other standing, which added `game.created_by`. Zones are assigned to team ordinals by the zone
style and **anyone may swap two teams' zones**, which is how the roll-off winner picks an edge.
Loading a layout replaces the whole draft, size included, closing setup content's open item.
**Terrain generators** are the rulebook's four planet tables (pages 17–18), run on the client as a
batch of adds that land beside the table; they place **built-in terrain** that ships with the
client like textures do, so `terrain_placement` names a built-in or an asset, and a user may swap
the mesh while the piece keeps its volumes and declarations. Importing terrain and setting its six
declarations was already designed end to end (asset library, geometry values) and is untouched.
**Joining is allowed at any status**, restoring accounts' settled rule over the scheduler's
one-day-old contradiction: a late joiner brings no army and is what a spectator is.

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

**Presence** — what players see of each other between events. Nothing in it
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
content hash with nothing on the other end; it now has eight tables. Stat declarations, one
catalog of named rules whose kinds are `unit`, `model`, `weapon` and `spell`, each entry's
effects and their conditions, plus the two tables that exist because **what an effect can
target must be data**: actions (a name and a distance, because Grimdark Future has no movement
stat and `Fast` needs something to point at) and, since 09-03, rolls (a name and nothing else,
because `AP(X)` points at the Defense roll and 40k's wound and save rolls must be content
rather than a migration). What only the engine reads — may it shoot, a roll's dice convention,
its threshold derivation, its modifier cap — stays in `GameRules`.

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

**Nothing in the data layer**, nor the resolution machinery (09-03), nor the scheduler and the
setup phase (09-04). What still needs a design session:

- **The client shell.**
- **The mesh prep tool** — `architecture.md` says outright it is undesigned.
- **Deployment**, meaning the infrastructure — ECS Fargate, RDS, S3 in `architecture.md`. Not
  unit deployment, which the scheduler designed.
- **The effect grammar follow-up** — small: real spells (Alien Hives, via the Army Forge API)
  exposed two gaps, a grant with a duration (*"gets X once"*) and multi-target selection
  (*"pick up to two friendly units within 12""*). Neither blocks anything except authoring
  spell rows.

**Three things from the rulebook were deliberately left out**, recorded where they belong:

- **Buildings as targets.** A solid building has `Defense 2+` and a player-set `Tough(X)` and
  can be destroyed. That needs terrain to carry stats and to become event-driven state on top
  of the setup snapshot — additive, per setup content, and not done.
- **Action boosting.** The optional rules are written as percentages — boosted `Rush` moves
  *"+50%"* — and `amount` is an integer. None of the twelve effect kinds expresses a
  proportion.
- **Advanced Casting.** Three optional modules — spell books, the psychic pool, living
  spells (GF pages 31–32). Core casting and the token economy are designed; these are not.

## Open questions

Carried from the design docs:

- Conflicts between two abilities reading the same result — whether both apply or one
  wins. Deferred deliberately: every example is specific to one ruleset.
- The two `GameRules` interpretations action sequences flagged rather than guessed: whether
  *"one try per spell"* scopes to the activation or the round, and whether a later weapon
  group's attacks are recomputed after an earlier group's casualties. (`Counter` before
  `Impact` was ruled on 09-03.)
- Whether `step_completed` payloads need a schema per step kind.
- The triangle-count threshold at upload. Agreed in principle, never chosen.
- Whether an objective ever needs game data again. Setup content deleted the field because
  scoring is manual; reopening it needs a reader.
- Whether terrain can be unlocked mid-game. Not a permission change — it is the point at which
  terrain becomes event-driven state on top of the setup snapshot.
- Whether the closed terrain declaration set is complete.
- Whether the client ever needs to edit an army rather than only pick one.
- Loopback or device code for getting a browser session into the client.
- Whether a thirteenth effect kind, or a flavour on kind 1, carries proportions. Only the
  optional boosting rules need it.
- Whether `GameRules` ever becomes data. Not before a second ruleset exists, per
  `architecture.md`; Warhammer is what would force it — and the nine interface constraints in
  [action sequences](superpowers/specs/2026-09-03-action-sequences-design.md) are the start of
  that generalisation, derived from reading 40k's core rules rather than imagining them.

**Do not treat this list as live without checking.** Each design doc's own *Still open* section
is the authority, and a later doc beats an earlier one.

## Next — pick up here

**Design the remaining modules, one prompt each: the client shell, the mesh prep tool,
deployment, and the small effect grammar follow-up.** The client shell is the natural next
session — every server-side behaviour it has to render now exists: Asks from the scheduler and the
interpreter, presence, setup edits, the lobby's two creator transitions, the game browser.

**Then review the whole design**, the way the data layer was reviewed on 08-30 — across
documents, looking for the contradictions and stale open lists that single sessions leave.

**Only after that review do implementation plans start.** Ruled on 09-03. That includes the
contracts (`Tabletop.Geometry`, `Tabletop.Protocol`, `Tabletop.Content`) and everything
`architecture.md`'s build order says is "not waiting on design" — nothing is exempt from the
review gate. `docs/workstreams/` gets rewritten after the contracts exist, so the prompts can
name real types rather than describe them.

## Two things worth carrying into any session

**Consult the rulebooks; do not reason about the rules.** Reading them has produced better
answers than designing did, nine sessions running — most recently the scheduler, where page
49's team-game and free-for-all rules settled turn order without a guess, and the setup phase,
where pages 16–18 supplied the generators and the line about removing unplaceable terrain. Sources:

- Grimdark Future v3.4.1 and Age of Fantasy v3.5.0, PDFs on the Windows host at
  `/mnt/c/Users/clays/Downloads/`.
- Warhammer 40,000 10th edition core rules, readable at
  <https://wahapedia.ru/wh40k10ed/the-rules/core-rules/> via WebFetch; core Stratagem timings
  turn up through search on the same site.
- Per-faction spells via the Army Forge API:
  `https://army-forge.onepagerules.com/api/army-books?filters=official&gameSystemSlug=grimdark-future`
  lists books with `uid`s; `.../api/army-books/{uid}?gameSystem=2` returns a `spells` array.

No `pdftotext` and no `poppler-utils`; headless Chrome cannot launch (missing `libnspr4`). What
works: `pypdf` in a venv (already at `./venv`) for text, and `pypdf` with Pillow to extract
diagrams as images, since the deployment zone dimensions live in pictures rather than the text
layer.

```bash
./venv/bin/python -c "
from pypdf import PdfReader
r = PdfReader('/mnt/c/Users/clays/Downloads/GF - Advanced Rules v3.4.1.pdf')
print(r.pages[12].extract_text())"        # 68 pages; special rules are 13-14
```

Useful page numbers in Grimdark Future v3.4.1: **7** actions and their distances, **8-10**
shooting, charging and melee, **11-12** terrain, **13-14** every special rule, **16** placement
styles, **18** army terrain, **20** buildings, **21-23** deployment styles, **24-28** missions,
**16** terrain placement styles, **17-18** terrain generators, **19** army terrain, **29-34** extra
actions, boosting, advanced casting and hero upgrades, **49** multiplayer games.

**Age of Fantasy is not a second ruleset for testing generalisation.** Its terrain section is
word for word identical to Grimdark Future's — same engine, same text. Warhammer is the real
test: 40k's core rules have now been read once (action sequences), and the standing questions —
whether the six terrain declarations are complete, whether twelve effect kinds survive, when
`GameRules` moves to data — still wait on designing against it for real.

## Where to look at the model

Both diagrams render at <https://claude.ai/code/artifact/f5d2d64c-d819-4553-b03f-e23271ac0f63>,
zoomable, generated from `docs/schema.md` and `docs/data-model.md`. **It is a copy, not a
source** — it goes stale the moment either document changes, and it is regenerated by pulling
the mermaid block out of each and republishing to the same URL. Regenerated 09-04 after `game.created_by` and `terrain_placement.builtin` landed.

## Environment notes

Stytch MCP is authenticated and works. Project `TabletopWar`, vertical `CONSUMER`, test
project `project-test-437432c7-798d-49d2-9dc4-eff103937e0f`. There is no production
environment — both redirect URLs are in the test project. The redirect config was edited
during the 08-24 session, so re-read it rather than trusting older quoted values.

The available MCP tools are project-configuration only — redirect URLs, SDK config,
tokens, email templates. There are no user-management tools, so anything about the
Consumer user schema has to come from the docs.
