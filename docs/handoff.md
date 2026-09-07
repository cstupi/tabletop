# Handoff — 2026-09-06

Where the project stands, and what to pick up next.

## State

Tree is clean; everything is committed. **No code exists** beyond the Godot scaffold. Every
module is designed, planned, and documented. Nothing is implemented.

| Layer | Where | Size |
| --- | --- | --- |
| Design sessions | `docs/superpowers/specs/`, 21 files | the requirements |
| Implementation plans | `docs/superpowers/plans/`, 10 files | 37,876 lines |
| Engineering design docs | `docs/design/`, 10 folders plus an index | 15,060 lines |

**Read in this order from cold:** this file, then `docs/design/README.md`, which is the system
design document and defines the vocabulary. Then `docs/architecture.md`. Then the design document
for whatever you are touching, and its plan beside it.

Precedence, when two documents disagree: **the specs settle behaviour, the plans settle what gets
written, the design docs settle structure.** A later spec beats an earlier one, and
`superpowers/specs/2026-09-04-whole-design-review.md` beats every other spec. Where a design
document disagrees with its plan, the plan is what an implementer follows.

## The mode

Design and planning are both complete. **The next thing is execution**, starting with
`superpowers/plans/2026-09-05-contracts-and-local-dev.md`, which everything else consumes.

Execute with the `superpowers:subagent-driven-development` or
`superpowers:executing-plans` skill, as each plan's own header says. Build order is in
`docs/design/README.md`: contracts, then geometry and the management API and the game server
skeleton in any order, then the web app, the client, and the two rules modules. The simulator and
the viewer come last and repay early, because the rules modules' full-game tests run through the
simulator.

Two things the plans depend on that are not yet done:

- **About seventy proposed spec corrections sit unapplied**, in each plan's *Spec corrections
  proposed* section. They are corrections to the specs, `docs/schema.md`, `docs/architecture.md`
  and `CLAUDE.md`. `docs/schema.md` in particular is stale against
  the schema SQL the contracts plan will write to `scripts/postgres/init/001_schema.sql`, which
  exists only inside that plan until it is executed. `docs/schema.md` is missing
  `participant.game_id`, `participant.army_id`, `game.rng_seed`, `blob.uploaded` and
  `blob.content_type`. Apply them before or alongside execution.
- **`CLAUDE.md` still describes three deliverables.** `sim/` and `viewer/` are the fourth and
  fifth, ruled 2026-09-06. Several plans' last task edits `CLAUDE.md`; those edits are written to
  compose in build order.

## The ten modules

| Module | Ships as | Plan | Design |
| --- | --- | --- | --- |
| Contracts and local dev | `libs/`, `docker-compose.yml`, schema SQL | `2026-09-05-contracts-and-local-dev.md` | `design/contracts/` |
| Geometry | `libs/Tabletop.Geometry` bodies | `2026-09-05-geometry.md` | `design/geometry/` |
| Management API | `server/management/` | `2026-09-05-management-api.md` | `design/management-api/` |
| Game server | `server/game/`, no rules | `2026-09-05-game-server-skeleton.md` | `design/game-server/` |
| Web app | `web/` | `2026-09-05-web-app.md` | `design/web-app/` |
| Client | `client/` | `2026-09-05-client-shell.md` | `design/client/` |
| Interpreter and effects | `server/game/`, the rules | `2026-09-05-interpreter-and-effects.md` | `design/interpreter/` |
| Scheduler | `server/game/`, turn order | `2026-09-05-scheduler.md` | `design/scheduler/` |
| Simulator | `sim/`, a developer tool | `2026-09-06-simulation.md` | `design/simulation/` |
| Viewer | `viewer/`, a developer tool | `2026-09-06-viewer.md` | `design/viewer/` |

The two developer tools are new as of 2026-09-06 and have their own design session,
`superpowers/specs/2026-09-06-simulation-design.md`. The simulator plays whole games with no
client, through either an in-process driver or a real socket, and records a projection per event.
The viewer draws that recording top-down, or attaches to a live game as a spectator.

## Rulings of 2026-09-06

The owner ruled on about thirty questions in one session. They are applied across the plans and
the design documents, and this is the index, because they are otherwise scattered. Where a ruling
overturned an assumption the plans had already been written against, that is marked.

### The wire and the servers

| Ruling | Note |
| --- | --- |
| The RNG seed is stored, not derived from the game id | **Overturned.** `long RngSeed` on `GameInfo`, positional, over `game.rng_seed bigint not null` with no default. Management writes it once at creation. The simulator sets it and uses ordinary UUIDv7 ids |
| An unknown id in an event is a typed desync; the client rebuilds | **Overturned.** The applier had sixteen fallible lookups, not the two that were known. The client bounds retries by the sequence position of the fault. The server faults instead, holding the only copy of the log |
| The payload format's lack of versioning is accepted | Revisit trigger: the first game worth keeping |
| The server sends a rest frame when it has nothing more to emit | New `AtRest(long Seq)`. The frame follows its batch: broadcast when the batch appended an event, to the provoking client alone when it did not. It replaced the simulator's 250 ms quiet window |
| Presence departure stays an explicit frame | Confirmed |
| The account id reaches the token through a Stytch claim template | **Overturned.** `{"account_id": "{{ user.external_id }}"}`, rendered on every mint. One re-mint survives on a first login, because the one-time code is single-use so the session is minted before `external_id` exists. The template is project configuration and its absence is silent |
| A development-only endpoint mints simulator tokens | Real Stytch sessions, via embeddable magic links, which take an external id and send no email. No second signing key |
| Only the creator may end or delete a game | **Overturned.** Was any participant |

