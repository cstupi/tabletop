# Moving through units

Rulebook p.7 (3.5.1 and 3.6.0, same text): "Models may never be within 1” of models from other units, unless they are using a Charge
action, and may never move through other models or units (friendly or enemy), even if they are using a Charge action."

Owner ruling (2026-11-27): both clauses hold along the whole path, not only at the landing. A path that slips between two models of
another unit is moving through that unit.

## Behaviour

- **A unit is an obstacle.** Every unit other than the mover's own blocks a moving model's path as its perimeter: the convex hull of its
  models' bases (the hull `Bodies.Perimeter` builds for sight), from its lowest base to its tallest top. Friendly and enemy alike.
- **The 1" holds on the way.** Outside a Charge (the region's `Separation` is set), the perimeter is grown by that distance: no point of the
  path comes within 1" of another unit's model. A Charge's regions carry no `Separation`, so a charge path is held only to the perimeter.
- **The charge target** blocks as its models' bases only (no perimeter), so a charge reaches any model of it a path round its bases reaches.
- **The mover's own unit** blocks as its models' bases, as now.
- **Starting inside.** A model that starts inside a unit's grown perimeter is held to its plain perimeter; one that starts inside the plain
  perimeter, to that unit's bases; one whose base overlaps a base, as now (that base left out). So a model is never trapped by where it stands.
- **Verdicts.** A move that fits only through a unit's perimeter (or through models) is `move_through_models`: "{model} would move through
  {unit}: models may never move through other models or units". A move that fits round the perimeters but only within 1" of a unit's model
  is `move_separation`: "{model} would pass within 1" of {unit}: models may never be within 1" of models from other units". A landing within 1" is still `move_separation`, judged before the path, so a
  landing too close is never reported as a path through. Strict refuses, warn warns, silent passes.
- **Allowance.** A path that goes round a unit is measured round it (p.5): the detour counts against the allowance, as a detour round terrain does.
- **Everywhere a path is measured.** The same obstacles apply to every move judged or measured: the move Ask's breaches and `ApplyMove`, the
  difficult cap, charge targets and contact reach, containers in reach, regroup and charge formations, forced moves, and both clients' reach
  area, clamp, ruler, contact marks and footprint path. The clients take the unit grouping and the Ask's `Separation` and `ContactWith` as the
  server does, so the area drawn is the area the server accepts.
- **Hand moves** (outside a move step) are unchanged: `hand_move` under the enforcement setting, strict refusing.
