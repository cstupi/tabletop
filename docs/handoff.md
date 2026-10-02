# Handoff — 2026-10-02, steps 1 to 5 merged; the simulator plays whole games with no client; the viewer is next

Where the project stands, and what to pick up next. **Steps 1 to 5 are built and merged: contracts,
geometry, management, the game-server skeleton, the web app, the client, the interpreter, the scheduler and
the simulator. No tree is open. `sim/` plays whole games in process or over the socket with no client. The
owner has logged in, imported armies and started a game in the client. Checkpoint 3 (whole games, by the
owner) gates step 6, the viewer.**

## State

Merged into `main`, in order: the contracts plan (`bd10e5f`), the geometry plan (`ee62d19`), the game-server
skeleton (`862072a`), the management API (`f0d0223`), the web app (`588b848`), the client shell
(`ba53db1`), the interpreter and effects (`c8ae306`), the scheduler (`2f7a168`) and the simulator (`0e49d3f`); each branch is kept and
each worktree removed. On `main`, `dotnet build --no-incremental` has 0 errors and 4 analyzer warnings, all
in test files (xUnit2029 at `GfDealWoundsTests.cs:217` and `LivePropertiesTests.cs:90`, xUnit2031 at
`LivePropertiesTests.cs:56`, CS0028 at `WindowTests.cs:15`); `./scripts/check-client` prints nothing; and
`dotnet test` passes 918 tests: Geometry 157, Content 17, Protocol 37, Client 64, GameServer 453,
Management 88, Sim 102. The two server suites need the compose stack up. In `web/`, `npm test` passes 62 with 1
skipped, `svelte-check` reports 0 errors and `npm run build` writes `build/index.html`.

The game server registers `RulesEngine` through `RulesEngineFactory` with `SchedulerFactory`: a game that
reaches `playing` runs the Grimdark Future roll-off, deployment, activations, rounds, shooting, melee,
morale, casting and transports through Asks. No wire kind was added after the contracts plan, so
`ProtocolVersion` is unchanged.

The first owner session against the real stack (2026-10-01) produced these commits on `main`:

| Commit | What |
| --- | --- |
| `ebcdb2b` | `Sessions.Complete` stamps `account_id` on each new session through `session_custom_claims` (`StytchClient.StampAccount`); no Stytch claim template is involved |
| `c45d974` | six generated starter miniatures in a `Starter` folder at first login (`StarterMeshes`, `StarterAssets`, migration `003`); spec `docs/superpowers/specs/2026-10-01-starter-meshes-design.md` |
| `5d22033` | `GET /v1/assets?kind=` parses the kind ignoring case; the army editor's mesh picker sends `miniature` |
| `2432975` | the Windows client and presigned MinIO URLs use `127.0.0.1`, not `localhost` (Environment, below) |
| `03df02d` | Charge is offered only with an enemy unit within charge distance (p.7, `GfMelee.ChargeTargets` in `ActionsFor`); the cast window opens only when a caster can afford an untried spell (`GfCasting.SpellOptions`); `moved` clears at `turn_ended` |

**The simulator** is `sim/` (`Tabletop.Sim`), with `tests/Tabletop.Sim.Tests`. It plays a scenario from a
catalog of six (`skirmish`, `terrain`, `player-turn`, `free-for-all`, `stalemate`, `casting`), answers every Ask
from its legal set with a uniform seeded policy or a script, drives the session in process or over a real
socket, checks five invariants, and writes one recording per run (5 to 6 MB for an eight-round game). The
first Ask of every game is the roll-off `confirm` gate addressed to both teams, answered once. A socket run
mints its sessions from `POST /v1/dev/sessions`, so it needs management in Development and the game server;
socket-backed tests seed the core properties document themselves. Coverage: uniform play almost never
reaches `decided` in `skirmish`; `casting` reaches `decided`, spell targets and two-team sealed spend windows.
No catalog scenario exercises Takedown, Transport, Scout or Ambush. `casting` runs in process only, because
the shipped properties hold no spells and `sim/` never writes a properties row. `.superpowers/e2e/`, the
scripted driver that preceded it, is superseded.