### Teams, and the boundary that was not a wall

| Ruling | Note |
| --- | --- |
| Teams are locked at the start of play; a player rejoins their slot | The specs contradicted themselves on this and it kept resurfacing. Corrected in place across four specs, with the canonical statement in `superpowers/specs/2026-08-28-game-flow-design.md` → *Teams are frozen at the flip, and a team is a slot*. Every other document points there |
| The game server seeds the ruleset content, and the schema boundary is not a wall | Confirmed, and the prose treating a write as a border crossing was removed. Row ownership by game status stays, because hydration depends on it |

### The rules

| Ruling | Note |
| --- | --- |
| One try per spell resets per activation | Confirmed |
| Attack counts, and the `Blast` cap, are settled before any group fires | **Overturned.** A group firing after casualties rolls its original count |
| A unit cannot enter an enemy transport | Confirmed. Page 14's clause about occupants targeting the transport itself is therefore unreachable, since valid targets are enemies only |
| A transport survivor with nowhere legal to stand is destroyed | **Overturned.** Was shelved |
| Sight is centre to any part of the target | **Changed.** The eye is the body's centre. The target is sampled on three rings, base, mid-height and top, which bounds an error that was unbounded vertically |
| Cover is a fraction of the unit at fifty per cent | **Changed.** Fewer than half the models visible gives cover; half or more inside cover terrain gives cover. A single model uses its base centre. Geometry reports per-model visibility and the ruleset applies the threshold |
| A team allocates its token spend across its own models | **Overturned.** Was a total the server took from the nearest models. The shape is a map from model to its own cap, with no total |
| The caster may spend its own leftover tokens on its own roll | **Overturned.** A caster inside a transport therefore now modifies its own roll, where it used to roll a flat 4+ |
| Sniper picks which model takes the wound | **Overturned.** A sniped wound skips only what building the assignment prompt decides: free choice of who dies, wounded `Tough` first, heroes last. `Deadly`, `Regeneration` and `Tough` as a capacity still apply, and wounds past that capacity are lost |
| An unreadable turn answer follows the enforcement dial | **Overturned.** Was always a pass. In practice it bites only for a ruleset that offers an id it cannot read |
| The deployment roll-off is gated like any other roll | **Overturned.** It became two steps, because editing dice rewrites the roll record but never a completed step. It is the one gate that ignores the between-steps wait setting |
| The round does not advance on its own; a player ends it | **Overturned**, ruled earlier the same day. Any player on any team answers the boundary Ask. This also removed the need to handle a round nobody can act in |

### Content and platform

| Ruling | Note |
| --- | --- |
| A joined hero merges into the unit it joined | **Overturned.** Its stats land on its slot. It fights at the squad's quality and defence, because the block roll is thrown before any wound is assigned to a model |
| Height scales from the published base | **Overturned.** Was a flat 1.5 inches |
| Both Grimdark Future and Age of Fantasy import | **Overturned.** Same mechanics, one rules implementation. **No Age of Fantasy content set exists**, so such a list imports before there is anything to play it with |
| Weapons are dealt onto models in order | Confirmed |
| The table origin is a corner | Ratified, having been ruled by a compatibility review rather than by the owner |
| Side Battle's zone scales with the table; small tables are out of scope | **Changed**, and it contradicted a spec. Side Battle is the only proportional style; on a table near the standard size every style agrees anyway |
| The client takes no Windows-only dependency | **Changed.** `architecture.md` said Windows outright. The stored session is a file created owner-only, not Windows data protection |
| A minimal built-in art set ships | **Overturned.** Ten grouped meshes and five textures. These are asset files a person must make; the hitbox-first drawing is the fallback |
| A roll is shown as a readout, not as dice objects | Confirmed |
| Terrain generator alternatives are picked at random | Confirmed |
| Zones for team ordinals with no team are drawn grey | Confirmed |
| The game list filters by status, name and created-by-me | Confirmed |

## Open questions

Every plan proceeds under a stated assumption, so none of these blocks execution. Each plan's own
*Questions for the project owner* section is the authority; this is the union, and the ones marked
worth asking are the ones where a late answer means rewriting rather than editing.

**Worth asking**

- **Who chooses which model pays** is settled, but two residues are not: whether a sniper group
  with two Sniper models picks one target between them, and that *"resolved as if it was a unit of
  1"* is not modelled (interpreter, question 7's residue).
