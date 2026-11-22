# libs/Tabletop.Geometry

Geometry values and computations: distance, line of sight, cover, collision (rest, surfaces, slide, separate, land), containment, overlap, coherency, the movement region and the built-in zone styles.
`net10.0`, Godot-free, persistence-free, no project references. Consumed by `libs/Tabletop.Content`, `libs/Tabletop.Protocol`, `libs/Tabletop.Geometry.Wasm` (the viewer's WebAssembly build), `server/game`, `client/` and `sim/`.

## Layout

- `Points.cs`, `Placements.cs`: `Point2`, `Point3`, `Yaw`, `TableSize`, `Placement`, `PlacedModel`, `TerrainTransform`.
- `Bases.cs`, `Bodies.cs`: `Base` (circle, oval, rect), `ModelBody`; a model is a base and a height, upright, yaw only.
- `Volumes.cs`, `WorldVolumes.cs`: terrain `Volume` (Solid or Area, `Footprint`, declarations) and `WorldVolume` (the piece's transform applied).
- `Outline.cs`: convex `Polygon` and `PolygonOutline`; every base and footprint becomes one.
- `Distance.cs` (also `Between`, the plan gap between two placed footprints or a footprint and a point), `LineOfSight.cs`, `Cover.cs`, `Collision.cs`, `Collision.Surfaces.cs`, `Collision.Slide.cs`, `Collision.Land.cs`, `Collision.Crosses.cs`, `Containment.cs`, `Overlap.cs`, `Coherency.cs`, `Movement.cs`, `UnitFormation.cs`: static computations, one class each.
- `MoveShape.cs`, `MoveSpace.cs`, `MoveSpace.Search.cs`, `MoveReach.cs`, `ReachMesh.cs`: movement along the path (`docs/design/geometry/movement.md`). `MoveShape` is one obstacle in the moving base's configuration space (a convex core grown by a radius) with its ring of graph corners; `MoveSpace` a base's obstacles by group (walls, bodies, difficult, dangerous, unstable), the table box, `Clear`, `Route` (A*) and `Settle` (a Dijkstra `Field`); `MoveReach` one model's reach for a move Ask (`Route`, `Clamp`, `Reaches`, `Triangles`, and `Bands`/`BandAt`: the area split by what reaching a point costs, over the dangerous and unstable volumes it is given; `Union`, several models' areas as one, each band the union of theirs; read-only once built, the bands' fields settled on first use); `ReachMesh` the reachable area as triangles, one band or several in one walk, its 8" coarse cells walked on several threads (one in the browser's WebAssembly).
- `SweptPath.cs`: a move's base swept along its path (`Of`: `PathFootprint`s a base length apart plus one where the base is deepest in each dangerous volume it touches, the dangerous volumes by `Movement.VolumesOnPath`, the server's test, and per such volume the `PathStretch` of the walk along which the base touches it; `DangerousTo`: the unstable volumes join the dangerous ones past `UnstableOver`). Both clients' move path and dangerous terrain warning.
- `SightBlockers.cs`: a table's blocking volumes prepared once for many sight lines (outlines, heights and boxes built once, a box test before the exact one); `Check` and `Measure` equal `LineOfSight.Check` and `Cover.SightBlockersBetween`. It also takes `SightPrism`s (a convex outline between two heights; `Bodies.Perimeter` builds a unit's from the convex hull of its bases) and an ignore predicate.
- `BaseEdge.cs`: one placed base's outline built once for many point distances (`To` equals `Distance.EdgeToEdge` to a point), with the radii of the circles about its centre it contains (`Inner`) and lies in (`Outer`).
- `RangeOutline.cs`: `Of`, the union outline of the rings at a range from each model's base edge (the client's range rings, and the browser's).
- `Clearance.cs`: a placement held over a distance from models, base to base: `Breaks` (the first model a placed base comes within its distance of, -1 when none or when within `liftedWithin` of a lifted model) and `Outline` (the area a placed base's edge may not enter: `RangeOutline.Of` per model's distance, less the lifted area, its arcs cut where the two meet so the loops close). The server's keep-away breaches and both clients' keep-away area.
- `Contacts.cs`: `Of`, each proposed model's first contact (`ContactKind`: on a base, a path through a model, too close, in impassable terrain) from `ContactBody` rows and `ContactTerms`; `RoundModels`, a caller's answer that a path round the models reaches a landing (the client's per-Ask `MoveReach`), skips that landing's path search.
- `Table.cs`: the slab, `Bounds`, `ContainsPoint`, `ContainsBase`, `ContainsVolume` (a footprint wholly on it), `IsValid` (edges at most `MaxEdge`, 144").
- `ZoneStyles.cs`: the seven built-in deployment styles and `Resolve` against a `TableSize`.
- `Tolerance.cs`: `1e-6` inches, internal.
- `Sampling.cs`: `Steps`, the sample count of a swept check, capped at `MaxSamples` (4096).

## How it works

- Inputs are values (`PlacedModel`, `WorldVolume`, `Point2`); outputs are values or id lists. There is no state.
- Distance is edge to edge between bases, never centre to centre.
- `Collision.RestHeight` rests a model, a point or a terrain piece on the highest Solid top under its outline or an Area floor, else 0 (the slab); touching a side is not standing on it.
- `Collision.Surfaces` (held pieces lift over spec) are the heights a model or piece could rest at: 0, each Solid top and (a model) each Area floor under it, open where the body standing there overlaps no Solid's height interval; the highest is always `RestHeight`. `Collision.RestNear(body, volumes, level)` is the open surface nearest a level, the higher on a tie (`double.PositiveInfinity`: the highest).
- `Collision.Slide` moves a body toward a target in `Step` (0.1") increments to the first contact with the Solid volumes the caller names as walls, then slides the rest of the drag along each contact's tangent (up to four contacts), and rests it. With a finite `level` the body rests nearest it on everything but the walls it can meet, and a wall stops it only where their height intervals overlap, so it passes under a bridge with room; the default level is the highest rest with walls in plan. A terrain piece in hand uses `Collision.Follow` (straight to the target when its footprint there meets no wall, else `Slide`); `Collision.Clear` moves a piece out of the walls it stands in (a turn) to the nearest free spot, searched in rings 0.25" apart; `Collision.Blocked` tests it; all three take the level. `Collision.Separate` places a dragged group in drag order against the stationary models.
- A lifted held body: `Collision.Land` puts a model at the nearest spot to its target (the same rings) where its base enters no wall and overlaps no model rested there, and a caller's `within` takes it (the group form in drag order), drawn flush against an `Abutment` model it ends within the given distance of, and within `Abutment.Vertical` of in height when set (`Collision.Abut`: a charge), a piece where `Clear` puts it at the target; it rests on its highest open surface. `Collision.Hover` is where it is drawn: `Lift` (0.5") over the highest top of any volume or model under it.
- `Collision.Crosses` sweeps a body along a straight move against other models' bodies (models overlapped at the start excluded). Model against model is three-dimensional everywhere (`Slide`, `Crosses`, movement paths, `Overlap.Bases`, `Distance.BodyToBody`): the moving body rests on the terrain under it at each sample and meets another only where the outlines share area and the height intervals overlap. `Bodies.AsBlockingVolume` is the line-of-sight body only.
- Containment is computed, never stored.
- A move is measured along the shortest path its base can take (p.5): round impassable volumes, other models (in plan and height) and the table edge, and round difficult and dangerous volumes when that fits (round difficult within the allowance first, else through it within the cap). `Movement.Check` judges one move and returns its `Path`; `Movement.Measured` is the path a move is measured along when some volumes are dangerous only past a length (Check round the dangerous ones, again round the unstable ones too past the threshold; the server's `ApplyMove`, the client's route); `Movement.VolumesOnPath` is p.11's "in or through" along it; `Movement.ContactDistance` the path to base contact (a charge target, a container in reach); `MoveReach` (and `Movement.Region`, its triangles) is the client's area, drag hold and ruler path. The wire carries destinations only; each side computes the same path.
- Broad phase: `Bodies.Apart` (centre distance against each base's `Reach`, the circle about its centre that holds it) skips the exact test in `Overlap.Bases`, `Collision.Hover`, `Contacts`' distances and a model's `MoveShape` in `MoveSpace` (for two circles, the shape's own centre and bound, without building it), only where the exact test cannot be true. A body that meets the moving base at every height the base can rest at inside the body's shape (`MoveSpace.MeetsAtEveryRest`) is not rested per sample; one whose shape only some surfaces come near (`Obstacle.Rests`) rests over those alone (`RestAt`), and a segment along which every height it can rest at gives the same answer (`MeetsAlong`) is not sampled; a segment clearly inside its shape (`MoveShape.SurelyEnters`) skips the interval. `MoveShape.Interval` is closed form (the core's interior, a band outside each edge, a disc about each vertex). A many-sided core (an oval base has 64 sides) keeps coarse bounds: circles about its centre inside and holding it, and 16-sided polygons inside and holding it; `Blocks`, `SurelyEnters` and the zero-radius `Enters` decide on them when they clear the limit by `Slack` (1e-9) and measure the core only near it, and `CoreSigned` and `SegmentDistance` skip an edge or vertex whose line distance already exceeds the least so far, so every answer is the one every edge gives. `tests/Tabletop.Geometry.Tests/ReferenceAgreementTests` holds the reach area, contacts and landings over the reference game's table (`Fixtures/reference-move.json`: 150 models, a ruin, two forests) to what they were, bit for bit, and `CavalryRushTests` (`CavalryRushScene`: a 16" move of five 60 × 35 mm ovals among 50 models and 18 pieces) each rider's area, routes and clamps; `MoveShapeReferenceTests` holds `CoreSigned` and `Blocks` to the plain computation and `Interval` to the search it replaced. `ReachMeshReferenceTests` holds the walk to the plain one-thread walk it replaced (the same area, the same points inside) and `MoveReach.Union` to each model's `BandAt`. On the predefined tables (`MoveAreaBench` in `tests/Tabletop.Client.Tests`, optimized, twelve threads) an infantry lead's 12" area takes 2–5 ms, a 10- or 20-model unit's 12" area as one 10–50 ms, a cavalry lead's 16" bands 30–70 ms and five riders' as one 80–500 ms; about ten times that in a debug build. The client draws the held models' areas as one (`MoveRegions.Area`), the whole unit's built from the Ask's opening.
- `Coherency.Check` is the server's `move_coherency` (p.7 1" and 9", p.11 3" at different elevation). `UnitFormation.InContact` forms a unit up with one
  model on a `Movement.ContactPoints` landing in base contact and the rest beside it (`Follow`), every move judged by `FormationTerms`; with
  `ContactVertical` the lead rests on its open surface nearest the target's level and is contact only within that height of the target's base, and
  each landing comes back with its rest height. The game server's `charge_contact` asks whether a charge could have reached contact, and the
  simulator's policy charges that way. `Movement.ContactInReach` is the same test for one model (a plan path to contact, then a contact landing
  within the vertical reach on a path within the limit), the game server's charge targets. Collision keeps bodies apart in the client's drag.

## Commands

```bash
dotnet build libs/Tabletop.Geometry
dotnet test tests/Tabletop.Geometry.Tests
```

## Profiling a slow reach area

`dotnet tool install dotnet-trace` fails here (the package lacks `DotnetToolSettings.xml`), so profile with Stopwatch probes:

1. Put the table in a test scene (`CavalryRushScene` is the pattern) and capture each area's digest first (`CavalryRushTests`' expected strings) so
   a change is checked bit for bit.
