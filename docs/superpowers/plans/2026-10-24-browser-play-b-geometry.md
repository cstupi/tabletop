# Browser play B: Geometry in the browser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** `libs/Tabletop.Geometry` runs in the viewer as WebAssembly, behind a typed TypeScript module
`viewer/src/lib/geometry/`, so the browser setup and play plans drag, land, rest, check contacts, measure and test zones with
the same code Godot runs.

**Architecture:** `libs/Tabletop.Geometry.Wasm` (`Microsoft.NET.Sdk.WebAssembly`, `browser-wasm`) references
`Tabletop.Geometry` only. `Facade.cs` holds plain `string → string` methods (JSON in, JSON out); `Exports.cs` wraps each in a
`[JSExport]`. Two compositions that live in client core today and are pure geometry move into `Tabletop.Geometry` so both
clients call one implementation: `RangeRings.Union` becomes `RangeOutline.Of`, the per-landing geometry of `ContactCheck.Of`
becomes `Contacts.Of`. `scripts/geometry-wasm` publishes the project and copies `wwwroot/_framework/` to
`viewer/public/geometry/_framework/` (git-ignored); Vite copies it into both builds, `load.ts` imports `dotnet.js` from
`${BASE_URL}geometry/_framework/` on first use only. The facade is tested in-process by xUnit (a `net10.0` test project can
reference the WebAssembly project; checked with a spike), which also writes `viewer/tests/fixtures/geometry.cases.json`;
Vitest replays every case through the WASM under Node and compares.

**Tech Stack:** .NET 10.0.400 SDK, `Microsoft.NET.Sdk.WebAssembly`, `System.Runtime.InteropServices.JavaScript`
(`[JSExport]`), System.Text.Json source generation, xUnit; Vite 8, Svelte 5, Vitest 5, Node 24; Caddy 2.

**Spec:** `docs/superpowers/specs/2026-10-24-browser-play-design.md` → Coordinator's calls → *Drags are solid, as in Godot*,
*Heights*, *Moves in the browser*, *What play shows*.

## Findings that shape the plan

- **No workload needed.** A spike (`Microsoft.NET.Sdk.WebAssembly`, `OutputType Exe`, `AllowUnsafeBlocks`,
  `InvariantGlobalization`, a project reference to `Tabletop.Geometry`) builds and publishes with the installed SDK and no
  workloads (`dotnet workload list` is empty). Publish trims (IL) and prints *"strongly recommend using `wasm-tools`"*: that
  workload adds native relinking and AOT (emscripten), which only shrink `dotnet.native.wasm`; not used. The runtime pack
  comes from NuGet on restore, so CI and the SDK image need nothing extra. Build warns CA1416 on `[JSExport]`;
  `<SupportedPlatform Include="browser" />` silences it.
- **Size (spike, Geometry + System.Text.Json):** about 4.8 MB raw, 1.8 MB gzip, 1.5 MB brotli. `dotnet.native.wasm` 3.0 MB
  (976 KB br), `System.Private.CoreLib` 1.2 MB (380 KB br), `System.Text.Json` 220 KB, the rest under 50 KB each. Publish
  writes `.br` and `.gz` beside every file; Caddy serves them with `file_server { precompressed br gzip }`.
- **Node loads it.** `await import(file:///…/_framework/dotnet.js)`, `dotnet.create()`, `getAssemblyExports(name)` runs under
  plain Node 24 (110 ms) and under the viewer's Vitest. Vitest prints source-map warnings for `dotnet.js` unless the folder is
  external: `test.server.deps.external: [/_framework\//]` silences them (checked).
- **A `net10.0` project references the WebAssembly project** and calls its plain static methods (checked), so the facade's
  xUnit tests need no source linking.
- **JSON with source generation works trimmed**: a `JsonSerializerContext` plus `JsonStringEnumConverter<TEnum>(SnakeCaseLower)`
  per enum reads the wire's own shapes (`{"shape":"circle"}`, `"declarations":"impassable, blocks_line_of_sight"`) and
  `Collision.Land` answered correctly from Node.
- **What Godot calls** (`DragBodies.cs`, `ContactCheck.cs`, `RangeRings.cs`, `HoverView.cs`): `Collision.Land` and `Hover`
  (model and piece), `Clear` (turn), `Surfaces`/`RestNear`, `Overlap.Bases`, `Movement.Check`, `Distance.*`, `BaseEdge`.
- **Not exported, by the spec:** `Collision.Follow`, `Separate`, `Slide` (the at-a-level drag; *Heights*: the browser always
  drops lifted), `MoveReach`/`Movement.Region` (*Moves in the browser*: no reach outline), so `Land`'s `within` is absent: a
  landing past the allowance is the server's to refuse. `LineOfSight` (sight is the server's `SightAnswer`).
