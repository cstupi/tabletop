# Geometry

`libs/Tabletop.Geometry` is the measurement engine the client and the game server both run.
It answers six questions: distance, line of sight, containment, overlap, reachable region, and
whether a shape is on the table. Every answer is a **2D shape intersection plus a vertical
range**. Convex hulls were dropped for extruded prisms in `architecture.md` → Geometry. It
knows nothing of Godot, of a ruleset, or of persistence: OPR's numbers arrive as parameters,
and every result is a measurement, a flag, or a drawn region the caller interprets. **The
client's copy is a cosmetic prediction and the server's is the answer**, so bit-exact
cross-platform determinism is not a requirement and `Tolerance.Distance = 1e-6` is the whole
numerical policy.

## Scope

In scope, all of it inside `libs/Tabletop.Geometry`:

| Subject | Type | Task |
| --- | --- | --- |
| Point helpers, point-in-polygon | `Point2`, `Polygon`, `Tolerance` | 1 |
| The one internal convex shape | `Outline`, `CircleOutline`, `PolygonOutline` | 2 |
| Values to world outlines | `Bodies`, `WorldVolumes` | 3 |
| Edge-to-edge measurement | `Distance` | 4 |
| Surface settling | `Settling` | 5 |
| Standing in and on terrain | `Containment` | 6 |
| Base overlap, terrain intersection | `Overlap` | 7 |
| Line of sight | `LineOfSight`, `LineOfSightResult` | 8 |
| The geometric half of the cover rule | `Cover` | 9 |
| Unit coherency | `Coherency`, `CoherencyResult` | 10 |
| The slab | `Table` | 11 |
| The movement region from server parameters | `Movement`, `MovementRegion`, `MoveCheck` | 12 |
| The built-in deployment zone styles | `ZoneStyles` | 13 |

Not in scope:

- **The rules.** No legality decision, no majority-of-models rule, neither of the owner's two
  50% cover thresholds, no `+1 to Defense`, no 6" difficult cap of its own. Facts are returned
  per model and the caller counts. `architecture.md` → Rules enforcement: flagged, never
  prevented.
- **The terrain generators and the built-in terrain inventory.** The setup phase spec ships the
  four planet tables with the client, and the server never reads them. The client shell plan
  owns `TerrainGenerators` and `BuiltinContent` in `client/src/Core/`. The zone styles are the
  one piece of built-in setup content that does come into the library, because the server
  checks a deployment against them too.
- **Small tables.** The owner ruled on 2026-09-06: *"We don't need to support skirmish at
  all."* The seven built-in zone styles therefore assume a table near 72×48, and the 30×22
  skirmish size the first pass argued from is not a target. `Table` and `TableSize` still take
  any positive size; it is the styles' absolute insets that stop working, at the thresholds
  named under Failure modes. Spec correction 11 records this against `architecture.md` →
  Table and layout, whose `Table` bullet reads *"Arbitrary sizes … Never a constant"*.
- **Geodesic movement**, **exact ellipse math**, **polygon clipping** (area coverage),
  **convex hulls**, and **unit conversion**. Each is recorded in Open questions with the
  assumption taken instead.
- **Persistence and the wire.** The library has zero package dependencies. `Tabletop.Protocol`
  serialises these value types, and nothing here reads a row.

Position in the build order: **step 2**, `architecture.md` → Build order, in parallel with
step 3 and depending only on step 1. The prerequisite is the contracts plan
(`docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md`) Tasks 1–3: the project, the
solution, the test project, the value types in `Points.cs` / `Bases.cs` / `Volumes.cs` /
`Placements.cs`, and the declared-but-throwing surface in `Api.cs`. Nothing else in the
repository is a prerequisite, and there is no consumer yet. `libs/` holds only a `.gitkeep`
today.

`Api.cs` is a set of `NotImplementedException` stubs whose signatures are frozen contract, and
`ApiSurfaceTests.cs` asserts that they throw. Each stub moves into its own file as its task
implements it. `Api.cs` is deleted in Task 8 when `LineOfSight` leaves it, and
`ApiSurfaceTests.cs` is deleted in Task 4 in favour of behaviour tests. **No declared signature
changes; members are only added.**

## Context

```mermaid
flowchart TD
    subgraph client["client/ (Godot 4.7, Windows)"]
        TV["Table view, ruler, region overlay"]
    end
    subgraph gs["server/game/ (Linux)"]
        GR["GameRules, sequences"]
        BD["Board, DraftEditor"]
    end
    GEO["libs/Tabletop.Geometry"]
    PROTO["libs/Tabletop.Protocol"]
    PG[("Postgres")]
    WEB["web/ (volume editor)"]

    TV -->|"predicts: region, ruler, settling, LoS"| GEO
    GR -->|"adjudicates: the same calls, authoritatively"| GEO
    BD -->|"Table.ContainsPoint at end-setup"| GEO
    PROTO -->|"value types on the wire"| GEO
    GR -->|"MoveRegion parameters, Asks"| PROTO
    PROTO -->|"Ask, events"| TV
    BD -->|"table_state, terrain_volume rows"| PG
    WEB -->|"authors Volume rows"| PG
    PG -->|"volumes, bases, table size"| BD
```

The library sits below both runtimes and above nothing. It has no inbound dependency, and two
consumers that must agree.

Three seams:

**Geometry to protocol.** `Tabletop.Protocol` does not wrap these types. It uses them.
`Point3`, `Yaw`, `Placement`, `TableSize`, `Volume` and `TerrainTransform` are on the wire
verbatim, so the coordinate frame is a library constraint rather than a client convention.
`TerrainPlacement.WorldVolumes()` in `Tabletop.Content` produces the
`IReadOnlyList<WorldVolume>` that every call here takes. Nothing converts anywhere. `Polygon`
is the one type that never crosses the wire. A deployment zone travels as a shape **id**
(`"frontline/1"`), and both ends call `ZoneStyles.Resolve` to get the polygons back.

**Geometry to the game server.** The server owns which volumes count. `MoveRegion` carries
`ImpassableVolumeIds`, `DifficultVolumeIds` and the `DifficultCap` that `GameRules` computed.
`Movement.Region` takes those as plain parameters, so the library never references the protocol
record. `GameRules` supplies every OPR constant at the call site: coherency `(1, 9)`, the 1"
separation, the 6" cap, melee reach `EdgeToEdge <= 2 && Vertical <= 4`, objectives
`EdgeToMarker <= 3`, Ambush `> 9`, Scouts `<= 12`.

**Geometry to the client.** The client draws consequences, never answers. It resolves the id
lists to `WorldVolume`s, calls `Movement.Region`, and draws `.Boundary` as a polygon. It calls
`Settling.SurfaceHeightAt(new Point3(x, y, currentZ), volumes)` on every drag frame. It builds
a presence `Measurement(From, To)` from `Distance.ClosestPoints`. When a server event disagrees
with any of it, the model snaps. That is the entire cost (`architecture.md` → Geometry).

## Design

### 1. The coordinate frame

No spec said where the origin was or which way yaw turned. This plan first assumed the table's
**centre**. The compatibility review of 2026-09-05 ruled the **corner**, and the owner ratified
that on 2026-09-06 (plan question 1, spec correction 4). Every affected file matches. Three
reasons carried the ruling: the game server plan's `DraftEditor.OnSlab` and the contracts plan's
wire samples already used `0 <= x <= width`, nothing in the specs preferred the centre, and one
frame everywhere removes a conversion layer from the client. This is the only decision in the
module that the wire, the database and both runtimes depend on simultaneously.

```mermaid
flowchart LR
    O(("origin<br/>0,0,0")) -->|"X along TableSize.Width, 0..W"| XE(("W,0"))
    O -->|"Y along TableSize.Length, 0..L"| YE(("0,L"))
    O -->|"Z up from the slab top face"| ZE(("z"))
    XE --- FAR(("W,L"))
    YE --- FAR
    Y0["Yaw 0 faces +Y"] -.->|"positive yaw turns counter-clockwise seen from above"| Y90["Yaw 90 faces -X"]
```

A position is on the slab when `0 <= x <= W` and `0 <= y <= L`. `Point2.Rotate(new Yaw(90))`
sends `(0,1)` to `(-1,0)`.

The dependent conventions, all in the plan's Global Constraints:

| Convention | Consequence in code |
| --- | --- |
| One world unit = one inch, angles in degrees | No conversion anywhere; millimetres never enter |
| Yaw only, always upright | `Outline` is 2D; settling sets height, never tilt |
| An oval's `A` (length) lies along the facing axis | `BaseOutline` builds `(B/2 cos t, A/2 sin t)` then rotates |
| A rect footprint's `A` is local X, `B` local Y | Matches `Footprint.Rect(width, length)` |
| `Scale` multiplies footprint dimensions, offset, `Bottom` and `Top` | `WorldBottom` = `Position.Z + Bottom * Scale` |
| `Position.Z` is added to both heights | A piece raised 1" raises its whole prism |

The last two are the whole of the terrain transform. The scale is uniform: a per-axis scale
would stretch a circular footprint into an ellipse, a shape the geometry does not have
(geometry values → The terrain transform).

### 2. Extruded prisms, not convex hulls

`architecture.md` originally listed boxes, cylinders **and convex hulls** as collision shapes.
Geometry values dropped the hulls. With footprints restricted to a circle or a rectangle and
bases restricted to a circle, an oval or a square, every test in the library factors into two
independent problems:

1. a **2D shape intersection or distance** between two convex outlines on the table plane, and
2. a **1D interval test** between two vertical ranges `[bottom, top]`.

No hull construction, no 3D separating-axis test, and no physics engine: `Godot physics is not
in the scene` (client shell → Geometry). A `bottom`/`top` pair rather than a single height then
covers every case with one mechanism. A hill is `0-2`, a wall is `0-4` with a thin footprint, a
second storey is `4-8` with nothing beneath it, and a forest on a hill is a Solid `0-2` with an
Area `2-8` on top.

The costs. An uploaded mesh's visual detail lies outside its hitbox. A sloped roof is a box. An
ellipse is not representable, so an oval base becomes a convex 64-gon
(`Bodies.OvalVertices = 64`, error under 0.2% of the radius). The 64-gon is inscribed, its
vertices on the ellipse, so distances to an oval are slightly over-reported and overlaps
slightly under-detected. `DistanceTests` compares oval results at three decimal places rather
than six.

### 3. `Outline`, the one 2D shape

`Outline.cs` holds the entire geometric core, and all of it is `internal`. Consumers see only
`PlacedModel`, `WorldVolume`, `Polygon`, `Point2/3` and the result records. The test project
reaches the internals through `InternalsVisibleTo`, added to the csproj in Task 1.

