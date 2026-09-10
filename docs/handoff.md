# Handoff — 2026-09-09

Where the project stands, and what to pick up next. Rewritten at the end of the session that closed
the last open questions: all twenty-five are decided, and three defects surfaced while propagating
them are fixed. The session before it cleared the plans' debts — the joined-hero mechanism written,
all 98 spec corrections applied. **Design, planning and cleanup are all complete. The only thing
left is to build it.**

## State

Tree is clean. **No code exists** beyond the Godot scaffold: `client/Tabletop.csproj`
and `client/src/Main.cs` are the only source files, and `server/`, `libs/`, `web/`, `sim/` and
`viewer/` are empty or absent. Every module is designed, planned and documented. Nothing is
implemented.

| Layer | Where | Size |
| --- | --- | --- |
| Design sessions | `docs/superpowers/specs/`, 24 files | the requirements |
| Implementation plans | `docs/superpowers/plans/`, 10 files | 38,325 lines |
| Engineering design docs | `docs/design/`, 10 folders plus an index | 15,145 lines |

The specs are now level with the plans. Every correction a plan proposed to a spec, to
`docs/schema.md`, `docs/data-model.md`, `docs/architecture.md` or to a sibling plan has been
applied, so a document no longer disagrees with the plan that reads it.

**Read in this order from cold:** this file, then `docs/design/README.md`, which is the system
design document and defines the vocabulary. Then `docs/architecture.md`. Then the design document
for whatever you are touching, and its plan beside it.

Precedence, when two documents disagree: **the specs settle behaviour, the plans settle what gets
written, the design docs settle structure.** A later spec beats an earlier one. Where a design
document disagrees with its plan, the plan is what an implementer follows.

Four specs sit above the rest, newest last:

1. `2026-09-04-whole-design-review.md` beats every spec written before it, which was all of them
   at the time.
2. `2026-09-06-simulation-design.md` postdates the review and is not subordinate to it. It also
   covers two modules the review never saw.
3. `2026-09-06-ruleset-3-5-1-design.md` beats both, on the one subject it touches: the ruleset.
   See *The ruleset moved to 3.5.1*.
4. `2026-09-09-open-questions-design.md` is newest, and beats every one of them on the twenty-five
   questions it names and on nothing else. See *The open questions are closed*.

## The mode

Design and planning are both complete. **The next thing is execution**, starting with
`superpowers/plans/2026-09-05-contracts-and-local-dev.md`, which everything else consumes.

Execute with the `superpowers:subagent-driven-development` or `superpowers:executing-plans` skill,
as each plan's own header says. Build order is in `docs/design/README.md`: contracts, then geometry
and the management API and the game server skeleton in any order, then the web app, the client, and
the two rules modules. The simulator and the viewer come last and repay early, because the rules
modules' full-game tests run through the simulator.

**Run the management API before the game server.** The build graph allows either order, but the
game server plan's `CLAUDE.md` edit replaces text it labels as the management plan's, so the two
are not interchangeable. **And run the interpreter before the scheduler**: `docs/design/README.md`
→ *Build order and why* says so, the scheduler consumes the interpreter's `RulesEngine`,
`ISchedulerFactory`, `ISequenceStarter` and `SequenceStart`, and its `CLAUDE.md` step assumes the
interpreter's bullet text is already there.

**Three things to know before the first task.** The plans' *Spec corrections proposed* sections are
now history rather than work: their *Before* quotes no longer match the documents they quote.
`plans/2026-09-05-interpreter-and-effects.md` grew a mechanism after it was written, so read Task 7,
12, 13 and 14 as they now stand rather than trusting a summary of them. And every plan's *Questions
for the project owner* section is now closed end to end — no plan proceeds on an assumption any
more, and six of the answers changed the plan they sat in.

### The one debt the plans still carry

- **The `CLAUDE.md` edits are a chain, and the hazard is structural.** All ten plans' last task
  edits the file, though geometry's and the scheduler's task titles do not say so. Nine of them
  quote a predecessor's exact wording as their *Before*, so an edit landing out of order, or a
  change made to the file directly, silently breaks a later plan. An audit on 2026-09-06 found
  three that would have reverted an earlier plan's wording, one of them the client's platform
  correction. Re-audited on 2026-09-08 and again on 2026-09-09 against the file as it now stands.
  The second audit found the closing-note chain **did not compose**: the game server plan quoted a
  narrowing to "`server/game/` and `web/`" that the management plan never wrote, and the simulation
  and viewer plans both narrated a deletion by the web app plan that its Task 17 did not contain.
  Both edits are written now, so the note goes contracts → management → game server → web app
  (deleted) and the last two plans find the one-sentence note they quote. The opening-paragraph
  chain, the `libs/` parenthesis, the `server/game/` and `tests/` bullets, the Conventions sentence
  and the two developer-tool stubs all composed on both audits. Check a plan's *Before* text
  against the file rather than trusting it.

