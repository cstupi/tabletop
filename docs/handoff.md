# Handoff — 2026-09-06

Where the project stands, and what to pick up next.

## State

Tree is clean; everything is committed. **No code exists** beyond the Godot scaffold. Every
module is designed, planned, and documented. Nothing is implemented.

| Layer | Where | Size |
| --- | --- | --- |
| Design sessions | `docs/superpowers/specs/`, 23 files | the requirements |
| Implementation plans | `docs/superpowers/plans/`, 10 files | 37,972 lines |
| Engineering design docs | `docs/design/`, 10 folders plus an index | 15,085 lines |

**Read in this order from cold:** this file, then `docs/design/README.md`, which is the system
design document and defines the vocabulary. Then `docs/architecture.md`. Then the design document
for whatever you are touching, and its plan beside it.

Precedence, when two documents disagree: **the specs settle behaviour, the plans settle what gets
written, the design docs settle structure.** A later spec beats an earlier one. Where a design
document disagrees with its plan, the plan is what an implementer follows.

`superpowers/specs/2026-09-04-whole-design-review.md` beats every spec written before it, which
was all of them until `2026-09-06-simulation-design.md`. That one postdates the review and is not
subordinate to it; it also covers two modules the review never saw.

**`superpowers/specs/2026-09-06-ruleset-3-5-1-design.md` is newer than both** and beats them on
every point it touches, which is the ruleset alone: the rules text the engine implements, the
names those rules carry, and the pages they are cited from. The repository was designed against
Grimdark Future v3.4.1; the owner supplied v3.5.1 for both games on 2026-09-06, and it renames
five rules the plans were built on, removes two, adds three, changes behaviour in seventeen
places and shifts every printed page from 15 onward by one. The rebase is propagated: `Sniper` is
`Takedown`, `Entrenched` is `Artillery`, `Lance` is `Thrust`, `Poison` is `Bane`, `Lock-On` is
gone, and `Surge` and `Unstoppable` are new.

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

- **Ninety-eight spec corrections are proposed and about eighty-three are still pending**, in each
  plan's *Spec corrections proposed* section; only fifteen carry a resolution. They target the
  specs, `docs/schema.md`, `docs/data-model.md`, `docs/architecture.md`, `CLAUDE.md`, and in nine
  cases a sibling plan. Apply them before or alongside execution.
- **`docs/schema.md` is behind the schema SQL** the contracts plan will write to
  `scripts/postgres/init/001_schema.sql`, which exists only inside that plan until it is executed.
  Six columns are missing from the document: `participant.game_id`, `participant.army_id`,
  `game.rng_seed`, `blob.uploaded`, `blob.content_type` and `effect.target_stat_level`, the last
  of which a foreign key the document promises cannot exist without.
- **`CLAUDE.md` and `docs/architecture.md` both undercount the deliverables.** `sim/` and
  `viewer/` were ruled the fourth and fifth on 2026-09-06. **All ten** plans' last task edits
  `CLAUDE.md`, though geometry's and the scheduler's task titles do not say so, and the simulator
  plan owns the `architecture.md` deliverables table. Those edits were audited on 2026-09-06 and
  three did not compose: two would have reverted earlier plans' wording and one would have
  reverted the client's platform correction. They compose now, but the hazard is structural, so
  check a plan's *Before* text against the file as it actually stands rather than trusting it.
