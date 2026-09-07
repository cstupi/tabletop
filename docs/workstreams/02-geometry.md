> **Superseded 2026-09-06 — do not execute.** Written 2026-08-24, before the first design
> session, and describing a data model that no longer exists. Replaced by
> `docs/superpowers/plans/2026-09-05-geometry.md`. See [README.md](README.md) for the full fate
> of this file. Kept only as a record of how the project was first scoped.

# 02 — Geometry

**Owns:** `libs/Tabletop.Geometry/`
**Depends on:** 01 (the API surface it implements)
**Branch:** `ws/02-geometry`

Read `docs/workstreams/README.md`, `docs/architecture.md`, and `CLAUDE.md` first.

## Goal

Implement the geometry library behind the signatures workstream 01 defined. Pure math, headless
tests, no Godot, no rules, no I/O.

This is the only library both the client and the game server run. The client uses it to draw
affordances; the game server uses it to adjudicate. They run it for different reasons — see
"Correctness" below, because the answer is less strict than it looks.

## Scope

- **Collision volumes** — boxes, cylinders, convex hulls. Never the visual mesh.
- **Distance** — edge-to-edge between bases, never centre-to-centre.
- **Line of sight** — ray/volume intersection against collision volumes only.
- **Surface settling** — raycast down through terrain volumes to place a model on whatever it
  lands on.

One world unit is one inch. Everything is 3D.

## Correctness

Both sides run this, but **only the server's answer counts**. The client's copy is a prediction
that exists to remove the feeling of latency; when the two disagree, the server wins and the
client resyncs. A model snaps back, and that is the whole cost.

So: **bit-exact cross-platform determinism is not a requirement.** The client is Windows and the
server is Linux, and small floating-point divergence between them is acceptable because it
cannot compound — the client holds no authoritative state to diverge into. That constraint
belongs to lockstep architectures, which this deliberately is not.

Do not spend effort on fixed-point math, fast-math flags, or reproducibility harnesses. Do spend
it on being correct, readable, and fast enough to run per-frame during a drag.

## Out of scope — do not build

Anything with a rule in it: movement allowances, terrain difficulty costs, cover, weapon ranges,
what blocks what. This library answers *"is there a clear line between these two volumes"* and
*"how far apart are these two bases"*. It never answers *"can this unit see that one"* — that is
the game server, which calls this library and applies rules to the result.

Also out: Godot types, serialization, persistence, the protocol, rendering.

## Testing

`CLAUDE.md` notes there is no test project yet and that adding one (`net10.0` class library +
xUnit) is right when there is logic worth testing headlessly. This is that moment — **create it
in this workstream.**

Geometry is the best-tested thing in the codebase because it is pure and consequential. Cover
degenerate cases explicitly: zero-length rays, coincident volumes, bases exactly touching, a
model settling on the boundary between two terrain pieces, LoS exactly grazing a volume edge.

## Done when

1. Every signature from `Tabletop.Geometry` is implemented — no `NotImplementedException` left.
2. A test project exists and `dotnet test` passes.
3. `dotnet build` from the repo root succeeds.
4. No reference to Godot, and nothing outside `libs/Tabletop.Geometry/` and the test project is
   modified.

Paste the test output. Do not claim done without it.

## Decisions

Record here, one line of reasoning each.