## The spec corrections are applied

**2026-09-08.** All 98 were worked through and written into the documents they name, in five passes
split by target file so that no two passes wrote the same file. Each plan's *Spec corrections
proposed* section is kept as the historical record of what was asked and why, and each now carries a
note saying so, because a *Before* quote in one of those lists no longer matches the document it
quotes.

The count was 98 exactly. 84 were bare under a strict reading and 14 carried a recorded outcome; the
remembered figure of seventy-eight bare is reachable by also counting the eight that cite a
2026-09-06 ruling while still proposing an unmade edit.

Sixteen turned out to be already applied, seven of them the schema work done earlier the same day.
Five did not apply as written: `contracts #8` was folded into `client-shell #9`, which replaces the
same sentence and is a superset, with #8's one distinguishing clause carried across because #9
otherwise leaves *"The Ask"* with no antecedent; `contracts #13` is a strict subset of
`game-server #8`; `geometry #4` and `game-server #3` are word for word identical and were written
once; `simulation #1` was withdrawn by its own plan after the stored-seed ruling. Four more were
struck before the pass began.

**`contracts #9` applied only its `docs/architecture.md` half.** Its `CLAUDE.md` half would have
broken the ten-plan quote chain, and the correction's own text says that file is Task 16's. So
`architecture.md` lists `tests/` under repo-level support and `CLAUDE.md` does not, until the
contracts plan runs. That divergence is deliberate: do not close it by editing `CLAUDE.md`.

**`game-server #7` is propagated in three of four places.** The fourth,
`plans/2026-09-05-management-api.md` line 18, says the game *server* owns the `Properties` catalog
types rather than naming a plan, which is the service boundary and is right as it stands.

Three corrections proved wrong about the documents they described, and each was corrected rather
than applied blind: `simulation #9` misquoted the scheduler spec, `management #5` sat beside a
sentence asserting the opposite of what it added, and `game-server #4` quoted a *Joining* sentence
its own change made false.

**Two loose threads the pass left, both worth a decision before the module that owns them is
built.** `client-shell #8` was applied but is contingent: its plan offers an alternative in the same
breath, putting the `Properties` action names on `GameInfo`, and taking it makes that spec sentence
wrong. And the setup-phase spec still lists *"Which socket frames carry which message kind"* as
undecided while the client-shell spec now records it settled, because `client-shell #7` struck only
one of the two copies and no correction targets the other.

**Also cleared on 2026-09-08.** `docs/schema.md` had six columns the contracts plan's SQL has and it
did not: `participant.game_id`, `participant.army_id`, `game.rng_seed`, `blob.uploaded`,
`blob.content_type` and `effect.target_stat_level`, the last of which a foreign key the document
promises cannot exist without, since `stat_definition`'s key is `(properties_hash, level, name)`.
All six are in the diagram now, with prose for the seed, the participant's two links and the blob's
upload state, and the open item on one database versus two is closed for local development.
`CLAUDE.md` and `docs/architecture.md` undercounted the deliverables at three against a ruling made
on 2026-09-06; both now name five, `CLAUDE.md` carries a stub bullet for `sim/` and `viewer/`, and
`architecture.md`'s table has six rows and an audience column. The simulator plan's Task 11 owned
those edits and now verifies them instead, its Step 2 replaces the `sim/` stub rather than inserting
a bullet, and the viewer plan's *Before* quotes the stub it will replace.

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

## The ruleset moved to 3.5.1

Everything in this repository was designed against Grimdark Future v3.4.1. The owner supplied
v3.5.1 for both games on 2026-09-06. It is more than a version bump, and the rebase is propagated
in commits `9e64b46`, `14fdd04` and `c320364`, with
`superpowers/specs/2026-09-06-ruleset-3-5-1-design.md` as the record.

