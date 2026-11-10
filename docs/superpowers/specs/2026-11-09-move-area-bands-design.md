# Move area bands

Owner, 2026-11-09 ("do it"): the move area is drawn in up to three bands by what reaching a point costs, in the Godot client and the browser viewer.

## The rules

- p.11 / 3.6.0 p.12 Difficult: *"If any model in a unit moves in or through difficult terrain at any point of its move, then all models in the unit
  may not move more than 6" for that movement."*
- 3.5.1 p.11 Dangerous: *"If a model moves in or through dangerous terrain, or is activated in it, then it must take a dangerous terrain test."*
  3.5.1 p.12: *"Rivers - Dangerous when using rush/charge"*, *"Ruins - Cover + Dangerous when using rush/charge"* (the `DangerousOnRushCharge`
  declaration).
- 3.6.0 p.12 Dangerous: *"If any model in a unit moves into or through Dangerous terrain, or activates in it, then the entire unit must take a
  Dangerous Terrain Test."* Unstable (the same declaration): *"If any model in a unit moves over 6” when moving into or through unstable terrain,
  then it counts as Dangerous terrain."* The 6" is the whole movement, earlier segments of a split move included (`GfMoves.ApplyMove`'s `usedBefore`).
- p.13 Flying and Aircraft ignore terrain effects, the Banner of Eternal Flame (p.36) too: no dangerous terrain. p.14 Strider ignores difficult
  terrain only: no cap, dangerous terrain still tests.

## The bands

- **Clear**: reached within the allowance by a path round every difficult volume (when the cap binds) and every volume dangerous to it.
- **Difficult**: else reached through difficult ground within the cap, round every volume dangerous to it.
- **Dangerous**: else reached at all. A point both difficult and dangerous is Dangerous.
- An unstable volume is dangerous only to a path longer than the Ask's `UnstableOver`, so a path no longer than that may enter it: under 3.6 the
  Dangerous band from unstable ground starts beyond 6" of total move.
- No cap (Strider, Flying, Aircraft, a cap at or over the allowance): no Difficult band. A volume the Ask does not list draws no band.
- The bands never change what is reached, the clamp or the cap once a path enters difficult ground.

## The route

The ruler shows the path the server measures (`GfMoves.ApplyMove`, `Movement.Measured`): `Movement.Check`'s order over the dangerous volumes
(round difficult and dangerous within the allowance, round difficult within it, round dangerous within the cap, any within the cap), chosen again
round the unstable ones too once the path is longer than `UnstableOver` and enters dangerous or unstable ground. A unit already held to the cap is
judged by its shortest path, and the ruler shows that. Godot: `MoveReach.Route`, its fields settled on the pool when the Ask opens
(`MoveRegions.Prepare` → `PrepareRoute`). Viewer: `Geometry.moveRoutes` (facade `MoveRoutes` over `Movement.Measured`) per drag frame for the held
models, asked again `capped` once any path goes in difficult ground; the readouts, the gate, the allowance bar and the ruler (a polyline) read it.

## The wire

`MoveRegion` (libs/Tabletop.Protocol/Asks.cs) gains three optional fields, null when absent:

| Field | 3.5.1 Advance | 3.5.1 Rush, Charge | 3.6.0 any move |
| --- | --- | --- | --- |
| `DangerousVolumeIds` | Dangerous | Dangerous and DangerousOnRushCharge | Dangerous |
| `UnstableVolumeIds` | null | null | DangerousOnRushCharge |
| `UnstableOver` | null | null | 6" less what earlier segments of the move used, at least 0 |

All three are null for a unit that ignores dangerous terrain. The server fills them where it opens a unit's own move (`GfMoves.RegionDangers`:
the action's move step, a rule's reposition, which 3.5.1 reads as neither Rush nor Charge); a spell's forced move tests no dangerous terrain and
names none.

## Computation

`MoveReach.Build(…, dangerous, unstable, unstableOver)` puts the dangerous volumes in `MoveSpace.Dangerous` and the unstable ones in
`MoveSpace.Unstable` (dropped when `unstableOver` is at or past the allowance). `MoveReach.Bands(least?)` and `BandAt(point)` read three nested
levels, each a path length less a limit, least over its terms:

- reached: the reach's own (`_any` within the cap, `_avoiding` within the allowance);
- clear: round walls, bodies, difficult (when binding), dangerous and unstable within the allowance; or round all but unstable within
  `min(allowance, unstableOver)`;
- round danger: clear's terms, and round walls, bodies, dangerous and unstable within the cap; or round all but unstable and difficult within
  `min(cap, unstableOver)`.

A field whose groups add nothing in reach is the reach's own, so a table with no dangerous or unstable volume in reach settles no more fields.
`ReachMesh` walks one quadtree for the three bands (Clear `c`, Difficult `max(i, −c)`, Dangerous `max(r, −i)`), each point evaluated once; a cell is
drawn whole or dropped only when every band is clear of zero on it. The bands are disjoint and cover `Triangles`' area. `Triangles` is unchanged,
bit for bit. The cavalry Rush (`MoveBandsTests`, a dangerous piece and an unstable lair added) builds its bands in about 1.5 times its plain area.

## Colours

`MoveBandLook` (client Core; viewer `moveBandLook.ts`), the same in both themes:

- Clear: the moving team's colour at 0.25 alpha, as the area was.
- Difficult: the team's colour halfway to grey at 0.2 alpha.
- Dangerous: the terrain legend's dangerous colour (`TerrainEffects`, `#F0722E`) at 0.4 alpha, striped diagonally every 0.6" (half drawn), so it reads
  apart from every team colour, the activated band's green (over 60° of hue in both themes) and the threat rings.

## Drawing

- Godot: `MoveRegions.Bands` (per model, on the pool through `BandsAsync`), `MoveRegionNode.ShowBands` (one mesh per band; the dangerous band's
  material maps a stripe texture in world space). The shader warm-up draws the three.
- Viewer: `Geometry.moveBands` (facade `MoveBands`, the same composition as `MoveRegions`, a `MinDistance` read as `MoveRuler.Minimum`), asked once
  per listed model per Ask (`moveHooks.bands`), drawn by `bandsOverlay` (a path per band, the dangerous one clipped and striped); the allowance
  circles stay until the geometry is loaded.
- No legend: the decision box has no place for one.
