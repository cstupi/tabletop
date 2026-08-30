# Geometry values

Design session, 2026-08-29. What the geometric fields in
[`docs/data-model.md`](../../data-model.md) actually are.

Ninth module, and the first of three putting real definition on that diagram's placeholder
types. It covers base shape and height, position and facing, the terrain transform,
collision volumes and their declarations, deployment shapes, asset scale and orientation,
and marker size. Identity and enumerations, and content values, are separate sessions.

Still not C#, not persistence, not the wire format. This says what a value *is*.

Already settled elsewhere and not revisited: one world unit is one inch; distance is
edge-to-edge between bases; line of sight runs against the base-and-height cylinder and
never the visual mesh; models settle by raycasting down; markers ignore terrain.

## Bases

**A shape and its dimensions, in world units.**

```
base   circle(diameter) | oval(length, width) | square(side)
```

Presets exist and carry the familiar names — `32mm round` is a *name*, `1.26` is the value.
A user may set custom dimensions, but always in one of the three shapes.

`architecture.md` described the base as a preset dropdown. That would make the geometry
library look up a table to answer "how big is this," and the per-slot base exists precisely
so users can base models their own way. Presets are UI over a shape, not the stored value.

Millimetres never enter the model. `Tabletop.Geometry` does no unit conversion.

A rectangular base is a later addition if some game needs one.

## Height, position, facing

**Height** is one number in world units: how tall the line-of-sight cylinder stands above
the base. Per slot, defaulting from the ruleset, like the base.

**A model's facing is yaw only, and a model always stands upright.** Settling on a slope
sets its height, never its tilt. Line of sight runs against a vertical cylinder, and a
tilted one makes every test harder to compute and harder to reason about in exchange for
realism nobody measures against. A real miniature on a hill gets propped up anyway.

**"What it is resting on" is dropped.** The table domain model listed it as part of a
placement. It is derivable — settling raycasts down whenever the answer is needed — and
terrain is locked after setup, so nothing a model stands on ever moves. Storing it would be
a reference to keep in sync for no gain.

So a placement is **position and yaw**.

## The terrain transform

**Position, yaw, and a uniform scale.** Terrain is resizable — one rock mesh at three sizes
is ordinary use — and uniform keeps volumes scaling with it cleanly. Per-axis scale would
stretch a circular footprint into an ellipse, quietly reintroducing a shape the geometry
does not have.

## Collision volumes

**The mesh is visual. Gameplay runs on simple prisms.**

```
Volume   kind          solid | area
         footprint     circle(radius) | rect(width, length)
         offset        x, y from the piece's origin
         yaw
         bottom, top   heights above the piece's base
         declarations  see below
         notes         free text
```

**Convex hulls are dropped.** `architecture.md` listed boxes, cylinders and convex hulls.
With footprints restricted to circles and rectangles, every test is a 2D shape intersection
plus a vertical range — and model bases are circles, ovals and squares, so model-versus-terrain
is 2D shape intersection with a height check. No hull math anywhere, and moving a model
through terrain stays cheap.

**`bottom` and `top` rather than a single height** is what makes one mechanism cover every
case. A hill is `0–2`. A wall is `0–4` with a thin footprint. A second story is `4–8` with
nothing beneath it. A piece carries a **list** of volumes; the overwhelmingly common case is
exactly one, and the list exists so the rare ruin can have a ground floor and an upper deck
without a special mechanism.

**Solid versus area** is whether the space is occupied:

- **Solid** — a model cannot be inside it, and its top face is what a model settles onto.
  Hills, walls, buildings, the slab that is a second story floor.
- **Area** — a model passes through freely and stands on whatever is underneath. Forests,
  rubble fields, water.

A forest is one area volume declaring cover and difficult, so models clip through the trees
rather than fighting them during placement. A forest on a hill is a solid volume `0–2` with
an area volume `2–8` on top: models stand on the hill and are inside the forest, and neither
needed a special case.

This settles standing without a flag: **solid tops are standable, area volumes are stood
inside.**

## Declarations

Six flags, and they are OPR's own terrain types.

```
impassable      a model may not cross it
blocks LoS      the volume is opaque
see in and out  modifies blocking: see into and out of it, but not through
cover           
difficult       
dangerous       
```

Read from the Grimdark Future and Age of Fantasy advanced rulebooks. Their terrain sections
are **word for word identical**, so Age of Fantasy is not a second example for terrain
despite being a second game.

**No effects are stored.** OPR's cover is +1 to Defense rolls, difficult caps a unit's move
at 6", dangerous is a die per model with each 1 a wound — all of which live in the pack, not
on a rock. A flag says *this is difficult*; the ruleset says what that costs. This is the
table domain model's rule — terrain declares what it does, the game system interprets it —
tested against a real ruleset rather than a hypothetical one.

**`impassable` is orthogonal to solid versus area**, and all four combinations are real:

