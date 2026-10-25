# Handoff — 2026-10-24

The game is built end to end: a Grimdark Future game runs from lobby and setup through the roll-off, deployment,
activations, shooting, melee, morale, casting, transports and objectives to a winner, in the Godot client or the browser
(`/viewer/`). `sim/` plays whole games headless; `viewer/` also replays them. v0.1.23 is the latest tag; *Next* holds what is
on `main` since. What each earlier batch built, with the owner's rulings, is in `docs/shipped.md`. An undone roll's dice stay
spent: an undo always draws fresh dice (`DiceCursor.DrawnSoFar` counts the whole log).

## Get running

```bash
docker compose up -d     # Postgres 5432, MinIO 9000/9001, management 5100, game 5200 (dotnet watch), web 5173, viewer 5174 (Vite), sim-runner 5180
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
- The web app (`http://127.0.0.1:5173`, importing armies and meshes) and the viewer (`http://127.0.0.1:5174`) run as
  Vite dev servers in `node:24.19.0` containers, proxying `/v1` to the management container. They share `node_modules`
  with the host and run `npm ci` on start only when `package-lock.json` changed since the last install.
- Never `docker compose down -v`: the Postgres volume holds the schema the server tests read.
- Godot's import step wipes the client's build output. If every script fails with "class could not be found", run
  `./scripts/client` again.
- `./scripts/client` runs the game, not the editor, so it never imports. After a new font, texture or mesh lands
  (a pull, a merge), run `./scripts/godot --headless --import` once, then `./scripts/client`; otherwise the client fails
  at `ThemeBuilder.FontOf` with "Error loading resource".
- The client's log is `/mnt/c/Users/clays/AppData/Roaming/Godot/app_userdata/Tabletop/logs/godot.log` (the latest run;
  older runs are the dated files beside it). A crash can cut its tail off.

**The lifecycle suite.** `dotnet run --project sim -- lifecycle` plays six shapes against the compose stack by the client's
path (spec `2026-10-20-lifecycle-suite-design.md`, `sim/AGENTS.md` → The lifecycle suite): management over HTTP (create on the
first set of `GET /v1/properties`, join, army import through the dev-mode `POST /v1/dev/armies/import`, statuses, leave,
delete) and the socket (lobby and setup edits, waiting for players, play to the rules' end, replay on `/ws?watch=1`), on
`lifecycle-player-0..3` and `lifecycle-spectator`; nothing written to Postgres directly. Today 6 of 6 pass, about 2 min.

**The rules suite.** `dotnet run --project sim -- suite` plays every suite game in-process (about 45 s);
`--driver socket` plays them against the compose stack (about 12 min) on the fixed accounts `sim-player-0..3` and deletes
each game's rows, never the accounts.
`--only core,special,army:<book uid>,spells:<book uid>,<game name>` selects; `--keep` keeps recordings (and a socket run's
games, with `sim-spectator` as a participant for the live feed); `--list` lists games and what each covers. Every run writes
`recordings/suite-<run id>.md` and `.json` (copied to `suite-latest.md` and `.json`; rewritten after each game, so an aborted run
keeps what it played): the verdict, each failure with its lines and replay command, every game, then coverage. The console prints
a counter, each failure as it happens, the verdict line and the report paths; `--verbose` adds every game and the coverage table. The viewer's Tests page (`http://127.0.0.1:5174`, Tests tab) runs the same suite through `sim-runner`:
pick games, Keep, driver, Run; results stream in, then coverage; Watch opens a kept recording, Open live a kept socket game.
Today: 305 of 305 games pass in process and over the socket with no pin, 545 of 545 entries covered, 0 not automated. See `sim/AGENTS.md` → The rules suite.

**Checks.** `dotnet build` has 0 errors (9 analyzer warnings in test files). `./scripts/check-client` prints nothing.
Two suites (spec `docs/superpowers/specs/2026-10-16-test-suites-design.md`, `tests/AGENTS.md`):
- **Unit**, `dotnet test`: needs nothing running (servers over in-memory fakes; a test that reaches a live service is a
  defect). About 60 s. Geometry 228, Content 23, ArmyBooks 40, TerrainMeshes 114, TableTextures 29, Protocol 62, Viewer 9,
  Client 726, GameServer 1415, Management 276, Sim 305.
- **E2E**, `dotnet test --settings tests/e2e.runsettings` with the stack up: the Postgres and S3 adapters (shared contract
  tests run against fake and real), migrations, the suite games in process and over the socket, and the six lifecycle shapes
  against the compose stack (`LifecycleTests`, about 2 min). Management 47, GameServer 40, Sim 335; about 7 min.

`cd viewer && npm run check && npm test` passes (125); `cd web && npm test` passes 123 (1 skipped).
In a kept socket run's games, every hit, defense and morale `RollRecord` judged on its modified result carries `basis`
(`game.game_event`, `step_completed` payloads).
Every catalog scenario passes `--seeds 1..10`, `objectives` `--seeds 1..20`. `objectives` moves toward markers its team
does not hold (`ObjectiveGoal`), so every seed seizes at least once and `round_ended` is expected; a tie still passes.

## Next

### 2026-10-24

**Tagged: v0.1.23** (pushed). Specs `docs/superpowers/specs/2026-10-24-*`, plans `docs/superpowers/plans/2026-10-24-*`.
- **Held pieces lift over.** Every pick-up is lifted: models and terrain go straight to the cursor over everything, drawn
  0.5" above what is under them, a ring marking where they land; the drop lands at the nearest clear spot (a model within its
  reach in a move step, searched 24" out; a terrain piece clear of Impassable pieces). PageDown/PageUp step the held pieces
  down or up one surface (`HoldLevel`); at a level they slide along what is at that height and pass under a Solid with room.
  `Battlefield.Settle` and setup terrain edits (`DraftEditor.Rest`) rest on the open surface nearest the Z sent; without an
  overhang that is the old rest.
- **Sound effects.** Shoot, melee, move and wound cues (`SoundCues`, `SoundPlayer`), once per cue per live batch, never on
  catch-up, replay or undo; WAVs synthesised by `scripts/Sounds` into `client/content/sounds/`. Settings: Sound, Volume.
- **The roll-off result.** The roll-off no longer opens a Continue for every team (it still gates under the wait setting,
  which only the rules suite uses for fixed dice); a game stopped at the old gate resumes (`SequenceInterpreter.StaleGates`).
  Every client shows "Team k goes first" with each team's roll for 4 s ahead of the turn banner (`RollOffBanners`; the
  ranking step writes `rolled_<ordinal>_<k>`).
