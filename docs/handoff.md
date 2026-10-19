# Handoff — 2026-10-18

The game is built end to end and playable from the server's side: a Grimdark Future game runs from lobby and setup
through the roll-off, deployment, activations, shooting, melee, morale, casting, transports and objectives to a winner.
`sim/` plays whole games headless; `viewer/` draws them. On 2026-10-15 the owner played in the client and reported setup,
movement, log, marker and game-end problems; *2026-10-15* below was built in response, and *2026-10-16* settled the
combat log, split the tests into a unit suite and an E2E suite, and cleared most of the open handoff items; *2026-10-17*
finished the army-book rules, built optional rules and the production path, and hardened both servers for public hosting;
*2026-10-18* is the live home deployment and the owner's play-testing fixes. All on `main`. An undone roll's dice stay spent: an undo always draws fresh
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

**The rules suite.** `dotnet run --project sim -- suite` plays every suite game in-process (about 45 s);
`--driver socket` plays them against the compose stack (about 12 min) on the fixed accounts `sim-player-0..3` and deletes
each game's rows, never the accounts.
`--only core,special,army:<book uid>,spells:<book uid>,<game name>` selects; `--keep` keeps recordings (and a socket run's
games, with `sim-spectator` as a participant for the live feed); `--list` lists games and what each covers. A failing game prints its
replay command. The viewer's Tests page (`http://127.0.0.1:5174`, Tests tab) runs the same suite through `sim-runner`:
pick games, Keep, driver, Run; results stream in, then coverage; Watch opens a kept recording, Open live a kept socket game.
Today: 194 of 194 games pass in process with no pin (193 last run over the socket), 428 of 428 entries covered (71 by pattern), 108 not automated. See `sim/AGENTS.md` → The rules suite.

**Checks.** `dotnet build` has 0 errors (9 analyzer warnings in test files). `./scripts/check-client` prints nothing.
Two suites (spec `docs/superpowers/specs/2026-10-16-test-suites-design.md`, `tests/AGENTS.md`):
- **Unit**, `dotnet test`: needs nothing running (servers over in-memory fakes; a test that reaches a live service is a
  defect). About 20 s. Geometry 193, Content 17, ArmyBooks 36, TerrainMeshes 5, Protocol 53, Viewer 9, Client 357,
  GameServer 1113, Management 130, Sim 276. Five consecutive full runs clean.
- **E2E**, `dotnet test --settings tests/e2e.runsettings` with the stack up: the Postgres and S3 adapters (shared contract
  tests run against fake and real), migrations, and the suite games in process and over the socket. Management 33,
  GameServer 34, Sim 212; about 15 min.

`cd viewer && npm run check && npm test` passes (112); `cd web && npm test` passes 63 (1 skipped).
In a kept socket run's games, every hit, defense and morale `RollRecord` judged on its modified result carries `basis`
(`game.game_event`, `step_completed` payloads).
Every catalog scenario passes `--seeds 1..10`, `objectives` `--seeds 1..20`. `objectives` moves toward markers its team
does not hold (`ObjectiveGoal`), so every seed seizes at least once and `round_ended` is expected; a tie still passes.

## Next

### 2026-10-18

**Live: v0.1.15** on the owner's home server `docker1` at `https://tabletop.cstupi.com` (files at
`https://files.tabletop.cstupi.com`). **Unreleased on `main`:** the Show hitboxes setting (B; `HitboxLook`) — ship it in
the next batch. Release flow (owner ruling: batch work, one tag per batch, run the release's own tests before tagging):
tag `vX.Y.Z` on main and push; on docker1 `git fetch --tags && git checkout vX.Y.Z && scripts/release --deploy home
--version vX.Y.Z` (tests in containers, client built in the Godot builder container, publishes `releases/` and `builtin/`,
deploys). The owner's other Claude sessions take instructions over Remote Control: `docker1-logical-curry` (server; it
cannot run superuser DB queries without the owner's approval) and `syn01-piped-castle` (NAS; no root). The owner's local
Docker is stopped; start it (`docker compose up -d`) before anything needs the dev stack.

Built since v0.1.1 (details in the design docs and module AGENTS.md):
- **Client:** release builds always use their built-in servers; dev builds use `Tabletop-dev` and say "(dev)". Built-in
  content (catalog `client/content/builtin/catalog.json`, 30 terrain meshes from `scripts/TerrainMeshes`, 5 textures) is
  packed as the fallback and published to MinIO `builtin/` by each release; `GET /v1/builtin` serves it and clients switch
  when `Tabletop-Builtin-Version` changes. Placement ghosts draw the model's mesh. Unit card groups like models with wound
  totals; terrain details/controls live in the unit card's slot (`TerrainCard`). Ruler: hold Ctrl (starts under the
  cursor), H toggles 2D/3D (`TapeMeasure`, `Measurement` mode on the wire), M click-click. Spell hover text on cast options
  and log rows. Terrain turns in setup (Q/E while held, Shift 5°, card/menu when selected); drags slide along contacts.
