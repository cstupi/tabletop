# Geometry: movement along the path

How `Tabletop.Geometry` measures a move, draws where a model can go, and judges a move for the server. Section 12 of
`design.md` points here.

## The rule

- p.5 MEASURING MOVEMENT: *"When measuring how far a model moves always measure so that no part of its base moves further
  than the total distance."* The path may bend; its length is what counts.
- p.9 CHARGE MOVES: *"charge moves don't have to be in a straight line"*; a charge target is valid when a model *"is within
  charge distance of one model from the target unit, and has a clear path to reach it"*.
- p.11 IMPASSABLE: *"Units may not move through impassable terrain"*. DIFFICULT: *"If any model in a unit moves in or
  through difficult terrain at any point of its move, then all models in the unit may not move more than 6" for that
  movement."* DANGEROUS: *"If a model moves in or through dangerous terrain, or is activated in it, then it must take a
  dangerous terrain test."*
- p.13 Flying *"may move through units and terrain"*; Aircraft *"ignore all units and terrain when moving and stopping"*.
- p.7: *"Models may never be within 1” of models from other units, unless they are using a Charge action, and may never move through
  other models or units (friendly or enemy), even if they are using a Charge action."* Owner ruling (2026-11-27): both clauses hold along
  the whole path; a path that slips between two models of another unit moves through that unit
  (`docs/superpowers/specs/2026-11-27-move-through-units-design.md`).

So a model reaches a spot behind a wall by going round it, and a 12" Rush that goes round a wood never enters it and is not
capped.

## Configuration space