- **Four-team games.** Lifecycle shape `four-teams` (8 accounts, 2 a team, four army books) and `FreeForAllChecks` over every
  shape; `FourTeamGameTests`; suite game `core-four-teams`. Fixed: a game of three or four teams started setup with only the
  two long-edge zones, so teams 3 and 4 could deploy anywhere; it now starts on p.50's four corner areas
  (`ZoneStyles.FreeForAll`), and a team created during setup moves default zones to the corners.

- **Decision box hover.** Hovering an option's button lights its unit in `Palette.Hovered` and darkens the other choices, so
  same-named units tell apart.
- **Web: mesh pickers by folder.** The army page's pickers list one library folder (and its sub-folders), the army's choice,
  kept in the browser per army (`web/src/lib/armies/folder.ts`).

Validated on the merged `main`: unit suite all green, E2E 425/425 (the seven lifecycle shapes included), the rules suite in
process 306/306 and over the socket 305/306: `battle-brothers-re-deployment` stopped with `NoAskNoEvents` after its
re-deploy in the full socket run and passed three runs alone, so it reads as the socket driver's step timing under load
(`sim/Run/RunLoop.cs`), not a rule.

### 2026-10-24, after v0.1.23

On `main`, not pushed, not tagged.

- **Graphics settings** (spec `2026-10-24-graphics-settings-design.md`): Settings → Graphics: Display (Windowed, Fullscreen
  borderless), Quality (Low, Medium, High: MSAA, shadow splits and atlas, render scale), Render scale 50–100 % (FSR 1 below
  100 %), VSync; `Core/GraphicsSettings`, `Table/GraphicsApplier`. The table's `SubViewport` had no MSAA before; Medium is today's
  shadows with 2× MSAA. The window's size and position are kept.
