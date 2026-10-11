# Client

The Godot 4.7 / C# client in `client/` is the only Godot dependency and the only non-headless
deliverable. It is a renderer, an input surface and a geometry engine over one session object. It
owns login, the game list, joining, and one game screen serving `lobby`, `setup`, `playing` and
`ended`, since all four are one socket and one hydrate path. The table is drawn from a projection
that `Tabletop.Protocol.EventApplier` builds by applying every event. No ruleset code, no dice, no
turn order and no legality check runs on the player's machine.

Everything that holds a decision lives under `client/src/Core/`: the session object, the
management client, the socket seam, the cache, the built-in content, the generators and every view
model. `Core` is compiled from source into `tests/Tabletop.Client.Tests`, so it must run without
an engine. The Godot layer above it draws what those decided and forwards input back. It is tested
by running the game.

| File | Sections |
| --- | --- |
| [overview.md](overview.md) | Scope, Context |
| [core-and-screens.md](core-and-screens.md) | Core and the Godot layer, Screens and the session object |
| [connection.md](connection.md) | The connection, The projection, Desync and resync, Presence |
| [table-view.md](table-view.md) | The table view, Mesh loading and the content-addressed cache, Input, presence and a drag, The ruler and the movement region |
| [play-screen.md](play-screen.md) | The play screen, The decision box, A spend over models and a pick of one model, The combat log, The right-click menu |
| [setup-and-animation.md](setup-and-animation.md) | Setup: the lobby panel, the toolbar, built-in content and the generators; Animation |
| [decisions-and-interfaces.md](decisions-and-interfaces.md) | Key decisions, Interfaces (produced for and consumed from other modules) |
| [sequences.md](sequences.md) | Connect and hydrate, Undo to here, A desync and its resync, A setup edit, Embark then disembark |
| [state-failures-testing.md](state-failures-testing.md) | State and data, Failure modes, Testing |
| [rulings-and-references.md](rulings-and-references.md) | Rulings and deferred work, References |
