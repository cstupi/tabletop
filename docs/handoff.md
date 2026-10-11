# Handoff — 2026-10-09

The game is built end to end and playable from the server's side: a Grimdark Future game runs from lobby and setup
through the roll-off, deployment, activations, shooting, melee, morale, casting, transports and objectives to a winner
after round 4. `sim/` plays whole games headless; `viewer/` draws them. The rules suite (192 sim games over every catalog
rule and spell) found sixteen engine defects, D1-D16, all fixed. The in-game client is built to the in-game UX
spec, merged to `main`, and waits for the owner's on-screen check (*Next* 2); polish follows.

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
- `docker compose stop management game` frees the ports for `./scripts/management` and `dotnet run --project server/game`;
  `docker compose stop web viewer` does the same for `./scripts/web` and the viewer's `npm run dev`.
- The web app (`http://127.0.0.1:5173`, importing armies and meshes) and the viewer (`http://127.0.0.1:5174`) run as
  Vite dev servers in `node:24.19.0` containers, proxying `/v1` to the management container. They share `node_modules`
  with the host and run `npm ci` on start only when `package-lock.json` changed since the last install.
- Never `docker compose down -v`: the Postgres volume holds the schema the server tests read.
- Godot's import step wipes the client's build output. If every script fails with "class could not be found", run
  `./scripts/client` again.

**The rules suite.** `dotnet run --project sim -- suite` plays every suite game in-process (about 45 s);
`--driver socket` plays them against the compose stack (about 12 min) on the fixed accounts `sim-player-0..3` and deletes
each game's rows, never the accounts.
`--only core,special,army:<book uid>,spells:<book uid>,<game name>` selects; `--keep` keeps recordings (and a socket run's
games, with `sim-spectator` as a participant for the live feed); `--list` lists games and what each covers. A failing game prints its
replay command. The viewer's Tests page (`http://127.0.0.1:5174`, Tests tab) runs the same suite through `sim-runner`:
pick games, Keep, driver, Run; results stream in, then coverage; Watch opens a kept recording, Open live a kept socket game.
Today: 192 of 192 games pass on both drivers with no pin, 428 of 428 entries covered (71 by pattern), 108 not automated. See `sim/AGENTS.md` → The rules suite.

**Checks.** `dotnet build` has 0 errors (8 analyzer warnings in test files). `./scripts/check-client` prints nothing.
`dotnet test` passes 2058 with the stack up: Geometry 159, Content 17, ArmyBooks 36, Protocol 49, Viewer 9, Client 192,
GameServer 1035, Management 96, Sim 465. `cd web && npm test` and `cd viewer && npm test` cover the two web apps.
Every catalog scenario passes `--seeds 1..10`, `objectives` `--seeds 1..20`. `objectives` moves toward markers its team
does not hold (`ObjectiveGoal`), so every seed seizes at least once and `round_ended` is expected; a tie still passes.

## Next

### 1. Open items from the defect fixes

- **Owner decision (admin watch):** the spec has `GET /v1/games` list only the caller's games for a non-admin, but the client's Home
  screen is the public game browser players join from (`HomeScreen.LoadPage`, web-app spec → *Games are public, listed in the
  client*). The list stays public, so it already lists every game for an admin, and no `Admin` policy exists in management. Restricting
  it needs another way for a player to find a game to join.

- **Deploy caution:** games `playing` with an open sequence frame must be ended or abandoned before running this server. The step
  lists of attack-sequence, casting, spell-hits and morale-test changed (`SequenceInterpreter` indexes steps by position), so a
  parked frame can resolve hits twice. Nothing guards it; a guard needs a sequence version.
- **Owner decision:** dice edits cannot correct a cast, a morale test or score-hits in normal play (`WaitsBetweenSteps` off, so the
  deciding step runs in the same advance as the roll; `SequenceInterpreter.cs:62-69`). Smallest fix: a "stop before" flag on the
  `result`, `score-hits`, `override` and `consequence` steps reusing the Continue gate. Alternative: an atomic rewind-and-edit correction.
- **Six rolls still decide in the rolling step:** defence `extra_wounds` (`GfAttack.cs:44`), wound-ignore, dangerous terrain,
  Impact, attack-start faces, dice-distance moves.
