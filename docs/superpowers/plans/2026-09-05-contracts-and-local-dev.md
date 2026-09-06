# Contracts and Local Development Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the three shared libraries under `libs/` as types with no engine implementations behind them, the rules-free event applier and its projection, three xUnit test projects, and the local development setup (Postgres and MinIO in Docker, the schema as SQL).

**Architecture:** `Tabletop.Geometry` holds the geometry value types and the declared (unimplemented) distance / line-of-sight / settling API. `Tabletop.Content` holds the army snapshot and table-state records all three deliverables read. `Tabletop.Protocol` holds every socket message — commands, events, Asks, presence, edits — the JSON wire format, and `EventApplier`, which builds a `Projection` by copying what events say. Nothing in `libs/` knows Godot, Postgres or a ruleset.

**Tech Stack:** .NET 10 (`net10.0`, C# 12), System.Text.Json polymorphism, xUnit 2.9, Docker Compose (Postgres 17, MinIO), plain SQL.

**Spec:** `docs/architecture.md`; `docs/schema.md`; `docs/superpowers/specs/2026-08-29-geometry-values-design.md`, `2026-08-29-identity-and-enumerations-design.md`, `2026-08-29-content-values-design.md`, `2026-08-27-table-domain-model-design.md`, `2026-08-28-game-flow-design.md`, `2026-08-28-resolution-design.md`, `2026-08-30-presence-design.md`, `2026-09-03-action-sequences-design.md`, `2026-09-04-scheduler-design.md`, `2026-09-04-setup-phase-design.md`, `2026-09-04-client-shell-design.md`, `2026-09-04-spell-effects-design.md`, `2026-09-04-whole-design-review.md`, `2026-08-29-setup-content-design.md`, `2026-08-28-army-design.md`, `2026-08-28-asset-library-design.md`. `docs/workstreams/01-contracts.md` is stale and is not followed (it names `EndActivation` and `DiceRolled`, neither of which exists, and "terrain multipliers", an invention).

## Global Constraints

- `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`, root namespace = project name (matches `client/Tabletop.csproj`).
- `global.json` pins SDK `10.0.400`; `Directory.Build.props` puts `obj/` under `obj/$(OS)/` for every project, the new ones included. Never add a shared `obj/`.
- `libs/` projects reference nothing but each other and the BCL. No Godot, no Npgsql, no ORM, no persistence attributes (`architecture.md` → Persistence).
- The rules engine is not in `libs/`. No legality, dice, turn order or ruleset code appears here (`architecture.md` → What is actually shared).
- One world unit = one inch; yaw and orientation in degrees; `Tabletop.Geometry` does no unit conversion (geometry values → Bases).
- Identifiers are `Guid` created with `Guid.CreateVersion7()`; no integer ids.
- Every closed set is a C# `enum` or a sealed union of records, never a free string (identity and enumerations → The closed sets). What stays `string` is only what a *later ruleset or a content inventory* must be able to extend without a Protocol change, and every one of these has a spec saying so:
  - **Named per ruleset**, so a ruleset with one nobody anticipated needs content rather than a code change (identity and enumerations → The closed sets, stated for actions and applied to the rest by the same argument): action names, stat names, condition names, catalog entry names, and the `GameRules` identifier itself.
  - **Rows in the pinned `Properties` set**, so an effect can target them (action sequences → Rolls become rows; `schema.md` → An action is a row): roll names, action names again.
  - **Supplied by `GameRules`**, which is per-ruleset C# and of which only OPR's is built: Ask question names other than the engine's own in `AskQuestions`, sequence names, step names, phase names, window kinds (action sequences → constraint 9; scheduler → The line).
  - **Content inventories deliberately left undecided**: built-in mesh, texture and marker-visual names, and zone style and shape ids (setup content → the inventories; geometry values → Deployment shapes).
- What is **not** a string, and must not become one: `GameStatus`, `SchedulerShape`, `Enforcement`, `MarkerKind`, `SubjectKind`, `PlacementKind`, `BaseShape`, `FootprintKind`, `VolumeKind`, `TerrainDeclarations`, `StatKind`, `RejectReason`, `RefusalReason`, and the `StateChange`, `EventPayload`, `Command`, `Edit`, `LegalSet`, `AskAnswer`, `Location` and `SocketMessage` unions. The closed shape of a legal set and its answer is what makes the free-string question safe.
- Events carry every state change they made; the applier copies and never derives (client shell → The connection and the projection).
- JSON wire format: camelCase properties, string enums, `"kind"` type discriminator in snake_case, nulls omitted.
- Local only: Postgres and MinIO in Docker; no AWS; no hosting (`architecture.md` → Deployment, ruled 09-04).
- Commit after every task with a message in the repo's style (imperative, one line, no prefix), ending with the `Co-Authored-By` and `Claude-Session` trailers the session requires.

## Key decisions

Four owner rulings of 2026-09-06 land in this plan. The first three are built here; the fourth is a risk taken deliberately.

1. **Presence departure stays an explicit frame.** `PresenceEnded(ParticipantId)` is sent when a participant's last socket closes. The assumption in Task 11 and Task 12 is confirmed, nothing changes, and no client owns a presence timeout (ruling 1; open question 1 answered; spec correction 13).
2. **An unknown id is a typed desync.** `EventApplier` throws `ProjectionDesyncException(Kind, UnknownId)` at every lookup that can miss, in place of `KeyNotFoundException` and `InvalidOperationException`. The client drops its projection and rebuilds from the log; the game server faults the session. Task 14 (ruling 2).
3. **The server sends a frame when it comes to rest.** `AtRest(long Seq)`, wire kind `at_rest`, server to client, in Task 12's union (ruling 4).
4. **The payload format's lack of versioning is accepted.** `game_event.payload` is the serialized wire message and nothing versions it, so renaming a property breaks every stored log while every round-trip test still passes, because those tests prove self-consistency and not stability. Accepted, because nothing is built and no stored game matters yet. **Revisit trigger:** the first game worth keeping. The two ways out were considered and **neither is built now**: golden fixtures, a committed corpus of literal JSON per kind, which fails the build on a rename and needs regenerating on every intentional change; and a version stamped on the stored payload, which needs a reader per version from the first bump. Do not add a version field or a fixture corpus to any task in this plan (ruling 3).

---

## File Structure

```
Tabletop.sln                                   modified: three libs + three test projects
docker-compose.yml                             Postgres 17 + MinIO + bucket bootstrap
.env.example                                   local credentials (committed; .env is not)
CLAUDE.md                                      modified: commands, layout (tests/), local dev
scripts/postgres/init/001_schema.sql           the schema from docs/schema.md, two Postgres schemas
libs/Tabletop.Geometry/
  Tabletop.Geometry.csproj
  Points.cs            Point2, Point3, Yaw, Orientation, TableSize, Polygon
  Bases.cs             BaseShape, Base, ModelBody
  Volumes.cs           FootprintKind, Footprint, VolumeKind, TerrainDeclarations, Volume
  Placements.cs        Placement, TerrainTransform, PlacedModel, WorldVolume
  Api.cs               Distance, LineOfSight, LineOfSightResult, Settling, Containment, Overlap (declared, bodies throw)
libs/Tabletop.Content/
  Tabletop.Content.csproj                      references Geometry
  Stats.cs             StatKind, StatValue
  Army.cs              PropertyInstance, Weapon, ModelSlot, Unit, Army, ArmySnapshot
  TableState.cs        VisualRef, TerrainVolume, TerrainPlacement, MarkerKind, Marker, DeploymentZone, TableState
  ContentJson.cs       JsonSerializerOptions shared with Protocol
libs/Tabletop.Protocol/
  Tabletop.Protocol.csproj                     references Geometry, Content
  ProtocolVersion.cs   version constant, header name, 426
  Game.cs              GameStatus, SchedulerShape, Enforcement, Team, Participant, GameInfo, ArmyInPlay, Board
  State.cs             Location, Subject, Grant, Die, RollModifier, RollRecord, SequenceContext, ModelRelocation
  StateChanges.cs      StateChange union
  Asks.cs              Ask, AskOption, LegalSet union, AskAnswer union, AskQuestions
  Events.cs            EventPayload union, GameEvent, EventKinds
  Commands.cs          Command union
  Presence.cs          Presence, Cursor, Selection, PresencePlacement, PlacementKind, Measurement, Ping
  Edits.cs             Edit union (LobbyEdit, SetupEdit families)
  Wire.cs              SocketMessage union, RejectReason, RefusalReason, WireJson
  Projection.cs        Projection, UnitInPlay, ModelInPlay, Frame, CompletedStep, SchedulerPosition, OpenAsk
  EventApplier.cs      EventApplier
tests/Tabletop.Geometry.Tests/                 xUnit
tests/Tabletop.Content.Tests/
tests/Tabletop.Protocol.Tests/
```

`tests/` is a fourth top-level directory of the repo-level-support kind (neither shipped nor consumed), added to `CLAUDE.md` in Task 16. `docker-compose.yml` and `.env.example` sit at the root beside `Tabletop.sln`, the established place for repo-level files; the init SQL lives under `scripts/`, the existing home for dev tooling, which `CLAUDE.md` keeps out of `client/`.

---
### Task 1: Projects, solution and test scaffolding

**Files:**
- Create: `libs/Tabletop.Geometry/Tabletop.Geometry.csproj`, `libs/Tabletop.Content/Tabletop.Content.csproj`, `libs/Tabletop.Protocol/Tabletop.Protocol.csproj`
- Create: `tests/Tabletop.Geometry.Tests/Tabletop.Geometry.Tests.csproj`, `tests/Tabletop.Content.Tests/Tabletop.Content.Tests.csproj`, `tests/Tabletop.Protocol.Tests/Tabletop.Protocol.Tests.csproj`
- Create: `tests/Tabletop.Geometry.Tests/SmokeTests.cs`
- Modify: `Tabletop.sln`

**Interfaces:**
- Consumes: nothing — this is the first task. The repo already has `Tabletop.sln`, `global.json` (SDK `10.0.400`), `Directory.Build.props` (`obj/$(OS)/`) and a `.gitignore` that excludes `.env` and keeps `.env.example`; none of the four is rewritten.
- Produces: three assemblies `Tabletop.Geometry`, `Tabletop.Content` (→ Geometry), `Tabletop.Protocol` (→ Geometry, Content); `dotnet test` at the repo root runs all three test projects.

- [ ] **Step 1: Write the library csproj (same content for all three, changing `RootNamespace`)**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <RootNamespace>Tabletop.Geometry</RootNamespace>
  </PropertyGroup>
</Project>
```

`Tabletop.Content.csproj` adds `<ItemGroup><ProjectReference Include="../Tabletop.Geometry/Tabletop.Geometry.csproj" /></ItemGroup>`; `Tabletop.Protocol.csproj` adds both Geometry and `../Tabletop.Content/Tabletop.Content.csproj`.

- [ ] **Step 2: Write the test csproj (same for all three, changing the ProjectReference)**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>Tabletop.Geometry.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../libs/Tabletop.Geometry/Tabletop.Geometry.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

`Tabletop.Protocol.Tests` references Protocol only (Geometry and Content flow transitively).

- [ ] **Step 3: Write the failing smoke test**

```csharp
namespace Tabletop.Geometry.Tests;

public class SmokeTests
{
    [Fact]
    public void Assembly_loads() => Assert.Equal("Tabletop.Geometry", typeof(Point3).Assembly.GetName().Name);
}
```

- [ ] **Step 4: Add to the solution and run; expect a compile failure (`Point3` undefined)**

```bash
cd /home/cstupi/src/tabletop
dotnet sln Tabletop.sln add --solution-folder libs libs/Tabletop.Geometry libs/Tabletop.Content libs/Tabletop.Protocol
dotnet sln Tabletop.sln add --solution-folder tests tests/Tabletop.Geometry.Tests tests/Tabletop.Content.Tests tests/Tabletop.Protocol.Tests
dotnet test
```

Expected: restore succeeds (network needed once), build fails with `CS0246: The type or namespace name 'Point3' could not be found`.

`dotnet build` and `dotnet test` at the root build `Tabletop.sln`, which already lists `client/Tabletop.csproj`: `Godot.NET.Sdk/4.7.2` restores from NuGet and compiles headless in WSL without the editor, so the client stays in the solution and every plan's whole-tree check builds it. Nothing under `web/` is in the solution.

- [ ] **Step 5: Add the minimal type in `libs/Tabletop.Geometry/Points.cs`** (Task 2 finishes this file)

```csharp
namespace Tabletop.Geometry;

/// <summary>A point in world units (inches). Z is up.</summary>
public readonly record struct Point3(double X, double Y, double Z);
```

- [ ] **Step 6: Run `dotnet test`; expect 1 passed. Check `ls libs/Tabletop.Geometry/obj` shows only `Unix/`.**

- [ ] **Step 7: Commit**

```bash
git add Tabletop.sln libs tests
git commit -m "$(printf '%s\n' \
  'Add the three contract libraries and their test projects' \
  '' \
  'Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>' \
  'Claude-Session: <the session URL the environment gives this run>')"
```

**Every later task's commit takes the same two trailers** (Global Constraints); the tasks below print only the subject line.

### Task 2: Geometry value types

**Files:**
- Create: `libs/Tabletop.Geometry/Points.cs`, `Bases.cs`, `Volumes.cs`, `Placements.cs`
- Test: `tests/Tabletop.Geometry.Tests/ValueTests.cs`

**Interfaces:**
- Consumes: `Point3` (Task 1, finished here).
- Produces (namespace `Tabletop.Geometry`): `Point2`, `Point3`, `Yaw`, `Orientation`, `TableSize`, `Polygon`, `BaseShape`, `Base`, `ModelBody`, `FootprintKind`, `Footprint`, `VolumeKind`, `TerrainDeclarations`, `Volume`, `Placement`, `TerrainTransform`, `PlacedModel`, `WorldVolume` — full signatures in the summary tables at the end.

Spec: geometry values → Bases, Height/position/facing, The terrain transform, Collision volumes, Declarations, Asset scale and orientation. Dimensions are flat `(Shape, A, B)` to mirror `base_shape/base_a/base_b` and `footprint/dim_a/dim_b` in `schema.md`; symmetric shapes store the same value in A and B so `(A, B)` is always the footprint's extent.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Tabletop.Geometry.Tests;

public class ValueTests
{
    [Fact]
    public void Circle_base_stores_diameter_in_both_dimensions()
    {
        var b = Base.Circle(1.26);
        Assert.Equal(BaseShape.Circle, b.Shape);
        Assert.Equal(1.26, b.A);
        Assert.Equal(1.26, b.B);
    }

    [Fact]
    public void Oval_base_is_length_then_width()
    {
        var b = Base.Oval(2.36, 1.38);
        Assert.Equal((2.36, 1.38), (b.A, b.B));
    }

    [Fact]
    public void Rect_footprint_is_width_then_length_and_circle_is_radius()
    {
        Assert.Equal((FootprintKind.Rect, 4.0, 6.0), (Footprint.Rect(4, 6).Kind, Footprint.Rect(4, 6).A, Footprint.Rect(4, 6).B));
        Assert.Equal((FootprintKind.Circle, 3.0, 3.0), (Footprint.Circle(3).Kind, Footprint.Circle(3).A, Footprint.Circle(3).B));
    }

    [Fact]
    public void Declarations_combine_as_flags()
    {
        var forest = TerrainDeclarations.Cover | TerrainDeclarations.Difficult | TerrainDeclarations.SeeInAndOut;
        Assert.True(forest.HasFlag(TerrainDeclarations.Cover));
        Assert.False(forest.HasFlag(TerrainDeclarations.Impassable));
    }

    [Fact]
    public void Point3_horizontal_drops_z() => Assert.Equal(new Point2(1, 2), new Point3(1, 2, 3).Horizontal);

    [Fact]
    public void Volume_is_a_value()
    {
        Volume Hill() => new(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.None, null);
        Assert.Equal(Hill(), Hill());
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.Geometry.Tests`; expect compile errors for every missing type.**

- [ ] **Step 3: Implement — `Points.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Tabletop.Geometry;

/// <summary>A point on the table plane, in inches.</summary>
public readonly record struct Point2(double X, double Y);

/// <summary>A point in world units (inches). Z is up.</summary>
public readonly record struct Point3(double X, double Y, double Z)
{
    /// <summary>Derived, never serialised: System.Text.Json writes every get-only property, and a Point3 is on the wire everywhere.</summary>
    [JsonIgnore] public Point2 Horizontal => new(X, Y);
}

/// <summary>Facing about the vertical axis, in degrees. Models are yaw-only and always upright.</summary>
public readonly record struct Yaw(double Degrees);

/// <summary>
/// An asset's import rotation, in degrees, measured against the table's axes. The one convention (stated here; the web app's
/// converter writes it, the client's mesh loader applies it): the mesh's raw coordinates are read as table-frame coordinates and
/// rotated by Pitch about the table's X axis, then Roll about Y, then Yaw about Z — right-handed, extrinsic — and only then scaled.
/// A Y-up glTF stood upright in the Z-up table frame is Pitch = 90.
/// </summary>
public readonly record struct Orientation(double Pitch, double Yaw, double Roll);

/// <summary>The slab: width and length in inches. A rectangle, nothing more.</summary>
public readonly record struct TableSize(double Width, double Length);

/// <summary>A flat polygon on the table plane; deployment zones resolve to sets of these.</summary>
public sealed record Polygon(IReadOnlyList<Point2> Vertices)
{
    /// <summary>Point-in-polygon. Implemented by the geometry plan.</summary>
    public bool Contains(Point2 point) => throw new NotImplementedException("Implemented by the geometry plan.");
}
```

- [ ] **Step 4: Implement — `Bases.cs`**

```csharp
namespace Tabletop.Geometry;

public enum BaseShape { Circle, Oval, Square }

/// <summary>A base shape and its dimensions in inches. Circle: A = B = diameter. Oval: A = length, B = width. Square: A = B = side.</summary>
public readonly record struct Base(BaseShape Shape, double A, double B)
{
    public static Base Circle(double diameter) => new(BaseShape.Circle, diameter, diameter);
    public static Base Oval(double length, double width) => new(BaseShape.Oval, length, width);
    public static Base Square(double side) => new(BaseShape.Square, side, side);
}

/// <summary>What line of sight and distance run against: the base, extruded to the height.</summary>
public readonly record struct ModelBody(Base Base, double Height);
```

- [ ] **Step 5: Implement — `Volumes.cs`**

```csharp
namespace Tabletop.Geometry;

public enum FootprintKind { Circle, Rect }

/// <summary>Circle: A = B = radius. Rect: A = width (x), B = length (y).</summary>
public readonly record struct Footprint(FootprintKind Kind, double A, double B)
{
    public static Footprint Circle(double radius) => new(FootprintKind.Circle, radius, radius);
    public static Footprint Rect(double width, double length) => new(FootprintKind.Rect, width, length);
}

/// <summary>Solid: occupied, its top is what a model settles onto. Area: passed through, stood inside.</summary>
public enum VolumeKind { Solid, Area }

/// <summary>The six terrain declarations the engine reads. Everything else is <see cref="Volume.Notes"/>.</summary>
[Flags]
public enum TerrainDeclarations
{
    None = 0,
    Impassable = 1,
    BlocksLineOfSight = 2,
    SeeInAndOut = 4,
    Cover = 8,
    Difficult = 16,
    Dangerous = 32,
}

/// <summary>An extruded prism relative to its piece's origin. Bottom and Top are heights above the piece's base.</summary>
public sealed record Volume(
    VolumeKind Kind,
    Footprint Footprint,
    Point2 Offset,
    Yaw Yaw,
    double Bottom,
    double Top,
    TerrainDeclarations Declarations,
    string? Notes);
```

- [ ] **Step 6: Implement — `Placements.cs`**

```csharp
namespace Tabletop.Geometry;

/// <summary>Position and yaw. For a model, Z is the settled height the server computed.</summary>
public readonly record struct Placement(Point3 Position, Yaw Yaw);

/// <summary>A terrain piece's transform: position, yaw and a uniform scale.</summary>
public readonly record struct TerrainTransform(Point3 Position, Yaw Yaw, double Scale);

/// <summary>A model body standing somewhere.</summary>
public readonly record struct PlacedModel(ModelBody Body, Placement Placement);

/// <summary>A volume placed in the world. Id is the terrain_volume id the move Ask names as impassable.</summary>
public sealed record WorldVolume(Guid Id, Volume Volume, TerrainTransform Transform);
```

- [ ] **Step 7: Run `dotnet test tests/Tabletop.Geometry.Tests`; expect 7 passed. Commit.**

```bash
git add libs/Tabletop.Geometry tests/Tabletop.Geometry.Tests
git commit -m "Define the geometry value types"
```

### Task 3: Geometry API surface (declared, not implemented)

**Files:**
- Create: `libs/Tabletop.Geometry/Api.cs`
- Test: `tests/Tabletop.Geometry.Tests/ApiSurfaceTests.cs`

**Interfaces:**
- Consumes: `Point2`, `Point3`, `Base`, `ModelBody`, `Placement`, `PlacedModel`, `Volume`, `VolumeKind`, `TerrainDeclarations`, `WorldVolume`, `Polygon` (Task 2).
- Produces: `Distance.EdgeToEdge(PlacedModel, PlacedModel) : double`, `Distance.EdgeToEdge(PlacedModel, Point2) : double`, `LineOfSight.Check(PlacedModel from, PlacedModel to, IReadOnlyList<WorldVolume>) : LineOfSightResult`, `LineOfSightResult(bool Clear, IReadOnlyList<Guid> ObstructedBy)`, `Settling.SurfaceHeightAt(Point2, IReadOnlyList<WorldVolume>) : double`, `Containment.VolumesContaining(PlacedModel, IReadOnlyList<WorldVolume>) : IReadOnlyList<Guid>`, `Overlap.Bases(PlacedModel, PlacedModel) : bool`, `Overlap.WithSolid(PlacedModel, WorldVolume) : bool`, `Polygon.Contains(Point2) : bool`.
- **The geometry plan fills in every body in this file and `Polygon.Contains`; nothing else in `Tabletop.Geometry` is left to it.** The documented contracts below are the contract; the geometry plan may not change a signature or a contract without a spec correction.

Spec: `architecture.md` → Geometry (edge-to-edge, volumes not meshes, settle by raycasting down); geometry values → Declarations (blocking is declared; see-in-and-out modifies blocking), Collision volumes (solid tops are standable, area volumes are stood inside); table domain model → Containment is computed, never stored.

- [ ] **Step 1: Write the test that pins the surface (it passes once the file compiles; the geometry plan replaces it with behaviour tests)**

```csharp
namespace Tabletop.Geometry.Tests;

public class ApiSurfaceTests
{
    static readonly PlacedModel A = new(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(0, 0, 0), new Yaw(0)));
    static readonly PlacedModel B = new(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(6, 0, 0), new Yaw(0)));
    static readonly WorldVolume[] None = [];

    [Fact] public void Distance_is_declared() => Assert.Throws<NotImplementedException>(() => Distance.EdgeToEdge(A, B));
    [Fact] public void LineOfSight_is_declared() => Assert.Throws<NotImplementedException>(() => LineOfSight.Check(A, B, None));
    [Fact] public void Settling_is_declared() => Assert.Throws<NotImplementedException>(() => Settling.SurfaceHeightAt(new Point2(0, 0), None));
    [Fact] public void Containment_is_declared() => Assert.Throws<NotImplementedException>(() => Containment.VolumesContaining(A, None));
    [Fact] public void Overlap_is_declared() => Assert.Throws<NotImplementedException>(() => Overlap.Bases(A, B));
}
```

- [ ] **Step 2: Run; expect compile errors. Then write `Api.cs`:**

```csharp
namespace Tabletop.Geometry;

/// <summary>Edge-to-edge between bases, never centre-to-centre. Horizontal, in inches; 0 when the outlines overlap.</summary>
public static class Distance
{
    public static double EdgeToEdge(PlacedModel a, PlacedModel b) => throw new NotImplementedException("Implemented by the geometry plan.");
    public static double EdgeToEdge(PlacedModel a, Point2 point) => throw new NotImplementedException("Implemented by the geometry plan.");
}

/// <summary>Which volumes obstruct the line. Empty when clear.</summary>
public sealed record LineOfSightResult(bool Clear, IReadOnlyList<Guid> ObstructedBy);

/// <summary>
/// Line of sight between two base-and-height bodies against placed volumes. Only volumes declaring
/// BlocksLineOfSight obstruct; a SeeInAndOut volume obstructs a line passing through it but not one
/// that starts or ends inside it. The mesh is never consulted.
/// </summary>
public static class LineOfSight
{
    public static LineOfSightResult Check(PlacedModel from, PlacedModel to, IReadOnlyList<WorldVolume> volumes) =>
        throw new NotImplementedException("Implemented by the geometry plan.");
}

/// <summary>
/// Raycast down: the highest Solid top under the point, or 0 (the slab) when nothing is. The geometry plan adds a
/// <c>SurfaceHeightAt(Point3 from, …)</c> overload that drops from a height — the highest Solid top at or below it — so a model
/// dragged along the ground passes under a second storey; the server uses the client's <c>To.Z</c> as the drop height.
/// </summary>
public static class Settling
{
    public static double SurfaceHeightAt(Point2 point, IReadOnlyList<WorldVolume> volumes) =>
        throw new NotImplementedException("Implemented by the geometry plan.");
}

/// <summary>Ids of the volumes whose footprint and vertical range contain the model's base centre (Area or Solid).</summary>
public static class Containment
{
    public static IReadOnlyList<Guid> VolumesContaining(PlacedModel model, IReadOnlyList<WorldVolume> volumes) =>
        throw new NotImplementedException("Implemented by the geometry plan.");
}

/// <summary>Flagged, never prevented: base overlap and terrain intersection (architecture.md → Rules enforcement).</summary>
public static class Overlap
{
    public static bool Bases(PlacedModel a, PlacedModel b) => throw new NotImplementedException("Implemented by the geometry plan.");
    public static bool WithSolid(PlacedModel model, WorldVolume volume) => throw new NotImplementedException("Implemented by the geometry plan.");
}
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.Geometry.Tests`; expect 12 passed. Commit**

```bash
git add libs/Tabletop.Geometry tests/Tabletop.Geometry.Tests
git commit -m "Declare the geometry API the geometry plan implements"   # plus the two trailers, as Task 1
```

### Task 4: Content — stats and the army

**Files:**
- Create: `libs/Tabletop.Content/Stats.cs`, `libs/Tabletop.Content/Army.cs`
- Test: `tests/Tabletop.Content.Tests/StatValueTests.cs`, `tests/Tabletop.Content.Tests/ArmyTests.cs`

**Interfaces:**
- Consumes: `Base` (Task 2), for the slot's hitbox.
- Produces (namespace `Tabletop.Content`): `StatKind { TargetNumber, Distance, Count, DiceExpression }`; `StatValue(StatKind Kind, string Text)` with `static Read(StatKind, string)`, `int TargetNumber`, `double Inches`, `int Count`; `PropertyInstance(string Name, int? Parameter)`; `Weapon`, `ModelSlot`, `Unit`, `Army`, `ArmySnapshot` (+ `static ArmySnapshot.Of(Army, Guid id)`).
- Stats are `IReadOnlyDictionary<string, string>` — the text as written; `Properties` says how to read it (content values → Stats are typed; `schema.md` → Stats are rows).

Spec: content values (stats at three levels, weapon on the slot, absent range is absent, a snapshot is a deep copy with no edge back); the army (base and height per slot, asset a soft reference); identity and enumerations (`StatDefinition.kind`).

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Tabletop.Content.Tests;

public class StatValueTests
{
    [Theory]
    [InlineData("4+", 4)]
    [InlineData("2", 2)]
    public void Target_number_reads_the_score(string text, int expected) =>
        Assert.Equal(expected, StatValue.Read(StatKind.TargetNumber, text).TargetNumber);

    [Theory]
    [InlineData("24\"", 24.0)]
    [InlineData("6", 6.0)]
    [InlineData("1.5\"", 1.5)]
    public void Distance_reads_inches(string text, double expected) =>
        Assert.Equal(expected, StatValue.Read(StatKind.Distance, text).Inches);

    [Fact]
    public void Count_reads_an_integer() => Assert.Equal(3, StatValue.Read(StatKind.Count, "3").Count);

    [Fact]
    public void Dice_expression_is_kept_as_text() => Assert.Equal("D6", StatValue.Read(StatKind.DiceExpression, "D6").Text);

    [Fact]
    public void Wrong_format_is_a_FormatException() => Assert.Throws<FormatException>(() => StatValue.Read(StatKind.Count, "4+"));

    [Fact]
    public void Wrong_kind_accessor_is_an_InvalidOperationException() =>
        Assert.Throws<InvalidOperationException>(() => StatValue.Read(StatKind.Count, "3").Inches);
}
```

```csharp
using Tabletop.Geometry;

namespace Tabletop.Content.Tests;

public class ArmyTests
{
    public static Army Sample()
    {
        var rifle = new Weapon(Guid.CreateVersion7(), "Heavy Rifle",
            new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" },
            [new PropertyInstance("AP", 1)]);
        var slot = new ModelSlot(Guid.CreateVersion7(), 1, "Battle Brother",
            new Dictionary<string, string>(), [new PropertyInstance("Tough", 3)],
            Base.Circle(1.26), 1.5, null, [rifle]);
        var unit = new Unit(Guid.CreateVersion7(), 1, "Battle Brothers",
            new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "3+" },
            [new PropertyInstance("Fearless", null)], [slot]);
        return new Army(Guid.CreateVersion7(), "Strike Force", "grimdark-future", "https://army-forge.onepagerules.com/share?id=x", [unit]);
    }

    [Fact]
    public void Snapshot_copies_units_and_has_no_army_id()
    {
        var army = Sample();
        var id = Guid.CreateVersion7();
        var snapshot = ArmySnapshot.Of(army, id);
        Assert.Equal(id, snapshot.Id);
        Assert.Equal(army.Name, snapshot.Name);
        Assert.Equal(army.GameSystem, snapshot.GameSystem);
        Assert.Same(army.Units, snapshot.Units);
        Assert.DoesNotContain(typeof(ArmySnapshot).GetProperties(), p => p.Name is "ArmyId" or "Source");
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `Stats.cs`:**

```csharp
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Tabletop.Content;

/// <summary>The closed set a stat declaration carries (identity and enumerations → StatDefinition.kind).</summary>
public enum StatKind { TargetNumber, Distance, Count, DiceExpression }

/// <summary>A stat's text as written, read under the kind its declaration gives it.</summary>
public readonly partial record struct StatValue(StatKind Kind, string Text)
{
    [GeneratedRegex(@"^\s*(\d+)\s*\+?\s*$")] private static partial Regex TargetNumberPattern();
    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)\s*(?:""|”|in)?\s*$")] private static partial Regex DistancePattern();
    [GeneratedRegex(@"^\s*(\d+)\s*$")] private static partial Regex CountPattern();

    /// <summary>Validates <paramref name="text"/> against <paramref name="kind"/>. Dice expressions are kept as text; their grammar is undecided.</summary>
    public static StatValue Read(StatKind kind, string text)
    {
        var ok = kind switch
        {
            StatKind.TargetNumber => TargetNumberPattern().IsMatch(text),
            StatKind.Distance => DistancePattern().IsMatch(text),
            StatKind.Count => CountPattern().IsMatch(text),
            StatKind.DiceExpression => !string.IsNullOrWhiteSpace(text),
            _ => false,
        };
        return ok ? new StatValue(kind, text) : throw new FormatException($"'{text}' is not a {kind}.");
    }

    // All three are [JsonIgnore] because two of them throw for any given Kind, and System.Text.Json writes every get-only property.
    [JsonIgnore] public int TargetNumber => Kind == StatKind.TargetNumber
        ? int.Parse(TargetNumberPattern().Match(Text).Groups[1].Value, CultureInfo.InvariantCulture)
        : throw new InvalidOperationException($"{Kind} is not a target number.");

    [JsonIgnore] public double Inches => Kind == StatKind.Distance
        ? double.Parse(DistancePattern().Match(Text).Groups[1].Value, CultureInfo.InvariantCulture)
        : throw new InvalidOperationException($"{Kind} is not a distance.");

    [JsonIgnore] public int Count => Kind == StatKind.Count
        ? int.Parse(CountPattern().Match(Text).Groups[1].Value, CultureInfo.InvariantCulture)
        : throw new InvalidOperationException($"{Kind} is not a count.");
}
```

- [ ] **Step 3: Write `Army.cs`**

```csharp
using Tabletop.Geometry;

namespace Tabletop.Content;

/// <summary>A property entry on a unit, slot or weapon: a catalog name and its zero-or-one integer parameter.</summary>
public sealed record PropertyInstance(string Name, int? Parameter);

/// <summary>Lives on the model slot. A melee weapon has no Range stat at all.</summary>
public sealed record Weapon(Guid Id, string Name, IReadOnlyDictionary<string, string> Stats, IReadOnlyList<PropertyInstance> Properties);

/// <summary>One model. Base and Height are the hitbox; AssetId is appearance and a soft reference.</summary>
public sealed record ModelSlot(
    Guid Id, int Ordinal, string Name,
    IReadOnlyDictionary<string, string> Stats, IReadOnlyList<PropertyInstance> Properties,
    Base Base, double Height, Guid? AssetId,
    IReadOnlyList<Weapon> Weapons);

public sealed record Unit(
    Guid Id, int Ordinal, string Name,
    IReadOnlyDictionary<string, string> Stats, IReadOnlyList<PropertyInstance> Properties,
    IReadOnlyList<ModelSlot> Models);

/// <summary>GameSystem is the GameRules identifier; Source is where it was imported from.</summary>
public sealed record Army(Guid Id, string Name, string GameSystem, string Source, IReadOnlyList<Unit> Units);

/// <summary>
/// The army as it stood when setup ended: name, game system and the whole unit tree, with no edge back to the army and no Source.
/// Unit and slot ids are the ids in play. Every type below is an immutable record, so <c>Of</c> shares the list rather than cloning
/// it — the copy that matters is the serialised one, written once to <c>participant.army_snapshot</c> and never updated, which is
/// what stops an edit to a saved army reaching into a live match (content values → ARMYSNAPSHOT is a deep copy).
/// </summary>
public sealed record ArmySnapshot(Guid Id, string Name, string GameSystem, IReadOnlyList<Unit> Units)
{
    public static ArmySnapshot Of(Army army, Guid id) => new(id, army.Name, army.GameSystem, army.Units);
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Content.Tests`; expect 10 passed. Commit: `Define stats and the army snapshot types`.**

### Task 5: Content — the table state and Content JSON

**Files:**
- Create: `libs/Tabletop.Content/TableState.cs`, `libs/Tabletop.Content/ContentJson.cs`
- Test: `tests/Tabletop.Content.Tests/TableStateTests.cs`

**Interfaces:**
- Consumes: `Point3`, `Yaw`, `TableSize`, `Volume`, `TerrainTransform`, `WorldVolume` (Task 2); `Army` and `ArmyTests.Sample()` (Task 4) for the JSON round-trip test.
- Produces: `VisualRef(string? Builtin, Guid? AssetId)` + `OfBuiltin(string)`, `OfAsset(Guid)`, `bool IsBuiltin`; `TerrainVolume(Guid Id, Volume Volume)`; `TerrainPlacement(Guid Id, VisualRef Mesh, Point3 Position, Yaw Yaw, double Scale, IReadOnlyList<TerrainVolume> Volumes)` + `TerrainTransform Transform`, `IReadOnlyList<WorldVolume> WorldVolumes()`; `MarkerKind { Objective, Token }`; `Marker(Guid Id, MarkerKind Kind, Point3 Position, Yaw Yaw, double Diameter, string? Label, VisualRef Visual)`; `DeploymentZone(Guid Id, string Shape, int TeamOrdinal)`; `TableState(Guid Id, string? Name, TableSize Size, VisualRef Texture, IReadOnlyList<TerrainPlacement> Terrain, IReadOnlyList<Marker> Markers, IReadOnlyList<DeploymentZone> Zones)`; `ContentJson.Options`, `ContentJson.Serialize<T>(T)`, `ContentJson.Deserialize<T>(string)`.
- `TableState` is in Content, not Protocol, because management stores it as a layout, the game server writes it as the draft, and the client saves and loads it over HTTP (setup phase → Layouts; whole-design review ruling 4).
- A placement's `Volumes` is always populated — copied from the asset or the built-in on add — so the draft and snapshot are self-contained and the game server never resolves `asset_volume`. Spec correction 1 at the end records this.

Spec: setup content (the table is a slab; markers one record with a kind; zones a built-in shape plus a team ordinal); setup phase (built-in or asset, exactly one; volumes written on add); geometry values (marker size a diameter).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Geometry;

namespace Tabletop.Content.Tests;

public class TableStateTests
{
    public static TableState Sample()
    {
        var hill = new TerrainVolume(Guid.CreateVersion7(),
            new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.None, null));
        var placement = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill-small"), new Point3(12, 12, 0), new Yaw(45), 1.0, [hill]);
        var objective = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(36, 24, 0), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"));
        return new TableState(Guid.CreateVersion7(), "Sample", new TableSize(72, 48), VisualRef.OfBuiltin("grass"),
            [placement], [objective],
            [new DeploymentZone(Guid.CreateVersion7(), "frontline/1", 1), new DeploymentZone(Guid.CreateVersion7(), "frontline/2", 2)]);
    }

    [Fact]
    public void VisualRef_is_exactly_one_of_builtin_or_asset()
    {
        Assert.True(VisualRef.OfBuiltin("grass").IsBuiltin);
        Assert.False(VisualRef.OfAsset(Guid.CreateVersion7()).IsBuiltin);
        Assert.Throws<ArgumentException>(() => new VisualRef("grass", Guid.CreateVersion7()));
        Assert.Throws<ArgumentException>(() => new VisualRef(null, null));
    }

    [Fact]
    public void Placement_projects_its_volumes_into_the_world()
    {
        var placement = Sample().Terrain[0];
        var world = placement.WorldVolumes();
        Assert.Single(world);
        Assert.Equal(placement.Volumes[0].Id, world[0].Id);
        Assert.Equal(new TerrainTransform(placement.Position, placement.Yaw, placement.Scale), world[0].Transform);
    }

    [Fact]
    public void TableState_round_trips_through_json()
    {
        var json = ContentJson.Serialize(Sample());
        var back = ContentJson.Deserialize<TableState>(json);
        Assert.Equal(json, ContentJson.Serialize(back));
        Assert.Contains("\"kind\":\"solid\"", json);
        Assert.Contains("\"declarations\":\"none\"", json);
        // Derived properties stay off the wire: System.Text.Json writes every get-only property unless it is [JsonIgnore].
        foreach (var derived in new[] { "\"horizontal\"", "\"isBuiltin\"", "\"transform\"" }) Assert.DoesNotContain(derived, json);
    }

    [Fact]
    public void Terrain_declarations_are_a_flags_string()
    {
        // The [Flags] wire form the web app parses and formats: "none", one name, or names joined by ", " in ascending flag order.
        Assert.Equal("\"none\"", ContentJson.Serialize(TerrainDeclarations.None));
        Assert.Equal("\"impassable, cover\"", ContentJson.Serialize(TerrainDeclarations.Cover | TerrainDeclarations.Impassable));
        Assert.Equal(TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, ContentJson.Deserialize<TerrainDeclarations>("\"blocks_line_of_sight, cover\""));
    }

    [Fact]
    public void Army_round_trips_through_json()
    {
        var json = ContentJson.Serialize(ArmyTests.Sample());
        Assert.Equal(json, ContentJson.Serialize(ContentJson.Deserialize<Army>(json)));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `TableState.cs`:**

```csharp
using System.Text.Json.Serialization;
using Tabletop.Geometry;

namespace Tabletop.Content;

/// <summary>A built-in name or a library asset, exactly one (schema.md → Built-in or asset, exactly one).</summary>
public readonly record struct VisualRef
{
    public string? Builtin { get; }
    public Guid? AssetId { get; }

    [JsonConstructor]
    public VisualRef(string? builtin, Guid? assetId)
    {
        if ((builtin is null) == (assetId is null))
            throw new ArgumentException("Exactly one of builtin or assetId must be set.");
        Builtin = builtin;
        AssetId = assetId;
    }

    /// <summary>Derived, never serialised.</summary>
    [JsonIgnore] public bool IsBuiltin => Builtin is not null;
    public static VisualRef OfBuiltin(string name) => new(name, null);
    public static VisualRef OfAsset(Guid assetId) => new(null, assetId);
}

/// <summary>A volume row on a placement. Always present: copied from the asset or built-in on add, editable in place.</summary>
public sealed record TerrainVolume(Guid Id, Volume Volume);

public sealed record TerrainPlacement(Guid Id, VisualRef Mesh, Point3 Position, Yaw Yaw, double Scale, IReadOnlyList<TerrainVolume> Volumes)
{
    /// <summary>Derived, never serialised.</summary>
    [JsonIgnore] public TerrainTransform Transform => new(Position, Yaw, Scale);
    public IReadOnlyList<WorldVolume> WorldVolumes() => Volumes.Select(v => new WorldVolume(v.Id, v.Volume, Transform)).ToList();
}

public enum MarkerKind { Objective, Token }

/// <summary>Objectives and tokens are one record. Markers ignore terrain.</summary>
public sealed record Marker(Guid Id, MarkerKind Kind, Point3 Position, Yaw Yaw, double Diameter, string? Label, VisualRef Visual);

/// <summary>A built-in zone shape and the team ordinal it belongs to. Shape ids are namespaced by style, e.g. "frontline/1" — the ids Tabletop.Geometry.ZoneStyles.ShapesOf(style) lists (geometry plan Task 13); one row per shape, keyed by team ordinal 1..n.</summary>
public sealed record DeploymentZone(Guid Id, string Shape, int TeamOrdinal);

/// <summary>The setup phase's draft and result, and a saved layout: one shape, two owners.</summary>
public sealed record TableState(
    Guid Id, string? Name, TableSize Size, VisualRef Texture,
    IReadOnlyList<TerrainPlacement> Terrain, IReadOnlyList<Marker> Markers, IReadOnlyList<DeploymentZone> Zones);
```

- [ ] **Step 3: Write `ContentJson.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Content;

/// <summary>The JSON conventions for Geometry and Content types. Protocol builds its options on these.</summary>
public static class ContentJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions());

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.AllowOutOfOrderMetadataProperties = true;
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("null");
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Content.Tests`; expect 15 passed. Commit: `Define the table state and the content JSON conventions`.**

### Task 6: Protocol — version, game info, in-play values

**Files:**
- Create: `libs/Tabletop.Protocol/ProtocolVersion.cs`, `Game.cs`, `State.cs`
- Test: `tests/Tabletop.Protocol.Tests/GameTests.cs`

**Interfaces:**
- Consumes: `Point3`, `Yaw`, `Placement` (Task 2); `ArmySnapshot` (Task 4); `TableState`, `ContentJson` (Task 5).
- Produces (namespace `Tabletop.Protocol`): `ProtocolVersion.Current = 1`, `ProtocolVersion.Header = "Tabletop-Client-Version"`, `ProtocolVersion.UpgradeRequiredStatusCode = 426`; `GameStatus { Lobby, Setup, Playing, Ended }`; `SchedulerShape { PlayerTurn, AlternatingActivation }` (wire names `player-turn`, `alternating-activation`); `Enforcement { Silent, Warn, Strict }`; `Team(Guid Id, int Ordinal)`; `Participant(Guid Id, Guid AccountId, string Name, Guid? TeamId, Guid? ArmyId)`; `GameInfo(Guid Id, string Name, GameStatus Status, Guid CreatedBy, string GameRules, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps, long RngSeed, IReadOnlyList<Team> Teams, IReadOnlyList<Participant> Participants)` (`RngSeed` is positional and last among the scalars; wire name `rngSeed`); `ArmyInPlay(Guid ParticipantId, ArmySnapshot Snapshot)`; `Board(TableState Table, IReadOnlyList<ArmyInPlay> Armies)`; `Location` union (`OnTable(Placement)`, `OnShelf`, `Inside(Guid ContainerId)`); `ModelRelocation(Guid ModelId, Location From, Location To)`; `SubjectKind { Unit, Model }`; `Subject(SubjectKind Kind, Guid Id)`; `Grant(Guid Id, Guid EffectId, string EntryName, Guid CasterUnitId, int RemainingUses)`; `Die(int Raw, int Modified, bool Success)`; `RollModifier(string Source, int Amount)`; `RollRecord(Guid Id, string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, IReadOnlyList<Die> Dice)`; `SequenceContext(Guid? ActorUnitId, IReadOnlyList<Guid> TargetUnitIds, IReadOnlyList<Guid> WeaponIds, string? Action)`.

Spec: management API → The version check (`426`); identity and enumerations → The closed sets; game flow → The shelf, Inside a container (a model is always in exactly one of three places); action sequences → One roll primitive (raw and modified per die, threshold and modifiers carried; null threshold for the roll-off, scheduler → Deployment); spell effects → The grants store; whole-design review ruling 5 (nullable team). `Participant.ArmyId` is spec correction 2; `GameInfo.RngSeed` is owner ruling 1 of 2026-09-06 (the seed is stored, not derived from the game id) and spec correction 12.

- [ ] **Step 1: Write the failing test**

```csharp
using Tabletop.Content;

namespace Tabletop.Protocol.Tests;

public class GameTests
{
    [Fact]
    public void Scheduler_shape_uses_the_hyphenated_wire_names()
    {
        var json = ContentJson.Serialize(SchedulerShape.AlternatingActivation);
        Assert.Equal("\"alternating-activation\"", json);
        Assert.Equal(SchedulerShape.PlayerTurn, ContentJson.Deserialize<SchedulerShape>("\"player-turn\""));
    }

    [Fact]
    public void Location_is_one_of_three_places()
    {
        Location[] all = [new OnTable(new(new(1, 2, 0), new(90))), new OnShelf(), new Inside(Guid.CreateVersion7())];
        Assert.Equal(3, all.Select(l => l.GetType()).Distinct().Count());
    }

    [Fact]
    public void Game_info_carries_the_stored_rng_seed_whole()
    {
        // long.MinValue + 1: signed, and past 2^53, so a JSON number that went through a double would come back changed.
        var game = new GameInfo(Guid.CreateVersion7(), "Friday", GameStatus.Lobby, Guid.CreateVersion7(),
            "grimdark-future", "sha256:abc", SchedulerShape.PlayerTurn, Enforcement.Warn, false, -9223372036854775807L, [], []);
        var json = ContentJson.Serialize(game);
        Assert.Contains("\"rngSeed\":-9223372036854775807", json);
        Assert.Equal(game.RngSeed, ContentJson.Deserialize<GameInfo>(json).RngSeed);
    }

    [Fact]
    public void Version_check_constants() => Assert.Equal((1, 426), (ProtocolVersion.Current, ProtocolVersion.UpgradeRequiredStatusCode));
}
```

- [ ] **Step 2: Run; expect compile errors. Write `ProtocolVersion.cs`:**

```csharp
namespace Tabletop.Protocol;

/// <summary>Sent on every HTTP request and in Connect; management answers 426 when below its minimum.</summary>
public static class ProtocolVersion
{
    public const int Current = 1;
    public const string Header = "Tabletop-Client-Version";
    public const int UpgradeRequiredStatusCode = 426;
}
```

- [ ] **Step 3: Write `Game.cs`**

```csharp
using System.Text.Json.Serialization;
using Tabletop.Content;

namespace Tabletop.Protocol;

public enum GameStatus { Lobby, Setup, Playing, Ended }

public enum SchedulerShape
{
    [JsonStringEnumMemberName("player-turn")] PlayerTurn,
    [JsonStringEnumMemberName("alternating-activation")] AlternatingActivation,
}

public enum Enforcement { Silent, Warn, Strict }

public sealed record Team(Guid Id, int Ordinal);

/// <summary>TeamId is null until the participant picks one; a participant with no team is never Asked. ArmyId is the lobby pick, null after deployment.</summary>
public sealed record Participant(Guid Id, Guid AccountId, string Name, Guid? TeamId, Guid? ArmyId);

/// <summary>
/// Teams, participants and configuration: what a lobby connect receives.
/// RngSeed is the game's stored dice seed, generated once when the game row is created and never changed —
/// which is what makes undo and replay exact. It is not derived from Id (owner ruling 1, 2026-09-06).
/// </summary>
public sealed record GameInfo(
    Guid Id, string Name, GameStatus Status, Guid CreatedBy,
    string GameRules, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps,
    long RngSeed,
    IReadOnlyList<Team> Teams, IReadOnlyList<Participant> Participants);

public sealed record ArmyInPlay(Guid ParticipantId, ArmySnapshot Snapshot);

/// <summary>The draft during setup (Armies empty) or the log's initial state from playing on.</summary>
public sealed record Board(TableState Table, IReadOnlyList<ArmyInPlay> Armies);
```

- [ ] **Step 4: Write `State.cs`**

```csharp
using System.Text.Json.Serialization;
using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>Where a model is: on the table, on its owner's shelf, or inside a container (a model or terrain piece).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OnTable), "table")]
[JsonDerivedType(typeof(OnShelf), "shelf")]
[JsonDerivedType(typeof(Inside), "inside")]
public abstract record Location;
public sealed record OnTable(Placement Placement) : Location;
public sealed record OnShelf : Location;
public sealed record Inside(Guid ContainerId) : Location;

/// <summary>One model changing place. Deployment, a hand move, a casualty and disembarking are all this.</summary>
public sealed record ModelRelocation(Guid ModelId, Location From, Location To);

public enum SubjectKind { Unit, Model }
public sealed record Subject(SubjectKind Kind, Guid Id);

/// <summary>A once-effect written onto a unit; consumed the first time it applies.</summary>
public sealed record Grant(Guid Id, Guid EffectId, string EntryName, Guid CasterUnitId, int RemainingUses);

public sealed record Die(int Raw, int Modified, bool Success);
public sealed record RollModifier(string Source, int Amount);

/// <summary>A roll as it happened. Threshold is null for the roll-off. Replay applies this; it never re-rolls.</summary>
public sealed record RollRecord(Guid Id, string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, IReadOnlyList<Die> Dice);

/// <summary>What a sequence was invoked with. Action is the action row's name when the sequence is an action.</summary>
public sealed record SequenceContext(Guid? ActorUnitId, IReadOnlyList<Guid> TargetUnitIds, IReadOnlyList<Guid> WeaponIds, string? Action);
```

- [ ] **Step 5: Run `dotnet test tests/Tabletop.Protocol.Tests`; expect 4 passed. Commit: `Define the protocol's game info and in-play values`.**

### Task 7: Protocol — the StateChange union

**Files:**
- Create: `libs/Tabletop.Protocol/StateChanges.cs`, `libs/Tabletop.Protocol/Events.cs` (the one-line `EventPayload` placeholder of Step 2; Task 9 replaces the file)
- Test: `tests/Tabletop.Protocol.Tests/StateChangeTests.cs`

**Interfaces:**
- Consumes: `Marker` (Task 5); `ModelRelocation`, `Subject`, `SubjectKind`, `Grant` (Task 6); `Point3`, `Yaw` (Task 2). Declares the `EventPayload` placeholder Task 9 replaces.
- Produces: `abstract record StateChange` and its closed set, each a sealed record with the wire kind in brackets: `ModelsMoved(IReadOnlyList<ModelRelocation> Moves)` [models_moved], `WoundsSet(Guid ModelId, int Wounds)` [wounds_set], `ConditionSet(Subject Subject, string Condition, bool Present)` [condition_set], `ActionRecorded(Guid UnitId, string Action)` [action_recorded], `ActionRecordCleared(Guid UnitId)` [action_record_cleared], `GrantAdded(Guid UnitId, Grant Grant)` [grant_added], `GrantRemoved(Guid UnitId, Guid GrantId)` [grant_removed], `SpellTokensSet(Guid ModelId, int Tokens)` [spell_tokens_set], `WoundsThisActivationSet(Guid UnitId, int Wounds)` [wounds_this_activation_set], `ScoreSet(Guid TeamId, int Score)` [score_set], `MarkerPlaced(Marker Marker)` [marker_placed], `MarkerMoved(Guid MarkerId, Point3 Position, Yaw Yaw)` [marker_moved], `MarkerRemoved(Guid MarkerId)` [marker_removed].
- A `StateChange` is both an event payload on its own (a menu edit or a hand move) and an element of the `Changes` list that `step_completed`, `round_started` and `turn_ended` carry. That is what lets one `Apply(change)` serve automation and the menu — game flow → Nothing is an invariant ("the same fields through the same path").

Spec: game flow (action record, conditions, placement, the shelf, containment, scoring); spell effects (grants written and consumed explicitly in the payload); action sequences (wounds-this-activation, spell tokens as editable counters); setup content (`marker_placed/moved/removed` for tokens dropped in play); table domain model (logged moves carry start and stop).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using System.Text.Json.Serialization;

namespace Tabletop.Protocol.Tests;

public class StateChangeTests
{
    public static IEnumerable<Type> ConcreteSubclasses(Type baseType) =>
        baseType.Assembly.GetTypes().Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract);

    public static IReadOnlyDictionary<Type, string> Registered(Type baseType) =>
        baseType.GetCustomAttributes<JsonDerivedTypeAttribute>().ToDictionary(a => a.DerivedType, a => (string)a.TypeDiscriminator!);

    [Fact]
    public void Every_state_change_is_registered_with_a_unique_snake_case_kind()
    {
        var registered = Registered(typeof(StateChange));
        foreach (var t in ConcreteSubclasses(typeof(StateChange)))
            Assert.True(registered.ContainsKey(t), $"{t.Name} is not a [JsonDerivedType] of StateChange");
        Assert.Equal(registered.Count, registered.Values.Distinct().Count());
        Assert.All(registered.Values, k => Assert.Matches("^[a-z][a-z0-9_]*$", k));
    }
}
```

- [ ] **Step 2: Run; expect `StateChange` undefined. Write `StateChanges.cs`:**

```csharp
using System.Text.Json.Serialization;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>
/// One change to in-play state, stated in full. Applying one is copying. A menu edit logs one as an event;
/// a step, a round boundary or a turn end logs a list of them inside its own event.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ModelsMoved), "models_moved")]
[JsonDerivedType(typeof(WoundsSet), "wounds_set")]
[JsonDerivedType(typeof(ConditionSet), "condition_set")]
[JsonDerivedType(typeof(ActionRecorded), "action_recorded")]
[JsonDerivedType(typeof(ActionRecordCleared), "action_record_cleared")]
[JsonDerivedType(typeof(GrantAdded), "grant_added")]
[JsonDerivedType(typeof(GrantRemoved), "grant_removed")]
[JsonDerivedType(typeof(SpellTokensSet), "spell_tokens_set")]
[JsonDerivedType(typeof(WoundsThisActivationSet), "wounds_this_activation_set")]
[JsonDerivedType(typeof(ScoreSet), "score_set")]
[JsonDerivedType(typeof(MarkerPlaced), "marker_placed")]
[JsonDerivedType(typeof(MarkerMoved), "marker_moved")]
[JsonDerivedType(typeof(MarkerRemoved), "marker_removed")]
public abstract record StateChange : EventPayload;

/// <summary>Start and stop for each model. Deployment, hand moves, casualties, embarking: all this one kind.</summary>
public sealed record ModelsMoved(IReadOnlyList<ModelRelocation> Moves) : StateChange;
public sealed record WoundsSet(Guid ModelId, int Wounds) : StateChange;
public sealed record ConditionSet(Subject Subject, string Condition, bool Present) : StateChange;
public sealed record ActionRecorded(Guid UnitId, string Action) : StateChange;
public sealed record ActionRecordCleared(Guid UnitId) : StateChange;
public sealed record GrantAdded(Guid UnitId, Grant Grant) : StateChange;
public sealed record GrantRemoved(Guid UnitId, Guid GrantId) : StateChange;
public sealed record SpellTokensSet(Guid ModelId, int Tokens) : StateChange;
public sealed record WoundsThisActivationSet(Guid UnitId, int Wounds) : StateChange;
public sealed record ScoreSet(Guid TeamId, int Score) : StateChange;
/// <summary>A token dropped in play. Setup markers are rows in the snapshot, not this.</summary>
public sealed record MarkerPlaced(Marker Marker) : StateChange;
public sealed record MarkerMoved(Guid MarkerId, Point3 Position, Yaw Yaw) : StateChange;
public sealed record MarkerRemoved(Guid MarkerId) : StateChange;
```

`EventPayload` does not exist yet; add a placeholder `public abstract record EventPayload;` at the top of a new `Events.cs` — Task 9 replaces it.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.Protocol.Tests`; expect 5 passed. Commit: `Define the closed set of in-play state changes`.**

### Task 8: Protocol — Asks

**Files:**
- Create: `libs/Tabletop.Protocol/Asks.cs`
- Test: `tests/Tabletop.Protocol.Tests/AskTests.cs`

**Interfaces:**
- Consumes: `ModelRelocation` (Task 6).
- Produces: `Ask(Guid Id, string Question, string Prompt, IReadOnlyList<Guid> Teams, LegalSet Legal, bool AllowPass, bool Sealed, string? WindowKind, Guid? FrameId)`; `AskOption(string Id, string Label, IReadOnlyList<Guid> Pieces)`; `abstract record LegalSet` with `Options(IReadOnlyList<AskOption> Choices)` [options], `PieceSelection(IReadOnlyList<Guid> Eligible, int Min, int Max)` [pieces], `Allocation(IReadOnlyList<Guid> Targets, int Amount)` [allocation], `Assignment(IReadOnlyList<AskOption> Items, IReadOnlyList<Guid> Targets, int MaxDistinctTargets)` [assignment], `MoveRegion(IReadOnlyList<Guid> ModelIds, double Allowance, IReadOnlyList<Guid> ImpassableVolumeIds, IReadOnlyList<Guid> DifficultVolumeIds, double? DifficultCap, MoveContainment? Containment = null)` [move_region], `Bid(int MaxTokens)` [bid], `Confirmation` [confirmation]; `MoveContainment(IReadOnlyList<Guid> Enterable, Guid? ExitingId, double? ExitWithin)`; `abstract record AskAnswer` with `Chosen(string OptionId)` [chosen], `SelectedPieces(IReadOnlyList<Guid> PieceIds)` [selected_pieces], `Allocated(IReadOnlyDictionary<Guid, int> Counts)` [allocated], `Assigned(IReadOnlyDictionary<string, Guid> Targets)` [assigned], `Destinations(IReadOnlyList<ModelRelocation> Moves)` [destinations], `BidPlaced(int Tokens)` [bid_placed], `Confirmed` [confirmed], `Passed` [passed]; `static class AskQuestions` with `Activate = "activate"`, `ActOrEndPhase = "act_or_end_phase"`, `Deploy = "deploy"`, `DeployScouts = "deploy_scouts"`, `DeployAmbusher = "deploy_ambusher"`, `EndRound = "end_round"`, `Confirm = "confirm"`, `React = "react"`, `Bid = "bid"`.
- The **legal set's shape** is the closed set the client renders from; the **question** is a name: the engine's own in `AskQuestions`, a ruleset's (e.g. `shoot_or_not`, `strike_back`, `remove_casualties`) supplied by `GameRules` the way action names are. Action sequences constraint 9 (who answers differs per ruleset) is why the question is not an enum.
- The move Ask carries the allowance and the impassable volume ids (`architecture.md` → Continuous actions); its answer is destinations, whose `To` positions carry the client's settled Z and are overwritten by the server's event. It also carries **what containment allows**, because `Location.Inside` and `ModelRelocation` already put an `Inside` destination on the wire but nothing told the client where one is legal: `MoveRegion.Containment` is non-null exactly when this move may embark or is disembarking (game flow → Inside a container; rulebook p.14 `Transport(X)`, p.20 solid buildings). `Enterable` are the containers a model reaching one embarks the whole unit into — *"only one model needs to reach the Transport for everyone to embark"* — so the answer is a `Destinations` whose `To` is `Inside(thatContainer)` for every model, not one relocation per reachable model. `ExitingId` and `ExitWithin` are the container the unit is inside and the distance its models must be placed **fully** within of it (OPR: 6"), which is `Tabletop.Geometry.Containment.BaseFullyWithin`; the client draws that region instead of `Movement.Region`, since an occupant has no position to move from, and `Allowance` is then the action's remaining distance and binds nothing. Enterable and exiting are never both set, because a unit may not embark and disembark in one activation. `Containment` is optional so that a move with no container in play — every move on open table, and the charge close-in, fall-back and consolidation Asks — is constructed exactly as before.

Spec: action sequences → Asks, Windows (offered, invoked, sealed; pass is a real answer); scheduler → The turn is an Ask (options = unit × action), Deployment, The round boundary (Ambushers), player-turn phases; client shell → Asks and windows (the panel's seven renderings, muting by window kind, sealed bids, confirm gates); spell effects → Target selection (one to `target_count` targets); game flow → Inside a container (enter or exit with any move action, never both in one activation, exit within 6").

- [ ] **Step 1: Write the failing test**

```csharp
namespace Tabletop.Protocol.Tests;

public class AskTests
{
    public static Ask Activate(Guid team, Guid unit) => new(
        Guid.CreateVersion7(), AskQuestions.Activate, "Activate a unit", [team],
        new Options([new AskOption($"{unit}:Advance", "Advance", [unit]), new AskOption($"{unit}:Hold", "Hold", [unit])]),
        AllowPass: true, Sealed: false, WindowKind: null, FrameId: null);

    [Fact]
    public void Every_legal_set_and_answer_kind_is_registered()
    {
        foreach (var baseType in new[] { typeof(LegalSet), typeof(AskAnswer) })
        {
            var registered = StateChangeTests.Registered(baseType);
            foreach (var t in StateChangeTests.ConcreteSubclasses(baseType))
                Assert.True(registered.ContainsKey(t), $"{t.Name} missing on {baseType.Name}");
            Assert.Equal(registered.Count, registered.Values.Distinct().Count());
        }
    }

    [Fact]
    public void A_sealed_bid_is_addressed_to_several_teams()
    {
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.Bid, "Bid tokens", [Guid.CreateVersion7(), Guid.CreateVersion7()],
            new Bid(3), AllowPass: true, Sealed: true, WindowKind: "casting", FrameId: Guid.CreateVersion7());
        Assert.Equal(2, ask.Teams.Count);
        Assert.True(ask.Sealed);
    }

    [Fact]
    public void A_move_says_nothing_about_containers_unless_one_is_in_play()
    {
        var model = Guid.CreateVersion7(); var bus = Guid.CreateVersion7();
        Assert.Null(new MoveRegion([model], 6, [], [], 6).Containment);                                     // the ordinary move: five arguments, as before
        var embark = new MoveRegion([model], 6, [], [], 6, new MoveContainment([bus], null, null));
        Assert.Equal([bus], embark.Containment!.Enterable);
        Assert.Null(embark.Containment.ExitingId);
        var disembark = new MoveRegion([model], 6, [], [], 6, new MoveContainment([], bus, 6));
        Assert.Equal((bus, 6.0), (disembark.Containment!.ExitingId!.Value, disembark.Containment.ExitWithin!.Value));
        Assert.Empty(disembark.Containment.Enterable);                                                      // never both in one activation
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `Asks.cs`:**

```csharp
using System.Text.Json.Serialization;

namespace Tabletop.Protocol;

/// <summary>
/// A question addressed to one team (several for a sealed window), with the server-computed legal set.
/// Any team member may answer; the log records who. FrameId is null for the scheduler's Asks.
/// WindowKind is the name a player mutes by; null when the Ask is not a window.
/// </summary>
public sealed record Ask(
    Guid Id, string Question, string Prompt, IReadOnlyList<Guid> Teams, LegalSet Legal,
    bool AllowPass, bool Sealed, string? WindowKind, Guid? FrameId);

/// <summary>A discrete choice. Pieces are the ids to highlight (for activate: the unit).</summary>
public sealed record AskOption(string Id, string Label, IReadOnlyList<Guid> Pieces);

/// <summary>The engine-owned questions. Ruleset questions are GameRules' own names.</summary>
public static class AskQuestions
{
    public const string Activate = "activate";
    public const string ActOrEndPhase = "act_or_end_phase";
    public const string Deploy = "deploy";
    public const string DeployScouts = "deploy_scouts";
    public const string DeployAmbusher = "deploy_ambusher";
    /// <summary>The round boundary: an unframed Confirmation Ask to every team, whose first answer starts the next round.</summary>
    public const string EndRound = "end_round";
    public const string Confirm = "confirm";
    public const string React = "react";
    public const string Bid = "bid";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Options), "options")]
[JsonDerivedType(typeof(PieceSelection), "pieces")]
[JsonDerivedType(typeof(Allocation), "allocation")]
[JsonDerivedType(typeof(Assignment), "assignment")]
[JsonDerivedType(typeof(MoveRegion), "move_region")]
[JsonDerivedType(typeof(Bid), "bid")]
[JsonDerivedType(typeof(Confirmation), "confirmation")]
public abstract record LegalSet;
public sealed record Options(IReadOnlyList<AskOption> Choices) : LegalSet;
public sealed record PieceSelection(IReadOnlyList<Guid> Eligible, int Min, int Max) : LegalSet;
/// <summary>Distribute Amount over Targets (wound assignment, casualty removal).</summary>
public sealed record Allocation(IReadOnlyList<Guid> Targets, int Amount) : LegalSet;
/// <summary>Give each item one target (weapon groups to up to N targets).</summary>
public sealed record Assignment(IReadOnlyList<AskOption> Items, IReadOnlyList<Guid> Targets, int MaxDistinctTargets) : LegalSet;
/// <summary>
/// Anywhere within the allowance: parameters, not an enumeration. The client draws the region with Tabletop.Geometry.Movement.Region.
/// ImpassableVolumeIds and DifficultVolumeIds name volumes on the Board (TerrainPlacement.WorldVolumes()); DifficultCap is the
/// whole-move cap GameRules computed when any listed difficult volume is entered (OPR: 6"), null when the ruleset has none.
/// Containment is non-null only when this move may embark or is disembarking; null is every move on open table.
/// </summary>
public sealed record MoveRegion(IReadOnlyList<Guid> ModelIds, double Allowance, IReadOnlyList<Guid> ImpassableVolumeIds,
    IReadOnlyList<Guid> DifficultVolumeIds, double? DifficultCap, MoveContainment? Containment = null) : LegalSet;
/// <summary>
/// What containment allows on one move (rulebook p.14 Transport(X), p.20 solid buildings). Enterable are the containers a
/// model reaching one embarks the whole unit into, so the answer is Inside(container) for every model of the unit. ExitingId
/// is the container the unit is inside and ExitWithin the distance its models must be placed fully within of it (OPR: 6"),
/// which is Tabletop.Geometry.Containment.BaseFullyWithin; the client draws that region rather than Movement.Region, because
/// an occupant has no position to move from. Never both — a unit may not embark and disembark in one activation.
/// </summary>
public sealed record MoveContainment(IReadOnlyList<Guid> Enterable, Guid? ExitingId, double? ExitWithin);
public sealed record Bid(int MaxTokens) : LegalSet;
public sealed record Confirmation : LegalSet;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Chosen), "chosen")]
[JsonDerivedType(typeof(SelectedPieces), "selected_pieces")]
[JsonDerivedType(typeof(Allocated), "allocated")]
[JsonDerivedType(typeof(Assigned), "assigned")]
[JsonDerivedType(typeof(Destinations), "destinations")]
[JsonDerivedType(typeof(BidPlaced), "bid_placed")]
[JsonDerivedType(typeof(Confirmed), "confirmed")]
[JsonDerivedType(typeof(Passed), "passed")]
public abstract record AskAnswer;
public sealed record Chosen(string OptionId) : AskAnswer;
public sealed record SelectedPieces(IReadOnlyList<Guid> PieceIds) : AskAnswer;
public sealed record Allocated(IReadOnlyDictionary<Guid, int> Counts) : AskAnswer;
public sealed record Assigned(IReadOnlyDictionary<string, Guid> Targets) : AskAnswer;
public sealed record Destinations(IReadOnlyList<ModelRelocation> Moves) : AskAnswer;
public sealed record BidPlaced(int Tokens) : AskAnswer;
public sealed record Confirmed : AskAnswer;
/// <summary>A real answer, logged; only legal when the Ask allows it.</summary>
public sealed record Passed : AskAnswer;
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.Protocol.Tests`; expect 8 passed. Commit: `Define Asks, legal sets and answers`.**

### Task 9: Protocol — events

**Files:**
- Modify: `libs/Tabletop.Protocol/Events.cs` (replaces the Task 7 placeholder)
- Test: `tests/Tabletop.Protocol.Tests/EventTests.cs`

**Interfaces:**
- Consumes: every `StateChange` (Task 7); `SequenceContext`, `RollRecord`, `RollModifier`, `Die` (Task 6); `Ask`, `AskAnswer` (Task 8).
- Produces: `abstract record EventPayload` whose concrete kinds are every `StateChange` (Task 7) plus: `RoundStarted(int Round, IReadOnlyList<Guid> Order, IReadOnlyList<StateChange> Changes)` [round_started], `TurnStarted(Guid TeamId)` [turn_started], `TurnEnded(Guid TeamId, bool Passed, IReadOnlyList<StateChange> Changes)` [turn_ended], `PhaseStarted(string Phase)` [phase_started], `SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context)` [sequence_started], `StepCompleted(Guid FrameId, string Step, AskAnswer? Answer, IReadOnlyList<RollRecord> Rolls, IReadOnlyList<StateChange> Changes) { IReadOnlyDictionary<string, int>? Values { get; init; } }` [step_completed] (`Values` is a non-positional init property, wire name `values`, omitted when null), `SequenceFinished(Guid FrameId)` [sequence_finished], `SequenceAbandoned(Guid FrameId)` [sequence_abandoned], `AskOpened(Ask Ask)` [ask_opened], `AskAnswered(Guid AskId, Guid TeamId, AskAnswer? Answer, bool OutsideLegalSet)` [ask_answered], `AskClosed(Guid AskId, IReadOnlyList<TeamAnswer> Answers)` [ask_closed], `TeamAnswer(Guid TeamId, AskAnswer Answer)`, `DiceRerolled(Guid FrameId, Guid RollId, IReadOnlyList<DieReplacement> Replaced)` [dice_rerolled], `DiceEdited(Guid FrameId, Guid RollId, IReadOnlyList<DieReplacement> Replaced)` [dice_edited], `DieReplacement(int Index, Die Old, Die New)`, `ModifierAdded(Guid FrameId, string Roll, RollModifier Modifier)` [modifier_added], `Undone(long ToSeq)` [undone]; `GameEvent(long Seq, Guid? Actor, EventPayload Payload, string? Warning)` with `string Kind`; `EventKinds.Of(Type)`, `EventKinds.Of(EventPayload)`, `EventKinds.All`.
- `GameEvent.Actor` is the participant who caused it, null when the engine did. `Warning` is set under `warn` enforcement when the command was outside the legal set (whole-design review ruling 1). `Kind` is what the game server writes to `game_event.kind`.
- `ask_answered.Answer` is null on the stream to teams a sealed answer is withheld from; `ask_closed` reveals all (action sequences → Windows; `architecture.md` → per-player filtered streams). Asks are events because the projection holds "the current Asks" and is rebuilt from the log — spec correction 3.

Spec: scheduler → Events (four kinds; `turn_ended` carrying `passed`), The round boundary (hooks' changes ride on `round_started`); action sequences → The resolution stack (four kinds; `step_completed` records everything it produced), Abandonment; resolution → Players roll their own dice (reroll records which dice it replaced; a hand-added modifier has a pack modifier's shape); `architecture.md` → Undo (replay to N).

- [ ] **Step 1: Write the failing test**

```csharp
namespace Tabletop.Protocol.Tests;

