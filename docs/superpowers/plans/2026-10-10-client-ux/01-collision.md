# Physical collision Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nothing on the table is inside anything else. A model or terrain piece rests on the highest Solid top under any
part of it, slides along what it cannot enter, and a group compresses against an obstacle; the server rests every
placement it stores and judges p.7's three contact clauses under the enforcement setting; hills are drawn flat-topped.

**Architecture:** `Tabletop.Geometry.Collision` (rest, slide, separate) is the one implementation, shared by the client's
drags, the server's placements and the sim's answers; it replaces `Settling` everywhere. The server's `Battlefield.Settle`
rests a body over its base and ignores the client's Z; `GfMoves.ContactBreach` names `move_overlap`, `move_through_models`
and `move_separation`. `MoveRegion` carries the separation distance and whether the unit passes through models, so neither
the client nor the sim needs a rule name. The hill meshes come from a generator under `scripts/`.

**Tech Stack:** `net10.0`, C# 12, xUnit, Godot 4.7.2 Mono (C#); glTF 2.0 binary; the viewer's generated TypeScript.

**Spec:** [`docs/superpowers/specs/2026-10-10-client-ux-and-themes-design.md`](../../specs/2026-10-10-client-ux-and-themes-design.md),
section *Physical collision*, and its *Testing* lines for geometry and server.

**Requires landed:** `main` at `7c4ec5d`.

## Global Constraints

