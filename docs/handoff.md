# Handoff — 2026-11-03

The game is built end to end: a Grimdark Future game, under either edition (v3.5.1 or v3.6.0, chosen per game in the lobby;
3.6.0 is the default for new games, existing and unfinished games stay on 3.5.1), runs from lobby and setup through the
roll-off, deployment, activations, shooting, melee, morale, casting, transports and objectives to a winner, in the Godot
client or the browser (`/viewer/`). The two editions are two rule sets, `grimdark-future` (3.5.1) and `grimdark-future-3.6`
(3.6.0); an army carries its edition (`army.edition`, `'3.5'` or `'3.6'`) and picks only into a game of that edition. The
3.6.0 work, with every rule and spell playing, is on branch `gf-3-6-0`, not yet merged to `main` or tagged. `sim/` plays whole games headless; `viewer/` also replays them. v0.1.31 is the latest tag. What each
earlier batch built, with the owner's rulings, is in `docs/shipped.md`. An undone roll's dice stay
spent: an undo always draws fresh dice (`DiceCursor.DrawnSoFar` counts the whole log).

## Get running

```bash
docker compose up -d     # Postgres 5432, MinIO 9000/9001, management 5100, game 5200 (dotnet watch), web 5173 (+ /viewer/), viewer 5174 (Vite), sim-runner 5180
./scripts/client         # build, check, launch the Godot client on Windows
```

- Docker Desktop must be running on the Windows host. From WSL:
  `(nohup "/mnt/c/Program Files/Docker/Docker/Docker Desktop.exe" >/dev/null 2>&1 &)`.
- `.env` (untracked) carries the Stytch project id and `TABLETOP_STYTCH_SECRET`; `cp -n .env.example .env` on a new
  machine, then add the secret.
- The two servers bind `127.0.0.1` only: Development maps `POST /v1/dev/sessions`, which mints a session with no login.
  Each builds into its own `obj/Unix-<slot>/` and `bin/-<slot>/`, so host builds never disturb them. The first start
  after a clean restores NuGet for about a minute.
- `docker compose stop management game` frees the ports for `./scripts/management` and `./scripts/game`;
  `docker compose stop web viewer` does the same for `./scripts/web` and the viewer's `npm run dev`.
- The web app (`http://127.0.0.1:5173`) and the viewer run as Vite dev servers in `node:24.19.0` containers, proxying `/v1`
  to the management container. The website proxies `/viewer/` to the viewer's server, so the browser game is
  `http://127.0.0.1:5173/viewer/` (the header's **Play**) and shares the website's session, as production serves it; the
  viewer's own `:5174` serves the same app without the website's session. They share `node_modules` with the host and run
  `npm ci` on start only when `package-lock.json` changed since the last install. The compose one-shot `geometry-wasm`
  publishes the WebAssembly geometry into `viewer/public/geometry/` first; rerun it after a Geometry change.
- Never `docker compose down -v`: the Postgres volume holds the schema the server tests read.
- Godot's import step wipes the client's build output. If every script fails with "class could not be found", run
  `./scripts/client` again.
- `./scripts/client` runs the game, not the editor, so it never imports. After a new font, texture or mesh lands
  (a pull, a merge), run `./scripts/godot --headless --import` once, then `./scripts/client`; otherwise the client fails
  at `ThemeBuilder.FontOf` with "Error loading resource".
- The client's log is `/mnt/c/Users/clays/AppData/Roaming/Godot/app_userdata/Tabletop/logs/godot.log` (the latest run;
  older runs are the dated files beside it). A crash can cut its tail off.

