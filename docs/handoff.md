# Handoff — 2026-09-13, end of day

Where the project stands, and what to pick up next. **Implementation has started. The contracts plan
is built, reviewed and merged; the next session launches the three plans that require only it.**

## State

Tree is clean at `main`. The contracts plan is merged (`bd10e5f`, 19 commits on the `contracts`
branch, which is kept): `libs/Tabletop.Geometry`, `libs/Tabletop.Content` and `libs/Tabletop.Protocol`
exist as the plan specifies, with `tests/` holding one xUnit project per library, `docker-compose.yml`
and `.env.example` at the root, and `scripts/postgres/init/001_schema.sql`. `dotnet build` is clean
and `dotnet test` passes 65 tests: Geometry 12, Content 17, Protocol 36. The Postgres and MinIO
containers were left running. Every other module is designed and planned and has no code:
`server/`, `web/`, `sim/` and `viewer/` are empty or absent, and `client/` is the Godot scaffold.

| Layer | Where | Role |
| --- | --- | --- |
| Design sessions | `docs/superpowers/specs/`, 24 files | the requirements; settle behaviour |
| Implementation plans | `docs/superpowers/plans/`, 10 files | what gets written, task by task |
| Engineering design docs | `docs/design/`, 10 folders plus an index | structure and vocabulary |

The documents state the current design only. Decisions carry their reason, not their history: each
plan's `## Rulings` section lists the owner's decisions by number with the rulebook or design reason,
and the design docs cite them as "the plan's ruling N". A document that needs correcting is rewritten
in place; nothing is appended as a correction note.

**Read in this order from cold:** this file, then `docs/design/README.md`, which is the system design
document and defines the vocabulary. Then `docs/architecture.md`. Then the design document for
whatever you are touching, and its plan beside it.

Precedence, when two documents disagree: **the specs settle behaviour, the plans settle what gets
written, the design docs settle structure.** A later document beats an earlier one. Where a design
document disagrees with its plan, the plan is what an implementer follows. Four specs sit above the
rest, newest last: `2026-09-04-whole-design-review.md`, `2026-09-06-simulation-design.md`,
`2026-09-06-ruleset-3-5-1-design.md` (on the ruleset only) and `2026-09-09-open-questions-design.md`
(on the twenty-five questions it names only).

## The mode

**One manager agent, and each plan on its own tree of subagents**, with
`superpowers:subagent-driven-development` inside each tree as every plan's header says. The manager's
job is the graph, not the code: launch a plan when its `Requires landed:` line is satisfied, give each
tree its own worktree, merge when a tree's whole-tree check is green, and resolve the two textual
conflicts that are expected rather than wrong — `Tabletop.sln` (re-run both trees' `dotnet sln add`
lines) and `.env.example` (keep both appends). Nothing else two concurrent trees write overlaps.

How the contracts tree ran, and what the next trees copy. The manager forked one controller per plan
with the worktree path, the model tiers, the commit-trailer rule, the hard limits (no merge, no push,
no `docs/` edits, no worktree removal) and a seven-item final-report contract; the controller ran the
SDD loop inside `.worktrees/<plan>` on branch `<plan>`. The manager's own ledger is
`.superpowers/manager.md`, git-ignored, in the main working tree; each tree's SDD ledger lives inside
its worktree and is deleted when the tree finishes, so the final report is the only record of a
tree's rulings and the manager copies them into `docs/` itself. A commit trailer names the model that
produced the commit. `dotnet sln add` rewrites `Tabletop.sln` from CRLF to LF and adds x64/x86 rows,
so the expected `.sln` conflict is textual and the LF form is what lands. A spend-limit `429` kills a
controller mid-task with its implementer; the controller resumes from its ledger when messaged, and
uncommitted files in the worktree are the dead implementer's partial work, to be finished or replaced.

The graph, from `docs/design/README.md` → *Build order and why*:

1. contracts, alone — `superpowers/plans/2026-09-05-contracts-and-local-dev.md`. **Landed.**
2. geometry, management, game-server skeleton — together. **Next: three worktrees, three controllers,
   dispatched in one message.**
3. web app, client, interpreter — together;
4. scheduler;
5. simulator;
6. viewer.

The simulator and the viewer come last and repay early, because the rules modules' full-game tests
run through the simulator.

**What the manager should not trust.** A plan's *expect N passed* is a checkpoint, not an oracle: two
counts in the game-server plan (Tasks 4 and 6) come from data-driven theories whose case counts the
plan does not show, so a disagreement there is decided by the test runner. The contracts plan's counts
were off by two at Task 14 and the runner decided.

**Merge hazards.** Three files are touched by more than one plan: `CLAUDE.md` (each plan edits only
the lines it owns and appends the rest, per the ownership rule at the top of every plan's `CLAUDE.md`
step), `Tabletop.sln` and `.env.example` (the contracts plan creates it, management adds the Stytch
lines, the skeleton appends one comment). Everything else a later plan modifies belongs to a plan it
already requires.