- **Game flow:** deployment waits until every seat has an army (`WaitingForPlayers`); a participant row is a seat (one
  team, one army, migration 018); one account may hold several seats (self-play); team games per p.50: turns alternate by
  team, each seat commands only its own units (`Ask.Seat`, `OwnUnitsOnly`, `SeatAuthority`). Under strict a team may not
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
- Ship the hitbox setting (next batch). Owner hasn't confirmed the new default that terrain volumes show faintly under a
  mesh while the setting is on (previously always hidden); revert to hidden if they object.
- Replay: nothing deployed can replay a game. Owner asked; options offered: a Replay mode in the client (the client
  already projects any log prefix) or the developer viewer behind an admin login. Not chosen yet.
- Management has no per-request log lines, so a manifest/asset fetch can't be traced in the journal; add an
  Information line per `GET /v1/games/{id}/assets` (game id, asset count).
- Team games leave team-wide: spell-token spending, hand-move overrides, and passing the team's activation.
- The waiting panel's army choice is one button per army (its dropdown would not open there; cause not found).
- Miniatures generated for the owner (power-armour gunner, Soul-Snatcher Cults, Orc BOOM) live outside the repo in the
  owner's Downloads; their generator scripts are in the session scratchpad only.

### 2026-10-17

**Home deployment:** the owner's home server `docker1` runs the stack (`/opt/docker/tabletop`, compose project `tabletop-home`,
`deploy/docker-compose.home.yml`, `scripts/release --deploy home`), behind the owner's Caddy on `gateway_default`:
`https://tabletop.cstupi.com` (web, `/v1`, `/ws`) and `https://files.tabletop.cstupi.com` (MinIO, data on the NAS share
`/mnt/tabletop/minio`; nightly Postgres dumps to `/mnt/tabletop/pg-backups`). Stytch live project with custom domain
`login.tabletop.cstupi.com`. DNS in Route 53 (CNAMEs to `dub.cstupi.com`, updated by the owner's dynamic DNS). Client
releases still need the GitHub secrets `RELEASE_S3_*` and variable `TABLETOP_WEB_URL`. Not yet exercised live: `/ws`
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
(`--deploy docker|systemd|none`, `--dry-run`); `.github/workflows/release.yml` on `v*.*.*` tags and `ci.yml` on main.
`GET /v1/client/latest` serves 15-minute links to signed-in users; the web app's `/download` page lists them; the
client's update-required screen links to it. Operator: GitHub secrets `RELEASE_S3_*`, variable `TABLETOP_WEB_URL`.

**Client and web:** feedback text for refusals, closes and HTTP errors (`Feedback`); watcher mode; rename; lobby Players
list with the creator's team pickers; End/Delete/Leave; setup caps shown before sending; Reauth on the open socket;
Spawn/Split ring placement; 1" separation in the move gate; reserve chips; Add a grant. The web app has the client's
Sci-fi and Fantasy themes (`web/src/themes.css`), plain errors, upload progress and a download page. Built-in terrain is
procedural and coloured (`scripts/TerrainMeshes`, 13 meshes incl. `lake` and `crater`), with new table lighting.
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
  a held model shows its own move region (`MoveRegions`); the drag clamp follows `Movement.Region` (difficult cap); the
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
`Movement.Region`): a model's volume is its base times 1.5 × the larger base dimension, so a model crossing above a lower
one passes it, and the 1" separation is measured in 3D. An Aircraft's placement ignores units and terrain (p.13): no 1"
separation, no impassable landing check; overlap still applies. Limit: `Collision.RestHeight` puts a model touching a
Solid on top of it, so no model stands under a roof or floor.

### 2026-10-15

**A. Setup panel** (`SetupToolbar`). The selected piece's controls are first; Table, Terrain, Markers, Zones and Layouts
collapse under headings. In setup Delete/Backspace (`Intent.Remove`) removes the selected terrain or marker, and a
right-click on one opens `MenuModel.ForSetupPiece` (Remove). Objectives were always placeable; the button was buried.

**B. Move step** (`TableInput`, `.Hold`, `MoveRuler`). A drag picks a ghost up where it stands (`Drag.Grabs`; distance
from `Origins`), so Shift-adjusting one model keeps the unit's move. Q/E with no rows turn each held model 15° (p.7), each
with its own facing. One ruler per held model; without Alt a model is held to its allowance (`MoveRuler.Clamp`);
`MoveRuler.Gate` counts models out of range — under strict Enter and C wait, under warn and silent they send (its count is
still straight-line).

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

