# Handoff — 2026-10-24

The game is built end to end and playable from the server's side: a Grimdark Future game runs from lobby and setup
through the roll-off, deployment, activations, shooting, melee, morale, casting, transports and objectives to a winner.
`sim/` plays whole games headless; `viewer/` draws them. On 2026-10-15 the owner played in the client and reported setup,
movement, log, marker and game-end problems; *2026-10-15* below was built in response, and *2026-10-16* settled the
combat log, split the tests into a unit suite and an E2E suite, and cleared most of the open handoff items; *2026-10-17*
finished the army-book rules, built optional rules and the production path, and hardened both servers for public hosting;
*2026-10-18* is the live home deployment and the owner's play-testing fixes; *2026-10-19* is path movement, the tabletop
look, the Ruins planet and the owner's deployment and setup fixes, released as v0.1.22; *2026-10-20* is games following the
current rules and the lifecycle suite; *2026-10-24* is held pieces lifting over, sound effects, the roll-off result and
four-team games. Released as v0.1.23. An undone roll's dice stay spent: an undo always draws fresh
dice (`DiceCursor.DrawnSoFar` counts the whole log).

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

### 2026-10-20

**Tagged: v0.1.22** (the owner deploys and says which is live on the home server `docker1` at
`https://tabletop.cstupi.com`, files at `https://files.tabletop.cstupi.com`).

On `main` and pushed (`68a242bc`):
- Owner ruling: "If a rule FIX is released it shouldnt publish a new version. everything should move to it.. its a fix".
  Every game not ended runs the set the game server seeded for its game rules: repinned at hydration, grants matched to the
  new set by entry and position path (spec `2026-10-19-games-follow-the-current-rules-design.md`). Checked live: a `playing`
  game on the old set moved on connect (one Information line); the ended TestAmbush kept its set.