- **The game server plan's `CLAUDE.md` edit replaces text it labels as the management plan's**, so
  those two are not interchangeable in order even though the build graph allows it. Run management
  first.

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
| An unknown id in an event is a typed desync; the client rebuilds | **Overturned.** Sixteen of the applier's lookups can miss; only two were reachable designed cases, and only those two were tested. The client bounds retries by the sequence position of the fault. The server faults instead, holding the only copy of the log |
| The payload format's lack of versioning is accepted | Revisit trigger: the first game worth keeping |
| The server sends a rest frame when it has nothing more to emit | New `AtRest(long Seq)`. The frame follows its batch: broadcast when the batch appended an event, to the provoking client alone when it did not. It replaced the simulator's 250 ms quiet window |
| Presence departure stays an explicit frame | Confirmed |
| The account id reaches the token through a Stytch claim template | **Overturned.** `{"account_id": "{{ user.external_id }}"}`, rendered on every mint. One re-mint survives on a first login, because the one-time code is single-use so the session is minted before `external_id` exists. The template is project configuration and its absence is silent |
| A development-only endpoint mints simulator tokens | Real Stytch sessions, via embeddable magic links, which take an external id and send no email. No second signing key |
| Only the creator may end or delete a game | **Overturned.** Was any participant |

### Teams, and the boundary that was not a wall

| Ruling | Note |
| --- | --- |
| Teams are locked at the start of play; a player rejoins their slot | The specs contradicted themselves on this and it kept resurfacing. Corrected in place across four specs, with the canonical statement in `superpowers/specs/2026-08-28-game-flow-design.md` → *Teams are frozen at the flip, and a team is a slot*. A sixth document, the scheduler spec, was still contradicting it until an audit on 2026-09-06 |
| The game server seeds the ruleset content, and the schema boundary is not a wall | Confirmed, and the prose treating a write as a border crossing was removed. Row ownership by game status stays, because hydration depends on it |

### The rules

| Ruling | Note |
| --- | --- |
| One try per spell resets per activation | Confirmed |
| Attack counts, and the `Blast` cap, are settled before any group fires | **Overturned.** A group firing after casualties rolls its original count |
| A unit cannot enter an enemy transport | Confirmed. Page 14's clause about occupants targeting the transport itself is therefore unreachable, since valid targets are enemies only |
| A transport survivor with nowhere legal to stand is destroyed | **Overturned.** It was shelved unwounded; it is now shelved with wounds at capacity and counted on the activation's tally, which is what makes it a casualty rather than a stored model |
| Sight is centre to any part of the target | **Changed.** The eye is the body's centre. The target is sampled on three rings, base, mid-height and top, which bounds an error that was unbounded vertically |
| Cover is a fraction of the unit at fifty per cent | **Changed, then revised the same day to p.11's own word.** Fewer than half the models visible gives cover; a *majority* inside cover terrain gives cover, which is strictly more than half, so five of ten is not enough. A single model uses its base centre. Geometry reports per-model visibility and the ruleset applies the threshold |
| A team allocates its token spend across its own models | **Overturned.** Was a total the server took from the nearest models. The shape is a map from model to its own cap, with no total |
| The caster may spend its own leftover tokens on its own roll | **Overturned.** A caster inside a transport therefore now modifies its own roll, where it used to roll a flat 4+ |
| `Takedown` picks which model takes the wound | **Overturned, then overtaken by 3.5.1.** The rule was called `Sniper` in 3.4.1 and the first ruling gave a group one pick between its models; 3.5.1 renamed it `Takedown`, dropped its *Shoots at Quality 2+*, and rewrote the clause as *"This model may pick"*, so the owner ruled the pick **per model** and `GfShooting.Groups` splits a takedown weapon type into one group per model. A takedown wound skips only what building the assignment prompt decides: free choice of who dies, wounded `Tough` first, heroes last. `Deadly`, `Regeneration` and `Tough` as a capacity still apply, and wounds past that capacity are lost |
| An unreadable turn answer follows the enforcement dial | **Overturned.** Was always a pass. In practice it bites only for a ruleset that offers an id it cannot read |
| The deployment roll-off is gated like any other roll | **Overturned.** It became two steps, because editing dice rewrites the roll record but never a completed step. It is the one gate that ignores the between-steps wait setting |
| The round does not advance on its own; a player ends it | **Overturned**, ruled earlier the same day. Any player on any team answers the boundary Ask. This also removed the need to handle a round nobody can act in |

### Content and platform