**The interpreter precedes the scheduler.** The scheduler consumes the interpreter's `RulesEngine`,
`ISchedulerFactory`, `ISequenceStarter` and `SequenceStart`, and its `CLAUDE.md` step assumes the
interpreter's bullet text is present. The interpreter's `IGameRules` members reference the
scheduler's seam records (`TurnContext`, `TurnAsk`, `Activation`, `HookResult`, `FirstTeamDecision`,
`ActivationOption`), so the interpreter plan's Task 8 declares them in `server/game/Scheduler/` and
the scheduler plan's Task 1 reads them.

Tasks 7, 12, 13 and 14 of `plans/2026-09-05-interpreter-and-effects.md` carry more mechanism than a
summary of them shows; read them as they stand.

## Before the management tree starts

The contracts plan's final reviewer raised five points about `001_schema.sql` and its surroundings.
Management adopts that file as its migration 001, so each is ruled before its tree is dispatched and
the ruling is carried in that tree's brief; the game-server skeleton reads the same rows. The
manager's recommendation stands beside each.

- `deployment_zone.style` holds a zone shape id and `Tabletop.Content.DeploymentZone` calls the same
  value `Shape`. Rename the column to `shape` in `docs/schema.md`, `docs/data-model.md` and the SQL,
  so the row and the record agree.
- Coordinates and yaws are `real` columns. `double precision` matches the C# `double` the values
  round-trip through; `real` loses digits on every write.
- No foreign-key column has an index. Add one per FK column that a `WHERE` in the management plan
  reads by; the plan's queries say which.
- `TABLETOP_DB_CONNECTION` in `.env.example` names no `Search Path=management` and repeats the
  password `TABLETOP_DB_PASSWORD` already carries. Add the search path and keep the repetition, since
  compose reads the one variable and the servers read the other.
- The Stytch configuration below is load-bearing and still not in the repo.

## What the contracts tree settled that the plans did not say

Obligations the contracts implementation places on later trees, beyond the plan's *Interfaces needed*
section, which mirrors the code:

- `VisualRef` carries a `JsonConverter` that rejects a present-but-invalid object and a
  `[JsonIgnore] bool IsValid`; an absent property deserializes to `default`. The game server and
  management check `IsValid` on every inbound edit and layout before writing a row.
- `EventApplier.Projection` is a new instance after every `Undone`; no consumer caches it. A
  `sequence_finished` or `sequence_abandoned` for a frame not on the stack and an out-of-range
  `DieReplacement.Index` are both `ProjectionDesyncException`, which the client answers by rebuilding
  and the game server by faulting the session.
- `WireJson.Serialize<T>` serializes with the nearest `[JsonPolymorphic]` ancestor of `T` as the input
  type, so a call typed to a concrete union member still writes `"kind"`.
- Docker Hub no longer serves `minio/*`; the compose file pulls the server and `mc` from `quay.io`.
- Deferred minors the final review triaged as not blocking merge, for whichever tree touches the
  area: `Samples.Game` and `Samples.Board` mint fresh Guids per access; `Tabletop.Protocol.Tests`
  references `Tabletop.Content.Tests` for its sample builders; the union registration tests run the
  snake_case regex over `StateChange` only, and the reviewer recommends it over all seven bases;
  `ContentJson.Configure` mutates the options it is given; `EventApplier.Log` exposes its live list;
  a wrong-typed `builtin` or `assetId` value throws `FormatException` from the converter rather than
  `JsonException`.

## The activate Ask's option label

An activation option is labelled `"{unit name}: {action}"` with `Id` `"{unitId:N}/{action}"`, as the
scheduler design and the interpreter plan's `ActivationOption.For` say. The client renders `Label`
and `Pieces` and parses neither; its "mark as having taken …" menu learns action names from
`ActionRecorded` events and `UnitInPlay.Actions` only, so it is empty until the game's first
activation, and free text covers the rest. `GameInfo` gains no `Actions` list. The contracts plan's
`AskTests` sample labels an option `"Advance"`; it is a round-trip fixture and constrains nothing.

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

The simulator plays whole games with no client, through either an in-process driver or a real socket,
and records a projection per event. The viewer draws that recording top-down, or attaches to a live
game as a spectator. Their design session is `superpowers/specs/2026-09-06-simulation-design.md`.

## The ruleset

Grimdark Future **v3.5.1** throughout; every page citation in the repository is to that edition's
printed page numbers. `superpowers/specs/2026-09-06-ruleset-3-5-1-design.md` is the record of what
the edition settles.

**One rules implementation serves both One Page Rules games.** At 3.5.1 the special rules are word
for word identical between Grimdark Future and Age of Fantasy, except that Age of Fantasy has Command
Groups (Sergeant, Musician, Banner), Grimdark Future has `Aircraft` and `Transport(X)`, and Age of
Fantasy runs a 75% points scale. Both import; **no Age of Fantasy content set exists**, so such a
list imports before there is anything to play it with. The catalogue holds 33 core rules.

