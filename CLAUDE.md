# CLAUDE.md

Virtual tabletop for One Page Rules and Warhammer 40,000: a Godot 4.7 / C# client and
.NET servers. What exists is what `Tabletop.sln` and the Layout section below say; a Layout
bullet that reads *Empty* or *does not exist yet* is a module not yet built.

`docs/handoff.md` says where things stand and what to pick up. `docs/design/README.md` is the
system design and defines the vocabulary; `docs/design/<module>/design.md` is each module's
structure and `docs/superpowers/plans/` its implementation plan. `docs/superpowers/specs/` are
the design sessions that settle behaviour, `docs/architecture.md` the cross-cutting decisions and
why, `docs/data-model.md` the entity diagram and `docs/schema.md` the Postgres tables.

When documents disagree: specs settle behaviour, plans settle what gets written, design docs
settle structure, and a later document beats an earlier one.

Every module has a plan; build from it. Do not scaffold ahead of a plan — no speculative folders,
interfaces, or placeholder systems for work that has not been designed yet.

## Environment

The repo lives in WSL (Ubuntu 26.04); the Godot editor runs on the **Windows host** and
reaches the project over `\\wsl.localhost\Ubuntu-26.04\home\cstupi\src\tabletop`.

- WSL `dotnet` is 10.0.400 from `~/.dotnet` (`dotnet-install.sh`), exported in `~/.bashrc`.
  apt's `dotnet-sdk-8.0` is also installed at `/usr/bin/dotnet` — PATH order is what picks the right one.
- Windows has Godot 4.7.2 Mono and .NET SDK 10.0.400, both via winget.
- `Directory.Build.props` gives each OS its own `obj/` (`obj/Unix`, `obj/Windows_NT`);
  a shared one breaks restore, since it records absolute OS-specific paths.
- Docker reaches WSL through Docker Desktop on the Windows host; if `docker` is not found, Docker Desktop is not
  running. Postgres and MinIO run in Docker inside WSL (`docker-compose.yml`, schema in `scripts/postgres/init/`);
  the Windows client reaches them and the servers over mirrored loopback at `127.0.0.1`, never `localhost`: Windows
  resolves `localhost` to `::1` first, which does not reach WSL, and .NET waits 21 s before falling back (browsers race
  both, so the web app is unaffected). Ports: 5432, 9000/9001, management 5100, game server 5200.
- The management API needs the Stytch test project (id in `.env.example`) to have
  `{TABLETOP_WEB_URL}/reset-password` (`http://localhost:5173/reset-password` locally) registered as a
  `RESET_PASSWORD` redirect URL in the Stytch dashboard, not in this repo, or password resets fail at Stytch.
  The `account_id` claim needs no dashboard setting: management adds it to each new session. The secret is set
  once per machine with `dotnet user-secrets set STYTCH_SECRET <secret>` in `server/management`.

`.env` needs `TABLETOP_STYTCH_PROJECT_ID` (the test project id from `docs/handoff.md`, in `.env.example`); the game server derives the JWKS URL from it and `TABLETOP_STYTCH_BASE_URL`. Tests in `tests/Tabletop.GameServer.Tests` marked `[Collection("Postgres")]` need the Docker Postgres up. Node 24 for `web/` comes from mise (`~/.local/share/mise`) in WSL; `npm`, exact versions (`web/.npmrc`).

## Commands

```bash
dotnet build                 # compile; output goes to client/.godot/mono/temp/bin/
./scripts/godot -e           # open the editor on the Windows host
./scripts/godot              # run
dotnet test                  # every xUnit project under tests/; the server tests need docker compose up -d
cp -n .env.example .env && docker compose up -d   # Postgres 17 (5432) and MinIO (9000/9001) in WSL
dotnet run --project server/game   # the game server on http://0.0.0.0:5200, /ws; reads .env (TABLETOP_DB_CONNECTION, TABLETOP_STYTCH_PROJECT_ID)
./scripts/management         # the management API on http://localhost:5100 (needs docker compose up and the Stytch secret)
cd server/management && dotnet user-secrets set STYTCH_SECRET <secret>   # once per machine; never in a file
./scripts/web                # the web app on http://localhost:5173 (Vite; /v1 proxied to management on :5100)
cd web && npm test           # Vitest: the API client, session, units, the mesh pipeline against tests/fixtures
./scripts/client             # build, run scripts/check-client, launch the Godot client on Windows
./scripts/check-client       # no physics body in a scene, no rules type or Godot in client/src/Core
dotnet test tests/Tabletop.Client.Tests   # the client's Godot-free core
dotnet run --project sim -- --list                 # the scenarios
dotnet run --project sim -- skirmish --seeds 1..20 # twenty games, recordings in ./recordings
```