public class EventTests
{
    [Fact]
    public void Every_payload_is_registered_once_including_state_changes()
    {
        var registered = StateChangeTests.Registered(typeof(EventPayload));
        foreach (var t in StateChangeTests.ConcreteSubclasses(typeof(EventPayload)))
            Assert.True(registered.ContainsKey(t), $"{t.Name} is not a [JsonDerivedType] of EventPayload");
        Assert.Equal(registered.Count, registered.Values.Distinct().Count());
        Assert.Equal(registered.Count, StateChangeTests.ConcreteSubclasses(typeof(EventPayload)).Count());
    }

    [Fact]
    public void State_change_kinds_match_on_both_bases()
    {
        var onEvent = StateChangeTests.Registered(typeof(EventPayload));
        foreach (var (type, kind) in StateChangeTests.Registered(typeof(StateChange)))
            Assert.Equal(kind, onEvent[type]);
    }

    [Fact]
    public void Kind_is_the_registered_discriminator()
    {
        var e = new GameEvent(7, null, new TurnStarted(Guid.CreateVersion7()), null);
        Assert.Equal("turn_started", e.Kind);
        Assert.Equal("models_moved", EventKinds.Of(typeof(ModelsMoved)));
        Assert.Contains("undone", EventKinds.All);
    }
}
```

- [ ] **Step 2: Run; expect failures. Write `Events.cs`:**

```csharp
using System.Reflection;
using System.Text.Json.Serialization;

