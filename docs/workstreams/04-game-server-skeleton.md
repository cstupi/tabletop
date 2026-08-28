# 04 — Game server skeleton

**Owns:** `server/game/`
**Depends on:** 01 (Protocol, Content, Geometry signatures)
**Branch:** `ws/04-game-server-skeleton`

Read `docs/workstreams/README.md`, `docs/architecture.md`, and `CLAUDE.md` first.

## Goal

The spine that every later game feature hangs off: accept a WebSocket connection, authenticate
it, take Commands, emit Events, persist the event log, and survive a reconnect.

**No rules.** No dice, no scheduler policy, no resolution pipelines, no rule packs, no OPR. This
workstream proves the machinery works with a trivial command set; workstreams 07 and 08 put real
rules behind it.

## Deliverable

An ASP.NET Core app in `server/game/`, `net10.0`, no Godot reference, that:

- Accepts a WebSocket connection carrying a Stytch session token
- Hosts a game session in memory
- Dispatches Commands to a validator, emits ordered Events
- Appends every event to Postgres
- Rebuilds session state from the log when a client reconnects or the process restarts

## Architecture decisions already made

These are settled. Do not relitigate them; if one seems wrong, stop and report.

**State model.** Live game state lives in memory. The append-only event log is the durable
truth. On connect or restart, rehydrate from the log. Do not treat Postgres as the live store,
and do not replay from sequence 0 on every command.

**Event log schema.** `(game_id, seq, kind, payload jsonb)` with a unique constraint on
`(game_id, seq)`. `jsonb` means adding an event type is not a migration.

**Database.** Same Postgres instance as the management service, **separate schema**. The event
log and the accounts tables never join. One database to run locally; clear ownership.

**Transport.** Raw WebSocket carrying Protocol messages. Godot's `MultiplayerAPI`, `[Rpc]`, and
`MultiplayerSynchronizer` are not used — they assume Godot on both ends and node-tree
replication, which is the wrong model for discrete board state.

**Auth.** Stytch is the token authority. The client presents a Stytch session token on connect;
validate it against Stytch. There is no bespoke token format. Authorization — is it this team's
window, is that unit theirs — is a session concern here, not a rules concern, and not in `libs/`.

**Sequence numbers are the contract.** Every event carries one. Ordering is total per game.
Undo is replay to sequence N, so events must be sufficient to reconstruct state without
consulting anything else.

**RNG lives in state as a seed.** Every roll is an event. Do not call `Random` anywhere outside
the seeded generator held in game state — rewinding and redoing an action must reproduce the
same dice.

## Session creation

Who instantiates a session when a lobby room starts is **not yet decided** — the lobby does not
exist. For this workstream, create sessions from a stub: a dev-only endpoint or a fixture that
starts a game with a fixed set of players and teams. Keep that seam narrow and obviously
temporary, so the real lobby handoff can replace it without touching anything else.

## Trivial command set

Enough to prove the spine, and no more. Something like `Ping` → `Pong`, plus one command that
mutates a scrap of state so replay and reconnect are actually testable. **Do not** implement
`MoveUnit`, `DeclareShoot`, or anything from a real ruleset.

## Reconnect and shutdown

Both matter more than they look:

- **Reconnect** — a four-player game runs hours. A client that drops must be able to rejoin and
  receive enough to rebuild its view. This is the same mechanism as crash recovery.
- **Graceful shutdown** — this is the only service holding live state, and it is what constrains
  deploys. On SIGTERM, drain to the event log before exiting. On Fargate this runs inside
  `stopTimeout`.

## Out of scope — do not build

Rules of any kind. Scheduler policy. Rule packs. Dice conventions. Lobby, rooms, invites, team
assignment. Accounts, armies, assets. The client. AWS infrastructure. Anything in `libs/` — you
consume those contracts, you do not change them.

## Done when

1. A test client connects over WebSocket with a valid Stytch token and is accepted; an invalid
   token is rejected.
2. A command produces an event, the event is in Postgres, and the sequence number is correct.
3. Killing and restarting the process rebuilds session state from the log — proven by a test.
4. A client reconnecting mid-session catches up correctly — proven by a test.
5. SIGTERM drains rather than dropping events.
6. `dotnet build` from the repo root succeeds; `dotnet test` passes.

Paste the commands and their output.

## Decisions

Record here, one line of reasoning each.
