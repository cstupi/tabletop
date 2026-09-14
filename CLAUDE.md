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
  the Windows client reaches them and the servers over forwarded `localhost`. Ports: 5432, 9000/9001,
  management 5100, game server 5200.

## Commands

```bash
dotnet build                 # compile; output goes to client/.godot/mono/temp/bin/
./scripts/godot -e           # open the editor on the Windows host
./scripts/godot              # run
dotnet test                  # every xUnit project under tests/; the server tests need docker compose up -d
cp -n .env.example .env && docker compose up -d   # Postgres 17 (5432) and MinIO (9000/9001) in WSL
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
  - `Tabletop.csproj` / root namespace `Tabletop`, `net10.0`
  - `src/` — all client C#; the SDK globs `**/*.cs`, so no `.csproj` edits when adding files
  - `scenes/` — `.tscn` files; `scenes/Main.tscn` is the entry scene, script `src/Main.cs`
- `server/game/` — live game sessions, rules engine, event log. WebSocket. Empty.
- `server/management/` — ASP.NET Core CRUD API: accounts, armies, assets, layouts, games.
  Empty.
- `web/` — Svelte SPA. Owns *preparation*: armies, assets including STL conversion in the
  browser, the account, and login. Does not exist yet.
- `sim/` — `Tabletop.Sim`, a headless simulator that plays whole games with no client, over an
  in-process driver or a real socket, and records a projection per event. Does not exist yet.
- `viewer/` — a static developer web app that draws a `sim/` recording top-down, or attaches to a
  live game as a spectator. Does not exist yet.
- `libs/` — `net10.0`, Godot-free, persistence-free. One child per library:
  - `Tabletop.Geometry` — values, and the distance/LoS/settling API declared here and implemented by the geometry plan.
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
