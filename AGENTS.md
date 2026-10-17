# Tabletop

A virtual tabletop for One Page Rules (Grimdark Future v3.5.1) and later Warhammer 40,000: a Godot 4.7 / C# client and
two .NET 10 servers. The rules run only on the game server. The client renders and measures, and there is no offline
mode.

`docs/handoff.md` says where things stand and what to pick up next. Each module folder has its own `AGENTS.md` with the
specifics.

## Where things are

| Path | What |
| --- | --- |
| `client/` | The Godot game client, the only project with a Godot dependency. `src/Core/` is Godot-free and unit-tested. |
| `server/game/` | The WebSocket game server and the rules engine: event log, interpreter, scheduler, Grimdark Future rules. |
| `server/management/` | The HTTP API (`/v1`): login through Stytch, accounts, armies, assets in MinIO, layouts, games, migrations. |
| `web/` | The SvelteKit web app: accounts, army import, mesh upload (STL to GLB). |
| `sim/` | A headless simulator that plays whole games against the server and writes recordings. Developer-only. |
| `viewer/` | A 2D web viewer for sim recordings and live games. Developer-only. |
| `libs/` | Shared, Godot-free, persistence-free: `Tabletop.Geometry`, `Tabletop.Content`, `Tabletop.Protocol`. |
| `tests/` | One xUnit project per library, per server, for the client's core, the sim, the viewer and the army books. |
| `scripts/` | Launch scripts, the Postgres init schema, `ArmyBooks/` (the army-book generator). |
| `docs/` | Design, specs, plans, schema. See below. |
| `deploy/` | The production path: `docker-compose.prod.yml`, systemd units, `prod.env.example`. |
| `Tabletop.sln`, `Directory.Build.props`, `global.json`, `docker-compose.yml` | Root build and the development stack. |

`client/`, `server/` and `web/` ship to players. `sim/` and `viewer/` ship to developers: neither is in the client's
export or referenced by a server.

## Get running

```bash
cp -n .env.example .env   # then add TABLETOP_STYTCH_SECRET; .env is never committed
docker compose up -d      # Postgres 5432, MinIO 9000/9001, management 5100, game 5200 (dotnet watch), web 5173, viewer 5174 (Vite), sim-runner 5180
./scripts/client          # build, check and launch the Godot client on Windows
dotnet build && dotnet test   # the unit suite, nothing running; --settings tests/e2e.runsettings for the E2E suite (stack up)
dotnet run --project sim -- suite                    # the rules suite in-process; --driver socket against the stack
```

`docker-compose.yml` and the `scripts/` launchers run the servers in dev mode (`ASPNETCORE_ENVIRONMENT=Development` plus
`TABLETOP_DEV_MODE=1`); Development without the opt-in refuses to start. Production is `deploy/README.md`: Release images or
systemd units behind Caddy, migrations through `Tabletop.Management --migrate`, and startup guards that refuse dev settings.

The rules suite (`sim/AGENTS.md` → The rules suite) also runs from the viewer's Tests page (`http://127.0.0.1:5174`), which
drives the `sim-runner` service (`sim serve` on `127.0.0.1:5180`, socket driver against the `game` container).

## Environment

- The repo lives in WSL (Ubuntu 26.04). The Godot editor and client run on the Windows host and open the project over
  `\\wsl.localhost\Ubuntu-26.04\home\cstupi\src\tabletop`.
- Windows reaches WSL at `127.0.0.1`, never `localhost`: Windows resolves `localhost` to `::1` first, which does not
  reach WSL, and .NET waits 21 s before falling back.
- WSL `dotnet` is 10.0.400 from `~/.dotnet`. apt's 8.0 SDK is also at `/usr/bin/dotnet`; PATH order picks the right one.
- `docker` works only while Docker Desktop runs on the Windows host. Never `docker compose down -v`.
- Every compose service publishes on `127.0.0.1` only. The web app is `http://127.0.0.1:5173`, the viewer
  `http://127.0.0.1:5174`; both proxy `/v1` to the management container. `docker compose stop <service>` frees a port
  for a host run (`./scripts/web`, `npm run dev`, `./scripts/management`).
- `Directory.Build.props` gives each OS its own `obj/` (`obj/Unix`, `obj/Windows_NT`), and each compose server container
  its own `obj/Unix-<slot>/` and `bin/-<slot>/`, because restore records absolute paths.

## Rules for every module

- One world unit is one inch. The table frame's origin is the table's corner, Z up, everywhere; nothing converts.
- C# only, no GDScript. Test-driven for everything with logic, xUnit under `tests/`.
- Every module is designed before it is built, and built from its plan. No speculative folders, interfaces or
  placeholders for undesigned work.
- The rules engine lives only in `server/game/`. Nothing else names or implements a rule.

## Docs

| Path | What |
| --- | --- |
| `docs/handoff.md` | Current state and next steps |
| `docs/design/README.md` | System design and vocabulary; `docs/design/<module>/design.md` per module |
| `docs/architecture.md` | Cross-cutting decisions and why |
| `docs/superpowers/specs/` | Design sessions that settle behaviour |
| `docs/data-model.md`, `docs/schema.md` | Entity diagram, Postgres tables |

When documents disagree, specs settle behaviour and design docs settle structure. A
later document beats an earlier one.