- **Browser play** (spec `2026-10-24-browser-play-design.md`, plans `2026-10-24-browser-play-{a,b,c1,c2,d1,d2,d3,d4}*.md`): every
  signed-in player sets up, plays and replays games in the browser at `/viewer/` (the website header's **Play**), models as
  circles on the 2D board; Godot and the browser share a table.
  - Tables: `Tabletop.Content` `RandomTables` lays a planet's rolled terrain on the table, the same count of each kind per team
    (mirrored for two teams, one per team per round for three or four); `POST /v1/layouts/random`; four predefined 72×48 tables
    at `GET /v1/layouts/builtin`; Godot's Setup has the planet pick, Random table and the predefined tables. Wasteland reaches
    15 % difficult, which is what p.19's table holds (p.12's thirds are guidelines).
  - `libs/Tabletop.Geometry.Wasm`: Geometry compiled to WebAssembly (`viewer/src/lib/geometry/`, about 1.7 MB brotli, loaded
    only for setup and play); `scripts/geometry-wasm`, compose one-shot `geometry-wasm`; `/viewer/` CSP allows
    `'wasm-unsafe-eval'`. `RangeOutline` and `Contacts` moved into Geometry; the client calls them.
  - Viewer: games list, create, join, leave, end, delete; `GameConnection` (refusals matched by message id); lobby; setup
    (terrain, markers, zones, layouts, random); play (decision box for every Ask mode, drag moves and deploys landing as
    Godot's, contacts, refused commits, combat log matching Godot's on the fixtures, unit card, dice, Undo, header, roster,
    range rings, sight check, menus, waiting panel); replays on `/ws?watch=1`.
  - Stack tests (`cd viewer && TABLETOP_E2E=1 npm run test:e2e`) play lobby to setup, deployment drags, a refused move, and a
    whole round from the browser modules. The whole-round driver always holds, so move, shoot and assign Asks are covered by
    unit tests only there.
  - Not built: the browser's level keys (it always drops lifted), the reach outline, closest-base lines, a Leave menu item,
    banners beyond the roll-off.
- Validated on `main` after the last merge: unit suite all green, E2E 425/425, viewer 744 unit and 4 stack tests, web 128,
  rules suite 306/306 in process. Nothing has been looked at on screen yet.
- Next: the easy computer player (brainstormed: sensible single decisions scored on the game server, deterministic,
  difficulty levels later with combos, blocking and screening); its design is the next step.

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
`AddGrant` check included; `Battlefield.Terrain` builds a table's world volumes once per `TableState`. `SightLines` re-sends an assigned target's sight
query only when a model placement or the terrain changed (`SightKey`, `SightCache`).

**Open from the review:** the Release play profile for the dev stack and client (finding 1). Risks to watch: GLB parsing off the main
thread, template copies of rigged or animated models.

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

## Defects the rules suite found

D1-D16 are fixed; the suite has 306 games and passes with no pin. What stays open is in *Next* 1.

## Rules-suite gaps

What the suite does not check or report today.

- **Fired is generous for suppressors.** `LiveProperties.ReadRemoved` (`server/game/Effects/LiveProperties.cs:59`) records a
  suppressor in `Applied` whenever a property it removed had an effect of the kind read, without checking that removed row's
  conditions. A suppressor can read as fired where the removed effect would not have applied.
- **Core clauses no game checks:** p.7 coherency and the 1" separation from enemy models; p.9 base contact and which models can
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

**The rulebooks.** The skill `grimdark-future-rules` (local plugin `licensed`, not committed) holds Grimdark Future
v3.5.1 one file per printed page (`pages/p07.md` is p.7). The PDFs are in `/mnt/c/Users/clays/Downloads/`
(`GF - Advanced_Rules_v3_5_1.pdf`, and Age of Fantasy v3.5.1 beside it); read nothing from older editions there.
Citations use printed page numbers; `pypdf`'s `r.pages[N]` is printed page `N + 1`. Useful pages: 4-5 core rules and
line of sight, 6 objectives and the roll-off, 7 rounds, actions and coherency, 8-10 shooting, charging, melee and
morale, 11-12 terrain, 13-15 special rules, 17-19 terrain placement and generators, 22-24 deployment, 25-29 missions.

Army books: `https://army-forge.onepagerules.com/api/army-books?filters=official&gameSystemSlug=grimdark-future` lists
books; `.../api/army-books/{uid}?gameSystem=2` returns one. A shared list is `GET /api/tts?id={id}`, which import reads.
`./scripts/army-books fetch` and `generate` rebuild `server/game/Content/army-books/`.