Godot's import step wipes `client/.godot/mono/temp/bin/`, so build *after* an import, not
before, or the assembly is missing and every script fails with "class could not be found".

## Layout

Three kinds of top-level directory:

- **Deliverables** — `client/`, `server/`, `web/`, `sim/`, `viewer/`. These ship. The first three
  ship to players; `sim/` and `viewer/` ship to developers, so neither is in the client's export,
  neither is referenced by a server, and a release that omits both is complete
  (`docs/superpowers/specs/2026-09-06-simulation-design.md`).
- **Shared code** — `libs/`. Consumed by the deliverables, never shipped alone.
- **Repo-level support** — `docs/`, `scripts/`, `tests/`. Neither shipped nor consumed. Dev tooling
  stays out of `client/`, because everything under the Godot project root is project
  content that the editor scans and lists.

- `client/` — the Godot game client, and the only project with a Godot dependency.
  - `src/Core/` — Godot-free: `Session`, `ManagementClient`, `ClientGameSocket`, `AssetCache`, `BuiltinContent`,
    `TerrainGenerators`, the view models. Compiled into `tests/Tabletop.Client.Tests` from source, so a `using Godot`
    here breaks the tests. May reference only the three libs and the BCL.
  - `src/Screens/`, `src/Table/`, `src/Panels/` — the Godot layer; thin over `Core` and `Tabletop.Geometry`.
  - `scenes/` — `Main.tscn` is the root that swaps `Login`, `Home`, `Game`, `UpdateRequired`; `TableView.tscn` is the table.
    Scenes are written minimal by hand and owned by the editor afterwards.
  - `content/builtin/` — the five table textures and the ten grouped terrain meshes; a missing file draws the piece's volumes.
