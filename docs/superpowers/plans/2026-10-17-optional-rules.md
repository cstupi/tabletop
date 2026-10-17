# Optional rules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** A game carries an optional-rules map beside its enforcement setting, set in the lobby, frozen from `playing`, and
read by the Grimdark Future rules through `GfOptions`: Unit sight blocking (`per_model` | `perimeter`, p.5), Line of sight
method (`core` | `hybrid_true`, p.56) and Melee separation (`on` | `off`, p.10).

**Architecture:** `game.optional_rules jsonb` travels as `GameInfo.OptionalRules` (an opaque string map); the ruleset
publishes a `RulesOptionCatalog` in `Welcome` and validates a `SetConfiguration`'s map; `Sight` takes an `ISightPolicy`
from `IGameRules`; the perimeter hull is a Geometry `SightPrism`.

**Tech Stack:** .NET 10, xUnit, Npgsql/Dapper, DbUp, Godot 4.7 C# client, generated viewer types.

**Spec:** `docs/superpowers/specs/2026-10-15-optional-rules-design.md`

## Global Constraints

- Nothing outside `server/game/` names an option key or value; the client builds its controls from the catalog.
- An absent key reads as its default; `'{}'` is the core game and every existing row plays as before.
- `SetConfiguration` is refused `WrongStatus` from `playing` on (unchanged) and `Unknown` for a map with problems.
- `001_schema.sql` is never edited; the column is migration `010_optional_rules.sql`.
- After a wire change: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then `cd viewer && npm test`.

## Review Focus

- A perimeter must never hide the target's own models from a line to the target (owner ruling: target unit blocks per model).
- `Sight` built with `alsoIgnoring` (p.11 cover) must drop that unit's perimeter as well as its bodies.
- Hybrid: the target's own unit may be seen through by exception 1 only; exception 2 needs the target above the unit in the way.
- Melee separation `off`: `move_separation` still applies against units the mover was not in contact with.

---

### Task 1: Wire

**Files:** `libs/Tabletop.Protocol/Game.cs`, `Edits.cs`, `Wire.cs`, `ProtocolVersion.cs`; test
`tests/Tabletop.Protocol.Tests/` (round trip); every `new SetConfiguration(` caller.

- [ ] Test: a `GameInfo` with `OptionalRules = {line_of_sight: hybrid_true}` and a `Welcome` with a catalog round-trip
  through `WireJson`; a `GameInfo` JSON without the field reads an empty map.
- [ ] `GameInfo.OptionalRules { get; init; }` defaulting to an empty map; `SetConfiguration(..., OptionalRules)`;
  `RulesOptionCatalog`, `RulesOption`, `RulesOptionValue`, `RulesPreset`; `Welcome.Options { get; init; }`;
  `ProtocolVersion.Current` 2.
- [ ] Regenerate the viewer's types; commit.

### Task 2: Rows

**Files:** `server/management/Persistence/Migrations/010_optional_rules.sql`, `docs/schema.md`,
`server/management/Games/GameRepository.cs`, `tests/Tabletop.Management.Tests/Infrastructure/InMemoryGameRepository.cs`;
`server/game/Persistence/PostgresGameRows.cs`, `.Writes.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/InMemoryGameRows.cs`,
`GameRowsContract.cs`; `sim/Drivers/SocketRowSeeder.cs`.

- [ ] `alter table management.game add column optional_rules jsonb not null default '{}'::jsonb`.
- [ ] Contract test: `SetConfigurationAsync` with a map reads it back; a seeded game reads `{}`.
- [ ] Postgres read `optional_rules::text` into the map, write `$7::jsonb`; the in-memory fakes store it; management maps
  it onto `GameInfo`.
- [ ] Commit.

### Task 3: GfOptions and the lobby

**Files:** `server/game/Rules/GrimdarkFuture/GfOptions.cs` (new), `GfRules.cs` (or a new `GfRules.Options.cs`),
`server/game/Rules/IGameRules.cs`, `server/game/Session/GameSession.Lobby.cs`, `GameSession.Connect.cs`,
`tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs` (`WithSequences` forwards); tests `GfOptionsTests`, `LobbyTests`.

- [ ] `GfOptionsTests`: `{}` reads `Core`; each key and value parses; an unknown key and an unknown value are problems;
  the catalog lists the three options with the book's labels and pages and the one preset *Core rules*.
- [ ] `LobbyTests`: an unknown option is refused `Unknown` and the row is unchanged; a valid map is in the next
  `GameInfoMessage`; `Welcome.Options` carries the catalog.
- [ ] Implement; commit.

### Task 4: Perimeter geometry

**Files:** `libs/Tabletop.Geometry/SightBlockers.cs`, `Bodies.cs`; tests `tests/Tabletop.Geometry.Tests/SightBlockersTests.cs`
(or a new `PerimeterTests.cs`).

