# Replay

A player replays a game they are in, ended or running, from its start. The table, the Hud and the combat log
show the game at a chosen position in its log; nothing sent from a replay changes the game.

## Entry and exit

- Home's game list marks each row the account is in (`GameSummary.Joined`, "(you)") and offers **Replay** on one
  that is `playing` or `ended` (`GameRowActions.CanReplay`). **Games I'm in** reloads the list with
  `GET /v1/games?joined=true`.
- Replay calls `Session.ReplayAsync(gameId)`, which connects like a join but on `/ws?watch=1`: the game server
  holds that socket as a watcher whatever the account (game-server design → Watchers). An admin's replay is the
  same socket.
- Home (or the Game over modal's Leave) closes it through `Session.DisconnectAsync` and returns to the game list.
  The participant row stays.

## The timeline

`ReplayTimeline` (Core) runs over the live log (`Session.Applier.Log`), which a running game keeps extending.
Position counts the events applied: 0 is the board as setup left it, `Count` the whole log. The projection at a
position is the log's first events as they happened, so an undo plays as the step back it was and the combat log
shows its `Undone to:` line. Forward applies the next events to the same applier; backward builds a fresh
`EventApplier` from the board.

`ReplayTimeline.Stops(log, unit)` gives the positions each unit stops at:

| Unit | Stops |
| --- | --- |
| Step | before each player input: an event a participant's command wrote, after one the engine or another participant wrote (`GameEvent.Actor`). A step shows an input and everything the rules did with it. |
| Action | before each action frame starts: a `sequence_started` with an action while none is open; an undo back past an open one closes it. |
| Turn | just after each `turn_started`, so the header names the turn. |
| Round | just after each `round_started`. |

Every unit also stops at 0 and at the end.

## Session

- `Session.Replay` is the timeline from the board on; `Session.Replaying` holds from `ReplayAsync` until home.
- `Session.Projection` is the replay's projection and `Session.Log` its shown events, so every part that reads them
  (table, roster, unit card, header, decision box, combat log, dice tray) shows the replay's position unchanged.
- Live events extend the timeline and leave the position where it is. A reconnect rebuilds and returns to the
  position it left.
- `SeekReplay(position, animate)`: forward and animated, it raises `EventsApplied` with the events played and
  `Fired` with their lasers, so the table glides, floating results and turn banners show and the camera follows;
  otherwise it raises `BoardChanged`, which redraws at the new position and makes `Animator` drop what it had
  queued.
- Nothing goes out but the connect and reauths: commands and edits throw, undo, sight queries, presence and pings
  send nothing, and live presence and pings are not shown.
- `IsWatching` holds, so the play screen's watcher rules apply (no drags, menus or undo); the corner reads
  "— replay" and the header `HeaderView.Replaying`. The table's right-click opens nothing.

## The replay bar

`ReplayBar` (`src/Hud/`) takes the left column's foot (`HudPart.Replay`, 420 × 112; the roster and unit card
share what is left above it).

- Buttons: back and forward by round (`«R`, `R»`), turn (`«T`, `T»`), action (`‹A`, `A›`) and step (`‹`, `›`),
  and Play/Pause. A forward step or action plays out as play does; a turn, a round and anything backward jump.
- Play steps forward one step every `ReplayClock.StepSeconds` (1.5 s) divided by the speed (0.5×, 1×, 2×, 4×,
  8×), animated up to 1×. At the end of an ended game it pauses, and Play starts again from the beginning; at the
  end of a running game it waits for the next events.
- The scrubber spans the whole log and jumps where it is dragged. The caption reads `Round 2 · 140 of 2210`.
