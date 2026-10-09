# Handoff — 2026-10-07

The game is built end to end and playable from the server's side: a Grimdark Future game runs from lobby and setup
through the roll-off, deployment, activations, shooting, melee, morale, casting, transports and objectives to a winner
after round 4. `sim/` plays whole games headless; `viewer/` draws them. **What it lacks is a usable in-game client.** The
owner played an activation in the Godot client and could not tell what was happening. The work now is the in-game UX,
then polish.

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
`dotnet test` passes 1358 with the stack up: Geometry 157, Content 17, ArmyBooks 25, Protocol 40, Viewer 9, Client 82,
GameServer 810, Management 91, Sim 127. `cd web && npm test` and `cd viewer && npm test` cover the two web apps.

## Next

### 1. Design the in-game UX

A `superpowers:brainstorming` session, architectural path, paused mid-way on 2026-10-07. Settle the design with the
owner, write `docs/superpowers/specs/<date>-in-game-ux-design.md`, get it reviewed, then `superpowers:writing-plans`.
Scope: the game screen from deployment to the end of the game. Placeholder art. Server changes where the flow needs them.

**The owner's requirements:**
- Use the screen's real estate. The narrow side panel of generic buttons is most of the UI today.
- Nobody clicks "continue" without knowing what is happening. Remove stops that carry no decision.
- Clarity through an activation: whose turn, which unit, which step, what just happened, what comes next.
- Hotkeys, including a modifier to select and position individual models. Shift already moves one model in a drag;
  nothing on screen says so.
- No accidental moves.
- A way to check that line of sight is computed correctly.
- Keep the move-range circle: the one thing that worked, once found.
- Hover a unit: its name above it, and the distance to it when one of your units is selected.
- Left-click a unit: select it and show its stat sheet in the lower-left corner, not in the side panel.

**Settled:**
- Between steps the game runs to the next real decision. Each step's result stays visible in an on-screen activation
  feed; undo and the dice edits are the correction path.
- `WaitsBetweenSteps` (the per-game flag behind every "Continue: {step}" Ask, `SequenceInterpreter.cs:64`) becomes a
  development-only switch, not offered to players.

- Moves follow the game's enforcement setting. Under `strict` a hand move outside an open move step is refused by the
  server and never starts in the client; today `MoveModels` is an `Override` command and skips `EnforcementPolicy`
  (`CommandDispatcher.cs:23`), so this is a server change. Under `warn` and `silent` a plain drag outside a move step
  moves the unit, and on release an undo button for that move shows clearly on screen.
- A plain click and drag acts on the whole unit; a held modifier acts on individual models (select and position one).
- Layout: the table fills the window, everything floats over it. Header strip at the top: round, whose turn, score,
  the active unit and its action, a step strip (done, current, next), and a "waiting on" line. Stat sheet lower left
  with tabs Stats and This round (the unit's activation and what was done to it this round). The current decision
  bottom centre, the latest dice above it until the next roll. The log minimized to a button lower right, expanding
  with the full history, every roll's dice and undo-to-here. No side panel; right-click menus stay.
- The stat sheet follows the activating unit when an activation begins; a click on another unit holds it there until
  the next activation begins.
- Sight check: hold L with a unit selected and hover a target (shown without L while picking shooting targets): a line
  per model pair, clear, in cover or blocked, the blocker lit, and a summary. The server answers through a new
  read-only socket question using `Sight`; nothing is logged.
- Hover a unit: its name, its distance from your selected unit (closest models, p.5) with a line, and the wounds left
  above each model with more than one wound. Click selects the unit; empty table or Esc clears; Shift+click one model,
  Shift+drag moves one. While an Ask wants a target, eligible units glow and a click picks.
- Stats tab: name and size, Quality, Defense; chips for activated (action), Shaken, Fatigued, spell tokens, grants; the
  weapons table; special rules with their text on hover from the pinned catalog, a rule with no effect tagged "not
  automated"; a models strip with wounds left, a click selects the model.
- Mockups: `.superpowers/brainstorm/5808-1791512643/content/`.

**Still to cover:**
- the screen layout (offer the visual companion, as its own message, when the first layout question comes up)
- the activation feed
- the line-of-sight check
- hover and selection
- the stat sheet: Army Forge stats (Quality, Defense, weapons, special rules) plus live wounds, conditions and tokens
- hotkeys
- picking targets and removing casualties on the table, not in dropdowns and spin boxes
- a round, phase and turn header
- dice that stay visible
- an end-of-game screen
- model height. The import sets it to 1.5 × base length, so an Attack Vehicle draws as a 7" pillar: keep, cap, or a
  fixed height per base type.

**What the client has today.** Every Ask has a control and nearly every menu command has one; the gaps are display
and feel.

| Question | Legal type | Client today |
| --- | --- | --- |
| `confirm` (step gate), `end_round` | Confirmation | a button |
| `deploy`, `deploy_scouts`, `deploy_ambusher` | PieceSelection of shelved unit ids | buttons per shelved unit, then "Place on table" |
| `activate`, `act_or_end_phase`, `shoot_or_not`, `react`, `strike_back`, `cast` | Options | a column of buttons |
| `move`, `disembark` | MoveRegion | the region drawn on the table, drag, "Commit move" |
| `declare_targets` | Assignment | a dropdown of unit names per weapon group |
| `remove_casualties` | Allocation | a spin box per model, sum unchecked |
| `charge_target`, `takedown_target`, `spell_targets` | PieceSelection | click on the table |
| `spend_tokens` | TokenSpend | a spin box per caster, sealed |

Shown nowhere on the table: wounds, Shaken, Fatigued, spell tokens, unit names, who has activated, round, phase.
Dice readouts last 1.5 s and only with animation on. The game's end is one log line.

Read first: `client/AGENTS.md`, `docs/design/client/design.md`, `client/src/Panels/AskPanel.cs`, `client/src/Table/TableInput.cs`,
`client/src/Screens/GameScreen.Play.cs`.

### 2. Check the terrain fix in the client

Merged (`4fbe694`). The built-in terrain meshes are glTF Y-up and were read as Z-up, which laid them on their side
and sank half of each into the table; `BuiltinContent.MeshOrientation` (pitch 90) stands them up. Check that hills,
woods and buildings stand upright with their bases flush on the table, and water and pads lie flat.
`docs/design/client/design.md` still says the built-ins are authored Z-up; correct it to Y-up at pitch 90.

### 3. Smaller items

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

**Most army-book rules do nothing in play.** The official books put 313 rule names on units and upgrades. 231 of them
have no `catalog_entry` (Precise, Fortified, Regeneration Aura, Mend, Versatile Attack, Agile, Armor, Unique, …), and
`LiveProperties.Gather` skips a name the set lacks, so a unit carrying one plays without it and nobody is told. The
spell-library spec recorded this ("the other 261 army-book rule names"); it is its own work item after the in-game UX:
(1) the army-book generator writes a text-only entry (name, kind, aggregation, the book's `description` as
`rule_text`) for every rule the books define, all 361 descriptions being in the Army Forge dump; (2) the client fetches
the pinned set's rule texts by hash and tags rules with no effect "not automated"; (3) the effects are authored, many
by pattern (Aura, Buff, Mark, Boost), under their own design and plan. Army import drops the export's `specialRules`
descriptions; the catalog, not the army, is where rule text lives.

Aircraft movement (p.13: straight line, +30", ignores units and terrain) is not built; only its action restriction and
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
