# Handoff — 2026-10-09

The game is built end to end and playable from the server's side: a Grimdark Future game runs from lobby and setup
through the roll-off, deployment, activations, shooting, melee, morale, casting, transports and objectives to a winner
after round 4. `sim/` plays whole games headless; `viewer/` draws them. The in-game client is built to the
in-game UX spec, merged to `main`, and waits for the owner's on-screen check (*Next* 1); polish follows.

## Get running

```bash
docker compose up -d     # Postgres 5432, MinIO 9000/9001, management 5100, game server 5200 (both under dotnet watch)
./scripts/client         # build, check, launch the Godot client on Windows
./scripts/web            # only for importing armies and meshes (http://localhost:5173)
```

- Docker Desktop must be running on the Windows host. From WSL:
  `(nohup "/mnt/c/Program Files/Docker/Docker/Docker Desktop.exe" >/dev/null 2>&1 &)`.
- `.env` (untracked) carries the Stytch project id and `TABLETOP_STYTCH_SECRET`; `cp -n .env.example .env` on a new
  machine, then add the secret.
- The two servers bind `127.0.0.1` only: Development maps `POST /v1/dev/sessions`, which mints a session with no login.
  Each builds into its own `obj/Unix-<slot>/` and `bin/-<slot>/`, so host builds never disturb them. The first start
  after a clean restores NuGet for about a minute.
- `docker compose stop management game` frees the ports for `./scripts/management` and `dotnet run --project server/game`.
- Never `docker compose down -v`: the Postgres volume holds the schema the server tests read.
- Godot's import step wipes the client's build output. If every script fails with "class could not be found", run
  `./scripts/client` again.

**Checks.** `dotnet build` has 0 errors (7 analyzer warnings in test files). `./scripts/check-client` prints nothing.
`dotnet test` passes 1503 with the stack up: Geometry 157, Content 17, ArmyBooks 25, Protocol 44, Viewer 9, Client 171,
GameServer 859, Management 94, Sim 127. `cd web && npm test` and `cd viewer && npm test` cover the two web apps.
`dotnet run --project sim -- objectives --seeds 1..4` fails seeds 3 and 4 ("expected at least 1 'round_ended' events,
saw 0"; "expected team 2 to win, got winners [1, 2]"), and did before the in-game UX branch; not yet diagnosed.

## Next

### 1. The in-game UX: the owner's on-screen check

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
rule; the client holds none); terrain and marker drags in play are not gated under `strict`; the e2e sight check's
observer has only melee weapons, so it never proves a non-empty `CanShoot` (the unit tests do).

### 2. Check the terrain fix in the client

Merged (`4fbe694`). The built-in terrain meshes are glTF Y-up and were read as Z-up, which laid them on their side
and sank half of each into the table; `BuiltinContent.MeshOrientation` (pitch 90) stands them up. Check that hills,
woods and buildings stand upright with their bases flush on the table, and water and pads lie flat.

### 3. Base shape at import

Army Forge publishes a round and a square base for every unit (`bases.round`, `bases.square`: a tank `120x92` oval or
`100x60` rectangle, bikers `60x35` or `50x25`), with no field saying which units are vehicles. `ArmyForgeImport` reads
only `Bases.Round`. Owner ruling: the base set is picked when the army is imported. Model height stays as imported.

### 4. Smaller items

- Apply section 6 of `.superpowers/reports/spells.md` (docs corrections) to `docs/schema.md`, the interpreter design
  doc and the plans.
- Cleanup the owner runs; the permission check refuses these from Claude: `git worktree remove .worktrees/spells`,
  `git branch -D spells-pre-rebase-backup`, and the merged agent worktrees under `.claude/worktrees/`
  (`agent-a1bf2de16cf5e473e`, `agent-a5a23d5dd048008b7`, `agent-a926f6b5bf678900a`).
- Spells rulings standing until the owner says otherwise: choosing an innate Hit & Run or Teleport then moving nothing
  spends the use; a granted Flying is spent by the first leg of a split move; a granted Counter-Attack or Unwieldy on a
  unit with no melee weapon stays until next used.
- Management: a login blocks up to 100 s in starter-miniature seeding while MinIO is down, contrary to its own comment.

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
and not per group: a split-fire target one group cannot legally shoot is accepted for that group. Aircraft movement (p.13: straight line, +30", ignores units and terrain) is not built; only its action restriction and
the -12" to attackers' range are. Takedown's picked model still blocks line of sight and gives cover, against p.14. A
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
