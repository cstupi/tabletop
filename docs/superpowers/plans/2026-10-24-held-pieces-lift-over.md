# Held pieces lift over Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** A held model or terrain piece is lifted by default: it goes straight to the cursor over everything, drawn 0.5" above
what is under it, and lands at the nearest clear spot. PageDown and PageUp step it down and up the open surfaces under it; at a
level it slides along what its body meets there and can pass under a bridge. The server rests a model on its open surface nearest
the Z the client sent.

**Architecture:** `Tabletop.Geometry.Collision` gains open surfaces (`Surfaces`, `RestNear`), a level on `Slide`, `Separate`,
`Follow` and `Clear`, a lifted landing (`Land`) and the hover height (`Hover`). `client/src/Core/DragBodies` returns a `HeldEnd`
(where a piece is drawn and where it lands) per held piece from a `double? level` (null: lifted); `HoldLevel` steps the level.
`TableInput` keeps the level on its `Drag`, binds PageUp/PageDown, draws the hover and a landing mark, and drops at the landing.
`Battlefield.Settle`, `DraftEditor.Rest` and `ContactCheck` rest near the Z they are given.

**Tech Stack:** .NET 10, xUnit, Godot 4.7 C# client.

**Spec:** `docs/superpowers/specs/2026-10-24-held-pieces-lift-over-design.md`

## Global Constraints

