# Tabletop

Godot 4.7 / C# project.

## Requirements

- Godot 4.7.x, **.NET/Mono build** (the standard build cannot open a C# project)
- .NET SDK 8.0

The `Godot.NET.Sdk` version in `Tabletop.csproj` must match your Godot version.

## Build and run

```bash
dotnet build                 # compile the C# assembly
godot --editor .             # open in the editor
godot .                      # run
```

## Layout

```
project.godot        project settings
Tabletop.csproj      game assembly
Tabletop.sln         solution
scenes/              scenes; Main.tscn is the entry scene
src/                 C# source, root namespace `Tabletop`
```

New `.cs` files under `src/` are picked up automatically — no `.csproj` edit needed.