**Settled by the owner on 2026-10-02:**
- `GET /v1/properties` lists one set per `(game_rules, name)`, the most recently seeded (`properties.seeded_at`,
  management migration `004_properties_seeded_at.sql`; the seeder refreshes it on every startup). An older set
  stays for the games that pinned it.
- Duplicate activate labels from two units the army names alike are the army author's; nothing to fix.
- The round-4 end (p.7, *"After 4 rounds have been played the game ends"*) follows the game's enforcement
  setting: `warn` warns that the final round has finished, `strict` ends the game, `silent` leaves it to the
  players. It is to be built on **one generic enforcement engine** that any system raises a breach into, not
  as a special case: today `Dispatch/EnforcementPolicy.Decide` maps only a command's `Legality`, and a
  state-time breach raised from `Advance` with a strict consequence (ending the game) has no path. Ending under
  `strict` also touches the rows-by-writer rule (the game server writes rows only in `lobby`/`setup`) and needs
  a `GameInfoMessage` broadcast. **Deferred by the owner** until the other items are done; it needs a design
  session (spec, then plan) first.
- Built-in art is placeholder until the game is structurally sound: `client/content/builtin/terrain/*.glb`
  are flat-coloured primitives (woods: trees on a disc; ruins: broken walls; the rest one shape each, authored
  in the unit box), `textures/*.png` are solid colours, `grass` the slab's green.

| Layer | Where | Role |
| --- | --- | --- |
| Design sessions | `docs/superpowers/specs/`, 25 files | the requirements; settle behaviour |
| Implementation plans | `docs/superpowers/plans/`, the unsplit files and five directories | what gets written, task by task |
| Engineering design docs | `docs/design/`, 10 folders plus an index | structure and vocabulary |

**Each merged tree's record** is `.superpowers/reports/<tree>.md`, seven sections: 4 holds every ruling,
5 the parked and deferred findings, 6 the docs corrections, 7 the obligations on later trees. For
`management`, `web-app`, `client-shell`, `interpreter` and `scheduler`, sections 6 and 7 are applied: 6 to
the plans, specs and design docs, 7 to this file. `geometry.md` and `game-server-skeleton.md` are kept for
their deferred-minor lists. `.superpowers/` is git-ignored and local to this machine.
`.superpowers/sdd/client-shell/` keeps two files the client obligations cite: `implementer-contract.md`
and `task-06-report.md`. `.superpowers/briefs/common.md` is the brief every tree's controller reads.
`.superpowers/manager.md` is the manager's scratch and defers to this file.

**Split plans are directories.** `plans/2026-09-05-management-api/`, `2026-09-05-web-app/`,
`2026-09-05-client-shell/`, `2026-09-05-interpreter-and-effects/` and `2026-09-05-scheduler/` are each
a `README.md` (goal, constraints, file structure, the task index, *Interfaces → Produces*, *Interfaces
needed*, rulings) and one `task-NN-*.md` per task; interpreter Tasks 8, 11, 14 and 16 are two files each,
`-1.md` and `-2.md`. A reference of the form `plan → *Section*` resolves against the README. Three task
files still run over 500 lines (management 5; scheduler 2 and 5): split one by step before editing it at
length. The other five plans run 2 000 to 4 700 lines and the design docs 1 000 to 1 700; split each the
same way before a tree is dispatched on it or before editing it.

**Read in this order from cold:** this file, then `docs/design/README.md`, then `docs/architecture.md`,
then the design document for whatever you are touching and its plan beside it.

Precedence, when two documents disagree: **the specs settle behaviour, the plans settle what gets
written, the design docs settle structure.** A later document beats an earlier one. Where a design
document disagrees with its plan, the plan is what an implementer follows. Four specs sit above the
rest, newest last: `2026-09-04-whole-design-review.md`, `2026-09-06-simulation-design.md`,
`2026-09-06-ruleset-3-5-1-design.md` (on the ruleset only) and `2026-09-09-open-questions-design.md`
(on the twenty-five questions it names only).

## Next