**The two books converged.** At 3.5.1 the special rules are word for word identical between
Grimdark Future and Age of Fantasy, except that Age of Fantasy has Command Groups (Sergeant,
Musician, Banner), Grimdark Future has `Aircraft` and `Transport(X)`, and Age of Fantasy runs a
75% points scale: 750 and 1500pts recommended, one hero per 375pts, one unit per 150pts. One rules
implementation for both game systems is therefore confirmed by the text rather than assumed. An
earlier finding in the same session that the books had diverged was an artifact of comparing 3.4.1
against Age of Fantasy 3.5.0.

**Five rules were renamed or removed**, which reached 189 references across 17 files:

| 3.4.1 | 3.5.1 |
| --- | --- |
| `Sniper` | `Takedown`, and *"Shoots at Quality 2+"* is gone |
| `Entrenched` | folded into `Artillery`, which is `Immobile` plus two range-banded hit modifiers |
| `Lance` | `Thrust`, word for word |
| `Poison` | `Bane`, reworded to one effect |
| `Lock-On` | gone. `Unstoppable` is the new `suppress_modifiers` case, but does not ignore cover |

`Surge` and `Unstoppable` are new, so the catalogue holds 33 entries rather than 32. The
identifiers followed their rule: `GfNames.Takedown`, `IsTakedownGroup`, `TakedownModel`, and the
`takedown_target` Ask, which is on the wire and reaches generated TypeScript.

**Seven rules kept their name and changed their text.** `Fearless` and `Regeneration` need every
model to carry them, `Relentless` triggers on shooting over 9" away rather than on a Hold action,
`Rending` is additive `AP(+4)`, `Scout` deploys *"fully within 12" of their deployment zone"*
rather than 12" from its own position, `Tough` assigns heroes wounds last *"even if already
wounded"*, and `Aircraft` lost its -1 to hit and keeps only -12" to range.

**Four changes fall outside the special rules.** Multiplayer drops the per-round roll-off, so
`GfRules.FirstTeam` lost its team-count branch and three players order the way page 7 orders two,
with the deployment roll-off untouched. A Shaken unit may strike back counting as fatigued, and
may not cast or use any active special rule. Page 11 gained a 3" coherency allowance across
elevations. Page 13 gained a same-time rules-priority tie-break, which `ordinal` deliberately
does not implement, because a deterministic order is what replay needs.

**Every printed page from 15 onward moved by one**, and the special rules grew from two pages to
three, so `Furious` and `Hero` moved to 14 while `Tough`, `Transport` and `Unstoppable` moved to
15. That was 117 mechanical citation shifts plus 113 reassignments of a page 13 or 14 citation to
the page its rule now sits on. All 272 citations to pages 13 through 15 were then verified against
the rule named beside them.

**Not applicable yet.** Every objectives and scoring change lands on ground this project has not
built: *seized* became distinct from *control*, a dropped marker is placed within 1" by the
opponent, and six side-missions moved from *"3+ models"* to *"5+ models"*. The repo places markers
and scores nothing, so the spec records those for whoever designs missions.

## Rulings of 2026-09-06

The owner ruled on roughly forty questions across two sessions that day. They are applied across
the plans and the design documents, and this is the index, because they are otherwise scattered.
Where a ruling overturned an assumption the plans were already written against, that is marked.

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
| Teams are locked at the start of play; a player rejoins their slot | The specs contradicted themselves on this and it kept resurfacing. Corrected in place across four specs, with the canonical statement in `superpowers/specs/2026-08-28-game-flow-design.md` → *Teams are frozen at the flip, and a team is a slot*. A sixth document, the scheduler spec, was still contradicting it until an audit that day |
| The game server seeds the ruleset content, and the schema boundary is not a wall | Confirmed, and the prose treating a write as a border crossing was removed. Row ownership by game status stays, because hydration depends on it |

### The rules

