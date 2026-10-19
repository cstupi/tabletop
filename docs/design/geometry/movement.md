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
| `Bodies` | other models' bases | where the heights overlap too: the mover rested on the surfaces at each 0.1" of the part of the segment inside the shape (`Collision.Rest`), so a base passes above or below a model on another level; a model it starts on is left out |
| `Difficult`, `Dangerous` | the volumes the caller lists | only in the searches that avoid them |

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

`Movement.Check(start, destination, allowance, impassable, difficult, cap, table, models?, surfaces?, dangerous?)` returns
`MoveCheck(Distance, EffectiveAllowance, WithinAllowance, CrossesImpassable, EntersDifficult, LeavesTable, Path,
CrossesModels)`. The path is the first that fits, in order:

1. round every difficult and dangerous volume, within the allowance;
2. round every difficult volume, within the allowance;
3. round every dangerous volume, within the cap;
4. any path, within the cap.

Without a cap (Strider, Flying, Aircraft) the order is round both, round dangerous, any, all within the allowance. A player
going round a wood or a pit never chose to enter it, and the cap binds the whole unit, so keeping out of difficult ground
comes first. `EntersDifficult` is whether the chosen path's base touches a difficult volume; then `EffectiveAllowance` is the
cap. With `models`, a destination no path round them fits is tried again without them: found, it is `CrossesModels` (the
server's `move_through_models`). Nothing fits at all: the shortest path at any length gives the `Distance` a refusal quotes,
and `CrossesImpassable` is set when the straight line meets an impassable volume or no path gets there. `LeavesTable` is the
base off the slab at the destination.

`Movement.VolumesOnPath(start, path, volumes)` names the volumes the base touches along a path (p.11 "in or through");
`Movement.VolumesCrossed(start, destination, volumes)` is the same for the straight line.
`Movement.ContactDistance(start, target, limit, impassable, table)` is the shortest path to base contact with a model, round
impassable volumes, when it is at most `limit` (p.9's "clear path to reach it"; p.15's container in reach).

## The reach and the drawn area

`MoveReach.Build(start, allowance, impassable, difficult, cap, table, models?, surfaces?)` settles two fields once per model
per move Ask: one round the difficult volumes (walls and bodies too) and one through them. Owner ruling: *"the visible
feedback to users should just show the furthest they could move accounting for both if they cross or go around the
difficult"*, so a point is reachable when the field round difficult ground reaches it within the allowance **or** the field
through it within the cap, and the client draws that one area.

- `Route(to, shortest?)`: the path a move to `to` is measured along, by the same order as `Check`; `shortest` once the unit is
  capped. The client's ruler follows it and reads its length.
- `Clamp(target, cap?)`: the target when reachable, else the nearest point a path within the limit gets to (from the start
  and each settled corner, as far toward the target as the length left and the way allow). With `cap`, the unit is held to it.
- `Triangles(least?)`: the area as triangles. `ReachMesh` walks a quadtree over the square of the allowance: a cell clear of
  the limit by more than its diagonal, with no shape or table edge crossing it, is drawn whole or dropped (a path length
  changes no faster than the distance moved); one inside a wall is dropped; the rest split down to about 0.25", which
  marching squares cut at the limit: interpolated along an edge where the field is smooth, bisected where an obstacle cuts
  it off. `least`, a minimum move's straight distance (p.13 Aircraft), cuts the middle out.

`Movement.Region(…)` is `MoveReach.Build(…).Triangles(least)` as a `MovementRegion(Origin, Allowance,
CappedByDifficultTerrain, Triangles)`.

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