1. **Bring the stack up.** Docker Desktop must be running on the host; from WSL,
   `(nohup "/mnt/c/Program Files/Docker/Docker/Docker Desktop.exe" >/dev/null 2>&1 &)` starts it, and
   `docker compose up -d` follows from the repo root. `.env` exists and carries the Stytch project id.
   Never `down -v`: the volume holds the schema every server test reads. The compose `tabletop` database
   carries `public.schema_version` with 001 baselined and 002 to 004 applied, and two `properties` rows
   (an older core-document hash and the current one; the listing shows the current one). The servers are long-running: start them in the
   owner's terminals (`./scripts/management`, `dotnet run --project server/game`, `./scripts/web`); a
   Claude Code background command is stopped after two hours.
2. **The enforcement engine.** One mechanism behind `silent | warn | strict` that any system hands a breach to
   (what was broken, the detail, and what `strict` does about it), with the round-4 end as its first state-time
   client (*Settled by the owner on 2026-10-02*, above). Design session first: a spec, then a plan, propagated
   to `architecture.md` → *Rules enforcement*, the scheduler spec's *There is no round limit*, the accounts
   spec's *Ending is a flag* and the rows-by-writer table.
   `sim/` scenarios run under `strict`, and `SubmitResult.Accepted.Warning` is never populated: a `warn`
   breach is visible only on its event.
3. **Checkpoint 3, then step 6, the viewer.** Checkpoint 3 is whole games through `sim/`. Run on 2026-10-02
   against the live servers: `skirmish --seeds 1..5` and `casting --seeds 1..3` in process passed (casting seed 2
   `decided`), `skirmish --seeds 1..3 --driver socket` passed 3 of 3, and `casting --driver socket` exits 2 because
   the shipped properties hold no spells. A socket run reads `TABLETOP_DB_CONNECTION` from the nearest `.env` and
   calls management at `TABLETOP_SIM_MANAGEMENT` (default `http://127.0.0.1:5100`). The owner's sign-off on
   checkpoint 3 dispatches the viewer. Its spec is the simulation spec, its design `docs/design/viewer/`, its
   plan `plans/2026-09-06-viewer/README.md` and one file per task. Its first version is playback only and sends
   no command; answering an Ask from the browser is a later design session.
4. **Checkpoints by hand.** Checkpoint 1 (the table by hand) is section 5 of
   `.superpowers/reports/client-shell.md`, *Owner checks*. Checkpoint 2 is *Checkpoint 2: what a player
   should see* below; its server side is covered by the end-to-end driver, and what remains is the client's
   rendering and input. The client's setup panel is `client/src/Panels/SetupToolbar.cs`: table size and
   texture, the "{planet} planet" generators, built-in terrain added beside the table and dragged on, the
   selected placement's volumes, objectives and tokens, zone style and swap, layouts, End setup.

## Checkpoint 2: what a player should see

No Ask kind is new; each uses an existing `Legal` type, and the client renders them as its plan says.

- **Roll-off.** After the flip to `playing`, every team gets one framed `Confirm` Ask between the
  roll-off's two steps. The dice can be edited until any team confirms. After the confirm,
  `round_started(0)` carries the order and `phase_started("deployment")` follows.
- **Deployment.** The active team gets "Deploy one unit", a one-piece `PieceSelection` over its shelved
  units, with no pass; set-aside units are excluded. The player picks the unit and places its models from
  the team panel. The turn ends when the unit's last model is on the table by a hand `MoveModels`, and the
  teams alternate one unit each (p.6). Scouts come after every other unit, as "Deploy a Scout", starting
  with the roll-off winner. A unit is set aside as a Scout or Ambusher with the menu's `set_condition`;
  nothing asks for it. The team panel's right-click stays the manual placement path. Hand moves never end
  an activation turn.
- **Activation.** "Activate a unit": options labelled `"{unit name}: {action}"`, pass allowed. A Shaken
  unit is offered only Idle. Immobile and Artillery units are offered only Hold. An Aircraft is offered
  only Advance. Charge is offered only when an enemy unit is within charge distance (p.7). Under the player-turn shape the same team is asked again until it passes or has nothing
  left.
