# Same calls from both clients

Owner, 2026-10-25: *"we need to be sure the same calls would get made in godot and the web, i understand the view is different"*,
and *"I am fine with needing to hand test godot"*. Raised over Ambush, which has had bugs in both the server and the Godot client.

## Intent

The browser (`viewer/`) and the Godot client are two views of one game. For the same game state and the same player choice,
both send the server the same message: the same `AnswerAsk`, `MoveModels`, menu command, `Undo`, lobby or setup `Edit`, with the
same fields. Then a behaviour checked in the browser holds in Godot, and only Godot's rendering and input need hand testing.

## Goals

- **Every outbound message has one tested builder per client**, and the two agree. The browser's ports of Godot's core already
  build theirs in Godot-free TypeScript; Godot's must be in `client/src/Core/` (Godot-free, unit-tested). Where Godot builds a
  message inside Godot-dependent code (`src/Hud/`, `src/Table/`, `src/Panels/`, `src/Screens/`), that building moves into a Core
  function the node calls, with no behaviour change.
- **A shared agreement fixture**: cases of (board, game, log, the player's choice) → the exact wire message, written by a C# test
  from Godot's Core builders (`TABLETOP_WRITE_FIXTURES=1`, failing when stale, as `CombatLogViewerFixtureTests` does) and replayed by
  a Vitest test through the TypeScript builders, which must produce byte-equal JSON after the same normalisation. Cases cover every
  Ask mode (Choose, Move, PickPieces, Assign, Allocate, TokenSpend, Confirm, game_over), deployment picks and placements, hand
  moves, each play menu item (set aside, return to deployment, transports, grants, wounds, conditions, objective control, score,
  marker lock), undo targets, and the lobby and setup edits each control sends.
- **The decisions behind the calls agree too**, where they decide what can be sent: whose decision it is (`Seats.Acting`,
  `AskViewModel.Mine`), what a deploy pick offers and whether a team is mid-deploy (`DeployView.Pickable`, `MidDeploy`), which
  menu items appear (`MenuModel`), the undo plan (`UndoPlan`). The same fixture carries their answers.
- **Ambush in the browser, end to end**: a stack test (`viewer/tests/e2e`, `TABLETOP_E2E=1`) that sets a unit aside from the roster
  menu, plays to the round boundary, and places the Ambusher through the decision box: a placement within 9" of an enemy refused
  (`ambush_distance`) and one over 9" accepted; and the same with one account holding both teams.

## Non-goals

- Rendering, camera, input handling, animation, sound: Godot is hand tested for those.
- Godot-only features the browser does not have (the reach outline, height levels, the tape measure). Their messages are still
  built in Core and still covered by C# tests; they have no browser counterpart to agree with.

## Constraints

- No behaviour change in either client from the moves into Core; the existing client and viewer tests stay green.
- The fixture is written from Godot's code, never by hand, and the browser is fixed to match it, not the reverse, unless the case
  shows a Godot bug; then the fix lands in both with a test.
- A new outbound message in either client is not done until it has a fixture case (written into `client/AGENTS.md` and
  `viewer/AGENTS.md`).
