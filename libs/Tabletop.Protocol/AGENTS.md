# libs/Tabletop.Protocol

Every socket message, the wire JSON, the event log's payloads, and the projection built from them.
`net10.0`, Godot-free, persistence-free. References `Tabletop.Geometry` and `Tabletop.Content`. Consumed by `server/game`, `server/management`, `client/`, `sim/` and the viewer's generated types.

## Layout

- `Wire.cs`: `SocketMessage` and its kinds (`Connect`, `CommandMessage`, `EditMessage`, `Welcome`, `GameInfoMessage`, `BoardMessage`, `EventsMessage`, `AtRest`, `Refused`, `Rejected`, `PresenceMessage`, `PingMessage`, `PresenceEnded`, `SightQuery`, `SightAnswer` with `SightLine`, `ModelFacts`), `RejectReason`, `RefusalReason`, `WireJson`.
- `Commands.cs`: `Command` (`AnswerAsk` and the others). `Edits.cs`: `Edit`, `LobbyEdit` (rows, never logged).
- `Asks.cs`: `Ask`, `AskOption`, `AskAnswer`, and the `LegalSet` shapes.
- `Events.cs`, `StateChanges.cs`: `EventPayload`, `GameEvent`, `StateChange` (the log's content). `SequenceStarted.Outline` (`OutlineStep`) is null in logs written before it, and `Frame.Outline` is then empty.
- `State.cs`, `Game.cs`: `Location`, `GameInfo`, `GameStatus`, `SchedulerShape`, `Enforcement`, `Team`. `RollRecord.Basis` (`ThresholdBasis`, `ThresholdChange`) is null for a threshold no stat gave and in logs written before it; `EventApplier` does not read it. `RollModifier.Via` (the granting rule; for a player's modifier, the participant's name), `Die.Triggered` (what the die set off; `Die` compares it by value) and `RollRecord.Cover` (`CoverVerdict`, a shooting block's p.11 verdict) are optional, null in logs written before them, and read only by the combat log.
- `EventApplier.cs`: applies events to a `Projection`; `EffectiveLog` drops undone events; `ProjectTo(seq)`.
- `Projection.cs`: `Projection`, `UnitInPlay`, `ModelInPlay`; only `EventApplier` writes it.
- `Presence.cs`: presence, cursor, selection, measurement; latest wins, never logged.
- `ProtocolVersion.cs`: `Current`, the `Tabletop-Client-Version` header, the 426 status.

## How it works

- Client and server exchange `SocketMessage` JSON through `WireJson` (polymorphic, options built on `ContentJson`).
- Three message kinds travel inward: `Command` (logged), `Edit` (applied to rows, persisted, not logged) and presence. A `SightQuery` is a question, answered and never stored.
- The server answers with events, `AtRest` and `Refused`; a `SightQuery` with a `SightAnswer` to the asker alone, outside the log. `ModelFacts` follow the batches that change them.
- Replay applies logged events and never re-derives them. The server, client, sim and viewer all run `EventApplier`.

## Commands

```bash
dotnet build libs/Tabletop.Protocol
dotnet test tests/Tabletop.Protocol.Tests
TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests   # after a wire change
```

## Conventions and pitfalls

- `viewer/src/lib/protocol/wire.generated.ts` is generated from `Connect`, `Welcome`, `GameInfoMessage`, `BoardMessage`, `EventsMessage`, `Refused` and `Rejected` plus what they reference. A plain `dotnet test` fails when it is stale. Never hand-edit it.
- A change to `EventApplier` ports to `viewer/src/lib/live/` until `viewer/tests/agreement.test.ts` passes, and regenerates `viewer/tests/fixtures/`.
- `ProtocolVersion.Current` is sent on every HTTP request and in `Connect`; management answers 426 below its minimum.
- `ProjectionDesyncException` means an event named an unknown id; the projection must be rebuilt.
- The rules engine is not here. This library holds only the contract.

## Docs

- `docs/design/contracts/design.md`
- `docs/superpowers/specs/2026-08-29-identity-and-enumerations-design.md`
- `docs/superpowers/specs/2026-08-30-presence-design.md`
- `docs/superpowers/specs/2026-09-03-action-sequences-design.md`