namespace Tabletop.Protocol;

/// <summary>Everything the log holds. Replay applies these and never re-derives them.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RoundStarted), "round_started")]
[JsonDerivedType(typeof(TurnStarted), "turn_started")]
[JsonDerivedType(typeof(TurnEnded), "turn_ended")]
[JsonDerivedType(typeof(PhaseStarted), "phase_started")]
[JsonDerivedType(typeof(SequenceStarted), "sequence_started")]
[JsonDerivedType(typeof(StepCompleted), "step_completed")]
[JsonDerivedType(typeof(SequenceFinished), "sequence_finished")]
[JsonDerivedType(typeof(SequenceAbandoned), "sequence_abandoned")]
[JsonDerivedType(typeof(AskOpened), "ask_opened")]
[JsonDerivedType(typeof(AskAnswered), "ask_answered")]
[JsonDerivedType(typeof(AskClosed), "ask_closed")]
[JsonDerivedType(typeof(DiceRerolled), "dice_rerolled")]
[JsonDerivedType(typeof(DiceEdited), "dice_edited")]
[JsonDerivedType(typeof(ModifierAdded), "modifier_added")]
[JsonDerivedType(typeof(Undone), "undone")]
[JsonDerivedType(typeof(ModelsMoved), "models_moved")]
[JsonDerivedType(typeof(WoundsSet), "wounds_set")]
[JsonDerivedType(typeof(ConditionSet), "condition_set")]
[JsonDerivedType(typeof(ActionRecorded), "action_recorded")]
[JsonDerivedType(typeof(ActionRecordCleared), "action_record_cleared")]
[JsonDerivedType(typeof(GrantAdded), "grant_added")]
[JsonDerivedType(typeof(GrantRemoved), "grant_removed")]
[JsonDerivedType(typeof(SpellTokensSet), "spell_tokens_set")]
[JsonDerivedType(typeof(WoundsThisActivationSet), "wounds_this_activation_set")]
[JsonDerivedType(typeof(ScoreSet), "score_set")]
[JsonDerivedType(typeof(MarkerPlaced), "marker_placed")]
[JsonDerivedType(typeof(MarkerMoved), "marker_moved")]
[JsonDerivedType(typeof(MarkerRemoved), "marker_removed")]
public abstract record EventPayload;

// Scheduler
/// <summary>Order is the order over teams this round. Changes are the round-boundary hooks' effects (record clears, tokens, fatigue).</summary>
public sealed record RoundStarted(int Round, IReadOnlyList<Guid> Order, IReadOnlyList<StateChange> Changes) : EventPayload;
public sealed record TurnStarted(Guid TeamId) : EventPayload;
/// <summary>Passed is the logged pass that stops a team being re-asked this round. Changes: the end-of-turn hook's effects.</summary>
public sealed record TurnEnded(Guid TeamId, bool Passed, IReadOnlyList<StateChange> Changes) : EventPayload;
public sealed record PhaseStarted(string Phase) : EventPayload;

// Resolution stack
public sealed record SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context) : EventPayload;
/// <summary>
/// Everything the step produced: the answer that resumed it, its rolls, the state changes it applied, and Values — a flat map of
/// named integers holding the step's settled parameters (attacks counted, a capped multiplier, a distance used in tenths, a bid,
/// a winner's ordinal) so a later step or nested frame reads them without re-deriving. No schema per step kind; omitted when null.
/// A step may complete and run again (the same name twice in a row) — a second step_completed, a fresh history.
/// </summary>
public sealed record StepCompleted(Guid FrameId, string Step, AskAnswer? Answer, IReadOnlyList<RollRecord> Rolls, IReadOnlyList<StateChange> Changes) : EventPayload
{
    public IReadOnlyDictionary<string, int>? Values { get; init; }
}
public sealed record SequenceFinished(Guid FrameId) : EventPayload;
/// <summary>Pops the frame and everything above it. Applied effects stay applied.</summary>
public sealed record SequenceAbandoned(Guid FrameId) : EventPayload;