| Ruling | Note |
| --- | --- |
| One try per spell resets per activation | Confirmed |
| Attack counts, and the `Blast` cap, are settled before any group fires | **Overturned.** A group firing after casualties rolls its original count |
| A unit cannot enter an enemy transport | Confirmed. Page 15's clause about occupants targeting the transport itself is therefore unreachable, since valid targets are enemies only |
| A transport survivor with nowhere legal to stand is destroyed | **Overturned.** It was shelved unwounded; it is now shelved with wounds at capacity and counted on the activation's tally, which is what makes it a casualty rather than a stored model |
| Sight is centre to any part of the target | **Changed.** The eye is the body's centre. The target is sampled on three rings, base, mid-height and top, which bounds an error that was unbounded vertically |
| Cover is a fraction of the unit | **Changed twice in one day, landing on page 11's own word.** A *majority* is needed, which is strictly more than half, so five of ten models inside cover terrain gives nothing. The first ruling said half or more; the book says *"the majority of models in a unit"* and governs. The other clause, fewer than half the models visible, already meant majority-hidden and needed no change, so both clauses now use one threshold. A single model uses its base centre. Geometry reports per-model facts and the ruleset applies the threshold |
| A team allocates its token spend across its own models | **Overturned.** Was a total the server took from the nearest models. The shape is a map from model to its own cap, with no total |
| The caster may spend its own leftover tokens on its own roll | **Overturned.** A caster inside a transport therefore modifies its own roll, where it used to roll a flat 4+ |
| `Takedown` picks which model takes the wound, per model | **Overturned, then overtaken, then ruled again.** The rule was `Sniper` in 3.4.1 and the first ruling gave a group one pick between its models, because *"each model with Sniper"* met a pooled roll. 3.5.1 renamed it `Takedown` and rewrote the clause as *"This model may pick"*, so the pick is **per model**: `GfShooting.Groups` splits a takedown weapon type into one group per model, each with its own pick, roll and wounds. A takedown wound skips only what building the assignment prompt decides: free choice of who dies, wounded `Tough` first, heroes last. `Deadly`, `Regeneration` and `Tough` as a capacity still apply, and wounds past that capacity are lost |
| `Artillery` aggregates `all` | Ruled after `Stealth`, which is the same to-hit penalty at the same range band and reads *"units where all models have this rule"* in both books. `Artillery` inherited `Entrenched`'s problem when 3.5.1 replaced it: the -2 is written per model while a hit roll is pooled per weapon group, and no hit belongs to a target model until the defender removes casualties. Cover's majority was the alternative, and `any`, the plans' old reading, the loosest |
| `Fearless` needs every model to carry it | Ruled before 3.5.1 was read, and confirmed by it: both books now say *"a unit where all models have this rule"*, where 3.4.1 said *"most models"*. `Regeneration` reads `all` for the same reason |
| A joined hero attacks at its own Quality | **Overturned.** p.4 defines a Quality test against *"the model's Quality value"*, and the Hero rule overrides `Defense` alone. The clause that settles it is the same rule's *"may take morale tests on behalf of the unit"*, which changes nothing if the hero already used the unit's Quality. It blocks on the unit's `Defense` until every other model is dead, which is the Hero clause's own sunset and is cheap once the unit holds one model. It takes the unit's morale tests whenever its own Quality is better, which resolves the clause's *"may"* as always-when-it-helps and needs no Ask. The mechanism was written on 2026-09-08; see *Rulings of 2026-09-08* |
| A disembarking model may take any facing that fits | **Overturned**, over the container's yaw. p.7: *"Models may move and turn in any direction regardless of their facing"*, and a unit exits by using any move action, so a placement is legal if some yaw fits. Only oval bases and models with no base are affected. `Location.Inside` gains no yaw, because nothing is preserved |
| An unreadable turn answer follows the enforcement dial | **Overturned.** Was always a pass. In practice it bites only for a ruleset that offers an id it cannot read |
| The deployment roll-off is gated like any other roll | **Overturned.** It became two steps, because editing dice rewrites the roll record but never a completed step. It is the one gate that ignores the between-steps wait setting. 3.5.1 narrowed its consumer to deployment order, and it still has to yield the whole descending order |
| The round does not advance on its own; a player ends it | **Overturned.** Any player on any team answers the boundary Ask. This also removed the need to handle a round nobody can act in |

### Content and platform

| Ruling | Note |
| --- | --- |
| A joined hero is a model of the unit it joined | **Overturned.** Its stats land on its slot. A `unit`-kind rule on that slot is the whole unit's, read off the definition rather than the living models, so a captain with `Ambush` sets a ten-model squad aside. The alternative was for a joined hero's own `Ambush` and `Scout` to do nothing, with no diagnostic anywhere |
| Height scales from the published base | **Overturned.** Was a flat 1.5 inches |
| Both Grimdark Future and Age of Fantasy import | **Overturned.** One rules implementation, now confirmed by 3.5.1's text rather than assumed. **No Age of Fantasy content set exists**, so such a list imports before there is anything to play it with |
| The table origin is a corner | Ratified, having been ruled by a compatibility review rather than by the owner |
| Side Battle's zone scales with the table; small tables are out of scope | **Changed**, and it contradicted a spec. Side Battle is the only proportional style; on a table near the standard size every style agrees anyway |
| The client takes no Windows-only dependency | **Changed.** `architecture.md` said Windows outright. The stored session is a file created owner-only, not Windows data protection |
| A minimal built-in art set ships | **Overturned.** Ten grouped meshes and five textures. These are asset files a person must make; the hitbox-first drawing is the fallback |
| A roll is shown as a readout, not as dice objects | Confirmed |
| Terrain generator alternatives are picked at random | Confirmed |
| Zones for team ordinals with no team are drawn grey | Confirmed |
| The game list filters by status, name and created-by-me | Confirmed |

