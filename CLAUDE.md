# CLAUDE.md

Godot 4.7 / C# project, currently at scaffold stage.

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
dotnet build                 # compile; output goes to .godot/mono/temp/bin/
./scripts/godot -e           # open the editor on the Windows host
./scripts/godot              # run
```

Godot's import step wipes `.godot/mono/temp/bin/`, so build *after* an import, not before,
or the assembly is missing and every script fails with "class could not be found".

## Layout

- `Tabletop.csproj` / `Tabletop.sln` — game assembly, `net10.0`, root namespace `Tabletop`
- `src/` — all C#; the SDK globs `**/*.cs`, so no `.csproj` edits when adding files
- `scenes/` — `.tscn` files; `scenes/Main.tscn` is the entry scene, script `src/Main.cs`

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
