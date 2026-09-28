# Handoff — 2026-09-27, paused mid step 3

Where the project stands, and what to pick up next. **Steps 1 and 2 are built and merged. Step 3's three
trees are dispatched and part-way through their plans, every commit reviewed, every worktree clean; their
controllers died to the spend limit and are resumed by dispatching fresh ones from the briefs and ledgers.**

## State

Merged into `main`, in order: the contracts plan (`bd10e5f`), the geometry plan (`ee62d19`, branch
`geometry` kept), the game-server skeleton (`862072a`, branch `game-server-skeleton` kept) and the
management API (`f0d0223`, branch `management` kept, its worktree removed). On `main`, `dotnet build` is
clean with no warnings and `dotnet test` passes 413 tests: Geometry 131, Content 17, Protocol 36,
GameServer 149, Management 80. The two server suites need the compose stack up.

**Step 3, in flight.** Three worktrees off `main` at `8b5d8b1`, one branch each, all clean, nothing
uncommitted, no report written yet:

| Tree | Worktree, branch | Head | Tasks complete | Was doing |
| --- | --- | --- | --- | --- |
| web app | `.worktrees/web-app`, `web-app` | `d05bc1b`, 10 commits | 1-9 of 17 | task 10 committed, its review not run |
| client | `.worktrees/client-shell`, `client-shell` | `32ae44e`, 12 commits | 1-8 of 16 | task 9 fix round 1 committed, its scoped re-review not run |
| interpreter | `.worktrees/interpreter`, `interpreter` | `49b6108`, 9 commits | 1-7 of 19 | task 8 committed, its review not run |

Each tree's ledger is `.superpowers/ledgers/<tree>.md` and is the resume point: its `complete` lines say
what not to redo, its rulings are binding on the rest of the tree, and its last lines say which review
was in flight. The briefs are `.superpowers/briefs/common.md` plus `<tree>.md`. Test counts at the heads:
web app `npm test` 40 passed 1 skipped; client 46; interpreter 448 across the game-server suite.

| Layer | Where | Role |
| --- | --- | --- |
| Design sessions | `docs/superpowers/specs/`, 24 files | the requirements; settle behaviour |
| Implementation plans | `docs/superpowers/plans/`, nine files and one directory | what gets written, task by task |
| Engineering design docs | `docs/design/`, 10 folders plus an index | structure and vocabulary |

**The management tree's record** is `.superpowers/reports/management.md`, seven sections: 4 holds every
ruling, 5 the parked and deferred findings, 6 the docs corrections, 7 the obligations on later trees.
Sections 6 and 7 are applied: 6 to the plan, the spec, the design doc and `docs/schema.md`, 7 to this
file. `.superpowers/` is git-ignored and local to this machine; `.superpowers/reports/geometry.md` and
`game-server-skeleton.md` are the other two trees' reports, kept for their deferred-minor lists.

**Split plans are directories.** `plans/2026-09-05-management-api/`, `2026-09-05-web-app/`,
`2026-09-05-client-shell/` and `2026-09-05-interpreter-and-effects/` are each a `README.md` (goal,
constraints, file structure, the task index, *Interfaces → Produces*, *Interfaces needed*, rulings) and
one `task-NN-*.md` per task. A reference of the form `plan → *Section*` resolves against the README.
Six task files still run 510 to 700 lines (client shell 5 and 8; interpreter 8, 11 and 14; management 5):
split one by step before editing it at length. The other six plans run 2 000 to 4 700 lines and the design
docs 1 000 to 1 700; split each the same way before a tree is dispatched on it or before editing it.

**Read in this order from cold:** this file, then `docs/design/README.md`, then `docs/architecture.md`,
then the design document for whatever you are touching and its plan beside it.

Precedence, when two documents disagree: **the specs settle behaviour, the plans settle what gets
written, the design docs settle structure.** A later document beats an earlier one. Where a design
document disagrees with its plan, the plan is what an implementer follows. Four specs sit above the
rest, newest last: `2026-09-04-whole-design-review.md`, `2026-09-06-simulation-design.md`,
`2026-09-06-ruleset-3-5-1-design.md` (on the ruleset only) and `2026-09-09-open-questions-design.md`
(on the twenty-five questions it names only).

