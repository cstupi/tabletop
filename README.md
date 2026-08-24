# Tabletop

Godot 4.7 / C# project. Developed in WSL; the Godot editor runs on the Windows host.

## Requirements

- Godot 4.7.x, **.NET/Mono build**, installed on the Windows host — the standard build
  cannot open a C# project
- .NET SDK 10.0.400 on **both** sides: WSL (for CLI builds) and Windows (Godot shells out
  to `dotnet` to build the assembly)

```powershell
winget install --id GodotEngine.GodotEngine.Mono --exact
winget install --id Microsoft.DotNet.SDK.10 --exact
```

The `Godot.NET.Sdk` version in `Tabletop.csproj` must match your Godot version.

## Build and run

```bash
dotnet build                 # compile the C# assembly
./scripts/godot -e           # open the editor on the Windows host
./scripts/godot              # run
```

`scripts/godot` finds the Windows Godot executable and passes it this project's path
translated to a UNC path. Set `GODOT_EXE` to override the editor location.

## Layout

```
project.godot            project settings
Tabletop.csproj          game assembly, net10.0
Tabletop.sln             solution
global.json              pins the .NET SDK
Directory.Build.props    per-OS obj/ dirs, so WSL and Windows builds don't collide
scenes/                  scenes; Main.tscn is the entry scene
scripts/godot            editor/game launcher
src/                     C# source, root namespace `Tabletop`
```

New `.cs` files under `src/` are picked up automatically — no `.csproj` edit needed.
