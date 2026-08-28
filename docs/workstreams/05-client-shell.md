# 05 — Client shell

**Owns:** `client/`
**Depends on:** 01 (Geometry, Content signatures), 02 (Geometry implementation)
**Branch:** `ws/05-client-shell`

Read `docs/workstreams/README.md`, `docs/architecture.md`, and `CLAUDE.md` first.

## Goal

The table, the camera, and the mouse. Everything a player does that involves no rules and no
server: look around, pick up a model, put it down, measure the distance between two things.

**This workstream connects to nothing.** No WebSocket, no server, no auth, no protocol. It is a
local sandbox that proves the interaction that is the point of the product — measuring and
dragging with a mouse — feels right before anything is on the wire.

## Deliverable

In the existing Godot project at `client/`:

- A table rendered at a real size, in inches (one world unit is one inch)
- A camera that orbits, pans, and zooms over it comfortably
- Placeholder models that can be picked up, dragged, and dropped
- Models settle onto whatever surface they land on, via `Tabletop.Geometry`
- Edge-to-edge distance measurement between two bases, displayed live while dragging
- A movement ghost: a region rendered from parameters, not computed from rules

## The load-bearing constraint

**Godot is a renderer.** It is never asked a question the rules depend on. Every distance,
every intersection, every settle is computed by `Tabletop.Geometry` — not by Godot physics, not
by `RayCast3D`, not by collision shapes in the scene tree.

You may use Godot nodes to *draw* volumes. You may not use them to *answer* with volumes. If
you find yourself reading a result out of a Godot physics query and using it for anything but
visuals, stop.

This is what makes the game headlessly testable and the server authoritative. It is the single
easiest thing to get wrong in this workstream.

## Client-side geometry is cosmetic

Everything you compute here is a prediction. Later, when there is a server, it will be the
authority and this code becomes a latency hider — when the two disagree, the server wins and
the client resyncs. A model snaps back, and that is the whole cost.

Two consequences:

- **Do not** build reconciliation machinery, rollback, or determinism harnesses. Not needed.
- **Do** keep local prediction cleanly separated from state, so that state can later arrive from
  the wire instead of from local mutation. Assume every position you compute will one day be
  overwritten by an event.

## Movement ghost: parameters, not rules

Movement is continuous — anywhere within an allowance, not a list of squares. The server will
eventually send **parameters**: an allowance in inches, terrain multipliers, impassable volume
ids. The client renders the reachable region from those.

For this workstream, hardcode a parameter set in a config or dev panel. **Do not** implement
terrain difficulty rules, movement rules, or anything that decides what the allowance should
be. You are building the renderer of a constraint, not the constraint.

## Out of scope — do not build

Networking, WebSocket, protocol, auth, login. Rules, dice, turn order, legality. Army loading,
Army Forge, asset upload, glTF loading from a server. Terrain placement as commands — that is
the vertical slice in workstream 06 and needs a server. UI beyond what is needed to see the
above working.

## Godot notes

`CLAUDE.md` has the build ordering trap: Godot's import step wipes
`client/.godot/mono/temp/bin/`, so build *after* an import, not before, or every script fails
with "class could not be found".

`.tscn`, `.tres`, `.godot`, and `.import` files are owned by the editor. Hand-edit only when
necessary and never reformat them.

C# only, no GDScript. New `.cs` files under `client/src/` are picked up automatically.

## Done when

1. `./scripts/godot` runs and you can orbit, pan, and zoom over a correctly sized table.
2. A model can be dragged and settles onto terrain beneath it.
3. Distance between two bases displays live and is edge-to-edge, not centre-to-centre.
4. A movement ghost renders from hardcoded parameters.
5. Every geometric answer comes from `Tabletop.Geometry` — verifiable by grep: no physics query
   result feeds anything but rendering.
6. `dotnet build` from the repo root succeeds.

A screenshot or recording is the proof for 1–4. Paste build output for 6.

## Decisions

Record here, one line of reasoning each.