- [ ] Test: `Bodies.Perimeter` of two bases 0.5" apart contains the gap's midpoint; a `SightBlockers` with that prism
  blocks a line through the gap and names the prism's id; an ignore predicate that passes the id clears it; prisms
  report after volumes in `Measure.Blockers`.
- [ ] `SightPrism(Guid Id, IReadOnlyList<Point2> Outline, double Bottom, double Top)`; the hull of every base outline's
  circumscribed points (monotone chain); `SightBlockers(volumes, prisms)`; `Check`/`Measure` overloads taking
  `Func<Guid, bool>`.
- [ ] Commit.

### Task 5: Sight policy

**Files:** `server/game/Interpreter/Sight.cs`, `server/game/Rules/IGameRules.cs`,
`server/game/Rules/GrimdarkFuture/GfSight.cs` (new); test `tests/Tabletop.GameServer.Tests/GfSightOptionsTests.cs`.

- [ ] Tests (spec table, perimeter and hybrid rows): gap line clear under `per_model`, blocked under `perimeter` with
  `BlockedBy` the unit id; the target's own rear model still seen past its front model only per model; the observer's
  own unit never blocks; p.11 cover behind a gap only under `perimeter`; `SizeClass` table; Tough(12) sees past a
  Tough(1) unit, Tough(1) does not; Tough(6) unit blocks a Tough(6) observer's line to Tough(1) but not to Tough(15);
  an observer on a 2" block sees past a unit on the ground; that unit gives no cover; `core` blocks each.
- [ ] `ISightPolicy` (`Perimeters`, `SeesThrough`, `PerModel`); `IGameRules.SightPolicy(state)`; `Sight.From` reads it;
  `GfSight` implements it from `GfOptions.Of(state)`.
- [ ] Commit.

### Task 6: Melee separation off

**Files:** `server/game/Rules/GrimdarkFuture/GfMelee.cs` (`ConsolidateStep`), `GfMoves.Contact.cs`; tests in
`GfMeleeTests`/`GfContactTests`.

- [ ] Tests: both alive under `off`, the consolidation step opens no move Ask; under `on` it opens the 1" move. A unit
  within 1" of an enemy advancing to land within 1" of that enemy has no `move_separation` breach under `off`, and has
  one against a third unit it was not in contact with; a unit in contact is offered a Charge at that unit.
- [ ] Implement; commit.

### Task 7: Client

**Files:** `client/src/Core/RulesOptions.cs` (new), `client/src/Core/Session.cs` (`Options` from `Welcome`),
`client/src/Panels/LobbyPanel.cs`, `client/src/Table/SightLines.cs`; test `tests/Tabletop.Client.Tests/RulesOptionsTests.cs`.

- [ ] Tests: the preset matching a map (absent keys read as defaults) or *Custom*; picking a preset gives its map; the map
  built from chosen values drops defaults.
- [ ] Lobby: preset and one `OptionButton` per option, labelled `Label (Page)`; *Apply configuration* sends the map.
  `SightLines.BoundsOf` merges a unit id's model bounds.
- [ ] Commit.

### Task 8: Sim and suite games

**Files:** `sim/Scenarios/Scenario.cs`, `ScenarioBuilder.cs`, `ScenarioCompiler.cs`, `sim/Run/HandPlacement.cs`,
`sim/Suite/SuiteCatalog.Core.Options.cs` (new), `SuiteCatalog.Core.cs`; tests in `tests/Tabletop.Sim.Tests`.

- [ ] `Scenario.OptionalRules` (builder `.Options(...)`) into `GameInfo` and the socket seeder's insert;
  `ScenarioArmy.Sizes` (unit ordinal to model count) and `ScenarioArmy.Anchors` (unit ordinal to the first model's
  position; `HandPlacement` uses it in place of a drawn anchor).
- [ ] Test: a compiled scenario carries the map; a unit with an anchor is placed in a row from it.
- [ ] Games: `core-perimeter-sight` (shooter, a four-model screen whose middle gap lines up with Blue 1, Blue 2 in the
  open, everyone holds: Blue 2 offered, Blue 1 never), `core-hybrid-sight` (Tough(12) shooter, a Tough(1) screen
  before Blue 1 and a Tough(12) screen before Blue 2: Blue 1 offered, Blue 2 never),
  `core-melee-no-separation` (melee armies: no consolidation move while both live, a unit in contact charges or moves).
- [ ] `dotnet run --project sim -- suite`; commit.

### Task 9: Docs and the full run

- [ ] `server/game/AGENTS.md`, `sim/AGENTS.md`, `libs/Tabletop.Geometry/AGENTS.md` name the new pieces.
- [ ] `dotnet build && dotnet test`; `dotnet run --project sim -- suite`; commit.