- **Casting.** At the before-attacking checkpoint of an activation (Hold's `casting-window` step, and after
  each move segment of a moving action), when a caster in the acting unit can afford a spell it has not
  tried, the acting team only gets `react` "React at before-attacking?" with one option, "Cast a spell" (id `cast`), and may pass. Choosing it opens `cast` "Cast which spell?",
  an `Options` of `"{spell} ({cost})"` per caster model and affordable, untried spell (id
  `"{spell}|{modelId}"`); the cost is deducted on pick. A sealed `spend_tokens` "Spend tokens for or
  against the casting roll" then goes to every team with a `TokenSpend` (max by model; casters within 18"
  and in sight), on both sides at once. The roll follows. On a success, `spell_targets` "{spell}: pick up
  to {n}" is a `PieceSelection` (1..n). The window reopens after each cast while budget remains. A player
  sees only their own team's offers, and the tokens drop after the pick. The spell list is every catalog
  spell, not the faction's (p.13), until the army snapshot carries a faction.
- **Takedown.** Before a Takedown group's hit roll, if the target has 2 or more alive models, the attacker
  gets `takedown_target` "Pick the model this takedown weapon targets", a `PieceSelection` (1..1) of the
  target's alive models, per Takedown weapon per model, in shooting and in melee, before other weapons. Its
  wounds go to that model with no `remove_casualties` Ask.
- **Transport (embark).** The activation's `move` Ask is a `MoveRegion` whose `Containment.Enterable`
  lists the containers in reach (not on Charge, and not after disembarking this activation). An `Inside`
  destination embarks at no measured distance.
- **Disembark.** A unit inside a container gets `move` "Disembark: place every model fully within 6\" of
  the transport", with `Containment.ExitingId` the container and `ExitWithin` 6; allowance and difficulty
  are unbound. When the container is destroyed, its unit gets `disembark` "Place the survivors fully within
  6\" of the wreck" (`MoveRegion` with `ExitingId`, no pass). Survivors are Shaken, and a survivor left
  unplaced or placed outside the radius is destroyed. The rules path deploys through these Asks only; the
  team panel's right-click stays open.