- **D13:** a player's `AddModifier` lands in `Projection.PendingModifiers` (`Projection.cs:94`) but nothing reads it into a roll plan.
- **Owner rulings on movement:** a spell's forced move moves the unit however that unit moves (*"Spells move as however that
  unit moves"*): its region and its apply read the target's own Flying, Strider and Aircraft (`GfMoves.TerrainFor`). A granted
  Flying lasts the whole activation (*"Granted flying applies to the whole move - its per activation"*): once it applies it stays
  live for the activation-start test, every leg of a split move and the landing, and `GfHooks.OnTurnEnded` removes it when that
  activation ends (`GfMoves.ConsumeKeepingFlying`, a name check in one place); a spell's forced move of the unit flies with it but
  neither spends nor holds it, since that is not the unit's activation. On a table without 30" of room an Aircraft moves as
  far as it can in the direction taken (`GfMoves.Minimum`, `Movement.RoomAlong`).
- **Movement leftovers:** D8 suite evidence is weak; the client has no per-model cap region and no landing volumes for Flying; the
  client's Aircraft minimum ring (`MoveRuler.Minimum`) and the sim's `AnswerPolicy` still use the farthest-corner reach on a small table,
  stricter than the server's direction-taken minimum; the Aircraft landing exemption is keyed on `MinDistance` (`GfMoves.PathBreach`), not
  on Aircraft, so under strict a spell's forced move of an Aircraft may not land in impassable terrain; a forced move from inside a wall is a
  silent zero move; the start-in-impassable exemption is per volume (lenient); p.36 Banner of Eternal Flame's "may ignore all terrain
  effects" is unbuilt.
- **Content hash:** it sorts actions while the engine reads them in order (`PropertySetDocument.cs`), so reordering actions keeps the hash.
- **Threshold basis in Postgres (after the client-ux merge):** run `dotnet run --project sim -- suite --driver socket --keep`,
  then check a kept game's `step_completed` rows in `game.game_event`: every hit, defense and morale record without `requiredRaw`
  carries `basis` (the jq filter of plan `2026-10-10-client-ux/02-combat-log.md` Task 7 Step 4, over the `payload` column). The
  recordings cannot show it: they do not carry `basis`.

### 2. The in-game UX: the owner's on-screen check

Merged to `main` (spec `docs/superpowers/specs/2026-10-08-in-game-ux-design.md`, plan
`docs/superpowers/plans/2026-10-08-in-game-ux/`): the table to the edges with the header, stat sheet, decision box,
dice, log drawer and end message over it; selection, hover, formations, and target and casualty picks on the table;
the sight check, answered by the server; hand moves under the enforcement setting, with **Undo move**. New games start
with Frontline deployment zones, drawn through round 0. The server side is checked by
`tests/Tabletop.GameServer.Tests` and the sim.

**The owner's check** in the client (`./scripts/client`), the plan's Task 19, Step 6: layout, hover and distance,
selection and the stat sheet's two tabs, Shift for one model, formations (1–5 while holding, Q/E), the sight check (L),
shooting targets (chips, two targets), casualties (clicks, Auto; one-wound models read `Remove N models`, Tough models take wounds one model at a time), **Undo move** and Ctrl+Z, zones during deployment and
gone at round 1, the end message, F1, the shooting lasers (one second, shooter to nearest target model, none in melee),
Ctrl held to measure (live for the other player, gone on release).

**Standing rulings from the build:** the sight summary reads "k of n clear lines screened", not "in cover" (cover is a
rule; the client holds none); objective markers are locked in play under every enforcement setting until the marker's
menu unlocks them (client-local); a terrain drag in play starts under every setting, sends nothing and snaps back; the e2e sight check's
observer has only melee weapons, so it never proves a non-empty `CanShoot` (the unit tests do).

### 3. Check the terrain fix in the client

Merged (`4fbe694`). The built-in terrain meshes are glTF Y-up and were read as Z-up, which laid them on their side
and sank half of each into the table; `BuiltinContent.MeshOrientation` (pitch 90) stands them up. Check that hills,
woods and buildings stand upright with their bases flush on the table, and water and pads lie flat.

### 4. Base shape at import

Army Forge publishes a round and a square base for every unit (`bases.round`, `bases.square`: a tank `120x92` oval or
`100x60` rectangle, bikers `60x35` or `50x25`), with no field saying which units are vehicles. `ArmyForgeImport` reads
only `Bases.Round`. Owner ruling: the base set is picked when the army is imported. Model height stays as imported.