- **JSON, not a compact form.** The inputs are the wire's objects the viewer already holds (`TerrainPlacement`, `Base`,
  `Placement`, `TableSize`), typed by `wire.generated.ts` and written by `ContentJson`'s conventions; `JSON.stringify` of them
  needs no mapping and the C# reads them with the same converters. A drag frame sends the table's pieces (tens of volumes, a
  few KB) and parses in well under a millisecond, while `Land` searches up to 30,000 spots; typed arrays would add a second
  hand-kept layout on both sides for no measurable gain.

## Global Constraints

- Nothing here names a rule; Ask parameters arrive as parameters (`libs/Tabletop.Geometry/AGENTS.md`).
- `Tabletop.Geometry` keeps no project references; the Wasm project references only it.
- Playback never downloads the runtime: only `loadGeometry()` fetches it, and no playback module imports
  `src/lib/geometry/`.
- `viewer/public/geometry/` and `build/geometry-wasm/` are build output, never committed.
- Commands: `dotnet build`, `dotnet test tests/Tabletop.Geometry.Wasm.Tests`, `dotnet test tests/Tabletop.Client.Tests`,
  `./scripts/geometry-wasm`, `cd viewer && npm test && npm run check && npm run build:hosted`.

## Review Focus

- The facade's compositions equal `DragBodies.Models` (lifted, `free` false, `within` null) and `DragBodies.Piece` (lifted).
- `Contacts.Of` and `RangeOutline.Of` are moves, not rewrites: the client's `ContactCheckTests`, `RangeRingsTests` and
  `RangeRingsPackedTests` pass unchanged apart from `using` lines.
- The CSP adds only `script-src 'self' 'wasm-unsafe-eval'`.
- The TypeScript `Geometry` interface (Task 5) is what the setup and play plans consume.

---

### Task 1: `RangeOutline` and `Contacts` move into `Tabletop.Geometry`

**Files:** create `libs/Tabletop.Geometry/RangeOutline.cs`, `libs/Tabletop.Geometry/Contacts.cs`; modify
`client/src/Core/RangeRings.cs`, `client/src/Core/ContactCheck.cs`, `libs/Tabletop.Geometry/AGENTS.md` (Layout),
`client/AGENTS.md` (`ContactCheck` line); test `tests/Tabletop.Geometry.Tests/RangeOutlineTests.cs`,
`tests/Tabletop.Geometry.Tests/ContactsTests.cs`; `using Tabletop.Geometry;` where `tests/Tabletop.Client.Tests` names
`ContactKind`/`Contact`.

**Interfaces:**

```csharp
namespace Tabletop.Geometry;
/// <summary>The union outline of the rings at `range` from the base edge of each model (RangeRings' algorithm, unchanged).</summary>
public static class RangeOutline
{
    public const int Points = 96;
    public static IReadOnlyList<(Point2 A, Point2 B)> Of(IReadOnlyList<PlacedModel> models, double range);
}

public enum ContactKind { OnBase, Crosses, TooClose, InImpassable }
public sealed record Contact(ContactKind Kind, Guid? UnitId);
/// <summary>A model on the table or proposed: its unit, body and where it stands (null off the table).</summary>
public sealed record ContactBody(Guid Id, Guid UnitId, ModelBody Body, Placement? At);
/// <summary>A move Ask's measuring terms (MoveRegion's Allowance, ImpassableVolumeIds, DifficultVolumeIds, DifficultCap).</summary>
public sealed record ContactMove(double Allowance, IReadOnlyList<Guid> Impassable, IReadOnlyList<Guid> Difficult, double? DifficultCap);
/// <summary>Separation, PassesModels, whether a landing in Impassable terrain counts, the move (null for a placement), the table, the capped allowance.</summary>
public sealed record ContactTerms(double? Separation, bool PassesModels, bool Landing, ContactMove? Move = null, TableSize? Size = null, double? Unit = null);
public static class Contacts
{
    /// <summary>Each proposed model's first contact, in ContactKind order (ContactCheck.Of's body, unchanged).</summary>
    public static IReadOnlyDictionary<Guid, Contact> Of(IReadOnlyList<ContactBody> models, IReadOnlyDictionary<Guid, Placement> proposed,
        IReadOnlyList<WorldVolume> volumes, ContactTerms terms);
}
```

`RangeRings.Of` keeps its signature and calls `RangeOutline.Of`; `RangeRings.Points` becomes `const int Points = RangeOutline.Points`.
`ContactCheck.Move`/`Placement` keep their signatures and build `ContactBody` rows from `p.Models` (`m.Location is OnTable t ?
t.Placement : null`) and `ContactTerms` from the `MoveRegion` (`Landing: !legal.IgnoresTerrain`). The private `Contact`
constant inside `Collision.Slide.cs` shadows the type only inside `Collision`, which never names the type.

- [ ] Write `RangeOutlineTests` first: `One_round_base_is_one_closed_ring_at_range_plus_radius` (a 1" circle at (10,10),
  range 6: 96 segments, every endpoint 6.5 ± 1e-9 from the centre, last B equals first A);
  `Two_far_apart_bases_give_two_rings` (192 segments); `A_covered_arc_is_dropped` (bases 2" apart, range 6: fewer than 192
  segments, no endpoint nearer than 6 − 1e-6 to either base edge by `Distance.EdgeToEdge(model, point)`).
