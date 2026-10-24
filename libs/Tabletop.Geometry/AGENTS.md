# libs/Tabletop.Geometry

Geometry values and computations: distance, line of sight, cover, collision (rest, surfaces, slide, separate, land), containment, overlap, coherency, the movement region and the built-in zone styles.
`net10.0`, Godot-free, persistence-free, no project references. Consumed by `libs/Tabletop.Content`, `libs/Tabletop.Protocol`, `server/game`, `client/` and `sim/`.

## Layout

- `Points.cs`, `Placements.cs`: `Point2`, `Point3`, `Yaw`, `TableSize`, `Placement`, `PlacedModel`, `TerrainTransform`.
- `Bases.cs`, `Bodies.cs`: `Base` (circle, oval, rect), `ModelBody`; a model is a base and a height, upright, yaw only.
- `Volumes.cs`, `WorldVolumes.cs`: terrain `Volume` (Solid or Area, `Footprint`, declarations) and `WorldVolume` (the piece's transform applied).
- `Outline.cs`: convex `Polygon` and `PolygonOutline`; every base and footprint becomes one.
- `Distance.cs`, `LineOfSight.cs`, `Cover.cs`, `Collision.cs`, `Collision.Surfaces.cs`, `Collision.Slide.cs`, `Collision.Land.cs`, `Collision.Crosses.cs`, `Containment.cs`, `Overlap.cs`, `Coherency.cs`, `Movement.cs`: static computations, one class each.
- `MoveShape.cs`, `MoveSpace.cs`, `MoveSpace.Search.cs`, `MoveReach.cs`, `ReachMesh.cs`: movement along the path (`docs/design/geometry/movement.md`). `MoveShape` is one obstacle in the moving base's configuration space (a convex core grown by a radius) with its ring of graph corners; `MoveSpace` a base's obstacles by group (walls, bodies, difficult, dangerous), the table box, `Clear`, `Route` (A*) and `Settle` (a Dijkstra `Field`); `MoveReach` one model's reach for a move Ask (`Route`, `Clamp`, `Triangles`); `ReachMesh` the reachable area as triangles.
- `SightBlockers.cs`: a table's blocking volumes prepared once for many sight lines (outlines, heights and boxes built once, a box test before the exact one); `Check` and `Measure` equal `LineOfSight.Check` and `Cover.SightBlockersBetween`. It also takes `SightPrism`s (a convex outline between two heights; `Bodies.Perimeter` builds a unit's from the convex hull of its bases) and an ignore predicate.
- `BaseEdge.cs`: one placed base's outline built once for many point distances (`To` equals `Distance.EdgeToEdge` to a point), with the radii of the circles about its centre it contains (`Inner`) and lies in (`Outer`).
- `Table.cs`: the slab, `Bounds`, `ContainsPoint`, `ContainsBase`, `IsValid` (edges at most `MaxEdge`, 144").
- `ZoneStyles.cs`: the seven built-in deployment styles and `Resolve` against a `TableSize`.
- `Tolerance.cs`: `1e-6` inches, internal.
- `Sampling.cs`: `Steps`, the sample count of a swept check, capped at `MaxSamples` (4096).

## How it works

- Inputs are values (`PlacedModel`, `WorldVolume`, `Point2`); outputs are values or id lists. There is no state.
- Distance is edge to edge between bases, never centre to centre.
- `Collision.RestHeight` rests a model, a point or a terrain piece on the highest Solid top under its outline or an Area floor, else 0 (the slab); touching a side is not standing on it.
- `Collision.Surfaces` (held pieces lift over spec) are the heights a model or piece could rest at: 0, each Solid top and (a model) each Area floor under it, open where the body standing there overlaps no Solid's height interval; the highest is always `RestHeight`. `Collision.RestNear(body, volumes, level)` is the open surface nearest a level, the higher on a tie (`double.PositiveInfinity`: the highest).
- `Collision.Slide` moves a body toward a target in `Step` (0.1") increments to the first contact with the Solid volumes the caller names as walls, then slides the rest of the drag along each contact's tangent (up to four contacts), and rests it. With a finite `level` the body rests nearest it on everything but the walls it can meet, and a wall stops it only where their height intervals overlap, so it passes under a bridge with room; the default level is the highest rest with walls in plan. A terrain piece in hand uses `Collision.Follow` (straight to the target when its footprint there meets no wall, else `Slide`); `Collision.Clear` moves a piece out of the walls it stands in (a turn) to the nearest free spot, searched in rings 0.25" apart; `Collision.Blocked` tests it; all three take the level. `Collision.Separate` places a dragged group in drag order against the stationary models.
- A lifted held body: `Collision.Land` puts a model at the nearest spot to its target (the same rings) where its base enters no wall and overlaps no model rested there, and a caller's `within` takes it (the group form in drag order), a piece where `Clear` puts it at the target; it rests on its highest open surface. `Collision.Hover` is where it is drawn: `Lift` (0.5") over the highest top of any volume or model under it.
- `Collision.Crosses` sweeps a body along a straight move against other models' bodies (models overlapped at the start excluded). Model against model is three-dimensional everywhere (`Slide`, `Crosses`, movement paths, `Overlap.Bases`, `Distance.BodyToBody`): the moving body rests on the terrain under it at each sample and meets another only where the outlines share area and the height intervals overlap. `Bodies.AsBlockingVolume` is the line-of-sight body only.
- Containment is computed, never stored.
- A move is measured along the shortest path its base can take (p.5): round impassable volumes, other models (in plan and height) and the table edge, and round difficult and dangerous volumes when that fits (round difficult within the allowance first, else through it within the cap). `Movement.Check` judges one move and returns its `Path`; `Movement.VolumesOnPath` is p.11's "in or through" along it; `Movement.ContactDistance` the path to base contact (a charge target, a container in reach); `MoveReach` (and `Movement.Region`, its triangles) is the client's area, drag hold and ruler path. The wire carries destinations only; each side computes the same path.
- `Coherency.Check` has no production caller: nothing judges coherency. Collision keeps bodies apart in the client's drag.

## Commands

```bash
dotnet build libs/Tabletop.Geometry
dotnet test tests/Tabletop.Geometry.Tests
```

## Conventions and pitfalls

- One world unit is one inch.
- The table frame has its origin at the table's corner: `0 <= x <= width`, `0 <= y <= length`, Z up, top face of the slab at Z = 0. The wire, the server and this library use it; nothing converts. Godot converts in the client (`WorldFrame`).
- Cross-platform floating-point determinism is not required; comparisons use `Tolerance.Distance`.
- `Table.ContainsPoint` is edge inclusive; it is the server's `OnSlab` rule.
- Circle `Footprint` stores the radius in `A` and `B`; a circle `Base` stores the diameter. Oval and rect: `A` is length along facing, `B` is width.
- Nothing here names a game's rules; rule constants arrive as parameters (for example `Coherency.Check` takes its distances).

## Docs

- `docs/design/geometry/design.md`
- `docs/superpowers/specs/2026-08-29-geometry-values-design.md`
