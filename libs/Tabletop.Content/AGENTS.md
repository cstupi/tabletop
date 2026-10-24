# libs/Tabletop.Content

The army snapshot and the table state: the records that describe what is brought to a game and what stands on the table.
`net10.0`, Godot-free, persistence-free. References `Tabletop.Geometry`. Consumed by `Tabletop.Protocol`, `server/game`, `server/management`, `client/` and `sim/`.

## Layout

- `Army.cs`: `Army`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`, and `ArmySnapshot` (the army frozen into a game).
- `Stats.cs`: `StatKind` and `StatValue` (a stat's text, read under its kind; numbers are ASCII digits, at most six).
- `TableState.cs`: `VisualRef` (built-in name or library asset, exactly one), `TerrainVolume`, and the terrain and table records.
- `BuiltinTerrain.cs`: a built-in terrain piece (`Name`, `Label`, `Volume`, `Mesh`) and the one parser of a catalog's `terrain` array (`Parse`, `FromCatalog`), read by the client's `BuiltinCatalog` and management.
- `PlanetTerrain.cs`: `Planet` and Grimdark Future p.18–19's rolls plus the owner's Ruins pack (`Fixed`, `Rolled`, `Roll` from a seeded `Random`).
- `TableSides.cs`: each team's anchor from its zones, the side of a point, and per-team category counts (`TerrainShare`).
- `RandomTables.cs`, `RandomTables.Place.cs`: `RandomTables.Lay`, the random table.
- `ContentJson.cs`: the JSON conventions for Geometry and Content types. `Tabletop.Protocol` builds its options on `ContentJson.Configure`. `Configure(options, integerEnums: false)` (what `WireJson` uses) reads an enum by name only.

## How it works

- Everything is an immutable record, so `ArmySnapshot.Of` shares lists instead of cloning.
- Unit and slot ids in a snapshot are the ids in play.
- A melee weapon has no `Range` stat at all.
- `Unit.Reserve` (`UnitReserve`: a property name and the slot or unit holding it) marks a unit the army holds off the table until that property places it; null for every other unit and omitted from the JSON.
- Stat values stay text as written; `StatKind` (`TargetNumber`, `Distance`, `Count`, `DiceExpression`) says how to read them.
- `RandomTables.Lay(planet, size, zones, seed, terrain)`: rolls the names (`PlanetTerrain.Roll`, one seeded `Random` for everything), groups the catalog's pieces by signature (the p.12 categories each declares: blocks line of sight, cover, difficult), deals each group evenly to the teams (the remainder is neutral), and places largest first, down a gap ladder of 6", 3", 0.5" between footprints, 400 candidates a rung. Two teams: a pair is one piece at a point on a team's side and its partner at the reflection through the table centre, yaw + 180°; a neutral piece sits on the bisector of the anchors. Three or four teams: rounds of one per team, each on its own side; a neutral piece within a quarter of the shorter edge of the centre. A pair or round with no room is withdrawn whole (`Unplaced`). Ids and draws come from the seeded `Random`, so a seed pins the table.
- Consumers: management serves it (`POST /v1/layouts/random`) over its embedded catalog; the predefined tables are its output committed as data.

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
