# Browser play A: random and predefined tables Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** The random table moves to `libs/Tabletop.Content` and lays the rolled pieces on the table, clear of each other and
balanced across the teams; management serves it (`POST /v1/layouts/random`) and four predefined 6'×4' tables
(`GET /v1/layouts/builtin`); Godot's Setup uses both through `ReplaceDraft`.

**Architecture:** `Tabletop.Geometry` gains two public footprint queries. `Tabletop.Content` gains `BuiltinTerrain` (the
catalog's terrain entries, parsed once for the client and management), `Planet` / `PlanetTerrain` (p.18–19's rolls, moved
from the client) and `RandomTables` (roll, allocate per team, place). Management embeds the repo catalog and the four table
files and maps both endpoints in `Content/Layouts/`. The client's `TerrainGenerators` goes; `SetupToolbar` calls management.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, Godot 4.7 C#, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-24-browser-play-design.md` (Decided → Terrain; Coordinator's calls → The random
table, Predefined tables). Rulebook: Grimdark Future p.6 (objective markers), p.12 (terrain balance), p.17–19 (placement,
generators).

## Findings that shape the plan

- **Generation is not a rule** and `server/game` never reads the catalog (builtin content spec → Source of truth), so the
  placer goes in `libs/Tabletop.Content`: Godot-free, references `Tabletop.Geometry`, owns `TableState` and
  `TerrainPlacement`, consumed by management and the client.
- **The terrain volumes live only in the client today.** `client/src/Core/BuiltinCatalog.cs` parses
  `client/content/builtin/catalog.json`; management passes `terrain` through unread (`PublishedBuiltin.Terrain` is a
  `JsonElement`). The terrain-entry checks move to `Tabletop.Content` so both read one parser. Management embeds the repo
  catalog (a linked `EmbeddedResource`, as the client does): the published `builtin/catalog.json` is that same file plus
  `version` and `files` for the release, so one server image and its catalog agree, and dev stacks with nothing published
  work. `server/management/Dockerfile` copies `client/content/builtin/` (only the JSON) for it.
- **Geometry's outlines are internal** (`WorldVolumes.WorldOutline`, `Table.Outline`, `Outline.Separation`). Content needs a
  public footprint-to-footprint distance and a "fully on the table" test.
- **Zones:** `ZoneStyles` has six two-team styles, all point-symmetric about the table centre, and `free-for-all` (four
  corners; a three-team game uses its first three). The request carries the draft's `DeploymentZone` rows, so swapped and
  three-team zones arrive as they stand. No zones: Frontline's two.
- **Seeded ids.** `BuiltinCatalog.Place` uses `Guid.CreateVersion7`; a seed-pinned result needs ids from the seeded
  `Random` (16 bytes, RFC 4122 version 4 bits set). `new Random(int)` is the legacy seeded algorithm, stable across .NET.
- **Management's HTTP JSON** is `ContentJson.Configure` plus snake-case-lower string enums (`Program.cs`): `Planet.Hospitable`
  is `"hospitable"` on the wire, `VolumeKind.Solid` `"solid"`. An integer enum is a 400.
- **p.12 balance**: at least 50 % block line of sight, 33 % cover, 33 % difficult; 15–20 pieces; gaps of at least 6" for
  large units, none over 12". p.17's placement styles keep pieces 6" apart. p.6: objective markers outside the deployment
  zones, over 9" from each other, never in impassable terrain.
- **Three categories** a piece counts toward, read from its volumes' declarations: line-of-sight blocker
  (`BlocksLineOfSight`), cover (`Cover`), difficult (`Difficult`). Its *signature* is the set it has (eight possible).

## The placer

`RandomTables.Lay(planet, size, zones, seed, terrain)`:

1. `rng = new Random(seed)`. Roll the names as `TerrainGenerators.Generate` does today (the nine fixed, 4D3 more on 2D6, one
   alternative picked), same draw order. A name the catalog lacks goes to `Unlisted`.