// Asks
public sealed record AskOpened(Ask Ask) : EventPayload;
/// <summary>Answer is null when withheld from this viewer (a sealed window still open).</summary>
public sealed record AskAnswered(Guid AskId, Guid TeamId, AskAnswer? Answer, bool OutsideLegalSet) : EventPayload;
public sealed record TeamAnswer(Guid TeamId, AskAnswer Answer);
public sealed record AskClosed(Guid AskId, IReadOnlyList<TeamAnswer> Answers) : EventPayload;

// Dice
public sealed record DieReplacement(int Index, Die Old, Die New);
public sealed record DiceRerolled(Guid FrameId, Guid RollId, IReadOnlyList<DieReplacement> Replaced) : EventPayload;
public sealed record DiceEdited(Guid FrameId, Guid RollId, IReadOnlyList<DieReplacement> Replaced) : EventPayload;
public sealed record ModifierAdded(Guid FrameId, string Roll, RollModifier Modifier) : EventPayload;

// Undo
/// <summary>Replay to ToSeq from the initial state. The log stays append-only; the applier drops the undone range.</summary>
public sealed record Undone(long ToSeq) : EventPayload;

/// <summary>One row of the log. Actor is null when the engine acted. Warning is set under warn enforcement.</summary>
public sealed record GameEvent(long Seq, Guid? Actor, EventPayload Payload, string? Warning)
{
    [JsonIgnore] public string Kind => EventKinds.Of(Payload);
}

public static class EventKinds
{
    static readonly IReadOnlyDictionary<Type, string> ByType = typeof(EventPayload)
        .GetCustomAttributes<JsonDerivedTypeAttribute>()
        .ToDictionary(a => a.DerivedType, a => (string)a.TypeDiscriminator!);

    public static IReadOnlyCollection<string> All => ByType.Values.ToArray();
    public static string Of(Type payloadType) => ByType[payloadType];
    public static string Of(EventPayload payload) => ByType[payload.GetType()];
}
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.Protocol.Tests`; expect 10 passed. Commit: `Define the event log's payloads`.**

### Task 10: Protocol — commands

**Files:**
- Create: `libs/Tabletop.Protocol/Commands.cs`
- Test: `tests/Tabletop.Protocol.Tests/CommandTests.cs`

**Interfaces:**
- Consumes: `Marker` (Task 5); `ModelRelocation`, `Subject`, `Grant` (Task 6); `AskAnswer` (Task 8); `Point3`, `Yaw` (Task 2).
- Produces: `abstract record Command` with: `AnswerAsk(Guid AskId, AskAnswer Answer)` [answer_ask], `MoveModels(IReadOnlyList<ModelRelocation> Moves)` [move_models], `PlaceMarker(Marker Marker)` [place_marker], `MoveMarker(Guid MarkerId, Point3 Position, Yaw Yaw)` [move_marker], `RemoveMarker(Guid MarkerId)` [remove_marker], `SetWounds(Guid ModelId, int Wounds)` [set_wounds], `SetCondition(Subject Subject, string Condition, bool Present)` [set_condition], `RecordAction(Guid UnitId, string Action)` [record_action], `ClearActionRecord(Guid UnitId)` [clear_action_record], `AddGrant(Guid UnitId, Grant Grant)` [add_grant], `RemoveGrant(Guid UnitId, Guid GrantId)` [remove_grant], `SetSpellTokens(Guid ModelId, int Tokens)` [set_spell_tokens], `SetWoundsThisActivation(Guid UnitId, int Wounds)` [set_wounds_this_activation], `SetScore(Guid TeamId, int Score)` [set_score], `SetActiveTeam(Guid TeamId)` [set_active_team], `SetRound(int Round)` [set_round], `Undo(long ToSeq)` [undo], `AbandonSequence(Guid FrameId)` [abandon_sequence], `RerollDice(Guid FrameId, Guid RollId, IReadOnlyList<int> DiceIndices)` [reroll_dice] (empty = all), `SetDice(Guid FrameId, Guid RollId, IReadOnlyList<DieValue> Dice)` [set_dice], `DieValue(int Index, int Raw)`, `AddModifier(Guid FrameId, string Roll, int Amount)` [add_modifier].
- Commands are intents; the server answers with events (or `Refused` under `strict`). There is no end-turn command and no pass command: both are `AnswerAsk` (scheduler → The turn is an Ask; whole-design review → Architecture's examples). Menu edits are the `Set*/Record*/Add*/Remove*` commands, each producing the matching `StateChange` event; `SetActiveTeam` logs `turn_started`, `SetRound` logs `round_started` (scheduler → Nothing is an invariant).

Spec: client shell → Input and presence (the drop sends one move command), The log, undo and the menu (every item is one command); resolution → Players roll their own dice; action sequences → Abandonment; scheduler → Deployment (every roll result is editable from the menu).

- [ ] **Step 1: Write the failing test**

```csharp
namespace Tabletop.Protocol.Tests;

public class CommandTests
{
    [Fact]
    public void Every_command_is_registered_once()
    {
        var registered = StateChangeTests.Registered(typeof(Command));
        foreach (var t in StateChangeTests.ConcreteSubclasses(typeof(Command)))
            Assert.True(registered.ContainsKey(t), $"{t.Name} missing");
        Assert.Equal(registered.Count, registered.Values.Distinct().Count());
    }

    [Fact]
    public void There_is_no_end_turn_or_pass_command() =>
        Assert.DoesNotContain(StateChangeTests.ConcreteSubclasses(typeof(Command)), t => t.Name is "EndTurn" or "EndActivation" or "Pass");
}
```

- [ ] **Step 2: Run; expect `Command` undefined. Write `Commands.cs`:**

```csharp
using System.Text.Json.Serialization;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>An intent. The server validates and emits events; under strict enforcement it may refuse.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AnswerAsk), "answer_ask")]
[JsonDerivedType(typeof(MoveModels), "move_models")]
[JsonDerivedType(typeof(PlaceMarker), "place_marker")]
[JsonDerivedType(typeof(MoveMarker), "move_marker")]
[JsonDerivedType(typeof(RemoveMarker), "remove_marker")]
[JsonDerivedType(typeof(SetWounds), "set_wounds")]
[JsonDerivedType(typeof(SetCondition), "set_condition")]
[JsonDerivedType(typeof(RecordAction), "record_action")]
[JsonDerivedType(typeof(ClearActionRecord), "clear_action_record")]
[JsonDerivedType(typeof(AddGrant), "add_grant")]
[JsonDerivedType(typeof(RemoveGrant), "remove_grant")]
[JsonDerivedType(typeof(SetSpellTokens), "set_spell_tokens")]
[JsonDerivedType(typeof(SetWoundsThisActivation), "set_wounds_this_activation")]
[JsonDerivedType(typeof(SetScore), "set_score")]
[JsonDerivedType(typeof(SetActiveTeam), "set_active_team")]
[JsonDerivedType(typeof(SetRound), "set_round")]
[JsonDerivedType(typeof(Undo), "undo")]
[JsonDerivedType(typeof(AbandonSequence), "abandon_sequence")]
[JsonDerivedType(typeof(RerollDice), "reroll_dice")]
[JsonDerivedType(typeof(SetDice), "set_dice")]
[JsonDerivedType(typeof(AddModifier), "add_modifier")]
public abstract record Command;

/// <summary>The only way to act inside a turn or a sequence, and the only way to pass.</summary>
public sealed record AnswerAsk(Guid AskId, AskAnswer Answer) : Command;
/// <summary>A hand move: start and stop per model. Never ends a turn.</summary>
public sealed record MoveModels(IReadOnlyList<ModelRelocation> Moves) : Command;
public sealed record PlaceMarker(Marker Marker) : Command;
public sealed record MoveMarker(Guid MarkerId, Point3 Position, Yaw Yaw) : Command;
public sealed record RemoveMarker(Guid MarkerId) : Command;

// The menu: every property is editable, accepted under every enforcement setting.
public sealed record SetWounds(Guid ModelId, int Wounds) : Command;
public sealed record SetCondition(Subject Subject, string Condition, bool Present) : Command;
public sealed record RecordAction(Guid UnitId, string Action) : Command;
public sealed record ClearActionRecord(Guid UnitId) : Command;
public sealed record AddGrant(Guid UnitId, Grant Grant) : Command;
public sealed record RemoveGrant(Guid UnitId, Guid GrantId) : Command;
public sealed record SetSpellTokens(Guid ModelId, int Tokens) : Command;
public sealed record SetWoundsThisActivation(Guid UnitId, int Wounds) : Command;
public sealed record SetScore(Guid TeamId, int Score) : Command;
/// <summary>Logs a turn_started.</summary>
public sealed record SetActiveTeam(Guid TeamId) : Command;
/// <summary>Logs a round_started with the current order.</summary>
public sealed record SetRound(int Round) : Command;

public sealed record Undo(long ToSeq) : Command;
public sealed record AbandonSequence(Guid FrameId) : Command;
/// <summary>Empty DiceIndices means every die. Per roll, not per die.</summary>
public sealed record RerollDice(Guid FrameId, Guid RollId, IReadOnlyList<int> DiceIndices) : Command;
public sealed record DieValue(int Index, int Raw);
public sealed record SetDice(Guid FrameId, Guid RollId, IReadOnlyList<DieValue> Dice) : Command;
/// <summary>The same shape as a pack's modify-roll effect, with the player as its source.</summary>
public sealed record AddModifier(Guid FrameId, string Roll, int Amount) : Command;
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.Protocol.Tests`; expect 12 passed. Commit: `Define the commands`.**

### Task 11: Protocol — presence and edits

**Files:**
- Create: `libs/Tabletop.Protocol/Presence.cs`, `libs/Tabletop.Protocol/Edits.cs`
- Test: `tests/Tabletop.Protocol.Tests/EditTests.cs`

**Interfaces:**
- Consumes: `Point3`, `Yaw`, `TableSize` (Task 2); `VisualRef`, `TerrainVolume`, `TerrainPlacement`, `Marker`, `TableState` (Task 5); `SchedulerShape`, `Enforcement` (Task 6).
- Produces (presence): `Presence(Guid ParticipantId, Cursor? Cursor, Selection? Selection, IReadOnlyList<PresencePlacement> Placements, Measurement? Measurement)`; `Cursor(Point3 Point, Guid? Target)`; `Selection(Guid PieceId, IReadOnlyList<Guid> ModelIds)` (PieceId a unit, or during setup a terrain placement or marker; empty ModelIds = the whole unit); `PlacementKind { Held, Proposed }`; `PresencePlacement(Guid PieceId, Point3 Position, Yaw Yaw, PlacementKind Kind)`; `Measurement(Point3 From, Point3 To)`; `Ping(Guid Sender, Point3 Point, Guid? Target)`.
- Produces (edits): `abstract record Edit` — the third message kind: applied, broadcast, persisted, never logged. Lobby family, accepted at any status: `CreateTeam` [create_team], `PickTeam(Guid? TeamId)` [pick_team], `PickArmy(Guid? ArmyId)` [pick_army], `SetConfiguration(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps)` [set_configuration], `StartSetup` [start_setup], `EndSetup` [end_setup] (the last two creator-only). Setup family, accepted only in `setup`: `SetTableSize(TableSize Size)` [set_table_size], `SetTexture(VisualRef Texture)` [set_texture], `AddTerrain(TerrainPlacement Placement)` [add_terrain], `MoveTerrain(Guid PlacementId, Point3 Position, Yaw Yaw)` [move_terrain], `ScaleTerrain(Guid PlacementId, double Scale)` [scale_terrain], `RemoveTerrain(Guid PlacementId)` [remove_terrain], `OverrideTerrainVolumes(Guid PlacementId, IReadOnlyList<TerrainVolume> Volumes)` [override_terrain_volumes], `SwapTerrainMesh(Guid PlacementId, VisualRef Mesh)` [swap_terrain_mesh], `AddSetupMarker(Marker Marker)` [add_setup_marker], `MoveSetupMarker(Guid MarkerId, Point3 Position, Yaw Yaw)` [move_setup_marker], `RemoveSetupMarker(Guid MarkerId)` [remove_setup_marker] (named `*Setup*` because `PlaceMarker`/`MoveMarker`/`RemoveMarker` are the in-play commands), `SetZoneStyle(string Style, IReadOnlyList<string> Shapes)` [set_zone_style], `SwapZones(int TeamOrdinalA, int TeamOrdinalB)` [swap_zones], `ReplaceDraft(TableState Draft)` [replace_draft].
- `SetZoneStyle` carries the style's shape ids — `Tabletop.Geometry.ZoneStyles.ShapesOf(style)`, namespaced `"<style>/<n>"` in team-ordinal order (geometry plan Task 13) — so the server's `DraftEditor` stays a pure function over strings: it writes one `DeploymentZone` per shape keyed by team ordinal `1..n` whether or not a team with that ordinal exists yet, and `SwapZones` swaps two ordinals' shapes (game server plan Task 11). Both ends reference the library; the client draws `ZoneStyles.Resolve(shape, size)` and the server may check `ZoneStyles.BaseFullyWithin` for the deploy Ask (setup phase → Zones).

Spec: presence (the value, the cursor, one placement kind, a measurement is two points, a ping is not a field); setup phase → Editing is free for all (the closed set), Joining, The lifecycle, Layouts; whole-design review ruling 4 (lobby picks are of the setup-edit kind).

- [ ] **Step 1: Write the failing test**

```csharp
namespace Tabletop.Protocol.Tests;

public class EditTests
{
    [Fact]
    public void Every_edit_is_registered_once()
    {
        var registered = StateChangeTests.Registered(typeof(Edit));
        foreach (var t in StateChangeTests.ConcreteSubclasses(typeof(Edit)))
            Assert.True(registered.ContainsKey(t), $"{t.Name} missing");
        Assert.Equal(registered.Count, registered.Values.Distinct().Count());
    }

    [Fact]
    public void Lobby_and_setup_families_partition_the_edits()
    {
        var all = StateChangeTests.ConcreteSubclasses(typeof(Edit)).ToList();
        Assert.All(all, t => Assert.True(t.IsSubclassOf(typeof(LobbyEdit)) ^ t.IsSubclassOf(typeof(SetupEdit))));
        Assert.Equal(6, all.Count(t => t.IsSubclassOf(typeof(LobbyEdit))));
    }

    [Fact]
    public void A_ping_is_not_a_presence_field() =>
        Assert.DoesNotContain(typeof(Presence).GetProperties(), p => p.PropertyType == typeof(Ping));
}
```

- [ ] **Step 2: Run; expect compile errors. Write `Presence.cs`:**

```csharp
using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>One value per connected participant, latest wins, never logged. Existence is connectedness.</summary>
public sealed record Presence(Guid ParticipantId, Cursor? Cursor, Selection? Selection, IReadOnlyList<PresencePlacement> Placements, Measurement? Measurement);

/// <summary>Where the ray landed, and the piece under it.</summary>
public sealed record Cursor(Point3 Point, Guid? Target);

/// <summary>A unit and optionally models within it; during setup a terrain placement or marker. Empty ModelIds means the whole unit.</summary>
public sealed record Selection(Guid PieceId, IReadOnlyList<Guid> ModelIds);

public enum PlacementKind { Held, Proposed }

/// <summary>A piece somewhere it is not committed to. Held: others render it there. Proposed: a translucent copy.</summary>
public sealed record PresencePlacement(Guid PieceId, Point3 Position, Yaw Yaw, PlacementKind Kind);

/// <summary>Two points already resolved edge to edge; the receiver draws the line and computes the length.</summary>
public sealed record Measurement(Point3 From, Point3 To);

/// <summary>A one-shot pointer with no words.</summary>
public sealed record Ping(Guid Sender, Point3 Point, Guid? Target);
```

- [ ] **Step 3: Write `Edits.cs`**

```csharp
using System.Text.Json.Serialization;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>The third message kind: applied to the game's rows or draft, broadcast, persisted, never logged.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CreateTeam), "create_team")]
[JsonDerivedType(typeof(PickTeam), "pick_team")]
[JsonDerivedType(typeof(PickArmy), "pick_army")]
[JsonDerivedType(typeof(SetConfiguration), "set_configuration")]
[JsonDerivedType(typeof(StartSetup), "start_setup")]
[JsonDerivedType(typeof(EndSetup), "end_setup")]
[JsonDerivedType(typeof(SetTableSize), "set_table_size")]
[JsonDerivedType(typeof(SetTexture), "set_texture")]
[JsonDerivedType(typeof(AddTerrain), "add_terrain")]
[JsonDerivedType(typeof(MoveTerrain), "move_terrain")]
[JsonDerivedType(typeof(ScaleTerrain), "scale_terrain")]
[JsonDerivedType(typeof(RemoveTerrain), "remove_terrain")]
[JsonDerivedType(typeof(OverrideTerrainVolumes), "override_terrain_volumes")]
[JsonDerivedType(typeof(SwapTerrainMesh), "swap_terrain_mesh")]
[JsonDerivedType(typeof(AddSetupMarker), "add_setup_marker")]
[JsonDerivedType(typeof(MoveSetupMarker), "move_setup_marker")]
[JsonDerivedType(typeof(RemoveSetupMarker), "remove_setup_marker")]
[JsonDerivedType(typeof(SetZoneStyle), "set_zone_style")]
[JsonDerivedType(typeof(SwapZones), "swap_zones")]
[JsonDerivedType(typeof(ReplaceDraft), "replace_draft")]
public abstract record Edit;

/// <summary>Writes the game's rows; accepted at any status (teams and armies stay editable through setup).</summary>
public abstract record LobbyEdit : Edit;
public sealed record CreateTeam : LobbyEdit;
/// <summary>Null leaves the team: a participant with no team watches.</summary>
public sealed record PickTeam(Guid? TeamId) : LobbyEdit;
public sealed record PickArmy(Guid? ArmyId) : LobbyEdit;
public sealed record SetConfiguration(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps) : LobbyEdit;
/// <summary>Creator only. Lobby → setup; creates the draft row.</summary>
public sealed record StartSetup : LobbyEdit;
/// <summary>Creator only. Setup → playing; off-slab terrain dropped, snapshots written, the log opens.</summary>
public sealed record EndSetup : LobbyEdit;

/// <summary>Writes the draft; accepted only while status is setup. Applied in arrival order.</summary>
public abstract record SetupEdit : Edit;
public sealed record SetTableSize(TableSize Size) : SetupEdit;
public sealed record SetTexture(VisualRef Texture) : SetupEdit;
/// <summary>Client-generated ids; volumes always present (copied from the asset or built-in).</summary>
public sealed record AddTerrain(TerrainPlacement Placement) : SetupEdit;
public sealed record MoveTerrain(Guid PlacementId, Point3 Position, Yaw Yaw) : SetupEdit;
public sealed record ScaleTerrain(Guid PlacementId, double Scale) : SetupEdit;
public sealed record RemoveTerrain(Guid PlacementId) : SetupEdit;
/// <summary>Also how overrides are "cleared": send the asset's or built-in's own volumes again.</summary>
public sealed record OverrideTerrainVolumes(Guid PlacementId, IReadOnlyList<TerrainVolume> Volumes) : SetupEdit;
/// <summary>The placement keeps its volumes and declarations.</summary>
public sealed record SwapTerrainMesh(Guid PlacementId, VisualRef Mesh) : SetupEdit;
public sealed record AddSetupMarker(Marker Marker) : SetupEdit;
public sealed record MoveSetupMarker(Guid MarkerId, Point3 Position, Yaw Yaw) : SetupEdit;
public sealed record RemoveSetupMarker(Guid MarkerId) : SetupEdit;
/// <summary>Shapes are the style's zone shape ids in order; team one gets the first.</summary>
public sealed record SetZoneStyle(string Style, IReadOnlyList<string> Shapes) : SetupEdit;
public sealed record SwapZones(int TeamOrdinalA, int TeamOrdinalB) : SetupEdit;
/// <summary>How a layout is loaded. Size included: the layout wins.</summary>
public sealed record ReplaceDraft(TableState Draft) : SetupEdit;
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Protocol.Tests`; expect 15 passed. Commit: `Define presence values and the unlogged edits`.**

### Task 12: Protocol — the socket envelope and the wire format

**Files:**
- Create: `libs/Tabletop.Protocol/Wire.cs`
- Modify: `tests/Tabletop.Protocol.Tests/Tabletop.Protocol.Tests.csproj` (add the `Tabletop.Content.Tests` project reference the samples need — Step 1)
- Test: `tests/Tabletop.Protocol.Tests/Samples.cs`, `tests/Tabletop.Protocol.Tests/WireJsonTests.cs`

**Interfaces:**
- Consumes: `ContentJson.Configure` (Task 5); `GameInfo`, `Board` (Task 6); `GameEvent` (Task 9); `Command` (Task 10); `Presence`, `Ping`, `Edit` (Task 11); and, in the tests, `ArmyTests.Sample()` and `TableStateTests.Sample()` (Tasks 4 and 5).
- Produces: `abstract record SocketMessage` — one union for both directions, each kind documented with its direction: client→server `Connect(string SessionJwt, Guid GameId, int ClientVersion)` [connect], `CommandMessage(Guid CommandId, Command Command)` [command], `EditMessage(Guid EditId, Edit Edit)` [edit]; server→client `Welcome(Guid ParticipantId, GameInfo Game)` [welcome], `GameInfoMessage(GameInfo Game)` [game], `BoardMessage(Board Board)` [board], `EventsMessage(IReadOnlyList<GameEvent> Events)` [events], `AtRest(long Seq)` [at_rest], `PresenceEnded(Guid ParticipantId)` [presence_ended], `Refused(Guid MessageId, RefusalReason Reason, string Detail)` [refused], `Rejected(RejectReason Reason)` [rejected]; both ways `PresenceMessage(Presence Presence)` [presence], `PingMessage(Ping Ping)` [ping]. Thirteen kinds. `RejectReason { UpgradeRequired, InvalidSession, NotAParticipant }`; `RefusalReason { OutsideLegalSet, Illegal, WrongStatus, NotPermitted, Unknown }`. `WireJson.Options`, `WireJson.Serialize(SocketMessage)`, `WireJson.Deserialize(string) : SocketMessage`, `WireJson.Serialize<T>(T)`, `WireJson.Deserialize<T>(string)`.
- Connect sequence (client shell → The connection and the projection): `Connect` → `Welcome` (teams, participants, configuration) → from `setup` on, `BoardMessage` (the draft, or the snapshot with armies) → `EventsMessage` with the log from the start → live `EventsMessage`s. After a lobby edit the server sends `GameInfoMessage`; after a setup edit, `BoardMessage` with the whole draft (the client shell: "a setup edit replaces the draft"). Each of those rounds of processing ends with `AtRest(Seq)`, whose recipients follow the game server plan's rule that the frame follows its batch (owner protocol ruling 4 of 2026-09-06).

- [ ] **Step 1: Write `Samples.cs` — one instance of every concrete message, event, command, edit, legal set and answer**

```csharp
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;

namespace Tabletop.Protocol.Tests;

public static class Samples
{
    public static readonly Guid Team1 = Guid.CreateVersion7(), Team2 = Guid.CreateVersion7(), P1 = Guid.CreateVersion7();
    public static readonly Guid Frame = Guid.CreateVersion7(), Roll = Guid.CreateVersion7(), AskId = Guid.CreateVersion7();
    public static readonly Army Army = ArmyTests.Sample();
    public static Guid Unit => Army.Units[0].Id;
    public static Guid Model => Army.Units[0].Models[0].Id;
    static Placement At(double x, double y) => new(new Point3(x, y, 0), new Yaw(0));
    public static ModelRelocation Deploy => new(Model, new OnShelf(), new OnTable(At(12, 12)));
    public static readonly Guid Container = Guid.CreateVersion7();
    /// <summary>A move action that ended inside a container (game flow → Inside a container); the reverse is the disembark.</summary>
    public static ModelRelocation Embark => new(Model, new OnTable(At(12, 12)), new Inside(Container));
    public static ModelRelocation Disembark => new(Model, new Inside(Container), new OnTable(At(14, 12)));
    public static GameInfo Game => new(Guid.CreateVersion7(), "Friday", GameStatus.Playing, P1, "grimdark-future", "sha256:abc",
        SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 0x0123_4567_89AB_CDEF,
        [new Team(Team1, 1), new Team(Team2, 2)],
        [new Participant(P1, Guid.CreateVersion7(), "Clay", Team1, null)]);
    public static Board Board => new(TableStateTests.Sample(), [new ArmyInPlay(P1, ArmySnapshot.Of(Army, Guid.CreateVersion7()))]);
    public static RollRecord Hit => new(Roll, "hit", 4, [new RollModifier("AP", -1)], [new Die(6, 6, true), new Die(2, 2, false)]);
    public static Marker Token => new(Guid.CreateVersion7(), MarkerKind.Token, new Point3(1, 1, 0), new Yaw(0), 1, "smoke", VisualRef.OfBuiltin("chip"));
    public static Grant Grant => new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Regeneration", Unit, 1);

    public static readonly LegalSet[] LegalSets =
    [
        new Options([new AskOption("a", "Advance", [Unit])]), new PieceSelection([Unit], 1, 2), new Allocation([Model], 3),
        new Assignment([new AskOption("g1", "Rifles", [Model])], [Unit], 2), new MoveRegion([Model], 6, [Guid.CreateVersion7()], [Guid.CreateVersion7()], 6),
        new MoveRegion([Model], 6, [], [], 6, new MoveContainment([Container], null, null)),                 // may embark
        new MoveRegion([Model], 6, [], [], 6, new MoveContainment([], Container, 6)),                        // is disembarking
        new Bid(3), new Confirmation(),
    ];
    public static readonly AskAnswer[] Answers =
    [
        new Chosen("a"), new SelectedPieces([Unit]), new Allocated(new Dictionary<Guid, int> { [Model] = 3 }),
        new Assigned(new Dictionary<string, Guid> { ["g1"] = Unit }), new Destinations([Deploy]), new Destinations([Embark]), new Destinations([Disembark]),
        new BidPlaced(2), new Confirmed(), new Passed(),
    ];
    public static Ask Ask => new(AskId, AskQuestions.Activate, "Activate", [Team1], LegalSets[0], true, false, null, null);

    public static readonly EventPayload[] Events =
    [
        new RoundStarted(1, [Team1, Team2], [new ActionRecordCleared(Unit)]), new TurnStarted(Team1), new TurnEnded(Team1, true, []),
        new PhaseStarted("deployment"), new SequenceStarted(Frame, "attack-sequence", new SequenceContext(Unit, [Unit], [], "Advance")),
        new StepCompleted(Frame, "roll-to-hit", new Chosen("a"), [Hit], [new WoundsSet(Model, 1)]) { Values = new Dictionary<string, int> { ["attacks"] = 2, ["hits"] = 1 } }, new SequenceFinished(Frame),
        new SequenceAbandoned(Frame), new AskOpened(Ask), new AskAnswered(AskId, Team1, null, false),
        new AskClosed(AskId, [new TeamAnswer(Team1, new BidPlaced(2))]),
        new DiceRerolled(Frame, Roll, [new DieReplacement(1, new Die(2, 2, false), new Die(5, 5, true))]),
        new DiceEdited(Frame, Roll, [new DieReplacement(0, new Die(6, 6, true), new Die(1, 1, false))]),
        new ModifierAdded(Frame, "hit", new RollModifier("Clay", 1)), new Undone(3),
        new ModelsMoved([Deploy]), new ModelsMoved([Embark]), new WoundsSet(Model, 2), new ConditionSet(new Subject(SubjectKind.Unit, Unit), "shaken", true),
        new ActionRecorded(Unit, "Advance"), new ActionRecordCleared(Unit), new GrantAdded(Unit, Grant), new GrantRemoved(Unit, Grant.Id),
        new SpellTokensSet(Model, 3), new WoundsThisActivationSet(Unit, 2), new ScoreSet(Team1, 4),
        new MarkerPlaced(Token), new MarkerMoved(Token.Id, new Point3(2, 2, 0), new Yaw(90)), new MarkerRemoved(Token.Id),
    ];
    public static readonly Command[] Commands =
    [
        new AnswerAsk(AskId, new Passed()), new MoveModels([Deploy]), new PlaceMarker(Token), new MoveMarker(Token.Id, new Point3(0, 0, 0), new Yaw(0)),
        new RemoveMarker(Token.Id), new SetWounds(Model, 1), new SetCondition(new Subject(SubjectKind.Model, Model), "fatigued", false),
        new RecordAction(Unit, "Hold"), new ClearActionRecord(Unit), new AddGrant(Unit, Grant), new RemoveGrant(Unit, Grant.Id),
        new SetSpellTokens(Model, 6), new SetWoundsThisActivation(Unit, 0), new SetScore(Team2, 1), new SetActiveTeam(Team2), new SetRound(2),
        new Undo(3), new AbandonSequence(Frame), new RerollDice(Frame, Roll, []), new SetDice(Frame, Roll, [new DieValue(0, 6)]), new AddModifier(Frame, "hit", -1),
    ];
    public static readonly Edit[] Edits =
    [
        new CreateTeam(), new PickTeam(Team1), new PickArmy(Army.Id), new SetConfiguration("Friday", "sha256:abc", SchedulerShape.PlayerTurn, Enforcement.Strict, true),
        new StartSetup(), new EndSetup(), new SetTableSize(new TableSize(72, 48)), new SetTexture(VisualRef.OfBuiltin("grass")),
        new AddTerrain(TableStateTests.Sample().Terrain[0]), new MoveTerrain(Guid.CreateVersion7(), new Point3(1, 2, 0), new Yaw(0)),
        new ScaleTerrain(Guid.CreateVersion7(), 1.5), new RemoveTerrain(Guid.CreateVersion7()),
        new OverrideTerrainVolumes(Guid.CreateVersion7(), TableStateTests.Sample().Terrain[0].Volumes), new SwapTerrainMesh(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruin")),
        new AddSetupMarker(Token), new MoveSetupMarker(Token.Id, new Point3(3, 3, 0), new Yaw(0)), new RemoveSetupMarker(Token.Id),
        new SetZoneStyle("frontline", ["frontline/1", "frontline/2"]), new SwapZones(1, 2), new ReplaceDraft(TableStateTests.Sample()),
    ];
    public static readonly SocketMessage[] Messages =
    [
        new Connect("jwt", Guid.CreateVersion7(), ProtocolVersion.Current), new CommandMessage(Guid.CreateVersion7(), Commands[0]),
        new EditMessage(Guid.CreateVersion7(), Edits[0]), new Welcome(P1, Game), new GameInfoMessage(Game), new BoardMessage(Board),
        new EventsMessage([new GameEvent(1, P1, Events[0], null), new GameEvent(2, null, Events[5], "outside the legal set")]),
        new PresenceMessage(new Presence(P1, new Cursor(new Point3(1, 2, 3), Model), new Selection(Unit, []), [new PresencePlacement(Model, new Point3(1, 1, 0), new Yaw(0), PlacementKind.Held)], new Measurement(new Point3(0, 0, 0), new Point3(6, 0, 0)))),
        new PingMessage(new Ping(P1, new Point3(1, 1, 0), null)), new PresenceEnded(P1), new AtRest(2),
        new Refused(Guid.CreateVersion7(), RefusalReason.OutsideLegalSet, "unit already activated"), new Rejected(RejectReason.UpgradeRequired),
    ];
}
```

`Tabletop.Protocol.Tests.csproj` needs `<ProjectReference Include="../Tabletop.Content.Tests/Tabletop.Content.Tests.csproj" />` for the sample builders.

- [ ] **Step 2: Write the failing round-trip tests**

```csharp
namespace Tabletop.Protocol.Tests;

public class WireJsonTests
{
    static void RoundTrips<T>(T value)
    {
        var json = WireJson.Serialize(value);
        var back = WireJson.Deserialize<T>(json);
        Assert.Equal(json, WireJson.Serialize(back));
        Assert.Equal(value!.GetType(), back!.GetType());
    }

    [Fact] public void Messages_round_trip() { foreach (var m in Samples.Messages) RoundTrips<SocketMessage>(m); }
    [Fact] public void Events_round_trip() { foreach (var e in Samples.Events) RoundTrips<EventPayload>(e); }
    [Fact] public void State_changes_round_trip_as_a_list() => RoundTrips<IReadOnlyList<StateChange>>(Samples.Events.OfType<StateChange>().ToList());
    [Fact] public void Commands_round_trip() { foreach (var c in Samples.Commands) RoundTrips<Command>(c); }
    [Fact] public void Edits_round_trip() { foreach (var e in Samples.Edits) RoundTrips<Edit>(e); }
    [Fact] public void Legal_sets_and_answers_round_trip() { foreach (var l in Samples.LegalSets) RoundTrips<LegalSet>(l); foreach (var a in Samples.Answers) RoundTrips<AskAnswer>(a); }

    [Fact]
    public void Every_concrete_kind_has_a_sample()
    {
        (Type Base, IEnumerable<object> Samples)[] unions =
        [
            (typeof(SocketMessage), Samples.Messages), (typeof(EventPayload), Samples.Events), (typeof(Command), Samples.Commands),
            (typeof(Edit), Samples.Edits), (typeof(LegalSet), Samples.LegalSets), (typeof(AskAnswer), Samples.Answers),
        ];
        foreach (var (baseType, samples) in unions)
        {
            var sampled = samples.Select(s => s.GetType()).ToHashSet();
            foreach (var t in StateChangeTests.ConcreteSubclasses(baseType))
                Assert.True(sampled.Contains(t), $"no sample for {t.Name}");
        }
    }

    [Fact]
    public void Wire_shape_is_camel_case_with_a_kind_discriminator()
    {
        var json = WireJson.Serialize(new EventsMessage([new GameEvent(1, null, new TurnStarted(Samples.Team1), null)]));
        Assert.StartsWith("{\"kind\":\"events\",\"events\":[{\"seq\":1,\"payload\":{\"kind\":\"turn_started\",\"teamId\":\"", json);
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `Wire.cs`:**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Tabletop.Content;

namespace Tabletop.Protocol;

/// <summary>One frame on the socket, either direction. Kinds say which way they travel.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Connect), "connect")]
[JsonDerivedType(typeof(CommandMessage), "command")]
[JsonDerivedType(typeof(EditMessage), "edit")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(GameInfoMessage), "game")]
[JsonDerivedType(typeof(BoardMessage), "board")]
[JsonDerivedType(typeof(EventsMessage), "events")]
[JsonDerivedType(typeof(AtRest), "at_rest")]
[JsonDerivedType(typeof(PresenceMessage), "presence")]
[JsonDerivedType(typeof(PingMessage), "ping")]
[JsonDerivedType(typeof(PresenceEnded), "presence_ended")]
[JsonDerivedType(typeof(Refused), "refused")]
[JsonDerivedType(typeof(Rejected), "rejected")]
public abstract record SocketMessage;

// Client → server
/// <summary>First frame. The JWT carries the account id; the game id names the game; the version is checked.</summary>
public sealed record Connect(string SessionJwt, Guid GameId, int ClientVersion) : SocketMessage;
public sealed record CommandMessage(Guid CommandId, Command Command) : SocketMessage;
public sealed record EditMessage(Guid EditId, Edit Edit) : SocketMessage;

// Server → client
public sealed record Welcome(Guid ParticipantId, GameInfo Game) : SocketMessage;
/// <summary>After every lobby edit.</summary>
public sealed record GameInfoMessage(GameInfo Game) : SocketMessage;
/// <summary>On connect from setup on, and after every setup edit (the whole draft).</summary>
public sealed record BoardMessage(Board Board) : SocketMessage;
/// <summary>The log from the start on connect, then live events. Filtered per viewer for sealed answers.</summary>
public sealed record EventsMessage(IReadOnlyList<GameEvent> Events) : SocketMessage;
/// <summary>The server has finished processing and has nothing further to emit, sent after the last frame that
/// processing produced. Who receives it, and which inbound frames produce one, are the game server's to decide and
/// are settled in its plan: the frame follows its batch, so it is broadcast when the batch appended an event and
/// goes to the provoking client alone when it did not. A relayed presence or ping never produces one.</summary>
/// <param name="Seq">The last sequence number in the log. It repeats the previous value when this round of
/// processing appended no event, and is 0 for a game whose log is empty, so it is a rest point and not a receipt.</param>
public sealed record AtRest(long Seq) : SocketMessage;
public sealed record PresenceEnded(Guid ParticipantId) : SocketMessage;
/// <summary>A command or edit was not applied. MessageId is the CommandId or EditId.</summary>
public sealed record Refused(Guid MessageId, RefusalReason Reason, string Detail) : SocketMessage;
/// <summary>The connect was not accepted; the server closes the socket after this.</summary>
public sealed record Rejected(RejectReason Reason) : SocketMessage;

// Both directions
public sealed record PresenceMessage(Presence Presence) : SocketMessage;
public sealed record PingMessage(Ping Ping) : SocketMessage;

public enum RejectReason { UpgradeRequired, InvalidSession, NotAParticipant }
public enum RefusalReason { OutsideLegalSet, Illegal, WrongStatus, NotPermitted, Unknown }

public static class WireJson
{
    public static JsonSerializerOptions Options { get; } = ContentJson.Configure(new JsonSerializerOptions());

    public static string Serialize(SocketMessage message) => JsonSerializer.Serialize(message, Options);
    public static SocketMessage Deserialize(string json) => Deserialize<SocketMessage>(json);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("null frame");
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Protocol.Tests`; expect 23 passed. If `Every_concrete_kind_has_a_sample` fails, add the sample — never delete the assertion. Commit: `Define the socket envelope and the wire format`.**

### Task 13: Protocol — the projection

**Files:**
- Create: `libs/Tabletop.Protocol/Projection.cs`
- Test: `tests/Tabletop.Protocol.Tests/ProjectionTests.cs`

**Interfaces:**
- Consumes: `Unit`, `ModelSlot`, `Marker` (Tasks 4 and 5); `Board`, `GameInfo`, `Location`, `Grant`, `RollRecord`, `RollModifier`, `SequenceContext` (Task 6); `StateChange` (Task 7); `Ask`, `AskAnswer` (Task 8); `StepCompleted.Values` (Task 9).
- Produces: `sealed class Projection` with `static Projection Initial(Board board, GameInfo game)`; read-only views `Board Board`, `IReadOnlyDictionary<Guid, UnitInPlay> Units`, `IReadOnlyDictionary<Guid, ModelInPlay> Models`, `IReadOnlyDictionary<Guid, Marker> Markers` (tokens dropped in play), `IReadOnlyDictionary<Guid, int> Scores` (by team), `SchedulerPosition Scheduler`, `IReadOnlyList<Frame> Stack` (bottom first), `IReadOnlyDictionary<Guid, OpenAsk> OpenAsks`, `long LastSeq`. Internal mutable state is exposed only to `EventApplier` (same assembly, `internal` members).
- `sealed class UnitInPlay`: `Guid Id`, `Guid ParticipantId`, `Unit Definition`, `IReadOnlySet<string> Actions`, `IReadOnlySet<string> Conditions`, `IReadOnlyList<Grant> Grants`, `int WoundsThisActivation`. `sealed class ModelInPlay`: `Guid Id`, `Guid UnitId`, `ModelSlot Slot`, `Location Location`, `int Wounds`, `IReadOnlySet<string> Conditions`, `int SpellTokens`. `sealed class SchedulerPosition`: `int Round`, `IReadOnlyList<Guid> Order`, `Guid? ActiveTeamId`, `string? Phase`, `IReadOnlySet<Guid> Passed`. `sealed class Frame`: `Guid Id`, `string Sequence`, `SequenceContext Context`, `IReadOnlyList<CompletedStep> Steps`, `IReadOnlyDictionary<string, IReadOnlyList<RollModifier>> PendingModifiers`. `sealed class CompletedStep`: `string Step`, `AskAnswer? Answer`, `IReadOnlyList<RollRecord> Rolls`, `IReadOnlyList<StateChange> Changes`, `IReadOnlyDictionary<string, int>? Values` (copied from `StepCompleted.Values`; the interpreter plan's `FrameReader` reads the event, the client's log panel reads this copy). `sealed class OpenAsk`: `Ask Ask`, `IReadOnlyDictionary<Guid, AskAnswer?> Answers` (team → answer, null while withheld).
- Initial state: every model on its owner's shelf, no wounds, no conditions, scores 0, round 0 (client shell → The connection and the projection; game flow → The shelf).

- [ ] **Step 1: Write the failing test**

```csharp
namespace Tabletop.Protocol.Tests;

public class ProjectionTests
{
    [Fact]
    public void Initial_projection_shelves_every_model_and_zeroes_scores()
    {
        var p = Projection.Initial(Samples.Board, Samples.Game);
        Assert.Single(p.Units);
        Assert.Equal(Samples.P1, p.Units[Samples.Unit].ParticipantId);
        Assert.All(p.Models.Values, m => Assert.IsType<OnShelf>(m.Location));
        Assert.Equal(Samples.Unit, p.Models[Samples.Model].UnitId);
        Assert.Equal(0, p.Scores[Samples.Team1]);
        Assert.Equal(0, p.Scheduler.Round);
        Assert.Empty(p.Stack);
        Assert.Empty(p.OpenAsks);
        Assert.Equal(0, p.LastSeq);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `Projection.cs`:**

```csharp
using Tabletop.Content;

namespace Tabletop.Protocol;

/// <summary>Everything the client draws from, rebuilt from the board and the log. Only EventApplier writes it.</summary>
public sealed class Projection
{
    internal readonly Dictionary<Guid, UnitInPlay> UnitsMut = new();
    internal readonly Dictionary<Guid, ModelInPlay> ModelsMut = new();
    internal readonly Dictionary<Guid, Marker> MarkersMut = new();
    internal readonly Dictionary<Guid, int> ScoresMut = new();
    internal readonly List<Frame> StackMut = new();
    internal readonly Dictionary<Guid, OpenAsk> OpenAsksMut = new();

    private Projection(Board board) { Board = board; }

    public Board Board { get; }
    public IReadOnlyDictionary<Guid, UnitInPlay> Units => UnitsMut;
    public IReadOnlyDictionary<Guid, ModelInPlay> Models => ModelsMut;
    public IReadOnlyDictionary<Guid, Marker> Markers => MarkersMut;
    public IReadOnlyDictionary<Guid, int> Scores => ScoresMut;
    public SchedulerPosition Scheduler { get; } = new();
    public IReadOnlyList<Frame> Stack => StackMut;
    public IReadOnlyDictionary<Guid, OpenAsk> OpenAsks => OpenAsksMut;
    public long LastSeq { get; internal set; }

    public static Projection Initial(Board board, GameInfo game)
    {
        var p = new Projection(board);
        foreach (var team in game.Teams) p.ScoresMut[team.Id] = 0;
        foreach (var army in board.Armies)
            foreach (var unit in army.Snapshot.Units)
            {
                p.UnitsMut[unit.Id] = new UnitInPlay(unit.Id, army.ParticipantId, unit);
                foreach (var slot in unit.Models) p.ModelsMut[slot.Id] = new ModelInPlay(slot.Id, unit.Id, slot);
            }
        return p;
    }
}

public sealed class UnitInPlay(Guid id, Guid participantId, Unit definition)
{
    internal readonly HashSet<string> ActionsMut = new();
    internal readonly HashSet<string> ConditionsMut = new();
    internal readonly List<Grant> GrantsMut = new();
    public Guid Id { get; } = id;
    public Guid ParticipantId { get; } = participantId;
    public Unit Definition { get; } = definition;
    public IReadOnlySet<string> Actions => ActionsMut;
    public IReadOnlySet<string> Conditions => ConditionsMut;
    public IReadOnlyList<Grant> Grants => GrantsMut;
    public int WoundsThisActivation { get; internal set; }
}

public sealed class ModelInPlay(Guid id, Guid unitId, ModelSlot slot)
{
    internal readonly HashSet<string> ConditionsMut = new();
    public Guid Id { get; } = id;
    public Guid UnitId { get; } = unitId;
    public ModelSlot Slot { get; } = slot;
    public Location Location { get; internal set; } = new OnShelf();
    public int Wounds { get; internal set; }
    public IReadOnlySet<string> Conditions => ConditionsMut;
    public int SpellTokens { get; internal set; }
}

/// <summary>Round, order over teams, whose turn, who has passed. Phase is deployment, a GameRules phase name, or null.</summary>
public sealed class SchedulerPosition
{
    internal readonly List<Guid> OrderMut = new();
    internal readonly HashSet<Guid> PassedMut = new();
    public int Round { get; internal set; }
    public IReadOnlyList<Guid> Order => OrderMut;
    public Guid? ActiveTeamId { get; internal set; }
    public string? Phase { get; internal set; }
    public IReadOnlySet<Guid> Passed => PassedMut;
}

public sealed class Frame(Guid id, string sequence, SequenceContext context)
{
    internal readonly List<CompletedStep> StepsMut = new();
    internal readonly Dictionary<string, List<RollModifier>> PendingMut = new();
    public Guid Id { get; } = id;
    public string Sequence { get; } = sequence;
    public SequenceContext Context { get; } = context;
    public IReadOnlyList<CompletedStep> Steps => StepsMut;
    public IReadOnlyDictionary<string, IReadOnlyList<RollModifier>> PendingModifiers =>
        PendingMut.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<RollModifier>)kv.Value);
}