- **End of round.** When every team has passed or has nothing legal, one unframed `Confirmation` Ask "End
  the round" (question `end_round`, no pass) goes to every team with a participant. Any one player's
  confirm ends the round; the client shows "End the round" and logs "confirmed for Team N". Nothing
  advances until someone confirms, including a round nobody could act in. After the confirm,
  `round_started(n+1)` clears Fatigued and grants Caster(X) tokens (+X, capped at 6, even off the table),
  with no prompt; the end of each activation clears wounds-this-activation and clears Shaken after an idle
  activation (or an Aircraft's Advance). From round 2, "Deploy an Ambusher or pass" goes to each team with
  an Ambusher, starting with the team that activates next (p.13). Then the first team gets its activation
  Ask: the team that finished activating first last round.
- **Menu edits.** A menu `set_round` or `set_active_team` closes an open end-of-round, deploy or activation
  Ask with no answer: it disappears from the panel.

Known parked behaviour a player can hit (scheduler report section 4, items 35 to 37): under warn or silent,
a pass on a deploy Ask also skips that team's Scouts turn; `set_round(0)` once the phase is `rounds` leaves
it at `rounds`, so no deploy Asks return; a partly placed Ambusher holds the next Ask until it is fully
placed.

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
3. web app, client, interpreter, together. **Landed.**
4. scheduler. **Landed.**
5. simulator. **Landed.**
6. viewer.

**What the manager should not trust.** A plan's *expect N passed* is a checkpoint, not an oracle;
every tree so far has overshot it as rulings added tests, and the runner decides. The controllers'
rulings lists are exhaustive by contract, and each ends with what it costs if wrong.

**Merge hazards.** Three files are touched by more than one plan: `CLAUDE.md` (each plan edits only
the lines it owns and appends the rest), `Tabletop.sln` and `.env.example`. Everything else a later
plan modifies belongs to a plan it already requires.

**`IGameRules` spans two modules.** The interpreter's nine members and the scheduler's seven sit on one
interface (`server/game/Rules/IGameRules.cs`); the scheduler's seam records (`TurnContext`, `TurnAsk`,
`Activation`, `HookResult`, `FirstTeamDecision`, `ActivationOption`) live in `server/game/Scheduler/`. A
new member is forwarded in the test decorator `WithSequences` (`StubRules.cs`).

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
  with a zero dimension, or a `TableSize` with a zero edge. The game server's
  `DraftEditor.Apply` refuses such an edit through `DraftEditor.Degenerate`, which calls the
  geometry-owned checks `Footprint.IsValid(scale)` (for a rect, twice the scaled area at least twice
  the polygon threshold) and `Table.IsValid` (including every built-in zone style's polygons).
  **Management does not guard it yet:** its layout validation (`POST`/`PATCH /v1/layouts`, 422) and
  the web volume editor should call or mirror those two checks before a save, so that `DraftEditor`
  does not refuse what an editor accepted.
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
  the session through `AdvanceAsync`'s own catch (`FaultAsync`), at runtime and on the hydrate catch-up.
  `GameSession.StartAsync` advances only a `Playing` game. `EventFilter.ForViewer` understands `Undone`, so an engine
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
  column lands as a new management migration; the skeleton's `Reads_an_army_tree_back_to_the_sample`
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
  stays valid up to five minutes and its writes answer 500 in that window, not 401; `GET`, `PATCH` and
  `DELETE /v1/account` answer 401 once the row is gone. Management does not validate
  `PUT /v1/assets/{id}/volumes` beyond deserialisation (no `top > bottom`, no positive extent), nor a slot's
  base or height; the web app's checks are the only ones. A modelled base cannot be cleared through
  `PATCH /v1/assets/{id}`, whose update coalesces every field.
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

## What the web-app tree settled that the plans did not say

- **Client plan.** Refresh once a minute with `POST /v1/auth/session/refresh`. Sign out only on a 401; keep
  the token on a transport failure and on 502, 429 or 500. Dedupe concurrent refreshes, and after a failed
  refresh back off the full minute. The web app sends no `Tabletop-Client-Version`; the Godot client does.
- **Anything that loads an asset** (the client's `MeshLoader.Orient`, `sim/`, `viewer/`). The GLB stays in
  the file's own units and axes; scale and orientation live on the asset row and are never baked in. A
  converted STL is centred on its footprint with its lowest point at the origin, along the axis the user
  confirmed. A GLB dropped in directly is left as authored, scale 1 and identity orientation unless the user
  changed them. Orientation is Pitch about X, then Roll about Y, then Yaw about Z, right-handed and
  extrinsic, scale last.
- **Base versus Footprint** use opposite conventions. A rect Base has A = length along the facing axis (+Y)
  and B = width; a rect Footprint has A = width (x) and B = length (y). A circle Base's A is its diameter; a
  circle Footprint's A is its radius.
- **Terrain.** The default volume the web app writes is the bounding box in the table frame after scale and
  orientation: offset at the xy centre, bottom and top at the z extent. It is not assumed to sit at the origin.
- **Management plan.** A later management change inherits the missing checks above: asset ownership of an
  `asset_id` in a slot, volumes, slot height and base, and clearing a modelled base. If a server-side check
  is added, its messages match the web app's.
- **Real-STL test.** `web/tests/fixtures/real/` is where the owner drops real STLs (ASCII, binary, kit,
  pre-supported); the skipped test runs once they are present.
- **`CLAUDE.md`.** The web-app tree owns the `web/` Layout bullet, the Node/mise sentence in Environment,
  the `./scripts/web` and `cd web && npm test` Commands lines, and the Conventions line on inches and the
  endpoints the web app never calls. Other trees merge around them.

## What the client tree settled that the plans did not say

- **Socket.** `Session` sends nothing but `Connect` until `Welcome`: before it, `SendCommandAsync` and
  `SendEditAsync` throw `InvalidOperationException` and `SendPresenceAsync` and `SendPingAsync` drop. A new send
  path goes through the same gate. The client half-closes cleanly; a `Rejected` goes Home or to the update
  screen, and any other close, 1011 included, reconnects at 1, 2, 5, 10 s. An unknown message `kind` throws in
  `WireJson` and loops reconnects, so a tree that adds a kind bumps `ProtocolVersion`.
- **Game-server plans.** The server keeps writing army snapshots before it broadcasts `GameInfo(Playing)`: the
  client refetches the asset manifest on that transition and expects every army's assets in it. The mesh
  fallback for an asset missing from the manifest is owner-only (`GetDownload`), so another player's asset
  must be in `/v1/games/{id}/assets`.
- **Management plan.** Only a 401 from `/v1/auth/session/refresh` signs the client out. Management answers
  401 only for an invalid or revoked session, and 5xx or 429 for its own failures.
- **Refresh.** `Session.RefreshAsync()` and `TryResumeAsync()` return `Task<bool>` and never throw; the
  constructor takes an optional trailing `TimeProvider? time`; `SendCommandAsync(Command, Guid id)` names the
  message id to match `Refused.MessageId`.
- **Assets.** Library terrain is placed at `TerrainPlacement.Scale = 1`; the asset row's scale and
  orientation apply to the mesh through `MeshLoader.Orient`, never to the placement, and `ScaleTerrain` is the
  per-placement multiplier only. A change to the Base or Footprint convention changes `PieceNode`,
  `TerrainNode`, `MeshLoader.LoadBuiltin`'s fit, `SetupToolbar` and `BuiltinContent` together.
- **Rules plans.** The team panel's right-click (`MenuModel.OffTable`, `Names.TeamOfModel`) is the only manual
  path to put a shelf or contained model on the table, and stays open when deployment runs through Asks.
  `FixedAskEngineFactory` stays test-only.
- **Client code.** The Godot-layer rules in `.superpowers/sdd/client-shell/implementer-contract.md` hold for
  later client work: Session subscriptions are fields removed in `_ExitTree`; no exception escapes
  `async void` and nothing is discarded unobserved; check `IsInstanceValid` after an await; never read global
  transforms before a node is in the tree. `client/src/Core/` stays Godot-free, enforced by
  `./scripts/check-client`.
- **`CLAUDE.md`.** The client tree owns its three Commands lines, the `client/` Layout children and the
  table-frame Conventions line. Other trees merge around them.

## What the interpreter and scheduler trees settled that the plans did not say

The parked and deferred findings, each with its ruling, are section 5 of
`.superpowers/reports/interpreter.md` and of `scheduler.md`.

- **Engine and session.** A seeded `Playing` game whose log has no roll-off starts one on its first
  `Advance`, including on hydrate; a test that seeds a Playing game and expects no engine events uses
  `GameServerFactory.WithoutScheduler()`. `GameSession.StartAsync` advances only a `Playing` game, and
  `AdvanceAsync` faults the session when the engine throws. The GF first-team policy never branches on the
  scheduler shape. Only OPR's `GameRules` is built; 40k is tested only through `FakeRules`.
- **Later rules work.** Under the player-turn shape, `IGameRules.OnTurnEnded` runs after every activation
  as well as at `turn_ended`, so a ruleset's body must be idempotent. A Shaken activation stays
  sequence-less (`ActivationOption.Idle`): `ShootOrNotStep`'s Shaken exit relies on it. A step that wants
  the step-boundary Confirm names it; a bare `StepHistory.LastClosed()` skips it. A new turn-level Ask
  question must be classified by `SchedulerLog.IsSchedulerAsk` and `IsTurnAsk`, which decide whether the
  stale-Ask close and the dispatcher's readability probe cover it. When reactions reach more than one team,
  `Windows.Run` must count `Chosen` answers in log order rather than recount from `ReactionsAt` (R-N1), and
  `OrderReactions`, null for GF today, must implement p.13's roll-off for simultaneous rules (the winner
  picks the order).
- **A second ruleset.** `LiveSet.SuppressedModifiers` is one set, not one per side; scope it per side if
  any modifier class has two sides. GF's cover policy sits in the generic
  `Battlefield.ModelsBehindSightBlockers` and belongs in `Rules/GrimdarkFuture/`.
- **Recorders and log panels** (the client log, `viewer/`, `sim/`). A panel that displays
  `StepCompleted.Values` filters out `StepRepeat.Key` (`"$repeat"`), which stays on the wire and in
  `CompletedStep.Values`. `round_started.Order` is always a copy (`EventApplier` copies it before clearing),
  so a recording tool that builds payloads from a projection is safe.
- **Management.** `effect.target_action`, `target_roll` and `target_stat_level`+`target_stat` do not
  cascade, so a bare `delete from management.properties` can fail: delete `effect` rows first, or add
  `on delete cascade` in a new migration. The kind-conflict refusal is only in
  `PostgresPropertiesStore.SeedAsync`, not in `InMemoryPropertiesStore`.
- **Geometry consumers.** `Footprint.IsValid(scale)` and `Table.IsValid` are the outline validity checks;
  management's layouts and the web volume editor should call or mirror them (*What the geometry tree
  settled*).