## Next: resume step 3

1. **Bring the stack up.** Docker Desktop must be running on the host; from WSL,
   `(nohup "/mnt/c/Program Files/Docker/Docker/Docker Desktop.exe" >/dev/null 2>&1 &)` starts it, and
   `docker compose up -d` follows. `.env` exists and carries the Stytch project id. Never `down -v` while a
   tree is running: the volume holds the schema every server test reads. The compose `tabletop` database
   carries `public.schema_version` with 001 baselined and 002 applied.
2. **Resume the three controllers.** For each tree, dispatch a fresh controller on `opus` with the prompt
   "read `.superpowers/briefs/<tree>.md` then `common.md`; resume from `.superpowers/ledgers/<tree>.md`;
   the worktree is clean at the head named in the ledger; the review in flight was lost, redispatch it,
   then continue". The briefs already carry every obligation the trees owe; nothing needs re-deriving.
3. **Merge each tree when its report lands**, in any order: run the whole-tree check yourself from the
   worktree, `git merge --no-ff <tree>` from the repo root, resolve `CLAUDE.md`, `Tabletop.sln` and
   `.env.example` keep-both-sides (`dotnet sln add` for the projects), build and test on `main`, apply the
   report's section 6 to the docs and section 7 to this file, remove the worktree, keep the branch.
4. **Step 4** is the scheduler, once the interpreter has landed.

**Owner actions outstanding.** In the Stytch test project
`project-test-7a0ad7e7-906a-434a-a3d2-bf90668f9f43`: register `http://localhost:5173/reset-password`
as a `RESET_PASSWORD` redirect URL, and confirm the `SESSION` custom claim template is
`{"account_id": "{{ user.external_id }}"}`, which no tool here can read. The secret is set in this
machine's user-secrets (`dotnet user-secrets set STYTCH_SECRET` from `server/management`) and `.env`
carries the project id. Then do the management plan's Task 5 Step 8 once: a manual login through
`./scripts/management` against the real project with an emailed code, reading the JWT's claims at
jwt.io. It is the only check that proves the claim template exists; the tree's tests use a fake Stytch.

## The mode

**One manager agent, and each plan on its own tree of subagents**, with
`superpowers:subagent-driven-development` inside each tree as every plan's header says. The manager's
job is the graph, not the code: launch a plan when its `Requires landed:` line is satisfied, give each
tree its own worktree, merge when a tree's whole-tree check is green under the manager's own run, and
resolve the textual conflicts that are expected rather than wrong. Nothing else two concurrent trees
write overlaps.

How a tree runs. The manager dispatches one controller per plan, a fresh `opus` agent, never a fork of
the session's own model, and hands it a brief file
(`.superpowers/briefs/<tree>.md`): worktree path, model tiers (Haiku for implementers transcribing
code the plan already contains, Sonnet for implementers working from prose and for task reviewers,
Opus for the final review and fix-loop rounds 4-5), the commit-trailer rule, the hard limits (no
merge, no push, no `docs/` edits, no worktree removal, no `docker compose down`) and the seven-section
report contract (status; branch; whole-tree check; rulings; parked and deferred findings; docs
corrections; obligations on later trees). The controller runs the SDD loop inside
`.worktrees/<plan>` on branch `<plan>`; its ledger is `.superpowers/ledgers/<plan>.md`, git-ignored and
kept until the merge, and the report is the record: the manager copies its sections 6 and 7 into `docs/`
itself. The manager's
own ledger is `.superpowers/manager.md`. Each commit's `Co-Authored-By` names the model that produced
it. `dotnet sln add` rewrites `Tabletop.sln` from CRLF to LF and adds x64/x86 rows.

Spend limits are the failure mode. A `429` kills a controller and its live implementer mid-task;
uncommitted files in the worktree are the dead implementer's partial work. Within the same session
the controller resumes when messaged and picks up from its ledger. A new session cannot reach the old
controllers: dispatch a fresh one with the brief and the ledger path, and the ledger's `complete`
lines say what not to redo. Every subagent names its model on dispatch; the session model never runs a
tree.

The graph, from `docs/design/README.md` → *Build order and why*:

1. contracts, alone. **Landed.**
2. geometry, management, game-server skeleton, together. **Landed.**
3. web app, client, interpreter, together. **In flight**, see *State*.
4. scheduler;
5. simulator;
6. viewer.