## Rulings of 2026-09-08

| Ruling | Note |
| --- | --- |
| p.11's cover majority is frozen before any group fires | The cover ruling of 2026-09-06 settled the fraction and said nothing about the timing. p.8's *"all weapons are fired simultaneously"* settles it, and it is the same clause that settled the attack counts and `Blast`'s cap on question 3. `fire-groups` records `in_cover_<i>` through the new `GfAttack.InCoverAgainst`, `determine-attacks` carries it down as `in_cover`, and `roll-to-block` reads it there. Positions do not change between groups, so only the count was ever live. Judging it live at `roll-to-block` was the alternative, which let an earlier group's casualties shoot the unit out of cover. **No live read of the target's size is left in the shooting path**, and all three keys fall back to the live reading for a push that recorded nothing, which is every melee push and every spell's hits |
| The owners of a model determine how its wounds are distributed, and the owner is the target unit's team | Confirmed. p.8 gives the defending player free choice of removal order, and `assign` already opens its `Allocation` to `ctx.TeamOf(target)`. A unit knows its owning participant in `UnitInPlay.ParticipantId`, so addressing that participant alone was the alternative and was declined. Asks address teams, and one answer per team is what the dispatcher enforces, so in a team game a teammate may remove another player's models |

A correction fell out of checking it. `Tough`'s ordering clauses are nested rather than parallel:
p.15 writes *"heroes must be assigned wounds last, even if already wounded"* as a parenthetical
inside *"you must continue to put wounds on the tough model with most wounds"*, which makes it an
exception to that clause. `GfAttack.EligibleForNextWound` read the two independently and returned a
wounded `Tough` hero as the sole eligible target while unwounded models stood. 3.5.1 added *even if
already wounded* and the rebase carried the phrase into the prose without changing the function.
Heroes are now deferred before the `Tough` ordering is read, with a regression test in
`superpowers/plans/2026-09-05-interpreter-and-effects.md` and the prose corrected in
`design/interpreter/opr-sequences.md`. The rule's other ordering clause, *"if a model with tough
joins a unit without it, then it is removed last"*, needs nothing: p.14 lets only Heroes join a
unit, so heroes-last already covers it. One stale citation was fixed beside it, a `p.14` for
`Transport` in the `deal-wounds` diagram in `design/interpreter/design.md`, left by the page shift.

### The joined-hero mechanism, written 2026-09-08

The largest un-designed consequence in the repo, and a debt because the ruling had reached every
document and no code. Written in `plans/2026-09-05-interpreter-and-effects.md` Tasks 7, 12, 13 and
14, and in `design/interpreter/`.

**A Quality test belongs to a model, so the hit roll splits by Quality value rather than by weapon
group.** `StatReader.TargetNumber` took `StatLevel.Unit` only and threw for anything else; it now
takes an optional model, reads that slot's text and falls back to the unit's, so an ordinary squad
reads one number whichever ref is used. Its `replace_stat`/`modify_stat` scan matches on the stat's
**name** at either level, because a target number is one stat with a declaration at each level and
every catalog row names the unit-level ref. That is what keeps `Reliable`'s *"attacks at Quality 2+"*
over a hero's own value. `GfRolls.HitQuality` names p.4's value per model and returns 6 for a
fatigued melee, since p.9 replaces the value rather than modifying it and collapses a unit to one
pool. `GfAttack.Attacks` sums per Quality value, ascending, so the dice cursor does not depend on
model order.

**The Hero clause's sunset is a model count, and it is the frozen one.** `GfRolls.DefensePlan` takes
`defenderModels` and reads the surviving model's own slot `Defense` only when that count is one,
which for a slot without one is the unit's value again, so it branches on no rule name.
`defender_models_<i>` **replaced `blast_cap_<i>`**: p.13's *"as many hits as models in the target
unit"* and p.14's *"until all other models have been killed"* count the same models at the same
moment, and the old name fitted only one of its two readers.