- [ ] Write `ContactsTests` first: `A_landing_on_a_base_is_OnBase_with_that_unit`, `A_landing_in_an_impassable_solid_is_InImpassable`
  (`Landing: true`), `Landing_false_ignores_impassable`, `A_path_only_through_a_model_is_Crosses` (a 1" model boxed by three
  others, `Move` allowance 3, `PassesModels: false`), `Within_separation_of_another_unit_is_TooClose`,
  `A_unit_already_that_close_at_the_start_is_left_to_the_server`, `A_model_answered_where_it_stands_has_no_contact`.
- [ ] Run `dotnet test tests/Tabletop.Geometry.Tests --filter "RangeOutline|Contacts"`: fails to compile.
- [ ] Move `Union`, `Within`, `Reach` from `RangeRings.cs` into `RangeOutline` (rename `Union` to `Of`, keep the bodies);
  move `ContactKind`, `Contact` and the body of `ContactCheck.Of` into `Contacts.Of` (`p.Models[id]` lookups become a
  dictionary of `ContactBody` by id; `legal` becomes `terms.Move`). Rewire the client.
- [ ] Run `dotnet test tests/Tabletop.Geometry.Tests` and `dotnet test tests/Tabletop.Client.Tests`: pass, with
  `ContactCheckTests`, `RangeRingsTests` and `RangeRingsPackedTests` untouched but for `using`. `./scripts/check-client`: clean.
- [ ] Commit: `Geometry: RangeOutline and Contacts, moved from the client's core`.

### Task 2: `libs/Tabletop.Geometry.Wasm`: the facade

**Files:** create `libs/Tabletop.Geometry.Wasm/Tabletop.Geometry.Wasm.csproj`, `Program.cs`, `Requests.cs`, `FacadeJson.cs`,
`Facade.cs`, `Exports.cs`, `AGENTS.md`, `CLAUDE.md` (`@AGENTS.md`); `tests/Tabletop.Geometry.Wasm.Tests/` (`.csproj`
referencing the Wasm project, `Tabletop.Geometry` and, for the JSON parity test only, `Tabletop.Content`; `FacadeTests.cs`,
`FacadeJsonTests.cs`); add both to `Tabletop.sln`; root `AGENTS.md` Where things are (`libs/` row).

The csproj: `Sdk="Microsoft.NET.Sdk.WebAssembly"`, `net10.0`, `OutputType Exe`, `AllowUnsafeBlocks true`,
`InvariantGlobalization true`, `Nullable`/`ImplicitUsings` enabled, `LangVersion 12`,
`<SupportedPlatform Include="browser" />`, `<ProjectReference Include="../Tabletop.Geometry/Tabletop.Geometry.csproj" />`.
`Program.cs` is `return;` (the runtime needs an entry assembly; the browser never runs `Main`).

**Interfaces** (`namespace Tabletop.Geometry.Wasm`; JSON is camelCase, enums snake_case strings, nulls omitted, as `ContentJson`):