**What the manager should not trust.** A plan's *expect N passed* is a checkpoint, not an oracle;
every tree so far has overshot it as rulings added tests, and the runner decides. The controllers'
rulings lists are exhaustive by contract, and each ends with what it costs if wrong.

**Merge hazards.** Three files are touched by more than one plan: `CLAUDE.md` (each plan edits only
the lines it owns and appends the rest), `Tabletop.sln` and `.env.example`. Everything else a later
plan modifies belongs to a plan it already requires.

**The interpreter precedes the scheduler.** The scheduler consumes the interpreter's `RulesEngine`,
`ISchedulerFactory`, `ISequenceStarter` and `SequenceStart`, and its `CLAUDE.md` step assumes the
interpreter's bullet text is present. The interpreter's `IGameRules` members reference the
scheduler's seam records (`TurnContext`, `TurnAsk`, `Activation`, `HookResult`, `FirstTeamDecision`,
`ActivationOption`), so the interpreter plan's Task 8 declares them in `server/game/Scheduler/` and
the scheduler plan's Task 1 reads them. The skeleton already ships `IGameRules`, `GrimdarkFutureRules`
(partial), `IRulesEngine`, `IRulesEngineFactory`, `GameState`, `Actor`, `EngineResult`,
`CommandDispatcher`, `DispatchOutcome`, `CommandClass` and `MenuTranslation` exactly as its plan's
*Interfaces → Produces* table spells them; with no engine registered the session runs `InertEngine`.

Tasks 7, 12, 13 and 14 of `plans/2026-09-05-interpreter-and-effects/README.md` carry more mechanism than a
summary of them shows; read them as they stand.

## What step 2 settled in the schema

Ruled on `main` before the step 2 trees were dispatched, and applied to `001_schema.sql`, the contracts
plan's copy, `docs/schema.md`, `docs/data-model.md`, every design erDiagram and both server plans:

- `deployment_zone.shape` holds the zone shape id (`frontline/1`); it is the same value as
  `Tabletop.Content.DeploymentZone.Shape`. No column is named `style`.
- Every length, coordinate, yaw and scale column is `double precision` (`float8` in the diagrams). The
  libraries carry `double`; rows round-trip exactly.
- Every foreign-key column that does not lead a primary key or unique constraint has an index: 21 of
  them, at the end of the management schema.
- `TABLETOP_DB_CONNECTION` in `.env.example` ends `;Search Path=management,game,public`, the value
  management's `Database` also sets in code. The skeleton qualifies table names with `management.`.

Management's migrator adopts that file byte for byte as migration 001 and recognises a compose-built
database. The compose volume on this machine was rebuilt from it: 33 tables, 21 indexes.

## What the geometry tree settled that the plans did not say

- A degenerate outline throws `ArgumentException` at construction: a `WorldVolume` whose
  `Transform.Scale` is 0 (`default(TerrainTransform)`, or a wire message that omits `scale`), a footprint
  with a zero dimension, or a `TableSize` with a zero edge. **Neither server guards this yet.** The
  skeleton's `DraftEditor.Apply` refuses an invalid `VisualRef` but not a zero scale or zero table edge,
  and nothing in `server/game` calls geometry today. The interpreter tree, the first server-side caller,
  adds the refusal to `DraftEditor.Apply` alongside the `VisualRef` check; management's layout
  validation (`POST`/`PATCH /v1/layouts`, 422) does the same.
- `LineOfSight.Check` is asymmetric: the eye is the caller's body centre and the target is sampled, so
  `Check(a, b).Clear` and `Check(b, a).Clear` differ when the centre line is blocked. The rules module
  calls it from the shooter's side. It returns on the first clear line, so `ObstructedBy` is complete
  only when `Clear` is false; `Cover.SightBlockersBetween` walks every line.
- `LineOfSightResult.ObstructedBy`, `Cover.SightBlockersBetween` and `Movement.VolumesCrossed` return
  ids in the order of the `volumes` argument. `ObstructedBy` mixes terrain-volume ids and model ids
  (from `Bodies.AsBlockingVolume`) without a discriminator.
