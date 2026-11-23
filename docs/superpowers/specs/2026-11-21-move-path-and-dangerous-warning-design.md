# Move path footprints and the dangerous terrain warning

Owner ruling, 2026-11-21, after a Flayer Cavalry model's oval base clipped a toxic smog's edge on a straight 16" Rush while its centre
line missed: *"Any part of the base counts."* 3.6.0 p.12: "If any model in a unit moves into or through Dangerous terrain… the entire
unit must take a Dangerous Terrain Test"; p.5: "always measure so that no part of its base moves further than the total distance."
The server already judges the swept base (`VolumesOnPath` over `Movement.Measured`); the client must show the same before a commit.
Owner: *"faintly draw the travel line with footprints so users can see the path they took and also warn before it would cause the
dangerous terrain."*

## Behaviour (client and browser)

- **Path:** for each held or proposed model in a move Ask, small marks along the server's route (`MoveRegions.Route`, the path
  `Movement.Measured` gives) from its origin to its ghost. Owner, 2026-11-22: *"it should be little footprints instead of traces of the
  base and lines. That way it shows movement. Vehicles should show treads for their path."* and *"it should be based on the number of
  models. A single model shouldn't have a sea of footprints like this but instead a line of a pair of footprints."* Each model leaves one
  trail: a single pair of feet's prints alternating left and right a little either side of the route, facing along it, stride and print
  size scaled modestly with the base (an infantryman's 0.4" apart, a monster's larger and further apart). Owner: *"if they had to pivot or
  go around something that has to be reflected in their pathing."* The trail follows the measured route round whatever it bends round
  (impassable terrain, models, dangerous ground), the feet turning in place at each bend, and where the model's facing at the start or
  end differs from the route's the prints turn in place there (a vehicle's tracks swing round its centre); a
  vehicle (the army list published no base for it: Army Forge's `bases` "none" or empty, `ModelSlot.Vehicle`) leaves two tread tracks
  of repeating links at the base's width. Drawn while the Ask is open, under the ghosts, in the team colour in the theme's look (Fantasy
  an ink stamp, Sci-fi a faint light trace); a mark on the stretch of the walk where any part of the base touches a dangerous volume
  (`SweptBase.Stretches`, the same sweep the warning uses) is drawn in the dangerous colour.
- **After the move:** the same marks, fainter, stay for the unit's last move until the next activation starts, so a player
  sees the path taken (both players see it; replay shows it for the step).
- **Warning:** a model whose swept base enters or crosses a volume the Ask lists as dangerous (`DangerousVolumeIds`, `UnstableVolumeIds`
  with `UnstableOver`) is marked like a contact mark, label `Dangerous terrain test`, and the decision box's gate line names it
  ("Flayer Cavalry 1 crosses Toxic Smog: Dangerous terrain test"). It warns, never blocks (the move is legal). The test is the same
  geometry the server uses (`VolumesOnPath` over the swept base), so the warning and the server's test agree.
- **Bands:** the dangerous band draws above the clear and difficult bands of every held model, so a sliver stays visible.

## Where it lives

Core (Godot-free, tested): `MovePaths` (route, footprints, dangerous hits and stretches per model) shared by the warning and the drawing,
`PathMarks` (the prints and treads); the viewer port with an agreement fixture. Nodes: `MovePathNode` (a MultiMesh per mark shape) beside
`RulerNode`; the browser's 2D table fills the same shapes.