- **Content.** `grimdark-future-core.properties.json` holds no spell entries, so no casting window opens in play until
  spells are authored. The spell list is not filtered by faction (p.13) until the army snapshot carries a faction.
- **Wire.** Neither tree added a wire kind; `AskQuestions.EndRound` and `DeployAmbusher` predate them, and
  `ProtocolVersion` is unchanged. Army snapshots are still written before `GameInfo(Playing)` is broadcast
  (`GameSession.EndSetup.cs`).

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
| Scheduler | `server/game/`, turn order | `2026-09-05-scheduler/README.md` | `design/scheduler/` |
| Simulator | `sim/`, a developer tool | `2026-09-06-simulation/README.md` | `design/simulation/` |
| Viewer | `viewer/`, a developer tool | `2026-09-06-viewer/README.md` | `design/viewer/` |

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
Aircraft's movement (p.13: straight line, +30", ignores units and terrain) is not built; only its
action restriction and its -12" to attackers' range are. Takedown's picked model still blocks line of
sight and gives cover, against p.14's *"does not block line of sight or provide cover"*. A Limited
weapon with no eligible target is not marked used; the book is silent. `RerollDice` and `SetDice`
carry no actor check, because the design does not say who may edit dice.

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
rulebook's diagrams are read by extracting images with `pypdf` and Pillow. WSL runs mirrored networking
(`%UserProfile%\.wslconfig`): Windows reaches the WSL servers at `127.0.0.1`, while `localhost` resolves to
`::1` first, which does not reach WSL, and .NET waits about 21 s before falling back. The client's defaults
(`ClientSettings.Default`) and `TABLETOP_S3_ENDPOINT` therefore name `127.0.0.1`; an older saved
`%APPDATA%\Godot\app_userdata\Tabletop\settings.json` carrying `localhost` has to be edited by hand.

