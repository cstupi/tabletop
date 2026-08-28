# Tabletop

Virtual tabletop for One Page Rules and Warhammer 40,000.

A Godot 4.7 / C# client and .NET servers. Developed in WSL; the Godot editor runs on
the Windows host.

## Requirements

- Godot 4.7.x, **.NET/Mono build**, installed on the Windows host — the standard build
  cannot open a C# project
- .NET SDK 10.0.400 on **both** sides: WSL (for CLI builds) and Windows (Godot shells out
  to `dotnet` to build the assembly)

```powershell
winget install --id GodotEngine.GodotEngine.Mono --exact
winget install --id Microsoft.DotNet.SDK.10 --exact
```

The `Godot.NET.Sdk` version in `client/Tabletop.csproj` must match your Godot version.

## Build and run

```bash
dotnet build                 # compile the C# assembly
./scripts/godot -e           # open the editor on the Windows host
./scripts/godot              # run
```

`scripts/godot` finds the Windows Godot executable and passes it `client/` as a UNC
path. Set `GODOT_EXE` to override the editor location.

## Layout

```
client/                  deliverable: the Godot game client
  project.godot          project settings
  Tabletop.csproj        client assembly, net10.0
  scenes/                scenes; Main.tscn is the entry scene
  src/                   C# source, root namespace `Tabletop`
server/                  deliverables: the .NET servers, no Godot dependency
  game/                  live game sessions, rules, event log
  management/            accounts, game browser, collections, assets
libs/                    shared libraries, consumed by client and servers
docs/                    design documents
scripts/godot            editor/game launcher
Tabletop.sln             solution
global.json              pins the .NET SDK
Directory.Build.props    per-OS obj/ dirs, so WSL and Windows builds don't collide
```

New `.cs` files under `client/src/` are picked up automatically — no `.csproj` edit
needed. Because the client project lives in `client/`, its `**/*.cs` glob cannot reach
sibling projects under `server/` or `libs/`.

`server/` and `libs/` are empty; each module is designed before it is built.