2. Teams: the distinct `TeamOrdinal`s of `zones` (Frontline's two when empty). A team's anchor is the mean of its zone
   polygons' vertex centroids; a point's side is the team with the nearest anchor.
3. Allocate: group the pieces by signature, largest footprint area first. With `n` teams, each team gets `c / n` of a group of
   `c`, the remainder `c % n` is neutral. The team dealt the first piece rotates from group to group.
4. Place, largest first, each candidate a uniform point and a 15° yaw step from `rng`, at most 400 candidates per rung of the
   gap ladder 6", 3", 0.5" (the clearance between this footprint and every placed one, and the table edge containing it):
   - **Two teams:** pieces of a group pair up (largest with the next). One goes at a point on team 1's side, its partner at the
     point's reflection through the table centre, yaw + 180°, and the reflection must fall on team 2's side. A neutral piece's
     centre is a point on the bisector of the two anchors.
   - **Three or four teams:** a group's pieces are placed in rounds of one per team, each on its team's side; a round is all
     or nothing. A neutral piece's centre lies within `min(W, L) / 4` of the table centre.
   - A pair or round with no candidate on the last rung is withdrawn whole into `Unplaced`, so the shares stay equal.
5. Every piece rests at Z 0 (none stand on another), scale 1, volumes copied from the catalog with seeded ids.

Each team's side therefore holds the same count of each signature, so of each category; neutral pieces sit on the dividing
line (two teams) or at the centre (three or four).

## Endpoint contracts (the browser plan consumes these)

```text
POST /v1/layouts/random            signed in
  body  RandomTableRequest { size: { width, length }, planet: "generic"|"hospitable"|"wasteland"|"death"|"ruins",
                             zones?: DeploymentZone[] (the draft's rows: { id, shape, teamOrdinal }), seed?: int }
  200   RandomTable { seed: int, terrain: TerrainPlacement[], unplaced: string[], unlisted: string[] }
  422   size not Table.IsValid; an unknown zone shape id; over 16 zones
  400   unknown planet or an integer planet (enum binding)
  Same body with the same seed → byte-identical terrain. No seed → the server picks one and answers it.

GET /v1/layouts/builtin            signed in, read-only
  200   TableState[]: four, ordered Hospitable, Wasteland, Death, Ruins; each 72×48, a name ("Hospitable planet" …),
        a built-in texture, terrain, four objective markers, zones frontline/1 (team 1) and frontline/2 (team 2).
GET /v1/layouts                    unchanged (the account's LayoutSummary list)
```

`TerrainPlacement`, `TableState`, `DeploymentZone` and `Marker` are the existing `Tabletop.Content` records in their existing
JSON. A client sends `ReplaceDraft(draft with { terrain = response.terrain })` for Random, `ReplaceDraft(table)` for a
predefined one.

## Global Constraints

- `client/src/Core/` stays Godot-free; `libs/` stays persistence-free and Godot-free. Nothing in `server/game` changes.
- No speculative members: each new public member has a caller in this plan.
- Each new public member gets a one-line `<summary>` in the repo's dense style; no change-history prose.
- Files stay under 500 lines; `RandomTables` splits into `RandomTables.cs` (roll, allocate) and `RandomTables.Place.cs`.
- Commands: `dotnet test tests/Tabletop.Geometry.Tests`, `dotnet test tests/Tabletop.Content.Tests`,
  `dotnet test tests/Tabletop.Management.Tests`, `dotnet test tests/Tabletop.Client.Tests`, `./scripts/check-client`,
  `dotnet build && dotnet test` before the last commit.

## Review Focus

- The balance claim is exact per signature only for team-dealt pieces; check that the neutral rule (bisector or centre disc)
  and the all-or-nothing withdrawal hold it.
- Determinism: every draw comes from the one seeded `Random`, in a fixed order; no `Guid.CreateVersion7`, no `Random.Shared`,
  no dictionary-order iteration in `RandomTables`.
- The embedded catalog in management and the Dockerfile copy; the compose container builds from the mounted repo.
- `ReplaceDraft` keeps size, texture, markers and zones for Random; replaces all of it for a predefined table.

---
### Task 1: Geometry — footprint clearance and containment

**Files:** modify `libs/Tabletop.Geometry/Distance.cs`, `libs/Tabletop.Geometry/Table.cs`; test
`tests/Tabletop.Geometry.Tests/FootprintClearanceTests.cs`.

**Interfaces:**

```csharp
// Distance.cs
/// <summary>The gap between two placed footprints in plan, 0 when they touch or overlap; heights ignored.</summary>
public static double Between(WorldVolume a, WorldVolume b);
/// <summary>The gap from a placed footprint to a point in plan, 0 inside it.</summary>
public static double Between(WorldVolume a, Point2 point);
// Table.cs
/// <summary>The footprint lies wholly on the slab, edges inclusive.</summary>
public static bool ContainsVolume(TableSize size, WorldVolume volume);
```

- [ ] Write the tests first:
  - `Rects_six_inches_apart_are_six_apart`: two 6×6 rect Solids at (10,10) and (22,10), yaw 0 → `Between` = 6 (±1e-6).
  - `A_turned_rect_measures_its_corner`: the second at yaw 45 → `6 - (3√2 - 3)` .
  - `Overlapping_and_touching_are_zero`: centres 4" and 6" apart → 0 and 0.
  - `Circle_to_rect_is_edge_to_edge`: circle radius 3 at (20,10), rect 6×6 at (10,10) → 4.
  - `A_point_inside_is_zero_and_outside_is_the_gap`: rect 6×6 at (10,10); (10,10) → 0, (16,10) → 3.
  - `Scale_grows_the_footprint`: the second at scale 2 → `Between` = 3.
  - `A_footprint_hanging_off_the_edge_is_not_contained`: rect 6×6 at (2,24) on 72×48 → false; at (3,24) → true; circle r 3 at
    (69,45) → true, (70,45) → false.
- [ ] `dotnet test tests/Tabletop.Geometry.Tests --filter FootprintClearanceTests` fails to compile.
- [ ] Implement over the internal `WorldOutline()`, `Outline.Separation(...).Distance`, `PolygonOutline.DistanceTo` /
  circle distance, and `Table.Outline(size).ContainsOutline(...)`.
- [ ] `dotnet test tests/Tabletop.Geometry.Tests` passes.
- [ ] Commit: `Geometry: footprint clearance and table containment for terrain placement`.

### Task 2: Content — `BuiltinTerrain` parsed in one place

**Files:** create `libs/Tabletop.Content/BuiltinTerrain.cs`; modify `client/src/Core/BuiltinCatalog.cs` (drop the record,
`TerrainDto`, `FootprintDto`, `DeclarationNames` and the terrain loop; call the parser); test
`tests/Tabletop.Content.Tests/BuiltinTerrainTests.cs`.

**Interfaces:**

```csharp
namespace Tabletop.Content;
/// <summary>A built-in terrain piece: its volume (the data) and the mesh it draws with (builtin content spec).</summary>
public sealed record BuiltinTerrain(string Name, string Label, Volume Volume, string Mesh)
{
    /// <summary>The catalog's <c>terrain</c> array, checked; <see cref="FormatException"/> names the first fault. Ordinal key order is the array's.</summary>
    public static IReadOnlyDictionary<string, BuiltinTerrain> Parse(JsonElement terrain, IReadOnlyCollection<string> meshes);
    /// <summary>The <c>terrain</c> of a whole catalog file (the repo's or the published one).</summary>
    public static IReadOnlyDictionary<string, BuiltinTerrain> FromCatalog(string catalogJson);
}
```

- [ ] Write `BuiltinTerrainTests` first: `A_faulty_entry_is_refused_naming_the_fault`, a theory over the eight terrain rows of
  the client's `BuiltinCatalogTests.Faults` (`names mesh 'tower'`, `listed twice`, `not solid or area`, `not a terrain
  declaration`, `footprint`, `has top`, `no label`, `notes over 500`), each `Assert.Contains(fault, Assert.Throws<FormatException>
  (...).Message)` against `BuiltinTerrain.Parse`; and `The_repo_catalog_parses`: `FromCatalog(File.ReadAllText(<repo>/client/
  content/builtin/catalog.json))` has `barricade` as a Solid rect 3×1, top 1, `Cover | Difficult`, the repo root found by
  walking up to `Tabletop.sln`. The client's `Faults` theory stays as it is and still passes through `BuiltinCatalog.Parse`.
- [ ] Red: `dotnet test tests/Tabletop.Content.Tests --filter BuiltinTerrainTests`.
- [ ] Implement; `BuiltinCatalog` keeps `Terrain` as `IReadOnlyDictionary<string, BuiltinTerrain>` (now the Content type) and
  every client reference compiles unchanged through `using Tabletop.Content`.
- [ ] `dotnet test tests/Tabletop.Content.Tests` and `dotnet test tests/Tabletop.Client.Tests` pass; `./scripts/check-client`.
- [ ] Commit: `Content: built-in terrain entries parsed in Tabletop.Content for the client and management`.

### Task 3: Content — `Planet` and p.18–19's rolls move from the client

**Files:** create `libs/Tabletop.Content/PlanetTerrain.cs`; test `tests/Tabletop.Content.Tests/PlanetTerrainTests.cs` and
`tests/Tabletop.Content.Tests/RepoCatalog.cs` (the repo catalog through `BuiltinTerrain.FromCatalog`, read once). The client's
`TerrainGenerators` stays until Task 7.

**Interfaces:**

```csharp
namespace Tabletop.Content;
public enum Planet { Generic, Hospitable, Wasteland, Death, Ruins }
/// <summary>Grimdark Future p.18–19's planets plus the owner's Ruins pack: nine fixed pieces, then 4D3 more rolled on 2D6.</summary>
public static class PlanetTerrain
{
    public static IReadOnlyList<string> Fixed(Planet planet);
    public static IReadOnlyList<string> Rolled(Planet planet, int twoD6);
    /// <summary>The fixed nine then the rolled ones, drawn from <paramref name="rng"/> as 4D3, then per piece 2D6 and the alternative.</summary>
    public static IReadOnlyList<string> Roll(Planet planet, Random rng);
}
```

- [ ] Write the tests first, moving the client's: `Generic_fixed_pieces_are_the_rulebooks_nine`,
  `The_ruins_pack_is_ruins_without_whole_buildings` (against `RepoCatalog`) from `TerrainGeneratorTests`, and
  `Every_generator_entry_names_a_built_in` from `BuiltinContentTests`. New: `A_roll_is_the_fixed_nine_then_4d3_more` (every
  planet, seeds 0..19: count in 13..21, the first nine equal `Fixed`, each later name in some `Rolled(planet, 2..12)`);
  `The_same_seed_rolls_the_same_names`.
- [ ] Red: `dotnet test tests/Tabletop.Content.Tests --filter PlanetTerrainTests`.
- [ ] Implement with the client's tables verbatim and its draw order (4D3 summed first, then per piece `Next(1,7)+Next(1,7)`
  and `Next(alternatives.Count)`).
- [ ] `dotnet test tests/Tabletop.Content.Tests` passes.
- [ ] Commit: `Content: planets and their terrain rolls in Tabletop.Content`.

### Task 4: Content — `RandomTables` lays the table

**Files:** create `libs/Tabletop.Content/TableSides.cs`, `libs/Tabletop.Content/RandomTables.cs`,
`libs/Tabletop.Content/RandomTables.Place.cs`; test `tests/Tabletop.Content.Tests/RandomTablesTests.cs`,
`tests/Tabletop.Content.Tests/TableSidesTests.cs`.

**Interfaces:**

```csharp
namespace Tabletop.Content;
/// <summary>The categories p.12 balances; a piece counts toward each its volumes declare.</summary>
public readonly record struct TerrainShare(double SightBlockers, double Cover, double Difficult);
/// <summary>Each team's side of the table: the points nearer its zones' anchor than any other team's.</summary>
public static class TableSides
{
    /// <summary>Per team ordinal, the mean of its zone polygons' centroids; Frontline's two when <paramref name="zones"/> is empty.</summary>
    public static IReadOnlyDictionary<int, Point2> Anchors(TableSize size, IReadOnlyList<DeploymentZone> zones);
    /// <summary>The team whose anchor is nearest; null on a tie within 1e-6 (the dividing line).</summary>
    public static int? SideOf(IReadOnlyDictionary<int, Point2> anchors, Point2 point);
    /// <summary>Per team, the share of each category among the pieces whose centre lies on its side; a tied centre counts 1/k to each of the k tied teams.</summary>
    public static IReadOnlyDictionary<int, TerrainShare> Shares(TableSize size, IReadOnlyList<DeploymentZone> zones, IReadOnlyList<TerrainPlacement> terrain);
}
/// <summary>A placed piece and the team it was dealt to; null for a neutral piece.</summary>
public sealed record LaidPiece(TerrainPlacement Placement, int? Team);
/// <summary>The table laid: the pieces, the rolled names withdrawn for want of room, and the names the catalog lacks.</summary>
public sealed record LaidTable(int Seed, IReadOnlyList<LaidPiece> Pieces, IReadOnlyList<string> Unplaced, IReadOnlyList<string> Unlisted);
/// <summary>A random table (plan → The placer): rolled per planet, dealt evenly to the teams by signature, placed clear of each other, mirrored for two teams.</summary>
public static class RandomTables
{
    public static readonly double[] Gaps = [6, 3, 0.5];
    public const int Candidates = 400;
    public static LaidTable Lay(Planet planet, TableSize size, IReadOnlyList<DeploymentZone> zones, int seed, IReadOnlyDictionary<string, BuiltinTerrain> terrain);
}
```

- [ ] `TableSidesTests` first: `Frontline_anchors_are_the_long_edge_bands` (72×48: team 1 (36, 42), team 2 (36, 6));
  `Free_for_all_has_four_corner_anchors`; `The_centre_line_is_a_tie` (`SideOf` (10, 24) under Frontline → null, (10, 30) → 1);
  `Shares_split_a_tied_piece` (one forest centred on (36, 24) → 0.5 of each category to each team).
- [ ] `RandomTablesTests` first. `Zones(style)` builds the rows from `ZoneStyles.ShapesOf`, team n the n-th shape. Over every
  planet and seeds 0..49 on 72×48 unless stated:
  - `Every_piece_lies_on_the_table_clear_of_the_others`: `Table.ContainsVolume` for every world volume; `Distance.Between` ≥
    0.5 for every pair of volumes on different pieces; every `Position.Z` is 0 and `Scale` 1.
  - `Two_teams_mirror_about_the_centre` (each two-team style): every team-1 piece has exactly one team-2 piece of the same
    signature at `(W − x, L − y)` (±1e-6) with yaw `+180 mod 360`.
  - `Two_teams_share_each_category_equally` (each two-team style): `TableSides.Shares` gives team 1 and team 2 equal shares.
  - `Each_dealt_piece_stands_on_its_teams_side`: `SideOf(anchors, centre) == Team` for every non-null `Team`.
  - `Neutral_pieces_sit_on_the_dividing_line` (two teams): `SideOf` is null for every `Team == null` centre.
  - `Three_and_four_teams_get_equal_counts_per_signature` (`free-for-all`, its first three shapes and all four): per category,
    the count of dealt pieces is the same for every team; each neutral centre within `min(W, L) / 4` of (36, 24).
  - `Swapped_zones_swap_the_sides`: Frontline rows with ordinals 1 and 2 exchanged → every team-1 centre has y < 24.
  - `No_zones_is_frontline`: `Lay(..., [], ...)` serializes (`ContentJson.Serialize`) equal to `Lay(..., Zones("frontline"), ...)`.
  - `A_crowded_table_withdraws_whole_pairs`: Wasteland on 24×24, seeds 0..19 → some seed has `Unplaced` non-empty, and in
    every seed the shares stay equal and `Pieces.Count + Unplaced.Count + Unlisted.Count == PlanetTerrain.Roll(...).Count`.
  - `A_piece_the_catalog_does_not_list_is_unlisted`: a hills-only catalog → every placed piece is `hill`, `Unlisted` holds
    the other rolled names.
  - `The_seed_pins_the_table`: two `Lay` calls with seed 7 serialize equal; seed 8 differs; and for Hospitable, Frontline,
    seed 1, the piece count and the first three pieces' (name, x, y, yaw), rounded to 0.01, equal the values recorded on the
    first green run (written into the test then), so a change in draw order fails it.
  - `Ids_are_distinct_and_seeded`: placement and volume ids distinct within a table, version nibble 4, equal across two runs.
- [ ] Red: `dotnet test tests/Tabletop.Content.Tests --filter "RandomTablesTests|TableSidesTests"`.
- [ ] Implement as in The placer. Points on a side: rejection-sample the table rectangle until `SideOf` gives the team (at
  most `Candidates` draws counted per rung). The two-team bisector: the line through the anchors' midpoint perpendicular to
  them, a uniform point on its chord inside the table. Area for "largest first" from the volume's footprint (`π a²` or `a·b`).
  Iterate groups in a fixed order (signature as a bit field, then area, then name), never by dictionary order.
- [ ] `dotnet test tests/Tabletop.Content.Tests` passes.
- [ ] Commit: `Content: RandomTables places the rolled terrain on the table, balanced across the teams`.

### Task 5: Management — `POST /v1/layouts/random`

**Files:** create `server/management/Content/Layouts/BuiltinLayouts.cs`, `server/management/Content/Layouts/RandomTableEndpoint.cs`;
modify `server/management/Tabletop.Management.csproj`, `server/management/Program.cs`,
`server/management/Content/Layouts/LayoutEndpoints.cs`, `server/management/Dockerfile`, `.dockerignore`; test
`tests/Tabletop.Management.Tests/RandomTableTests.cs`.

**Interfaces:**

```csharp
namespace Tabletop.Management.Content.Layouts;
/// <summary>The built-in content management serves from its own build: the repo catalog's terrain (embedded), for the random table.</summary>
public sealed class BuiltinLayouts
{
    public IReadOnlyDictionary<string, BuiltinTerrain> Terrain { get; }
    /// <summary>Reads the embedded <c>builtin-catalog.json</c> once; registered as a singleton.</summary>
    public static BuiltinLayouts Embedded();
}
public sealed record RandomTableRequest(TableSize Size, Planet Planet, IReadOnlyList<DeploymentZone>? Zones, int? Seed);
public sealed record RandomTable(int Seed, IReadOnlyList<TerrainPlacement> Terrain, IReadOnlyList<string> Unplaced, IReadOnlyList<string> Unlisted);
public static class RandomTableEndpoint
{
    /// <summary>The request's problem, or null: size (Table.IsValid), at most LayoutRules' 16 zones, every shape id ZoneStyles resolves.</summary>
    public static string? Problem(RandomTableRequest r);
    /// <summary><c>POST /v1/layouts/random</c> in the layouts group: <see cref="RandomTables.Lay"/> over the embedded terrain.</summary>
    public static void MapRandomTable(this RouteGroupBuilder layouts);
}
```

- [ ] csproj: `<EmbeddedResource Include="../../client/content/builtin/catalog.json" LogicalName="builtin-catalog.json"
  Link="Content/Layouts/builtin-catalog.json" />`. `.dockerignore`: `!client/content/builtin/catalog.json` after `client`.
  Dockerfile: `COPY client/content/builtin/catalog.json client/content/builtin/catalog.json` before the publish.
- [ ] Write the tests first (`ManagementFactory`, `AuthenticatedClient`):
  - `Random_lays_a_table_for_a_seed`: POST `{ size 72×48, planet "hospitable", zones frontline rows, seed 1 }` → 200;
    `seed` 1; 13–21 minus `unplaced` placements, every one a built-in with volumes and Z 0.
  - `The_same_seed_answers_the_same_body`: two POSTs → identical response strings.
  - `No_seed_picks_one_and_answers_it`: POST without `seed`, then again with the answered seed → the same `terrain`.
  - `It_matches_the_library`: the answer equals `RandomTables.Lay(Planet.Hospitable, …, 1, BuiltinLayouts.Embedded().Terrain)`
    placements.
  - `Bad_requests_are_refused`: size 0×48 → 422; 145×48 → 422; zone shape `frontline/9` → 422; 17 zones → 422;
    planet `"lava"` → 400; planet `1` → 400.
  - `Signed_out_is_401`.
- [ ] Red: `dotnet test tests/Tabletop.Management.Tests --filter RandomTableTests`.
- [ ] Implement; `Program.cs` registers `BuiltinLayouts.Embedded()` as a singleton; `MapLayouts` calls
  `g.MapRandomTable()`. No seed: `Random.Shared.Next()`.
- [ ] `dotnet test tests/Tabletop.Management.Tests` passes; `docker build -f server/management/Dockerfile -t tabletop-management .`
  succeeds.
- [ ] Commit: `Management: POST /v1/layouts/random lays a random table from the built-in catalog`.

### Task 6: Predefined tables — four files and `GET /v1/layouts/builtin`

**Authoring.** The four tables are generated by `RandomTables` and committed as data. `BuiltinTablesWriter` (an env-gated
test, `TABLETOP_WRITE_GENERATED=1`, the repo's pattern for checked-in outputs) builds each planet's table on 72×48 with
Frontline zones and takes the first seed from 1 upward whose table passes `BuiltinTableChecks`, then writes
`client/content/builtin/tables/<planet>.json` (`ContentJson`, indented). Ids come from a `Random(seed)` separate from the
placer's, so a rerun writes the same bytes. Each table:

- name `"<Planet> planet"`, texture Hospitable `grass`, Wasteland `wasteland`, Death `alien`, Ruins `urban`;
- terrain: `RandomTables.Lay(planet, 72×48, frontline, seed, catalog)` placements;
- markers: four objectives as Godot's Add objective makes them (diameter 3, `objective-ring`, no label) at (18, 18), (54, 30),
  (54, 18), (18, 30): outside both Frontline zones, two pairs mirrored about the centre, 12" or more apart (p.6: over 9");
- zones: `frontline/1` team 1, `frontline/2` team 2.

`BuiltinTableChecks.Problem(TableState)` (null when it passes), in the test project:

- p.12 counts over the terrain: at least 15 pieces, at least half block line of sight, a third cover, a third difficult;
- nothing `Unplaced` (every rolled piece stands);
- `TableSides.Shares` equal for the two teams;
- every marker's centre at least 2.5" (its radius plus 1") from every Impassable or Solid footprint (`Distance.Between`);
- `LayoutRules.Problem(state, nameRequired: true)` is null.

The writer fails, naming the planet, if seeds 1..100000 hold none.

**Files:** create `tests/Tabletop.Management.Tests/BuiltinTablesTests.cs` (writer, checks, tests),
`client/content/builtin/tables/{hospitable,wasteland,death,ruins}.json` (written); modify `BuiltinLayouts.cs`,
`LayoutEndpoints.cs`, `Tabletop.Management.csproj`, `Dockerfile`, `.dockerignore`.

**Interfaces:**

```csharp
// BuiltinLayouts
/// <summary>The predefined tables (embedded <c>builtin-table-*.json</c>), in <see cref="Planets"/> order.</summary>
public IReadOnlyList<TableState> Tables { get; }
public static readonly Planet[] Planets = [Planet.Hospitable, Planet.Wasteland, Planet.Death, Planet.Ruins];
// LayoutEndpoints.MapLayouts
g.MapGet("/builtin", (BuiltinLayouts b) => Results.Ok(b.Tables));   // read-only; GET "" unchanged
```

- [ ] csproj: `<EmbeddedResource Include="../../client/content/builtin/tables/*.json" LogicalName="builtin-table-%(Filename).json" />`;
  `.dockerignore` `!client/content/builtin/tables`; Dockerfile `COPY client/content/builtin/tables/ client/content/builtin/tables/`.
- [ ] Write the tests first:
  - `Write_the_builtin_tables` (skips unless `TABLETOP_WRITE_GENERATED=1`).
  - `Each_committed_table_passes_the_checks`: every file through `ContentJson.Deserialize<TableState>` → `Problem` null, size
    72×48, four objective markers at the four points, zones `frontline/1`→1 and `frontline/2`→2.
  - `The_writer_reproduces_the_committed_files`: the writer's output for each planet equals the file's text.
  - `Get_builtin_lists_the_four_tables`: GET `/v1/layouts/builtin` → 200, four `TableState`s named `Hospitable planet`,
    `Wasteland planet`, `Death planet`, `Ruins planet`, in that order, equal to the files.
  - `The_account_list_is_unchanged`: a new account's GET `/v1/layouts` → `[]` (no built-in appears in it); POST a layout →
    one summary.
  - `Get_builtin_signed_out_is_401`.
- [ ] Red: `dotnet test tests/Tabletop.Management.Tests --filter BuiltinTablesTests`.
- [ ] Implement `BuiltinLayouts.Tables` and the route; then `TABLETOP_WRITE_GENERATED=1 dotnet test
  tests/Tabletop.Management.Tests --filter Write_the_builtin_tables` writes the four files.
- [ ] `dotnet test tests/Tabletop.Management.Tests` passes; the Docker build succeeds.
- [ ] Commit (files included): `Management: four predefined tables at GET /v1/layouts/builtin`.

### Task 7: Godot — Random calls management; Layouts lists the predefined tables

**Files:** modify `client/src/Core/ManagementClient.cs`, `client/src/Core/ManagementDtos.cs`,
`client/src/Panels/SetupToolbar.cs`; create `client/src/Core/LayoutChoices.cs`; delete `client/src/Core/TerrainGenerators.cs`
(+ `.uid`) and `tests/Tabletop.Client.Tests/TerrainGeneratorTests.cs`, and `Every_generator_entry_names_a_built_in` from
`BuiltinContentTests` (moved in Task 3); test `tests/Tabletop.Client.Tests/ManagementClientTests.cs`,
`tests/Tabletop.Client.Tests/LayoutChoicesTests.cs`.

**Interfaces:**

```csharp
// ManagementDtos.cs (mirrors of management's records; Planet is Tabletop.Content's)
public sealed record RandomTableRequest(TableSize Size, Planet Planet, IReadOnlyList<DeploymentZone>? Zones, int? Seed);
public sealed record RandomTable(int Seed, IReadOnlyList<TerrainPlacement> Terrain, IReadOnlyList<string> Unplaced, IReadOnlyList<string> Unlisted);
// ManagementClient.cs
public Task<RandomTable> RandomTable(RandomTableRequest request) => Post<RandomTable>("/v1/layouts/random", request);
public Task<IReadOnlyList<TableState>> BuiltinLayouts() => Get<IReadOnlyList<TableState>>("/v1/layouts/builtin");
// LayoutChoices.cs
/// <summary>The Load layout list: the predefined tables first, then the account's own; a pick is a whole TableState or a saved id.</summary>
public sealed record LayoutChoice(string Label, TableState? Builtin, Guid? SavedId);
public static class LayoutChoices
{
    public static IReadOnlyList<LayoutChoice> Of(IReadOnlyList<TableState> builtin, IReadOnlyList<LayoutSummary> saved);
    /// <summary>The draft Random sends: the current draft with the new terrain; size, texture, markers and zones kept.</summary>
    public static TableState WithTerrain(TableState draft, RandomTable table);
}
```

- [ ] Write the tests first:
  - `ManagementClientTests.RandomTable_posts_the_request_and_reads_the_table`: the fake handler sees POST
    `/v1/layouts/random` with `"planet":"wasteland"`, the draft's zones and no `seed`; answers a body with one placement →
    `RandomTable.Terrain` has it.
  - `ManagementClientTests.BuiltinLayouts_reads_the_list`: GET `/v1/layouts/builtin` → two `TableState`s by name.
  - `LayoutChoicesTests.Builtins_come_first_then_the_accounts`: labels `Hospitable planet (built-in, 72×48, 17 terrain)`,
    then `Mine (72×48, 3 terrain)`; the built-in carries its `TableState`, the saved one its id.
  - `LayoutChoicesTests.Random_keeps_everything_but_terrain`: `WithTerrain` keeps `Id`, `Name`, `Size`, `Texture`, `Markers`,
    `Zones` by reference and replaces `Terrain`.
- [ ] Red: `dotnet test tests/Tabletop.Client.Tests --filter "ManagementClientTests|LayoutChoicesTests"`.
- [ ] Implement. `SetupToolbar`: the five planet buttons become one planet `OptionButton` (`Enum.GetValues<Planet>()`, labels
  `"<Planet> planet"`) and a `Random table` button, gated on a draft being present:
  `var t = await s.Management.RandomTable(new(draft.Size, planet, draft.Zones, null));` then
  `await s.SendEditAsync(new ReplaceDraft(LayoutChoices.WithTerrain(draft, t)))`, and a toast naming `t.Unplaced` when it is
  non-empty. `LoadLayout` fetches `BuiltinLayouts()` and `ListLayouts()` together, fills the `OptionButton` from
  `LayoutChoices.Of`, and on confirm sends `ReplaceDraft(choice.Builtin ?? await s.Management.GetLayout(choice.SavedId!.Value))`.
- [ ] `dotnet test tests/Tabletop.Client.Tests` passes; `dotnet build client/Tabletop.csproj`; `./scripts/check-client`.
- [ ] Against the stack (`docker compose up -d`; the management container rebuilds under `dotnet watch`): with a
  `POST /v1/dev/sessions` token, `curl -s -X POST http://127.0.0.1:5100/v1/layouts/random -H "Authorization: Bearer $T"
  -H 'Content-Type: application/json' -d '{"size":{"width":72,"length":48},"planet":"ruins","seed":3}'` twice → the same
  body; `curl -s http://127.0.0.1:5100/v1/layouts/builtin -H "Authorization: Bearer $T"` → four tables.
- [ ] Commit: `Client: Random table and predefined layouts from management, loaded by ReplaceDraft`.

### Task 8: Docs

**Files:** modify `libs/Tabletop.Content/AGENTS.md` (Layout: `BuiltinTerrain.cs`, `PlanetTerrain.cs`, `TableSides.cs`,
`RandomTables*.cs`; How it works: the placer in four lines; consumers), `libs/Tabletop.Geometry/AGENTS.md` (`Distance.Between`,
`Table.ContainsVolume`), `server/management/AGENTS.md` (Layout: `BuiltinLayouts`, `RandomTableEndpoint`; How it works →
Layouts: the two routes, the embedded catalog and tables, the Dockerfile copy), `client/AGENTS.md` (Setup's Random and Load
layout), `docs/superpowers/specs/2026-10-18-builtin-content-from-server-design.md` (Source of truth: `tables/`; Client: the
line on `TerrainGenerators` becomes management's embedded catalog for the random table), `docs/design/management-api/design.md`
(the two routes), `docs/handoff.md` (state).

- [ ] `grep -rn "TerrainGenerators\|beside the long edge, off the slab" docs client libs server sim viewer --include=*.md
  --include=*.cs` returns nothing.
- [ ] `dotnet build && dotnet test` passes.
- [ ] Commit: `Docs: random and predefined tables`.