public sealed class CompletedStep(string step, AskAnswer? answer, IReadOnlyList<RollRecord> rolls, IReadOnlyList<StateChange> changes, IReadOnlyDictionary<string, int>? values)
{
    internal readonly List<RollRecord> RollsMut = rolls.ToList();
    public string Step { get; } = step;
    public AskAnswer? Answer { get; } = answer;
    public IReadOnlyList<RollRecord> Rolls => RollsMut;
    public IReadOnlyList<StateChange> Changes { get; } = changes;
    /// <summary>The step's settled parameters, copied from StepCompleted.Values; null when the step recorded none.</summary>
    public IReadOnlyDictionary<string, int>? Values { get; } = values;
}

public sealed class OpenAsk(Ask ask)
{
    internal readonly Dictionary<Guid, AskAnswer?> AnswersMut = new();
    public Ask Ask { get; } = ask;
    public IReadOnlyDictionary<Guid, AskAnswer?> Answers => AnswersMut;
}
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.Protocol.Tests`; expect 24 passed. Commit: `Define the projection the client draws from`.**

### Task 14: Protocol — the rules-free event applier

**Files:**
- Create: `libs/Tabletop.Protocol/EventApplier.cs`
- Test: `tests/Tabletop.Protocol.Tests/EventApplierTests.cs`

**Interfaces:**
- Consumes: `Board`, `GameInfo` (Task 6); every `StateChange` (Task 7); every `EventPayload` and `GameEvent` (Task 9); `Projection` and its views (Task 13); `Samples` (Task 12) in the tests.
- Produces: `sealed class EventApplier(Board board, GameInfo game)` with `Projection Projection { get; }`, `IReadOnlyList<GameEvent> Log` (every event received, undone included), `void Apply(GameEvent e)`, `void ApplyAll(IEnumerable<GameEvent> events)`, `Projection ProjectTo(long seq)` (a fresh projection of the live log up to `seq`, for local undo prediction), `static IReadOnlyList<GameEvent> EffectiveLog(IReadOnlyList<GameEvent> log)` (drops every undone range and the `undone` markers), `static void ApplyTo(Projection p, GameEvent e)`, `static void ApplyChange(Projection p, StateChange change)`. Also `sealed class ProjectionDesyncException(string kind, Guid unknownId) : Exception` with `string Kind` and `Guid UnknownId`, declared in the same file.
- Desync: every lookup that can miss throws `ProjectionDesyncException`, carrying the event kind (`EventKinds.Of(payload)`) and the id that was not found; a change riding inside `round_started`, `turn_ended` or `step_completed` reports its own kind rather than the carrier's. Sixteen lookups can miss: `ModelsMut` under `models_moved`, `wounds_set`, `condition_set` on a model and `spell_tokens_set`; `UnitsMut` under `condition_set` on a unit, `action_recorded`, `action_record_cleared`, `grant_added`, `grant_removed` and `wounds_this_activation_set`; `MarkersMut` under `marker_moved`; `OpenAsksMut` under `ask_answered`; `FrameOf` under `step_completed`, `modifier_added` and `ReplaceDice`; and the roll lookup inside `ReplaceDice`. It means the projection is unusable, not that the event was bad: nothing is unwound, the failed event is already in `Log`, and the caller throws the whole applier away. The client rebuilds from the log; the game server faults the session, because it is applying events it wrote itself and a rebuild throws again at the same event (owner protocol ruling 2 of 2026-09-06).
- Contract: the applier copies. It never decides legality, never derives a location, never clears a store on its own. `Undone(N)` drops live events with `Seq > N`; `N` must be the seq of a live event or 0 (the server refuses anything else). Pending modifiers for roll `R` are cleared when a `step_completed` carries a roll named `R`, because the record already folds them in (action sequences → One roll primitive). Open Asks close only on `ask_closed`; the server emits it when a frame with open Asks is abandoned.

Spec: client shell → The connection and the projection (applier rules-free; undo local first; reconnect is connect); scheduler → State, Re-entering a round (the pass stands); action sequences → The resolution stack; `architecture.md` → Undo.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Tabletop.Protocol.Tests;

public class EventApplierTests
{
    static EventApplier Fresh() => new(Samples.Board, Samples.Game);
    static GameEvent Ev(long seq, EventPayload p) => new(seq, Samples.P1, p, null);
    static readonly Guid Ask = Samples.AskId, Frame = Samples.Frame, Roll = Samples.Roll;

    [Fact]
    public void Deployment_moves_a_model_from_shelf_to_table()
    {
        var a = Fresh();
        a.Apply(Ev(1, new ModelsMoved([Samples.Deploy])));
        Assert.Equal(Samples.Deploy.To, a.Projection.Models[Samples.Model].Location);
        Assert.Equal(1, a.Projection.LastSeq);
    }

    [Fact]
    public void Stores_copy_what_events_say()
    {
        var a = Fresh();
        a.ApplyAll([
            Ev(1, new WoundsSet(Samples.Model, 2)), Ev(2, new ConditionSet(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", true)),
            Ev(3, new ActionRecorded(Samples.Unit, "Advance")), Ev(4, new GrantAdded(Samples.Unit, Samples.Grant)),
            Ev(5, new SpellTokensSet(Samples.Model, 3)), Ev(6, new WoundsThisActivationSet(Samples.Unit, 2)), Ev(7, new ScoreSet(Samples.Team1, 4)),
            Ev(8, new MarkerPlaced(Samples.Token)), Ev(9, new MarkerMoved(Samples.Token.Id, new(5, 5, 0), new(90))),
        ]);
        var u = a.Projection.Units[Samples.Unit]; var m = a.Projection.Models[Samples.Model];
        Assert.Equal((2, 3), (m.Wounds, m.SpellTokens));
        Assert.Contains("shaken", u.Conditions); Assert.Contains("Advance", u.Actions);
        Assert.Single(u.Grants); Assert.Equal(2, u.WoundsThisActivation); Assert.Equal(4, a.Projection.Scores[Samples.Team1]);
        Assert.Equal(5, a.Projection.Markers[Samples.Token.Id].Position.X);
        a.ApplyAll([Ev(10, new ConditionSet(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", false)), Ev(11, new ActionRecordCleared(Samples.Unit)),
            Ev(12, new GrantRemoved(Samples.Unit, Samples.Grant.Id)), Ev(13, new MarkerRemoved(Samples.Token.Id))]);
        Assert.Empty(u.Conditions); Assert.Empty(u.Actions); Assert.Empty(u.Grants); Assert.Empty(a.Projection.Markers);
    }

    [Fact]
    public void Scheduler_position_follows_the_four_events_and_the_pass_stands()
    {
        var a = Fresh();
        a.ApplyAll([Ev(1, new RoundStarted(1, [Samples.Team1, Samples.Team2], [new ActionRecorded(Samples.Unit, "x")])),
            Ev(2, new PhaseStarted("rounds")), Ev(3, new TurnStarted(Samples.Team1)), Ev(4, new TurnEnded(Samples.Team1, true, [new ActionRecordCleared(Samples.Unit)]))]);
        var s = a.Projection.Scheduler;
        Assert.Equal((1, "rounds"), (s.Round, s.Phase));
        Assert.Equal([Samples.Team1, Samples.Team2], s.Order);
        Assert.Null(s.ActiveTeamId); Assert.Contains(Samples.Team1, s.Passed);
        Assert.Empty(a.Projection.Units[Samples.Unit].Actions);
        a.Apply(Ev(5, new RoundStarted(2, [Samples.Team2, Samples.Team1], [])));
        Assert.Empty(a.Projection.Scheduler.Passed);
    }

    [Fact]
    public void Frames_push_record_steps_and_pop_with_everything_above()
    {
        var a = Fresh(); var inner = Guid.CreateVersion7();
        a.ApplyAll([Ev(1, new SequenceStarted(Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null))),
            Ev(2, new ModifierAdded(Frame, "hit", new RollModifier("Clay", 1))),
            Ev(3, new StepCompleted(Frame, "roll-to-hit", null, [Samples.Hit], [new WoundsSet(Samples.Model, 1)]) { Values = new Dictionary<string, int> { ["hits"] = 1 } }),
            Ev(4, new SequenceStarted(inner, "resolve-hits", new SequenceContext(Samples.Unit, [], [], null)))]);
        Assert.Equal(2, a.Projection.Stack.Count);
        Assert.Single(a.Projection.Stack[0].Steps); Assert.Empty(a.Projection.Stack[0].PendingModifiers);
        Assert.Equal(1, a.Projection.Stack[0].Steps[0].Values!["hits"]);                       // copied, never derived
        Assert.Equal(1, a.Projection.Models[Samples.Model].Wounds);
        a.Apply(Ev(5, new DiceRerolled(Frame, Roll, [new DieReplacement(1, new Die(2, 2, false), new Die(5, 5, true))])));
        Assert.Equal(5, a.Projection.Stack[0].Steps[0].Rolls[0].Dice[1].Raw);
        a.Apply(Ev(6, new SequenceAbandoned(Frame)));
        Assert.Empty(a.Projection.Stack);
    }

    [Fact]
    public void Asks_open_collect_answers_and_close()
    {
        var a = Fresh();
        a.ApplyAll([Ev(1, new AskOpened(Samples.Ask)), Ev(2, new AskAnswered(Ask, Samples.Team1, null, false))]);
        Assert.True(a.Projection.OpenAsks[Ask].Answers.ContainsKey(Samples.Team1));
        a.Apply(Ev(3, new AskClosed(Ask, [new TeamAnswer(Samples.Team1, new Passed())])));
        Assert.Empty(a.Projection.OpenAsks);
    }

    [Fact]
    public void Undo_drops_the_range_and_the_log_stays_append_only()
    {
        var a = Fresh();
        a.ApplyAll([Ev(1, new ScoreSet(Samples.Team1, 1)), Ev(2, new ScoreSet(Samples.Team1, 2)), Ev(3, new ScoreSet(Samples.Team1, 3)),
            Ev(4, new Undone(1)), Ev(5, new ScoreSet(Samples.Team2, 9))]);
        Assert.Equal((1, 9), (a.Projection.Scores[Samples.Team1], a.Projection.Scores[Samples.Team2]));
        Assert.Equal(5, a.Log.Count);
        Assert.Equal([1L, 5L], EventApplier.EffectiveLog(a.Log).Select(e => e.Seq));
        Assert.Equal(1, a.ProjectTo(1).Scores[Samples.Team1]);
        Assert.Equal(0, a.ProjectTo(0).Scores[Samples.Team1]);
        a.Apply(Ev(6, new Undone(0)));
        Assert.Equal(0, a.Projection.Scores[Samples.Team2]);
    }

    [Fact]
    public void Moving_a_marker_placed_during_setup_is_a_desync()
    {
        var board = Samples.Board;                                                  // its markers are the draft's, not the log's
        var a = new EventApplier(board, Samples.Game);
        var setupMarker = board.Table.Markers[0].Id;
        var ex = Assert.Throws<ProjectionDesyncException>(
            () => a.Apply(Ev(1, new MarkerMoved(setupMarker, new(2, 2, 0), new(90)))));
        Assert.Equal("marker_moved", ex.Kind);
        Assert.Equal(setupMarker, ex.UnknownId);
    }

    [Fact]
    public void Editing_the_dice_of_a_popped_frame_is_a_desync()
    {
        var a = Fresh();
        a.ApplyAll([Ev(1, new SequenceStarted(Frame, "roll-off", new SequenceContext(null, [], [], null))),
            Ev(2, new StepCompleted(Frame, "roll", null, [Samples.Hit], [])),
            Ev(3, new SequenceFinished(Frame))]);
        var ex = Assert.Throws<ProjectionDesyncException>(
            () => a.Apply(Ev(4, new DiceEdited(Frame, Roll, [new DieReplacement(0, new Die(6, 6, true), new Die(1, 1, false))]))));
        Assert.Equal("dice_edited", ex.Kind);
        Assert.Equal(Frame, ex.UnknownId);
    }
}
```

