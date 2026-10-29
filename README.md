# Tabletop

Virtual tabletop for One Page Rules and Warhammer 40,000. A Godot 4.7 / C# client, .NET servers
and a Svelte web app. Developed in WSL; the Godot editor runs on the Windows host.

`CLAUDE.md` describes the layout and conventions. `docs/handoff.md` says where the project stands.

## Release

Tag `vX.Y.Z` on main and push it, then on `docker1` in `/opt/docker/tabletop`:

```bash
git checkout main && git pull && scripts/release --deploy home --version vX.Y.Z
```

That builds and uploads the client, deploys the servers, web app and viewer, and publishes the built-in content; it
runs no tests unless `--tests`. Options and other hosts: `deploy/README.md` → Home server (docker1) and Releases.

## Requirements

- Godot 4.7.x, **.NET/Mono build**, on the Windows host. The standard build cannot open a C#
  project.
- .NET SDK 10.0.400 on **both** sides: WSL for CLI builds, Windows because Godot shells out to
  `dotnet` to build the assembly.

```powershell
winget install --id GodotEngine.GodotEngine.Mono --exact
winget install --id Microsoft.DotNet.SDK.10 --exact
```

The `Godot.NET.Sdk` version in `client/Tabletop.csproj` must match the Godot version.

## Build

```bash
dotnet build                 # compile every project
dotnet test                  # needs the Docker stack up (below)
./scripts/godot -e           # open the editor on the Windows host
```

`scripts/godot` finds the Windows Godot executable and passes it `client/` as a UNC path. Set
`GODOT_EXE` to override the editor location.

## Run a game locally

Once per machine:

```bash
cp -n .env.example .env      # carries the Stytch test project id; set TABLETOP_STYTCH_SECRET in it
```

The Stytch test project needs `http://localhost:5173/reset-password` registered as a
`RESET_PASSWORD` redirect URL (Stytch dashboard), or password resets fail at Stytch.

Each session, from the repo root in WSL. Docker Desktop must be running on
the Windows host.

```bash
docker compose up -d                  # Postgres, MinIO, management :5100, game :5200/ws, web app :5173, viewer :5174; never from a worktree
./scripts/client                      # builds, then launches the Godot client on Windows
```

Run `./scripts/client` (or `./scripts/godot`) a second time for the second player. If the client
fails with "class could not be found" after Godot re-imports, build again: the import wipes the
assembly.

### Smoke test, two players

1. In the web app, **Create account** for two accounts, then on **Armies** paste an Army Forge share
   link or list id and **Import** one army per account.
2. Log in on each client (**Sign up** works here too). Login should give way to Home.
3. Client A: create a game. Client B: refresh, see it, join. Pick teams and armies; only A sees
   **Start setup**.
4. Setup: set the table size and texture, run the "Hospitable planet" generator (13–21 pieces), drag a
   piece with its ruler, then **End setup**.
5. Play: the roll-off, deployment, then alternating activations. The addressed player gets the Ask
   panel's buttons, the other sees "Waiting for …". Activate a unit with Hold, Advance or Charge and
   check the dice readout, a shooting or melee result and the end of the round.
6. Along the way: the other player's cursor and drags show live, M measures, P pings, a log row's
   **Undo to here** rewinds, and going Home and back rejoins the same team.

Terrain and models draw as plain volume proxies until the built-in art files exist. The full
checklists are `docs/handoff.md` → *Next* and *Checkpoint 2: what a player should see*.

## Layout

```
client/                  Godot game client, the only Godot dependency
server/game/             live game sessions, rules engine, event log (WebSocket)
server/management/       accounts, armies, assets, layouts, games (HTTP)
web/                     Svelte SPA: armies, assets, the account
sim/                     headless full-game simulator, for developers
viewer/                  draws a simulator recording, for developers
libs/                    Tabletop.Geometry, Tabletop.Protocol, Tabletop.Content
docs/                    design documents and plans
scripts/                 godot, client, management, web launchers; check-client
Tabletop.sln             solution
global.json              pins the .NET SDK
Directory.Build.props    per-OS obj/ dirs, so WSL and Windows builds don't collide
```