**The lifecycle suite.** `dotnet run --project sim -- lifecycle` plays eight shapes against the compose stack by the client's
path (spec `2026-10-20-lifecycle-suite-design.md`, `sim/AGENTS.md` → The lifecycle suite): management over HTTP (create on the
first set of `GET /v1/properties`, join, army import through the dev-mode `POST /v1/dev/armies/import`, statuses, leave,
delete) and the socket (lobby and setup edits, waiting for players, play to the rules' end, replay on `/ws?watch=1`), on
`lifecycle-player-0..7` and `lifecycle-spectator`; nothing written to Postgres directly. `two-players-3.6` plays `two-players`
on the 3.6.0 set with 3.6 armies. On `gf-3-6-0` (2026-11-03) all 8 of 8 pass (about 3 min).

**The rules suite.** `dotnet run --project sim -- suite` plays every suite game in-process (about 3.7 min; a game with a dice fix plays with the step gate on);
`--driver socket` plays them against the compose stack (about 12 min) on the fixed accounts `sim-player-0..3` and deletes
each game's rows, never the accounts (`sim-player-0..8`).
`--only core,special,army:<edition>:<book uid>,spells:<edition>:<book uid>,<game name>` selects (`army:3.5:<uid>`, `army:3.6:<uid>`; a 3.6.0 run is `<game name>@3.6`); `--keep` keeps recordings (and a socket run's
games, with `sim-spectator` as a participant for the live feed); `--list` lists games and what each covers. Every run writes
`recordings/suite-<run id>.md` and `.json` (copied to `suite-latest.md` and `.json`; rewritten after each game, so an aborted run
keeps what it played): the verdict, each failure with its lines and replay command, every game, then coverage. The console prints
a counter, each failure as it happens, the verdict line and the report paths; `--verbose` adds every game and the coverage table. The viewer's Tests page (`/viewer/#/tests`) runs the same suite through `sim-runner`:
pick games, Keep, driver, Run; results stream in, then coverage; Watch opens a kept recording, Open live a kept socket game.
On `gf-3-6-0`, in process: `PASSED: 685 of 685 games passed, 0 failing on known defects, 0 unexpected; 1122 of 1122 entries covered, 0 not automated`, in about 220 s (default seed 222 s; seeds 1-5 each 217-228 s and each 685 of 685):
308 games under 3.5.1 and 377 under 3.6.0; coverage 3.5 545 of 545 (88 by pattern), 3.6 577 of 577 (90 by pattern), 0 not automated in either.
Each shared game's 3.6.0 run is named `<name>@3.6`. `--seed N` plays every game on seed N instead of its own (the replay line carries it).
`--only army:3.6:<uid>` runs one 3.6.0 book's games. See `sim/AGENTS.md` → The rules suite.

**Checks.** `dotnet build` has 0 errors (analyzer warnings in test files only). `./scripts/check-client` prints nothing.
Two suites (spec `docs/superpowers/specs/2026-10-16-test-suites-design.md`, `tests/AGENTS.md`):
- **Unit**, `dotnet test`: needs nothing running (servers over in-memory fakes; a test that reaches a live service is a
  defect). About 4 min. Geometry 303, Geometry.Wasm 44, Content 70, ArmyBooks 56, TerrainMeshes 114, TableTextures 29,
  Protocol 65, Sounds 65, Viewer 16, Client 945, GameServer 1795, Management 330, Sim 334.
- **E2E**, `dotnet test --settings tests/e2e.runsettings` with the stack up: the Postgres and S3 adapters (shared contract
  tests run against fake and real), migrations, the suite games in process and over the socket, and the seven lifecycle shapes
  against the compose stack (`LifecycleTests`, about 3 min). Management 61, GameServer 40, Sim 338; about 7 min.
  On `gf-3-6-0` against the stack: migrations 023–028 apply; `MigrationTests` passes (it asserts `effect_floor_check` is the
  only floor check and admits `modify_parameter`). Not yet run on the branch: the whole E2E suite and the socket-driver rules
  suite.

`cd viewer && npm run check && npm test` passes 1,676 (6 skipped) (needs `./scripts/geometry-wasm` once); `TABLETOP_E2E=1 npm run test:e2e`
plays 4 stack tests (lobby to setup, deployment drags, a refused move, a whole round) and deletes what it creates;
`cd web && npm test` passes 136 (1 skipped). Screenshots of the viewer for review: a headless Chromium script signs in with a
dev session (`POST /v1/dev/sessions`, roles admin) and stores it as `tabletop.session` on the website's origin.
In a kept socket run's games, every hit, defense and morale `RollRecord` judged on its modified result carries `basis`
(`game.game_event`, `step_completed` payloads).
Every catalog scenario passes `--seeds 1..10`, `objectives` `--seeds 1..20`. `objectives` moves toward markers its team
does not hold (`ObjectiveGoal`), so every seed seizes at least once and `round_ended` is expected; a tie still passes.

## Release

The owner releases. "tag it" means tag `vX.Y.Z` on `main` and push at once, no extra pre-tag run. There is no CI: the full unit, rules, lifecycle and browser suites run locally on `main` before it is called ready to tag. The release runs no tests unless `--tests`.
On the home server `docker1` (`https://tabletop.cstupi.com`, files at `https://files.tabletop.cstupi.com`): `git checkout main && git pull && scripts/release --deploy home --version vX.Y.Z` (tests in containers, the client built in the Godot builder
container, publishes `releases/` and `builtin/`, deploys). The owner says which version is live. The owner's other Claude
sessions take instructions over Remote Control: `docker1-logical-curry` (server; no superuser DB queries without the owner's
approval) and `syn01-piped-castle` (NAS; no root).

## Next

In order:

1. **Merge `gf-3-6-0` to `main`.** Still to run on the branch: the full E2E suite, the socket-driver rules suite, then lifecycle. The edition build's ledger is `.superpowers/sdd/index/progress.md`; `docs/shipped.md` quotes the
   owner's edition ruling.
2. **A GameServer test fails intermittently,** about once in five full runs. Its name was never captured;
   `ZonesInPlayTests.A_third_team_created_in_setup_moves_default_zones_to_the_corners_and_leaves_chosen_zones` failed once.
3. **The ISR Mark games cannot catch a small over-reach.** That needs a second shooter 18-20" from the marked unit.

### 2026-10-26, v0.1.29

- **Replay sound:** a replay's forward steps (Play at 1× or slower, Step and Action forward) play their cues; a jump, a step back or
  faster Play stays silent and reseeds (`SoundCues`: an empty batch reseeds). The browser has no sound.
- **Tab:** past the last unit (before the first) Tab clears the selection and the decision box shows every unit's options again
  (`SelectionView.Cycle`); owner: "after a rotation it should show full list".
- **Set aside:** the server names the units that may be set aside (`IGameRules.MaySetAside`, `ModelFacts.MaySetAside`, before round one,
  counts-as chains included: Infiltrate, an Infiltrate Aura, Surprise Attack); both clients' roster menus read it, no rule names.
- **Shot lasers:** `determine-attacks` records each shooter's nearest seen target model and the point it saw (`GfAttack.Aims`,
  `aim_<shooter>_<target>`); `ShotLines` draws only those, so no beam passes a perimeter. Server sight was already right.
