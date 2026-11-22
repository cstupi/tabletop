# Move path footprints and the dangerous terrain warning

Owner ruling, 2026-11-21, after a Flayer Cavalry model's oval base clipped a toxic smog's edge on a straight 16" Rush while its centre
line missed: *"Any part of the base counts."* 3.6.0 p.12: "If any model in a unit moves into or through Dangerous terrain… the entire
unit must take a Dangerous Terrain Test"; p.5: "always measure so that no part of its base moves further than the total distance."
The server already judges the swept base (`VolumesOnPath` over `Movement.Measured`); the client must show the same before a commit.
Owner: *"faintly draw the travel line with footprints so users can see the path they took and also warn before it would cause the
dangerous terrain."*

## Behaviour (client and browser)

- **Path:** for each held or proposed model in a move Ask, a faint line along the server's route (`MoveRegions.Route`, the path
  `Movement.Measured` gives) from its origin to its ghost, with the base's outline drawn as faint footprints along it (one every base
  length, at the model's facing along the path, plus the start and end), so the swept width is visible. Drawn while the Ask is open,
  under the ghosts, in the team colour at low opacity; a footprint that overlaps a dangerous volume is drawn in the dangerous colour.
- **After the move:** the same faint path and footprints stay for the unit's last move until the next activation starts, so a player
  sees the path taken (both players see it; replay shows it for the step).
- **Warning:** a model whose swept base enters or crosses a volume the Ask lists as dangerous (`DangerousVolumeIds`, `UnstableVolumeIds`
  with `UnstableOver`) is marked like a contact mark, label `Dangerous terrain test`, and the decision box's gate line names it
  ("Flayer Cavalry 1 crosses Toxic Smog: Dangerous terrain test"). It warns, never blocks (the move is legal). The test is the same
  geometry the server uses (`VolumesOnPath` over the swept base), so the warning and the server's test agree.
- **Bands:** the dangerous band draws above the clear and difficult bands of every held model, so a sliver stays visible.

## Where it lives

Core (Godot-free, tested): `MovePath` (route, footprints, dangerous hits per model) shared by the warning and the drawing; the viewer port
with an agreement fixture. Nodes: a path layer beside `RulerNode`; the browser's 2D table the same.