- Lifted is `double.PositiveInfinity` as a level inside `Collision` (the open surface nearest it is the highest, today's rest);
  `DragBodies` and `TableInput` carry it as `double? level`, null for lifted.
- `Collision.RestHeight` is unchanged: the highest open surface always equals today's rest (the highest candidate is never
  overlapped by a Solid above it, since that Solid's top would be a higher candidate).
- At a level, a wall's top is never a surface mid-drag: the body's bottom is read from the volumes less the walls it can meet
  (the walls it did not start in), and a wall stops it when the wall's height interval overlaps the body's there
  (`Overlap.IntervalsOverlap`). A wall it starts in stays a surface, so a model on a bridge stays on it.
- A lifted landing's walls are plan only (a footprint entering a wall, as `Clear` reads it today); its models are
  three-dimensional (`Overlap.Bases` at the rested spot).
- No new Godot code holds logic: the Table glue only draws and routes keys.

## Review Focus

- Every existing Geometry, client and server test keeps its result except the `DragBodiesTests` that assert today's slide, which
  now pass `level: 0` (a level at the table) to keep testing the slide.
- `Battlefield.Settle` with Z = 0 on a hill or crate still gives its top.

## Task 1: Open surfaces

**Files:** create `libs/Tabletop.Geometry/Collision.Surfaces.cs`; test `tests/Tabletop.Geometry.Tests/SurfacesTests.cs`.

- [ ] **Test first** (`SurfacesTests`), with `Fx.Box` volumes:
  - `The_open_table_has_one_surface`: a model on an empty table → `[0]`.
  - `A_hill_hides_the_table`: a base half over a 2" hill → `[2]`; `RestNear(…, 0)` → 2.
  - `A_bridge_with_room_has_two_surfaces`: a Solid deck x 8..16, Z 3..4, a 1.26"/1.5" model under it → `[0, 4]`;
    `RestNear(…, 0)` → 0, `RestNear(…, 3.9)` → 4, `RestNear(…, ∞)` → 4.
  - `A_low_bridge_has_no_room_under_it`: deck Z 1..2 → `[2]`.
  - `A_tie_takes_the_higher`: surfaces 0 and 4, level 2 → 4.
  - `An_area_floor_is_a_surface_for_a_model_not_a_piece`: an Area lifted to Z 2 → model `[0, 2]`, piece `[0]`.
  - `A_piece_reads_each_of_its_volumes_heights`: a piece whose volume is Z 0..2 under the deck Z 3..4 → `[0, 4]`; Z 0..3.5 → `[4]`.
  - `The_highest_open_surface_is_the_rest_height`: for the `CollisionTests` cases, `Surfaces(…)[^1] == RestHeight(…)`.
- [ ] **Build.** `partial class Collision`:
  - `readonly record struct Part(Outline Outline, double Bottom, double Top)`: one part of a body, heights above where it rests.
    A model is one part (`BaseOutline()`, 0, `Body.Height`); a piece one per own volume (`WorldOutline()`,
    `WorldBottom() - Position.Z`, `WorldTop() - Position.Z`).
  - `internal static List<double> Open(IReadOnlyList<Part> body, IReadOnlyList<WorldVolume> volumes, bool floors)`: candidates 0,
    each Solid top and (with `floors`) each Area `WorldBottom()` whose outline shares interior with a part; a candidate `s` is open
    when no Solid sharing interior with a part `p` has `IntervalsOverlap(s + p.Bottom, s + p.Top, w.WorldBottom(), w.WorldTop())`.
    Ascending, distinct within `Tolerance.Distance`.
  - `internal static double Nearest(IReadOnlyList<double> open, double level)`: least `|s - level|`, the higher on a tie;
    `+∞` gives the last.
  - Public: `Surfaces(PlacedModel, volumes)`, `Surfaces(IReadOnlyList<WorldVolume> piece, volumes)` (own volumes excluded, as
    `RestHeight` does), `RestNear(PlacedModel, volumes, double level)`, `RestNear(piece, volumes, level)`.
- [ ] `dotnet test tests/Tabletop.Geometry.Tests`; commit "Geometry: a body's open surfaces and the rest nearest a level".

## Task 2: A drag at a level

**Files:** modify `libs/Tabletop.Geometry/Collision.Slide.cs`; test `tests/Tabletop.Geometry.Tests/SlideTests.cs`.

- [ ] **Test first** (append to `SlideTests`), with an Impassable deck `Fx.Box(12, 10, 8, 4, 3, 4)` (x 8..16, y 8..12, Z 3..4) passed
  as a wall and a volume:
  - `At_table_level_a_model_passes_under_a_bridge_with_room`: `Slide(Fx.Model(4, 10), new(12, 10), [], [deck], [deck], level: 0)`
    → `(12, 10, 0)`.
  - `A_model_too_tall_for_the_gap_stops_at_the_bridge`: height 3.5 → `X < 8 - 0.62`.
  - `A_model_on_the_bridge_stays_on_it_at_its_level`: from `(10, 10, 4)`, to `(14, 10)`, level 4 → `(14, 10, 4)`.
  - `A_low_wall_still_stops_a_model_on_a_hill_beside_it`: a hill top 2 (not a wall) and an Impassable wall Z 0..4 on it → stops.
  - `A_model_under_a_wall_it_passes_meets_a_model_on_its_level_only`: a model standing on the deck does not stop a model passing
    under it.
  - `A_piece_at_table_level_passes_under_a_bridge`: `Slide([Fx.Box(4, 10, 2, 2, 0, 2)], new(12, 10), [deck], [deck], level: 0)` →
    `(12, 10, 0)`; `Follow` the same; `Clear` of a piece standing under the deck at level 0 leaves it where it is.
  - The existing `SlideTests` pass unchanged (the default level is `+∞`).
- [ ] **Build.**
  - `Slide(PlacedModel moving, Point2 to, models, walls, volumes, double level = double.PositiveInfinity)`: the side walls are the
    Solid walls whose outline the start does not enter (today's `Sides`, kept as `WorldVolume`s); `ground` is `volumes` less the side
    walls. At each sample the body's bottom is `Nearest(Open([part at], ground, floors: true), level)`; a model stops it as today
    (`IntervalsOverlap` and `InteriorsIntersect`), a side wall when the outlines share interior and
    `IntervalsOverlap(bottom, bottom + height, wall.WorldBottom(), wall.WorldTop())`. The end rests the same way on `ground`.
  - `Slide(piece, to, walls, volumes, level)`: the same over the piece's parts (each part's own heights against a wall), resting
    with `floors: false`.
  - `Separate(…, double level = ∞)` passes the level to each `Slide`.
  - `Follow(piece, to, walls, volumes, level = ∞)` and `Blocked(piece, walls, volumes = null, level = ∞)`: with a finite level a
    wall blocks only where it overlaps the part's height interval at the rest near the level over the volumes less the walls; with
    `+∞` (no volumes) plan only, as today.
  - `Clear(piece, walls, volumes, level = ∞)`: its free test is `Blocked`'s at that level.
  - `Rested(piece, at, volumes, level)` rests through `RestNear`.
- [ ] `dotnet test tests/Tabletop.Geometry.Tests`; commit "Geometry: slide, follow and clear at a level, under what has room".

## Task 3: Lifted: the landing and the hover

**Files:** create `libs/Tabletop.Geometry/Collision.Land.cs`; modify `Collision.Slide.cs` (`Clear`'s ring search shared);
test `tests/Tabletop.Geometry.Tests/LandTests.cs`.

- [ ] **Test first** (`LandTests`):
  - `A_free_spot_is_landed_on_as_it_is`: `Land(Fx.Model(0, 0), new(5, 5), [], [], [])` → `(5, 5, 0)`.
  - `A_spot_on_a_base_lands_at_the_nearest_clear_spot`: another model at (5, 5) → the landing overlaps it nowhere
    (`Overlap.Bases`) and lies within 1.4" of (5, 5).
  - `A_spot_in_a_wall_lands_beside_it`: the `SlideTests` wall, target (10, 10) → not entering it in plan, within 1" of (10, 10).
  - `A_model_on_another_storey_does_not_move_the_landing`: a model at (5, 5, 4) on a crate top… lands on the open table spot.
  - `The_landing_rests_on_its_highest_open_surface`: over a hill → Z 2; under a non-wall deck with room → on the deck's top.
  - `Only_allowed_spots_are_candidates`: `within: p => p.X <= 4` with a model at (5, 5) → `X <= 4`.
  - `A_group_lands_in_drag_order`: two held models at one target → the second clear of the first.
  - `The_hover_is_half_an_inch_over_the_highest_thing_under_it`: over a hill top 2 and a model 1.5 tall on it → 4;
    the open table → 0.5; a piece over a 6" Area wood → 6.5.
  - `A_lifted_piece_lands_clear_of_walls`: `Land(piece, to, walls, volumes)` equals `Clear` of the piece moved to `to`.
- [ ] **Build.**
  - `const double ClearStep, ClearReach` stay; `static Point2? Ring(Func<Point2, bool> free)`: the zero offset, then rings
    `ClearStep` apart out to `ClearReach`, the first offset `free` takes. `Clear` uses it.
  - `public const double Lift = 0.5`.
  - `Land(PlacedModel moving, Point2 to, models, walls, volumes, Func<Point2, bool>? within = null)`: `Ring` over offsets from `to`;
    a spot (rings out to 24", `LandReach`, so a frame stays bounded) is free when the base there shares interior with no Solid wall, the model rested there (`RestHeight`) overlaps no model
    (`Overlap.Bases`), and `within` takes it, tested in that order. No free spot: `to`, rested.
  - `Land(IReadOnlyList<(PlacedModel Moving, Point2 To, Func<Point2, bool>? Within)> held, stationary, walls, volumes)`: in drag
    order, each against the stationary and the held already landed.
  - `Land(piece, to, walls, volumes)`: `Clear` of the piece moved to `to`.
  - `Hover(PlacedModel at, volumes, models)`: `Lift` over the highest `WorldTop()` of any volume, and `Top()` of any model, whose
    outline shares interior with the base; 0 when none. `Hover(piece, volumes)`: over its footprints, own volumes excluded.
- [ ] `dotnet test tests/Tabletop.Geometry.Tests`; commit "Geometry: a lifted body's landing and hover height".

## Task 4: The client core: held ends, the level, the keys

**Files:** modify `client/src/Core/DragBodies.cs`, `client/src/Core/Hotkeys.cs`; create `client/src/Core/HoldLevel.cs`;
modify `client/src/Core/ContactCheck.cs`; tests `tests/Tabletop.Client.Tests/DragBodiesTests.cs`, `HoldLevelTests.cs`,
`HotkeysTests.cs`, `ContactCheckTests.cs`.

- [ ] **Test first.**
  - `DragBodiesTests`: the tests that assert a slide (`A_drag_stops_at_an_impassable_wall_and_climbs_anything_else`,
    `A_move_ask_names_the_walls_and_a_flyer_passes_models`, `A_drag_and_its_region_pass_above_a_lower_model`,
    `Each_held_model_gets_its_own_end`, the terrain `Piece` tests) pass `level: 0`, so they still test the drag at a level.
    New: `A_lifted_model_goes_straight_over_a_wall_and_lands_beyond_it` (target past the Impassable wall → lands at the
    target, drawn at `Lift`); `A_lifted_model_over_another_lands_clear_of_it_and_hovers_over_it` (drawn Z = model top + 0.5,
    landing overlaps nothing); `A_lifted_landing_keeps_to_the_reach` (`within`); `A_lifted_landing_never_lands_on_a_base_even_when_the_ask_passes_models`;
    `Alt_rests_near_the_level` (free under a deck with room at level 0 → Z 0; lifted → the deck's top);
    `A_lifted_terrain_piece_crosses_a_building_and_lands_clear`; `A_piece_at_table_level_goes_under_a_bridge`.
  - `HoldLevelTests`: `Lower(null, [0, 4])` → 4; `Lower(4, [0, 4])` → 0; `Lower(0, [0, 4])` → 0; `Raise(0, [0, 4])` → 4;
    `Raise(4, [0, 4])` → null; `Raise(null, …)` → null; a level between surfaces steps to the next one past it.
  - `HotkeysTests`: `Pagedown` → `Intent.Lower`, `Pageup` → `Intent.Raise`, with or without Shift.
  - `ContactCheckTests`: a ghost under an Impassable-free deck with room, sent at Z 0, is judged at Z 0 (no base contact with a
    model on the deck).
- [ ] **Build.**
  - `public sealed record HeldEnd(Point3 Drawn, Point3 Lands)`.
  - `DragBodies.Models(held, stationary, volumes, region, bool free, double? level, Func<Guid, Point2, bool>? within = null)` →
    `IReadOnlyDictionary<Guid, HeldEnd>`. Free: straight, `RestNear(level ?? ∞)`. Level: `Separate(…, level)` as today.
    Lifted: `Collision.Land` with the stationary always (a landing on a base is never right), `DragBodies.Walls`, each `within`;
    drawn at the target, Z `Collision.Hover(at target, volumes, stationary)`.
  - `DragBodies.Piece(terrain, pieceId, at, to, free, double? level)` → `HeldEnd`. Free: straight, rest near the level. Level:
    `Clear(…, level)` then `Follow(…, level)`. Lifted: `Collision.Land(piece, to, walls, others)`, drawn at `to` and
    `Collision.Hover(piece at to, others)`.
  - `DragBodies.Surfaces(terrain, pieceId, Placement at)`: the held piece's open surfaces there.
  - `HoldLevel.Lower(double? level, IReadOnlyList<double> surfaces)`, `Raise(…)`: null is lifted; Lower from lifted is the highest
    surface, else the highest below the level (or the level); Raise the lowest above the level, else lifted.
  - `HotKey.Pageup`, `HotKey.Pagedown` (Godot's names); `Intent.Raise`, `Intent.Lower`.
  - `ContactCheck`: each landing rests `RestNear(body, volumes, kv.Value.Position.Z)`.
- [ ] `dotnet test tests/Tabletop.Client.Tests`; commit "Client core: held pieces lifted by default, a level stepped by PageUp/PageDown".

## Task 5: The table: hover, landing mark, keys, drop

**Files:** modify `client/src/Table/TableInput.cs`, `TableInput.Hold.cs`, `TableInput.Keys.cs`, `client/src/Hud/KeyOverlay.cs`;
create `client/src/Table/LandingMarks.cs`. No unit tests (Godot glue); `dotnet build client/Tabletop.csproj`.

- [ ] `Drag.Level` (`double?`, null on every pick-up).
- [ ] `Place`: models through `DragBodies.Models(…, free, drag.Level, within)`, where `within` is, for a Proposing model inside a
  move step, `Reach.Clamp(id, p, capped)` within `1e-3` of `p`; terrain through `DragBodies.Piece(…, drag.Level)`.
  `drag.Current[id]` is the landing (the ruler, `ModelDragged`, presence and the drop read it); the node stands at `Drawn`.
  A piece whose `Drawn` is not its `Lands` shows a landing mark there.
- [ ] `LandingMarks` (a `Node3D` under the view): `Show(IReadOnlyList<(Point3 At, Yaw Yaw, Base Base)>)` for models,
  `ShowPiece(IReadOnlyList<WorldVolume>)` for a terrain piece's footprints (a rect footprint as `Base.Rect(length, width)`,
  a circle as `Base.Circle(2r)`, each at its world centre and yaw), both drawn with `Palette.BaseRing`; `Clear()`.
- [ ] Keys: `Intent.Lower`/`Intent.Raise` while `HoldingTurnable` step `drag.Level` through `HoldLevel` over the lead piece's
  surfaces where it lands (`Collision.Surfaces` for a model, `DragBodies.Surfaces` for a piece) and `Place` again from the
  cursor; otherwise unused.
- [ ] Drop and cancel: `OnLeftUp` moves each held node to `drag.Current` before anything is sent or kept; `CancelDrag` and
  `OnLeftUp` hide the marks (`Clear`).
- [ ] `KeyOverlay`: `("PageDown / PageUp (while holding)", "lower or raise the held models or terrain piece one surface, to pass under a bridge; above the highest, lifted again")`,
  and Alt's line reads "place freely: no lifting, sliding, stopping or allowance".
- [ ] `dotnet build client/Tabletop.csproj`; `./scripts/check-client`; commit "Client: held pieces hover over what is under them and land clear; PageUp/PageDown step the level".

## Task 6: The server rests near the Z it is sent

**Files:** modify `server/game/Interpreter/Battlefield.cs`, `server/game/Setup/DraftEditor.cs`; tests
`tests/Tabletop.GameServer.Tests/SettleNearLevelTests.cs`.

- [ ] **Test first** (`SettleNearLevelTests`, over a table holding a hill and a deck with room under it, built as
  `TurnedTerrainTests` builds its state):
  - `Z_zero_on_a_hill_is_the_hills_top`.
  - `Z_zero_under_a_bridge_with_room_stays_under_it`.
  - `Z_at_the_deck_top_stands_on_the_deck`.
  - `A_terrain_edit_sent_under_a_bridge_stays_under_it` (`DraftEditor` `MoveTerrain` with Z 0 under the deck → Z 0; with the
    deck's top → the top).
- [ ] **Build.** `Battlefield.Settle`: `Collision.RestNear(At(state, modelId, client), Terrain(state), client.Position.Z)`.
  `DraftEditor.Rest`: `Collision.RestNear(piece.WorldVolumes(), others, piece.Position.Z)`.
- [ ] `dotnet test tests/Tabletop.GameServer.Tests`; commit "Game server: a placement rests on its open surface nearest the Z sent".

## Task 7: Docs

- [ ] `libs/Tabletop.Geometry/AGENTS.md` (How it works: surfaces, `RestNear`, the level, `Land`, `Hover`; Layout: the two files).
- [ ] `client/AGENTS.md` (the drag paragraph: lifted by default, PageUp/PageDown, the landing mark; `HoldLevel` in the core list).
- [ ] `docs/superpowers/specs/2026-10-10-client-ux-and-themes-design.md` → Physical collision: the Client and Server bullets that
  the spec makes false (slide by default, the server ignores the client's Z).
- [ ] `docs/design/geometry/design.md` §6: `Slide`'s walls by height at a level, `Surfaces`, `RestNear`, `Land`, `Hover`.
- [ ] Grep `Collision.Follow`, "ignores the client's Z", "The client's Z is not read", "slides along" for other stale statements.
- [ ] `dotnet build && dotnet test`; commit "Docs: held pieces lift over".