- The lifecycle suite (spec `2026-10-20-lifecycle-suite-design.md`; `sim lifecycle`, `LifecycleTests` E2E): six shapes
  (`two-players`, `self-play`, `team-game`, `late-joiner`, `ambusher`, `leave-and-delete`) from `POST /v1/games` to delete,
  every step over `/v1` or the game socket as the client does, the status asserted at each step, the new game on the newest
  set (Fanatic, Ambush, Death Bolt present), `ended` in the row, replay over `/ws?watch=1`. Armies come from the dev-mode route
  `POST /v1/dev/armies/import` (a list body, the fetched import's code). It closes the gap the sim's socket driver leaves:
  `SocketRowSeeder` writes rows and starts games at `playing`.
- A player who left a lobby or setup game (the client's Leave: `POST /leave`, then the socket closes) stayed in the game server's
  lobby until the next edit, and `EndSetup` snapshotted the leaver's army onto the board. The session now reads the participant
  rows again on every lobby/setup connect, on a participant's last socket closing, and at `EndSetup` (`LobbyLeaveTests`).
  Found by the lifecycle suite.
- `GET /v1/properties` lists the newest seeded set first, so the create dialog defaults to the current rules. A set a failed
  `PropertiesStoreTests` run left in the dev DB ("Core 25453bf2", no army-book rules, no spells) sorted first by name, and 28
  dev games, the owner's Test and TestAmbush among them, ran without Fanatic and spells. The test now cleans up in `finally`.
- Casualty pick: a click on a picked model that can take no more puts it back (`CasualtyPick.Click`); a Clear button.

Next: a release when the owner says "tag it".

Release flow: tag `vX.Y.Z` on main and push when the owner says (owner ruling: "tag it" means tag and push at once, no
extra pre-tag run; the release runs its own tests); on docker1 `git fetch --tags && git checkout vX.Y.Z && scripts/release
--deploy home --version vX.Y.Z` (tests in containers, client built in the Godot builder container, publishes `releases/`
and `builtin/`, deploys). The owner's other Claude sessions take instructions over Remote Control: `docker1-logical-curry`
(server; it cannot run superuser DB queries without the owner's approval) and `syn01-piped-castle` (NAS; no root).

### 2026-10-19

Released on 2026-10-19 (v0.1.20–v0.1.22), beyond the bullets below:
- Setup: the print dropdown changes the board and names the board's print; Q/E held turn a held piece or models smoothly
  (120°/s, Shift 30°/s; `CameraFocus.HeldTurn`); Clear terrain (asked first; `ReplaceDraft` of the draft without terrain,
  which gives markers and zones new ids); the Ruins planet pack (`Planet.Ruins`: `ruined-corner`, `ruined-wall`,
  `rubble-field` meshes, the built-in mesh cap 40 MiB).
- Deployment: a model arriving with Show hitboxes off no longer throws in `TableView.Redraw` (it froze placement and lost
  the unit); placing after an undo (`DeployView.MidDeploy` reads the live log); one account on both teams follows the
  latest of its teams to start a turn or answer an Ask between turns (`Seats.Acting`), so the camera no longer swings to
  Team 1 and a round boundary's Ambusher pick can be placed; the decision box places a pick by any team of the account.
- `Session` raises each subscriber on its own (`Handlers.Raise`): one that throws no longer skips the render mark.
- The log over the dice tray no longer throws in `GameScreen.Layout`.
- The terrain-mesh staleness test reads `scripts/TerrainMeshes/meshes.sha256` (source and file hashes): a fresh build differs
  between C runtimes (v0.1.19 failed its release tests on it).

Seen in the real client on 2026-10-19 (WSLg harness): deployment with hitboxes off and on, self-play handover, undo then
re-place, print changes, held Q/E, Clear terrain. Not yet seen in the client: the Ambusher placement in self-play, the Ruins
planet pieces on the table, the movement area and bent ruler. In the harness no dropdown list opens (no keyboard focus).

Leads not reproduced (2026-10-19 client validation):
- Ctrl+Z pressed three times in a row each reported "Undone to #17" and stopped; the log's Undo to here still worked. A
  second run stepped back normally.
- A self-play Fanatic game reset to the round-0 roll-off between client restarts, losing its placements.

Dev-stack leftovers from 2026-10-19: games `repro soul-snatcher-cults-fanatic …` (several), `Validate setup`, and the
tabletop-look staging games under `look-dev`; delete when convenient. Leaked test sets (`Core …`, `GF test`) stay in
`management.properties`; deleting one needs its games deleted first (owner's call).

Built since v0.1.1 (details in the design docs and module AGENTS.md):
- **Client:** release builds always use their built-in servers; dev builds use `Tabletop-dev` and say "(dev)". Built-in
  content (catalog `client/content/builtin/catalog.json`, 33 terrain meshes from `scripts/TerrainMeshes`, 7 board prints from `scripts/TableTextures`) is
  packed as the fallback and published to MinIO `builtin/` by each release; `GET /v1/builtin` serves it and clients switch
  when `Tabletop-Builtin-Version` changes. Placement ghosts draw the model's mesh. Unit card groups like models with wound
  totals; terrain details/controls live in the unit card's slot (`TerrainCard`). Ruler: hold Ctrl (starts under the
  cursor), H toggles 2D/3D (`TapeMeasure`, `Measurement` mode on the wire), M click-click. Spell hover text on cast options
  and log rows. Terrain turns in setup (Q/E held turn it smoothly, Shift slower; card/menu when selected); drags slide along contacts.
- **Game flow:** deployment waits until every team has a player and an army (`WaitingForPlayers`; owner ruling: "You need
  an army on your team.", so a seat with no army does not hold it); a participant row is a seat (one
  team, one army, migration 018); one account may hold several seats (self-play); team games per p.50: turns alternate by
  team, and either player of the team takes the activation (and the deployment) with any unit of the team and answers every
  Ask to the team (owner ruling: "Either player from the team can take the activation across any unit in their team."). Under strict a team may not
  pass while a unit can activate (p.7). A unit set aside during deployment withdraws the stale deploy Ask
  (`SchedulerBase.Withdrawn`), including on hydration.
- **Web:** Sci-fi/Fantasy themes; GLBs default to glTF metres/Y-up with a size readout; the mesh preview orbits over the
  table with fill/rim light; asset page has the same scale/orientation tools; uploads land in the open folder; Library
  multi-select with bulk endpoints (≤ 200 ids); `/download` page.
- **Ops:** home stack logs to journald (docker1 caps the journal at 500M); servers log one line per message.
- **Owner rulings:** Caddy routes on docker1 are fine, Caddy config stays out of the repo; never touch Pi-hole; dice are
  never set by hand (`SetDice` is suite-only); behaviour decided in code is verified by unit tests, screen checks are for
  layout and look.

**Open / next:**
- Movement along the path (geometry `docs/design/geometry/movement.md`): a move is measured along the shortest path its base can
  take (p.5), round impassable terrain, other models and the table edge, and round difficult and dangerous terrain when that fits;
  a 12" Rush round a wood is not capped, a model reaches behind an impassable block, a charge goes round to base contact (p.9). The
  client draws one area (owner ruling: *"the visible feedback to users should just show the furthest they could move accounting for
  both if they cross or go around the difficult"*), holds a drag to it, and the ruler follows the bent path with its length. The wire
  is unchanged: each side computes the same path. The client's through-model mark keeps its path on the table and, once the unit
  is capped, within the cap. The sim's random moves go round models as the server allows; eight suite games were given new seeds
  for it. Suite game `core-round-the-terrain`. Not yet seen: the area and the bent ruler in the client.
- **Tabletop look** (spec `docs/superpowers/specs/2026-10-19-tabletop-look-design.md`): seven gaming-mat boards (grass, desert, urban, snow, ash, wasteland,
  alien) from `scripts/table-textures`, drawn through `board.gdshader` with derived normals and anti-tiling on a wooden
  table; `TableLighting` (warm shadowed key, cool fill, sky ambient, SSAO, AgX); every built-in mesh built with its
  detail in the geometry (`Crag`, `Masonry`, `Foliage` helpers), painted by `Bake` (AO wash, edge drybrush) and given
  triplanar surface grain by `TerrainLook`. Built-in content is 39.5 MiB of meshes and 12.5 MiB of prints. A draft now
  accepts Dangerous on Rush and Charge (`DraftLimits`), which the built-in ruins and quicksand carry and setup refused.
  Weak spots: broadleaf leaves read as small triangles close up; the urban and desert mats are bright under the key light;
  the hills' earth bands between ledges are smooth; building facades away from the light are dark.
- One socket suite run on 2026-10-19 failed one game that passed on the rerun; its name was lost. The suite's report file
  (`recordings/suite-latest.md`) now keeps it if it recurs.
- Alien Hives miniatures (owner's request, Army Forge `yrLpcpMH6fIA`): accepted by the owner as good enough to save an army, outside the repo at
  `C:\Users\clays\Downloads\alien-hives\` — `src\` holds the generator (numpy SDF → marching cubes → painted GLB), the
  list JSON and the owner's style reference; `draft\` holds all 12 models (three passes) and a side-by-side with the
  reference; `src\README.txt` lists the gaps. Not yet put on an army.
- Client Replay (`docs/design/client/replay.md`): Home's Replay on a game the player is in (`playing` or `ended`;
  "Games I'm in" lists `GET /v1/games?joined=true`) opens it read-only on `/ws?watch=1`, which the game server holds as a
  watcher for a participant or an admin, ended games included. The replay bar steps by round, turn, action and step,
  plays at 0.5–8×, scrubs, and the combat log follows the position.
- Admin Replays: the viewer's hosted build at `https://tabletop.cstupi.com/viewer/` (web image, no Caddy change). The
  website shows a Replays link to accounts with the `admin` role; the viewer reuses the website's session, lists every
  game including ended ones, and replays a game's log on its timeline over `wss://…/ws` (an admin watches any game).
- Management has no per-request log lines, so a manifest/asset fetch can't be traced in the journal; add an
  Information line per `GET /v1/games/{id}/assets` (game id, asset count).
- Miniatures generated for the owner (power-armour gunner, Soul-Snatcher Cults, Orc BOOM) live outside the repo in the
  owner's Downloads; their generator scripts are in the session scratchpad only.

### 2026-10-17

**Home deployment:** the owner's home server `docker1` runs the stack (`/opt/docker/tabletop`, compose project `tabletop-home`,
`deploy/docker-compose.home.yml`, `scripts/release --deploy home`), behind the owner's Caddy on `gateway_default`:
`https://tabletop.cstupi.com` (web, `/v1`, `/ws`) and `https://files.tabletop.cstupi.com` (MinIO, data on the NAS share
`/mnt/tabletop/minio`; nightly Postgres dumps to `/mnt/tabletop/pg-backups`). Stytch live project with custom domain
`login.tabletop.cstupi.com`. DNS in Route 53 (CNAMEs to `dub.cstupi.com`, updated by the owner's dynamic DNS). Not yet exercised live: `/ws`
end to end, a real sign-up. A management crash exits 139 rather than a clean code (seen when the DB login failed).

**Production path** (`deploy/README.md`): Release images (`server/*/Dockerfile`, non-root, Production) or systemd units,
`deploy/docker-compose.prod.yml`, `prod.env.example`. Caddy and the WAF are the owner's; `deploy/README.md` lists the
upstreams they front. Migrations run as the owner role through `Tabletop.Management --migrate`;
runtime roles `tabletop_management` / `tabletop_game` have DML only (`scripts/postgres/roles.sh`); MinIO signs with a
bucket-scoped service user (`scripts/minio/init.sh`). Dev mode is `ASPNETCORE_ENVIRONMENT=Development` plus
`TABLETOP_DEV_MODE=1` (compose, `scripts/management`, `scripts/game`); `StartupGuard` refuses dev settings anywhere else.
Upstream MinIO images are gone; both compose files use `pgsty/minio` pinned by digest; S3 or R2 is the production store.

**Security hardening** (four reviews: management, game server, deployment, uploaded content):
- Management: the dice seed is server-only (`GameInfo.RngSeed` `[JsonIgnore]`); `ArmyLimits` bounds import and patch;
  uploads go to `staging/`, are hashed and validated on confirm (`GlbHeader`, `ImageHeader`, `UploadLimits`), size caps
  and a locked per-account quota, `UploadSweeper`; asset references must be the caller's; the manifest presigns only
  participants' assets; rate limits per IP and per email behind `TABLETOP_TRUSTED_PROXIES` (addresses or CIDR); 1 MB
  bodies; enums by name; `Limits.Text` on names; new accounts are `Player-xxxx`; generic auth errors.
- Game server: bounded send queue, frame caps (8 KB connect, 64 KB, 512 KB for a layout), per-connection rates, 4
  sockets per account, Kestrel connection limits, JWKS refetch floor with a 1 h stale bound, close at JWT expiry; from
  `playing` a teamless participant is a watcher; `SetConfiguration` creator-only; `PickTeam` takes an empty team, or any
  team when the creator places someone (`ParticipantId`; no client UI for that yet); a throwing `Advance` stores nothing
  and the session stays live; dice per roll clamped to 1000; tables ≤ 144"; draft, army, presence and text bounds;
  asset ids in setup edits must belong to a participant; per-game hydration lock; the draft write is one batch.
- Client: `AssetCache` caps downloads; `GlbPreflight` checks a GLB before Godot parses it.
- Free-text "Mark as having taken…" is refused for an action the rules don't define.

**Releases** (spec `2026-10-17-client-releases-design.md`, `deploy/README.md` → Releases): `client/export_presets.cfg`
(Windows, Linux, macOS; unsigned); `scripts/release-client` exports and uploads to `releases/<version>/` plus
`releases/latest.json`; `scripts/release` builds, tests, publishes the client, installs the web build and deploys
(`--deploy docker|systemd|none`, `--dry-run`); `ci.yml` builds and tests main.
`GET /v1/client/latest` serves 15-minute links to signed-in users; the web app's `/download` page lists them; the
client's update-required screen links to it. The owner releases by hand on docker1.

**Client and web:** feedback text for refusals, closes and HTTP errors (`Feedback`); watcher mode; rename; lobby Players
list with the creator's team pickers; End/Delete/Leave; setup caps shown before sending; Reauth on the open socket;
Spawn/Split ring placement; 1" separation in the move gate; reserve chips; Add a grant. The web app has the client's
Sci-fi and Fantasy themes (`web/src/themes.css`), plain errors, upload progress and a download page.
Claude checks the client on screen itself (WSLg + Godot movie frames + xdotool); right-click popups are not captured.

**Rules:** under strict a team may not pass its activation while a unit can activate (p.7); warn records
`activation_required`.

**Army-book rules:** all play, including Spawn, Split, Reinforcement (reserve units the import adds,
`ArmyBookReserves`) and Takedown Shot / Strike (`ArmyBookAdditions`).

### 2026-10-16

- **Combat log settled** (C below): `main`'s build kept; the `combat-log` branch's extras ported.
- **F** checked over the socket: Shaken, Aircraft, Artillery and No Charge notes reach the wire.
- **Server rules:** a Scout's `set-aside` clears on its deploy turn (p.14); `Lookup.AuraGrants` reads models in play;
  p.15 passengers deploy only with their Transport, and only a unit never deployed and set aside exactly when the
  Transport is (`MayDeployInside`, `hand_move` breach); p.6/p.14 "fully within" the zone is a `deploy_zone` breach before
  round one when the game has zones (`ZoneStyles.BaseFullyWithin`); a Takedown pick offers only models the weapon reaches
  and sees with the target's own models ignored, and cover reads the picked model alone (p.14).
- **Movement:** `MoveRegion.IgnoresTerrain` (wire) keys the Aircraft landing exemption on Aircraft (p.13), not on
  `MinDistance`; a forced move out of a wall applies, one that stays in it is refused under strict.
- **Client:** every model of a move glides; `UnitCard` rebuilds only when `UnitCardContent` changes and keeps its scroll;
  a held model shows its own move region (`MoveRegions`); the drag clamp follows its reach (`MoveRegions.Clamp`); the
  re-grab rule is in Core (`DragBodies.Grabs`).
- **Base shape at import** (spec `2026-10-16-base-shape-at-import-design.md`): import takes `bases.round`, falling back
  to `bases.square` as a `Base.Rect`; there is no choice on the import form.
- **Management:** starter seeding's blob writes share a 5 s budget, so a MinIO outage holds a login at most that.
  Repositories sit behind interfaces (`IAccountRepository` …, `IMigrator`).
- **Viewer:** the live log labels a breached answer.
- **Docs:** `docs/schema.md` and the interpreter design reconciled with the spell library; both split
  (`docs/schema-diagram.md`; `docs/design/interpreter/` index plus `properties`, `resolution`, `decisions`,
  `interfaces`, `operations`).

**Owner rulings, 2026-10-16 (built):** account deletion deletes the games the account hosted and leaves the games it
joined (`IGameRepository.Leave`; accounts are normally locked, not deleted — locking is not built); base height is 1.5 ×
the larger dimension of the imported base.

**Passengers** (spec `2026-10-16-passengers-design.md`). During deployment a roster right-click on my shelved unit
offers **Put in transport: [Transport]** / **Take out of transport**: a hand move placing it `Inside` the still-shelved
Transport (`GfRules.MayDeployInside`: never deployed, matching set-aside, p.15 spaces), no deploy turn spent; it deploys
inside the Transport when that is placed. In play, a right-click on a model of my moving unit offers **Board transport:
[Transport]** when one is in reach. `ModelFacts.DeployInside` carries the choices. Suite game `core-transport-deploy`.
Embarking in play still does not check p.15's spaces.

**2026-10-17:** Ambush placement over 9" from enemy units (p.13, breach `ambush_distance`); under Takedown, Blast's cap and
the defence roll read the picked model (p.14); AP reads `AP(1)`, a granted one `Rending AP(+4)`; a die a rule rerolls carries its
new face's triggers (`RollRecord.FaceTriggers`); rolls, rerolls and modifiers are the rules' alone: `RerollDice`,
`AddModifier` and the log's Reroll / Set die / +1 buttons are gone (protocol 3); `SetDice` is accepted only with `GameServerOptions.AcceptsSetDice` (Development, and the sim's
in-process driver), for the rules suite's fixed dice.
Model against model is 3D (`libs/Tabletop.Geometry`: `Collision.Crosses`, `Slide`, `Separate`, `Distance.BodyToBody`,
movement paths): a model's volume is its base times 1.5 × the larger base dimension, so a model crossing above a lower
one passes it, and the 1" separation is measured in 3D. An Aircraft's placement ignores units and terrain (p.13): no 1"
separation, no impassable landing check; overlap still applies. A body rests on its open surface nearest the Z it
was given (`Collision.RestNear`), so a model can stand under a roof or floor with room.

### 2026-10-15

**A. Setup panel** (`SetupToolbar`). The selected piece's controls are first; Table, Terrain, Markers, Zones and Layouts
collapse under headings. In setup Delete/Backspace (`Intent.Remove`) removes the selected terrain or marker, and a
right-click on one opens `MenuModel.ForSetupPiece` (Remove). Objectives were always placeable; the button was buried.

**B. Move step** (`TableInput`, `.Hold`, `MoveRuler`). A drag picks a ghost up where it stands (`Drag.Grabs`; distance
from `Origins`), so Shift-adjusting one model keeps the unit's move. Q/E held with no rows turn each held model smoothly (p.7), each
with its own facing. One ruler per held model, along its path; without Alt a model is held to its reach (`MoveRegions.Clamp`);
`MoveRuler.Gate` counts models out of range by path length — under strict Enter and C wait, under warn and silent they send.

**C. Combat log** (spec `2026-10-13-combat-log-design.md`, plan `docs/superpowers/plans/2026-10-15-combat-log.md`).
One line per roll, faces against the target its modifiers make; question/answer/close, empty steps, sequence and turn
boundaries are dropped; a failed morale reads `<unit> Fled!`. Wire: `RollModifier.Via`, `Die.Triggered`, and
`RollRecord.Cover` (`CoverVerdict`: InCover, Models, Of, IgnoredBy) on every shooting defense roll, from
`GfRolls.InCover`. Lines read e.g. `Block · Defense 4+ (no cover: 0/3 models) · ⚀ ⚄ · 1 blocked, 1 wound`.
The `combat-log` branch's extras are ported onto `main` (2026-10-16): the nested `Cast [spell]` block,
`Hold → shoots [target]`, a single-die test's `failed → Shaken`, the roll-off's tie names and winner, grant and
cross-unit condition lines, repeated-undo headers, Hud row wrapping with a width floor and tray clipping; spell hit dice and
defense dice carry their triggers; an added attack die names no extra attacks.

**D. Markers, casting, set-aside, terrain labels.** Per-model red wound pips and blue spell-token dots
(`ModelMarks`, `ModelMarkNodes`); a yellow 12-spike ring round every base of a Shaken unit. The move step's stop button
reads **Cast a spell (C)** when a moving model holds tokens (`DecisionView.StopLabel`); tokens were always gained and spent
automatically (`GfHooks`, `GfCasting`). A roster row's right-click offers **Set aside** / **Return to deployment** for my
fully shelved Ambush or Scout unit (`MenuModel.ForRosterUnit`, condition `set-aside`). Hovering terrain in play shows
`TerrainLabel` (name, each volume's declarations and height); a click pins it. Uploaded terrain reads "Terrain": the asset
manifest has no names.

**E. Game end.** Ruling: strict ends after round 4; under warn each round end from round 4 asks every team
**End or Continue** (`Engine/GameOverAsk.cs`) — any End ends the game with strict's result and scores, all Continue plays
on; silent continues. After the end everyone stays in to view the board and score. `EndMessage` is a modal: End/Continue
while asked, then Stay/Leave. The sim answers End. Before this, warn games never ended, which is why the owner never saw
the end message.

**F. Team tint and limited-choice reasons.** Every model takes a light tint of its team colour (`Palette.Tinted`);
yellow stays the selection. `AskOption.Note` carries the ruleset's reason an activation choice is limited
(`GfRules.ActivationChoices`: Shaken → Idle p.10, Aircraft → Advance, Immobile/Artillery → Hold, no Charge without an enemy
in range); the decision box and the unit card show it. Checked over the socket: all four notes reach the wire.

**G. Owner rulings and requests.**
- Line of sight: the owner's "no line of sight" screenshot (game TestSetupCaster, `01a142ac-…`) was not a sight bug:
  every model draws in the same placeholder green (hence F), the yellow unit was their own Mutant Brutes, and the
  Assassins were really behind the 8" Large Building. A replay of every seq found no false block.
- "Your own units DO screen": a target unit's models screening its other models (`Sight.cs`) stays the default.
- Optional rules are built per game (spec `2026-10-15-optional-rules-design.md`, plan `2026-10-17-optional-rules.md`):
  `game.optional_rules` jsonb (migration 010), `GfOptions`, lobby buttons; `unit_sight_blocking` per_model|perimeter (convex
  hull of the bases; the book never defines the outline), `line_of_sight` core|hybrid_true (p.56; a Tough between classes
  stays in the lower class until the next named value), `melee_separation` on|off (p.10). Protocol version 2.

**Housekeeping.** Docker Desktop restarting takes every container down with exit 255;
`docker compose up -d` brings them back.

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
