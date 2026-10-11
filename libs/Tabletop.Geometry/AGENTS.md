# libs/Tabletop.Geometry

Geometry values and computations: distance, line of sight, cover, collision (rest, slide, separate), containment, overlap, coherency, the movement region and the built-in zone styles.
`net10.0`, Godot-free, persistence-free, no project references. Consumed by `libs/Tabletop.Content`, `libs/Tabletop.Protocol`, `server/game`, `client/` and `sim/`.

## Layout

- `Points.cs`, `Placements.cs`: `Point2`, `Point3`, `Yaw`, `TableSize`, `Placement`, `PlacedModel`, `TerrainTransform`.
- `Bases.cs`, `Bodies.cs`: `Base` (circle, oval, rect), `ModelBody`; a model is a base and a height, upright, yaw only.
- `Volumes.cs`, `WorldVolumes.cs`: terrain `Volume` (Solid or Area, `Footprint`, declarations) and `WorldVolume` (the piece's transform applied).
- `Outline.cs`: convex `Polygon` and `PolygonOutline`; every base and footprint becomes one.
- `Distance.cs`, `LineOfSight.cs`, `Cover.cs`, `Collision.cs`, `Collision.Slide.cs`, `Collision.Crosses.cs`, `Containment.cs`, `Overlap.cs`, `Coherency.cs`, `Movement.cs`: static computations, one class each.
- `Table.cs`: the slab, `Bounds`, `ContainsPoint`, `ContainsBase`, `IsValid`.
- `ZoneStyles.cs`: the seven built-in deployment styles and `Resolve` against a `TableSize`.
- `Tolerance.cs`: `1e-6` inches, internal.

## How it works

- Inputs are values (`PlacedModel`, `WorldVolume`, `Point2`); outputs are values or id lists. There is no state.
- Distance is edge to edge between bases, never centre to centre.
- `Collision.RestHeight` rests a model, a point or a terrain piece on the highest Solid top under its outline or an Area floor, else 0 (the slab); touching a side is not standing on it.
- `Collision.Slide` moves a body toward a target in `Step` (0.1") increments along the Solid volumes the caller names as walls and rests it; `Collision.Separate` places a dragged group in drag order against the stationary models. `Collision.Crosses` sweeps a base along a straight move against other models' true base outlines (models overlapped at the start excluded). `Bodies.AsBlockingVolume` is the line-of-sight body only.
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
- `docs/superpowers/plans/2026-09-05-geometry.md`
- `docs/superpowers/specs/2026-08-29-geometry-values-design.md`