```mermaid
classDiagram
    class Outline {
        <<internal abstract record>>
        +Contains(Point2) bool
        +BoundaryDistance(Point2) double
        +ClosestPointTo(Point2) Point2
        +DistanceTo(Point2) double
        +IntersectsSegment(Point2, Point2) bool
        +BoundaryPoints(int) IReadOnlyList~Point2~
        +ContainsOutline(Outline) bool
        +Separation(Outline a, Outline b) static
        +InteriorsIntersect(Outline a, Outline b) static
        +ClosestOnSegment(Point2 a, Point2 b, Point2 p) static
        +SegmentsIntersect(Point2 p1, Point2 p2, Point2 q1, Point2 q2) static
    }
    class CircleOutline {
        +Point2 Centre
        +double Radius
    }
    class PolygonOutline {
        +IReadOnlyList~Point2~ Vertices
        +ClosestBoundaryPoint(Point2) Point2
        +SeparatingAxisTest(PolygonOutline a, PolygonOutline b) static
    }
    Outline <|-- CircleOutline
    Outline <|-- PolygonOutline
```

The library's whole geometric surface is a dozen members over three internal types. Everything
public is a thin composition over them.

**A circle is exact, a polygon is convex.** `CircleOutline` is a true circle, not a polygon
approximation, so a round base carries no discretisation error. Round is the commonest base.
`PolygonOutline`'s constructor rejects fewer than three vertices and **reorders the vertices
counter-clockwise** by the sign of twice the signed area. Callers may therefore hand it either
winding and `Contains`, a left-of test against every edge, stays correct. Convexity is an
unchecked precondition. Every producer inside the library builds a rectangle, a square, an
inscribed ellipse or a table rectangle, all convex by construction.

**Separation is dispatched on the pair, and it is exact.**

| Pair | Method | Exactness |
| --- | --- | --- |
| circle, circle | centre distance minus both radii, clamped at 0 | exact |
| circle, polygon | `ClosestBoundaryPoint` from the centre, minus the radius | exact |
| polygon, polygon | every vertex of A against B's boundary, and the reverse | exact |

The polygon-polygon scan is exact, not a sampling heuristic. For two convex polygons the
minimum distance is always attained with at least one **vertex** as an endpoint, so scanning
vertices against the other's boundary cannot miss it. `Separation` also returns the two witness
points, which is what a presence measurement is made of (presence → A measurement is two
points).

**Overlap is not distance.** `InteriorsIntersect` requires *positive* shared area. Touching
bases do not overlap, and neither does a model standing exactly on a hill's top face. For two
polygons it runs a separating-axis test over the edge normals of both. The axis projections are
compared with a `Tolerance.Distance` slack that makes edge contact "not intersecting". Both
shapes are convex and neither is axis-aligned in general. A square base at yaw 45 against a
rotated wall footprint is the ordinary case here.

**Segment tests** exist for line of sight. A circle intersects a segment when the closest point
on the segment is within the radius. A polygon does when either endpoint is inside or any edge
crosses. `SegmentsIntersect` uses the four-cross-product orientation test with a collinear
fallback through `Polygon.OnSegment`. That is the same tolerance-aware on-segment predicate
`Polygon.Contains` uses for boundary inclusion.

**Boundary inclusion is uniform: on the boundary is inside.** Every `Contains`, every
`ContainsPoint`, `Table.ContainsPoint`, `Polygon.Contains` and the vertical range test are
inclusive to within `Tolerance.Distance`. The bias is toward admitting a placement, consistent
with `flagged, never prevented`.

### 4. From values to outlines: `Bodies` and `WorldVolumes`

Two files convert the stored value types into world-space outlines, and they are the only
places a transform is applied. Every other class calls them.

```mermaid
flowchart TD
    PM["PlacedModel: ModelBody + Placement"] --> BS{"Base.Shape"}
    BS -->|Circle| CO["CircleOutline(centre, A/2) - exact"]
    BS -->|Square| SQ["4 local corners"]
    BS -->|Oval| OV["64 points on (B/2 cos t, A/2 sin t)"]
    SQ --> PL["Bodies.Place: Rotate(yaw) then + centre"]
    OV --> PL
    PL --> PO["PolygonOutline"]
    WV["WorldVolume: Volume + TerrainTransform"] --> WC["WorldCentre = Position.Horizontal + (Offset * Scale).Rotate(Transform.Yaw)"]
    WC --> FK{"Footprint.Kind"}
    FK -->|Circle| WCO["CircleOutline(centre, A * Scale)"]
    FK -->|Rect| WPO["PolygonOutline from half extents A*S/2, B*S/2 at WorldYaw"]
    WV --> VR["WorldBottom = Position.Z + Bottom * Scale<br/>WorldTop = Position.Z + Top * Scale"]
```

A model's Z never enters its outline; it is the vertical range. A volume's yaw is the **sum** of
the piece's yaw and the volume's own.

`Bodies` also supplies the three vertical facts a body has. `Bottom()` is the settled Z,
`Top()` is `Bottom + Height`, and `Eye()` is `(X, Y, Bottom + Height / 2)`, the centre of the
body. It supplies one adapter,
`AsBlockingVolume(Guid id, PlacedModel m)`, which expresses rulebook p.11's *"other units are
sight blockers"* without the library knowing what a unit is. The caller passes the models it
wants treated as blockers, so p.8's *"models may always ignore friendly models from their own
unit"* is the caller leaving them out. No friend/foe concept enters the library.

`AsBlockingVolume` is asymmetric. An oval or square base becomes its **bounding rectangle**
(`Footprint.Rect(b.B, b.A)`), because `Footprint` has no oval kind. A model is shot *at* as a
64-gon and blocks *as* a rectangle, and the rectangle is the larger shape. It over-blocks by
the corner area of the bounding box, about 21% of the footprint for a 2.36×1.38 oval.

### 5. Edge-to-edge distance

`Distance` is seven members of dispatch onto `Outline.Separation`. Measurement is **edge to
edge between base shapes, never centre to centre** (table domain model). Centre-to-centre is
one subtraction and is wrong by a visible margin. A 32mm round reaches 0.63" past its centre
and a 60mm oval 1.18" past its, so two of them measured centre to centre are short by nearly
two inches.

| Member | What it measures | Read from |
| --- | --- | --- |
| `EdgeToEdge(PlacedModel, PlacedModel)` | base edge to base edge, 0 when touching or overlapping | table domain model |
| `EdgeToEdge(PlacedModel, Point2)` | base edge to a bare point, 0 when the point is inside | contracts plan |
| `EdgeToEdge(PlacedModel, WorldVolume)` | base edge to a footprint edge | added |
| `EdgeToMarker(PlacedModel, Point2, double diameter)` | base edge to a marker's rim | p.6, *within 3" of a marker*; geometry values → Marker size |
| `ClosestPoints(PlacedModel, PlacedModel)` | the two witness points, each carrying its own base's Z | presence → A measurement is two points |
| `InRange(PlacedModel, PlacedModel, double)` | inclusive, with tolerance | p.8 |
| `Vertical(PlacedModel, PlacedModel)` | difference of base heights | p.9, *within 2" horizontally and 4" vertically* |

Distance is **horizontal**. Height never enters it; `Height_does_not_enter_horizontal_distance`
is a test. Vertical separation is a second, separate number, matching how the rulebook writes
melee reach on p.9. A single 3D distance would collapse two rules into one and match neither.

### 6. Settling: a raycast down through Solid volumes

`Settling.SurfaceHeightAt` is the whole of `architecture.md`'s *"settling a model onto the
surface it lands on by raycasting down through terrain volumes"*. It is a linear scan, not a
physics query. The answer is the highest `WorldTop()` of a **Solid** volume whose footprint
contains the point and whose top is at or below the drop height. It defaults to `0`, the slab.

Two overloads:

- `SurfaceHeightAt(Point2, volumes)` drops from **infinity**: the highest Solid top under the
  point. This is the declared contract and the right answer for placing a model.
- `SurfaceHeightAt(Point3 from, volumes)` drops from `from.Z`: the highest Solid top **at or
  below** it. A model dragged along the ground of a ruin therefore passes *under* its second
  storey instead of teleporting onto it. The server uses the client's `To.Z` as the drop height
  when settling a `Destinations` answer.

The scan skips anything that is not `Solid`, skips a top already beaten or above the drop
height, and only then does the footprint test. Area volumes are passed straight through
(*solid tops are standable, area volumes are stood inside*, geometry values → Collision
volumes). The slab at Z = 0 is the floor of the answer rather than a volume in the list.

**"What it is resting on" is not stored.** The table domain model listed it as part of a
placement. Geometry values dropped it: settling re-derives it on demand, and terrain is locked
after setup, so nothing a model stands on ever moves. There is no cached surface anywhere.

### 7. Containment and overlap

Containment answers where a model is standing. Overlap answers whether a placement is
physically possible. Both are computed and neither is stored (table domain model → Containment
is computed, never stored). The whole-design review annotated a vocabulary collision here.
**This** containment is standing in terrain. Game flow's containment, being inside a transport
or a building, is a stored `Location.Inside` on the wire and is not this module's concern
except through `BaseFullyWithin`.

| Call | Predicate | Serves |
| --- | --- | --- |
| `Containment.VolumesContaining` | footprint contains the base **centre**, vertical range contains the base height | p.11 *mostly inside* (single models); *is activated in it* |
| `Containment.VolumesFullyContaining` | footprint contains the **whole base** | p.11 *fully inside a piece of cover terrain* (multi-model units) |
| `Containment.BaseFullyWithin(model, container, inches)` | every point of the base within `inches` of the container's base, edge to edge | p.14 `Transport(X)`, p.20 solid buildings: *"must stay fully within 6" of it when exiting"* |
| `Overlap.Bases` | positive shared area **and** positive shared vertical extent | flagged base overlap |
| `Overlap.WithSolid` | the same, against a Solid volume only | flagged terrain intersection |

The vertical range is **inclusive**, so standing on a hill counts as being in it. A hill
declaring cover and difficult applies to the model on top of it, with no flag and no special
case. `Overlap` is the mirror image. It uses `IntervalsOverlap`, which demands a *positive*
shared extent. A model standing on a Solid's top does not intersect it, a model under a `4-8`
slab does not intersect it, and a 5"-tall model standing under the same slab does.

`BaseFullyWithin` is the one place a naive implementation would have broken the module's own
rule. The obvious reading of *"fully within 6\""* is a circle of radius 6 about the container's
centre. That is centre-to-centre, which this library never does. The region tested is instead
the **container's footprint grown by `inches`**, a Minkowski sum that is never constructed.
Both outlines are convex, so the farthest point of the moving base is exact with no new
primitive:

- a **polygon** base's farthest point is one of its vertices, so test all of them;
- a **circular** base's is its centre pushed `Radius` along the outward direction, so
  `container.DistanceTo(centre) + Radius <= inches` is the answer.

When the two bases overlap, `DistanceTo(centre)` is 0 and the circular form degrades to
`Radius <= inches`. That is an upper bound. It can only refuse a placement already touching the
container, never admit one that is too far.

### 8. Line of sight: centre to any part of the target

The owner ruled the policy on 2026-09-06: *"Center to any part of target."* The specs pin what
it runs **against**: *"Line of sight is settled against the cylinder that the base and height
describe. The visual mesh is never tested"* (table domain model), and *"blocking is declared,
not derived"* (geometry values → Declarations). The declaration says the volume is **opaque**,
and geometry answers whether a given line passes through it. Neither the specs nor the ruling
fix how many lines approximate *any part*.

The shooter end is a single point at the **centre of its body**: the base centre raised half
the height. That is the only point that is the centre in all three axes; the top centre is the
centre of the top face. Half height keeps the eye inside the body, so a blocker as tall as the
shooter is opaque to it and one of half its height cuts the centre line while leaving the upper
lines clear. It also puts the centre-to-centre line in the sample set of both directions, so
mutual visibility does not depend on which model is shooting.

The target end samples the body at three heights, its base, half height and its top, with the
centre and up to eight rim points at each. Sight is clear when **any one** line reaches its
point unobstructed.

```mermaid
flowchart LR
    E(("eye = centre of the shooter's body, half height")) --> RT(("centre + 8 rim points at the target's top"))
    E --> RM(("centre + 8 rim points at half height"))
    E --> RB(("centre + 8 rim points at the base"))
    RT --> J{"any line unobstructed?"}
    RM --> J
    RB --> J
    J -->|yes| CLR["LineOfSightResult(true, [])"]
    J -->|no| OBS["LineOfSightResult(false, every blocker that stopped a line, input order)"]
```

Twenty-seven rays for a circular or oval base, fifteen for a square whose four vertices are its
whole rim. Partial exposure counts as visible. `ObstructedBy` is the union over all failed rays,
not the blockers of any one ray.

`Rim(Outline)` supplies the horizontal sample points: `BoundaryPoints(8)` evenly around a
circle, all vertices of a polygon with eight or fewer, and every n-th vertex of a larger one. An
oval's 64-gon is therefore sampled at 8 of its 64 vertices. Sampling the base as well as the top
is what makes a low wall behave. A `0-1` wall leaves the target's upper points visible and its
base points hidden, so sight is clear and `Cover.SightBlockersBetween` is non-empty. That is
p.11's *behind a sight blocker*.

**Why three rings and not two.** Every sample is a real point of the target's body, so a `Clear`
result is never wrong: the error is one-sided, under-reporting visibility when the only visible
part falls between samples. Two rings left that error unbounded vertically. A target standing
behind a wall with a canopy above it is visible only through the band between them, and with
samples at the top and the base alone that band is missed however tall it is. A ring at half
height bounds the vertical gap by half the model's height. The horizontal gap needed no such
fix: eight rim points on a 1.26" base subtend 2.8° at 10", so the sliver that slips between
adjacent rays is under 0.24" wide at mid-range, finer than any terrain a table carries. The cost
is 27 rays a pair rather than 17, each a segment-prism test against every blocker, and there is
still no broad phase.

**Counting visible models.** The owner's companion rule of 2026-09-06 is *"Seeing less than 50%
of models in a unit gives cover"*. Cover was served only by a blocker list from one model to one
model, so `LineOfSight` gained
`VisibleModels(IRL<PlacedModel> observers, IRL<PlacedModel> unit, IRL<WorldVolume> volumes) : IRL<bool>`,
index-aligned with `unit`, true at `i` when at least one observer's `Check` against `unit[i]` is
clear. A single shooter is a one-element `observers` list. The caller counts and compares; no
member of this library takes a fraction. The cost is `observers.Count × unit.Count × 27` rays,
which for ten models against ten with forty blocking volumes is 108,000 segment-prism tests per
query, on the server, once per Ask.

`SeeInAndOut` is a **modifier on blocking**, not an independent property. It means nothing on a
volume that is not opaque. `Obstructions` implements it by skipping a blocker when either
endpoint of the segment is inside it. A forest therefore blocks sight *through* it while a
model inside it can see out, a model outside can see in, and two models both inside can see
each other (rulebook p.12).

**What the sampling cannot answer:**

- **How much of the target is visible.** The result is a boolean plus a blocker list. Any rule
  phrased as *"more than half obscured"* has nothing to read. OPR does not have one; a
  Warhammer ruleset might.
- **Which part is visible.** Twenty-seven booleans are discarded down to one. A rule that cared
  whether the *head* was exposed would need the per-point results kept.
- **A gap narrower than the angular resolution.** A blocker narrower than 0.24" at mid-range,
  positioned to cover one rim point without covering its neighbours, is not seen, and a target
  visible only through a slit that narrow reads as hidden.
- **Occlusion of the eye itself.** The eye is a single point, so a model whose own body is half
  inside a blocker still shoots from the centre of it. `Obstructions` skips a `SeeInAndOut`
  blocker containing the eye, and nothing else consults the eye's surroundings.
- **The upper and lower body as firing positions.** There is one eye, not three. A model cannot
  duck under an overhang that covers its centre, nor shoot over a wall that reaches half its
  height, whatever the target.

A single centre-to-centre line is what the ruling's first half names, and the second half is
what rules it out: *any part of target* means the whole body is a valid endpoint, and one line
to the target's centre makes a low wall hide a model wholly or not at all, leaving *behind a
sight blocker* nothing to measure. A denser sample changes only `TargetPoints`; sampling the
shooter's rim as well would contradict the ruling's *center*.

### 9. Cover

`Cover` exists so that `GameRules` can read p.11's cover rule without doing geometry of its own:
*"If the majority of models in a unit are fully inside a piece of cover terrain or behind a
sight blocker (for multi-model units), or that are mostly inside cover terrain or behind sight
blockers (for single-model units), they get +1 to Defense rolls"*.

| Rulebook phrase | Geometry call | Whose job |
| --- | --- | --- |
| *fully inside a piece of cover terrain* | `Cover.InsideCoverVolumes` non-empty | geometry |
| *behind a sight blocker* | `LineOfSight.Check` clear **and** `Cover.SightBlockersBetween` non-empty | geometry |
| *mostly inside cover terrain* | `Containment.VolumesContaining` filtered to `Cover` | geometry |
| seeing fewer than half a unit's models (owner, 2026-09-06) | `LineOfSight.VisibleModels` count against the list length | geometry counts, `GameRules` compares |
| *the majority of models*, the owner's two 50% thresholds, *+1 to Defense* | — | `GameRules` |

The owner ruled the two thresholds on 2026-09-06: *"50% models in unit in cover for it to
count"* and *"Seeing less than 50% of models in a unit gives cover"*. Every fact above is per
model, and no member of this library takes a fraction, so the counting and the comparison are
the ruleset's. Containment needed nothing new, since `InsideCoverVolumes` and
`VolumesContaining` are already called per model; visibility did, and `LineOfSight.VisibleModels`
is section 8's answer.

`SightBlockersBetween` draws the same twenty-seven rays as `Check` and returns every blocker that
stopped at least one of them, whether or not sight is clear overall. That is the only way to
express *behind*. A wall that blocks nothing is not cover, and a wall that blocks everything is
a failed shot rather than cover.

Two consequences. The server calls `Check` and `SightBlockersBetween` back to back for the same
pair, doing the ray work twice. A combined call returning both would halve it. And the
predicate is permissive: a 1"×1" pillar half an inch off the line stops one rim ray and
therefore grants cover. Whether that matches table play is a rules question, but this code
decides it.

### 10. Coherency

Rulebook p.7: *"All models in a unit must always stay within 1" of at least one other model,
and must stay within 9" of all other models … forming an uninterrupted chain of models in 1"
coherency with each other"*. Two conditions, and they are not the same shape of test:
connected components, then a spread check.

`Coherency.Check(models, chainDistance, maxSpread)`:

1. build the full `n × n` edge-to-edge distance matrix;
2. find connected components under `distance <= chainDistance`, by depth-first search;
3. the **largest** component is the unit's body. Ties keep the one holding the earliest model,
   which falls out of numbering components by first-encountered index;
4. a model outside the body is out of coherency; so is a model inside it that is further than
   `maxSpread` from **any** other model in the body.

The rulebook's `1"` and `9"` are the caller's, per the constraint that no OPR number lives in
this library. `CoherencyResult` reports indices into the input rather than ids, because the
library has no model identity. The caller zips them back.

Two properties. The spread test flags **both** ends of an over-long chain (the plan's
`A_long_chain_breaks_the_spread` expects `{0, 6}`), so the result is the set of models
participating in a violation, not the minimal set to move. And the matrix is O(n²) with a full
outline separation per pair. For a 20-model unit of ovals that is roughly 190 pairs of
64-gon-versus-64-gon separation.

### 11. The slab

The table is *a rectangle. No shape field and no play-area boundary volume* (setup content →
The table is a slab), so `Table` is three members over `TableSize`:

- `Bounds(TableSize)`: the four corners as a `Polygon`, in the corner frame;
- `ContainsPoint(TableSize, Point2)`: edge inclusive, `0 <= x <= W && 0 <= y <= L`. This is the
  game server's `DraftEditor.OnSlab` rule, and it deletes off-slab terrain at end-setup
  (setup phase → Terrain: *"A piece still off the slab when setup ends is dropped from the
  snapshot"*);
- `ContainsBase(TableSize, PlacedModel)`: the **whole base** on the slab, rulebook p.7
  *"models may also never move outside of the battlefield (no part of them, at any point)"*.

`ContainsPoint` is an axis comparison and `ContainsBase` goes through `PolygonOutline`. The two
agree. They are two implementations of the same predicate.

### 12. The movement region

`architecture.md` → How the client stays responsive is the brief for this class:
*"The server sends parameters, not an answer: move allowance, terrain effects, and the ids of
volumes that cannot be crossed. `Tabletop.Geometry` renders the region from those parameters."*
The same section records *"Terrain multipliers" was an invention*. OPR's difficult terrain
**caps a unit's whole move at 6"** rather than scaling it. The parameter is therefore
`double? difficultCap`, whatever `GameRules` computed, and `null` when the ruleset has none.

`Movement.Region(start, allowance, impassable, difficult, difficultCap, table)` returns
`MovementRegion(Origin, Allowance, CappedByDifficultTerrain, Boundary)`, one boundary vertex
per ray at `Rays = 180`. The client draws it as a polygon.

```mermaid
flowchart TD
    A["for each of 180 rays, dir = (cos, sin)"] --> B["limit = allowance, reached = 0"]
    B --> C["k = 1, 2, 3 ...; d = min(k * Step, limit), Step = 0.25"]
    C --> D["displace the base to origin + dir * d"]
    D --> E{"base touches an impassable volume,<br/>or leaves the slab?"}
    E -->|yes| STOP["stop; keep the last good d"]
    E -->|no| F{"difficultCap set and below limit,<br/>and the base touches a difficult volume?"}
    F -->|yes| G["limit = cap; CappedByDifficultTerrain = true"]
    G --> H{"d beyond limit?"}
    H -->|yes| STOP
    F -->|no| I["reached = d"]
    H -->|no| I
    I --> J{"d has reached limit?"}
    J -->|no| C
    J -->|yes| STOP
    STOP --> K["boundary vertex = origin + dir * reached"]
```

The region is a **star-shaped polygon** sampled at 2° intervals. The obstacle test displaces
the *base outline*, not a point. `d` is clamped to `limit`, so a non-multiple allowance such as
7.3" is reached exactly.

**Star-shaped, not geodesic** (plan question 4). A geodesic region, true reach around a corner
via a visibility graph over the footprint polygons, is a materially larger piece of work and
buys nothing that survives. The region is a cosmetic prediction, the server judges the landed
move, and the client shell's ruler readout is *"the base's displacement from where it started"*
(client shell → Input and presence). The rulebook measures the same way: p.7 says *"no part of
their bases move further than the total movement distance"*, not path length. Two visible
compromises. A model cannot be shown as able to step around a wall it can in fact step around.
And the region says nothing about whether the path taken to a reachable point was legal.

**Marching cost.** There is no bisection or refinement pass. The boundary is quantised to
`Step = 0.25`, and near an obstacle it under-reports reach by up to a quarter inch. The plan's
test asserts a range (`Assert.InRange(r.Boundary[0].X, 2.5, 2.87)`) rather than a value.
Bisecting the last interval would cost about five more samples per ray and take the error to
1e-2. The march costs 180 rays × up to `allowance / 0.25` samples. Each sample constructs a
base outline and runs `InteriorsIntersect` against **every** volume in both lists. There is no
bounding-box or bounding-circle broad phase anywhere in `Outline`. For a 6" allowance that is
roughly 4,300 displaced-base tests per volume. With an oval base (a 64-gon, 68 SAT axes) and
ten impassable pieces it is tens of millions of dot products for one region. It runs once per
move Ask rather than per frame. It is the first thing to profile and the cheapest thing to fix.

`Movement.Check(start, destination, …)` judges one straight move against the same parameters and
returns `MoveCheck(Distance, EffectiveAllowance, WithinAllowance, CrossesImpassable,
EntersDifficult, LeavesTable)`. `Distance` is the base's **displacement**, which is the ruler
readout. `EffectiveAllowance` applies the cap when the path enters difficult terrain, so the
client shows remaining allowance as `EffectiveAllowance - Distance`.

`Movement.VolumesCrossed(start, destination, volumes)` is the **in or through** test, and the
sixth declaration needs it. Rulebook p.11 fires a dangerous terrain test when a model *"moves
in or through dangerous terrain"*. The landing position alone cannot answer that, because the
model may have crossed the pit and ended past it. The plan's test states it: `VolumesCrossed`
names the pit while `Containment.VolumesContaining` at the destination is empty. The same
primitive answers *"moves in or through difficult terrain"*, and `Check.EntersDifficult` is its
boolean form over the difficult list.

### 13. The disembark region

A unit inside a container has no position to move from, so `Movement.Region` cannot draw its
exit. `MoveRegion.Containment` (protocol, spec correction 9, added 2026-09-06 by the containment
coverage gap) carries `MoveContainment(Enterable, ExitingId, ExitWithin)`. When `ExitingId` is
set the client does not call `Region` at all. It draws the set of placements that
`Containment.BaseFullyWithin(candidate, container, ExitWithin)` admits, which is the container's
base grown by the distance, per section 7. `Allowance` in that Ask is the action's remaining
distance and binds nothing.

The two are never both set: *a unit may not embark and disembark in one activation*
(game flow → Inside a container). Embarking needs no region. One model reaching the
transport embarks the whole unit, so the answer is a `Destinations` whose `To` is
`Inside(container)` for every model.

### 14. The six declarations, read geometrically

Six flags, and they are OPR's own terrain types, read from the Grimdark Future and Age of
Fantasy advanced rulebooks whose terrain sections are word-for-word identical. **No effects are
stored.** A flag says *this is difficult*. The ruleset says what that costs. How each flag is
read here:

| Declaration | Read by | Geometrically |
| --- | --- | --- |
| `Impassable` | `Movement.Region`, `Movement.Check` | the server names the volumes; a ray stops where the displaced base's interior would intersect a footprint |
| `BlocksLineOfSight` | `LineOfSight.Blockers` | the only filter on the volume list; a segment through the prism obstructs |
| `SeeInAndOut` | `LineOfSight.Obstructions` | a modifier: skip this blocker if either endpoint is inside it |
| `Cover` | `Cover.InsideCoverVolumes` | whole base inside the footprint and vertical range |
| `Difficult` | `Movement.Region`/`Check`, `VolumesCrossed` | *in or through*, not the landing position |
| `Dangerous` | `Movement.VolumesCrossed` | the same *in or through* primitive, over the dangerous list |

Which classes read a flag at all: `LineOfSight` and `Cover` filter on declarations themselves.
`Movement` does **not**. It is handed two pre-filtered lists, because the server already
decided which volumes count for this move. `Containment`, `Overlap`, `Settling` and `Distance`
read no declaration at all; they read `VolumeKind` (Solid or Area) or nothing. `Impassable` is
orthogonal to Solid-versus-Area, so all four combinations are real: a climbable hill, an
impassable building, a walkable forest, an impassable lake. Neither axis implies the other.

### 15. Deployment zone styles

Deployment zones are the one piece of built-in setup content that lands in this library. That
is a reversal. The contracts plan said *"Not here, by decision: deployment zone style
resolution … the client shell plan owns it"*. It moved because the **server** checks a
deployment against a zone too (`ZoneStyles.BaseFullyWithin` for the deploy Ask), and a resolver
living in the client cannot be called by the server. Spec correction 1 records the reversal and
was applied to the contracts plan.

Three facts the rulebook settles, all from geometry values → Deployment shapes:

1. **A zone is a set of shapes, not one shape.** Disordered gives each team two triangles, so
   `Resolve` returns `IReadOnlyList<Polygon>`.
2. **The shapes are polygons.** Triangles and diagonal bands are load-bearing. Unlike terrain
   volumes this costs nothing: a zone is *"a flat point-in-polygon test with no collision, no
   settling and no height"*.
3. **Measurements are anchored, not absolute and not normalized.** The rulebook writes absolute
   inches on an assumed table: 12", 24", 36", 54". Normalized fractions would scale 12" on a
   48" table into 11" on a 44" one, which the rules do not say. Anchoring to table features
   reads as the rulebook writes it. **Side Battle is the one exception**, ruled proportional by
   the owner on 2026-09-06 and recorded as spec correction 10. Six of the seven styles remain
   anchored.

**The zone styles assume a table near 72×48.** The owner ruled on 2026-09-06: *"We don't need
to support skirmish at all."* Every argument about absolute versus proportional insets was an
argument about small tables, and the 30×22 skirmish table the first pass argued from is not a
target. That ruling is what makes the split between one proportional style and six anchored
ones acceptable rather than merely tolerated. Side Battle's fractions were chosen to reproduce
the p.21 diagram's 54" and 30" legs on 72×48, so at 72×48 the two conventions are the same
polygon, and they part company in proportion to the table's departure from it. The tables where
the divergence is large are the tables that are out of scope. What the absolute insets do below
their own size is an accepted limit, listed under Failure modes with the threshold per style,
and no longer an open thread.

**The dimensions came from the page's diagrams, not its text.** Grimdark Future v3.4.1 p.21
names the six basic styles, Frontline, Ground War, Side Battle, Disordered, Spearhead and
Opposing Forces, and gives their dimensions only as labels on six figures. The plan records
them as read from the six extracted diagram images on a 72×48 table (re-extracted with pypdf
and re-read 2026-09-06, every figure a label on its own diagram). One diagram is internally
inconsistent. Side Battle's 54" and 30" legs put the perpendicular gap between the two
hypotenuses at 24.5", not the 24" its arrow says. The legs are the shape and the band is
illustration (question 6, ruled 2026-09-06).

A shape id is `"<style>/<n>"`, `n` the team ordinal the setup phase assigns in order.
`ShapesOf(style)` is the list `SetZoneStyle.Shapes` carries. The server's `DraftEditor` writes
one `deployment_zone` row per shape keyed by ordinal `1..n` and stays a pure function over
strings. `SwapZones` swaps two ordinals' shapes, which is how p.6's *"the winner picks an
edge"* works without the app running a roll-off.

Resolution happens in a canonical **u,v frame**: `u` along the long edge from 0, `v` along the
short edge from 0. `Frame.ToTable` swaps the pair when `Length > Width`. "Long edge" is the
longer dimension. A square table takes X as long.

```mermaid
flowchart TD
    subgraph tbl["Side Battle on 72 x 48, u along the long edge"]
        C0(("0,0")) -->|"0.75a = 54"| P1(("54,0"))
        C0 -->|"0.625b = 30"| P2(("0,30"))
        P1 -.->|"hypotenuse: side-battle/1"| P2
        C1(("72,48")) -->|"0.75a = 54"| P3(("18,48"))
        C1 -->|"0.625b = 30"| P4(("72,18"))
        P3 -.->|"hypotenuse: side-battle/2"| P4
    end
```

Side Battle **scales with the table**, ruled by the owner on 2026-09-06: its legs are `0.75`
of the long edge and `0.625` of the short, which is the diagram's 54" and 30" on 72×48 and
45" and 27.5" on a 60×44 table. The ruling overrides fact 3 above for this style
alone, which is why it carries a spec correction rather than a note. The anchored reading it
replaces, `Leg(edge) = max(0, edge - 18)`, had to invent a constant no page states, and
degenerated to a zero-area triangle at 18"; a fraction of a positive edge is always positive.

The seven styles as implemented:

| Style | Shapes each | Geometry, in the u,v frame |
| --- | --- | --- |
| `frontline` | 1 | 12" bands along both long edges. Also p.6's core deployment, *"fully within 12" of their table edge"* |
| `ground-war` | 1 | 24" bands along both short edges |
| `side-battle` | 1 | a triangle in each of two opposite corners, legs `0.75a` and `0.625b`. The only proportional style |
| `disordered` | 2 | a diamond through the four edge midpoints; each team keeps the two triangles on its long edge |
| `spearhead` | 1 | a triangle on each short edge, apex 24" in at the edge's centre |
| `opposing-forces` | 1 | a 24"×24" square in two diagonally opposite corners |
| `free-for-all` | 1 | p.49's *"2'x1' area"* in each of four corners, read as 24" along the long edge by 12" along the short |

`Contains(shapeId, table, point)` is the flat test. `BaseFullyWithin(shapeId, table, model)` is
p.6's *"fully within"*, the whole base inside **one** of the zone's polygons. Unknown styles and
malformed ids throw `ArgumentException` rather than returning empty, so a typo in a stored
`deployment_zone.style` fails loudly. The p.22–23 styles are not shipped here.

### 16. Determinism and tolerance

`architecture.md` → Geometry: *"This is why bit-exact cross-platform determinism is not a
requirement here, even though the client is Windows and the server is Linux. Divergence cannot
compound, because the client holds no authoritative state to diverge into."*

What that buys this design:

- **`double` throughout**, and free use of `Math.SinCos`, `Math.Sqrt` and `Enumerable`. No
  fixed-point arithmetic, no deterministic transcendental library, no ordering discipline
  imposed on floating-point accumulation.
- **One tolerance, one constant.** `internal static class Tolerance { const double Distance =
  1e-6; }`, used for boundary inclusion, interval overlap, SAT slack, on-segment tests and
  range inclusivity alike. Tests compare with `Assert.Equal(expected, actual, 6)`.
- **Approximations may be visible.** A 64-gon oval, a 0.25"-quantised region boundary,
  twenty-seven sampled sight lines, 180 rays. Each would be a correctness bug in a lockstep
  architecture. Here each is a tuning constant.
- **Both sides may disagree.** Turn-based play means no rollback netcode
  (`architecture.md` → Undo and the presence design). If the client says 4.7399", the server
  says 4.7401" and the range was 4.74", the server's answer stands. The visible consequence is
  a model snapping or a target greying out on the next event.

This does not license disagreement about **conventions**. A different origin, a different yaw
direction, or an oval's length along the wrong axis is not a tolerance question. It is a
different answer. The frame is therefore stated in Global Constraints, restated in `Table`'s
doc comment, and carried as spec correction 4 into geometry values.

## Key decisions

Contested or reversed decisions first.

| Decision | Reason | Alternative rejected | Source |
| --- | --- | --- | --- |
| **Origin at a table corner**, X along Width `0..W`, Y along Length `0..L`, Z up from the slab top; yaw 0 faces +Y, positive yaw counter-clockwise | The game server plan's `DraftEditor.OnSlab` and the contracts plan's wire samples already used it; one frame on the wire, in the library and in both runtimes removes a conversion layer | **Table centre**, which this plan assumed first | Ruled 2026-09-05 by the compatibility review, ratified by the owner 2026-09-06; plan question 1, spec correction 4 |
| **Zone style resolution lives in the library** | The server checks a deployment against a zone (`BaseFullyWithin`); a client-side resolver cannot be called by the server | Contracts plan's explicit *"Not here, by decision … the client shell plan owns it"* | Reversed by geometry plan spec correction 1, applied to the contracts plan |
| **The built-in zone styles assume a table near 72×48**, so one proportional style beside six anchored ones is the shipped set | The owner ruled *"We don't need to support skirmish at all"*; the only place the two conventions disagree materially is a small table, and Side Battle's fractions reproduce the diagram exactly at 72×48 | Making all seven proportional, which would move 12", 24" and 36" off the numbers the rulebook prints; making Side Battle absolute, which needs a constant no page states | Ruled 2026-09-06; question 6a, spec correction 11, the thresholds under Failure modes |
| **`MoveRegion` carries `DifficultVolumeIds` and `DifficultCap`** | `architecture.md` names "terrain effects" among the parameters and says the cap is whatever `GameRules` computes; the three-field record could not carry it | The declared three-field `MoveRegion` | Spec correction 5, applied 2026-09-05 |
| **`MoveRegion.Containment` (optional)** | `Location.Inside` and `ModelRelocation` already put an `Inside` destination on the wire, but nothing told the client *where* one is legal | Leaving embark/disembark undrawable | Spec correction 9, the containment coverage gap of 2026-09-06 |
| **Convex hulls dropped** | Every test becomes a 2D intersection plus a vertical range; no hull math, and moving a model through terrain stays cheap | `architecture.md`'s original boxes, cylinders **and convex hulls** | geometry values → Collision volumes |
| **Blocking is declared, not derived** | A chain-link fence is solid and transparent. The declaration says *opaque*; geometry answers whether a line passes through | An earlier draft where Solid volumes blocked sight geometrically | geometry values → Declarations |
| **`impassable` is a declaration of its own** | Without it a hill and a building are indistinguishable | The earlier five-declaration set | geometry values → Declarations |
| **"What it is resting on" is not stored** | Settling re-derives it; terrain is locked after setup, so nothing a model stands on ever moves | The table domain model's stored surface reference | geometry values → Height, position, facing |
| **Enforcement is advisory here** | Every result is a measurement, a flag or a region; `strict` refusal lives in the server | Geometry returning legality | Ruled 2026-09-04 in the whole-design review, ruling 1 |
| **`LineOfSight.VisibleModels`**, one boolean per model of a unit | The owner's *"Seeing less than 50% of models in a unit gives cover"* is a count over a unit, and cover was served only by a blocker list from one model to one model | A single boolean for the unit, or a member taking the 50% fraction, which would put a threshold in the library | Ruled 2026-09-06; Task 8, section 9 |

Decisions the plan took because **no spec said anything**. The owner ruled on four of them on
2026-09-06; the rest remain questions under *Open questions*:

| Decision | Reason | Alternative rejected | Where |
| --- | --- | --- | --- |
| Sight lines: eye at the centre of the shooter's body, half height; target sampled at its centre and 8 rim points at base, half height and top; clear if any line is clear | The owner's *"Center to any part of target"*; the mid-height ring bounds the vertical sampling error that two rings left unbounded | One centre-to-centre line; two rings; sampling the shooter's rim, which the ruling's *center* excludes | Ruled 2026-09-06; Task 8, question 2 closed |
| *Mostly inside* (single models) = base centre inside the volume | The whole-base test already serves *fully inside*; a centre test is exact and needs no new primitive | More-than-half-the-area, which needs polygon clipping and a `BaseCoverage` member | Confirmed by the owner 2026-09-06; Task 6, question 3 closed |
| The movement region is **star-shaped** about the start | It is a cosmetic prediction; the server judges the landed move, and the ruler measures displacement | Geodesic reach via a visibility graph; a plain disc | Task 12, question 4 |
| Ovals are convex 64-gons | Error under 0.2% of the radius, inside the tolerance the architecture allows | Exact ellipse-ellipse distance: no closed form, needs an iterative solver on both sides | Task 3, question 7 |
| Free-for-all's *2'x1'* is 24" along the long edge by 12" along the short | p.49 gives no orientation | The other way round, a two-number change per corner | Confirmed by the owner 2026-09-06; Task 13, question 5 closed |
| Side Battle legs are `0.75` of the long edge and `0.625` of the short | The owner ruled the style proportional to the table; those fractions are the p.21 diagram's 54" and 30" on 72×48 | `edge - 18`, which invents a constant no page states and degenerates at 18"; fixed 54"/30" legs, which the ruling overrides and which overrun any table shorter than 54" or narrower than 30" | Ruled 2026-09-06; Task 13, question 6 closed, spec correction 10 |
| `Rays = 180`, `Step = 0.25` | 2° angular resolution and a quarter-inch radial one, for a region redrawn once per Ask | Fewer rays with interpolation; a bisection refinement pass | Task 12 |
| `RimSamples = 8`, `OvalVertices = 64` | Eight rim points subtend 2.8° on a 1.26" base at 10", so the sliver between adjacent rays is under 0.24" at mid-range; 64 keeps the ellipse error negligible | Sixteen rim points, at twice the ray cost for a gap nothing on a table occupies; any other constants. Both are `public const`, so a caller can see them but not change them | Tasks 3 and 8 |
| `BaseFullyWithin` takes a `PlacedModel` container only | Only `Transport(X)` is automated; a solid building holds models through the client's menu, which places by hand | A `WorldVolume` overload: five lines and one test if a building must offer the same region | Task 6, question 8 |

## Interfaces

### Produced (namespace `Tabletop.Geometry`; `IRL<T>` is `IReadOnlyList<T>`)

Everything the contracts plan declared keeps its signature. Rows marked *added* are new to this
plan.

| Type | Members |
| --- | --- |
| `Point2` | *added:* `+`, `-`, `*(double)`, `double Length`, `double DistanceTo(Point2)`, `double Dot(Point2)`, `double Cross(Point2)`, `Point2 Rotate(Yaw)` |
| `Polygon` | `bool Contains(Point2)`, implemented. Boundary inclusive, any vertex order, concave allowed |
| `static class Bodies` *added* | `const int OvalVertices = 64`; ext. `double Bottom(this PlacedModel)`, `double Top(this PlacedModel)`, `Point3 Eye(this PlacedModel)`; `WorldVolume AsBlockingVolume(Guid id, PlacedModel)` |
| `static class WorldVolumes` *added* | ext. `double WorldBottom(this WorldVolume)`, `double WorldTop(this WorldVolume)`, `Point2 WorldCentre(this WorldVolume)`, `Yaw WorldYaw(this WorldVolume)` |
| `static class Distance` | `double EdgeToEdge(PlacedModel, PlacedModel)`, `double EdgeToEdge(PlacedModel, Point2)`; *added:* `double EdgeToEdge(PlacedModel, WorldVolume)`, `double EdgeToMarker(PlacedModel, Point2 centre, double diameter)`, `(Point3 OnA, Point3 OnB) ClosestPoints(PlacedModel, PlacedModel)`, `bool InRange(PlacedModel, PlacedModel, double range)`, `double Vertical(PlacedModel, PlacedModel)` |
| `sealed record LineOfSightResult(bool Clear, IRL<Guid> ObstructedBy)` | unchanged |
| `static class LineOfSight` | `LineOfSightResult Check(PlacedModel from, PlacedModel to, IRL<WorldVolume>)`; *added:* `IRL<bool> VisibleModels(IRL<PlacedModel> observers, IRL<PlacedModel> unit, IRL<WorldVolume>)`, `const int RimSamples = 8` |
| `static class Cover` *added* | `IRL<Guid> InsideCoverVolumes(PlacedModel, IRL<WorldVolume>)`, `IRL<Guid> SightBlockersBetween(PlacedModel from, PlacedModel to, IRL<WorldVolume>)` |
| `static class Settling` | `double SurfaceHeightAt(Point2, IRL<WorldVolume>)`; *added:* `double SurfaceHeightAt(Point3 from, IRL<WorldVolume>)` |
| `static class Containment` | `IRL<Guid> VolumesContaining(PlacedModel, IRL<WorldVolume>)`; *added:* `IRL<Guid> VolumesFullyContaining(PlacedModel, IRL<WorldVolume>)`, `bool BaseFullyWithin(PlacedModel model, PlacedModel container, double inches)` |
| `static class Overlap` | `bool Bases(PlacedModel, PlacedModel)`, `bool WithSolid(PlacedModel, WorldVolume)` |
| `sealed record CoherencyResult(bool Coherent, IRL<int> OutOfCoherency)` *added* | indices into the input |
| `static class Coherency` *added* | `CoherencyResult Check(IRL<PlacedModel> models, double chainDistance, double maxSpread)` |
| `static class Table` *added* | `Polygon Bounds(TableSize)`, `bool ContainsPoint(TableSize, Point2)`, `bool ContainsBase(TableSize, PlacedModel)` |
| `sealed record MovementRegion(Point2 Origin, double Allowance, bool CappedByDifficultTerrain, IRL<Point2> Boundary)` *added* | |
| `sealed record MoveCheck(double Distance, double EffectiveAllowance, bool WithinAllowance, bool CrossesImpassable, bool EntersDifficult, bool LeavesTable)` *added* | |
| `static class Movement` *added* | `const int Rays = 180`, `const double Step = 0.25`; `MovementRegion Region(PlacedModel start, double allowance, IRL<WorldVolume> impassable, IRL<WorldVolume> difficult, double? difficultCap, TableSize? table)`; `MoveCheck Check(PlacedModel start, Point2 destination, …same parameters…)`; `IRL<Guid> VolumesCrossed(PlacedModel start, Point2 destination, IRL<WorldVolume>)` |
| `static class ZoneStyles` *added* | `Frontline`, `GroundWar`, `SideBattle`, `Disordered`, `Spearhead`, `OpposingForces`, `FreeForAll`; `IRL<string> All`; `IRL<string> ShapesOf(string style)`; `IRL<Polygon> Resolve(string shapeId, TableSize)`; `bool Contains(string shapeId, TableSize, Point2)`; `bool BaseFullyWithin(string shapeId, TableSize, PlacedModel)` |

Internal, and visible to the test project only: `Tolerance.Distance`, `Outline`,
`CircleOutline`, `PolygonOutline`, `Polygon.OnSegment`, `Bodies.BaseOutline`, `Bodies.Place`,
`WorldVolumes.WorldOutline`, `WorldVolumes.VerticalRangeContains`, `WorldVolumes.ContainsPoint`,
`WorldVolumes.IntersectsSegment`, `Overlap.IntervalsOverlap`, `Table.Outline`,
`LineOfSight.Blockers`, `LineOfSight.TargetPoints`, `LineOfSight.Rim`,
`LineOfSight.Obstructions`.

### Consumed

| From | Type or member | Used for |
| --- | --- | --- |
| Contracts plan, Task 2 | `Point2`, `Point3`, `Yaw`, `Orientation`, `TableSize`, `Polygon`, `BaseShape`, `Base`, `ModelBody`, `FootprintKind`, `Footprint`, `VolumeKind`, `TerrainDeclarations`, `Volume`, `Placement`, `TerrainTransform`, `PlacedModel`, `WorldVolume` | every input and output |
| Contracts plan, Task 3 | `Api.cs` frozen signatures | the contract this plan fills in |
| BCL only | `System`, `System.Linq` | zero package dependencies |

### Obligations this design places on other modules

| Module | Obligation |
| --- | --- |
| Protocol (contracts plan Task 8) | `MoveRegion(ModelIds, Allowance, ImpassableVolumeIds, DifficultVolumeIds, DifficultCap, MoveContainment? Containment = null)` and `MoveContainment(Enterable, ExitingId, ExitWithin)`. `Movement.Region` takes the first five as plain parameters, so the library does not reference the record |
| Game server | Resolve both id lists from `TerrainPlacement.WorldVolumes()`; pass other units through `Bodies.AsBlockingVolume`, leaving out the shooter's own unit (p.8); settle a `Destinations` answer with the `Point3` overload using the client's `To.Z`; owe a dangerous-terrain test from `Movement.VolumesCrossed`, not from the landing position; delete off-slab terrain with `Table.ContainsPoint`; apply the owner's two 50% cover thresholds itself, counting `Cover.InsideCoverVolumes` per model and `LineOfSight.VisibleModels` over the target unit; supply every OPR constant from `GameRules` |
| Client shell | Draw a disembark region from `Containment.BaseFullyWithin` rather than `Movement.Region`; send `ZoneStyles.ShapesOf(style)` as `SetZoneStyle.Shapes`; draw `ZoneStyles.Resolve(zone.Shape, table.Size)`; build `Measurement(From, To)` from `Distance.ClosestPoints`; draw `Movement.Region(...).Boundary`; show `Movement.Check(...).Distance` as the readout and `EffectiveAllowance - Distance` as the remainder; settle a dragged model with the `Point3` overload |

## Sequences

### A declared move

```mermaid
sequenceDiagram
    participant P as Player
    participant C as Client geometry
    participant S as Server geometry
    participant G as GameRules

    S->>G: legal actions for this unit
    G-->>S: allowance, impassable ids, difficult ids, cap
    S->>C: Ask with MoveRegion(...)
    C->>C: resolve ids to WorldVolume via TerrainPlacement.WorldVolumes()
    C->>C: Movement.Region(start, allowance, impassable, difficult, cap, size)
    C-->>P: draw Boundary as a polygon
    P->>C: drag a model
    C->>C: Settling.SurfaceHeightAt(Point3(x, y, currentZ), volumes)
    C->>C: Movement.Check(...) for the ruler and the remainder
    C->>S: Destinations answer, To carries the client's settled Z
    S->>S: Movement.Check + Settling + Table.ContainsBase, authoritatively
    S->>S: Movement.VolumesCrossed for dangerous and difficult
    S-->>C: ModelMoved events with the server's Z
    C->>C: apply events; the model snaps if it disagreed
```

Two hazards. The client's `To.Z` is *input* to the server's settling, not a claim. A client
that sends a Z under a second storey gets a model settled beneath the slab and drawn on the
ground floor. That is the intended behaviour and also the exploit surface. The second is
staleness: the region was drawn from volume ids resolved at Ask time, and this is safe only
because terrain is locked from `playing` on.

### A shooting Ask

```mermaid
sequenceDiagram
    participant Seq as Action sequence
    participant GR as GameRules
    participant Geo as Tabletop.Geometry
    Seq->>GR: eligible targets for this weapon
    GR->>GR: other units to blocking volumes via Bodies.AsBlockingVolume
    Note over GR: the shooter's own unit is left out (p.8)
    GR->>Geo: LineOfSight.Check(shooter, target, volumes)
    Geo-->>GR: LineOfSightResult(Clear, ObstructedBy)
    GR->>Geo: Cover.SightBlockersBetween(shooter, target, volumes)
    Geo-->>GR: blocker ids, in input order
    GR->>Geo: Cover.InsideCoverVolumes(target, volumes)
    Geo-->>GR: cover volume ids
    GR->>Geo: LineOfSight.VisibleModels(shooters, target unit, volumes)
    Geo-->>GR: one boolean per model of the unit
    GR->>GR: the two 50% thresholds, then +1 to Defense
    GR-->>Seq: target legal, with its cover modifier
```

The first two calls redraw the same twenty-seven rays, and `VisibleModels` redraws them once
per model of the unit per shooter. Two hazards. The cost is quadratic in models when every model
of every other unit is converted to a blocking volume, and the three calls multiply it. And
`SightBlockersBetween` is permissive, so scatter terrain near the line grants cover.

### Settling during a drag

```mermaid
sequenceDiagram
    participant P as Player
    participant C as Client
    participant Geo as Tabletop.Geometry
    P->>C: drag frame at (x, y), model currently at z
    C->>Geo: SurfaceHeightAt(Point3(x, y, z), volumes)
    Geo->>Geo: Solid volumes only; top at or below z; footprint contains x, y
    Geo-->>C: highest such top, else 0
    C-->>P: model drawn at that height
    Note over C,Geo: dragging along the ground under a 4-8 slab keeps z = 0
    Note over C,Geo: dragging from above the slab keeps z = 8
```

The drop height is the model's *current* Z, which has two consequences. A model dragged off a
roof falls only once its path leaves the roof footprint, not while still above it. And a model
dragged onto the side of a hill from below will not climb it, because the hill's top is above
the drop height. Whether a model *can* get up there is a movement rule the pack owns (geometry
values: `climbable` is dropped).

### Setting a zone style and checking a deployment against it

```mermaid
sequenceDiagram
    participant P as Any participant
    participant C as Client
    participant S as Game server DraftEditor
    participant DB as Postgres
    participant Geo as Tabletop.Geometry
    P->>C: pick a zone style
    C->>Geo: ZoneStyles.ShapesOf(style)
    Geo-->>C: ["side-battle/1", "side-battle/2"]
    C->>S: SetZoneStyle(style, shapes) setup edit
    S->>DB: one deployment_zone row per shape, team_ordinal 1..n
    S-->>C: broadcast the edit; never logged
    C->>Geo: ZoneStyles.Resolve(zone.Shape, table.Size)
    Geo-->>C: polygons to draw
    P->>C: SwapZones(1, 2) so the winner takes its edge
    Note over S,Geo: at deployment, the server checks ZoneStyles.BaseFullyWithin(shape, size, model)
```

Two hazards. A style string that is not in `All` throws, and setup edits are not commands, so
the failure surfaces as a rejected edit rather than a refused command. And rows are written for
ordinals that may have no team yet (the whole-design review's ruling 5). A team created after
`playing` has no zone at all.

### Coherency and overlap after a move

After applying the move the server calls `Coherency.Check(unitModels, 1, 9)`, `Overlap.Bases`
pairwise and `Overlap.WithSolid` per Solid volume. It warns under `warn` and refuses only under
`strict`. The client highlights the flagged models. Two hazards. `OutOfCoherency` holds
**positional indices**, so the caller must pass models in a stable order and zip the result
back itself. And the spread test flags both ends of an over-long chain rather than naming the
model to move.

## State and data

**The library holds no state.** Every class is `static` and every method is a pure function of
its arguments. There is no cache, no spatial index and no mutable field. `Tolerance.Distance`,
`Bodies.OvalVertices`, `LineOfSight.RimSamples`, `Movement.Rays` and `Movement.Step` are the
only public constants; `ZoneStyles`' two Side Battle fractions are private, and `ZoneStyles.All`
is the only static collection. The event log rebuilds the
*placements* that this library then measures.

The values it measures come from `schema.md`.

```mermaid
erDiagram
    TABLE_STATE ||--o{ TERRAIN_PLACEMENT : holds
    TABLE_STATE ||--o{ DEPLOYMENT_ZONE : holds
    TERRAIN_PLACEMENT ||--o{ TERRAIN_VOLUME : "footprint and declarations"
    UNIT ||--o{ MODEL : slots
    TABLE_STATE {
        real width
        real length
    }
    TERRAIN_PLACEMENT {
        real pos_x
        real pos_y
        real pos_z
        real yaw
        real scale
    }
    TERRAIN_VOLUME {
        text kind
        text footprint
        real dim_a
        real dim_b
        real offset_x
        real offset_y
        real yaw
        real bottom
        real top
        bool impassable
        bool blocks_los
        bool see_in_out
        bool cover
        bool difficult
        bool dangerous
    }
    DEPLOYMENT_ZONE {
        text style
        int team_ordinal
    }
    MODEL {
        text base_shape
        real base_a
        real base_b
        real height
    }
```

Dimensions are stored flat as `(shape, dim_a, dim_b)` and `(base_shape, base_a, base_b)`, so
`Base` and `Footprint` are flat `(Kind, A, B)` triples with symmetric shapes repeating the
value in both. `(A, B)` is always the extent. `deployment_zone.style` holds a **shape id**
(`'frontline/1'`), not a style name, despite the column's name.

Two columns absent from these tables because of this module:

- **No `resting_on` column.** Settling derives it (geometry values → Height, position, facing).
- **No containment table.** *Containment is computed, never stored* (table domain model). The
  stored containment, `Location.Inside`, is a different concept with the same word, annotated
  as such by the whole-design review.

There is no state machine in this module. The only lifecycle it participates in is the game's
own. Terrain is editable while `status` is `lobby` or `setup` and locked from `playing` on. A
move region computed at Ask time therefore stays valid until the answer arrives.

## Failure modes

| Failure | What happens | Chosen? |
| --- | --- | --- |
| Client and server disagree on a measurement near a threshold | The server's answer stands. The client resyncs and a model snaps | Yes, `architecture.md` → Geometry |
| A non-convex `PolygonOutline` is constructed | `Contains` and SAT return wrong answers. Convexity is an unchecked precondition | Accepted: every producer inside the library builds a convex shape, and the type is `internal` |
| A degenerate zone triangle | Unreachable from the built-in styles since Side Battle became proportional: a fraction of a positive edge is positive, and no other style has a diagonal. A degenerate polygon from anywhere else still normalises a zero-length edge in `PolygonOutline.Axes` and yields `NaN` in SAT | **No.** A latent bug with no caller, listed under Open questions |
| A zone style's absolute inset on a table dimension smaller than the inset | Per style: `frontline`'s two 12" bands overlap below a 24" short edge, and each covers the whole board at 12" or under; `ground-war`'s two 24" bands overlap below a 48" long edge, and each covers the whole board at 24" or under; `spearhead`'s two apexes cross below a 48" long edge, and sit on or past the opposite short edge at 24" or under; `opposing-forces`' 24"×24" squares overrun the short edge below 24", and the two overlap when both edges are under 48"; `free-for-all`'s four corners overlap along the long edge below 48" and along the short edge below 24". `side-battle` and `disordered` are fractions of the edges and of the edge midpoints, so neither degenerates at any positive size | **Accepted limit**, not a bug: small tables are out of scope, ruled by the owner 2026-09-06. Every polygon stays well formed, so nothing throws, nothing divides by zero, and the only symptom is a zone that overlaps its opponent's or the whole board |
| A base outline with fewer than three vertices | `PolygonOutline`'s constructor throws `ArgumentException` | Yes, loudly |
| An unknown `BaseShape` or `FootprintKind` | `ArgumentOutOfRangeException` from the switch's default arm | Yes |
| An unknown or malformed zone shape id | `ArgumentException` from `ShapesOf` or `Resolve` | Yes. A typo in a stored `deployment_zone.style` must not draw nothing |
| A move region computed against stale terrain | Cannot happen while `playing`, because terrain is locked. During setup there is no move Ask | Structurally prevented |
| Base overlap, terrain intersection, broken coherency, a model off the slab | Reported as a flag or an index list. The server warns under `warn` and refuses only under `strict` | Yes, flagged never prevented |
| A move whose *path* was illegal but whose endpoint is fine | `Movement.Check` samples the straight segment only. A bent path is judged at its endpoint | Yes, question 4's straight-line reach |
| A blocker between two adjacent sight rays | Not detected. The target is called visible, which it is: every sample is a real point of the body, so `Clear` is never wrong. The error is one-sided, and `SightBlockersBetween` is what loses a thin blocker | Yes, sampling, question 2 as ruled 2026-09-06 |
| A target visible only through a gap narrower than the sampling | Called hidden, and `VisibleModels` under-counts, which biases toward granting cover | Yes. Three rings bound the vertical gap by half the model's height; the horizontal gap is under 0.24" at mid-range |
| A model dragged into a wall by the client | The client's region already excluded it, but nothing stops a hand-placed model overlapping a Solid. `Overlap.WithSolid` flags it | Yes |
| Two structurally identical `Polygon` values compared with `==` | Unequal. `Polygon` is a record over `IReadOnlyList<Point2>`, so equality is reference equality on the list | **Trap.** Nothing in this library compares polygons, but a consumer might |

There is no shared mutable state, no I/O and no async, so every call is thread-safe and
reentrant. The two remaining hazards are **ordering** and **cost**:

- **Ordering.** `Containment.VolumesContaining`, `VolumesFullyContaining`, `LineOfSight.Check`'s
  `ObstructedBy`, `Cover.SightBlockersBetween` and `Movement.VolumesCrossed` all promise
  **input order** in their results, and `Coherency` promises **input indices**. A caller that
  reorders the volume list between two calls gets differently ordered ids for the same
  situation. That matters if any of it is compared or hashed downstream.
- **Cost.** There is **no broad phase anywhere.** `Outline.InteriorsIntersect` and `Separation`
  do full work on every pair, with no bounding-circle rejection. Three places this bites:
  `Movement.Region` (180 rays × up to 24 samples × every volume), `Coherency.Check` (O(n²) full
  separations, 64-gons for ovals) and `Cover` plus `LineOfSight` called back to back on the
  same pair, now at 27 rays each and multiplied again by `VisibleModels` over a whole unit. No
  test would catch a performance regression here, because every test uses one or two volumes.

## Testing

Thirteen test classes in `tests/Tabletop.Geometry.Tests`, one per subject, roughly 112 facts,
all headless and all `dotnet test` with no infrastructure. **Nothing in this module needs
Docker, a database, a socket or Godot.** `architecture.md`'s build-order note gives geometry
*"headless tests, zero dependencies"*.

The method is test-driven per the plan's Global Constraints: failing test, run, implement, run,
commit, one commit per task. Tests reach `Outline`, `BaseOutline`, `TargetPoints` and the rest
through `InternalsVisibleTo`, which makes the test project's assembly name load-bearing.
`Fixtures.cs` gives one helper type, `Fx`, with `Model`, `Box`, `Cylinder` and the two
declaration combinations (`Blocks`, `ForestSight`).

What the tests are for, by class:

| Class | The hazard it exists to catch |
| --- | --- |
| `PolygonTests` | boundary and vertex inclusion, either winding, a concave notch, the yaw direction |
| `OutlineTests` | exact circle separation, circle-to-rect, rect-to-rect vertex/edge pairs, touching-is-not-intersecting, SAT on a rotated diamond, clockwise reordering, segment tests, whole-outline containment |
| `BodyTests` | the two transform chains: yaw on a square base, an oval's length along the facing axis, scale × yaw × offset × Z on a volume, a segment clipped to a prism's vertical range |
| `DistanceTests` | edge-to-edge on every base pair, height not entering, the marker overload, witness points carrying their own Z, inclusive range |
| `SettlingTests` | Area passed through, highest Solid from above, **dropping from below a slab landing beneath it**, scale and Z raising the top, a rotated footprint |
| `ContainmentTests` | centre versus whole base, a model on a hill under a canopy in both, ground beneath a second storey in neither, `BaseFullyWithin` measured from the container's *edge* |
| `OverlapTests` | touching is not overlapping, different storeys, standing on a hill, a tall model reaching a slab, Area never intersecting |
| `LineOfSightTests` | a low wall shot over, blocking declared not derived, a forest blocking through but not in/out/within, the first of two forests, a wall off the line, tall versus short bodies, another unit as a blocker, partly hidden still clear, the 27/15 sample counts with the centre among them, `VisibleModels` reporting one boolean per model, a second observer seeing what the first cannot |
| `CoverTests` | fully inside, a low wall as a sight blocker while sight is clear, a transparent wall is not one, another unit is one |
| `CoherencyTests` | a straggler, equal groups keeping the earliest, a long chain breaking the spread, exactly one inch counting |
| `TableTests` | the corner frame's bounds, edge inclusivity, a base wholly on the slab, **a square base rotated 45° off it** |
| `MovementTests` | a full circle on open ground, an impassable volume stopping short, the difficult cap, no cap changing nothing, the table edge, a non-multiple allowance, displacement versus allowance, `VolumesCrossed` naming what containment cannot |
| `ZoneStyleTests` | every style resolving, each style's diagram dimensions, Side Battle's legs scaling to a 60×44 table, the long-edge rule on a portrait table, `fully within` needing the whole base, malformed ids throwing |

Coverage gaps:

- **No performance test.** Every test uses one or two volumes. The `Movement.Region` cost in
  section 12 is unmeasured and unguarded.
- **No cross-platform comparison test.** Determinism is not a requirement, so this is by
  choice. A convention mismatch between the Windows client and the Linux server would not be
  caught here either. The frame is defended by prose and by `TableTests`, not by a differential
  test.
- **No property-based or fuzz testing.** Every fact is a hand-chosen case. The invariants that
  would benefit most are `Separation(a, b) == Separation(b, a)`,
  `Separation > 0 implies not InteriorsIntersect`, and `ContainsOutline implies Contains` of
  every boundary point.
- **The 0.25" region quantisation is asserted as a range**, not a value
  (`Assert.InRange(r.Boundary[0].X, 2.5, 2.87)`). A refinement pass could be added without the
  test changing, and a regression in the marching would have to be large to fail it.
- **`Movement.Region`'s difficult-cap interaction with an obstacle beyond the cap** is untested:
  the only fixture puts the forest at 3", where the cap binds immediately.
- **`AsBlockingVolume`'s bounding-rectangle inflation for oval bases** has no test asserting the
  size of the error.
- **The zone styles are tested at three sizes only**: 72×48, the portrait 48×72 for the
  long-edge rule, and 60×44 for Side Battle's fractions. The six anchored styles are untested
  below their own insets, which is no longer a gap: small tables are out of scope by the
  owner's ruling of 2026-09-06, and a test there would freeze the degenerate output named
  under Failure modes as if it were wanted behaviour.
- **`VisibleModels` has no test at unit scale.** Both facts use one or two models and one wall,
  so the cost that the 50% rules impose on a shooting Ask is unmeasured.

## Open questions and deferred work

The plan's eight questions, plus 6a, the follow-up the review raised once question 6 split the
styles into one proportional and six anchored. Six are closed by ruling: question 1 on
2026-09-05, ratified by the owner 2026-09-06, and questions 2, 3, 5, 6 and 6a by the owner on
2026-09-06. The other three carry the assumption taken and what changes if the answer differs.

| # | Question | Assumption taken | If the answer differs |
| --- | --- | --- | --- |
| 1 | Where is the origin? | **Closed: the corner**, ruled 2026-09-05 by the compatibility review and ratified by the owner 2026-09-06 | Nothing. The frame is settled on the wire, in the library, in both servers and in the client |
| 2 | What is the sight-line policy? | **Closed: *"Center to any part of target"***, ruled by the owner 2026-09-06. Eye at the centre of the shooter's body, half height; 27 target points, 15 for a square; clear if any line is clear | The eye moved from the top centre and the target gained a mid-height ring, 17 points to 27. A denser sample still changes only `TargetPoints`; a shooter-rim sample now contradicts the ruling |
| 3 | What does *mostly inside* mean for a single model? | **Closed: the base centre inside the volume**, confirmed by the owner 2026-09-06, who added *"50% models in unit in cover for it to count"* | Nothing here. The threshold is `GameRules`', over per-model facts; the companion rule about seeing a unit added `LineOfSight.VisibleModels` |
| 4 | Is straight-line reach enough? | Yes. The region is a cosmetic prediction and the server judges the landed move | Geodesic reach is a visibility graph over footprint polygons: a separate, larger task. A plain disc drops Task 12's obstacle handling entirely |
| 5 | Free-for-all's *2'x1'*, which way round? | **Closed: 24" along the long edge, 12" along the short**, confirmed by the owner 2026-09-06 | Nothing |
| 6 | Side Battle's anchoring | **Closed: proportional to the table**, ruled by the owner 2026-09-06. Legs are `0.75` of the long edge and `0.625` of the short, the p.21 diagram's 54" and 30" on 72×48. The diagram's own three labels stay inconsistent: those legs put the band at 24.5", not 24", so legs are the shape and the band is illustration | `Leg(edge) = edge - 18` is dropped, and the degeneracy it carried with it. The ruling contradicts geometry values → Deployment shapes, so it carries spec correction 10; the other six styles stay anchored |
| 6a | Should every style scale with the table, so the split is not inconsistent? | **Closed: no**, ruled by the owner 2026-09-06: *"We don't need to support skirmish at all."* One proportional style and six anchored ones is the shipped set. The two conventions agree exactly at 72×48, because Side Battle's fractions were chosen to reproduce that diagram, and diverge only as the table leaves the standard size | Nothing about the split. What the ruling changes is scope: the small-table behaviour the split would have hurt is an accepted limit under Failure modes, and spec correction 11 records that the built-in styles assume a table near 72×48 |
| 7 | Ovals as 64-gons? | Accepted: error under 0.2% of the radius | Exact ellipse-ellipse distance has no closed form and needs an iterative solver on both client and server |
| 8 | Is a terrain container needed as well as a model container? | No. Only `Transport(X)` is automated. A building holds models through the client's menu, which places by hand | One overload, `BaseFullyWithin(PlacedModel, WorldVolume, double)` over `WorldOutline()`, five lines and one test. `MoveContainment.ExitingId` then names a `TerrainVolume` id, which the bare `Guid` already permits |

Two further items the plan does not ask about:

- **`PolygonOutline` still accepts a degenerate polygon.** Its `Axes` divides an edge vector by
  its own length, so a zero-area or collinear triangle divides by zero. The Side Battle
  degeneracy that used to reach it is gone, since a fraction of a positive edge is positive, and
  no built-in style can now produce one. The guard belongs in `PolygonOutline`, which rejects
  fewer than three vertices but not three collinear ones.
- **No broad phase.** A bounding-circle rejection at the top of `InteriorsIntersect` and
  `Separation` would be a handful of lines and would change the cost of `Movement.Region` and
  `Coherency.Check` by an order of magnitude. Line of sight would gain the most: 27 rays a pair
  since 2026-09-06, multiplied again by `VisibleModels` over a unit. It is not in the plan.

Deferred, with where it is recorded:

| Deferred | Recorded in |
| --- | --- |
| Convex hull volumes | geometry values → Collision volumes (dropped, not deferred) |
| Rectangular bases | geometry values → Deliberately not decided |
| Whether a footprint ever needs to be a polygon | geometry values → Still open |
| Whether the closed six-declaration set survives a different ruleset. Warhammer is the real test | geometry values → Still open |
| The p.22–23 deployment styles; custom and adjustable zone shapes | geometry plan Task 13; setup content → Deliberately not decided |
| How zone styles are organised per game | geometry values → Deployment shapes; spec correction 2 |
| `climbable` as a declaration | geometry values → Declarations (dropped; a movement rule the pack owns) |
| The terrain generators and the built-in terrain inventory | setup phase → Terrain; the client shell plan's `TerrainGenerators` |
| A combined `Check` + `SightBlockersBetween` call | section 9 |
| A bisection pass on the movement region's rays | section 12 |

The plan proposes eleven **spec corrections**. Four are already applied to another plan (1, 5,
8 and 9 to the contracts plan, 1 also to the client shell obligations). Seven are still owed:
corrections 2, 4, 7 and 10 to `2026-08-29-geometry-values-design.md`, correction 3 to
`2026-08-29-setup-content-design.md`, correction 6 to `2026-09-04-client-shell-design.md`, and
correction 11 to `architecture.md` → Table and layout with a companion line in
`2026-08-29-setup-content-design.md`. Corrections 10 and 11 are the two that override a spec
rather than filling a gap in it: the owner's proportional Side Battle contradicts *"anchored,
not absolute and not normalized"*, and the scope ruling of 2026-09-06 narrows *"Arbitrary
sizes … Never a constant"* for the built-in zone styles. None is applied here, since this
design may only write under `docs/design/geometry/`.

## References

**The plan.** `docs/superpowers/plans/2026-09-05-geometry.md`: thirteen tasks, the
`Interfaces → Produces` table, eleven spec corrections, nine questions of which six are closed.

**Consumed plan.** `docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md`: Task 2
(the value types), Task 3 (the frozen API surface), Task 5 (`DeploymentZone`,
`TerrainPlacement.WorldVolumes()`), Task 8 (`MoveRegion`, `MoveContainment`, `Destinations`),
Task 11 (`SetZoneStyle`, `SwapZones`).

**Specs**, in `docs/superpowers/specs/`:

- `2026-08-29-geometry-values-design.md`, the primary spec: bases; height, position, facing;
  the terrain transform; collision volumes; declarations; deployment shapes; marker size.
- `2026-08-27-table-domain-model-design.md`: a miniature is a base and a height; distance is
  edge to edge; containment is computed, never stored; terrain declares, the system interprets.
- `2026-08-29-setup-content-design.md`: the table is a slab; deployment zones are a built-in
  shape id plus a team.
- `2026-09-04-setup-phase-design.md`: zones assigned to team ordinals and swapped; off-slab
  terrain dropped at end-setup.
- `2026-09-04-client-shell-design.md`: geometry runs in `Tabletop.Geometry`; the ruler, the
  reachable region, measure, drag, no Godot physics.
- `2026-09-03-action-sequences-design.md`: where the geometry calls sit inside a sequence
  (attack-sequence's *in range and line of sight*, charge, casting's 18" and line of sight).
- `2026-08-30-presence-design.md`: *A measurement is two points*, which `ClosestPoints` serves.
- `2026-08-28-game-flow-design.md`: *Inside a container*, enter or exit with any move action,
  never both in one activation, exit within 6".
- `2026-09-04-whole-design-review.md`, newer than every other spec: enforcement's three
  positions, the game server writing setup rows, a participant with no team.

**Architecture.** `docs/architecture.md` → The load-bearing seam; What is actually shared; How
the client stays responsive without the rules; Rules enforcement; Geometry; Table and layout;
Client and server responsibilities; Contracts to freeze; Build order.

**Data.** `docs/data-model.md`; `docs/schema.md` → `table_state`, `terrain_placement`,
`terrain_volume`, `deployment_zone`, `model`, `asset_volume`.

**Rulebook.** Grimdark Future v3.4.1: p.6 (deployment *fully within 12"*, objectives, within 3"
of a marker), p.7 (coherency 1"/9"; no part of a base off the battlefield; *no part of their
bases move further than the total movement distance*), p.8 (in range, line of sight, ignoring
friendly models of one's own unit), p.9–10 (charge; within 2" horizontally and 4" vertically),
p.11 (the terrain types: impassable, cover, difficult, dangerous, sight blockers; *fully inside*
versus *mostly inside*; *moves in or through*), p.12 (forests: see into and out of, not
through), p.13–14 (`Transport(X)`), p.20 (solid buildings), p.21 (the six basic deployment
styles, dimensions from the page's diagrams), p.49 (free-for-all's corner areas). The Age of
Fantasy advanced rulebook's terrain section is word-for-word identical and is not a second
example.

**Sibling design documents**, in `docs/design/`: contracts, game server, scheduler,
interpreter and effects, client shell, management API, web app.