- `Movement.Region` reads the difficult cap at the start position as `Movement.Check` does. The
  server judges a landed move with `Check`; the region is the client's prediction.
- `Settling.SurfaceHeightAt` never returns a height below 0; a sunken floor cannot be a Solid with a
  negative top.
- `Polygon.OnSegment` treats a zero-length segment as its one point. `PolygonOutline` validates only
  the vertex count and positive area; consecutive duplicate vertices give a NaN axis, so a consumer
  building one from user data de-duplicates first.
- `ZoneStyles.Resolve` and `ShapesOf` throw `ArgumentException` for an unknown style, a missing slash,
  a non-numeric ordinal, and ordinal 0 or out of range.
- `LineOfSight.Blockers` is recomputed per `Check`; a server checking many pairs against one board
  pre-filters to `BlocksLineOfSight` volumes itself, which `Check` accepts unchanged.

## What the skeleton tree settled that the plans did not say

- **Client plan.** `Welcome` carries the connecting participant's row even when they joined after the
  session hydrated; the others receive a `GameInfoMessage`. A `Rejected` is sent only for a version,
  session or participant refusal; a server-side failure to verify or look up closes `1011` with no
  `Rejected`. A `ReplaceDraft` loaded from a layout comes back in `BoardMessage` with fresh child ids,
  so the client never holds the layout's ids. A `SetupEdit` whose `VisualRef` is absent is refused with
  the visual named in `Refused.Detail`.
- **Scheduler and interpreter plans.** `GameSession.AdvanceAsync` caps at 10 000 iterations and logs.
  A faulted session drops every frame, tick and attach; an engine that throws inside `Advance` faults
  the session through `AppendAsync`'s catch. `EventFilter.ForViewer` understands `Undone`, so an engine
  that emits sealed Asks needs nothing more. `HttpJwksSource` refetches only on an unknown `kid`; a
  JWKS fetch failure propagates and the handshake closes `1011`.
- **Management plan.** `TABLETOP_DB_CONNECTION` is read from `.env` through `DotEnv.Load`, which walks
  up from the working directory. Until management's `.env.example` lines land,
  `dotnet run --project server/game` fails with `TABLETOP_STYTCH_PROJECT_ID is not set`.
- **Simulator plan.** `AtRest(Seq)` follows every command (broadcast when the batch appended,
  sender-only when it did not), every connect at every status, and end-setup; it never follows a lobby
  or setup edit, where the `GameInfoMessage` or `BoardMessage` is the terminator.
  `SocketConnection.ReadFrameAsync` rejects binary frames, frames over 4 MiB and unparseable JSON with a
  `1002` close.
- **Environment.** `PostgresFixture` runs against the shared compose database and cleans its own rows
  through `Xunit.IAsyncLifetime`; a sibling suite may run concurrently. Tests bind an in-process Kestrel
  per `GameServerFactory`, not port 5200; only `dotnet run` takes 5200. `ReadArmyAsync` orders stat rows
  by `ctid` for the exact-JSON round trip; the durable fix is an `ordinal` column on `weapon` and the six
  stat and property tables, a schema change management owns as a later migration.

## What the management tree settled that the plans did not say

The parked and deferred findings, each with its ruling, are section 5 of
`.superpowers/reports/management.md`; the first post-merge work on the module is the `Migrator.Baseline`
compose-init test and the two JWT negative tests it names.

- **Game-server plan.** `TABLETOP_DB_CONNECTION` ends `;Search Path=management,game,public`; Npgsql
  accepts it, DbUp does not, so management strips it before migrating. The skeleton qualifies table
  names and needs nothing. It reads the army rows management writes: `weapon`, `*_stat` and `*_property`
  have no ordering column, and a reader that needs import order uses `order by ctid` until an `ordinal`
  column lands as management's `003_*.sql`; the skeleton's `Reads_an_army_tree_back_to_the_sample`
  depends on this. The JWT parameters to match are those in
  `server/management/Identity/StytchAuthentication.cs`: `MapInboundClaims = false`, RS256 only,
  `ValidIssuer = stytch.com/{project_id}`, `ValidAudience = project_id`, 30 s skew, a key resolver over
  the JWKS cached with a 30 s miss floor.