- **Dangerous terrain:** one test per model per activation (p.11), whichever triggers it meets (`dangerous_tested_<model>`); a model
  activated in it that walks out rolls once.
- **Copyable log:** Godot's log rows are selectable `RichTextLabel`s (Ctrl+C, a Copy button for the log as drawn, `LogCopy`); the
  browser's log selects across rows.
- Checked on `main` before the tag: unit (all projects), E2E (Management 61, GameServer 40, Sim 340 with the lifecycle shapes), rules suite
  307 of 307, viewer 1,692 and its 6 stack tests, web 134.

### 2026-10-28, v0.1.30 and v0.1.31

- **v0.1.31:** the web app's upload and library preview draws a GLB's embedded textures (`connect-src` admits `blob:`, which three.js
  fetches them through); the README opens with the release command.
- **Godot setup screen** (spec `2026-10-27-godot-setup-screen-design.md`): a fixed head holds Start setup / End setup; the setup
  body opens on Terrain (search, catalog grouped by what a piece does as the browser's, Random table, Add my terrain, Clear), then
  closed Objectives, Table, Deployment zones, Layouts, Game settings and Players with counts in their headings. The terrain card shows
  facing, Remove and Scale; Mesh and Volumes are folded. No new outbound message (`SetupFolds`, `SetupLayout`, `TerrainGroups`).
- **Unit card over the replay bar:** the bar draws about 146 px in the scifi theme, more than `ReplayH` (112); `HudLayout.Of` now
  takes the bar's real height, so the left column ends above it.
- **Tokens removed** (owner: "Remove tokens"; spell buffs and debuffs are the server's grants and conditions): a marker is an objective.
  `MarkerKind.Token`, `place_marker`, `remove_marker`, `marker_placed` and `marker_removed` are gone, with Add token, the in-play
  Drop a token and the marker menu's Remove in both clients; the setup section is Objectives. Migration `022_remove_tokens.sql`
  deletes token rows and those events (the dev database had none).
- Unit tests pass for Client, Protocol, Content, GameServer, Management and Sim; viewer check and calls agreement pass. The rules,
  lifecycle, E2E and browser stack suites have not run on this batch.

### 2026-10-25, v0.1.26 and v0.1.27

v0.1.27 is v0.1.26 plus two `scripts/release` fixes: it builds from a fetched worktree of the tag (`build/source`), and the
Node checks mount the whole clone (the viewer reads `web/src` and `client/content/builtin/catalog.json`). Tagged v0.1.26 (migration `020_shared_assets.sql`). Specs `2026-10-25-performance-and-network.md`, `2026-10-25-loading-design.md`,
`2026-10-25-shared-models-design.md`.

- **Network:** permessage-deflate on `/ws` (a game 1,950 → 176 KiB per socket), compressed management JSON, content-hash routes
  cached immutable. A handshake close waits for the peer's close frame (Node drops an async-inflated `Rejected` otherwise).
- **Godot performance:** LODs generated at mesh load, the export builds the shared libs optimised, the roster redraws changed rows
  only, move regions off the main thread; Quality presets set SSAO, soft shadows and the LOD threshold (Low on integrated GPUs).
- **Loading:** a shader warm-up behind "Preparing graphics…" (reruns on a new GPU, driver, Godot or client version), "Connecting…" /
  "Loading game…" over game entry, a "Loading models n of m" strip; the browser has the same panel and a geometry download strip.
  Open: one 0.7–1.0 s frame at the first Hud control on the first game after a cold warm-up, behind "Loading game…"; cause not found.
- **Rules:** walls, fences and barricades give cover (p.11, p.12, p.18); a charge must reach base contact when it can (p.9), with a 0.5" grace and the
  charge drawn into contact when it had the movement (owner ruling);
  a move that breaks coherency is refused under strict (p.7). The oval-base deploy layout keeps coherency.
- **Polish:** browser play fixes (dead models, decision box, header height, refusals, drag clamp, terrain cards); Godot's table shows no
  bookkeeping conditions (`Bookkeeping`), marker rows declutter, the header reads VP; player text cut to the plain fact.

### Shared models: offers to a named player

Spec `2026-10-25-shared-models-design.md`, released in v0.1.28. Owner: "someone needs
to accept the shared files and it shouldnt just go to EVERYONE." v0.1.26's everyone-visible sharing is replaced; nothing is
public.

- Offering: on the website, **Offer…** on the Library's selection, **Offer folder…** on the folder in view (its assets and
  sub-folders' at that moment), **Offer to a player…** on the asset page. The recipients are players the sharer has been in
  a game with, shown with the games played together and the latest date. One `share_offer` row per recipient; 50 offers a
  day per sharer, 20 open per recipient. No email lookup (the spec says why).
- Answering: **Offered to you** (the Library tab shows the open count) previews each model, then **Accept** into a chosen
  folder, **Decline**, or **Block** the sharer (declines the rest, hides new ones; unblock below). Only the recipient of an
  open offer can see, preview or download it; every other caller gets 404. **Sent** shows each offer's state and withdraws a
  waiting one.
- Accept is v0.1.26's copy: the recipient's own asset under their quota, its blob row naming the sharer's S3 object; it
  stays when the sharer withdraws, deletes or leaves. An object is deleted only when no blob row names it.
- Migration `021_share_offers.sql`: `share_offer`, `share_offer_asset`, `share_block`; drops `asset.shared_at` (assets
  shared under 020 are offered to no one). `/v1/shared-assets` and `/v1/assets/share|unshare` are gone; `/v1/offers` replaces them.

### 2026-10-25, v0.1.25

Released as v0.1.25.

- **The play UI** (spec `2026-10-25-play-ui-design.md`), after the owner found the browser viewer broken (*"completely broken
  and has horrific ux"*, the replay's Play button dead):
  - One sign-in: the viewer is served under `/viewer/` in development too, through the website's dev server, and reads the
    website's session; the developer password form and the pasted JWT are gone; the dev socket is `ws://127.0.0.1:5200/ws`.
  - A page of the website: it loads `web/src/themes.css`, `app.css`, the header (`shell/SiteHeader.svelte`), icons and the theme
    switch, and fills the window (no page scroll; side columns scroll inside themselves).
  - Redesigned: the games list (status chip, players, age, one main action each); lobby (team cards beside a Game panel, Start
    setup always in view); setup (tools sidebar with terrain search grouped by what a piece does, the piece's card, End setup in
    the header strip); play (top bar, collapsible roster column, decision box leading the right column, unit card, log); replay
    (the play page's parts read-only and a playback bar: step by round, turn and action, Play/Pause, 0.5–8×, scrubber; the old
    one opened on the last frame, so Play did nothing); the board (theme colours read at draw time, filled terrain, zone bands,
    objective tokens, names only when zoomed or selected, never ids or snake_case).
  - The top bar used to stay on "Round 0" all game (it never redrew); fixed.
- **Objectives are points** (owner ruling: *"Within 3" of the CENTER of the marker"*, *"the marker is a point"*): the seize measures from a
  model's base edge to the marker's centre (`GfObjectives.Seize`, `Distance.EdgeToEdge(model, centre)`), not to the token's rim, which
  had let a unit 4.25" from a 3"-wide objective seize it. The objectives spec, the geometry design and the suite's mission check say so.
  Logs already written keep the seizes they recorded.
- **Objective names.** A marker with no label reads as its kind and place ("Objective 2"), never an id, in Godot's `Names` and
  the browser's.
- **Same calls from both clients** (spec `2026-10-25-same-calls-design.md`, plans `2026-10-25-same-calls-{1..5}*.md`), owner: *"we need
  to be sure the same calls would get made in godot and the web"*. Every message Godot sends is built in `client/src/Core/`
  (`AskAnswers`, `PiecePick`, `HandMoves`, `Menus`, `LobbyEdits`, `SetupEdits`, `DeployView.Place`, `GameOverView.Answer`,
  `CombatLog.UndoHere`); `CallsViewerFixtureTests` writes `viewer/tests/fixtures/calls.json` (414 cases over 21 recorded call
  logs: every Ask mode, deploys and Ambusher placements, moves and disembarks, menus, undo, drops, 43 lobby and setup controls,
  and 34 that must send nothing) and `calls.agreement.test.ts` replays each through the browser's builders, byte-equal after one
  normalisation. Fixed on the way: the browser's Undo to here on a roll row and its disembark facing (now `Containment.ExitYaw`
  through the wasm), and a Godot bug: a disembark onto non-impassable terrain fell back to the container's facing. A new outbound
  message needs a case (`client/AGENTS.md`, `viewer/AGENTS.md`). `viewer/tests/e2e/ambush.e2e.test.ts` sets a unit aside, plays to
  the round boundary and places the Ambusher (refused within 9", accepted over 9"), with two accounts and with one holding both
  teams. Godot's rendering and input stay hand-tested.
- Checked: viewer 781 unit tests, type check, hosted build and stack tests; client tests; the web image builds with the viewer
  in it. Every page was reviewed by screenshot in both themes at 1440×900 and 1280×720; nobody has clicked through it on screen,
  and Godot's graphics settings, Random table and predefined tables have not been seen in the client. The full unit, E2E and
  rules suites were last run before this batch (all green at v0.1.24).

**Next: the easy computer player.** Brainstormed with the owner (not yet a design): play against the computer at several
difficulty levels, built easy first. Deterministic C# on the game server (the rules live there), no LLM. Easy makes sensible,
human-looking single decisions aimed at winning: sample legal positions for deploy and move, score each (expected damage dealt
and taken from the real stats, cover from the threatening units, objectives, the unit's role from its weapons: a ranged army
holds and moves only for objectives), pick the best; activation order, targets, Scout and Ambush the same way. Harder levels
plan across activations (combos such as ambush and debuff one unit, finish with another), and block and screen with units.
The browser board is how the owner wants to play and judge it, many games at once. Next step: the written design.

### 0. Performance

`docs/superpowers/specs/2026-10-11-performance-review.md` traces one player action end to end and ranks what costs time.
Its "do first" set is built: charge targets check distance before walking paths; the move region skips unreachable
bases; sight lines reject by bounding box before the exact test (`SightBlockers`); one Postgres transaction per command
(`GameSession.RunToRestAsync`, broadcast only after the commit); the Hud renders once per frame (`Session.Frame`,
`RenderScheduler`) and builds the combat log once per log state (`CombatLogCache`); `TableView.Redraw` diffs the board
(`BoardDiff`) and `MeshLoader` parses each GLB once off the main thread. Measured, Release, 15 units of 5 a side,
2,879 events: command mean with Postgres 116 ms → 15 ms (p90 322 → 25 ms, max 1.7 s → 193 ms), appends per game
2,196 → 369, turn-boundary `Advance` 38–46 ms → 2–2.6 ms. The dev stack's `dotnet watch` (Debug, hot reload) is
12.8× slower than Release; production builds Release.

Range rings (`RangeRings`) scan each carrier's ring once, skip carriers whose rings cannot reach it, decide inside or outside
from each base's bounding circles (`Geometry.BaseEdge`: the base outline built once, `Inner`/`Outer`) and measure exactly only
between them; a round base's ring is closed form, any other is found by regula falsi. `RangeRingNode` recomputes only when the
selected unit or its models' slots or placements change (`RangeRingCache`) and rebuilds its mesh only when the rings or colour
do. Measured, Release, 20 carriers of a 24" weapon packed in a 5 × 4 grid (`RangeRingsPackedTests`): 1" rounds 129 → 3.5 ms,
2.5" × 1.5" ovals 8.0 s → 64 ms. The combat-log panel keeps the rows of the leading top-level entries that draw the same
(`CombatLogRows.Kept`) and rebuilds from the first that differs: an append rebuilds the open block and adds the new rows, an
undo rebuilds from the first entry it strikes; a fold, a reveal, the filter, the focus, the theme, a pending undo or new rule
texts rebuild all. Grants find their effect row through `PropertySet.EffectById` (one dictionary per set), the menu's
`AddGrant` check included; `Battlefield.Terrain` builds a table's world volumes once per `TableState`. `SightLines` re-sends the hovered unit's and an assigned
target's sight query only when the pair, a model placement or the terrain changed (`SightKey`, `SightSlot`, `SightCache`).

`docs/superpowers/specs/2026-10-25-performance-and-network.md` is the Release play profile of the server, the client's Core
and the viewer, and the wire for remote play. Built: `/ws` negotiates permessage-deflate (a game 1,950 → 176 KiB per
socket, a reconnect 1,864 → 144 KiB; `Connect` and `Reauth` go uncompressed from Godot and the sim); management compresses
JSON and caches the content-hash routes immutable; the web image's Caddy is 2.11 and the hashed `_framework` files are
immutable. A unit's pickup in a move Ask draws its areas in 5.5 ms (117-126 ms: `MoveSpace.MeetsAtEveryRest`,
`MoveShape.SurelyEnters`, bounding-circle culls). The browser's Geometry is AOT-compiled in the web image with hand-written
facade readers (`Contacts` 11 → 2.5 ms); the viewer gates a drag once per landing and once per frame (51 → 20 ms on the
interpreter), reuses the session JWT, mounts the Watch picker only for admins, and caches hover distances and zone polygons.
The harnesses that measure all this are in `.superpowers/perf/` (not in the repo). Left alone, with numbers in the spec: the
whole log on every connect, one `EventsMessage` per batch, the combat log's full rebuild per log state, the server's cold
first game. Risks to watch: GLB parsing off the main thread, template copies of rigged or animated models; the host SDK has
no `wasm-tools` workload, so a host or CI build of the geometry runs on the interpreter.

The same spec's "The Godot client" section profiles a release export through a whole game (15 units of 5 a side, the Ruins
table, the owner's 57k-triangle orc on every model) and the team player's actions. Built: LODs for every GLB at load (26 M →
6 M primitives a frame at Medium; GPU 2.67 → 1.18 ms on the development RX 6900 XT, Low 0.50 ms); the Quality presets also set
SSAO, soft shadows and the mesh LOD threshold, and a first start on an integrated GPU picks Low; the libs are optimized in an
export (`Directory.Build.props`: `ExportRelease` was building Geometry, Content and Protocol as debug code); the Hud rebuilds
only what changed (roster rows, header, dice tray, unit markers, the decision box's view); a move Ask's areas build on the thread
pool (`MoveRegions.OutlinesAsync`, up to 1.5 s on the main thread before); one gate and a `MoveReach.Reaches` landing test per
drag frame; shared materials and body meshes, an id index for `TableView.PieceAt`. Whole game at Medium 1280×720 on Vulkan:
p99 15.3 → 2.4 ms, frames over 33 ms 133 → 2 (the host's); a five-model drag (Compatibility) p50 55 → 9.6 ms. `PerfProbe` records frames
in a development build.

Loading feedback (spec `2026-10-25-loading-design.md`): a first start, or a new GPU driver, Godot or client version, runs a shader
warm-up behind "Preparing graphics…" (7.3 s cold on the RX 6900 XT) that draws every material the table makes under each Quality, so
a game's first frames compile nothing (before: frames of 1.8 and 1.7 s with the table shown). Entering a game shows "Connecting…" and
"Loading game…" over the screen until the table stands, with the work spread over frames (12 ms of posted work a frame, prints on the
thread pool): warm, the longest frame 0.6-1.1 s → 0.2 s; the first game after a cold warm-up still has one frame of 0.7-1.0 s behind
the curtain. Other players' models load behind a strip, "Loading models 3 of 12", over bases already on the table; a reconnect
shows "Reconnecting…" there. The browser has the same texts through one `Loading` component, and the WebAssembly geometry download
shows "Loading table tools 3 of 13".

### 1. Open items from the defect fixes

- **Owner rulings on movement:** a spell's forced move moves the unit however that unit moves (*"Spells move as however that
  unit moves"*): its region and its apply read the target's own Flying, Strider and Aircraft (`GfMoves.TerrainFor`). A granted
  Flying lasts the whole activation (*"Granted flying applies to the whole move - its per activation"*): once it applies it stays
  live for the activation-start test, every leg of a split move and the landing, and `GfHooks.OnTurnEnded` removes it when that
  activation ends (`GfMoves.ConsumeKeepingFlying`, a name check in one place); a spell's forced move of the unit flies with it but
  neither spends nor holds it, since that is not the unit's activation. On a table without 30" of room an Aircraft moves as
  far as it can in the direction taken (`GfMoves.Minimum`, `Movement.RoomAlong`); the client's ring and readout
  (`MoveRuler.Minimum`, `MoveRegions.Outline`) and the sim's `AnswerPolicy` read it the same way.
- **Movement rulings from the rulebook:** a model a hand move left in impassable terrain may head out of the volume, not move along it
  or deeper in (p.11 *"Units may not move through impassable terrain"*; the book is silent on leaving; `Movement.Check`: a path's
  first leg may head out of it, used by `GfMoves.PathBreach`, `ApplyForcedMove`, the client's reach and the sim). p.36 Banner of Eternal Flame (*"The Hero and its unit
  may ignore all terrain effects"*) ignores difficult terrain's cap and the dangerous terrain test, moving or activated, but is no leave
  to move through impassable terrain and keeps cover (`GfMoves.TerrainFor`; `Content/grimdark-future-relic-wargear.part.json`). The
  client holds every model of a move to the 6" cap once any ghost enters difficult terrain (`MoveRegions.Allowance`, p.11) and marks a
  landing in impassable terrain, a Flying one included, unless the Ask `IgnoresTerrain` (`ContactCheck`, `ContactKind.InImpassable`).

### 2. The client

**In-game UX** (merged to `main`; spec `docs/superpowers/specs/2026-10-08-in-game-ux-design.md`): the table to the edges with the header, decision box, dice, log drawer and
end message over it; selection, hover, formations, and target and casualty picks on the table; the sight check, answered by
the server; hand moves under the enforcement setting. New games start with Frontline deployment zones,
drawn through round 0.

**Client UX** (spec `docs/superpowers/specs/2026-10-10-client-ux-and-themes-design.md`):

- **Collision.** A held piece is lifted over everything and lands clear (spec `2026-10-24-held-pieces-lift-over-design.md`);
  PageDown/PageUp put it at a level, where it slides along what is at that height (`Collision.Slide`, `Separate`, `Land`,
  `RestNear`); Alt places freely. The server rests every placement on its open surface nearest the Z sent (`Battlefield.Settle`)
  and judges a move answer's p.7 contact clauses under the enforcement setting (`GfMoves.ContactBreach`: `move_overlap`,
  `move_through_models` when only a path through models fits, `move_separation`) and a hand placement's overlap (`PlacementBreach`). The
  built-in `ruin` is a walk-in Area; `large-sight-blocker` is Impassable.
- **Combat log.** One line per roll (Today → C); the drawer, filters, dice tray and Undo are as the client UX spec says.
- **Unit card.** `UnitCard` replaces `StatSheet`: weapons with AP, wound rows, rule chips with text, and the hovered-enemy
  strip with distance and each weapon's range.
- **Themes, settings, Hud frame.** Fantasy and Sci-fi (`UiThemes`, `ThemeBuilder`), a Settings dialog (theme, Animate, Camera
  follow, Turn banner, Range rings), and `HudLayout` slots so no two parts overlap at 1280×720 and up.
- **Undo.** An always-shown **Undo** button left of the log button (`UndoButton` over `UndoPlan`) undoes the last action in
  the game, whoever took it, one action per press, with the engine events after it; it warns before losing dice. Ctrl+Z
  presses it.
- **Table UX.** WASD/Q/E camera, Space/Tab/roster focus glide, camera follow, the army roster (R), the header's
  activation counts and objective chips, unit markers, floating results, range rings (V), turn banners.

**Deferred minors:**
- The card does not mark which model a Shift-click or wound-row click selected; Home has a text "Settings" button where
  the spec has a gear.

**Standing rulings from the build:** the sight summary reads "k of n clear lines screened", not "in cover" (cover is a
rule; the client holds none); objective markers are locked in play under every enforcement setting until the marker's
menu unlocks them (client-local); terrain is locked from the end of setup, so a drag on it starts only in Setup (`TerrainLock`); the e2e sight check's
observer has only melee weapons, so it never proves a non-empty `CanShoot` (the unit tests do).

### 3. Smaller items

- Spells rulings standing until the owner says otherwise: choosing an innate Hit & Run or Teleport then moving nothing
  spends the use; a granted Counter-Attack or Unwieldy on a unit with no melee weapon stays until next used.

## Rules-suite gaps

What the suite does not check or report today. D1-D17 are fixed; no game is pinned.

- **Scourge's morale test lists nothing.** The shooting or spell morale test Scourge triggers does not list Scourge as applied (the
  activation-end hook records nothing); `change-disciples-scourge-morale@3.6` checks it through an `Evidence` predicate.

- **A refused placement cannot be shown by a suite game:** the run loop reads a refusal as Stuck, so Scout's 9" and clearance refusals
  under 3.6.0 are proven by `GfScout36Tests`, not by `core-deployment@3.6`.
- **Decided on faces as rolled:** Bane re-rolls inside roll-to-block and Shred's Applied are read before a `DiceFix` can land, so the
  fix cannot steer them; nor can the 3.5.1 Regeneration die (decided in `assign`). The games that need those faces roll them in volume
  (`special-regrowth-under-rending-fire`, `jackals-scrapper-boost`, `dark-elf-raiders-regenerative-strength`).
- **Charge-side bands not proven by a game:** the 6" bands of Guarded, Sturdy, Machine-Fog and Screened, and the charge side of Devout,
  Ferocious and Infected. Takedown X and Piercing/Precision Target X>1 are exact only at X=1. `SecondActivationClears` requires the unit Fatigued at its
  second activation, so each game builds a first-activation charge that leaves it Fatigued.

- **Fired is generous for suppressors.** `LiveProperties.ReadRemoved` (`server/game/Effects/LiveProperties.cs:59`) records a
  suppressor in `Applied` whenever a property it removed had an effect of the kind read, without checking that removed row's
  conditions. A suppressor can read as fired where the removed effect would not have applied.
- **Core clauses no game checks:** p.7 coherency (judged on every move answer, `move_coherency`) and the 1" separation from enemy models; p.9 base contact (`charge_contact`) and which models can
  strike; p.8 which models can shoot; p.11 impassable and elevated terrain; a unit activated in dangerous terrain; morale for a
  single model by its Tough.
- **The exit code cannot gate CI.** `SuiteReport.Passed` (`sim/Suite/SuiteRunner.cs`) is false whenever a pinned game fails, so a
  full run exits 1 while any known defect remains. Nothing tells unexpected failures apart in the exit code.
- **The viewer's Tests page shows no known or stale status.** `SuiteGameResult` in `viewer/src/lib/suite/client.ts` has no `known`,
  `pinned` or `stale`; `counts` (`model.ts`) counts a known failure as failed, the verdict is always FAIL, and stale pins never show.
  The failure lines still carry `known defect Dn:`.

## Known rule gaps

**Unique is a list-building rule.** The engine judges no army list, so Unique's suite game only plays to its end.

A new game starts with Frontline zones; the owner's games created before that have none, so their deployment is not
checked against a zone. The Aircraft move and "can't be charged" are built; the action restriction and the -12" to attackers' range
are built. A
Limited weapon with no eligible target is not marked used; the book is silent. `SetDice` (suite only) carries no
actor check.

**3.6.0 army books (generated).** `generate --edition 3.6` writes 49 books, 196 spells and `army-book-rules.part.json`
(90 generated, 0 text-only), plus `army-book-units.3.6.json`; 3.5.1 has `army-book-units.3.5.json`. The printed spell range
replaces Army Forge's `inRange` in 96 spells (the dump's value is 6" short of the text; each prints as `range from text`).
Printed text is the authority. Brace and Parry are described by no book and no rulebook page; Army Forge's common-rules endpoint
(`GET https://army-forge.onepagerules.com/api/rules/common/2`) has both: `fetch` (or `fetch --rules-only`) saves it as
`common-rules.json` beside the books and `generate` reads a name's description from it when no book describes the name.
Postgres refuses a spell entry with `played: false` (`catalog_entry_played_check`), and the game server then fails at startup.

**3.6.0 standing rulings** (spec → Rulings taken here): the Tags spend one token per attack automatically; Predator and Primal
ask (`extra_attacks`) once 2.5 merges, until then they always roll; Versatile Defense is chosen by the defender's team per
attack-sequence, before the hit roll; Brace is "-X total", one per weapon type, none when the defender is Fatigued or Shaken;
Parry's ignored hits are those with the most per-die grants, no Ask; once grants last the attack they first apply in and go at
`attack-end`; Mind Control's once-per-round is per unit moved. Owner's rulings of 2026-11-04: Smash and Pierce on a spell's hits read
the spell's printed range, measured from the caster model to the target's nearest model; Scourge on a spell's hits counts toward the
end-of-activation Morale Test, as shooting's does. Rule fixes to the printed text: a once grant on a move action (Shock Speed) lasts the
whole action across a stop and is spent at the move's end (p.7); Deadly reassigns a wound its model no longer needs and multiplies it on the new model; under 3.5.1 Fortified,
Fortified Growth, Guardian and Primeborn floor AP at 0, as printed. Enhanced Versatile Warrior is in neither part: no 3.6.0 book
carries or grants it. Enhanced Watchborn is Watchborn without the range band. Counter in Melee is read as Counter, and its strike is
not listed in the applied-rules log; only weapons named Counter add "Counter". When Pierce drops cover, the roll's expanded
details (`RollEntry.Details`, viewer `rollEntry.ts`) still read "In cover" with no Pierce line; the worked lines show it.

**Steep terrain.** The Steep declaration makes hills and dunes Difficult when moving up in 3.5.1 games too (3.5.1 p.12 "Hills — Cover
+ Difficult when moving up"; a rule fix moves every game). Layouts and drafts saved before migration 025 keep `steep = false` on their
hills.

**3.6.0 army import and melee.** An import classifies rule levels against the 3.6 catalog (a solo Unique Hero is a Hero on its model),
and the 3.6 melee result counts the defender's Fear whether or not it struck.

## How to work here

- **Read the documents in this order from cold:** this file, `docs/design/README.md`, `docs/architecture.md`, then the
  design doc for what you touch. Specs settle behaviour and design docs structure; a
  later document beats an earlier one.
- **Work runs from a spec.** `superpowers:writing-plans` turns the spec into a plan in the git-ignored `.superpowers/`
  workspace, never under `docs/`. Each plan runs on its own worktree and branch under one controller agent (`opus`), with
  `superpowers:subagent-driven-development` inside. Briefs are in `.superpowers/briefs/`, ledgers in
  `.superpowers/ledgers/`, reports in `.superpowers/reports/<tree>.md` (local). Every subagent names its
  model. A controller stops every helper it started before it reports.
- **Read the rulebook; do not reason about the rules.** Name a mechanic in the rulebook's words, and quote the page.
- **A ruling reaches every layer:** specs, design docs and their diagrams. Grep for the superseded fact.
- **State the current decision; never append history** in a document.
- **Ask the owner in the language of the game, not the code.**
- **Split a file over 500 lines** before editing it at length.

## Environment

WSL Ubuntu 26.04 with mirrored networking: Windows reaches WSL at `127.0.0.1`. `localhost` resolves to `::1` first,
which does not reach WSL, and .NET waits about 21 s before falling back. An old
`%APPDATA%\Godot\app_userdata\Tabletop\settings.json` carrying `localhost` has to be edited by hand. Godot 4.7.2 Mono on
the Windows host (`./scripts/godot -e`). No `pdftotext`, and headless Chrome cannot launch (missing `libnspr4`), so the
rulebook's diagrams are read by extracting images with `pypdf` (`./venv`) and Pillow.

Stytch: test project `project-test-7a0ad7e7-906a-434a-a3d2-bf90668f9f43`, project domain
`https://serious-chicory-4478.customers.stytch.dev`, public token
`public-token-test-198e0eff-b710-4c13-8258-1187add70b1d`. The code calls the Consumer API at `https://test.stytch.com`.
Two pieces of configuration live in the Stytch dashboard, not the repo: `http://localhost:5173/reset-password`
registered as a `RESET_PASSWORD` redirect URL, and the admin role. The test project refuses mail to unverified domains.

The admin role (admin watch spec): the viewer watches any game as an admin, and admin is the Stytch RBAC role `admin`.
Management stamps the user's role ids (`user.roles`) into the session's `roles` claim at login; the game server admits an
admin non-participant as a watcher. Setup: in the Stytch dashboard, Roles & Permissions (RBAC policy), create a role with
id `admin` (it needs no permissions) and save the policy; then, under Users, open the owner's user and assign it the `admin`
role (or `PUT /v1/users/{user_id}` with `{"roles": ["admin"]}`). It reaches the session at the next login or
refresh. `POST /v1/dev/sessions` with `"roles": ["admin"]` mints an admin without the dashboard.

**The rulebooks.** The skill `grimdark-future-rules` (plugin `licensed`, tracked in `.claude/skills/licensed/`) holds Grimdark Future
one file per printed page in two folders, `.claude/skills/licensed/skills/grimdark-future-rules/v3.5.1/` (`pages/p07.md` is p.7) and
`.../v3.6.0/`. The PDFs are in `/mnt/c/Users/clays/Downloads/` (`GF - Advanced_Rules_v3_5_1.pdf`, `GF_-_Advanced_Rulebook_v3_6_0.pdf`);
read nothing from older editions there. A bare `p.N` cites 3.5.1, `3.6.0 p.N` cites 3.6.0.
Citations use printed page numbers; `pypdf`'s `r.pages[N]` is printed page `N + 1`. Useful pages: 4-5 core rules and
line of sight, 6 objectives and the roll-off, 7 rounds, actions and coherency, 8-10 shooting, charging, melee and
morale, 11-12 terrain, 13-15 special rules, 17-19 terrain placement and generators, 22-24 deployment, 25-29 missions.

Army books: `https://army-forge.onepagerules.com/api/army-books?filters=official&gameSystemSlug=grimdark-future` lists
books; `.../api/army-books/{uid}?gameSystem=2` returns one. A shared list is `GET /api/tts?id={id}`, which import reads.
`./scripts/army-books fetch` and `generate` rebuild `server/game/Content/army-books/`.