Both tests state a defect rather than a behaviour anyone wants: the scheduler promises the roll-off's
dice are editable, and setup content puts a token in the snapshot. They pin the fault as a typed,
recoverable desync. The fixes are the game server's (one marker id space, and a roll reachable after
its frame pops).

- [ ] **Step 2: Run; expect `EventApplier` undefined.**

- [ ] **Step 3: Write `EventApplier.cs`**

```csharp
namespace Tabletop.Protocol;

/// <summary>An event named an id the projection does not hold. The projection is unusable and must be rebuilt:
/// the client drops it, reconnects and replays the log; the game server faults the session, because it is applying
/// events it wrote itself and a rebuild throws again at the same event. Nothing is unwound, and the failed event is
/// already in Log, so the caller discards the whole applier rather than the event.</summary>
public sealed class ProjectionDesyncException(string kind, Guid unknownId)
    : Exception($"Event {kind} names {unknownId}, which the projection does not hold.")
{
    /// <summary>The kind of the event or state change that could not be applied.</summary>
    public string Kind { get; } = kind;
    /// <summary>The model, unit, marker, Ask, frame or roll id the projection has no entry for.</summary>
    public Guid UnknownId { get; } = unknownId;
}

/// <summary>Builds a Projection by copying what events say. Rules-free: it never derives, decides or clears on its own.</summary>
public sealed class EventApplier(Board board, GameInfo game)
{
    private readonly List<GameEvent> _log = new();

    public Projection Projection { get; private set; } = Projection.Initial(board, game);
    public IReadOnlyList<GameEvent> Log => _log;

    public void Apply(GameEvent e)
    {
        _log.Add(e);
        if (e.Payload is Undone) Projection = Rebuild(EffectiveLog(_log), e.Seq);
        else ApplyTo(Projection, e);
    }

    public void ApplyAll(IEnumerable<GameEvent> events) { foreach (var e in events) Apply(e); }

    /// <summary>A fresh projection of the live log up to and including <paramref name="seq"/>; what the client shows while an Undo travels.</summary>
    public Projection ProjectTo(long seq) => Rebuild(EffectiveLog(_log).Where(e => e.Seq <= seq).ToList(), seq);

    private Projection Rebuild(IReadOnlyList<GameEvent> live, long lastSeq)
    {
        var p = Projection.Initial(board, game);
        foreach (var e in live) ApplyTo(p, e);
        p.LastSeq = lastSeq;
        return p;
    }

    /// <summary>The log with every undone range removed. Undone markers never appear in the result.</summary>
    public static IReadOnlyList<GameEvent> EffectiveLog(IReadOnlyList<GameEvent> log)
    {
        var live = new List<GameEvent>();
        foreach (var e in log)
        {
            if (e.Payload is Undone u) live.RemoveAll(x => x.Seq > u.ToSeq);
            else live.Add(e);
        }
        return live;
    }

    public static void ApplyTo(Projection p, GameEvent e)
    {
        switch (e.Payload)
        {
            case StateChange c: ApplyChange(p, c); break;
            case RoundStarted r:
                p.Scheduler.Round = r.Round; p.Scheduler.OrderMut.Clear(); p.Scheduler.OrderMut.AddRange(r.Order);
                p.Scheduler.ActiveTeamId = null; p.Scheduler.PassedMut.Clear();
                foreach (var c in r.Changes) ApplyChange(p, c);
                break;
            case TurnStarted t: p.Scheduler.ActiveTeamId = t.TeamId; break;
            case TurnEnded t:
                if (t.Passed) p.Scheduler.PassedMut.Add(t.TeamId);
                if (p.Scheduler.ActiveTeamId == t.TeamId) p.Scheduler.ActiveTeamId = null;
                foreach (var c in t.Changes) ApplyChange(p, c);
                break;
            case PhaseStarted ph: p.Scheduler.Phase = ph.Phase; break;
            case SequenceStarted s: p.StackMut.Add(new Frame(s.FrameId, s.Sequence, s.Context)); break;
            case StepCompleted s:
            {
                var frame = FrameOf(p, s.FrameId, s);
                frame.StepsMut.Add(new CompletedStep(s.Step, s.Answer, s.Rolls, s.Changes, s.Values));
                foreach (var roll in s.Rolls) frame.PendingMut.Remove(roll.Roll);
                foreach (var c in s.Changes) ApplyChange(p, c);
                break;
            }
            case SequenceFinished f: Pop(p, f.FrameId); break;
            case SequenceAbandoned f: Pop(p, f.FrameId); break;
            case AskOpened a: p.OpenAsksMut[a.Ask.Id] = new OpenAsk(a.Ask); break;
            case AskAnswered a: Need(p.OpenAsksMut, a.AskId, a).AnswersMut[a.TeamId] = a.Answer; break;
            case AskClosed a: p.OpenAsksMut.Remove(a.AskId); break;
            case DiceRerolled d: ReplaceDice(p, d, d.FrameId, d.RollId, d.Replaced); break;
            case DiceEdited d: ReplaceDice(p, d, d.FrameId, d.RollId, d.Replaced); break;
            case ModifierAdded m:
            {
                var frame = FrameOf(p, m.FrameId, m);
                if (!frame.PendingMut.TryGetValue(m.Roll, out var list)) frame.PendingMut[m.Roll] = list = new List<RollModifier>();
                list.Add(m.Modifier);
                break;
            }
            case Undone: throw new InvalidOperationException("Undone is applied by rebuilding; use EventApplier.Apply.");
            default: throw new NotSupportedException($"Unknown payload {e.Payload.GetType().Name}");
        }
        p.LastSeq = e.Seq;
    }

    public static void ApplyChange(Projection p, StateChange change)
    {
        switch (change)
        {
            case ModelsMoved m: foreach (var r in m.Moves) Need(p.ModelsMut, r.ModelId, m).Location = r.To; break;
            case WoundsSet w: Need(p.ModelsMut, w.ModelId, w).Wounds = w.Wounds; break;
            case ConditionSet c:
            {
                var set = c.Subject.Kind == SubjectKind.Unit
                    ? Need(p.UnitsMut, c.Subject.Id, c).ConditionsMut
                    : Need(p.ModelsMut, c.Subject.Id, c).ConditionsMut;
                if (c.Present) set.Add(c.Condition); else set.Remove(c.Condition);
                break;
            }
            case ActionRecorded a: Need(p.UnitsMut, a.UnitId, a).ActionsMut.Add(a.Action); break;
            case ActionRecordCleared a: Need(p.UnitsMut, a.UnitId, a).ActionsMut.Clear(); break;
            case GrantAdded g: Need(p.UnitsMut, g.UnitId, g).GrantsMut.Add(g.Grant); break;
            case GrantRemoved g: Need(p.UnitsMut, g.UnitId, g).GrantsMut.RemoveAll(x => x.Id == g.GrantId); break;
            case SpellTokensSet s: Need(p.ModelsMut, s.ModelId, s).SpellTokens = s.Tokens; break;
            case WoundsThisActivationSet w: Need(p.UnitsMut, w.UnitId, w).WoundsThisActivation = w.Wounds; break;
            case ScoreSet s: p.ScoresMut[s.TeamId] = s.Score; break;                       // an assignment cannot miss
            case MarkerPlaced m: p.MarkersMut[m.Marker.Id] = m.Marker; break;
            case MarkerMoved m: p.MarkersMut[m.MarkerId] = Need(p.MarkersMut, m.MarkerId, m) with { Position = m.Position, Yaw = m.Yaw }; break;
            case MarkerRemoved m: p.MarkersMut.Remove(m.MarkerId); break;
            default: throw new NotSupportedException($"Unknown state change {change.GetType().Name}");
        }
    }

    /// <summary>The entry, or a desync naming the payload's kind and the id that was not there.</summary>
    private static TValue Need<TValue>(Dictionary<Guid, TValue> from, Guid id, EventPayload payload) =>
        from.TryGetValue(id, out var value) ? value : throw new ProjectionDesyncException(EventKinds.Of(payload), id);

    private static Frame FrameOf(Projection p, Guid frameId, EventPayload payload) =>
        p.StackMut.FirstOrDefault(f => f.Id == frameId) ?? throw new ProjectionDesyncException(EventKinds.Of(payload), frameId);

    private static void Pop(Projection p, Guid frameId)
    {
        var index = p.StackMut.FindIndex(f => f.Id == frameId);
        if (index >= 0) p.StackMut.RemoveRange(index, p.StackMut.Count - index);
    }

    private static void ReplaceDice(Projection p, EventPayload payload, Guid frameId, Guid rollId, IReadOnlyList<DieReplacement> replaced)
    {
        var step = FrameOf(p, frameId, payload).StepsMut.FirstOrDefault(s => s.RollsMut.Any(r => r.Id == rollId))
            ?? throw new ProjectionDesyncException(EventKinds.Of(payload), rollId);
        var i = step.RollsMut.FindIndex(r => r.Id == rollId);
        var dice = step.RollsMut[i].Dice.ToList();
        foreach (var r in replaced) dice[r.Index] = r.New;
        step.RollsMut[i] = step.RollsMut[i] with { Dice = dice };
    }
}
```

- [ ] **Step 4: Run `dotnet test`; expect every project green (Geometry 12, Content 15, Protocol 32). Commit: `Implement the rules-free event applier`.**

### Task 15: Local development — Postgres, MinIO and the schema

**Files:**
- Create: `docker-compose.yml`, `.env.example`, `scripts/postgres/init/001_schema.sql`

**Interfaces:**
- Consumes: nothing from Tasks 1–14. The SQL is `docs/schema.md`'s thirty-three tables; the enumerations in its check constraints are the same closed sets Tasks 2, 5 and 6 encode, and spec corrections 1, 2, 4, 6 and 12 are applied to it.
- Produces: Postgres 17 on `localhost:5432`, database `tabletop`, user `tabletop`, two Postgres schemas `management` and `game`; MinIO on `localhost:9000` (S3 API) and `localhost:9001` (console), bucket `tabletop-blobs`, path-style addressing; environment variable names in `.env.example` that the management API and game server plans read: `TABLETOP_DB_CONNECTION`, `TABLETOP_S3_ENDPOINT`, `TABLETOP_S3_BUCKET`, `TABLETOP_S3_ACCESS_KEY`, `TABLETOP_S3_SECRET_KEY`.
- Decisions: **MinIO** stands in for S3 because it speaks the S3 API the AWS SDK's `AmazonS3Client` already targets (`ServiceURL` + `ForcePathStyle`), presigned PUT and GET work unchanged, and it is one container plus one bootstrap command — so the management API plan writes S3 code once and hosting later changes only the endpoint (`architecture.md` → Local development: "deploying changes the address, not the shape"). **Two Postgres schemas in one database** (closing `schema.md`'s open item for local development): the game server reads and, before play, writes management's rows, which is one connection when they share a database. **Migrations**: the schema ships here as Postgres init SQL, applied once when the volume is first created; `docker compose down -v && docker compose up -d` re-applies it. The management API plan owns the real migration mechanism (its first migration is this file's contents), because management owns the schema and `libs/` may not touch persistence. Settled with that plan (compatibility review, 2026-09-05): both mechanisms stay. The compose init gives a bare `docker compose up -d` the schema, which is all the game server and its tests need; management's DbUp migrator (`Persistence/Migrator.cs`) embeds a byte-for-byte copy as `001_schema.sql`, recognises a database the init already built (`management.account` exists, no journal) and records 001 as applied instead of re-running it, then applies `002_blob_uploaded.sql` and later scripts itself; a database management creates from nothing (its per-run test databases) gets 001 from DbUp. The `create schema management; create schema game;` at the top of this file is therefore the only place the two schemas are created, on either path. The game server never migrates; the interpreter plan's `Properties` seeding writes rows, not schema.
- Windows reaches WSL over forwarded localhost: with WSL2's default `localhostForwarding=true`, Docker's published ports and any WSL process listening on `0.0.0.0` are reachable from Windows at `localhost:<port>`. The Godot client therefore uses `localhost` for management and the game server, and presigned MinIO URLs are minted for `http://localhost:9000` so the same URL works from a WSL server and a Windows client. Ports reserved for the server plans: management `5100`, game server `5200`.

Spec: `architecture.md` → Deployment (Docker at most), Local development; `docs/schema.md` in full; management API → Asset bytes never pass through the API (presigned URLs).

- [ ] **Step 1: Write `docker-compose.yml`**

```yaml
name: tabletop

services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: tabletop
      POSTGRES_USER: tabletop
      POSTGRES_PASSWORD: ${TABLETOP_DB_PASSWORD:-tabletop}
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data
      - ./scripts/postgres/init:/docker-entrypoint-initdb.d:ro
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U tabletop -d tabletop"]
      interval: 5s
      timeout: 3s
      retries: 10

  minio:
    image: minio/minio:RELEASE.2024-12-18T13-15-00Z
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: ${TABLETOP_S3_ACCESS_KEY:-tabletop}
      MINIO_ROOT_PASSWORD: ${TABLETOP_S3_SECRET_KEY:-tabletop-secret}
    ports:
      - "9000:9000"
      - "9001:9001"
    volumes:
      - minio-data:/data
    healthcheck:
      test: ["CMD", "mc", "ready", "local"]
      interval: 5s
      timeout: 3s
      retries: 10

  minio-init:
    image: minio/mc:RELEASE.2024-11-17T19-35-25Z
    depends_on:
      minio:
        condition: service_healthy
    entrypoint: >
      /bin/sh -c "
      mc alias set local http://minio:9000 ${TABLETOP_S3_ACCESS_KEY:-tabletop} ${TABLETOP_S3_SECRET_KEY:-tabletop-secret} &&
      mc mb --ignore-existing local/${TABLETOP_S3_BUCKET:-tabletop-blobs} &&
      echo bucket ready"

volumes:
  postgres-data:
  minio-data:
```

- [ ] **Step 2: Write `.env.example`** (copy to `.env` locally; `.gitignore` already excludes `.env` and keeps `.env.example`)

```bash
# Local development only. Copy to .env; docker compose and both servers read it.
TABLETOP_DB_PASSWORD=tabletop
TABLETOP_DB_CONNECTION=Host=localhost;Port=5432;Database=tabletop;Username=tabletop;Password=tabletop
TABLETOP_S3_ENDPOINT=http://localhost:9000
TABLETOP_S3_BUCKET=tabletop-blobs
TABLETOP_S3_ACCESS_KEY=tabletop
TABLETOP_S3_SECRET_KEY=tabletop-secret
```

- [ ] **Step 3: Write `scripts/postgres/init/001_schema.sql` — part 1: schemas, accounts, the library, the army**

```sql
-- The physical schema from docs/schema.md. Enumerations are text with check constraints; ids are UUIDv7
-- generated in application code; soft references to asset are plain uuid columns with no foreign key,
-- so deleting an asset is never blocked and a placement degrades to its hitbox.
create schema management;
create schema game;
set search_path to management;

create table account (
    id   uuid primary key,
    name text not null
);

create table blob (
    owner_id  uuid   not null references account(id) on delete cascade,
    hash      text   not null,
    s3_key    text   not null,
    byte_size bigint not null,
    primary key (owner_id, hash)
);

create table asset (
    id                  uuid primary key,
    owner_id            uuid not null references account(id) on delete cascade,
    blob_hash           text not null,
    name                text not null,
    kind                text not null check (kind in ('miniature', 'terrain', 'texture')),
    scale               real not null default 1,
    orientation_pitch   real not null default 0,
    orientation_yaw     real not null default 0,
    orientation_roll    real not null default 0,
    modelled_base_shape text check (modelled_base_shape in ('circle', 'oval', 'square')),
    modelled_base_a     real,
    modelled_base_b     real,
    foreign key (owner_id, blob_hash) references blob(owner_id, hash),
    check ((kind = 'miniature') = (modelled_base_shape is not null))
);

create table asset_volume (
    id          uuid primary key,
    asset_id    uuid not null references asset(id) on delete cascade,
    kind        text not null check (kind in ('solid', 'area')),
    footprint   text not null check (footprint in ('circle', 'rect')),
    dim_a       real not null,
    dim_b       real not null,
    offset_x    real not null default 0,
    offset_y    real not null default 0,
    yaw         real not null default 0,
    bottom      real not null default 0,
    top         real not null,
    impassable  boolean not null default false,
    blocks_los  boolean not null default false,
    see_in_out  boolean not null default false,
    cover       boolean not null default false,
    difficult   boolean not null default false,
    dangerous   boolean not null default false,
    notes       text
);

create table folder (
    id        uuid primary key,
    owner_id  uuid not null references account(id) on delete cascade,
    parent_id uuid references folder(id) on delete cascade,
    name      text not null
);

create table asset_folder (
    asset_id  uuid not null references asset(id) on delete cascade,
    folder_id uuid not null references folder(id) on delete cascade,
    primary key (asset_id, folder_id)
);

create table army (
    id          uuid primary key,
    owner_id    uuid not null references account(id) on delete cascade,
    name        text not null,
    game_system text not null,
    source      text not null
);

create table unit (
    id      uuid primary key,
    army_id uuid not null references army(id) on delete cascade,
    ordinal int  not null,
    name    text not null,
    unique (army_id, ordinal)
);
create table unit_stat     (unit_id uuid not null references unit(id) on delete cascade, stat text not null, value text not null, primary key (unit_id, stat));
create table unit_property (unit_id uuid not null references unit(id) on delete cascade, name text not null, parameter int, primary key (unit_id, name));

create table model_slot (
    id         uuid primary key,
    unit_id    uuid not null references unit(id) on delete cascade,
    ordinal    int  not null,
    name       text not null,
    base_shape text not null check (base_shape in ('circle', 'oval', 'square')),
    base_a     real not null,
    base_b     real not null,
    height     real not null,
    asset_id   uuid,            -- soft reference: appearance only, no FK
    unique (unit_id, ordinal)
);
create table slot_stat     (slot_id uuid not null references model_slot(id) on delete cascade, stat text not null, value text not null, primary key (slot_id, stat));
create table slot_property (slot_id uuid not null references model_slot(id) on delete cascade, name text not null, parameter int, primary key (slot_id, name));

create table weapon (
    id      uuid primary key,
    slot_id uuid not null references model_slot(id) on delete cascade,
    name    text not null
);
create table weapon_stat     (weapon_id uuid not null references weapon(id) on delete cascade, stat text not null, value text not null, primary key (weapon_id, stat));
create table weapon_property (weapon_id uuid not null references weapon(id) on delete cascade, name text not null, parameter int, primary key (weapon_id, name));
```

- [ ] **Step 4: Append part 2 to `001_schema.sql` — Properties, games, the table state, the log**