**Not built, by design.** Objectives and scoring: *seized* is distinct from *control*, a dropped
marker is placed within 1" by the opponent, side-missions count *"5+ models"*. The repo places
markers and scores nothing; missions are content in a book. Page 13's same-time rules-priority
tie-break is not implemented, because `ordinal` gives replay a deterministic order.

**Known limits inside closed rulings.** `Takedown`'s *"resolved as if it was a unit of [1]"* is not
modelled: cover, `Blast`'s cap and the defence roll read the whole target unit, and only the wound
is redirected. `Impact` and melee read live model counts mid-action, deliberately, because page 9
sequences melee by hand. The end-of-turn morale test's die is unreachable from the menu: a
single-step frame with an actor pops before anything can gate it. `SpellTargets`' model-grain branch
measures per model.

**Deferred by design**, each recorded where it belongs: hosting; buildings as shootable targets and
army terrain; garrison mechanics beyond hand placement; action boosting; Advanced Casting's optional
modules; missions, side-missions and scoring; a real spell list, which belongs to an army book; and
everything waiting on a second ruleset, which is Warhammer rather than Age of Fantasy.

## How to work here

- **Pin the rulebook version before you read it, and compare like with like.** Two editions of the
  same book differ in rule names and page numbers; two games at different revisions look like they
  have diverged when they have not.
- **Read the rulebook; do not reason about the rules.** Reading has produced better answers than
  designing every time, twice from a clause nobody connected to the question. When a question is put
  as a menu of options and the book answers it, the menu is the mistake.
- **Name a mechanic in the rulebook's own words.** An invented name becomes a wire discriminator and
  generated TypeScript before anyone notices. Quote the rule, then name the thing.
- **A ruling must reach every layer.** Plans, sibling plans, specs, the design documents including
  their mermaid diagrams, `docs/design/README.md`. Verify with grep for the superseded fact, not from
  memory of what you edited.
- **State the current decision; never append history.** A document says what the design is and why.
  "Corrected on", "originally said", "ruled by the owner on" and struck-through text confuse an
  implementer and, in code-block comments, get copied into source. Rewrite the sentence in place.
- **Write for a principal engineer.** No addressing the reader, no captions explaining notation, no
  defining standard terms, no meta-sentences about the design's own importance, no em-dashes, one
  fact per sentence.
- **Ask questions in the language of the game, not of the code.** A question about which model loses
  its tokens is answerable; a question about a capacity predicate is not.
- **A version rebase must re-read the C#, not just the citations.** Prose and code drift apart
  because grepping for a rule's name finds the prose.
- **A proposed correction is a claim about a document, not a fact about it.** Read the target section
  before applying, and fix what the correction makes untrue around it.
- **Split parallel document edits by target file, never by source document**, so no two workers
  write the same file.

## Environment

WSL is Ubuntu 26.04. `dotnet` 10.0.400 on PATH; Docker 29.7 and Compose v5.4 reach WSL only while
Docker Desktop is running on the Windows host, and `docker: command not found` means it is not;
`./venv` has `pypdf` for the rulebook; Godot 4.7.2 Mono on the Windows host, opened with
`./scripts/godot -e`. No `pdftotext`, and headless Chrome cannot launch (missing `libnspr4`), so the
rulebook's diagrams are read by extracting images with `pypdf` and Pillow.

Stytch: project `TabletopWar`, vertical CONSUMER, test project
`project-test-437432c7-798d-49d2-9dc4-eff103937e0f`. A live project exists and is unconfigured;
nothing uses it. Its MCP tools are project configuration only, with no user management, so the
Consumer user schema has to come from the docs. **Two pieces of Stytch configuration are
load-bearing and not in the repo:** the `SESSION` claim template mapping `{{ user.external_id }}` to
`account_id`, whose absence takes both services down with no error from Stytch; and a registered
redirect URL for password resets.

### The rulebooks

Grimdark Future v3.5.1 and Age of Fantasy v3.5.1 are at `/mnt/c/Users/clays/Downloads/`:

- `GF - Advanced_Rules_v3_5_1.pdf`, 71 printed pages
- `Age_of_Fantasy_-_Advanced_Rules_v3_5_1_-_Print_Friendly.pdf`, 70 printed pages

Older editions sit beside them; read nothing from them.

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
**28-29** side-missions, **30-36** the advanced modules, which the rulebook groups as extra actions,
boosting, advanced casting with relic wargear, and hero and monster feats, **50** multiplayer.

Per-faction spells through the Army Forge API:
`https://army-forge.onepagerules.com/api/army-books?filters=official&gameSystemSlug=grimdark-future`
lists books with `uid`s; `.../api/army-books/{uid}?gameSystem=2` returns a `spells` array. A shared
list is `GET /api/tts?id={id}`, which is what import reads.

## Where to look at the model

`docs/schema.md` and `docs/data-model.md` each carry a mermaid diagram and are the sources. Both are
level with the schema SQL inside the contracts plan.