Stytch: test project `project-test-7a0ad7e7-906a-434a-a3d2-bf90668f9f43`, project domain
`https://serious-chicory-4478.customers.stytch.dev` (the JWKS answers there and at
`https://test.stytch.com`; the code uses the latter), public token
`public-token-test-198e0eff-b710-4c13-8258-1187add70b1d` for the web app, unused until it exists. The
code calls the Consumer API. The Stytch MCP tools are project configuration only, with no user
management, so the Consumer user schema has to come from the docs. The local `stytch` MCP server
cannot log in: Stytch's login server returns an `iss` that differs from its published issuer and
Claude Code rejects the mismatch (RFC 9207); the `claude.ai Stytch` connector works but is signed into
a workspace that does not hold this project. **One piece of Stytch configuration is
load-bearing and not in the repo:** `http://localhost:5173/reset-password` registered as a `RESET_PASSWORD`
redirect URL, which the owner reports is in place. Password signup and login through the web app and the client
work against the real project; a reset email has not been sent from here, because the test project refuses
mail to unverified domains (`billing_not_verified_for_email` for `@example.com`).

### The rulebooks

The skill `grimdark-future-rules`, in the local plugin `licensed` (`.claude/skills/licensed/skills/grimdark-future-rules/`;
the plugin holds third-party licensed content and is not committed), holds the Grimdark
Future v3.5.1 text one file per printed page (`pages/p07.md` is p.7), with a page index; grep it and quote
the page. The PDFs are the source:

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
