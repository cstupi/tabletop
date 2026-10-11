# Client: sequences

## Sequences

### Connect and hydrate

```mermaid
sequenceDiagram
  participant M as Main
  participant S as Session
  participant GS as game server
  participant SC as GameScreen
  M->>S: ConnectAsync(gameId)
  S->>S: Reset(): drop applier, draft, presence
  S->>M: PhaseChanged -> Connecting -> Game.tscn
  S->>GS: Connect(jwt, gameId, ProtocolVersion.Current)
  alt rejected
    GS-->>S: Rejected: UpgradeRequired, InvalidSession or NotAParticipant
    GS-->>S: close
    S->>M: PhaseChanged -> UpdateRequired or LoggedIn
  else accepted
    GS-->>S: Welcome(participantId, GameInfo)
    S->>SC: PhaseChanged Connected, GameChanged
    opt status is setup or later
      GS-->>S: BoardMessage(board)
      S->>S: setup: Draft = board.Table, playing: new EventApplier
      S->>SC: BoardChanged
    end
    opt status is playing or ended
      GS-->>S: EventsMessage x N, up to 500 events each
      S->>S: Applier.ApplyAll
      S->>SC: EventsApplied(batch)
    end
    GS-->>S: presence snapshot, then live frames
  end
```

A reconnect runs this identical path, because `ConnectAsync` resets first.

A `BoardMessage` arriving before `Welcome` is dropped. The `playing`/`setup` decision reads
`Game.Status`, which `Welcome` sets. The client depends on the server's ordering and has no
reordering buffer. An `EventsMessage` arriving while `Applier` is null is ignored, which happens at
status `setup` or before the board. An ended game that never ran setup sends `Welcome` and nothing
else, so the game screen must render with `Draft` and `Applier` both null.

### Undo to here

```mermaid
sequenceDiagram
  participant P as player
  participant LP as CombatLogPanel
  participant S as Session
  participant GS as game server
  P->>LP: Undo to here on row 12
  LP->>S: UndoToAsync(12)
  S->>S: _predicted = Applier.ProjectTo(12), PendingUndoTo = 12
  S->>LP: EventsApplied([]) -> redraw against the prediction
  S->>GS: CommandMessage(id, Undo(12))
  alt accepted
    GS-->>S: EventsMessage([... Undone(ToSeq 12)])
    S->>S: ApplyAll, then ClearPrediction
    S->>LP: EventsApplied -> the truth is now the prediction
  else refused
    GS-->>S: Refused(id, Illegal, detail)
    S->>S: ClearPrediction
    S->>LP: EventsApplied([]) -> back to the truth
  end
```

Neither branch has rollback machinery.

The prediction clears only on an `Undone` whose `ToSeq` equals `PendingUndoTo`, or on a `Refused`
matching the one tracked command id. A second undo issued while the first is pending overwrites
both fields. A server answer that undoes to a different sequence number leaves the prediction
displayed indefinitely. The server refuses an `Undo` whose target is neither `0` nor a live event's
seq, which is the case the `Refused` branch exists for. Events arriving during a pending undo are
applied to the applier, but are invisible while the prediction shadows it.

### A desync and its resync

```mermaid
sequenceDiagram
  participant SC as GameScreen
  participant S as Session
  participant A as EventApplier
  participant GS as game server
  GS-->>S: EventsMessage: marker_moved naming an id nothing placed
  S->>A: ApplyAll
  A-->>S: ProjectionDesyncException(kind, id)
  alt reached past _desyncFloor
    S->>SC: ConnectionLost(Error = the exception, willReconnect true)
    S->>S: _desyncFloor = Projection.LastSeq, close, Reset()
    S->>GS: Connect(jwt, gameId, ProtocolVersion.Current)
    GS-->>S: Welcome, BoardMessage, EventsMessage x N
    S->>SC: GameChanged, BoardChanged, EventsApplied
  else at or under it
    S->>S: LastCloseReason = the message, close, Reset()
    S->>SC: PhaseChanged LoggedIn, ConnectionLost(willReconnect false)
    Note over SC: Main.Route swaps in Home, which prints LastCloseReason
  end
```

The first branch's rebuild is the connect-and-hydrate sequence unchanged.

The applier is partially applied when it throws, which is why the projection is dropped rather than
repaired. The socket carries no fault of its own, so the close is the client's own teardown and the
close-code inference plays no part.

### A setup edit

```mermaid
sequenceDiagram
  participant A as player A
  participant SA as Session A
  participant GS as game server
  participant SB as Session B
  participant B as player B
  loop dragging, 20 Hz
    A->>SA: motion
    SA->>GS: presence: held placement
    GS->>SB: relay
    SB->>B: PresenceOverlay moves the node
  end
  A->>SA: drop -> SendEditAsync(MoveTerrain(id, position, yaw))
  SA->>GS: EditMessage(editId, edit)
  GS->>GS: apply to the draft, then write the row
  GS->>SA: BoardMessage(whole draft)
  GS->>SB: BoardMessage(whole draft)
  SA->>SA: Draft = board.Table, BoardChanged -> Redraw
  SB->>SB: Draft = board.Table, BoardChanged -> Redraw
```

There is no client-side merge. The server broadcasts the whole draft and the last drop wins.

The broadcast is the whole `TableState`, so a generator batch is one `BoardMessage` per
`AddTerrain`: up to 21 full-draft broadcasts and 21 `Redraw` passes in quick succession.
`ReplaceDraft` from a large layout is a single frame that must stay under the server's 4 MiB frame
limit. An edit refused with `WrongStatus` toasts and leaves the piece where the last broadcast put
it. That happens when the flip to `playing` lands between the drag and the drop.

### Embark, then disembark

```mermaid
sequenceDiagram
  participant AP as DecisionBox
  participant TV as TableView
  participant S as Session
  participant GS as game server
  GS-->>S: AskOpened(move, MoveRegion with Containment.Enterable)
  S->>AP: EventsApplied
  AP->>TV: highlight the transport, note names it
  AP->>AP: PieceLeftClicked on the transport
  AP->>S: AnswerAsk(Destinations: one Inside(container) per model of the unit)
  S->>GS: command
  GS-->>S: models_moved: every model Inside
  S->>TV: Redraw frees every occupant node
  Note over GS: next activation of that unit
  GS-->>S: AskOpened(move, Containment.ExitingId = transport, ExitWithin = 6)
  AP->>TV: ShowExitGhosts(modelIds, containerId)
  AP->>AP: MoveRuler.ExitRegion -> MoveRegionNode
  AP->>S: AnswerAsk(Destinations: From = Inside, To = OnTable)
  S->>GS: command
  GS->>GS: GfMoves.ExitPlacement re-checks every model
  GS-->>S: models_moved, a model placed too far stays Inside
  AP->>TV: ClearExitGhosts
```

The exit ghosts are the one control surface the client draws that is not part of the projection.

`Enterable` and `ExitingId` are never both set. That invariant lives on the server, and the client
would render nonsense if it were broken. The exit ghosts are freed by `ClearExitGhosts` when the
answer is sent. If the Ask is closed by something other than this player's answer, such as a
teammate answering or the frame being abandoned, the decision box must clear them on the next
`EventsApplied`. `_renderedAsk` staleness checks exist for that case. A ghost dropped outside the
drawn region still commits, and the server leaves that model inside. The offer is drawn, not
enforced.

