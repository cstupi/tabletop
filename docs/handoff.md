# Handoff — 2026-08-28

Where the project stands, and what to pick up next.

## State

Tree is clean; everything is committed. No code has been written beyond the Godot
scaffold. Three planning sessions have happened.

| When | Produced |
| --- | --- |
| 2026-08-24 | Repo restructure, `docs/architecture.md`, seven workstream prompt sketches |
| 2026-08-27 | [Table domain model](superpowers/specs/2026-08-27-table-domain-model-design.md) |
| 2026-08-28 | [Game flow](superpowers/specs/2026-08-28-game-flow-design.md) |

## The mode — read this before proposing anything

**We are still ideating. Do not write buildable specs and do not freeze contracts.**

The 08-24 session produced workstream prompts and a "contracts to freeze before parallel
work" list. Those were premature and are on hold. Freezing an interface is only worth
doing when the model behind it has stopped moving, and it has not.

The mode the last two sessions established, and the one to continue in: **decide meaning,
defer expression.** Both design docs deliberately refuse to say how anything is expressed
in C#, how it persists, or what the wire format is. That is not an omission to fix later
by adding detail — it is what makes them cheap to change.

**The workstream prompts in `docs/workstreams/` are sketches. None should be run as
written.** They predate both design sessions and describe a data model that no longer
matches. `01-contracts` in particular was flagged as the weakest and most blocking, and
the correct response to that is not to strengthen it yet.

## What is designed

**`docs/architecture.md`** — settled, and the shared context every session reads. Thin
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

Two things govern the rest: **this is a tabletop simulator**, so nothing recorded is an
invariant and every property is editable from a menu through the same write path
automation uses; and **players count their own points**, so there are no victory
conditions.

## What is not designed

Resolution — dice, wounds, modifier stacking, the shooting and melee pipelines. This is
the largest remaining piece and it sits inside an activation, so game flow had to come
first.

Army import and mesh binding — Army Forge import, how a list freezes into a snapshot, and
binding granularity. Independent of everything above; it touches no game.

Management, assets, and the web app. Untouched since the 08-24 sketches.

## Open questions

Carried from the two design docs:

- Game lifecycle: created, in progress, abandoned, finished, and what a disconnect means.
- Whether setup configuration can change once a game is loaded. Pinning answers it for the
  rule pack; the rest is undecided.
- What state a model returns in when a mechanic brings it back off the shelf. A rules
  question, but the model has to support the answer.
- Whether an objective's game data is generic or system-specific.
- Binding granularity — unit-level, model-level, or unit default with per-model override.
  Third option recommended, not decided.
- `Layout` and `Mission` — Layout is a user-owned table, but a mission may be rule-pack
  data rather than a row. Depends whether users author missions.
- Triangle-count guardrail at upload — agreed in principle, threshold not chosen.
- Who instantiates a game session when a lobby room starts. Not blocking anything.

## Next

**Resolution** is the natural next module, for the same reason game flow followed the
table: it is the thing every remaining decision waits on. **Army import and binding** is
the alternative — smaller, more mechanical, and genuinely independent, so it can run any
time.

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
