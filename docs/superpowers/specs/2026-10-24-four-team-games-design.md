# Four-team games

Owner, 2026-10-24: *"Make sure there is tests for multiple armies and 4 teams with 2 players on each team."*

The lifecycle suite's largest shape is `team-game`: two teams of two accounts. Nothing plays four teams.

## The lifecycle shape

`four-teams`: eight accounts, two a team on four teams, each bringing its own army, the eight armies from at least four
different army books. Strict enforcement. The creator places the second account of each team as `team-game` does. It plays
from `POST /v1/games` to delete as every shape does, the status asserted at each step: the roll-off ranks all four teams,
every army deploys in its team's zone, activations go round all four teams, the game reaches its end with a winner, `ended`
stands in the row and the replay plays over `/ws?watch=1`. `LifecycleRunner.Players` grows to eight accounts. `LifecycleTests`
runs it with the other shapes.

## Game server

Unit tests in `tests/` over the game server for a four-team game of eight participants: the roll-off orders four teams;
deployment and activation order alternate across four teams with two armies each as the rulebook's multiplayer rules say (cited in the tests); morale, objectives and the winner count all four teams. Where the rules suite's in-process
driver can seat four teams, one suite game plays a four-team, eight-army game to its end.

Any defect these find is fixed on the game server with a test, and named in the handoff.