- **Interpreter plan.** `POST /v1/games` answers 422 until a `properties` row exists; management's tests
  insert one by hand and `PropertiesSeeder` is the interpreter's. `CatalogKinds.For` throws when two
  `properties` rows give one catalog name different kinds, so the seeder keeps kinds consistent across
  content sets or `CatalogKinds` moves to first-wins.
- **Web-app plan.** Response records are as the plan's table says: `SessionResponse(SessionJwt, Account)`,
  `OtpSentResponse(MethodId)`, `Page<T>(Items, Next)`, `CreateAssetResponse(Asset, UploadUrl?)`,
  `ConfirmResponse(Asset, Triangles?, Warning?)`, `DownloadResponse(Url, ExpiresAt)`,
  `Manifest(Assets, ExpiresAt)`. Every error is `application/problem+json`: Stytch failures as 401, 409,
  422, 429 or 502 (a transport failure or non-JSON body is 502), an unhandled server error as 500 with
  `ArgumentOutOfRangeException` mapped to 400 and `UnauthorizedAccessException` to 401. CORS allows
  exactly `Authorization`, `Content-Type`, `Tabletop-Client-Version` and five methods, and the preflight
  echoes the whole list. `UseExceptionHandler` sits before `UseCors`, so until that is reordered a 500 may
  reach the browser without CORS headers and read as a network error. The presigned PUT bounds nothing:
  `ByteSize` is checked only at confirm, where a mismatch, an unreadable GLB header, JSON chunk or mesh
  data, or a JSON chunk over 8 MiB all answer 422 and discard the object; the web app treats 422 from
  confirm as "re-export and upload again" and enforces any upload ceiling itself. A deleted account's JWT
  stays valid up to five minutes and its writes answer 500 in that window, not 401.
- **Web-app and client plans.** `POST /v1/layouts` and `PATCH` reject a `TableState` whose texture, any
  placement mesh or any marker visual is missing with 422; send every `VisualRef`. Management does not
  check that an `asset_id` in an army slot, a layout texture, a placement or a marker belongs to the
  caller: the spec's "private for discovery, not access" argument stands, and a client that binds
  another owner's asset gets a manifest entry presigned under that owner's key.
- **Client plan.** The manifest lists only `uploaded` blobs; an asset created but never confirmed is
  absent, not present with a broken URL.
- **Simulator plan.** `POST /v1/dev/sessions` exists only when `ASPNETCORE_ENVIRONMENT=Development`,
  which `./scripts/management` sets by default; the test host's environment is `Testing`.
- **Environment.** `IBlobStore.PresignUpload(key, contentType, ttl)` carries no size: an S3 query-string
  presigned PUT cannot express a content-length condition. `ManagementOptions` is registered lazily and
  resolved right after `Build()`, because `WebApplicationFactory` applies its configuration overrides
  during `Build()`. The management test assembly runs with collection parallelisation disabled because
  `TestKeys` is process-wide state. `tabletop_test_*` databases are created and dropped per test class.

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
| Management API | `server/management/` | `2026-09-05-management-api/README.md` | `design/management-api/` |
| Game server | `server/game/`, no rules | `2026-09-05-game-server-skeleton.md` | `design/game-server/` |
| Web app | `web/` | `2026-09-05-web-app/README.md` | `design/web-app/` |
| Client | `client/` | `2026-09-05-client-shell/README.md` | `design/client/` |
| Interpreter and effects | `server/game/`, the rules | `2026-09-05-interpreter-and-effects/README.md` | `design/interpreter/` |
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

Stytch: test project `project-test-7a0ad7e7-906a-434a-a3d2-bf90668f9f43`, project domain
`https://serious-chicory-4478.customers.stytch.dev` (the JWKS answers there and at
`https://test.stytch.com`; the code uses the latter), public token
`public-token-test-198e0eff-b710-4c13-8258-1187add70b1d` for the web app, unused until it exists. The
code calls the Consumer API. The Stytch MCP tools are project configuration only, with no user
management, so the Consumer user schema has to come from the docs. The local `stytch` MCP server
cannot log in: Stytch's login server returns an `iss` that differs from its published issuer and
Claude Code rejects the mismatch (RFC 9207); the `claude.ai Stytch` connector works but is signed into
a workspace that does not hold this project. **Two pieces of Stytch configuration are
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