- One world unit is one inch; the table frame's origin is the table's corner, Z up, everywhere. `WorldFrame` converts for Godot.
- C# only, no GDScript. Test-driven, xUnit under `tests/`.
- A rule is named only in `server/game/Rules/GrimdarkFuture/`. `Tabletop.Geometry` names no rule: constants arrive as parameters.
- `client/src/Core/` is Godot-free; `./scripts/check-client` prints nothing (no physics body, no rules type).
- Dev tooling stays out of `client/`: the editor scans everything under the project root.
- Every wire change is additive; `ProtocolVersion.Current` does not change. After it:
  `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then `cd viewer && npm run check && npm test`.
- `.tscn` and `.import` files are editor-owned; never hand-write one.
- Every file stays under 500 lines; `GfMoves.cs` (470) gains nothing, the new breaches go in a new partial.
- Rulebook p.7: *"Models may never be within 1” of models from other units, unless they are taking a Charge action, and may
  never move through other models or units (friendly or enemy), even if they are taking a Charge action."* / *"or be placed in
  physically impossible locations."* p.13 Flying: *"may move through units and terrain"*; Aircraft *"ignore all units and terrain
  when moving and stopping"*. p.9: the target unit's models *"must move by up to 3” to get into base contact"*.
- Server and sim socket tests need `docker compose up -d`. Never `docker compose down -v`.
- Commit after every task; message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and
  `Claude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW`.

## Rulings this plan makes

- **What a body slides along.** The spec's test (a Solid whose top is above the mover's rest height plus its height) stops a
  1.5" model (`ArmyForgeImport.HeightFromBase`, 25 mm) at a 2" hill, against the spec's own *"a model dragged onto a hill
  stands on it"*. A body slides along the Solid volumes its caller names as walls: during a move Ask the Ask's
  `ImpassableVolumeIds`, otherwise every Solid declared `Impassable`. Every other Solid is climbed (*"no step-up limit"*).
- **Elevated built-ins.** p.11: *"Terrain features that are over 3” tall count as elevated terrain, and are impassable"*. The
  built-in `large-sight-blocker` (6") declares it (`BuiltinContent`, 01b Task 8 Step 0), so a drag stops at its sides.
  The built-in `ruin` becomes an Area (owner ruling: ruins are passable and Difficult; seen into and out of, not through),
  so models walk into it rather than onto a 4" block. Uploaded terrain keeps the
  declarations its author gives it; the client holds no 3" constant.
- **Storeys.** `RestHeight` has no from-height, so a model under a raised Solid (an asset ruin's floor) rests on its top.
  Every built-in is one volume from Z 0; only uploaded multi-volume assets meet this.
- **Through.** `move_through_models` sweeps the base against models the answer does not move, less those it already overlaps.
  The moving unit's own models are judged at their destinations (`move_overlap`), not along the path.
- **Separation scope.** `MoveRegion.Separation` is 1 on every move except the Charge move, the close-in and consolidation (all
  inside the Charge action, p.7, p.9, p.10) and an Aircraft's move (p.13); a disembark keeps it.
- **Terrain rest.** A setup edit rests the edited piece only; a piece left over a removed support keeps its height.

## Review Focus

1. **Touching an edge is standing on it.** A base flush against a building's side must stay on the table, not jump to the roof.
   Test `Touching_a_side_is_not_standing_on_it` (Task 1).
2. **A long drag frame tunnels through a thin wall.** One frame can carry the cursor 10"; a 0.5" wall must still stop it.
   Test `A_long_drag_does_not_tunnel_through_a_thin_wall` (Task 2).
3. **A body that starts inside something can never move.** A model left overlapping under `silent` must be able to leave.
   Tests `A_body_starting_inside_an_obstacle_can_leave` (Task 2), `Leaving_a_model_it_overlaps_is_not_moving_through_it` (Task 6).
4. **A wrapper drops the new hook.** `WithSequences` must forward `PlacementBreach`, or a wrapped ruleset accepts any overlap.
   Test `A_wrapped_Grimdark_Future_still_judges_a_placement_overlap` (Task 6).
5. **The sim's answers start breaching.** Every sim scenario defaults to `strict`; a sampled move into another base or within
   1" of an enemy is refused and the run sticks. Test `Sampled_moves_never_breach_contact` (Task 10), and Task 12's suite runs.

## Files

Tasks 1–3 are below; 4–7 are in [`01a-collision-server.md`](01a-collision-server.md), 8–12 in
[`01b-collision-client.md`](01b-collision-client.md). Tasks run in order; 4 and 7 may run beside 3.

| Task | Creates | Modifies |
| --- | --- | --- |
| 1 | `libs/Tabletop.Geometry/Collision.cs`, `tests/Tabletop.Geometry.Tests/CollisionTests.cs` | — |
| 2 | `libs/Tabletop.Geometry/Collision.Slide.cs`, `tests/Tabletop.Geometry.Tests/SlideTests.cs` | — |
| 3 | — | `libs/Tabletop.Geometry/Movement.cs`, `tests/Tabletop.Geometry.Tests/MovementTests.cs` |
| 4 | `tests/Tabletop.GameServer.Tests/RestingTests.cs` | `server/game/Interpreter/Battlefield.cs`, `GfMoves.cs`, `GfMoves.Terrain.cs`, `GfMelee.cs`, `server/game/Dispatch/MenuTranslation.cs`, `server/game/Setup/DraftEditor.cs` |
| 5 | `tests/Tabletop.GameServer.Tests/GfContactTests.cs` | `libs/Tabletop.Protocol/Asks.cs`, `GfMoves.MoveStep.cs`, `GfMoves.Terrain.cs`, `GfMoves.cs`, `GfContainers.cs`, viewer generated types, `tests/Tabletop.Protocol.Tests/AskTests.cs` |
| 6 | `server/game/Rules/GrimdarkFuture/GfMoves.Contact.cs` | `GfNames.cs`, `GfRules.Moves.cs`, `GfMoves.cs` (summaries), `server/game/Rules/IGameRules.cs`, `server/game/Dispatch/HandMoves.cs`, `tests/.../Infrastructure/StubRules.cs`, `GfContactTests.cs`, `HandMovesTests.cs` |
| 7 | `scripts/TerrainMeshes/`, `scripts/terrain-meshes`, `tests/Tabletop.TerrainMeshes.Tests/` | `Tabletop.sln`, `client/content/builtin/terrain/hill.glb`, `client/src/Core/BuiltinContent.cs` |
| 8 | `client/src/Core/DragBodies.cs`, `tests/Tabletop.Client.Tests/DragBodiesTests.cs` | `client/src/Core/MoveRuler.cs` |
| 9 | — | `client/src/Table/TableInput.cs`, `TableInput.Hold.cs`, `client/src/Hud/DecisionBox.cs`, `DecisionBox.Move.cs` |
| 10 | `tests/Tabletop.Sim.Tests/ContactAnswerTests.cs` | `sim/Answers/PolicyView.cs`, `sim/Answers/AnswerPolicy.cs`, `sim/Run/HandPlacement.cs`; deletes `Settling.cs`, `SettlingTests.cs` |
| 11 | — | `docs/architecture.md`, `docs/design/**`, the `AGENTS.md` files |
| 12 | — | — (validation) |

`GfMoves*.cs`, `GfMelee.cs`, `GfContainers.cs`, `GfNames.cs`, `GfRules.Moves.cs` are under `server/game/Rules/GrimdarkFuture/`.

---

### Task 1: Rest heights

**Model:** sonnet

**Files:** create `libs/Tabletop.Geometry/Collision.cs`, `tests/Tabletop.Geometry.Tests/CollisionTests.cs`.

**Interfaces:**
- Consumes: `Outline.InteriorsIntersect`, `WorldVolumes.WorldOutline/WorldTop/WorldBottom`, `Bodies.BaseOutline`, `Fx` (tests).
- Produces: `public static partial class Collision` with `double RestHeight(PlacedModel model, IReadOnlyList<WorldVolume> volumes)`,
  `double RestHeight(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> volumes)` (volumes whose id is in `piece` are
  ignored), `double RestHeight(Point2 point, IReadOnlyList<WorldVolume> volumes)` (markers), and
  `internal static double Rest(IReadOnlyList<Outline> outlines, IReadOnlyList<WorldVolume> volumes, bool floors)`.

- [ ] **Step 1: Write the failing test** — `tests/Tabletop.Geometry.Tests/CollisionTests.cs`:

```csharp
namespace Tabletop.Geometry.Tests;

/// <summary>Client UX spec → Physical collision: rest on the highest Solid top under any part of the body, else 0.</summary>
public class CollisionTests
{
    static readonly WorldVolume Hill = Fx.Box(12, 12, 6, 6, 0, 2);                       // x and y 9..15, top 2

    [Fact] public void Open_table_is_zero() => Assert.Equal(0, Collision.RestHeight(Fx.Model(30, 30), [Hill]));
    [Fact] public void A_base_half_over_a_hill_stands_on_it() => Assert.Equal(2, Collision.RestHeight(Fx.Model(15.2, 12), [Hill]), 6);
    [Fact] public void Touching_a_side_is_not_standing_on_it() => Assert.Equal(0, Collision.RestHeight(Fx.Model(15.63, 12), [Hill]));
    [Fact] public void The_highest_solid_under_any_part_wins() => Assert.Equal(4, Collision.RestHeight(Fx.Model(15, 12), [Hill, Fx.Box(16, 12, 2, 2, 0, 4)]), 6);
    [Fact] public void A_point_rests_on_the_solid_containing_it() => Assert.Equal(2, Collision.RestHeight(new Point2(12, 12), [Hill]), 6);

    [Fact]
    public void An_area_lifted_onto_a_hill_floors_at_its_base()
    {
        Assert.Equal(2, Collision.RestHeight(Fx.Model(18, 12), [Hill, Fx.Cylinder(16, 12, 3, 0, 6, VolumeKind.Area, z: 2)]), 6);
        Assert.Equal(0, Collision.RestHeight(Fx.Model(18, 12), [Fx.Cylinder(16, 12, 3, 0, 6, VolumeKind.Area)]));
    }

    [Fact]
    public void Terrain_rests_on_terrain_and_ignores_itself()
    {
        var wood = Fx.Cylinder(13, 12, 3, 0, 6, VolumeKind.Area);
        Assert.Equal(2, Collision.RestHeight([wood], [Hill, wood]), 6);                   // a wood dropped on a hill sits on the hill
        Assert.Equal(0, Collision.RestHeight([Hill], [Hill, wood]));                      // an Area is never a floor for a piece
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Geometry.Tests --filter CollisionTests` — fails to compile: `Collision` does not exist.

- [ ] **Step 3: Implement** — `libs/Tabletop.Geometry/Collision.cs`:

```csharp
namespace Tabletop.Geometry;

/// <summary>
/// Nothing on the table is inside anything else (client UX spec → Physical collision). A body rests on the highest Solid top under
/// any part of it, with no step-up limit: whether a climb is allowed is the server's terrain judgement. Touching is not under.
/// </summary>
public static partial class Collision
{
    /// <summary>A model: Solid tops, and the base height of an Area lifted onto something (its floor).</summary>
    public static double RestHeight(PlacedModel model, IReadOnlyList<WorldVolume> volumes) => Rest([model.BaseOutline()], volumes, floors: true);

    /// <summary>A terrain piece (its volumes share one transform): Solid tops under any of its footprints; its own volumes and every Area are ignored.</summary>
    public static double RestHeight(IReadOnlyList<WorldVolume> piece, IReadOnlyList<WorldVolume> volumes)
    {
        var own = piece.Select(v => v.Id).ToHashSet();
        return Rest(piece.Select(v => v.WorldOutline()).ToList(), volumes.Where(v => !own.Contains(v.Id)).ToList(), floors: false);
    }

    /// <summary>A point (a marker): the highest Solid top whose footprint holds it, boundary inclusive.</summary>
    public static double RestHeight(Point2 point, IReadOnlyList<WorldVolume> volumes) =>
        volumes.Where(v => v.Volume.Kind == VolumeKind.Solid && v.WorldOutline().Contains(point)).Select(v => v.WorldTop()).DefaultIfEmpty(0).Max();

    internal static double Rest(IReadOnlyList<Outline> outlines, IReadOnlyList<WorldVolume> volumes, bool floors)
    {
        var best = 0.0;
        foreach (var v in volumes)
        {
            var height = v.Volume.Kind == VolumeKind.Solid ? v.WorldTop() : floors ? v.WorldBottom() : 0;
            if (height <= best) continue;
            var outline = v.WorldOutline();
            if (outlines.Any(o => Outline.InteriorsIntersect(o, outline))) best = height;
        }
        return best;
    }
}
```

- [ ] **Step 4: Run** the same command — passes.
- [ ] **Step 5: Commit** `git add libs/Tabletop.Geometry/Collision.cs tests/Tabletop.Geometry.Tests/CollisionTests.cs && git commit -m "Rest bodies on the highest Solid under any part of them"` with the two trailer lines.

### Task 2: Slide and separate

**Model:** opus

**Files:** create `libs/Tabletop.Geometry/Collision.Slide.cs`, `tests/Tabletop.Geometry.Tests/SlideTests.cs`.

**Interfaces:**
- Consumes: Task 1's `Collision.Rest`; `Outline.Shift`, `Outline.Separation`, `Overlap.IntervalsOverlap`, `Bodies.Bottom/Top`.
- Produces: `Point3 Slide(PlacedModel moving, Point2 to, IReadOnlyList<PlacedModel> models, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)`
  (Z is the rest height where it ends); `TerrainTransform Slide(IReadOnlyList<WorldVolume> piece, Point2 to, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)`;
  `IReadOnlyList<Point3> Separate(IReadOnlyList<(PlacedModel Moving, Point2 To)> held, IReadOnlyList<PlacedModel> stationary, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)`;
  `public const double Step = 0.1`. Only the Solid volumes among `walls` stop a body; an obstacle it overlaps at the start is ignored.

- [ ] **Step 1: Write the failing test** — `tests/Tabletop.Geometry.Tests/SlideTests.cs`:

```csharp
namespace Tabletop.Geometry.Tests;

public class SlideTests
{
    static readonly WorldVolume Wall = Fx.Box(10, 10, 0.5, 20, 0, 6);                     // x 9.75..10.25, y 0..20
    static readonly WorldVolume Hill = Fx.Box(12, 12, 6, 6, 0, 2);
    static PlacedModel At(PlacedModel m, Point3 p) => m with { Placement = m.Placement with { Position = p } };

    [Fact] public void A_free_drag_reaches_its_target() => Assert.Equal(new Point3(5, 3, 0), Collision.Slide(Fx.Model(2, 3), new(5, 3), [], [], []));
    [Fact] public void A_climbable_top_is_climbed() => Assert.Equal(new Point3(12, 12, 2), Collision.Slide(Fx.Model(2, 12), new(12, 12), [], [], [Hill]));
    [Fact] public void A_model_on_another_storey_does_not_block() => Assert.Equal(5, Collision.Slide(Fx.Model(2, 3), new(5, 3), [Fx.Model(4, 3, z: 4)], [], []).X, 6);
    [Fact] public void A_body_starting_inside_an_obstacle_can_leave() => Assert.Equal(5, Collision.Slide(Fx.Model(10, 10), new(5, 10), [], [Wall], [Wall]).X, 6);
    [Fact] public void A_long_drag_does_not_tunnel_through_a_thin_wall() => Assert.True(Collision.Slide(Fx.Model(8, 10), new(18, 10), [], [Wall], [Wall]).X < 9.75 - 0.62);

    [Fact]
    public void Stops_at_a_wall_and_slides_along_it()
    {
        var end = Collision.Slide(Fx.Model(8, 10), new(12, 12), [], [Wall], [Wall]);
        Assert.Equal(9.12, end.X, 2);                                                     // 9.75 less the 0.63 radius
        Assert.Equal(12, end.Y, 2);
    }

    [Fact]
    public void Slides_along_a_model_without_entering_it()
    {
        var other = Fx.Model(5, 0);
        var end = Collision.Slide(Fx.Model(2, 0), new(8, 0.3), [other], [], []);
        Assert.False(Overlap.Bases(At(Fx.Model(0, 0), end), other));
        Assert.True(end.X > 3.7);
    }

    [Fact]
    public void A_group_compresses_against_an_obstacle()
    {
        PlacedModel[] row = [Fx.Model(5, 10), Fx.Model(3.5, 10), Fx.Model(2, 10)];          // drag order: the lead first
        var ends = Collision.Separate(row.Select(m => (m, m.Placement.Position.Horizontal + new Point2(6, 0))).ToList(), [], [Wall], [Wall]);
        var placed = ends.Select(p => At(Fx.Model(0, 0), p)).ToList();
        Assert.All(placed, m => Assert.False(Overlap.WithSolid(m, Wall)));
        Assert.False(Overlap.Bases(placed[0], placed[1]) || Overlap.Bases(placed[1], placed[2]) || Overlap.Bases(placed[0], placed[2]));
        Assert.Equal(9.12, ends[0].X, 2);
    }

    [Fact]
    public void A_terrain_piece_slides_and_rests()
    {
        var wood = Fx.Cylinder(2, 30, 3, 0, 6, VolumeKind.Area);
        Assert.Equal(new Point3(12, 12, 2), Collision.Slide([wood], new(12, 12), [], [Hill]).Position);
        Assert.True(Collision.Slide([Fx.Cylinder(2, 10, 3, 0, 6, VolumeKind.Area)], new(16, 10), [Wall], [Wall]).Position.X < 9.75 - 2.99);
    }
}
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Geometry.Tests --filter SlideTests` — fails to compile: no `Collision.Slide`.

- [ ] **Step 3: Implement** — `libs/Tabletop.Geometry/Collision.Slide.cs`:

```csharp
namespace Tabletop.Geometry;

public static partial class Collision
{
    /// <summary>The path is sampled this finely, then bisected to <see cref="Contact"/>; a 0.5" wall cannot be stepped over.</summary>
    public const double Step = 0.1;
    const double Contact = 1e-3;

    public static Point3 Slide(PlacedModel moving, Point2 to, IReadOnlyList<PlacedModel> models, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)
    {
        var from = moving.Placement.Position.Horizontal;
        Outline[] shape = [moving.BaseOutline()];
        var bodies = models.Select(m => (Body: m, Outline: m.BaseOutline())).Where(b => !Outline.InteriorsIntersect(shape[0], b.Outline)).ToList();
        var sides = Sides(walls, shape);
        Outline? Obstacle(IReadOnlyList<Outline> at)
        {
            var bottom = Rest(at, volumes, floors: true);
            foreach (var (body, outline) in bodies)
                if (Overlap.IntervalsOverlap(bottom, bottom + moving.Body.Height, body.Bottom(), body.Top()) && Outline.InteriorsIntersect(at[0], outline)) return outline;
            return sides.FirstOrDefault(s => Outline.InteriorsIntersect(at[0], s));
        }
        var end = Move(shape, from, to, Obstacle);
        return new Point3(end.X, end.Y, Rest(Shifted(shape, end - from), volumes, floors: true));
    }

    public static TerrainTransform Slide(IReadOnlyList<WorldVolume> piece, Point2 to, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)
    {
        var origin = piece[0].Transform;
        var from = origin.Position.Horizontal;
        var shape = piece.Select(v => v.WorldOutline()).ToList();
        var own = piece.Select(v => v.Id).ToHashSet();
        var sides = Sides(walls.Where(w => !own.Contains(w.Id)).ToList(), shape);
        var end = Move(shape, from, to, at => sides.FirstOrDefault(s => at.Any(o => Outline.InteriorsIntersect(o, s))));
        var moved = piece.Select(v => v with { Transform = origin with { Position = new Point3(end.X, end.Y, 0) } }).ToList();
        return origin with { Position = new Point3(end.X, end.Y, RestHeight(moved, volumes)) };
    }

    /// <summary>Each held body slides against the stationary ones and the held bodies already placed, in drag order.</summary>
    public static IReadOnlyList<Point3> Separate(IReadOnlyList<(PlacedModel Moving, Point2 To)> held, IReadOnlyList<PlacedModel> stationary, IReadOnlyList<WorldVolume> walls, IReadOnlyList<WorldVolume> volumes)
    {
        var placed = stationary.ToList();
        var ends = new List<Point3>(held.Count);
        foreach (var (moving, to) in held)
        {
            var end = Slide(moving, to, placed, walls, volumes);
            ends.Add(end);
            placed.Add(moving with { Placement = moving.Placement with { Position = end } });
        }
        return ends;
    }

    static List<Outline> Sides(IReadOnlyList<WorldVolume> walls, IReadOnlyList<Outline> shape) =>
        walls.Where(w => w.Volume.Kind == VolumeKind.Solid).Select(w => w.WorldOutline()).Where(s => !shape.Any(o => Outline.InteriorsIntersect(o, s))).ToList();

    static IReadOnlyList<Outline> Shifted(IReadOnlyList<Outline> shape, Point2 by) => shape.Select(o => o.Shift(by)).ToList();

    /// <summary>To the first contact, then the rest of the drag projected on the contact's tangent, once.</summary>
    static Point2 Move(IReadOnlyList<Outline> shape, Point2 from, Point2 to, Func<IReadOnlyList<Outline>, Outline?> obstacle)
    {
        Outline? Hit(Point2 at) => obstacle(Shifted(shape, at - from));
        var (contact, by) = Advance(from, to, Hit);
        if (by is null) return to;
        var (_, onMe, onIt) = shape.Select(o => Outline.Separation(o.Shift(contact - from), by)).MinBy(s => s.Distance);
        var n = onMe - onIt;
        if (n.Length < Tolerance.Distance) return contact;
        n *= 1 / n.Length;
        var rest = to - contact;
        var along = rest - n * rest.Dot(n);
        return along.Length <= Contact ? contact : Advance(contact, contact + along, Hit).At;
    }

    static (Point2 At, Outline? By) Advance(Point2 from, Point2 to, Func<Point2, Outline?> hit)
    {
        var d = to - from;
        var steps = Math.Max(1, (int)Math.Ceiling(d.Length / Step));
        var free = 0.0;
        for (var k = 1; k <= steps; k++)
        {
            var t = (double)k / steps;
            if (hit(from + d * t) is not { } by) { free = t; continue; }
            double lo = free, hi = t;
            while ((hi - lo) * d.Length > Contact) { var mid = (lo + hi) / 2; if (hit(from + d * mid) is null) lo = mid; else hi = mid; }
            return (from + d * lo, by);
        }
        return (to, null);
    }
}
```

- [ ] **Step 4: Run** the same command, then `dotnet test tests/Tabletop.Geometry.Tests` — all pass.
- [ ] **Step 5: Commit** both files: `Slide bodies along what they cannot enter and compress groups`, with the two trailer lines.

### Task 3: The move region goes around models

**Model:** sonnet

**Files:** modify `libs/Tabletop.Geometry/Movement.cs`, `tests/Tabletop.Geometry.Tests/MovementTests.cs`.

**Interfaces:** Produces `Movement.Region(PlacedModel start, double allowance, IReadOnlyList<WorldVolume> impassable, IReadOnlyList<WorldVolume> difficult, double? difficultCap, TableSize? table, IReadOnlyList<PlacedModel>? models = null)`:
a ray stops where the base would overlap one of `models` (2D, as volumes are), less any it overlaps at the start. Existing callers compile unchanged.

- [ ] **Step 1: Failing test** — append to `MovementTests`:

```csharp
    [Fact]
    public void The_region_goes_around_other_models()
    {
        var start = Fx.Model(10, 10);
        var open = Movement.Region(start, 6, [], [], null, null);
        var blocked = Movement.Region(start, 6, [], [], null, null, [Fx.Model(13, 10), Fx.Model(10.5, 10)]);   // the second overlaps the start: ignored
        Assert.Equal(16, open.Boundary[0].X, 6);                                          // ray 0 is +X
        Assert.True(blocked.Boundary[0].X <= 13 - 1.26 + 1e-6);                          // stops short of contact, centres 1.26 apart
        Assert.Equal(open.Boundary[45], blocked.Boundary[45]);                           // ray 45 is +Y, clear
    }
```

- [ ] **Step 2: Run** `dotnet test tests/Tabletop.Geometry.Tests --filter The_region_goes_around_other_models` — fails to compile (seven arguments).
- [ ] **Step 3: Implement** — in `Movement.Region` add the parameter, then after `var slab = …`:

```csharp
        var bodies = (models ?? []).Select(m => m.BaseOutline()).Where(o => !Outline.InteriorsIntersect(baseOutline, o)).ToList();
```

and widen the ray's stop test to:

```csharp
                if (Touches(at, baseRadius, atOutline, impassableNear) || bodies.Any(o => Outline.InteriorsIntersect(atOutline, o))
                    || (slab is not null && !slab.ContainsOutline(atOutline))) break;
```

- [ ] **Step 4: Run** `dotnet test tests/Tabletop.Geometry.Tests` — passes.
- [ ] **Step 5: Commit** `Draw the move region around other models`, with the two trailer lines.

## Continued

Tasks 4–7 (server, wire, breaches, hill meshes): [`01a-collision-server.md`](01a-collision-server.md).
Tasks 8–12 (client, sim, documents, validation): [`01b-collision-client.md`](01b-collision-client.md).
The plan is split there to keep each file under 500 lines; the Global Constraints, Rulings and Review Focus above govern all three.
