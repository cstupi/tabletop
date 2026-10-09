# Handoff — 2026-10-09

The game is built end to end and playable from the server's side: a Grimdark Future game runs from lobby and setup
through the roll-off, deployment, activations, shooting, melee, morale, casting, transports and objectives to a winner
after round 4. `sim/` plays whole games headless; `viewer/` draws them. The rules suite (128 sim games over every catalog
rule and spell) found twelve engine defects, listed below as the next work. The in-game client is built to the in-game UX
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
`--driver socket` plays them against the compose stack (about 12 min) and deletes every row and account it made.
`--only core,special,army:<book uid>,spells:<book uid>,<game name>` selects; `--keep` keeps recordings (and a socket run's
games, with a spectator account for the live feed); `--list` lists games and what each covers. A failing game prints its
replay command. The viewer's Tests page (`http://127.0.0.1:5174`, Tests tab) runs the same suite through `sim-runner`:
pick games, Keep, driver, Run; results stream in, then coverage; Watch opens a kept recording, Open live a kept socket game.
Today: 113 of 128 games pass in-process (112 over the socket), every other game fails only on a pinned known defect
(report line `KNOWN Dn`), 296 of 296 entries covered. See `sim/AGENTS.md` → The rules suite.

**Checks.** `dotnet build` has 0 errors (8 analyzer warnings in test files). `./scripts/check-client` prints nothing.
`dotnet test` passes 1795 with the stack up: Geometry 157, Content 17, ArmyBooks 25, Protocol 47, Viewer 9, Client 179,
GameServer 887, Management 94, Sim 380. `cd web && npm test` and `cd viewer && npm test` cover the two web apps.
Every catalog scenario passes `--seeds 1..10`, `objectives` `--seeds 1..20`. `objectives` moves toward markers its team
does not hold (`ObjectiveGoal`), so every seed seizes at least once and `round_ended` is expected; a tie still passes.

## Next

### 1. Fix the engine defects the rules suite found

Twelve defects, each pinned to a failing suite game (*Defects the rules suite found*, below). Fix each in
`server/game/` test-first, then drop its pin (`.Pinned(...)` in `sim/Suite/SuiteCatalog.*.cs`, and the test tables in
`tests/Tabletop.Sim.Tests/Suite/`); a pinned game that passes shows `(pinned Dn did not fail: <check>)` in the report. D9's fix
leaves five spells covered only by raw-1 passes (Psychic Stabilization, Cleaving Rune, Brain Infestation, Corrode Weapons,
Mutating Inferno): their spell games' scripts must still bring a legal pass. Once D2 is fixed, `core-rough-ground`'s
policy may need to keep a unit's moves within 6" under strict.

### 2. The in-game UX: the owner's on-screen check

Merged to `main` (spec `docs/superpowers/specs/2026-10-08-in-game-ux-design.md`, plan
`docs/superpowers/plans/2026-10-08-in-game-ux/`): the table to the edges with the header, stat sheet, decision box,
dice, log drawer and end message over it; selection, hover, formations, and target and casualty picks on the table;
the sight check, answered by the server; hand moves under the enforcement setting, with **Undo move**. New games start
with Frontline deployment zones, drawn through round 0. The server side is checked by `.superpowers/e2e/`
(`E2E_SCENARIO=ingame`, once per setting) and the sim.

**The owner's check** in the client (`./scripts/client`), the plan's Task 19, Step 6: layout, hover and distance,
selection and the stat sheet's two tabs, Shift for one model, formations (1–5 while holding, Q/E), the sight check (L),
shooting targets (chips, two targets), casualties (clicks, Auto), **Undo move** and Ctrl+Z, zones during deployment and
gone at round 1, the end message, F1.

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
  spends the use; a granted Flying is spent by the first leg of a split move; a granted Counter-Attack or Unwieldy on a
  unit with no melee weapon stays until next used.
- A Scout unit keeps the `set-aside` condition after it deploys: `GfRules.Deployment.cs` clears it only for an Ambusher at the
  round boundary. Nothing reads it once the unit is on the table (every reader also requires all its models shelved); clear it
  when the Scout deploys.
- Management: a login blocks up to 100 s in starter-miniature seeding while MinIO is down, contrary to its own comment.

## Defects the rules suite found

Each defect fails the named suite game on both drivers unless noted; quoted evidence is the game's failure text.