| Ruling | Note |
| --- | --- |
| A joined hero merges into the unit it joined | **Overturned.** Its stats land on its slot. It attacks at **its own** Quality, ruled 2026-09-06 after p.4's *"the model's Quality value"* and the Hero clause overriding Defense alone, so `roll-to-hit` emits one roll per distinct Quality among the attacking models. It blocks on the unit's Defense until every other model is dead, which is the Hero clause's own sunset and is now implemented. It takes the unit's morale tests whenever its own Quality is better |
| Height scales from the published base | **Overturned.** Was a flat 1.5 inches |
| Both Grimdark Future and Age of Fantasy import | **Overturned.** Same mechanics, one rules implementation. **No Age of Fantasy content set exists**, so such a list imports before there is anything to play it with |
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

**Answer these first: a late answer means rewriting rather than editing**

- **What aggregation does `Artillery`'s -2 use?** Interpreter question 4, and the only one of the
  four originals still open. 3.5.1 deleted `Entrenched` and folded its penalty into `Artillery`,
  which writes it per model — *"When enemy units shoot at this model from over 9" away"* — while a
  hit roll is pooled per weapon group. **The owner ruled `all`** on 2026-09-06, after `Stealth`,
  which is the same penalty at the same range band and says *"all models"*. Recorded here because
  the alternative, cover's majority, would change what a partly-entrenched unit gets.
- **Are weapons dealt onto models in order?** Management question 4, and untouched by 3.5.1
  because it asks about Army Forge data rather than the rules. Assumed an advancing cursor, so a
  lone flamer lands on the last model. Asking the player instead means import cannot expand a
  unit on its own, which needs a per-slot weapon editor and a patch shape, neither designed.
- **How is the elevated 3" coherency measured?** New with 3.5.1. Page 11 gained *"If not all
  models in a unit can fit on elevation, then a coherency of 3" is allowed to models at different
  elevation."* `Coherency.Check` takes one chain distance for every pair, so a per-pair distance
  keyed on elevation is a signature change, and whether a unit *can fit* is a ruleset judgement
  geometry cannot make. The rule states the allowance and not the test.

Three of the four questions that stood here on the morning of 2026-09-06 are now answered:

- **A joined hero fights at its own quality.** Ruled, and the roll record becomes per-quality.
- **A disembarking model may take any facing that fits.** Ruled, over the container's yaw.
- **`Entrenched`'s aggregation** dissolved with the rule, and returned as `Artillery`'s above.

**Cheap either way, but still open**

- A terrain container for a solid building holding models, geometry question 8. One overload, five
  lines and a test.
- The simulator's scenario catalogue, and whether a casting scenario joins it now that casting has
  two player decisions, simulation question 4. Changes nothing else.
- The disembark radius and whether it is measured edge to edge, interpreter question 11.

Three more are worth a look but are **not** open questions, so a plan's question list will not
surface them. All are residues recorded inside a question the owner already closed:

- Interpreter question 7 is closed twice over, but `Takedown`'s *"resolved as if it was a unit of
  [1]"* is still not modelled: cover, `Blast`'s cap and the defence roll read the whole target
  unit, and only the wound is redirected.
- Interpreter question 3 is closed, but `Impact` and melee still read live model counts mid-action,
  deliberately, because page 9 sequences melee by hand.
- Interpreter question 8 is closed, but the end-of-turn morale test's die is still unreachable from
  the menu: it is a single-step frame with an actor, so it pops before anything can gate it.

**Local, and safe as assumed**

Client: presence sample rate, whose base the disembark region is drawn for, whether a container is
a click target or a button. Geometry: straight-line movement reach, ovals as 64-gons. Interpreter:
whether the generic effect code may know the two weapon stat names, whether a caster inside a
container may cast. Management: game list ordering, whether textures accept WebP. Game server:
what the setup draft starts as, one connection per participant. Web app: where the browser keeps
its token. Simulator: exit code on a stuck run, solution membership. Viewer: headless rendering
tests, fixture size, fetching a recording over HTTP, refusing an unknown ruleset, panel layout.

