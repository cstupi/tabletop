# CLAUDE.md

Godot 4.7 / C# project, currently at scaffold stage.

Each module is planned separately before it is built. Do not scaffold ahead of a plan — no
speculative folders, interfaces, or placeholder systems for work that has not been designed
yet.

## Commands

```bash
dotnet build                 # compile; output goes to .godot/mono/temp/bin/
godot --editor .             # open the editor
godot .                      # run
```

Godot may not be installed in a given dev environment; `dotnet build` still verifies the C#
side, since `Godot.NET.Sdk` and `GodotSharp` come from NuGet.

## Layout

- `Tabletop.csproj` / `Tabletop.sln` — game assembly, root namespace `Tabletop`
- `src/` — all C#; the SDK globs `**/*.cs`, so no `.csproj` edits when adding files
- `scenes/` — `.tscn` files; `scenes/Main.tscn` is the entry scene, script `src/Main.cs`

## Conventions

- One world unit = one inch.
- C# only, no GDScript.
- `.tscn`, `.tres`, `.godot`, and `.import` files are owned by the editor — hand-edit only
  when necessary, and never reformat them.
- The `Godot.NET.Sdk` version in `Tabletop.csproj` tracks the Godot editor version.
- No test project yet. Add one (`net8.0` class library + xUnit) when there is logic worth
  testing headlessly.