| | Rule | Code | Evidence game | Fix direction |
| --- | --- | --- | --- | --- |
| D1 | p.8: "all weapons are fired simultaneously"; a later weapon group whose target has no model left removes nothing | `GfAttack.AssignStep` (`Rules/GrimdarkFuture/GfAttack.cs:115-134`) opens `remove_casualties` as an `Allocation` over no eligible model, with no pass: the game sticks | `core-overkill` (seed 7) | Finish the group when no model is eligible (its wounds are lost); then also assert the second group removed nothing |
| D2 | p.11: "If any model in a unit moves in or through difficult terrain ... then all models in the unit may not move more than 6"" | `GfMoves.ApplyMove` (`GfMoves.cs:199-245`) computes `crossedDifficult` only for `Applied`; each model's cap comes from its own `Movement.Check` | `core-rough-ground` ("a rush into the bog moved a model 9.49"") | Cap every model of the unit at 6" when any model's path enters difficult ground, under strict |
| D3 | p.7: "models may also never move outside of the battlefield (no part of them, at any point)" | `GfMoves.ExitPlacement` (`GfMoves.cs:82`) checks the 6" exit distance, not the table; it also serves the destroyed-transport placement (`GfContainers.cs:67`) | `core-transport-run` (a disembarked model off a 24x36 table) | Check the base against the table in `ExitPlacement` |
| D4 | Slayer (Soul Snatcher Cults): "its weapons get AP(+2) if most models in the target have Tough(3) or higher" | `GfMelee.ChargeTargets` (`GfMelee.cs:106-108`) settles a melee `Interaction` with no defender; an opponent-subject condition (`MostModels`, `HoldsProperty`, `ActorState`) calls `Interaction.Of`, which throws "no defender in this interaction": the engine faults | `soul-snatcher-cults-slayers-charging` | Make opponent-subject conditions false with no defender (as `PositionTerrain` already is), or settle per candidate target |
| D5 | Resistance, Self-Repair, Plaguebound: "roll one die for each" wound; p.14 Regeneration the same | `GfAttack.AssignStep` (`GfAttack.cs:150-175`): wounds spilled past a dead model re-enter the step and take the ignore roll again | `havoc-brothers-resistance`, `robot-legions-self-repair`, `robot-legions-self-repair-boost`, `plague-disciples-plaguebound`, `plague-disciples-plaguebound-boost` ("4 dice [3 2 1 1] for 3 wounds") | Roll the ignore die once per wound; carried wounds keep their result |
| D6 | Scrapper Boost, Mischievous Boost: "must re-roll successful unmodified defense results of 5-6" | The Boost rows are `force_reroll` on `roll_result eq 5 [defense, raw]`; `Conditions.Holds` (`Effects/Conditions.cs:22-24`) compares raw or modified only, so a raw 5 that AP made fail is re-rolled | `jackals-scrapper-boost` | A roll-result predicate on the die's success, and the two rows authored with it |
| D7 | p.13 Aircraft: "May only use Advance actions ... adding 30" to its total move" (straight line, ignoring units and terrain) | Not built: the Advance allowance has no Aircraft term | `special-aircraft-overhead` (allowance 6, expected 36) | Build the Aircraft move |
| D8 | p.13 Aircraft: "can't be charged" | `GfMelee.ChargeTargets` (`GfMelee.cs:106-123`) filters by team, distance and impassable terrain only | `special-aircraft-overhead` (the Aircraft offered as a charge target) | Leave Aircraft out of the charge targets |
| D9 | p.5: "a roll of 6 always counts as a success and a roll of 1 always counts as a fail, regardless of how much it is being modified by" | `Roller.Judge` (`Dice/Roller.cs:13`) judges a thresholded roll by `modified >= threshold` only (a `RequiredRaw` roll compares the raw face), at the roll, on a reroll and on `SetDice` | `spells-human-defense-force` (casting "raw 1, modified 5: judged a success") | Raw 1 fails and raw 6 succeeds on thresholded rolls |
| D10 | Every roll result is editable (`docs/design/client/design.md`); p.13 casting "on 4+ resolve the effect"; p.14 Surge and Rending's extra hit "on unmodified results of 6" | Values a step stores when it rolls are not re-derived after `SetDice`: casting `passed` (`GfCasting.cs:172`, and a failed cast's frame ends in that step, so it cannot be corrected), `extra_hits` on attacks (`GfAttack.cs:434-444`) and spell hits (`GfCasting.cs:255-274`); morale finishes in the step that rolls | `spells-edited-casting-die` (a cast edited to a raw 1 still resolves), `blessed-sisters-devout-edited-dice` (extra hits frozen) | Derive these values in a later step from the dice as they stand (as `GfAttack.HitsFrom` does), or gate the decision behind its own step |
| D11 | p.14 Stealth "shot from over 9" away"; also Relentless, Artillery, Piercing Hunter; p.5 measures base to base | `Conditions.Holds` `Range` (`Effects/Conditions.cs:21`) floors the distance: 9.41" counts as 9, not over 9 | `special-stealth-at-nine-and-a-half` (a 9.41" shot with no Stealth modifier) | Compare the measured distance, unrounded |
| D12 | Socket only: the set read from Postgres must evaluate as the document does | `PostgresPropertiesStore` (`Properties/PostgresPropertiesStore.cs:21-23`) orders conditions by id (UUIDv7 minted in one loop, so random within a millisecond) and condition values by value; no condition ordinal is stored | `soul-snatcher-cults-slayers` over the socket (Slayer's opponent condition judged first, then D4's throw); may pass on a different seeding | Store a condition ordinal and order by it; the D4 fix removes the throw |

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
- **The 231 army-book rules outside the catalog are not reported.** The spec says the suite reports them as outside the catalog
  (*Known rule gaps*, below); the suite measures the catalog only.

## Known rule gaps

Passengers can deploy into a transport later than the transport: p.15 says *"units inside Transports are deployed at
the same time as the Transport"*, but deployment accepts placing a picked unit inside any friendly transport already on
the table.

**Most army-book rules do nothing in play.** The official books put 313 rule names on units and upgrades. 231 of them
have no `catalog_entry` (Precise, Fortified, Regeneration Aura, Mend, Versatile Attack, Agile, Armor, Unique, …), and
`LiveProperties.Gather` skips a name the set lacks, so a unit carrying one plays without it and nobody is told. The
spell-library spec recorded this ("the other 261 army-book rule names"); it is its own work item after the in-game UX:
(1) the army-book generator writes a text-only entry (name, kind, aggregation, the book's `description` as
`rule_text`) for every rule the books define, all 361 descriptions being in the Army Forge dump; (2)
`GET /v1/properties/{hash}/rules` delivers the texts and the stat sheet tags a rule with no entry "not automated", so a
text-only entry needs an explicit "played" flag for the tag to stay true;
(3) the effects are authored, many by pattern (Aura, Buff, Mark, Boost), under their own design and plan. Army import drops the export's `specialRules`
descriptions; the catalog, not the army, is where rule text lives.

Deployment is legal anywhere on the table: p.6's "fully within" the deployment zone is not checked. A new game
starts with Frontline zones, drawn only as a guide; the owner's games created before that have none. The Assignment
legal set carries one target list for every weapon group, so the server checks an answer's targets against the union
and not per group: a split-fire target one group cannot legally shoot is accepted for that group. Aircraft movement and "can't be charged" are D7 and D8 above; the action restriction and the -12" to attackers' range
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
One piece of configuration lives in the Stytch dashboard, not the repo: `http://localhost:5173/reset-password`
registered as a `RESET_PASSWORD` redirect URL. The test project refuses mail to unverified domains.

**The rulebooks.** The skill `grimdark-future-rules` (local plugin `licensed`, not committed) holds Grimdark Future
v3.5.1 one file per printed page (`pages/p07.md` is p.7). The PDFs are in `/mnt/c/Users/clays/Downloads/`
(`GF - Advanced_Rules_v3_5_1.pdf`, and Age of Fantasy v3.5.1 beside it); read nothing from older editions there.
Citations use printed page numbers; `pypdf`'s `r.pages[N]` is printed page `N + 1`. Useful pages: 4-5 core rules and
line of sight, 6 objectives and the roll-off, 7 rounds, actions and coherency, 8-10 shooting, charging, melee and
morale, 11-12 terrain, 13-15 special rules, 17-19 terrain placement and generators, 22-24 deployment, 25-29 missions.

Army books: `https://army-forge.onepagerules.com/api/army-books?filters=official&gameSystemSlug=grimdark-future` lists
books; `.../api/army-books/{uid}?gameSystem=2` returns one. A shared list is `GET /api/tts?id={id}`, which import reads.
`./scripts/army-books fetch` and `generate` rebuild `server/game/Content/army-books/`.