**`GfRolls.MoralePlan` takes the best of the unit's `Quality` and every living `Hero` model's own**,
one `Min` and no Ask. p.14's *"even if the hero is the last model remaining, it takes morale tests as
the unit"* is about the starting-size comparison that separates Shaken from Rout, so
`GfHooks.AtHalfOrLess` is untouched.

**The wire needed nothing.** `StepCompleted.Rolls` was already a list, `RollRecord` carries its own
`Threshold`, and `RulesEngine.PlanOf` rebuilds a plan from the record, so a reroll in one pool is
judged at that pool's threshold. `ModifierAdded` names a roll by name and is pending, so a player's
+1 to `hit` reaches every pool, which is right. Three readers of a single hit roll became readers of
several: the client's log-panel dice controls and its `Animator` readout both read `Rolls[0]`, and
`Sim.Recording.LogLines.Dice` with the viewer's TypeScript port of it printed a roll's name without
its threshold, which made two hit pools in one step indistinguishable in a recording's `line`. The
viewer's `RollPanel` already iterated the list and already showed each threshold.

Two document defects fell out of the work and are fixed. `effect-grammar.md` and the interpreter
plan's Task 6 still asserted that neither `Quality` declaration is read and that a last-standing
joined hero blocks at its host squad's defence. And `design/interpreter/design.md` claimed *"Eight
are closed by owner ruling: 1, 2, 3, 6, 7, 9, 12 and 14"* while eleven rows were marked closed; it
now reads eleven and names 5, 10 and 11 as the three still open.

## The open questions are closed

All twenty-five, on 2026-09-09. `superpowers/specs/2026-09-09-open-questions-design.md` is the
record and beats every earlier spec on these questions alone. Every plan's *Questions for the
project owner* section now carries its answer, so **no plan proceeds on an assumption any more**.

Ten were never open — a document already settled them and the plan that asked never wrote the
answer down. Four were closed as assumed. Twelve went to the owner, of which six confirmed the
assumption and change nothing: the weapon cursor, click-to-embark, the tabbed viewer panel, the
72×48 grass draft default, newer-socket-replaces-older, and a `stuck` run failing CI.

**Six answers changed a plan.** The rest are in the spec:

| Change | Where it lands |
| --- | --- |
| p.11's elevated 3" coherency is applied to every cross-elevation pair, its precondition never tested | `Coherency.Check` gains an `elevatedChainDistance`; `maxSpread` untouched. Closes the 3.5.1 rebase's second open question |
| `Thrust` grants `AP` rather than modifying it, as `Rending` does | The catalog entry's second effect becomes `grant_property`, so a melee weapon with no `AP` gains 1. `ModifyParameter` keeps `Counter` as its example. Closes the last residue |
| `GET /v1/games` is newest-first | `id < @after order by id desc`; the keyset cursor is unchanged |
| Textures accept `image/webp` | `AssetRules.ContentTypes`, the `002` constraint, `schema.md`, and the web app's intake |
| The simulator ships six scenarios and can declare its own spells | `casting` joins the five; `ScenarioBuilder.Spells` folds entries into the pinned `PropertySet` for one run, because the shipped content holds no spells |
| Garrisonable buildings stay entirely manual | Geometry question 8 is *no*: no `WorldVolume` overload, no exit region. Most terrain buildings are open, and the players agree how a garrison works at the table |

### Three defects the propagation found

None was on a question list. Each is fixed.

- **The disembark-facing ruling had reached no code.** `2026-09-06-ruleset-3-5-1-design.md` §4
  ruled *any facing that fits* over the container's yaw, and reached that spec and this file and
  stopped: `MoveRuler.Admits`, `AskPanel.YawOf`, the client plan's question 8 and three rows of
  `design/client/design.md` all still assumed the container's yaw. The **server was never wrong** —
  `GfMoves.ExitPlacement` checks the placement the client proposes — so nothing caught it.
  `MoveRuler.ExitYaws` is 8 and `ExitYawAt` answers with a facing that fits; a **circular base tries
  one**, so every ordinary infantry model costs what it did before.
- **The default base was wrong for every model it applied to.** An entry with no published base got
  `Base.Circle(32/25.4)`. Army Forge publishes `bases.round` reliably for infantry, so a missing
  base means a vehicle, and a tank measured from a 1.26" circle at its hull centre put its whole 6"
  disembark region — and its movement, cover and line of sight — inside itself. The default is now
  `DefaultBody`, a 221 × 119 × 113 mm hull, carrying its own height because `HeightFromBase` would
  have scaled 221 mm into a 13" tall tank.