2. Add temporary `internal static` counters and `Stopwatch`es around `MoveSpace.Clear`, `Field.Best`, `MoveShape.Blocks`, `Crosses`,
   `Interval` and `MeetsInHeight`, with a reset; print them from a throwaway test after a warm-up build (the first build is mostly JIT).
3. Measure both `dotnet test` (Debug: the dev client and the tests) and `dotnet test -c Release` (the optimized export); they differ by about ten
   times. Remove the probes before committing.

## Conventions and pitfalls

- One world unit is one inch.
- The table frame has its origin at the table's corner: `0 <= x <= width`, `0 <= y <= length`, Z up, top face of the slab at Z = 0. The wire, the server and this library use it; nothing converts. Godot converts in the client (`WorldFrame`).
- Cross-platform floating-point determinism is not required; comparisons use `Tolerance.Distance`.
- `Table.ContainsPoint` is edge inclusive; it is the server's `OnSlab` rule.
- Circle `Footprint` stores the radius in `A` and `B`; a circle `Base` stores the diameter. Oval and rect: `A` is length along facing, `B` is width.
- Nothing here names a game's rules; rule constants arrive as parameters (for example `Coherency.Check` takes its distances).
- The reach area's per-point methods (`MoveShape`, `MoveSpace`, `MoveSpace.Search`, `MoveReach`, `ReachMesh`, `Outline.ClosestOnSegment`) carry
  `[MethodImpl(MethodImplOptions.AggressiveOptimization)]`: the client calls them seldom enough that tiered JIT leaves them unoptimized for a
  move Ask's first areas (a cold 12" area: about 600 ms unoptimized, 300 ms marked, 80 ms with tiering off). A new method on that path takes it too.

## Docs

- `docs/design/geometry/design.md`
- `docs/superpowers/specs/2026-08-29-geometry-values-design.md`
