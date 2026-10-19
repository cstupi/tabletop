# libs/Tabletop.Geometry

Geometry values and computations: distance, line of sight, cover, collision (rest, slide, separate), containment, overlap, coherency, the movement region and the built-in zone styles.
`net10.0`, Godot-free, persistence-free, no project references. Consumed by `libs/Tabletop.Content`, `libs/Tabletop.Protocol`, `server/game`, `client/` and `sim/`.

## Layout

- `Points.cs`, `Placements.cs`: `Point2`, `Point3`, `Yaw`, `TableSize`, `Placement`, `PlacedModel`, `TerrainTransform`.
- `Bases.cs`, `Bodies.cs`: `Base` (circle, oval, rect), `ModelBody`; a model is a base and a height, upright, yaw only.
- `Volumes.cs`, `WorldVolumes.cs`: terrain `Volume` (Solid or Area, `Footprint`, declarations) and `WorldVolume` (the piece's transform applied).
- `Outline.cs`: convex `Polygon` and `PolygonOutline`; every base and footprint becomes one.
- `Distance.cs`, `LineOfSight.cs`, `Cover.cs`, `Collision.cs`, `Collision.Slide.cs`, `Collision.Crosses.cs`, `Containment.cs`, `Overlap.cs`, `Coherency.cs`, `Movement.cs`: static computations, one class each.
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
- `Collision.Slide` moves a body toward a target in `Step` (0.1") increments to the first contact with the Solid volumes the caller names as walls, then slides the rest of the drag along each contact's tangent (up to four contacts), and rests it. A terrain piece in hand uses `Collision.Follow` (straight to the target when its footprint there enters no wall, else `Slide`); `Collision.Clear` moves a piece out of the walls it stands in (a turn) to the nearest free spot, searched in rings 0.25" apart; `Collision.Blocked` tests it. `Collision.Separate` places a dragged group in drag order against the stationary models. `Collision.Crosses` sweeps a body along a straight move against other models' bodies (models overlapped at the start excluded). Model against model is three-dimensional everywhere (`Slide`, `Crosses`, `Movement.Region`, `Overlap.Bases`, `Distance.BodyToBody`): the moving body rests on the terrain under it at each sample and meets another only where the outlines share area and the height intervals overlap. `Bodies.AsBlockingVolume` is the line-of-sight body only.
- Containment is computed, never stored.
- `Movement.Region` yields the movement boundary; `Movement.Check` judges one straight move.
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
