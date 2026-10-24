# Held pieces lift over

Owner, 2026-10-24: *"Held models should move up and over things during placement or moving around - there are many
instances where some models can wrap around an enemy and right now that is very tedious to do. There should be keys to
lower/raise the models being held in case they need to go under a bridge but the default is to lift over. This is
especially important for terrain placement."*

Today a held model slides along other bases and Impassable Solids at table level (`Collision.Separate`, `Collision.Slide`)
and a held terrain piece slides along Impassable pieces (`Collision.Follow`), so a model cannot be carried past an enemy
unit to its far side, and a piece cannot be carried past another. Every rest takes the highest Solid top under any part of
the body (`Collision.Rest`), so nothing can stand under an overhang, and the server rests every placement the same way
whatever Z the client sent (`Battlefield.Settle`).

## Surfaces

A body's **surfaces** where it stands are the heights it could rest at: the table (0), each Solid top and each Area floor
(`WorldBottom`, as `Rest(floors: true)` reads it today) whose footprint shares interior with the body's footprint. A surface
`s` is **open** when the body standing on it, `[s, s + height]`, shares no vertical interval with any Solid under its
footprint (touching is not overlapping). With no overhang the only open surfaces are the highest Solid top under the body and
anything above it, so the highest open surface is today's rest height.

`Collision` gains the surfaces of a model (base outline, height) and of a terrain piece (its footprints and volume heights,
its own volumes excluded), and a rest **near a height**: the open surface nearest it, the higher on a tie.

## Held: lifted, or at a level

Every pick-up starts **lifted**. PageDown lowers the held pieces one surface, PageUp raises them one; above the highest open
surface under them is lifted again. The chosen level holds as the drag moves: at each step each held piece rests on its open
surface nearest the level. The key help lists both keys.

- **Lifted.** Each held piece goes straight to its target: no slide against models or walls. Inside a move step the target
  is still held to the model's reach (`MoveRegions.Clamp`, the allowance and the difficult cap), and the ruler still follows
  the route round what the move may not pass (`MoveRegions.Route`). The piece is drawn hovering 0.5" above the highest top of
  anything under its footprint, models included. A base outline drawn on the surface marks where it will land.
- **At a level.** Today's drag at that height: the piece slides along other models and walls whose vertical extent overlaps
  its body at that level (`Overlap.IntervalsOverlap`), so it passes under a Solid whose bottom is at or above its top, and
  rests on its open surface nearest the level.

**Landing.** A drop, and the landing marker, put a lifted piece at the nearest spot to its target (rings outward, as
`Collision.Clear` searches) where it is clear: a model's base overlaps no other model's base (the held ones already landed,
in drag order, included) and enters no wall `DragBodies.Walls` lists; a terrain piece's footprint enters no piece declared
Impassable, as today. Inside a move step only spots inside the model's reach are candidates. It rests on its highest open
surface there. A piece at a level lands where it was drawn. Alt places freely, as today: straight to the target, no slide, no
clearing; the drop still rests on the open surface nearest the level (the highest when lifted).

## Server

`Battlefield.Settle` reads the client's Z as the level: the model rests on its open surface nearest that Z. A client that
sends Z = 0 for a model on a hill or a crate gets the hill's or crate's top, as today, because 0 is not open there; a model
sent at table level under a bridge with room stays under it. The sim's placements and every existing test keep their
results.