- **`BaseShape.Square` could not express a hull.** A square is a rectangle, so the enum held the
  special case and not the general shape. **`Square` is replaced by `Rect(length, width)`**; a
  square is `Rect(s, s)`, and `A` is the length along the facing, matching `Oval`. This is
  wire-level: the `Protocol` enum, both check constraints, `Bodies.BaseOutline`, ten geometry
  tests, the client's `BoxMesh` arm, the web app's union, presets, picker and labels, the
  simulator's and viewer's `RecordedModel.BaseShape`, the viewer's ported `baseOutline`, and three
  specs. Army Forge's own `bases.square` field is their data and is untouched.

### Residues, not open questions

Each sits inside a question the owner already closed, so no plan's question list will surface them.

- Interpreter question 7 is closed twice over, but `Takedown`'s *"resolved as if it was a unit of
  [1]"* is still not modelled: cover, `Blast`'s cap and the defence roll read the whole target
  unit, and only the wound is redirected.
- Interpreter question 3 is closed, but `Impact` and melee still read live model counts mid-action,
  deliberately, because page 9 sequences melee by hand.
- Interpreter question 8 is closed, but the end-of-turn morale test's die is still unreachable from
  the menu: it is a single-step frame with an actor, so it pops before anything can gate it.
- Interpreter question 10 is closed, but `SpellTargets`' model-grain branch measures per model.

**Deferred by design**, each recorded where it belongs: hosting; buildings as shootable targets
and army terrain; action boosting; Advanced Casting's optional modules; missions, side-missions
and scoring; a real spell list, which belongs to an army book; and everything waiting on a second
ruleset, which is Warhammer rather than Age of Fantasy, since the two One Page Rules games share
an engine.

## `docs/workstreams/` is superseded

Seven prompt files and a README, from before the first design session, describing a data model
that no longer exists. Until 2026-09-06 its README told every session to read it first and marked
five workstreams ready, which is a second execution scheme competing with the plans. It now carries
a superseded banner, each prompt file names the plan that replaced it, and `docs/architecture.md`
no longer implies any of it is current.

It was kept rather than deleted because fourteen documents outside it cite those paths as the
sketch they departed from: four design documents, six plans and three specs, plus this file.
**Execute nothing from it.**

## How to work here

Nine things, most of them learned the hard way in a session.

- **Pin the rulebook version before you read it, and check for a newer one.** The whole repository
  was designed against Grimdark Future v3.4.1, and v3.5.1 renamed five rules the plans were built
  on, removed two and moved every page from 15 onward. A second lesson came free: comparing
  Grimdark Future 3.4.1 against Age of Fantasy 3.5.0 made the two games look like they had
  diverged, when the books were simply at different revisions. Compare like with like.
- **Read the rulebook; do not reason about the rules.** Eleven sessions running, reading has
  produced better answers than designing, and it has twice produced an answer from a clause nobody
  connected to the question: the Hero rule's *"may take morale tests on behalf of the unit"* is a
  no-op unless the hero's Quality is its own, which is what settled a joined hero's quality after
  a whole session had treated it as a judgement call. When a question is put as a menu of options
  and the book answers it, the menu is the mistake.
- **Name a mechanic in the rulebook's own words.** Casting's token spend was called a "sealed bid"
  and reached over three hundred occurrences across twenty files, including a wire discriminator
  and generated TypeScript, before the owner caught it. Measurable at `4b2e56b~1` if you want the
  exact figure; three different greps gave three different answers, which is its own lesson. There
  is no bid in the rules. Quote the rule, then name the thing. The rename cost is real: `Sniper`
  becoming `Takedown` moved 189 references across 17 files, and that was a rename the book made.
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
- **A version rebase must re-read the C#, not just the citations.** The 3.5.1 rebase verified 272
  page citations and every prose assertion, and left three defects in the code blocks inside the
  plans. `Tough` gained *"even if already wounded"* and the prose took it while
  `GfAttack.EligibleForNextWound` kept reading the two ordering clauses independently, so a wounded
  hero was the sole eligible target while unwounded models stood. `Regeneration` became aggregation
  `all` and `assign` kept filtering for a per-model instance that `LiveProperties.Gather` no longer
  produces, so the rule never rolled at all and the plan's own test would have failed against the
  plan's own code. `Takedown`'s rewrite reached the plan and the handoff but not
  `docs/design/interpreter/`, which still described `Sniper`. Prose and code drift apart because
  grepping for a rule's name finds the prose.