A base moves by translation (its yaw is fixed during a move). For a base `B` placed at its centre and an obstacle `O`, the
centres at which `B` meets `O` form `O ⊕ (−B)`. Every base and footprint here is a circle or a convex polygon, so that set is
exactly a convex core (a point or a convex polygon: the Minkowski sum of the cores) grown by a radius (the sum of the
circles' radii): `MoveShape`. A centre `p` is inside when the core's signed distance at `p` is under the radius by more than
`Tolerance.Distance`, which is `Outline.InteriorsIntersect`'s test. Segment tests are exact: the segment's distance to the
core against the radius, and for a polygon against a polygon (radius 0) a Cyrus–Beck clip against the core inset by the
tolerance.

`MoveSpace` holds one base's shapes from where it stands, in groups a search turns on or off:

| Group | From | Held to it |
| --- | --- | --- |
| `Walls` | the impassable volumes the caller lists | always; one the base already stands in may be left only along a line heading out of it (its signed distance growing from the first step), never along it or deeper in |
| `Bodies` | other models' bases; with `MoveUnits`, a unit's perimeter when it has no grown one beside it | where the heights overlap too: the mover rested on the surfaces at each 0.1" of the part of the segment inside the shape (`Collision.Rest`), so a base passes above or below a model on another level; a model it starts on is left out |
| `Spacing` | a unit's perimeter grown by `MoveUnits.Separation`, its heights grown by it too | as `Bodies`; every search the reach and `Check` make first |
| `Perimeter` | the plain perimeter of a unit whose grown one is in `Spacing` | as `Bodies`; only `Check`'s search that lets the separation go |
| `Difficult`, `Dangerous` | the volumes the caller lists | only in the searches that avoid them |

`MoveSpace.Held` (walls, `Bodies`, `Spacing`) is what holds every move; `MoveSpace.Models` (`Bodies`, `Spacing`, `Perimeter`) the groups
met in plan and height.

### Units

`MoveUnits(Of, Own, Separation, Bases)` gives the unit of each model a move is given (`Of[i]` for `models[i]`). Each unit other than `Own`
and those in `Bases` (a charge's target) is one obstacle, its perimeter: the convex hull of its models' bases (`MoveSpace.UnitCore`), from
its lowest base to its tallest top. A unit of circles of one size is the hull of their centres grown by the radius, exactly; circles of
several sizes are each the 16-gon circumscribing what lies beyond 0.05" under the smallest radius, grown by that radius; a unit with an oval
or rect base is the hull of the polygons' vertices and the circles' circumscribed 16-gons. As a `MoveShape` the hull is a convex core and the
grown radius (`MoveShape.Of(core, radius, mover, origin)`), so the separation adds to the radius. `Own` and `Bases` block as their models'
bases. Where the base starts decides the obstacle: outside the grown perimeter, the grown one in `Spacing` and the plain one in
`Perimeter`; inside the grown one (or with no separation), the plain one in `Bodies`; inside the plain one, the unit's bases in `Bodies`;
a base it overlaps is left out, as without units. So a model is never trapped by where it stands. Every body obstacle carries its unit's id,
and `MoveSpace.FirstMet` names the first one a path meets.

The table is a box for the centre (the slab shrunk by the base's extent); a base that already stands off it is held to no
table on the way. Only shapes within the search's length of the start are built: a path no longer than `L` stays within `L`
of it.

## Shortest paths

The shortest path for a translating convex base among convex obstacles bends only at obstacle corners, tangent to them.
Each shape gets a ring of corners just outside it (`MoveShape.Ring`): a polygon core's corner pushed out 0.001" along its
bisector; a rounded corner circumscribed by a polygon whose extra length round a half-turn is about 0.02" (at most 64 corners
to a full turn). The graph is the start, the goal and the ring corners no active obstacle holds, joined where the way is
`Clear` and, between two corners, where the line is tangent to both rings (the reduced visibility graph). Paths found are
legal (every leg is tested against the exact shapes) and at most a few hundredths of an inch longer than the exact shortest;
tests compare against the exact values (`MovementPathTests`: 11.588" round a 2" wood, 9.425" round a 2" × 4" block).

- `MoveSpace.Route(goal, groups, limit)`: A* for one destination (heuristic: the straight distance), the straight line first.
- `MoveSpace.Settle(groups, limit)`: Dijkstra over every corner within `limit`, a `Field`. A point's distance is the least,
  over the start and each settled corner it can see, of the corner's distance plus the straight line; the corner the last
  point used is tried first (asked across a grid, it usually wins, and the scan then skips most corners unchecked).
- Ring corners are built on first use: a move whose straight line is clear never builds them.

## Which path a move takes

`Movement.Check(start, destination, allowance, impassable, difficult, cap, table, models?, surfaces?, dangerous?, steep?, units?)` returns
`MoveCheck(Distance, EffectiveAllowance, WithinAllowance, CrossesImpassable, EntersDifficult, LeavesTable, Path,
CrossesModels, NearModels, Unit)`. The path is the first that fits, in order:

1. round every difficult and dangerous volume, within the allowance;
2. round every difficult volume, within the allowance;
3. round every dangerous volume, within the cap;
4. any path, within the cap.

Without a cap (Strider, Flying, Aircraft) the order is round both, round dangerous, any, all within the allowance. A player
going round a wood or a pit never chose to enter it, and the cap binds the whole unit, so keeping out of difficult ground
comes first. `EntersDifficult` is whether the chosen path's base touches a difficult volume; then `EffectiveAllowance` is the
cap. Every choice is held to walls, bodies and the grown perimeters (`Held`). A destination none fits is tried again round the plain
perimeters (`Bodies` and `Perimeter`): found, it is `NearModels` (the server's path `move_separation`); then without any model: found, it is
`CrossesModels` (the server's `move_through_models`). `Unit` names the unit the path met first (`FirstMet`: a grown perimeter for
`NearModels`, a plain perimeter or a base for `CrossesModels`). Nothing fits at all: the shortest path at any length gives the `Distance` a refusal quotes,
and `CrossesImpassable` is set when the straight line meets an impassable volume or no path gets there. `LeavesTable` is the
base off the slab at the destination.

`Movement.Measured(…, dangerous, unstable, unstableOver, steep?)` is the path a move is measured along when some volumes are dangerous only to a
long path (3.6.0 Unstable): `Check` round `dangerous`; when that path is longer than `unstableOver` and enters a dangerous or unstable volume,
`Check` again round both. The server's `ApplyMove` measures and tests along it; with no unstable volume it is `Check`.

`Movement.VolumesOnPath(start, path, volumes)` names the volumes the base touches along a path (p.11 "in or through");
`Movement.VolumesCrossed(start, destination, volumes)` is the same for the straight line.
`Movement.ContactDistance(start, target, limit, impassable, table, models?, units?, surfaces?)` is the shortest path to base contact with a
model, round impassable volumes and, given `models`, round them and their units' perimeters as `Check` holds a path (the target left out of
`models`; its unit among `Bases`), when it is at most `limit` (p.9's "clear path to reach it"; p.15's container in reach).
`Movement.ContactInReach` passes the same to it and to each landing's `Check`, which then fits only round the models.

## The reach and the drawn area

`MoveReach.Build(start, allowance, impassable, difficult, cap, table, models?, surfaces?, dangerous?, unstable?, unstableOver?, units?)`
settles two fields once per model per move Ask: one round the difficult volumes (walls, bodies and grown perimeters too) and one through
them. Every field, the area, the clamp and the route are held to `Held`, so the area drawn goes round each other unit, the separation out. The cap binds only when a difficult volume is in
reach: one listed out of reach caps nothing, as `Check` fits round it within the allowance. Owner ruling: *"the visible
feedback to users should just show the furthest they could move accounting for both if they cross or go around the
difficult"*, so a point is reachable when the field round difficult ground reaches it within the allowance **or** the field
through it within the cap, and the client draws that one area.

- `Route(to, shortest?)`: the path a move to `to` is measured along, `Measured`'s path: `Check`'s order over the dangerous volumes the reach was
  given, chosen again round the unstable ones past `unstableOver`; `shortest`, the shortest whatever it enters, once the unit is capped (the cap is
  judged by the shortest path). The client's ruler follows it and reads its length. Its fields round dangerous ground are settled on first use, or
  ahead by `PrepareRoute` (the client calls it on the pool when the Ask opens).
- `Clamp(target, cap?)`: the target when reachable, else the nearest point a path within the limit gets to (from the start
  and each settled corner, as far toward the target as the length left and the way allow). With `cap`, the unit is held to it.
- `Triangles(least?)`: the area as triangles. `ReachMesh` walks a quadtree over the square of the allowance, from coarse cells
  8" across (each walked on its own pool thread, the triangles joined in cell order, so the result is the same as one walk): a
  cell clear of the limit by more than its diagonal, with no shape or table edge crossing it, is drawn whole or dropped (a path
  length changes no faster than the distance moved); one inside a wall is dropped; the rest split down to about 0.25", which
  marching squares cut at the limit: interpolated along an edge where the field is smooth, bisected where an obstacle cuts
  it off; a fine cell wholly inside is two triangles, a cut one a fan from its first corner. A point whose straight line from
  the start meets no obstacle of any group takes that line's length for every field. `least`, a minimum move's straight
  distance (p.13 Aircraft), cuts the middle out.

`Movement.Region(…)` is `MoveReach.Build(…).Triangles(least)` as a `MovementRegion(Origin, Allowance,
CappedByDifficultTerrain, Triangles)`.

### Bands

`MoveReach.Build(…, dangerous, unstable, unstableOver)` takes the volumes the move Ask names dangerous to the move: `dangerous` at any length,
`unstable` only to a path longer than `unstableOver` (spec `2026-11-09-move-area-bands-design.md`). They change no reach, clamp or route. The client
draws the area split by what reaching a point costs (`BandAt`, `Bands(least?)` as `MoveBands(Clear, Difficult, Dangerous)`):

- Clear: within the allowance round every difficult volume (when the cap binds) and every volume dangerous to the path;
- Difficult: else within the cap through difficult ground, round every volume dangerous to the path;
- Dangerous: else reached at all.

Each is a level of fields settled on first use, a field whose groups (`MoveSpace.Dangerous`, `MoveSpace.Unstable`) add nothing in reach being the
reach's own; the bands and the route share them (by groups and limit). `ReachMesh` walks one quadtree for the three, a band one level less the one inside it, so the bands are disjoint and cover the area.

`MoveReach.Union(reaches, least?)` draws several models' areas as one (a move's held models): each band is the union of that band of every
model's area, so a band drawn over another (clear, difficult, dangerous on top) shows every model's, and no two triangles of one band overlap. One
walk over a box holding every area, on the first model's grid, each band's field the least of the models' at a point; a field is held within
`ReachMesh.Deep` of zero (past every coarse cell's diagonal and a fine cell's side the walk reads only its sign, so the triangles are those of the
exact least), and a model whose straight line less its allowance cannot bring a band under the least so far is not searched.
`ReachMeshReferenceTests` holds the walk to the plain walk it replaced (cells to 2", four triangles a fine cell, one thread: the same area and the
same points inside) and the union to each model's `BandAt`.

## The wire carries destinations, not paths

`Destinations` stays a list of relocations; the path is not on the wire. Each side computes the same shortest path from the
same inputs: the Ask's own parameters, the board's volumes and the models where the projection has them. The rulebook asks
whether a legal path of at most the allowance exists, not which one the player traced, so the server needs only the
destination to judge: the shortest legal path is the most generous reading, and the order above settles which terrain it
enters. A path on the wire would be one more thing to validate (each leg, each corner, its length) and would let two answers
to the same destination be judged differently. Floating-point differences between machines only move a path by
millionths of an inch; the server is the judge and the client's area and ruler are its prediction.

## Timings

Release, a 72" × 48" table with 150 models in 5-model units and four terrain pieces, the mover in the middle (17 shapes within
12"):

| What | Time |
| --- | --- |
| `MoveReach.Build`, 12" | 2.3 ms |
| `Triangles`, 12" | 11 ms (6,400 triangles) |
| `Movement.Region`, 6" | 1.0 ms |
| `MoveReach.Clamp` (a drag frame, one model) | 0.1 ms |
| `MoveReach.Route` (the ruler) | 0.004 ms |
| `Movement.Check` (the server, one model) | 0.12 ms |
| `Movement.ContactDistance` (a charge target pair) | 0.001 ms |

The area is built once per model per move Ask, the first time it is shown; a drag frame costs the clamp and the route per held
model.