### 5. Smaller items

- Apply section 6 of `.superpowers/reports/spells.md` (docs corrections) to `docs/schema.md`, the interpreter design
  doc and the plans.
- Cleanup the owner runs; the permission check refuses these from Claude: `git worktree remove .worktrees/spells`,
  `git branch -D spells-pre-rebase-backup`, and the merged agent worktrees under `.claude/worktrees/`
  (`agent-a1bf2de16cf5e473e`, `agent-a5a23d5dd048008b7`, `agent-a926f6b5bf678900a`).
- Spells rulings standing until the owner says otherwise: choosing an innate Hit & Run or Teleport then moving nothing
  spends the use; a granted Counter-Attack or Unwieldy on a unit with no melee weapon stays until next used.
- A Scout unit keeps the `set-aside` condition after it deploys: `GfRules.Deployment.cs` clears it only for an Ambusher at the
  round boundary. Nothing reads it once the unit is on the table (every reader also requires all its models shelved); clear it
  when the Scout deploys.
- Management: a login blocks up to 100 s in starter-miniature seeding while MinIO is down, contrary to its own comment.

## Defects the rules suite found

D1-D16 are fixed; the suite passes 192/192 on both drivers with no pin. What stays open is in *Next* 1.

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

`Lookup.AuraGrants` reads `Definition.Models`, so a dead carrier still grants its rule by name. Harmless while only Scout Aura and
Ambush Aura (read at deployment) use it; it must read living carriers before an Aura of another name read through `Lookup.UnitHas`
is authored.

Passengers can deploy into a transport later than the transport: p.15 says *"units inside Transports are deployed at
the same time as the Transport"*, but deployment accepts placing a picked unit inside any friendly transport already on
the table.

**Army-book rules not yet played.** Every rule name the official books put on a unit, a weapon, an item or an upgrade
has a catalog entry. 71 are generated template wrappers (Aura, Buff, Mark) and play; 108 are generated text-only
entries (`played: false`): the stat sheet shows their Army Forge text tagged "not automated" and the engine plays them
as absent. The rules suite lists them and does not fail on them. They are the spec's batches 2–8
(`docs/superpowers/specs/2026-10-09-army-book-rules-design.md`) and the wrappers of bases those batches author.

**Unit-level names on armies imported before generation stay silent.** A pick entry or a carried Aura held as a
unit-level property, as armies imported before the generated part hold them, is offered by no `GfPick.Offers` and
granted by no `Lookup.AuraGrants`, which read model slots; nothing tags it "not automated".

**Unique is a list-building rule.** The engine judges no army list, so Unique's suite game only plays to its end.

Deployment is legal anywhere on the table: p.6's "fully within" the deployment zone is not checked. A new game
starts with Frontline zones, drawn only as a guide; the owner's games created before that have none. The Aircraft move and "can't be charged" are built; the action restriction and the -12" to attackers' range
are built. Takedown's picked model still blocks line of sight and gives cover, against p.14. A
Limited weapon with no eligible target is not marked used; the book is silent. `RerollDice` and `SetDice` carry no
actor check. Side-missions, the optional extra actions (Overwatch, p.33), action boosting, Advanced Casting's optional modules, army terrain, buildings as targets
and a second ruleset are not built.

## How to work here

- **Read the documents in this order from cold:** this file, `docs/design/README.md`, `docs/architecture.md`, then the
  design doc and plan for what you touch. Specs settle behaviour, plans what gets written, design docs structure; a
  later document beats an earlier one.
- **Work runs as trees.** Each plan runs on its own worktree and branch under one controller agent (`opus`), with
  `superpowers:subagent-driven-development` inside. Briefs are in `.superpowers/briefs/`, ledgers in
  `.superpowers/ledgers/`, reports in `.superpowers/reports/<tree>.md` (git-ignored, local). Every subagent names its
  model. A controller stops every helper it started before it reports.
- **Read the rulebook; do not reason about the rules.** Name a mechanic in the rulebook's words, and quote the page.
- **A ruling reaches every layer:** plans, specs, design docs and their diagrams. Grep for the superseded fact.
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
- The viewer's live log (`viewer/src/lib/live/logLines.ts:64`) ignores `AskAnswered.Breach`, so a breach-warned answer shows no label there.