```csharp
// Requests.cs: the wire's own shapes. Piece reads a wire TerrainPlacement (its `mesh` is ignored).
public sealed record PieceVolume(Guid Id, Volume Volume);
public sealed record Piece(Guid Id, Point3 Position, Yaw Yaw, double Scale, IReadOnlyList<PieceVolume> Volumes)
{ public IReadOnlyList<WorldVolume> WorldVolumes() => Volumes.Select(v => new WorldVolume(v.Id, v.Volume, new(Position, Yaw, Scale))).ToList(); }
public sealed record HeldModel(Guid Id, PlacedModel At, Point2 To);
public sealed record LandRequest(IReadOnlyList<HeldModel> Held, IReadOnlyList<PlacedModel> Stationary, IReadOnlyList<Guid> Walls, IReadOnlyList<Piece> Terrain);
public sealed record HeldEnd(Guid Id, Point3 Drawn, Point3 Lands);
public sealed record PieceRequest(IReadOnlyList<Piece> Terrain, Guid PieceId, Placement At, Point2 To);
public sealed record PieceEnd(Point3 Drawn, Point3 Lands);
public sealed record TurnRequest(IReadOnlyList<Piece> Terrain, Guid PieceId, Yaw Yaw);
public sealed record ModelRestRequest(PlacedModel Model, IReadOnlyList<Piece> Terrain, double? Level);      // null: the highest
public sealed record PieceRestRequest(IReadOnlyList<Piece> Terrain, Guid PieceId, Placement At, double? Level);
public sealed record ContactsRequest(IReadOnlyList<ContactBody> Models, IReadOnlyDictionary<Guid, Placement> Proposed, IReadOnlyList<Piece> Terrain, ContactTerms Terms);
public sealed record ContactFound(Guid ModelId, ContactKind Kind, Guid? UnitId);
public sealed record PairRequest(PlacedModel A, PlacedModel B);
public sealed record PointRequest(PlacedModel Model, Point2 Point);
public sealed record MarkerRequest(PlacedModel Model, Point2 Centre, double Diameter);
public sealed record NearestRequest(IReadOnlyList<PlacedModel> From, IReadOnlyList<PlacedModel> To);
public sealed record Nearest(double Distance, int From, int To);
public sealed record RingRequest(IReadOnlyList<PlacedModel> Models, double Range);
public sealed record Segment(Point2 A, Point2 B);
public sealed record ZoneRequest(string Shape, TableSize Size, Point2? Point, PlacedModel? Model);
public sealed record TableRequest(TableSize Size, PlacedModel Model);

// Facade.cs: each method reads its request with FacadeJson.Options and writes its answer the same way.
public static class Facade
{
    public static string LandModels(string json);    // LandRequest → HeldEnd[]   (DragBodies.Models, lifted)
    public static string LandPiece(string json);     // PieceRequest → PieceEnd    (DragBodies.Piece, lifted)
    public static string TurnPiece(string json);     // TurnRequest → Point3        (DragBodies.Turn)
    public static string ModelSurfaces(string json); // ModelRestRequest → double[] (Collision.Surfaces; Level unused)
    public static string RestModel(string json);     // ModelRestRequest → double   (Collision.RestNear, Level ?? +∞)
    public static string PieceSurfaces(string json); // PieceRestRequest → double[] (DragBodies.Surfaces)
    public static string RestPiece(string json);     // PieceRestRequest → double
    public static string Contacts(string json);      // ContactsRequest → ContactFound[] (Contacts.Of)
    public static string OverlapBases(string json);  // PairRequest → bool
    public static string EdgeToEdge(string json);    // PairRequest → double
    public static string BodyToBody(string json);    // PairRequest → double
    public static string EdgeToPoint(string json);   // PointRequest → double
    public static string EdgeToMarker(string json);  // MarkerRequest → double
    public static string NearestPair(string json);   // NearestRequest → Nearest (HoverView's MinBy over EdgeToEdge)
    public static string RangeOutline(string json);  // RingRequest → Segment[]
    public static string ZonePolygons(string json);  // ZoneRequest → Point2[][]  (ZoneStyles.Resolve)
    public static string ZoneContains(string json);  // ZoneRequest(Point) → bool  (ZoneStyles.Contains)
    public static string ZoneHoldsBase(string json); // ZoneRequest(Model) → bool  (ZoneStyles.BaseFullyWithin)
    public static string TableHoldsBase(string json);// TableRequest → bool        (Table.ContainsBase)
}

// Exports.cs: one [JSExport] per Facade method, same name and signature, body `=> Facade.X(json)`.
public static partial class Exports { [JSExport] public static string LandModels(string json) => Facade.LandModels(json); /* … */ }

// FacadeJson.cs
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LandRequest))] /* … every request and answer type, double[], Point2[][], bool, double */
internal partial class FacadeJsonContext : JsonSerializerContext;
public static class FacadeJson
{   /// <summary>The context's options plus JsonStringEnumConverter&lt;T&gt;(SnakeCaseLower, allowIntegerValues: false) for BaseShape,
    /// FootprintKind, VolumeKind, TerrainDeclarations and ContactKind.</summary>
    public static JsonSerializerOptions Options { get; } }
```

`LandModels`: volumes are every piece's `WorldVolumes()`; walls the volumes whose id is in `Walls`; `Lands` from
`Collision.Land(held.Select(h => (h.At, h.To, (Func<Point2,bool>?)null)), stationary, walls, volumes)`; `Drawn` is
`(To.X, To.Y, Collision.Hover(at To, volumes, stationary))`, as `DragBodies.Models`. `LandPiece`/`TurnPiece`/`PieceSurfaces`/
`RestPiece` copy `DragBodies.Piece`'s lifted branch, `Turn`, `Surfaces` and the free branch's `RestNear`: others are the other
pieces' volumes, walls those declared Impassable, a piece with no volumes lands flat at the target. A JSON that does not read
throws `JsonException`, which reaches JavaScript as an `Error` with its message.

- [ ] Write `FacadeJsonTests` first: `Reads_what_ContentJson_writes` (a `TerrainPlacement` with a Solid Impassable +
  BlocksLineOfSight rect volume and an Area circle, written by `ContentJson.Serialize`, read as `Piece`: equal
  `WorldVolumes()` to `TerrainPlacement.WorldVolumes()`); `Writes_enums_as_ContentJson_does` (a `ContactFound` with
  `ContactKind.InImpassable` serializes `"kind":"in_impassable"`; a `Base.Oval` round-trips through both option sets to the
  same JSON); `Rejects_an_integer_enum` (`{"shape":0,…}` throws `JsonException`).