```sql
-- Properties: the pinned data half of a ruleset, eight tables, addressed by content hash.
create table properties (
    content_hash text primary key,
    name         text not null,
    game_rules   text not null
);
create table action (properties_hash text not null references properties(content_hash) on delete cascade, name text not null, distance real not null, primary key (properties_hash, name));
create table roll   (properties_hash text not null references properties(content_hash) on delete cascade, name text not null, primary key (properties_hash, name));

create table stat_definition (
    properties_hash text not null references properties(content_hash) on delete cascade,
    level           text not null check (level in ('unit', 'model', 'weapon')),
    name            text not null,
    kind            text not null check (kind in ('target_number', 'distance', 'count', 'dice_expression')),
    primary key (properties_hash, level, name)
);

create table catalog_entry (
    properties_hash text not null references properties(content_hash) on delete cascade,
    name            text not null,
    kind            text not null check (kind in ('unit', 'model', 'weapon', 'spell')),
    takes_parameter boolean not null default false,
    aggregation     text check (aggregation in ('all', 'any', 'majority', 'none')),
    cost            int,
    faction         text,
    rule_text       text not null,
    target_side     text check (target_side in ('friendly', 'enemy')),
    target_grain    text check (target_grain in ('unit', 'model')),
    target_count    int,
    target_range    real,
    primary key (properties_hash, name),
    check ((kind = 'model') = (aggregation is not null)),
    check ((kind = 'spell') = (cost is not null and target_side is not null and target_grain is not null and target_count is not null and target_range is not null)),
    check (kind <> 'spell' or takes_parameter = false)
);

create table effect (
    id                    uuid primary key,
    properties_hash       text not null,
    entry_name            text not null,
    parent_effect_id      uuid references effect(id) on delete cascade,
    subject               text not null check (subject in ('self', 'opponent')),
    ordinal               int  not null,
    kind                  text not null check (kind in ('modify_roll', 'modify_stat', 'replace_stat', 'force_reroll', 'roll', 'add_hit',
                                                        'multiply', 'override_result', 'grant_property', 'suppress_property', 'modify_parameter', 'suppress_modifiers')),
    target_roll           text,
    target_stat_level     text,   -- with target_stat: stat_definition's key needs the level (spec correction 6)
    target_stat           text,
    target_entry          text,
    target_action         text,
    target_quantity       text check (target_quantity in ('hits', 'wounds')),
    target_result         text check (target_result in ('wound', 'morale_test')),
    target_modifier       text check (target_modifier in ('cover', 'to_hit_penalties', 'range_penalties')),
    amount                int,
    amount_from_parameter boolean not null default false,
    threshold             int,
    cap_kind              text check (cap_kind in ('models_in_target_unit')),
    replacement           text,
    duration              text not null default 'interaction' check (duration in ('interaction', 'once')),
    foreign key (properties_hash, entry_name)    references catalog_entry(properties_hash, name) on delete cascade,
    foreign key (properties_hash, target_roll)   references roll(properties_hash, name),
    foreign key (properties_hash, target_stat_level, target_stat) references stat_definition(properties_hash, level, name),
    foreign key (properties_hash, target_entry)  references catalog_entry(properties_hash, name),
    foreign key (properties_hash, target_action) references action(properties_hash, name),
    check ((target_stat is null) = (target_stat_level is null)),
    check (threshold is null or kind = 'roll'),
    check (cap_kind is null or kind = 'multiply'),
    check (replacement is null or kind = 'replace_stat')
);

create table effect_condition (
    id         uuid primary key,
    effect_id  uuid not null references effect(id) on delete cascade,
    kind       text not null check (kind in ('action_taken', 'attack_kind', 'range', 'roll_result', 'actor_state', 'position_terrain', 'round_number', 'usage_count')),
    subject    text check (subject in ('self', 'opponent')),
    comparison text not null check (comparison in ('eq', 'neq', 'gte', 'lte', 'gt', 'lt')),
    number     int
);
create table condition_value (condition_id uuid not null references effect_condition(id) on delete cascade, value text not null, primary key (condition_id, value));

-- Games. The game server writes these rows while status is lobby or setup, and reads only from playing on.
create table game (
    id                  uuid primary key,
    name                text not null,
    status              text not null check (status in ('lobby', 'setup', 'playing', 'ended')),
    game_rules          text not null,
    properties_hash     text not null references properties(content_hash),
    scheduler_shape     text not null check (scheduler_shape in ('player-turn', 'alternating-activation')),
    enforcement         text not null check (enforcement in ('silent', 'warn', 'strict')),
    waits_between_steps boolean not null default false,
    rng_seed            bigint not null,                                    -- spec correction 12: the stored dice seed, written once at creation; no default, so omitting it fails loudly
    created_by          uuid not null references account(id),
    created_at          timestamptz not null default now()
);

create table team (
    id      uuid primary key,
    game_id uuid not null references game(id) on delete cascade,
    ordinal int  not null,
    unique (game_id, ordinal)
);

create table participant (
    id            uuid primary key,
    game_id       uuid not null references game(id) on delete cascade,      -- spec correction 4: team is nullable, so the game link is here
    team_id       uuid references team(id) on delete set null,
    account_id    uuid not null references account(id) on delete cascade,
    army_id       uuid references army(id) on delete set null,              -- spec correction 2: the lobby's pick, until the snapshot is written
    army_snapshot jsonb,
    unique (game_id, account_id)
);

create table table_state (
    id               uuid primary key,
    owner_id         uuid references account(id) on delete cascade,
    game_id          uuid references game(id) on delete cascade,
    name             text,
    width            real not null,
    length           real not null,
    texture_builtin  text,
    texture_asset_id uuid,        -- soft reference
    check ((owner_id is null) <> (game_id is null)),
    check ((texture_builtin is null) <> (texture_asset_id is null))
);

create table terrain_placement (
    id             uuid primary key,
    table_state_id uuid not null references table_state(id) on delete cascade,
    builtin        text,
    asset_id       uuid,          -- soft reference
    pos_x real not null, pos_y real not null, pos_z real not null default 0,
    yaw   real not null default 0,
    scale real not null default 1,
    check ((builtin is null) <> (asset_id is null))
);

create table terrain_volume (
    id                   uuid primary key,
    terrain_placement_id uuid not null references terrain_placement(id) on delete cascade,
    kind       text not null check (kind in ('solid', 'area')),
    footprint  text not null check (footprint in ('circle', 'rect')),
    dim_a real not null, dim_b real not null,
    offset_x real not null default 0, offset_y real not null default 0,
    yaw real not null default 0, bottom real not null default 0, top real not null,
    impassable boolean not null default false, blocks_los boolean not null default false, see_in_out boolean not null default false,
    cover boolean not null default false, difficult boolean not null default false, dangerous boolean not null default false,
    notes text
);

create table marker (
    id              uuid primary key,
    table_state_id  uuid not null references table_state(id) on delete cascade,
    kind            text not null check (kind in ('objective', 'token')),
    pos_x real not null, pos_y real not null, pos_z real not null default 0,
    yaw             real not null default 0,
    diameter        real not null,
    label           text,
    visual_builtin  text,
    visual_asset_id uuid,         -- soft reference
    check ((visual_builtin is null) <> (visual_asset_id is null))
);

create table deployment_zone (
    id             uuid primary key,
    table_state_id uuid not null references table_state(id) on delete cascade,
    style          text not null,   -- a zone shape id from Tabletop.Geometry.ZoneStyles, e.g. 'frontline/1'
    team_ordinal   int  not null
);

-- The game server's schema: one table, append-only. No FK across the boundary; account deletion is application logic.
create table game.game_event (
    game_id uuid   not null,
    seq     bigint not null,
    kind    text   not null,
    payload jsonb  not null,
    primary key (game_id, seq)
);
```

- [ ] **Step 5: Bring it up and verify from WSL**

```bash
cp -n .env.example .env
docker compose up -d && docker compose ps
docker compose exec postgres psql -U tabletop -d tabletop -Atc "select count(*) from information_schema.tables where table_schema in ('management','game')"
docker compose logs minio-init | tail -1
```