| | not impassable | impassable |
| --- | --- | --- |
| **solid** | Hill — climb it, stand on top | Building, mountain — cannot cross |
| **area** | Forest — walk through it | Deep water, lake — cannot enter |

Solid says the space is occupied; impassable says a model may not cross. Neither implies the
other. An earlier draft of this session had only five declarations and no `impassable`, which
made a hill and a building indistinguishable.

**Blocking is declared, not derived.** An earlier draft had solid volumes blocking sight
geometrically and only area volumes declaring it. That is wrong: a chain-link fence is solid
and transparent. The declaration says the volume is **opaque**; geometry then answers whether
a given line passes through it. A low wall declared blocking still will not stop a tall model
shooting over it, and that falls out for free.

**`see in and out` is a modifier on blocking**, not an independent property — it means
nothing on a volume that is not opaque. A forest and a ruin are `blocks LoS` + `see in and
out`; a wall is `blocks LoS` alone. OPR treats this as an extra condition players agree
rather than a listed type, but its own guidelines apply it to forests, dense vegetation and
smog, and forests and ruins are common enough that free text is the wrong home for it.

**Declarations are per volume, not per piece.** A ruin's ground floor and upper story
genuinely differ, and the volumes are already there.

Everything else OPR does with terrain is free text: *difficult when moving up*, *dangerous
when using Rush or Charge*, *units on top may ignore one unit for line of sight*. The
rulebook itself says players "must agree on what terrain type rules each piece follows,"
that a piece "may count as having multiple terrain types," and that they "may also add other
conditions to further customize." **A closed set plus an open field is the game's own
architecture**, which is a better validation of this design than any argument for it.

**`climbable` is dropped** from `architecture.md`'s tag list. A solid volume's top is
standable; whether a model can get up there is a movement rule the pack owns.

## Where volumes come from

**Every terrain asset gets a volume at upload, computed from the mesh's bounding box** — rect
footprint, `bottom 0`, `top` the mesh height, `kind: solid`. A crate, a rock or a wall is
finished at that point with no authoring at all.

The editor is only for when the default is wrong: a forest that should be area rather than
solid, a ruin needing an upper floor, a footprint that should not be the full bounding box.

**Volumes are therefore never absent**, and no consumer needs a fallback for an asset without
them.

**The editor lives in the web app**, with the rest of asset properties. `architecture.md` put
volume authoring in the client because volumes "need real 3D manipulation" — that reasoning
was about convex hulls, which are gone. A footprint and two heights is a top-down 2D task
over a model preview.

The bar it has to meet: **upload a terrain model and set where collision happens, easily,
with the supported shapes.**

**Third-party authoring is a later shortcut, not a dependency.** `KHR_physics_rigid_bodies`
and `KHR_implicit_shapes` are unratified, unsupported by the official Blender exporter, and
carried only by a third-party addon that warns names may change. Object names and glTF
`extras` are stable and could be read by convention later. None of it changes the model: a
volume is the same record whether it was computed from bounds, drawn in the editor, or read
from the file.

## Deployment shapes

**A named style, resolving to a set of polygons per zone.**

Three things the OPR rulebook settles about these:

- **A zone is a set of shapes, not one shape.** One of the six standard styles gives each
  team two diagonally opposite corners.
- **The shapes are polygons.** Triangles and diagonal bands are load-bearing. Unlike terrain
  volumes this costs nothing: a deployment zone is a flat point-in-polygon test with no
  collision, no settling and no height.
- **Measurements are anchored, not absolute and not normalized.** The rulebook writes them as
  absolute inches on an assumed table — 12", 24", 36", 54" — which does not survive a 30×22
  skirmish table. Normalized fractions would scale 12" on a 48" table into 11" on a 44" one,
  which is not what the rules say either. Anchoring to table features — *12 inches in from the
  long edge, full width*; *a 24×24 square in the corner* — reads exactly as the rulebook
  writes it and works at any size.

**A default set ships now.** More can be added later, and organising them by game is a later
problem. There is no need to decide today whether they are client content or pack content.

## Asset scale and orientation

The two corrections that make an arbitrary glTF usable:

- **Scale** — a uniform multiplier bringing the mesh to true size in world units.
- **Orientation** — a rotation making the mesh stand upright and face forward.

Both are applied at import, so the mesh sits properly on its base. What they are measured
against is the mesh's own axes versus the table's.

## Marker size

**A diameter in world units.** Objectives are large circles, tokens small chips, and both are
circles.

## Deliberately not decided

- How the footprint editor works, beyond the bar above.
- Reading volumes from glTF node names or `extras`.
- The contents of the default deployment set, and how styles are organised per game.
- Rectangular bases.
- Whether scale and orientation are written by the mesh prep tool or by the user.

## Still open

- **Whether the closed declaration set survives a genuinely different ruleset.** Age of
  Fantasy shares OPR's terrain text exactly, so the six flags have been tested against one
  game, not two. Warhammer is the real test.
- Whether a footprint ever needs to be a polygon rather than a circle or rectangle. Deployment
  zones needed polygons; terrain has not.
