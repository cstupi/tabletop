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

## Build and run

```bash
dotnet build                 # compile the C# assembly
./scripts/godot -e           # open the editor on the Windows host
./scripts/godot              # run
```

`scripts/godot` finds the Windows Godot executable and passes it `client/` as a UNC path. Set
`GODOT_EXE` to override the editor location.

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
scripts/godot            editor and game launcher
Tabletop.sln             solution
global.json              pins the .NET SDK
Directory.Build.props    per-OS obj/ dirs, so WSL and Windows builds don't collide
```