- `server/game/` — the WebSocket game server (`Tabletop.GameServer`, `net10.0`): the connect handshake and Stytch JWT
  verification, a `GameSession` per game hydrated from management's rows plus `game.game_event`, the append-only log and
  replay, `CommandDispatcher` over the `IRulesEngine` seam, the lobby and setup row writes. Tested in
  `tests/Tabletop.GameServer.Tests`. The engine behind the rules seam is `RulesEngine`, built by `RulesEngineFactory` over
  `SchedulerFactory`; its parts are the children below.
  - `Properties/` (the pinned set, its authored `Content/*.properties.json` form, the store and the startup seeder),
    `Dice/` (the seeded counter-based roll primitive), `Effects/` (the two passes over live properties), `Interpreter/`
    (frames from the log, steps, windows, the battlefield reader), `Engine/` (`RulesEngine`, the one `IRulesEngine`, and
    the scheduler seam `IScheduler`/`ISchedulerFactory`), `Scheduler/SchedulerTypes.cs` and `ActivationOption.cs` (the
    seam records the scheduler plan reads).
  - `Rules/GrimdarkFuture/` — OPR's sequences, rolls, hooks and names: the only place a rule is named.
  - `Scheduler/` — the generic turn machinery behind `Engine.IScheduler` (two shapes, the turn as an Ask, the roll-off read back from the ruleset's sequence); its Grimdark Future hooks are the `GfRules.Scheduling.cs`/`GfRules.Deployment.cs` partials in `Rules/GrimdarkFuture/`, beside the interpreter's sequences.
- `server/management/` — `Tabletop.Management`, ASP.NET Core minimal APIs: `Identity/` (Stytch login, JWT verification, the account), `Content/` (assets over MinIO and the generated starter miniatures, folders, armies from Army Forge, layouts), `Games/`, `Http/` (version check, keyset paging), `Persistence/` (Npgsql + Dapper, DbUp migrations in `Persistence/Migrations/`). Tested in `tests/Tabletop.Management.Tests` against the Docker stack.
- `web/` — the Svelte SPA (SvelteKit 2 + Svelte 5, `adapter-static`, `ssr = false`). `src/lib/api/` is the typed client over management's `/v1`; `src/lib/mesh/` is the STL → GLB pipeline (`meshoptimizer`, `@gltf-transform/core`), headless under Vitest and in a Web Worker in the page; `src/lib/components/` holds the three.js preview, the upload dialog and the volume editor; `src/routes/` are the pages. Node 24 via mise; `npm`, exact versions (`.npmrc`).
- `sim/` — `Tabletop.Sim`, a library plus a console entry point that plays a whole game with no
  Godot client: a scenario, a seeded answer policy over the server's legal sets, two drivers
  (in process and over the socket), the invariant checks, and one recording file per run.
  It reads game state freely but holds no second implementation of the rules: it answers from the
  server's legal sets without narrowing them, and asserts invariants.
- `viewer/` — a static developer web app that draws a `sim/` recording top-down, or attaches to a
  live game as a spectator. Does not exist yet.
- `libs/` — `net10.0`, Godot-free, persistence-free. One child per library:
  - `Tabletop.Geometry` — values; distance, line of sight, cover, settling, containment, overlap, coherency, the movement region, the slab, and the built-in zone styles — all headless, all tested.
  - `Tabletop.Content` — the army snapshot and the table state.
  - `Tabletop.Protocol` — every socket message, the wire JSON, `EventApplier` and `Projection`.
- `tests/` — one xUnit project per library, per server, and for the client's Godot-free core; the set is whatever
  `dotnet sln list` shows under `tests/`. `dotnet test` from the root; the server tests need `docker compose up -d`.

The rules engine is **not** in `libs/` — it lives only in `server/game/`. The client is a
renderer plus a geometry engine, and there is no offline mode: running the game means
running a server.
- `Tabletop.sln` — at the repo root, spanning every project; `Directory.Build.props` and
  `global.json` sit beside it and are found by MSBuild walking up from each project.

Because the client project lives in `client/`, its `**/*.cs` glob cannot reach sibling
projects — that is why it moved out of the repo root.

Each module is designed before it is built.

## Conventions

- One world unit = one inch.
- C# only, no GDScript.
- `net10.0` works even though Godot ships a `net8.0` host, because Godot's
  `GodotPlugins.runtimeconfig.json` sets `rollForward: LatestMajor`. If a Godot upgrade
  breaks C# loading, dropping the TFM back to `net8.0` is the first thing to try.
- `.tscn`, `.tres`, `.godot`, and `.import` files are owned by the editor — hand-edit only
  when necessary, and never reformat them.
- Tests: xUnit under `tests/`, one project per library, per server and for the client's Godot-free
  `src/Core/`; test-driven for everything with logic. The client's Godot layer is tested by running it.
- Schema changes are new `NNN_*.sql` files under `server/management/Persistence/Migrations/`; `001_schema.sql` there is a copy of `scripts/postgres/init/001_schema.sql` and neither is edited after the fact. The game server writes a game's rows only while it is in `lobby` or `setup`; management writes them at creation and at end/leave/delete (management plan → Rows by writer).
- The web app speaks inches to the API; millimetres exist only in its UI (`src/lib/units/`). It never calls `/v1/games*`, `/v1/properties` or `/v1/layouts`.
- The table frame's origin is the table's corner (`0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up) on the wire and in `Tabletop.Geometry` alike; nothing converts. Godot: x = X, y = Z, z = −Y, `rotation.y` = yaw (`WorldFrame`). No Godot physics; picking is ray-versus-AABB.
- The engine never appends or broadcasts and holds no state between calls: a step reads its position, values and history from the log and returns one outcome. Dice are a function of the game's stored `rng_seed` and the count of dice drawn in the effective log. Authored `Properties` are `server/game/Content/*.properties.json`, seeded by hash at startup; editing one makes a new set with a new hash — old games keep theirs.
