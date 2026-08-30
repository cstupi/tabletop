# CLAUDE.md

Virtual tabletop for One Page Rules and Warhammer 40,000: a Godot 4.7 / C# client and
.NET servers. Currently at scaffold stage — only the client exists.

`docs/architecture.md` records the design decisions and why they were made.
`docs/data-model.md` is the entity diagram, `docs/schema.md` the Postgres tables, and
`docs/superpowers/specs/` the per-module design sessions. `docs/handoff.md` says where things
stand and what to pick up.

Where `architecture.md` and a design doc disagree, **the design doc is newer**.

Each module is planned separately before it is built. Do not scaffold ahead of a plan — no
speculative folders, interfaces, or placeholder systems for work that has not been designed
yet.

## Environment

The repo lives in WSL (Ubuntu 20.04); the Godot editor runs on the **Windows host** and
reaches the project over `\\wsl.localhost\Ubuntu-20.04\home\cstupi\src\tabletop`.

- WSL `dotnet` is 10.0.400 from `~/.dotnet` (`dotnet-install.sh`), exported in `~/.bashrc`.
  Ubuntu 20.04 has no apt package for .NET 10, and apt's `dotnet-sdk-8.0` is still installed
  at `/usr/bin/dotnet` — PATH order is what picks the right one.
- Windows has Godot 4.7.2 Mono and .NET SDK 10.0.400, both via winget.
- `Directory.Build.props` gives each OS its own `obj/` (`obj/Unix`, `obj/Windows_NT`);
  a shared one breaks restore, since it records absolute OS-specific paths.

## Commands

```bash
dotnet build                 # compile; output goes to client/.godot/mono/temp/bin/
./scripts/godot -e           # open the editor on the Windows host
./scripts/godot              # run
```

Godot's import step wipes `client/.godot/mono/temp/bin/`, so build *after* an import, not
before, or the assembly is missing and every script fails with "class could not be found".

## Layout

Three kinds of top-level directory:

- **Deliverables** — `client/`, `server/`, `web/`, `tools/`. These ship. `tools/` is easy to
  overlook because it is not a service, but the mesh prep tool is run by users.
- **Shared code** — `libs/`. Consumed by the deliverables, never shipped alone.
- **Repo-level support** — `docs/`, `scripts/`. Neither shipped nor consumed. Dev tooling
  stays out of `client/`, because everything under the Godot project root is project
  content that the editor scans and lists.

- `client/` — the Godot game client, and the only project with a Godot dependency.
  - `Tabletop.csproj` / root namespace `Tabletop`, `net10.0`
  - `src/` — all client C#; the SDK globs `**/*.cs`, so no `.csproj` edits when adding files
  - `scenes/` — `.tscn` files; `scenes/Main.tscn` is the entry scene, script `src/Main.cs`
- `server/game/` — live game sessions, rules engine, event log. WebSocket. Empty.
- `server/management/` — ASP.NET Core CRUD API: accounts, armies, assets, layouts, games.
  Empty.
- `web/` — Svelte SPA. Owns *preparation*: armies, assets, the account, and login. Does not
  exist yet.
- `tools/mesh-prep/` — offline STL to GLB conversion and decimation, run by users before
  uploading. Does not exist yet.
- `libs/` — `Tabletop.Geometry`, `Tabletop.Protocol`, `Tabletop.Content`. Empty.

The rules engine is **not** in `libs/` — it lives only in `server/game/`. The client is a
renderer plus a geometry engine, and there is no offline mode: running the game means
running a server.
- `Tabletop.sln` — at the repo root, spanning every project; `Directory.Build.props` and
  `global.json` sit beside it and are found by MSBuild walking up from each project.

Because the client project lives in `client/`, its `**/*.cs` glob cannot reach sibling
projects — that is why it moved out of the repo root.

`server/`, `web/`, `tools/` and `libs/` are empty or absent on purpose. Each module is designed
before it is built.

## Conventions

- One world unit = one inch.
- C# only, no GDScript.
- `net10.0` works even though Godot ships a `net8.0` host, because Godot's
  `GodotPlugins.runtimeconfig.json` sets `rollForward: LatestMajor`. If a Godot upgrade
  breaks C# loading, dropping the TFM back to `net8.0` is the first thing to try.
- `.tscn`, `.tres`, `.godot`, and `.import` files are owned by the editor — hand-edit only
  when necessary, and never reformat them.
- No test project yet. Add one (`net10.0` class library + xUnit) when there is logic worth
  testing headlessly.