**Deferred by design**, each recorded where it belongs: hosting; buildings as shootable targets
and army terrain; action boosting; Advanced Casting's optional modules; and everything waiting on
a second ruleset, which is Warhammer rather than Age of Fantasy, since the two One Page Rules
games share an engine.

## `docs/workstreams/` is superseded

Eight prompt files from before the first design session, describing a data model that no longer
exists. Until 2026-09-06 its README told every session to read it first and marked five
workstreams ready, which is a second execution scheme competing with the plans. It now carries a
superseded banner, each prompt file names the plan that replaced it, and `docs/architecture.md` no
longer implies any of it is current.

It was kept rather than deleted because eleven files across the plans, the specs and the design
documents cite those paths as the sketch they departed from. **Execute nothing from it.**

## How to work here

Five things, four of them learned the hard way this session.

- **Read the rulebook; do not reason about the rules.** Ten sessions running, reading has produced
  better answers than designing. Grimdark Future v3.5.1 and Age of Fantasy v3.5.1 are at
  `/mnt/c/Users/clays/Downloads/`, as `GF - Advanced_Rules_v3_5_1.pdf` and
  `Age_of_Fantasy_-_Advanced_Rules_v3_5_1_-_Print_Friendly.pdf`. The 3.4.1 and 3.5.0 files sit
  beside them and are superseded; the repository was rebased off them on 2026-09-06, and
  `superpowers/specs/2026-09-06-ruleset-3-5-1-design.md` records what moved. Extract with `pypdf`
  from `./venv`; page numbers below.
- **Name a mechanic in the rulebook's own words.** Casting's token spend was called a "sealed bid"
  and reached over three hundred occurrences across twenty files, including a wire discriminator
  and generated TypeScript, before the owner caught it. Measurable at `4b2e56b~1` if you want the
  exact figure; three different greps gave three different answers, which is its own lesson. There is no bid in the rules. Quote the rule, then name
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
`project-test-437432c7-798d-49d2-9dc4-eff103937e0f`. A live project exists and is
unconfigured; nothing uses it. Its MCP tools are
project configuration only, with no user management, so the Consumer user schema has to come from
the docs. **Two pieces of Stytch configuration are now load-bearing and not in the repo:** the
`SESSION` claim template mapping `{{ user.external_id }}` to `account_id`, whose absence takes
both services down with no error from Stytch; and a registered redirect URL for password resets.

```bash
./venv/bin/python -c "
from pypdf import PdfReader
r = PdfReader('/mnt/c/Users/clays/Downloads/GF - Advanced_Rules_v3_5_1.pdf')
print(r.pages[12].extract_text())"        # prints printed page 13
```

`pypdf` indexes from zero and the page numbers below are the rulebook's printed ones, so
`r.pages[N]` is printed page `N + 1`. Grimdark Future 3.5.1 is 71 pages, Age of Fantasy 3.5.1 is
70. Every citation in this repository uses printed numbering.

Useful pages in Grimdark Future v3.5.1: **6** objectives and the deployment roll-off, **7** rounds
and action distances, **8-10** shooting, charging and melee, **11-12** terrain, **13-15** every
special rule (`Aircraft` through `Flying` on 13, `Furious` through `Thrust` on 14, `Tough`,
`Transport` and `Unstoppable` on 15), **16** pre-made rule packs, **17** terrain placement styles,
**18-19** terrain generators, **20** army terrain, **21** solid buildings, **22-24** deployment
styles (the zone dimensions are in the diagrams, not the text), **25-27** missions and **28-29**
side-missions, **30-36** the advanced modules, which the rulebook groups as extra actions,
boosting, advanced casting with relic wargear, and hero and monster feats, **50** multiplayer.

Every printed page from 15 onward is one higher than in 3.4.1, and the special rules grew from two
pages to three, so a citation taken from an older document is wrong by at least one.

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
