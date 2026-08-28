# 01 — Contracts

**Owns:** `libs/`
**Depends on:** nothing.
**Branch:** `ws/01-contracts`

Read `docs/workstreams/README.md`, `docs/architecture.md`, and `CLAUDE.md` first.

## Goal

Define the three shared libraries as **types and signatures only**. This workstream unblocks
the game server (04) and the client (05), which are two ends of the same contracts and will
diverge if they start before these are settled.

## Deliverable

Three `net10.0` class libraries under `libs/`, no Godot reference, added to `Tabletop.sln`:

| Project | Shared by | Contains |
| --- | --- | --- |
| `Tabletop.Geometry` | client, game server | Volume types, distance, LoS, settling — **signatures only** |
| `Tabletop.Protocol` | client, game server | Command and event types, wire format |
| `Tabletop.Content` | client, both servers | Army, unit, weapon, art profile types |

## What "signatures only" means

Method bodies throw `NotImplementedException`. Workstream 02 implements `Tabletop.Geometry`
behind these signatures; 04 and 05 code against them immediately.

You are designing an API surface, not an algorithm. Spend the effort on names, shapes, and
what belongs on which type.

## Boundaries

**The rules engine is not in `libs/`.** No pipelines, no scheduler, no ability catalog, no
modifier system, no rule packs, no dice. Those live only in `server/game/`. If you are
modelling how a wound is resolved, you are in the wrong workstream.

`libs/` never learns about persistence — no ORM entities, no attributes, no `DbContext`.

Geometry is in inches; one world unit is one inch. Distances are edge-to-edge between bases,
never centre-to-centre.

## Protocol shape

Commands go up, events come down. Commands are intents (`MoveUnit`, `DeclareShoot`,
`EndActivation`); events are facts (`ModelMoved`, `DiceRolled`, `WoundsApplied`). Events are
ordered and carry a sequence number — the event log is `(game_id, seq, kind, payload)`.

Design for two things the architecture depends on:

- **Undo is replay to sequence N.** Events must be sufficient to reconstruct state.
- **Continuous actions are sent as parameters, not enumerations.** A movement command's
  affordance is described by allowance, terrain multipliers, and impassable volume ids — the
  client renders the region. Make sure the protocol can express that.

## Out of scope — do not build

Implementations of anything. Persistence. Serialization tuning. The game server, the client,
the management API. Auth types — Stytch is the token authority and there is no bespoke token
format.

## Done when

1. Three projects exist, build clean, and are in `Tabletop.sln`.
2. `dotnet build` from the repo root succeeds.
3. No project in `libs/` references Godot or any persistence library.
4. A short `libs/README.md` states what each library is for and what may not go in it.

## Decisions

Record here, one line of reasoning each.