- **The `Blast` cap and attack counts now agree**, but `Impact` and melee still read live model
  counts mid-action, deliberately, because page 9 sequences melee by hand. Worth confirming that
  is the intent (interpreter, question 3's residue).
- **A terrain container** for a solid building holding models: the automated embark path is model
  containers only, and a building holds models through the menu (geometry, question 8).
- **The simulator's scenario catalogue**, and whether a casting scenario joins it now that casting
  has two player decisions rather than one (simulation, question 4).

**Local, and safe as assumed**

Client: presence sample rate, the facing a disembarking model takes, whose base the disembark
region is drawn for, whether a container is a click target or a button. Geometry: straight-line
movement reach, ovals as 64-gons. Interpreter: whether the generic effect code may know the two
weapon stat names, whether a caster inside a container may cast. Management: how partial weapon
counts are dealt, game list ordering, whether textures accept WebP. Game server: what the setup
draft starts as, one connection per participant. Web app: where the browser keeps its token.
Simulator: exit code on a stuck run, solution membership. Viewer: headless rendering tests,
fixture size, fetching a recording over HTTP, refusing an unknown ruleset, panel layout.

**Deferred by design**, each recorded where it belongs: hosting; buildings as shootable targets
and army terrain; action boosting; Advanced Casting's optional modules; and everything waiting on
a second ruleset, which is Warhammer rather than Age of Fantasy, since the two One Page Rules
games share an engine.

## How to work here

Five things, four of them learned the hard way this session.

- **Read the rulebook; do not reason about the rules.** Ten sessions running, reading has produced
  better answers than designing. Grimdark Future v3.4.1 and Age of Fantasy v3.5.0 are at
  `/mnt/c/Users/clays/Downloads/`. Extract with `pypdf` from `./venv`; page numbers below.
- **Name a mechanic in the rulebook's own words.** Casting's token spend was called a "sealed bid"
  and reached 223 occurrences across 18 files, including a wire discriminator and generated
  TypeScript, before the owner caught it. There is no bid in the rules. Quote the rule, then name
  the thing.
- **A ruling must reach every layer.** Plans, sibling plans, specs, the design documents including
  their mermaid diagrams, `docs/design/README.md`, and a new module means a new design folder.
  Verify with grep for the superseded fact, not from memory of what you edited. A diagram survived
  three commits asserting the opposite because nobody grepped.
- **Write for a principal engineer.** No addressing the reader, no captions explaining diagram
  notation, no defining standard terms, no meta-sentences about the design's own importance, no
  em-dashes, one fact per sentence. The first draft of `docs/design/` was rejected outright and
  rewritten.
- **Ask questions in the language of the game, not of the code.** A question about which model
  loses its tokens is answerable; a question about a capacity predicate is not.

## Environment

Checked 2026-09-06. WSL `dotnet` 10.0.400 on PATH; Docker 29.7 and Compose v5.4; `./venv` has
`pypdf` for the rulebook; Godot 4.7.2 Mono on the Windows host, opened with `./scripts/godot -e`.
No `pdftotext`, and headless Chrome cannot launch (missing `libnspr4`), so the rulebook's diagrams
are read by extracting images with `pypdf` and Pillow.

Stytch: project `TabletopWar`, vertical CONSUMER, test project
`project-test-437432c7-798d-49d2-9dc4-eff103937e0f`, no production environment. Its MCP tools are
project configuration only, with no user management, so the Consumer user schema has to come from
the docs. **Two pieces of Stytch configuration are now load-bearing and not in the repo:** the
`SESSION` claim template mapping `{{ user.external_id }}` to `account_id`, whose absence takes
both services down with no error from Stytch; and a registered redirect URL for password resets.

```bash
./venv/bin/python -c "
from pypdf import PdfReader
r = PdfReader('/mnt/c/Users/clays/Downloads/GF - Advanced Rules v3.4.1.pdf')
print(r.pages[12].extract_text())"        # 68 pages, zero-based; special rules are 13-14
```

Useful pages in Grimdark Future v3.4.1: **6** objectives and the deployment roll-off, **7** rounds
and action distances, **8-10** shooting, charging and melee, **11-12** terrain, **13-14** every
special rule, **16** terrain placement styles, **17-18** terrain generators, **19** army terrain,
**20** buildings, **21-23** deployment styles (the zone dimensions are in the diagrams, not the
text), **24-28** missions, **29-34** extra actions, boosting, advanced casting and hero upgrades,
**49** multiplayer.

Per-faction spells through the Army Forge API:
`https://army-forge.onepagerules.com/api/army-books?filters=official&gameSystemSlug=grimdark-future`
lists books with `uid`s; `.../api/army-books/{uid}?gameSystem=2` returns a `spells` array. A
shared list is `GET /api/tts?id={id}`, which is what import reads.

## Where to look at the model

Both diagrams render at <https://claude.ai/code/artifact/f5d2d64c-d819-4553-b03f-e23271ac0f63>,
generated from `docs/schema.md` and `docs/data-model.md`. **It is a copy, not a source**, it is
stale as of 2026-09-06, and regenerating it means pulling the mermaid block out of each document
and republishing to the same URL. Both documents are themselves behind
the schema SQL inside the contracts plan; see *The mode* above.
