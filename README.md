# Tabletop

Virtual tabletop for One Page Rules and Warhammer 40,000. A Godot 4.7 / C# client, .NET servers
and a Svelte web app. Developed in WSL; the Godot editor runs on the Windows host.

`CLAUDE.md` describes the layout and conventions. `docs/handoff.md` says where the project stands.

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
cp -n .env.example .env                                         # carries the Stytch test project id
cd server/management && dotnet user-secrets set STYTCH_SECRET <secret> && cd -
cd web && npm install && cd -
```

The Stytch test project's `SESSION` custom claim template must be exactly
`{"account_id": "{{ user.external_id }}"}` (set in the Stytch dashboard). Without it login appears to
work but every request after it answers 401 and the game server refuses the socket.

Each session, one terminal per line, from the repo root in WSL. Docker Desktop must be running on
the Windows host.

```bash
docker compose up -d                  # Postgres :5432, MinIO :9000; never from a worktree
./scripts/management                  # management API, http://localhost:5100
dotnet run --project server/game      # game server, http://localhost:5200/ws
./scripts/web                         # web app, http://localhost:5173
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