- [ ] Write `FacadeTests` first, each comparing the facade's JSON answer with the direct call:
  `LandModels_equals_Land_and_Hover` (target inside a 4"×4" Impassable solid at (10,10) and a second held model aimed at the
  first's target: lands (12.5,10,0) and the next clear spot; `Drawn.Z` = 3.5); `LandModels_ignores_volumes_not_named_walls`;
  `LandPiece_equals_DragBodies_Piece_lifted` (a crate dropped onto an Impassable block lands beside it; dropped onto a
  non-Impassable hill rests on its top); `A_piece_with_no_volumes_lands_flat`; `TurnPiece_moves_clear_of_an_impassable_piece`;
  `RestModel_without_level_is_the_highest_open_surface` and `RestModel_at_level_zero_under_a_bridge_is_zero`;
  `PieceSurfaces_equals_Collision_Surfaces`; `Contacts_equals_Contacts_Of` (the four kinds in one request);
  `EdgeToEdge_BodyToBody_EdgeToPoint_EdgeToMarker_equal_Distance`; `NearestPair_picks_the_closest_pair_and_its_indices`;
  `RangeOutline_equals_RangeOutline_Of`; `Zone_answers_equal_ZoneStyles` (`frontline/1` on 72×48: polygons, a point inside and
  one outside, a base straddling the edge); `TableHoldsBase_equals_Table_ContainsBase`; `Bad_json_throws_JsonException`.
- [ ] Run `dotnet test tests/Tabletop.Geometry.Wasm.Tests`: fails to compile.
- [ ] Write `Requests.cs`, `FacadeJson.cs`, `Facade.cs`, `Exports.cs`, `Program.cs`. Run the tests: pass. `dotnet build`: no
  warnings from the new project.
- [ ] Commit: `Geometry.Wasm: the browser facade over Tabletop.Geometry`.

### Task 3: `scripts/geometry-wasm` and the dev stack

**Files:** create `scripts/geometry-wasm`; modify `viewer/.gitignore` (`public/geometry/`), `.gitignore` (`build/` if not
already ignored), `.dockerignore` (`viewer/public/geometry`), `docker-compose.yml` (service `geometry-wasm`, `viewer`'s
`depends_on`).

**Interfaces:**

```bash
# scripts/geometry-wasm: publish libs/Tabletop.Geometry.Wasm (Release) and put its _framework/ where Vite serves it.
#   scripts/geometry-wasm [--out DIR]     DIR defaults to viewer/public/geometry
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd); out=$root/viewer/public/geometry
if [[ ${1:-} == --out ]]; then out=${2:?--out needs a directory}; fi
dotnet publish "$root/libs/Tabletop.Geometry.Wasm" -c Release -o "$root/build/geometry-wasm" --nologo
rm -rf "$out/_framework"; mkdir -p "$out"
cp -r "$root/build/geometry-wasm/wwwroot/_framework" "$out/_framework"
```

Compose: `geometry-wasm` is `<<: *dotnet`, `command: ["./scripts/geometry-wasm"]`, `environment: { <<: *dotnet-env,
TabletopBuildSlot: -wasm }`, `depends_on: { dev-init: { condition: service_completed_successfully } }`, no ports, `restart:
"no"`. `viewer` restates the anchor's `depends_on` (a merge key does not merge nested maps) and adds `geometry-wasm: {
condition: service_completed_successfully }`. A Geometry change needs `docker compose up -d geometry-wasm` (or
`./scripts/geometry-wasm` on the host); Vite serves the new files on reload.

- [ ] Write the script; `chmod +x`.
- [ ] Run `./scripts/geometry-wasm`; expect `viewer/public/geometry/_framework/dotnet.js` and one
  `Tabletop.Geometry.Wasm.*.wasm`, and `git status --short` showing no file under `viewer/public/geometry/` or `build/`.
- [ ] Run `du -ch viewer/public/geometry/_framework/*.br | tail -1`; expect at most 2 MB (record the figure in the commit
  body).
- [ ] Run `docker compose up -d geometry-wasm viewer`, then `curl -sI http://127.0.0.1:5174/geometry/_framework/dotnet.js`
  (`200`, `text/javascript`) and the same for the `dotnet.native.*.wasm` file (`application/wasm`).
- [ ] Commit: `Geometry.Wasm: scripts/geometry-wasm and the compose one-shot`.

### Task 4: the agreement fixtures

**Files:** create `tests/Tabletop.Geometry.Wasm.Tests/AgreementCases.cs`, `AgreementFixtureTests.cs`, `RepoPaths.cs` (as
`tests/Tabletop.Viewer.Tests/RepoPaths.cs`); generated `viewer/tests/fixtures/geometry.cases.json` (checked in).

**Interfaces:**

```csharp
/// <summary>One call: the facade method, the request as the browser would send it, and the answer of the direct Geometry call.</summary>
public sealed record AgreementCase(string Name, string Method, JsonElement Request, JsonElement Expected);
public static class AgreementCases { public static IReadOnlyList<AgreementCase> All { get; } }
```

`All` holds at least one case per `Facade` method and the cases of Task 2's `FacadeTests`, plus a busy table: the built-in
catalog's `large-sight-blocker` and `ruin` pieces (copied by value into the test, not read from `client/`), ten 25 mm and 32 mm
models, a held group of three landing among them. `Expected` is computed by calling `Tabletop.Geometry` directly (never through
`Facade`), written with `FacadeJson.Options`.

- [ ] Write `AgreementFixtureTests` first: `Every_facade_method_has_a_case` (reflection over `Facade`'s public static methods
  against `All`'s `Method` set); `The_facade_answers_every_case_as_Geometry_does` (each `Facade.<Method>(Request)` parsed and
  compared to `Expected`, numbers within 1e-9); `The_checked_in_fixture_is_current` (`TABLETOP_WRITE_FIXTURES=1` writes
  `viewer/tests/fixtures/geometry.cases.json`, indented, `\n` endings; otherwise asserts the file is byte for byte what it
  would write, failing with "run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Geometry.Wasm.Tests").
- [ ] Run `dotnet test tests/Tabletop.Geometry.Wasm.Tests`: the first two fail (no cases), the third fails (no file).
- [ ] Write `AgreementCases`; run `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Geometry.Wasm.Tests`, then
  `dotnet test tests/Tabletop.Geometry.Wasm.Tests`: pass.
- [ ] Commit: `Geometry.Wasm: agreement cases and the viewer fixture`.

### Task 5: `viewer/src/lib/geometry/`: the typed wrapper

**Files:** create `viewer/src/lib/geometry/types.ts`, `load.ts`, `walls.ts`, `index.ts` (re-exports); modify
`viewer/vite.config.ts` (`test.server.deps.external: [/_framework\//]`); test `viewer/tests/geometry.agreement.test.ts`,
`viewer/tests/geometry.wrapper.test.ts`, `viewer/tests/geometry.lazy.test.ts`, helper `viewer/tests/geometry.ts`.

**Interfaces** (the setup and play plans consume exactly this):

```ts
// types.ts
import type { Base, Placement, Point3, TableSize, TerrainPlacement, Yaw } from '../protocol/wire.generated';
export type { Base, Placement, Point3, TableSize, TerrainPlacement, Yaw };
/** A table point in inches. Answers also carry the wire Point2's `length`, which callers ignore. */
export interface XY { readonly x: number; readonly y: number }
export interface ModelBody { readonly base: Base; readonly height: number }
export interface PlacedModel { readonly body: ModelBody; readonly placement: Placement }
export interface HeldModel { readonly id: string; readonly at: PlacedModel; readonly to: XY }
/** Where a held model is drawn (0.5" over the highest top under it) and where it lands. */
export interface HeldEnd { readonly id: string; readonly drawn: Point3; readonly lands: Point3 }
export interface PieceEnd { readonly drawn: Point3; readonly lands: Point3 }
export type ContactKind = 'on_base' | 'crosses' | 'too_close' | 'in_impassable';
/** A model on the table (`at` set) or off it; proposed placements go in `contacts`' map. */
export interface ContactBody { readonly id: string; readonly unitId: string; readonly body: ModelBody; readonly at?: Placement }
export interface ContactMove {
  readonly allowance: number; readonly impassable: readonly string[]; readonly difficult: readonly string[]; readonly difficultCap?: number;
}
export interface ContactTerms {
  readonly separation?: number; readonly passesModels: boolean; readonly landing: boolean;
  readonly move?: ContactMove; readonly size?: TableSize; readonly unit?: number;
}
export interface Contact { readonly modelId: string; readonly kind: ContactKind; readonly unitId?: string }
export interface Nearest { readonly distance: number; readonly from: number; readonly to: number }
export interface Segment { readonly a: XY; readonly b: XY }
type Terrain = readonly TerrainPlacement[];