Expected: the count is `33` (32 management tables + `game.game_event`, matching `schema.md`'s thirty-three), and the last MinIO line is `bucket ready`. The count is **unchanged** by spec correction 12: `rng_seed` is a column on `management.game`, not a table. From Windows PowerShell: `Test-NetConnection localhost -Port 5432` and `Test-NetConnection localhost -Port 9000` both report `TcpTestSucceeded : True`.

- [ ] **Step 6: Commit**

```bash
git add docker-compose.yml .env.example scripts/postgres
git commit -m "Add the local development stack: Postgres, MinIO and the schema"
```

### Task 16: CLAUDE.md and the whole-tree check

**Files:**
- Modify: `CLAUDE.md` (Environment, Commands, Layout, Conventions)

**Interfaces:**
- Consumes: everything Tasks 1–15 produced — the three libraries, the three test projects, `docker-compose.yml`, `.env.example` and `scripts/postgres/init/001_schema.sql` — as the facts `CLAUDE.md` now records.
- Produces: no code. The whole-tree check of Step 2 is the plan's exit gate; `docs/architecture.md`'s matching `tests/` line is spec correction 9, which the coordinator makes.

- [ ] **Step 1: Edit `CLAUDE.md`**

In the opening paragraph, replace "Currently at scaffold stage — only the client exists." with "The client scaffold and the three `libs/` contracts exist; the servers and web app do not yet."

In **Commands**, add after the `dotnet build` line:

```bash
dotnet test                  # the three libs' xUnit projects under tests/
cp -n .env.example .env && docker compose up -d   # Postgres 17 (5432) and MinIO (9000/9001) in WSL
```

In **Layout**, change the first list to name a fourth kind alongside repo-level support: "**Repo-level support** — `docs/`, `scripts/`, `tests/`. Neither shipped nor consumed." and replace the `libs/` bullet with:

```
- `libs/` — `Tabletop.Geometry` (values, and the distance/LoS/settling API the geometry plan implements),
  `Tabletop.Content` (army snapshot and table state), `Tabletop.Protocol` (every socket message, the wire
  JSON, `EventApplier` and `Projection`). `net10.0`, Godot-free, persistence-free.
- `tests/` — one xUnit project per library: `Tabletop.Geometry.Tests`, `Tabletop.Content.Tests`,
  `Tabletop.Protocol.Tests`. `dotnet test` from the root.
```

Replace "`server/`, `web/` and `libs/` are empty or absent on purpose." with "`server/` and `web/` are empty on purpose."

In **Environment**, add: "Postgres and MinIO run in Docker inside WSL (`docker-compose.yml`, schema in `scripts/postgres/init/`); the Windows client reaches them and the servers over forwarded `localhost`. Ports: 5432, 9000/9001, management 5100, game server 5200."

In **Conventions**, replace the "No test project yet" bullet with: "Tests: xUnit under `tests/`, one project per library; test-driven for everything with logic. The Godot client is tested by running it."

- [ ] **Step 2: Run the whole check**

```bash
dotnet build && dotnet test && docker compose ps
git status --short
```

Expected: build clean, every test green, both containers `running (healthy)`, and no `obj/` outside `obj/Unix/` (`find libs tests -path '*/obj/*' -maxdepth 3 -type d | grep -v Unix` prints nothing).

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "Record the contracts, the test projects and the local stack in CLAUDE.md"
```

---

## Interfaces → Produces, per library

Later plans copy these names verbatim. `IRL<T>` abbreviates `IReadOnlyList<T>`, `IRD<K,V>` abbreviates `IReadOnlyDictionary<K,V>`.

### `Tabletop.Geometry` (namespace `Tabletop.Geometry`)

| Type | Members |
| --- | --- |
| `readonly record struct Point2(double X, double Y)` | |
| `readonly record struct Point3(double X, double Y, double Z)` | `Point2 Horizontal` |
| `readonly record struct Yaw(double Degrees)` | |
| `readonly record struct Orientation(double Pitch, double Yaw, double Roll)` | degrees; the convention: raw mesh coordinates read as table coordinates, rotated Pitch about X, then Roll about Y, then Yaw about Z (right-handed, extrinsic), then scaled; a Y-up glTF stands at Pitch = 90 — the web app writes it, the client's `MeshLoader.Orient` applies it |
| `readonly record struct TableSize(double Width, double Length)` | |
| `sealed record Polygon(IRL<Point2> Vertices)` | `bool Contains(Point2)` — geometry plan |
| `enum BaseShape { Circle, Oval, Square }` | |
| `readonly record struct Base(BaseShape Shape, double A, double B)` | `static Circle(double diameter)`, `static Oval(double length, double width)`, `static Square(double side)` |
| `readonly record struct ModelBody(Base Base, double Height)` | |
| `enum FootprintKind { Circle, Rect }` | |
| `readonly record struct Footprint(FootprintKind Kind, double A, double B)` | `static Circle(double radius)`, `static Rect(double width, double length)` |
| `enum VolumeKind { Solid, Area }` | |
| `[Flags] enum TerrainDeclarations { None=0, Impassable=1, BlocksLineOfSight=2, SeeInAndOut=4, Cover=8, Difficult=16, Dangerous=32 }` | |
| `sealed record Volume(VolumeKind Kind, Footprint Footprint, Point2 Offset, Yaw Yaw, double Bottom, double Top, TerrainDeclarations Declarations, string? Notes)` | |
| `readonly record struct Placement(Point3 Position, Yaw Yaw)` | |
| `readonly record struct TerrainTransform(Point3 Position, Yaw Yaw, double Scale)` | |
| `readonly record struct PlacedModel(ModelBody Body, Placement Placement)` | |
| `sealed record WorldVolume(Guid Id, Volume Volume, TerrainTransform Transform)` | |
| `static class Distance` | `double EdgeToEdge(PlacedModel, PlacedModel)`, `double EdgeToEdge(PlacedModel, Point2)` — geometry plan |
| `sealed record LineOfSightResult(bool Clear, IRL<Guid> ObstructedBy)` | |
| `static class LineOfSight` | `LineOfSightResult Check(PlacedModel from, PlacedModel to, IRL<WorldVolume> volumes)` — geometry plan |
| `static class Settling` | `double SurfaceHeightAt(Point2 point, IRL<WorldVolume> volumes)` — geometry plan, which adds `SurfaceHeightAt(Point3 from, IRL<WorldVolume>)` (drop from a height) |
| `static class Containment` | `IRL<Guid> VolumesContaining(PlacedModel model, IRL<WorldVolume> volumes)` — geometry plan |
| `static class Overlap` | `bool Bases(PlacedModel, PlacedModel)`, `bool WithSolid(PlacedModel, WorldVolume)` — geometry plan |

Zone style resolution is `Tabletop.Geometry.ZoneStyles` (geometry plan, Task 13): `ShapesOf(style)` is what `SetZoneStyle.Shapes` carries, `Resolve(shapeId, size)` is what the table view draws, `BaseFullyWithin` is what the server may check for the deploy Ask. The client shell plan consumes it. (Applied 2026-09-05 from the geometry plan's spec correction 1.) The table frame every value in this library uses is the corner frame: origin at a table corner, `0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up — the frame this plan's samples already use (compatibility ruling 2026-09-05).

### `Tabletop.Content` (namespace `Tabletop.Content`)

| Type | Members |
| --- | --- |
| `enum StatKind { TargetNumber, Distance, Count, DiceExpression }` | |
| `readonly record struct StatValue(StatKind Kind, string Text)` | `static StatValue Read(StatKind, string)` (throws `FormatException`), `int TargetNumber`, `double Inches`, `int Count` (throw `InvalidOperationException` on the wrong kind) |
| `sealed record PropertyInstance(string Name, int? Parameter)` | |
| `sealed record Weapon(Guid Id, string Name, IRD<string,string> Stats, IRL<PropertyInstance> Properties)` | |
| `sealed record ModelSlot(Guid Id, int Ordinal, string Name, IRD<string,string> Stats, IRL<PropertyInstance> Properties, Base Base, double Height, Guid? AssetId, IRL<Weapon> Weapons)` | |
| `sealed record Unit(Guid Id, int Ordinal, string Name, IRD<string,string> Stats, IRL<PropertyInstance> Properties, IRL<ModelSlot> Models)` | |
| `sealed record Army(Guid Id, string Name, string GameSystem, string Source, IRL<Unit> Units)` | |
| `sealed record ArmySnapshot(Guid Id, string Name, string GameSystem, IRL<Unit> Units)` | `static ArmySnapshot Of(Army, Guid id)` |
| `readonly record struct VisualRef` | `string? Builtin`, `Guid? AssetId`, `VisualRef(string?, Guid?)` (throws unless exactly one), `bool IsBuiltin`, `static OfBuiltin(string)`, `static OfAsset(Guid)` |
| `sealed record TerrainVolume(Guid Id, Volume Volume)` | |
| `sealed record TerrainPlacement(Guid Id, VisualRef Mesh, Point3 Position, Yaw Yaw, double Scale, IRL<TerrainVolume> Volumes)` | `TerrainTransform Transform`, `IRL<WorldVolume> WorldVolumes()` |
| `enum MarkerKind { Objective, Token }` | |
| `sealed record Marker(Guid Id, MarkerKind Kind, Point3 Position, Yaw Yaw, double Diameter, string? Label, VisualRef Visual)` | |
| `sealed record DeploymentZone(Guid Id, string Shape, int TeamOrdinal)` | |
| `sealed record TableState(Guid Id, string? Name, TableSize Size, VisualRef Texture, IRL<TerrainPlacement> Terrain, IRL<Marker> Markers, IRL<DeploymentZone> Zones)` | |
| `static class ContentJson` | `JsonSerializerOptions Options`, `JsonSerializerOptions Configure(JsonSerializerOptions)`, `string Serialize<T>(T)`, `T Deserialize<T>(string)` |

**`Properties` types are not in `libs/`.** Only the game server interprets the catalog, effects and conditions; management stores rows and serves `GET /v1/properties` as `(content_hash, name, game_rules)`, a DTO of its own. The game server plan owns the `Properties` types; the content import that fills the eight tables is undesigned (`architecture.md` → Rules as data).

### `Tabletop.Protocol` (namespace `Tabletop.Protocol`)

| Type | Members / kinds |
| --- | --- |
| `static class ProtocolVersion` | `const int Current = 1`, `const string Header = "Tabletop-Client-Version"`, `const int UpgradeRequiredStatusCode = 426` |
| `enum GameStatus { Lobby, Setup, Playing, Ended }` | wire: `lobby setup playing ended` |
| `enum SchedulerShape { PlayerTurn, AlternatingActivation }` | wire: `player-turn`, `alternating-activation` |
| `enum Enforcement { Silent, Warn, Strict }` | |
| `sealed record Team(Guid Id, int Ordinal)` | |
| `sealed record Participant(Guid Id, Guid AccountId, string Name, Guid? TeamId, Guid? ArmyId)` | |
| `sealed record GameInfo(Guid Id, string Name, GameStatus Status, Guid CreatedBy, string GameRules, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps, long RngSeed, IRL<Team> Teams, IRL<Participant> Participants)` | `RngSeed` is **positional**, the last scalar before the two collections; wire name `rngSeed` (camelCase policy, no attribute); column `management.game.rng_seed bigint not null`, no default. The stored dice seed: management generates it once at creation, the game server reads it with the rest of the row at hydration, the interpreter reads it instead of hashing `Id`, a simulator run sets it. |
| `sealed record ArmyInPlay(Guid ParticipantId, ArmySnapshot Snapshot)` | |
| `sealed record Board(TableState Table, IRL<ArmyInPlay> Armies)` | |
| `abstract record Location` | `OnTable(Placement Placement)` [table], `OnShelf` [shelf], `Inside(Guid ContainerId)` [inside] |
| `sealed record ModelRelocation(Guid ModelId, Location From, Location To)` | |
| `enum SubjectKind { Unit, Model }`; `sealed record Subject(SubjectKind Kind, Guid Id)` | |
| `sealed record Grant(Guid Id, Guid EffectId, string EntryName, Guid CasterUnitId, int RemainingUses)` | |
| `sealed record Die(int Raw, int Modified, bool Success)`; `sealed record RollModifier(string Source, int Amount)` | |
| `sealed record RollRecord(Guid Id, string Roll, int? Threshold, IRL<RollModifier> Modifiers, IRL<Die> Dice)` | |
| `sealed record SequenceContext(Guid? ActorUnitId, IRL<Guid> TargetUnitIds, IRL<Guid> WeaponIds, string? Action)` | |
| `abstract record StateChange : EventPayload` | `ModelsMoved(IRL<ModelRelocation> Moves)`, `WoundsSet(Guid ModelId, int Wounds)`, `ConditionSet(Subject Subject, string Condition, bool Present)`, `ActionRecorded(Guid UnitId, string Action)`, `ActionRecordCleared(Guid UnitId)`, `GrantAdded(Guid UnitId, Grant Grant)`, `GrantRemoved(Guid UnitId, Guid GrantId)`, `SpellTokensSet(Guid ModelId, int Tokens)`, `WoundsThisActivationSet(Guid UnitId, int Wounds)`, `ScoreSet(Guid TeamId, int Score)`, `MarkerPlaced(Marker Marker)`, `MarkerMoved(Guid MarkerId, Point3 Position, Yaw Yaw)`, `MarkerRemoved(Guid MarkerId)` — kinds are the snake_case of the type name |
| `abstract record EventPayload` | the StateChanges plus `RoundStarted(int Round, IRL<Guid> Order, IRL<StateChange> Changes)`, `TurnStarted(Guid TeamId)`, `TurnEnded(Guid TeamId, bool Passed, IRL<StateChange> Changes)`, `PhaseStarted(string Phase)`, `SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context)`, `StepCompleted(Guid FrameId, string Step, AskAnswer? Answer, IRL<RollRecord> Rolls, IRL<StateChange> Changes) { IRD<string,int>? Values { get; init; } }` (`values`, omitted when null), `SequenceFinished(Guid FrameId)`, `SequenceAbandoned(Guid FrameId)`, `AskOpened(Ask Ask)`, `AskAnswered(Guid AskId, Guid TeamId, AskAnswer? Answer, bool OutsideLegalSet)`, `AskClosed(Guid AskId, IRL<TeamAnswer> Answers)`, `DiceRerolled(Guid FrameId, Guid RollId, IRL<DieReplacement> Replaced)`, `DiceEdited(…same…)`, `ModifierAdded(Guid FrameId, string Roll, RollModifier Modifier)`, `Undone(long ToSeq)` — kinds snake_case of the type name |
| `sealed record TeamAnswer(Guid TeamId, AskAnswer Answer)`; `sealed record DieReplacement(int Index, Die Old, Die New)` | |
| `sealed record GameEvent(long Seq, Guid? Actor, EventPayload Payload, string? Warning)` | `string Kind` (JsonIgnore) |
| `static class EventKinds` | `IReadOnlyCollection<string> All`, `string Of(Type)`, `string Of(EventPayload)` |
| `sealed record Ask(Guid Id, string Question, string Prompt, IRL<Guid> Teams, LegalSet Legal, bool AllowPass, bool Sealed, string? WindowKind, Guid? FrameId)` | |
| `sealed record AskOption(string Id, string Label, IRL<Guid> Pieces)` | |
| `static class AskQuestions` | `Activate`, `ActOrEndPhase`, `Deploy`, `DeployScouts`, `DeployAmbusher`, `EndRound`, `Confirm`, `React`, `Bid` (snake_case strings) |
| `abstract record LegalSet` | `Options(IRL<AskOption> Choices)` [options], `PieceSelection(IRL<Guid> Eligible, int Min, int Max)` [pieces], `Allocation(IRL<Guid> Targets, int Amount)` [allocation], `Assignment(IRL<AskOption> Items, IRL<Guid> Targets, int MaxDistinctTargets)` [assignment], `MoveRegion(IRL<Guid> ModelIds, double Allowance, IRL<Guid> ImpassableVolumeIds, IRL<Guid> DifficultVolumeIds, double? DifficultCap, MoveContainment? Containment = null)` [move_region], `Bid(int MaxTokens)` [bid], `Confirmation` [confirmation] |
| `sealed record MoveContainment(IRL<Guid> Enterable, Guid? ExitingId, double? ExitWithin)` | not a union member; `MoveRegion.Containment`, null unless the move may embark or is disembarking |
| `abstract record AskAnswer` | `Chosen(string OptionId)`, `SelectedPieces(IRL<Guid> PieceIds)`, `Allocated(IRD<Guid,int> Counts)`, `Assigned(IRD<string,Guid> Targets)`, `Destinations(IRL<ModelRelocation> Moves)`, `BidPlaced(int Tokens)`, `Confirmed`, `Passed` — kinds snake_case of the type name |
| `abstract record Command` | `AnswerAsk(Guid AskId, AskAnswer Answer)`, `MoveModels(IRL<ModelRelocation> Moves)`, `PlaceMarker(Marker Marker)`, `MoveMarker(Guid MarkerId, Point3 Position, Yaw Yaw)`, `RemoveMarker(Guid MarkerId)`, `SetWounds(Guid ModelId, int Wounds)`, `SetCondition(Subject Subject, string Condition, bool Present)`, `RecordAction(Guid UnitId, string Action)`, `ClearActionRecord(Guid UnitId)`, `AddGrant(Guid UnitId, Grant Grant)`, `RemoveGrant(Guid UnitId, Guid GrantId)`, `SetSpellTokens(Guid ModelId, int Tokens)`, `SetWoundsThisActivation(Guid UnitId, int Wounds)`, `SetScore(Guid TeamId, int Score)`, `SetActiveTeam(Guid TeamId)`, `SetRound(int Round)`, `Undo(long ToSeq)`, `AbandonSequence(Guid FrameId)`, `RerollDice(Guid FrameId, Guid RollId, IRL<int> DiceIndices)`, `SetDice(Guid FrameId, Guid RollId, IRL<DieValue> Dice)`, `AddModifier(Guid FrameId, string Roll, int Amount)` — kinds snake_case of the type name |
| `sealed record DieValue(int Index, int Raw)` | |
| `sealed record Presence(Guid ParticipantId, Cursor? Cursor, Selection? Selection, IRL<PresencePlacement> Placements, Measurement? Measurement)` | |
| `sealed record Cursor(Point3 Point, Guid? Target)`; `sealed record Selection(Guid PieceId, IRL<Guid> ModelIds)`; `enum PlacementKind { Held, Proposed }`; `sealed record PresencePlacement(Guid PieceId, Point3 Position, Yaw Yaw, PlacementKind Kind)`; `sealed record Measurement(Point3 From, Point3 To)`; `sealed record Ping(Guid Sender, Point3 Point, Guid? Target)` | |
| `abstract record Edit`; `abstract record LobbyEdit : Edit`; `abstract record SetupEdit : Edit` | Lobby: `CreateTeam`, `PickTeam(Guid? TeamId)`, `PickArmy(Guid? ArmyId)`, `SetConfiguration(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps)`, `StartSetup`, `EndSetup`. Setup: `SetTableSize(TableSize Size)`, `SetTexture(VisualRef Texture)`, `AddTerrain(TerrainPlacement Placement)`, `MoveTerrain(Guid PlacementId, Point3 Position, Yaw Yaw)`, `ScaleTerrain(Guid PlacementId, double Scale)`, `RemoveTerrain(Guid PlacementId)`, `OverrideTerrainVolumes(Guid PlacementId, IRL<TerrainVolume> Volumes)`, `SwapTerrainMesh(Guid PlacementId, VisualRef Mesh)`, `AddSetupMarker(Marker Marker)`, `MoveSetupMarker(Guid MarkerId, Point3 Position, Yaw Yaw)`, `RemoveSetupMarker(Guid MarkerId)`, `SetZoneStyle(string Style, IRL<string> Shapes)`, `SwapZones(int TeamOrdinalA, int TeamOrdinalB)`, `ReplaceDraft(TableState Draft)` — kinds snake_case of the type name |
| `abstract record SocketMessage` | `Connect(string SessionJwt, Guid GameId, int ClientVersion)` [connect], `CommandMessage(Guid CommandId, Command Command)` [command], `EditMessage(Guid EditId, Edit Edit)` [edit], `Welcome(Guid ParticipantId, GameInfo Game)` [welcome], `GameInfoMessage(GameInfo Game)` [game], `BoardMessage(Board Board)` [board], `EventsMessage(IRL<GameEvent> Events)` [events], `AtRest(long Seq)` [at_rest], `PresenceMessage(Presence Presence)` [presence], `PingMessage(Ping Ping)` [ping], `PresenceEnded(Guid ParticipantId)` [presence_ended], `Refused(Guid MessageId, RefusalReason Reason, string Detail)` [refused], `Rejected(RejectReason Reason)` [rejected] — thirteen kinds |
| `enum RejectReason { UpgradeRequired, InvalidSession, NotAParticipant }`; `enum RefusalReason { OutsideLegalSet, Illegal, WrongStatus, NotPermitted, Unknown }` | |
| `static class WireJson` | `JsonSerializerOptions Options`, `string Serialize(SocketMessage)`, `SocketMessage Deserialize(string)`, `string Serialize<T>(T)`, `T Deserialize<T>(string)` |
| `sealed class Projection` | `static Initial(Board, GameInfo)`, `Board Board`, `IRD<Guid,UnitInPlay> Units`, `IRD<Guid,ModelInPlay> Models`, `IRD<Guid,Marker> Markers`, `IRD<Guid,int> Scores`, `SchedulerPosition Scheduler`, `IRL<Frame> Stack`, `IRD<Guid,OpenAsk> OpenAsks`, `long LastSeq` |
| `sealed class UnitInPlay` | `Guid Id`, `Guid ParticipantId`, `Unit Definition`, `IReadOnlySet<string> Actions`, `IReadOnlySet<string> Conditions`, `IRL<Grant> Grants`, `int WoundsThisActivation` |
| `sealed class ModelInPlay` | `Guid Id`, `Guid UnitId`, `ModelSlot Slot`, `Location Location`, `int Wounds`, `IReadOnlySet<string> Conditions`, `int SpellTokens` |
| `sealed class SchedulerPosition` | `int Round`, `IRL<Guid> Order`, `Guid? ActiveTeamId`, `string? Phase`, `IReadOnlySet<Guid> Passed` |
| `sealed class Frame` | `Guid Id`, `string Sequence`, `SequenceContext Context`, `IRL<CompletedStep> Steps`, `IRD<string, IRL<RollModifier>> PendingModifiers` |
| `sealed class CompletedStep` | `string Step`, `AskAnswer? Answer`, `IRL<RollRecord> Rolls`, `IRL<StateChange> Changes`, `IRD<string,int>? Values` |
| `sealed class OpenAsk` | `Ask Ask`, `IRD<Guid, AskAnswer?> Answers` |
| `sealed class EventApplier(Board, GameInfo)` | `Projection Projection`, `IRL<GameEvent> Log`, `void Apply(GameEvent)`, `void ApplyAll(IEnumerable<GameEvent>)`, `Projection ProjectTo(long seq)`, `static IRL<GameEvent> EffectiveLog(IRL<GameEvent>)`, `static void ApplyTo(Projection, GameEvent)`, `static void ApplyChange(Projection, StateChange)` |
| `sealed class ProjectionDesyncException(string kind, Guid unknownId) : Exception` | `string Kind`, `Guid UnknownId`. Thrown by `Apply`, `ApplyAll`, `ApplyTo` and `ApplyChange` when an event names an id the projection does not hold. It means the projection is unusable and must be rebuilt: the client drops it, reconnects and replays the log; the game server faults the session (owner protocol ruling 2 of 2026-09-06) |

### Local development

| Produced | Value |
| --- | --- |
| Postgres | `localhost:5432`, db `tabletop`, user `tabletop`, schemas `management`, `game`; `TABLETOP_DB_CONNECTION` |
| MinIO | S3 API `http://localhost:9000`, console `:9001`, bucket `tabletop-blobs`, path-style; `TABLETOP_S3_ENDPOINT`, `TABLETOP_S3_BUCKET`, `TABLETOP_S3_ACCESS_KEY`, `TABLETOP_S3_SECRET_KEY` |
| Schema | `scripts/postgres/init/001_schema.sql`, applied on first volume creation; includes `management.game.rng_seed bigint not null` (spec correction 12); the management API plan embeds the same file as DbUp migration 001, baselines a compose-built database, and owns every later migration (`002_blob_uploaded.sql` onward); the game server never migrates |
| Reserved ports | management `5100`, game server `5200`; both bind `0.0.0.0` so Windows reaches them at `localhost` |

## Every kind, and the spec it comes from

| Kind | Type | Source |
| --- | --- | --- |
| **Events** | | |
| `round_started`, `turn_started`, `turn_ended`, `phase_started` | scheduler events | scheduler → Events; `turn_ended.passed` from whole-design review → The scheduler's pass |
| `sequence_started`, `step_completed`, `sequence_finished`, `sequence_abandoned` | stack events | action sequences → The resolution stack, Abandonment |
| `ask_opened`, `ask_answered`, `ask_closed` | Ask events | action sequences → Asks, Windows (pass logged; sealed reveal); client shell → the projection holds the current Asks (spec correction 3) |
| `dice_rerolled`, `modifier_added` | | resolution → Players roll their own dice |
| `dice_edited` | | scheduler → Deployment ("every roll result is editable from the menu") |
| `undone` | | `architecture.md` → Undo; client shell → undo is local first |
| `models_moved` | StateChange | table domain model → The one wire decision (start and stop); game flow → The shelf, Inside a container (one kind of event for every relocation) |
| `wounds_set`, `condition_set`, `action_recorded`, `action_record_cleared` | StateChange | game flow → What a unit in play records, Nothing is an invariant |
| `grant_added`, `grant_removed` | StateChange | spell effects → The grants store |
| `spell_tokens_set` | StateChange | action sequences → The token economy |
| `wounds_this_activation_set` | StateChange | whole-design review → Gaps filled (a store that outlives the frame) |
| `score_set` | StateChange | game flow → Scoring |
| `marker_placed`, `marker_moved`, `marker_removed` | StateChange | setup content → Markers (tokens in play), whole-design review → Tokens had no home in the log |
| **Commands** | | |
| `answer_ask` | | action sequences → Asks; scheduler → The turn is an Ask (activate, pass) |
| `move_models` | | client shell → Input and presence (the drop sends one move command) |
| `place_marker`, `move_marker`, `remove_marker` | | setup content → Markers |
| `set_wounds`, `set_condition`, `record_action`, `clear_action_record`, `add_grant`, `remove_grant`, `set_spell_tokens`, `set_wounds_this_activation`, `set_score`, `set_active_team`, `set_round` | menu | client shell → The log, undo and the menu; scheduler → Nothing is an invariant |
| `undo` | | client shell → Undo is local first |
| `abandon_sequence` | | action sequences → Abandonment |
| `reroll_dice`, `add_modifier` | | resolution → Players roll their own dice |
| `set_dice` | | scheduler → Deployment (roll results editable) |
| **Asks** (question → legal set) | | |
| `activate` → `options` (unit × action), pass allowed | | scheduler → The turn is an Ask |
| `act_or_end_phase` → `options` | | scheduler → The two shapes (player-turn) |
| `deploy`, `deploy_scouts` → `pieces` | | scheduler → Deployment |
| `deploy_ambusher` → `pieces`, pass allowed | | scheduler → The round boundary |
| `confirm` → `confirmation` | | action sequences → One roll primitive (the confirm gate) |
| `react` → `options`, pass allowed, `WindowKind` set | | action sequences → Windows |
| `bid` → `bid`, `Sealed` | | action sequences → Windows (sealed simultaneous variant) |
| ruleset questions → `options` / `pieces` / `allocation` / `assignment` / `move_region` | e.g. shoot or not, strike back, remove casualties, weapon groups and targets, charge target, move destination, embark, disembark, spell and tokens, spell targets, consolidation | action sequences → Grimdark Future's sequences; spell effects → Target selection; game flow → Inside a container (`move_region.containment`); `architecture.md` → Continuous actions (move parameters) |
| **Setup edits** | | |
| `create_team`, `pick_team`, `pick_army`, `set_configuration` | LobbyEdit | whole-design review ruling 4 and 5; setup phase → Joining, The lifecycle |
| `start_setup`, `end_setup` | LobbyEdit | setup phase → The lifecycle (creator only) |
| `set_table_size`, `set_texture` | SetupEdit | setup phase → Editing is free for all |
| `add_terrain`, `move_terrain`, `scale_terrain`, `remove_terrain`, `override_terrain_volumes`, `swap_terrain_mesh` | SetupEdit | setup phase → Editing is free for all, Terrain: imported, built-in, generated |
| `add_setup_marker`, `move_setup_marker`, `remove_setup_marker` | SetupEdit | setup phase → Editing is free for all |
| `set_zone_style`, `swap_zones` | SetupEdit | setup phase → Zones |
| `replace_draft` | SetupEdit | setup phase → Layouts |
| **Presence** | | |
| `presence`, `ping`, `presence_ended` | SocketMessage | presence → The presence value, A ping is silent; `presence_ended` is owner protocol ruling 1 of 2026-09-06, confirming question 1's assumption |
| **Envelope** | | |
| `connect`, `welcome`, `game`, `board`, `events`, `command`, `edit`, `refused`, `rejected` | SocketMessage | client shell → The connection and the projection; management API → The version check (`rejected: upgrade_required`); whole-design review ruling 1 (`refused` under strict) |
| `at_rest` | SocketMessage | owner protocol ruling 4 of 2026-09-06, replacing the simulator's 250 millisecond quiet window |

---

## Spec corrections proposed

1. **`docs/schema.md` → "A terrain placement names a built-in or an asset"** and **`docs/superpowers/specs/2026-08-29-setup-content-design.md` → Terrain ("A placement may override them")**. Current wording (schema): *"For a built-in placement they are **the** volumes, not overrides: there is no `asset_volume` row beneath, so they are always written on add."* Replace with: *"A placement's `terrain_volume` rows are always written on add — copied from the built-in's shipped volumes or from the asset's `asset_volume` rows — so the draft and snapshot are self-contained and the game server never reads `asset_volume`. Editing them in place is the override; 'clearing' re-copies the source's volumes."* Reason: the client and game server need every placed volume (settling, LoS, the move Ask's impassable ids), the asset's volumes are owner-only over HTTP, and the manifest does not carry them.
2. **`docs/schema.md` → `PARTICIPANT`** and **`docs/data-model.md` → `PARTICIPANT`**. Add column `army_id uuid null references army(id) on delete set null` with the note: *"The lobby's army pick, so a dead task recovers it with the rest of the game's rows; `army_snapshot` is written from it when setup ends."* Reason: the setup phase writes the snapshot "from their army as it stands" at end-setup, but no row held which army was picked.
3. **`docs/superpowers/specs/2026-09-03-action-sequences-design.md` → The resolution stack** ("Four event kinds carry it") and **`2026-09-04-client-shell-design.md` → The connection and the projection**. Add after the four kinds: *"Three more carry Asks: `ask_opened` (team(s), question, legal set), `ask_answered` (team, answer — withheld on the stream for a sealed window still open — and whether it was outside the legal set), `ask_closed` (the revealed answers). The projection's 'current Asks' are rebuilt from these; a `sequence_abandoned` is preceded by `ask_closed` for any Ask the popped frames held."* Reason: two sentences already require it and neither spec lists the kinds. The client shell's projection *"holds the current Asks"* and is built by applying every event, and a reconnect rebuilds it *"from snapshot and log"* — an Ask outside the log would not survive one. Action sequences → Asks says *"any team member may [answer], and the log records who did"*, and a window's pass is *"a real answer, logged"*, which is `ask_answered` and nothing else.
4. **`docs/schema.md` → `PARTICIPANT`** and the diagram. Add `game_id uuid not null references game(id) on delete cascade`, and `unique (game_id, account_id)`. Reason: ruling 5 made `team_id` nullable, which left a team-less participant attached to no game.
5. **`docs/schema.md` → "Still open" → "Whether the two schemas are two Postgres schemas in one database or two databases."** Strike, with: *"Settled 2026-09-05 for local development by the contracts plan: two Postgres schemas, `management` and `game`, in one database. Hosting may revisit."*
6. **`docs/schema.md` → `EFFECT`**. Add `target_stat_level text` beside `target_stat`, with: *"Required with `target_stat`: `stat_definition`'s key is `(properties_hash, level, name)`, so the level is part of the reference."* Reason: the FK the section promises cannot exist without it.
7. **`docs/superpowers/specs/2026-09-04-setup-phase-design.md` → Zones.** Current: *"Setting a zone style creates its zones and assigns them to team ordinals in order."* Add: *"The client sends the style's zone shape ids with the edit — `Tabletop.Geometry.ZoneStyles.ShapesOf(style)`, the inventory both ends reference — and the server writes one `deployment_zone` row per shape keyed by team ordinal `1..n` whether or not a team with that ordinal exists yet; swapping two teams' zones swaps the two ordinals' shapes. `deployment_zone.style` holds a shape id, namespaced by style (`frontline/1`)."* (Aligned 2026-09-05 with the game server plan's spec correction 4 and the geometry plan's Task 13.)

   **This does not reopen ruling 5.** The setup phase and [the whole-design review](../specs/2026-09-04-whole-design-review.md) ruling 5 both say *"zones re-derive from the zone style whenever the team list changes"*, and that still holds: a style's shape list is a property of the style, not of the team list (`ShapesOf` takes only the style — two shapes for the six two-team styles, four for free-for-all), so the rows written at `set_zone_style` already cover every ordinal the style has and there is nothing left for a change of team list to re-derive. Ruling 5's second half is unchanged too: a team whose ordinal is past the style's shape count — including one created after `playing` — has no zone and nothing to deploy.
8. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → Input and presence.** Current: *"The Ask's answer returns the allowance and the impassable volume ids."* Replace with: *"Answering the activate Ask starts the action's sequence; its move step opens a `move_region` Ask carrying the allowance and the impassable volume ids, and the answer is the destinations."*
9. **`docs/architecture.md` → Repository layout** and **`CLAUDE.md` → Layout.** Add `tests/` to repo-level support: *"`tests/` — xUnit projects, one per library."* (CLAUDE.md is edited by Task 16; architecture.md is the coordinator's.)
10. **`docs/architecture.md` → What is actually shared**, the `Tabletop.Content` row. Current contents: *"Army, unit, model slot, weapon types"*. Replace with: *"Army, unit, model slot and weapon types, and the table state — the board, terrain placements, markers and deployment zones — which is one record with two owners: a user's saved layout in management, and a game's setup draft and snapshot on the game server."* Reason: the row's own justification is *"Content is shared because the client renders armies and both servers store and interpret them"*, and the table state is shared by exactly the same three — management stores it as a layout ([the management API](../specs/2026-08-29-management-api-design.md) → `/v1/layouts`), the game server writes it as the draft ([the setup phase](../specs/2026-09-04-setup-phase-design.md) → The draft is the row), and the client saves, loads and draws it. It was written before setup content existed, and `architecture.md` is corrected by a later spec rather than the reverse.

11. **`docs/superpowers/specs/2026-08-28-game-flow-design.md` → Inside a container**, after *"Disembarking is the operation that already exists: give a model a placement."* Add: *"What does not fall out is **where** a placement is legal, and which containers a move may enter. The wire carries the destination (`Inside`, in a `Destinations` answer) but not the offer, so the move Ask carries it: `move_region.containment` names the containers a model of the unit could reach — one reaching model embarks the whole unit — or the container it is inside with the 6\" its models must be placed fully within of. Enterable and exiting are never both set, which is where *"never both in one activation"* is enforced."* Reason: the section designs containment as a location and states the rules half, but no plan could offer either move without a field on the Ask (the coverage gap of 2026-09-06).

12. **`docs/schema.md` → the `GAME` entity block in the diagram**, and a new prose section beside **"A game has a name, an enforcement setting and a wait setting"**. Add column `rng_seed bigint not null` after `waits_between_steps`, and add the section:

    > ### A game stores its dice seed
    >
    > **Added 2026-09-06 by owner ruling 1.** `rng_seed` is the seed every roll in the game is drawn from. Management generates it once, in application code, when it inserts the `game` row — any value in `bigint`'s range is valid — and nothing ever writes it again. The game server reads it with the rest of the game's row at hydration and hands it to the interpreter, which stops deriving a seed from `game.id`.
    >
    > It is a **stored** column rather than a value derived from `id` because the two things a seed has to do pull apart otherwise. Undo and replay need the seed to be *fixed for the life of the game*, which a hash of the id gives; a simulator run needs it to be *chosen*, which a hash of the id does not, and the only way to choose one is then to choose the id — which is why the simulation plan had `SimIds.Of` deriving ids from a run seed and an exception to the UUIDv7 rule. A stored column gives both: fixed after creation, free at creation. The UUIDv7 exception is withdrawn, and a simulator run generates ordinary `Guid.CreateVersion7()` ids and sets `rng_seed` explicitly.
    >
    > It is not carried in the first event either. The seed has to be readable *before* the log is interpreted at all — the interpreter needs it to construct the `Roller` for the very first roll, and the game server hydrates `GameInfo` from the game's row before it reads a single `game_event` row. Putting it in an event would also make it undoable in principle and would give a game in `lobby` or `setup`, which has no events, no seed; and `game_event` belongs to the `game` schema while the value is written by management, which writes no events. There is one stored seed per game, written once at creation.
    >
    > No database default. Postgres 17 could fill it (`random(min, max)`), but then a management bug that omits the seed still produces a playable game and the write-once invariant lives in the database, where this plan's tests do not look; the schema's standing convention is that generated values come from application code, as ids do. `not null` with no default fails loudly instead. The table count is unaffected — this is a column, not a table.

13. **`docs/superpowers/specs/2026-08-30-presence-design.md` → the open item on how a client is told that a presence value has stopped, versus timing it out.** Strike it, with: *"**Answered 2026-09-06 by owner protocol ruling 1.** The server sends an explicit `PresenceEnded(ParticipantId)` frame when a participant's last socket closes, and no client owns a presence timeout. Departure is a fact the server knows exactly; a client inferring it from silence has to pick a timeout and will get it wrong on a slow network."* Reason: the question was this plan's open question 1, the assumption it took is confirmed, and the item is the last thing leaving the mechanism ambiguous for the client shell.

## Interfaces needed from other modules

None consumed. Obligations this plan places on later plans, stated as the signatures they must honour:

- **Geometry plan**: implement every body in `libs/Tabletop.Geometry/Api.cs` and `Polygon.Contains` without changing a signature; replace `ApiSurfaceTests` with behaviour tests.
- **Every plan that constructs `GameInfo`**: `RngSeed` is positional and last among the scalars, so every construction supplies it. See the note under *Spec corrections proposed* 12 for why it is positional rather than an init-only property: an init-only property would leave the six consuming plans' constructions compiling untouched, but a construction that forgot the seed would silently get `0`, and a `0` seed is a real game whose dice match every other game that forgot. Nothing is implemented yet, so the compile error costs a text edit in each plan and buys the guarantee that no game can be built without a seed.
- **Game server plan**: read `game.rng_seed` with the rest of the game's row at hydration and pass it into `GameInfo.RngSeed`; never write it. Also emit `ask_closed` before `sequence_abandoned` for any open Ask on the popped frames; write `GameEvent.Kind` (from `EventKinds.Of`) to `game_event.kind` and `WireJson.Serialize(GameEvent)` to `payload`; refuse `Undo(ToSeq)` unless `ToSeq` is `0` or the seq of a live event; send `BoardMessage` with the whole draft after each setup edit and `GameInfoMessage` after each lobby edit; filter `ask_answered.Answer` to null for teams a sealed window withholds from; own the `Properties` catalog types. Send `AtRest(Seq)` to every socket on the game once it has finished processing an inbound `connect`, `command` or `edit`, after the last frame that processing produced, with `Seq` the last sequence number in the log and no frame for a relayed `presence` or `ping`. Fault the session when its own `EventApplier` throws `ProjectionDesyncException`, rather than resyncing: it is applying events it wrote itself, so a rebuild throws again at the same event.
- **Management API plan**: write `rng_seed` on `POST /v1/games`, generated in application code (`Random.Shared.NextInt64()` is enough — the seed is not a secret, since editing dice is already a first-class command) and never updated; return it in the game representation the game server and client read. Adopt `scripts/postgres/init/001_schema.sql` as migration 001 and pick the migration tool; read `TABLETOP_*` variables from `.env.example`; answer `ProtocolVersion.UpgradeRequiredStatusCode` when `ProtocolVersion.Header` is below the minimum; mint presigned URLs against `TABLETOP_S3_ENDPOINT` with path-style addressing.
- **Interpreter plan**: set `GameState.RngSeed` from `GameInfo.RngSeed` — `unchecked((ulong)info.RngSeed)` keeps `GameState.RngSeed : ulong`, so `IDiceSource.Draw(ulong seed, …)`, `Roller` and SplitMix64 are untouched — and delete the SHA-256-of-`Info.Id` derivation and the test that asserts two game ids give two seeds.
- **Simulation plan**: set `GameInfo.RngSeed` from the run seed and generate ids with `Guid.CreateVersion7()`; `SimIds.Of` and the UUIDv7 exception are withdrawn. Wait for `at_rest` as the end of a step and delete the 250 millisecond quiet window: a socket run's pacing is the frame, not a timer.
- **Viewer plan**: ignore `at_rest`, and ignore any other `SocketMessage` kind it does not handle without faulting. It reads the log and has nothing to do at a rest point.
- **Client shell plan**: catch `ProjectionDesyncException` around every `Apply`, `ApplyAll` and `ProjectTo` call, drop the projection, reconnect and rebuild from the log — never skip the event and carry on; ignore `at_rest`, and any other kind it does not handle, without faulting. Add `ProjectReference`s from `client/Tabletop.csproj` to the three libraries and confirm the Godot-side build creates `obj/Windows_NT/` for them; consume `Tabletop.Geometry.ZoneStyles` for zone-style resolution (`ShapesOf` into `SetZoneStyle.Shapes`, `Resolve` to draw) and `Tabletop.Geometry.Table.ContainsPoint` as the on-slab check — the library answers in the wire frame, so the client converts nothing; render `MoveRegion.Containment` in the move Ask — a click on a listed `Enterable` container answers `Destinations` with `Inside(container)` for every model, and a non-null `ExitingId` draws the exit region with `Tabletop.Geometry.Containment.BaseFullyWithin` instead of `Movement.Region` and drags the occupants out of the container's model node.

## Questions for the project owner

**None open.**

1. ~~**How is a departed presence signalled?**~~ **Answered 2026-09-06 by owner protocol ruling 1.** The server sends `PresenceEnded` when a participant's last socket closes. The assumption this plan took is confirmed: the frame stays in `SocketMessage`, no mechanism changes, and no client owns a presence timeout. Spec correction 13 strikes presence's open item.

### Answered from the specs, 2026-09-05 (design review)

Three questions this plan carried are settled by documents it already cites, and are struck rather than asked:

- ~~**Should Asks be events in the log?**~~ **Yes.** [The client shell](../specs/2026-09-04-client-shell-design.md) → The connection and the projection puts *"the current Asks"* in the projection and builds that projection by *applying every event*, and says a reconnect *"is discarded and rebuilt from snapshot and log"* — so an Ask that is not in the log cannot survive a reconnect. [Action sequences](../specs/2026-09-03-action-sequences-design.md) → Asks adds the other half: *"any team member may [answer], and the log records who did"*, and a window's pass is *"a real answer, logged"*. Spec correction 3 is how that lands in the two specs that list the log's event kinds without them.
- ~~**Is `TableState` in `Tabletop.Content` acceptable?**~~ **Yes**, by `architecture.md`'s own rule for that table — Content is what the client and both servers share — and no design doc says otherwise. Its row predates [setup content](../specs/2026-08-29-setup-content-design.md); spec correction 10 updates it, per the standing rule that a design doc beats `architecture.md`.
- ~~**Do rulesets name their Ask questions as free strings?**~~ **Yes.** [Identity and enumerations](../specs/2026-08-29-identity-and-enumerations-design.md) → The closed sets settles the identical case: *"Actions are named per ruleset rather than enumerated … because a ruleset with an action nobody anticipated needs content, not a code change."* An Ask question is supplied by `GameRules` for the same reason ([action sequences](../specs/2026-09-03-action-sequences-design.md) → constraint 9, *"who answers an Ask differs per ruleset"*), and only OPR's `GameRules` is built, so a second ruleset must not be a Protocol change. What stays closed is the shape the client renders — `LegalSet` and `AskAnswer` — which is what makes the free string safe.