**Open from the review:** range rings (24 ms per redraw, ~1.5M distance calls), an incremental combat-log panel, a by-id
index for grant rows. Risks to watch:
GLB parsing off the main thread, template copies of rigged or animated models.

### 1. Open items from the defect fixes

- **Deploy caution:** games `playing` with an open sequence frame must be ended or abandoned before running this server. The step
  lists of attack-sequence, casting, spell-hits and morale-test changed (`SequenceInterpreter` indexes steps by position), so a
  parked frame can resolve hits twice. Nothing guards it; a guard needs a sequence version.
- **Six rolls still decide in the rolling step:** defence `extra_wounds` (`GfAttack.cs:44`), wound-ignore, dangerous terrain,
  Impact, attack-start faces, dice-distance moves.
- **Owner rulings on movement:** a spell's forced move moves the unit however that unit moves (*"Spells move as however that
  unit moves"*): its region and its apply read the target's own Flying, Strider and Aircraft (`GfMoves.TerrainFor`). A granted
  Flying lasts the whole activation (*"Granted flying applies to the whole move - its per activation"*): once it applies it stays
  live for the activation-start test, every leg of a split move and the landing, and `GfHooks.OnTurnEnded` removes it when that
  activation ends (`GfMoves.ConsumeKeepingFlying`, a name check in one place); a spell's forced move of the unit flies with it but
  neither spends nor holds it, since that is not the unit's activation. On a table without 30" of room an Aircraft moves as
  far as it can in the direction taken (`GfMoves.Minimum`, `Movement.RoomAlong`).
- **Movement leftovers:** D8 suite evidence is weak; the client has no per-model cap region and no landing volumes for Flying; the
  client's Aircraft minimum ring (`MoveRuler.Minimum`) and the sim's `AnswerPolicy` still use the farthest-corner reach on a small table,
  stricter than the server's direction-taken minimum; the start-in-impassable exemption is per volume (lenient); p.36 Banner of Eternal Flame's "may ignore all terrain
  effects" is unbuilt.
- **Content hash:** it sorts actions while the engine reads them in order (`PropertySetDocument.cs`), so reordering actions keeps the hash.

### 2. The client

**In-game UX** (merged to `main`; spec `docs/superpowers/specs/2026-10-08-in-game-ux-design.md`): the table to the edges with the header, decision box, dice, log drawer and
end message over it; selection, hover, formations, and target and casualty picks on the table; the sight check, answered by
the server; hand moves under the enforcement setting. New games start with Frontline deployment zones,
drawn through round 0.

**Client UX** (spec `docs/superpowers/specs/2026-10-10-client-ux-and-themes-design.md`):

- **Collision.** A drag slides along Impassable Solids, stops at other models' bases and rests on the highest Solid under the
  base (`Collision.Slide`, `Separate`, `RestHeight`); Alt places freely. The server rests every placement (`Battlefield.Settle`)
  and judges a move answer's p.7 contact clauses under the enforcement setting (`GfMoves.ContactBreach`: `move_overlap`,
  `move_through_models` by `Collision.Crosses`, `move_separation`) and a hand placement's overlap (`PlacementBreach`). The
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
- The book-table ruins (`ruins`, `large-ruins`, `overgrown-ruins`) keep p.12's declarations; only the built-in `ruin` is
  BlocksLineOfSight and SeeInAndOut, a walk-in Difficult Area with Cover.
- Under `strict` the client shows no warning for a drag the server refuses: a landing within 1" of another unit, a path
  through a model, and a flyer's drag or a placement dropped on a base.
- The sim can stall under `strict`: `AnswerPolicy`'s MinDistance fallbacks bypass `Clear`, and `HandPlacement`'s
  zone-centre fallback after 40 rejected anchors is unchecked against occupied bases.
- Range rings make about 1.5M distance calls per redraw for 20 packed carriers.

**Standing rulings from the build:** the sight summary reads "k of n clear lines screened", not "in cover" (cover is a
rule; the client holds none); objective markers are locked in play under every enforcement setting until the marker's
menu unlocks them (client-local); terrain is locked from the end of setup, so a drag on it starts only in Setup (`TerrainLock`); the e2e sight check's
observer has only melee weapons, so it never proves a non-empty `CanShoot` (the unit tests do).

### 3. Smaller items

- Spells rulings standing until the owner says otherwise: choosing an innate Hit & Run or Teleport then moving nothing
  spends the use; a granted Counter-Attack or Unwieldy on a unit with no melee weapon stays until next used.

## Defects the rules suite found

D1-D16 are fixed; the suite passes 193/193 on both drivers with no pin. What stays open is in *Next* 1.

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
actor check. Side-missions, the optional extra actions (Overwatch, p.33), action boosting, Advanced Casting's optional modules, army terrain, buildings as targets
and a second ruleset are not built.

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
