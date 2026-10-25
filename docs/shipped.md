# Shipped through v0.1.23

What each batch up to v0.1.23 built, with the owner's rulings made along the way. `docs/handoff.md` holds the current state.

## 2026-10-20

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

## 2026-10-19

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

## 2026-10-17

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

## 2026-10-16

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

## 2026-10-15

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