- **A proposed correction is a claim about a document, not a fact about it.** Three of the 98
  misdescribed their target: one misquoted the sentence it meant to replace, one sat beside a
  sentence asserting the opposite of what it added, and one quoted text its own change made false.
  Read the target section before applying, and fix what the correction makes untrue around it.
- **Split parallel document edits by target file, never by source document.** The corrections were
  proposed per plan and target twenty-five files, so applying them plan by plan would have had five
  workers writing the same specs at once. Bucketing by target made the passes disjoint, and the
  three corrections that straddled a boundary were all already resolved.

## Environment

Checked 2026-09-06. WSL `dotnet` 10.0.400 on PATH; Docker 29.7 and Compose v5.4; `./venv` has
`pypdf` for the rulebook; Godot 4.7.2 Mono on the Windows host, opened with `./scripts/godot -e`.
No `pdftotext`, and headless Chrome cannot launch (missing `libnspr4`), so the rulebook's diagrams
are read by extracting images with `pypdf` and Pillow.

Stytch: project `TabletopWar`, vertical CONSUMER, test project
`project-test-437432c7-798d-49d2-9dc4-eff103937e0f`. A live project exists and is unconfigured;
nothing uses it. Its MCP tools are project configuration only, with no user management, so the
Consumer user schema has to come from the docs. **Two pieces of Stytch configuration are
load-bearing and not in the repo:** the `SESSION` claim template mapping `{{ user.external_id }}`
to `account_id`, whose absence takes both services down with no error from Stytch; and a
registered redirect URL for password resets.

### The rulebooks

Grimdark Future v3.5.1 and Age of Fantasy v3.5.1 are at `/mnt/c/Users/clays/Downloads/`:

- `GF - Advanced_Rules_v3_5_1.pdf`, 71 printed pages
- `Age_of_Fantasy_-_Advanced_Rules_v3_5_1_-_Print_Friendly.pdf`, 70 printed pages

The 3.4.1 and 3.5.0 files sit beside them and are superseded. Nothing should be read from them
except to check what a citation used to mean.

```bash
./venv/bin/python -c "
from pypdf import PdfReader
r = PdfReader('/mnt/c/Users/clays/Downloads/GF - Advanced_Rules_v3_5_1.pdf')
print(r.pages[12].extract_text())"        # prints printed page 13
```

`pypdf` indexes from zero and the page numbers below are the rulebook's printed ones, so
`r.pages[N]` is printed page `N + 1`. Every citation in this repository uses printed numbering.

Useful pages in Grimdark Future v3.5.1: **4-5** the most important rule, unit stats, dice, Quality
tests, modifiers, measuring and line of sight, **6** objectives and the deployment roll-off, **7**
rounds, actions and coherency, **8-10** shooting, charging, melee and morale, **11-12** terrain,
**13-15** every special rule (`Aircraft` through `Flying` on 13, `Furious` through `Thrust` on 14,
`Tough`, `Transport` and `Unstoppable` on 15), **16** pre-made rule packs, **17** terrain placement
styles, **18-19** terrain generators, **20** army terrain, **21** solid buildings, **22-24**
deployment styles (the zone dimensions are in the diagrams, not the text), **25-27** missions and
**28-29** side-missions, **30-36** the advanced modules, which the rulebook groups as extra
actions, boosting, advanced casting with relic wargear, and hero and monster feats, **50**
multiplayer.

Per-faction spells through the Army Forge API:
`https://army-forge.onepagerules.com/api/army-books?filters=official&gameSystemSlug=grimdark-future`
lists books with `uid`s; `.../api/army-books/{uid}?gameSystem=2` returns a `spells` array. A shared
list is `GET /api/tts?id={id}`, which is what import reads.

## Where to look at the model

Both diagrams render at <https://claude.ai/code/artifact/f5d2d64c-d819-4553-b03f-e23271ac0f63>,
generated from `docs/schema.md` and `docs/data-model.md`. **It is a copy, not a source**, and
regenerating it means pulling the mermaid block out of each document and republishing to the same
URL. It is stale: both documents gained columns on 2026-09-08, six in `schema.md` and one edge plus
one column in `data-model.md`, and the artifact predates all of it. The documents themselves are now
level with the schema SQL inside the contracts plan.
