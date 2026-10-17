# libs/Tabletop.Content

The army snapshot and the table state: the records that describe what is brought to a game and what stands on the table.
`net10.0`, Godot-free, persistence-free. References `Tabletop.Geometry`. Consumed by `Tabletop.Protocol`, `server/game`, `server/management`, `client/` and `sim/`.

## Layout

- `Army.cs`: `Army`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`, and `ArmySnapshot` (the army frozen into a game).
- `Stats.cs`: `StatKind` and `StatValue` (a stat's text, read under its kind; numbers are ASCII digits, at most six).
- `TableState.cs`: `VisualRef` (built-in name or library asset, exactly one), `TerrainVolume`, and the terrain and table records.
- `ContentJson.cs`: the JSON conventions for Geometry and Content types. `Tabletop.Protocol` builds its options on `ContentJson.Configure`. Enums are read by name only (no integer values).

## How it works

- Everything is an immutable record, so `ArmySnapshot.Of` shares lists instead of cloning.
- Unit and slot ids in a snapshot are the ids in play.
- A melee weapon has no `Range` stat at all.
- Stat values stay text as written; `StatKind` (`TargetNumber`, `Distance`, `Count`, `DiceExpression`) says how to read them.

## Commands

```bash
dotnet build libs/Tabletop.Content
dotnet test tests/Tabletop.Content.Tests
```

## Conventions and pitfalls

- `VisualRef` rejects both-set and neither-set; `default(VisualRef)` is the only invalid-but-constructible value.
- JSON shape changes here change the wire; run the Protocol and Viewer tests.
- No rules are named here. Rule names are properties, held by the game server's pinned set.

## Docs

- `docs/design/contracts/design.md`
- `docs/superpowers/specs/2026-08-29-content-values-design.md`
- `docs/superpowers/specs/2026-08-28-army-design.md`
- `docs/superpowers/specs/2026-08-27-table-domain-model-design.md`