/** Tabletop.Geometry in the browser. Every call is synchronous once loaded; inches, table frame, Z up. */
export interface Geometry {
  /** Lifted held models in drag order (DragBodies.Models, lifted): `walls` are volume ids (wallsOf). */
  landModels(held: readonly HeldModel[], stationary: readonly PlacedModel[], walls: readonly string[], terrain: Terrain): HeldEnd[];
  /** A lifted setup piece held at `at` (its yaw included), cursor at `to` (DragBodies.Piece, lifted). */
  landPiece(terrain: Terrain, pieceId: string, at: Placement, to: XY): PieceEnd;
  /** A piece turned in place to `yaw`: where it stands, or the nearest spot clear of Impassable pieces, rested. */
  turnPiece(terrain: Terrain, pieceId: string, yaw: Yaw): Point3;
  modelSurfaces(model: PlacedModel, terrain: Terrain): number[];
  /** The open surface nearest `level`; omitted, the highest (the browser's drop). */
  restModel(model: PlacedModel, terrain: Terrain, level?: number): number;
  pieceSurfaces(terrain: Terrain, pieceId: string, at: Placement): number[];
  restPiece(terrain: Terrain, pieceId: string, at: Placement, level?: number): number;
  /** Each proposed model's first contact (Contacts.Of); a model with none is absent. */
  contacts(models: readonly ContactBody[], proposed: ReadonlyMap<string, Placement>, terrain: Terrain, terms: ContactTerms): Map<string, Contact>;
  overlapBases(a: PlacedModel, b: PlacedModel): boolean;
  edgeToEdge(a: PlacedModel, b: PlacedModel): number;
  bodyToBody(a: PlacedModel, b: PlacedModel): number;
  edgeToPoint(model: PlacedModel, point: XY): number;
  edgeToMarker(model: PlacedModel, centre: XY, diameter: number): number;
  /** The closest pair edge to edge, by index into `from` and `to`; null when either is empty. */
  nearestPair(from: readonly PlacedModel[], to: readonly PlacedModel[]): Nearest | null;
  /** The union outline of the rings at `range` from each model's base edge (RangeOutline.Of). */
  rangeOutline(models: readonly PlacedModel[], range: number): Segment[];
  zonePolygons(shape: string, size: TableSize): XY[][];
  zoneContains(shape: string, size: TableSize, point: XY): boolean;
  /** The whole base inside one of the zone's polygons (p.6 "fully within"). */
  zoneHoldsBase(shape: string, size: TableSize, model: PlacedModel): boolean;
  tableHoldsBase(size: TableSize, model: PlacedModel): boolean;
  /** One facade method by name on a JSON request: the agreement test's door. */
  call(method: string, json: string): string;
}

