# Lifecycle suite

`sim lifecycle` plays whole games against the running stack by the path the Godot client takes: management over HTTP
(`/v1`), the game over the socket (`/ws`). Nothing is written to Postgres directly; nothing is set up by a call the client
does not make, except the two dev-mode routes below. The rules suite (`sim suite`) covers the rules; this suite covers the
path before, around and after play.

## What it drives

- Sessions: `POST /v1/dev/sessions` for fixed accounts `lifecycle-player-0..7` and `lifecycle-spectator`
  (`SocketCredentials.MintOneAsync`). Accounts are never deleted.
- Armies: `POST /v1/dev/armies/import` (dev mode only, beside the session mint) takes `{ source, list }`, an Army Forge
  export body, and stores it through the mapping `POST /v1/armies/import` uses after its fetch (`ArmyForgeImport.ToArmy`,
  the account's 200-army cap, the same problems). The list is `tests/Tabletop.Management.Tests/Fixtures/army-forge-c2-6YsWRH3py.json`
  (Wormhole Daemons of War), embedded in `sim/`, cut to its three unjoined units (`LifecycleArmy`); the Ambusher shape
  adds `Ambush` to its Beast Riders. `four-teams` also brings lists of three units cut from Human Defense Force, Battle
  Brothers and Orc Marauders (`sim/Lifecycle/Armies/`). No call reaches Army Forge.
- Game: `GET /v1/properties` (the first set of `grimdark-future` is the client's default), `POST /v1/games`,
  `POST /v1/games/{id}/join`, `GET /v1/games/{id}`, `GET /v1/games?joined=true`, `POST /v1/games/{id}/leave`,
  `DELETE /v1/games/{id}`, `DELETE /v1/armies/{id}`.
- Lobby and setup over the socket (`LobbySocket`: `Connect`, `EditMessage`, waits on `GameInfoMessage`, `BoardMessage`,
  `Refused` by edit id): `CreateTeam`, `PickTeam`, `PickArmy`, `SetConfiguration`, `StartSetup`, `AddTerrain`,
  `MoveTerrain`, `AddSetupMarker`, `EndSetup`.
- Play: `SocketDriver` and `RunLoop` with `AnswerPolicy`, over a `CompiledScenario` read back from the server
  (`LifecycleScenario.From`: the `GameInfo` and `Board` the sockets received, the recorder last), round cap 5, so the game
  ends by the rules at round 4's end: `strict` writes `game_ended`, `warn` asks `game_over` and the policy answers End.
- Replay: `/ws?watch=1` with a participant's session.

## Shapes

| Shape | Accounts and seats | Enforcement |
| --- | --- | --- |
| `two-players` | player 0 (creator) on team 1, player 1 on team 2 | strict |
| `self-play` | player 0 holds both teams, an army each | warn |
| `team-game` | players 0 and 1 on team 1, players 2 and 3 on team 2 (the creator places 1 and 3) | strict |
| `late-joiner` | player 0 on team 1; setup ends with team 2 empty; player 1 joins, takes team 2 and brings an army | strict |
| `ambusher` | as `two-players`; player 0's Beast Riders (Ambush) set aside before deployment | strict |
| `four-teams` | players 0..7, two a team on four teams (the creator places 1, 3, 5 and 7), armies from four books | strict |
| `leave-and-delete` | a lobby game: player 1 joins, takes a team and leaves; player 1's delete is 404; the creator's delete is 204 | — |

`lifecycle-spectator` joins every played game in the lobby and holds no team: it is the driver's recorder.

## What it asserts

- Every step's status from the API: `GET /v1/games/{id}` and, for every participant account, its row in
  `GET /v1/games?joined=true` (`joined` set): `lobby` after create, `setup` after `StartSetup`, `playing` after `EndSetup`,
  `ended` after the log's `game_ended`, polled for up to 10 s.
- The set: the game's `properties_hash` (create response, `GET`, `Welcome`) is the first `grimdark-future` set of
  `GET /v1/properties` and the game server's embedded set (`ScenarioCompiler.CoreProperties`); its
  `GET /v1/properties/{hash}/rules` holds `Fanatic` (model), `Ambush` (unit) and `Death Bolt` (spell).
- `Welcome`: the connecting participant, the game, and `Options` naming the ruleset's catalog (`GfOptions.Catalog` keys).
- Lobby: every edit lands in the broadcast `GameInfo` (teams, seats, armies, configuration and its optional rules); a
  `SetConfiguration` with an unknown option value is `Refused(Unknown)`.
- Setup: the board after `EndSetup` holds the moved terrain and both markers.
- Waiting for players (`late-joiner`): after `EndSetup` the log is empty (`AtRest(0)`), the status `playing`; the joiner's
  `PickArmy` starts the log.
- Play: `RunLoop`'s outcome is `Ended`, its invariants hold. The log, read by `FreeForAllChecks` (p.50, p.6): the roll-off
  ranks every team; deployment and each round's turns go round the order, one unit a turn; every model stands in its team's
  zone at round one; round one follows the deployment order, later rounds who finished activating first; `game_ended` names
  the teams holding the most markers. `ambusher`: the Beast Riders reach the table from a
  `deploy_ambusher` Ask in round 2 or later.
- Replay: the watcher's `Welcome` has `Guid.Empty` and status `ended`; its log, applied to the board, ends the game at the
  recorder's last seq; a command from it is `Refused(Watcher)`.
- Leave (`leave-and-delete`, as the client's Leave: `POST /leave`, then the socket closes): `204`, the row gone from
  `GET /v1/games/{id}` and the leaver's list, the creator's open lobby and a fresh connect without the leaver, the game in
  the lobby.
- Delete: after the creator's delete, `GET /v1/games/{id}` is 404 and the game is in no participant's list.

## How it runs

- `dotnet run --project sim -- lifecycle [--only shape,...] [--list]`; exit 0 when every shape passed. Environment as the
  socket driver's: `TABLETOP_SIM_WS`, `TABLETOP_SIM_MANAGEMENT`; no database connection.
- Each played game is named `lifecycle <run id> <shape>`. Every game a shape creates and every army it imports is deleted
  in its `finally`. At the start a run deletes `lifecycle` games and the lifecycle accounts' armies older than an hour
  (v7 id time), what an interrupted run left.
- E2E: `LifecycleTests` in `tests/Tabletop.Sim.Tests` (`[Trait("Category", "E2E")]`) plays every shape against the stack.