// load.ts
/** Fetches the .NET runtime and the facade from `base` (default `${import.meta.env.BASE_URL}geometry/_framework/`) on the
 *  first call, once per base; a failed load is forgotten so the next call retries. */
export function loadGeometry(base?: string): Promise<Geometry>;

// walls.ts
/** DragBodies.Walls: inside a move Ask the ids it lists as impassable, else every volume declared Impassable. */
export function wallsOf(terrain: Terrain, impassableVolumeIds?: readonly string[]): string[];
/** A placed model from a wire slot's base and height. */
export function placed(slot: { readonly base: Base; readonly height: number }, placement: Placement): PlacedModel;
```

`load.ts` imports `${base}dotnet.js` with `import(/* @vite-ignore */ url)`, calls `dotnet.create()`, then
`getAssemblyExports('Tabletop.Geometry.Wasm')` and takes `.Tabletop.Geometry.Wasm.Exports`; each method is
`JSON.parse(exports.X(JSON.stringify(request)))`, with `level` and other absent optionals left `undefined` so the JSON omits
them, `proposed` sent as `Object.fromEntries`, `contacts`' array returned as a `Map` by `modelId`, and `nearestPair` answering
null without a call when a list is empty.

- [ ] Write `tests/geometry.ts`: `loadTestGeometry()` = `loadGeometry(pathToFileURL(resolve('public/geometry/_framework/')).href
  + '/')`, throwing "run ./scripts/geometry-wasm" when `dotnet.js` is missing.
- [ ] Write `geometry.agreement.test.ts` first: `describe.each(cases)` over `tests/fixtures/geometry.cases.json`, `it('<name>
  agrees with C#')`: `JSON.parse(g.call(c.method, JSON.stringify(c.request)))` deep-equals `c.expected` with numbers within
  1e-9 (a recursive `close` helper); and `it('covers every Geometry method')`: the set of fixture methods equals the facade's
  nineteen names.
- [ ] Write `geometry.wrapper.test.ts` first: `restModel without a level takes the highest open surface` (a model over a 3"
  solid: 3); `contacts returns a Map keyed by model id` (`get(id)?.kind === 'on_base'`, the unit id carried);
  `nearestPair of an empty list is null`; `wallsOf picks Impassable volumes, or the listed ids inside an Ask`;
  `landModels lands beside an Impassable solid` ((12.5, 10, 0), drawn z 3.5); `loadGeometry loads once` (two calls, same
  object); `a bad request throws an Error` (`restModel` with a base shape `'hexagon'`).
- [ ] Write `geometry.lazy.test.ts` first: `no playback module imports lib/geometry` (every `.ts`/`.svelte` under
  `src/lib/{feed,live,board,timeline,recording,asks,hosted,suite}` and `src/lib/components`, `src/App.svelte`, `src/main.ts`
  has no `geometry` import specifier).
- [ ] Run `./scripts/geometry-wasm && cd viewer && npm test`: the new tests fail (no module).
- [ ] Write `types.ts`, `load.ts`, `walls.ts`, `index.ts`; set `test.server.deps.external`. Run `npm test` (pass, no
  source-map warnings) and `npm run check` (clean).
- [ ] Commit: `Viewer: lib/geometry, Tabletop.Geometry as WebAssembly, and its agreement test`.

### Task 6: the hosted build, the release and CI

**Files:** modify `web/Dockerfile`, `web/Caddyfile`, `scripts/release`, `.github/workflows/ci.yml`.

**Interfaces:**

```dockerfile
# web/Dockerfile: a stage before `viewer`; the viewer stage copies its output into public/ before build:hosted.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS geometry
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY libs/Tabletop.Geometry/ libs/Tabletop.Geometry/
COPY libs/Tabletop.Geometry.Wasm/ libs/Tabletop.Geometry.Wasm/
COPY scripts/geometry-wasm scripts/
RUN ./scripts/geometry-wasm --out /out
# in the viewer stage, after `COPY viewer/ ./`:
COPY --from=geometry /out/_framework public/geometry/_framework
```

```caddyfile
# web/Caddyfile, handle /viewer/*: the policy gains script-src; the file server serves publish's .br/.gz.
header Content-Security-Policy "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self' wss://{host}; img-src 'self' data:; style-src 'self' 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'"
try_files {path} /viewer/index.html
file_server {
	precompressed br gzip
}
```

`'wasm-unsafe-eval'` lets `WebAssembly.instantiate` compile the runtime; it grants no JavaScript `eval`. The runtime's
`fetch` of its files is same-origin (`connect-src 'self'`). `/viewer/geometry/_framework/*` keeps `no-cache`: `dotnet.js` is
not fingerprinted, the rest are, and revalidation costs a 304.

`scripts/release`: the non-home path runs `run ./scripts/geometry-wasm` before `run npm --prefix viewer ci`; the home path
runs `in_sdk ./scripts/geometry-wasm` before the `in_node` checks (the image build has its own stage).
`ci.yml`'s `viewer` job gains, before `npm ci`: `actions/setup-dotnet@v4` with `global-json-file: global.json` and
`run: ../scripts/geometry-wasm` (working directory `viewer`). The `dotnet` job builds and tests the Wasm project through the
solution.

- [ ] Edit the four files.
- [ ] Run `docker build -f web/Dockerfile --build-arg TABLETOP_BLOB_ORIGIN=https://files.example.org -t tabletop-web:wasm .`
  then `docker run --rm -d -p 127.0.0.1:8089:8080 --name tt-wasm tabletop-web:wasm`.
- [ ] `curl -sI http://127.0.0.1:8089/viewer/` shows the policy with `'wasm-unsafe-eval'`;
  `curl -sI -H 'Accept-Encoding: br' http://127.0.0.1:8089/viewer/geometry/_framework/<dotnet.native file>` shows
  `Content-Encoding: br` and `Content-Type: application/wasm`.
- [ ] The runtime starts under the policy, in a real browser (before `docker stop`): in a scratch directory outside the repo,
  `npm i playwright-core@1.56.1`, then a `csp.mjs` that launches `chromium.launch({ executablePath: <the headless shell under
  ~/.cache/ms-playwright/chromium_headless_shell-*/> })`, opens `http://127.0.0.1:8089/viewer/`, records `console` errors,
  and runs `page.evaluate(async () => { const { dotnet } = await import('/viewer/geometry/_framework/dotnet.js'); const r =
  await dotnet.create(); const e = await r.getAssemblyExports('Tabletop.Geometry.Wasm'); return
  e.Tabletop.Geometry.Wasm.Exports.EdgeToEdge(JSON.stringify(<two 1" models 3" apart>)); })`. Expect `"2"` and no
  `Content Security Policy` console error. Repeat against an image built with the old policy: expect a `wasm-unsafe-eval`
  violation, which proves the check bites. Then `docker stop tt-wasm`.
- [ ] Run `bash -n scripts/release` (clean).
- [ ] Commit: `Geometry.Wasm: built into the web image, the release and CI; CSP allows WebAssembly`.

### Task 7: docs

**Files:** modify `viewer/AGENTS.md`, `libs/Tabletop.Geometry.Wasm/AGENTS.md` (from Task 2), `libs/Tabletop.Geometry/AGENTS.md`
(consumers), `deploy/README.md` (the viewer build line), root `AGENTS.md` (Get running), `docs/design/viewer/design.md`.

- `viewer/AGENTS.md`: Layout gains `src/lib/geometry/` (`loadGeometry`, `Geometry`, `wallsOf`, `placed`; the runtime in
  `public/geometry/_framework/` from `scripts/geometry-wasm`, never committed); How it works: fetched on the first
  `loadGeometry()`, about 1.5 MB brotli, never by playback; Commands: `./scripts/geometry-wasm` before `npm test`;
  Conventions: a Geometry change reruns `TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Geometry.Wasm.Tests` and
  `./scripts/geometry-wasm`; the hosted CSP's `'wasm-unsafe-eval'` lets the runtime start.
- `libs/Tabletop.Geometry.Wasm/AGENTS.md`: the browser facade (`browser-wasm`, no workload); Layout; JSON in and out with
  `FacadeJson`; one `[JSExport]` per `Facade` method; lifted-only compositions; Commands; pitfall: a new method needs its
  `[JSExport]`, a `JsonSerializable` entry, an agreement case and a `Geometry` method, a new enum a converter.

- [ ] Write the edits. The hosted build stays admin-only here; opening it is the setup plan's.
- [ ] Commit: `Docs: Geometry in the browser`.
