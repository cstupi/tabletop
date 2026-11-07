# Flaky GameServer unit test

`tests/Tabletop.GameServer.Tests` fails about once in five full runs. The one name caught:
`ZonesInPlayTests.A_third_team_created_in_setup_moves_default_zones_to_the_corners_and_leaves_chosen_zones`.

## Catch

- Build once, then loop the unit suite with `--no-build`, a TRX logger and a log per run, recording each run's
  summary line: `dotnet test tests/Tabletop.GameServer.Tests --no-build --logger "trx;LogFileName=runN.trx" --results-directory <dir>`.
- A failing run's TRX names the test and its message; every failure is kept.
- Stress the named test alone (`--filter`) in a loop, and with several loops at once to load the thread pool.

## Root cause

Read the server path the test drives and the assertion that failed; look for a read of shared state with no
happens-before edge to the write (a row read after a broadcast the write follows), static state, clocks, randomness
and order of unordered collections. A cause is accepted when it explains the failure message and a reproduction
makes it fail reliably.

## Findings

Three flaky tests.

1. `ZonesInPlayTests.A_third_team_created_in_setup_moves_default_zones_to_the_corners_and_leaves_chosen_zones`.
   `GameSession.FollowTeamCountAsync` broadcasts the corners' `BoardMessage`, then writes the draft row (the setup
   design's order: `docs/design/game-server/design.md` → "The broadcast happens *before* the write"). The test read
   `db.GameRows.DraftOf(...)` as soon as the board arrived, racing the session's mailbox to the write.
   Reproduction: a 200 ms delay before that `WriteDraftAsync` fails the old test every run (`Expected: 4, Actual: 2`).
2. `SocketCompressionTests.A_client_offering_deflate_gets_it_with_server_context_takeover_and_frames_round_trip`
   (caught in run 6 of 10 before any change: `Failed to bind to address http://127.0.0.1:5200: address already in use`).
   `WebApplicationFactory.UseKestrel(0)` writes the server's address (`TryConfigureServerPort`) while `Program.Main`'s
   `app.Run()` is already starting Kestrel on its own thread. When Kestrel binds first it takes `appsettings.json`'s
   `Kestrel:Endpoints:Http:Url`, 127.0.0.1:5200, which the compose `game` container holds ("Overriding address(es)
   'http://127.0.0.1:0'. Binding to endpoints defined via IConfiguration"), or the factory's `Addresses.Clear()` throws
   "cannot be modified after the server has started". Reproduction: a 100-case probe starting `UseKestrel(0)` factories
   inside the full suite failed 1, 0, 1, 2 cases in four runs, with both messages.

3. `WatcherTests.A_watcher_leaving_ends_no_presence_and_alone_keeps_the_game_in_memory_until_it_goes`
   (3 of 25 full runs once 1 and 2 were fixed: `Assert.Single() Failure: The collection was empty` on `SessionRegistry.Live`).
   A server bug. `SessionRegistry.ReleaseIfEmptyAsync` counted `Clients` and `Watchers` from outside the session's
   mailbox, so a socket whose `ConnectAsync`/`WatchAsync` was queued but not yet run did not count: the last socket
   leaving released and drained the session, and the newcomer got Welcome … AtRest and then a 1001 "released" close;
   a join queued behind the drain's close-all turn landed on a dead session and stayed open with nothing behind it.
   Reproduction: `SessionRegistryTests` hold the mailbox with one turn, queue a join and the release in either order,
   and fail every run (closed `(EndpointUnavailable, "released")`; no `SessionDrainedException`).

## Fix

- `ZonesInPlayTests`: wait for the draft row (`Wait.UntilAsync`), as `SetupEditTests` does. The server order stays.
- `GameServerFactory` sets `Kestrel:Endpoints:Http:Url` to `http://127.0.0.1:0`; `SocketCompressionTests` calls
  `UseKestrel()` with no port, so the factory never writes the address and Kestrel binds the configured ephemeral
  loopback port alone.
- `GameSession.RetireIfEmptyAsync` decides emptiness in the mailbox and marks the session retired;
  `ConnectAsync`, `WatchAsync` and `AttachAsync` throw `SessionDrainedException` on a retired session, which the
  handshake already answers by hydrating again. `ReleaseIfEmptyAsync` releases only on that decision.
- No retries, no skips. `tests/AGENTS.md` carries the two test pitfalls; `docs/design/game-server/design.md` the release rule.

## Validation

- Reproductions above fail before the fix; after it the delayed-write `ZonesInPlayTests` passes, the probe
  (100 Kestrel starts per run inside the full suite) passed 10 of 10 runs, 1000 starts, 0 failures (the probe was not
  kept), and both `SessionRegistryTests` pass.
- Stress: `--filter` on `ZonesInPlayTests|SocketCompressionTests` (before fix 3): 3 loops in parallel, 200 runs, 0 failures.
- Stress: `--filter` on `ZonesInPlayTests|SocketCompressionTests|WatcherTests|SessionRegistryTests`, 3 loops in parallel,
  200 runs in all, 0 failures.
- Full GameServer unit suite: 50 consecutive runs, 0 failures.
- Whole unit suite (`dotnet test`) once, green.

Results (2026-11-07, with the compose stack up, so 5200 held):

| Run | Before | After |
| --- | --- | --- |
| Full GameServer suite, baseline | 10 runs, 1 failed (`SocketCompressionTests`) | |
| Full GameServer suite, fixes 1 and 2 only | 25 runs, 3 failed (`WatcherTests`) | |
| Stress, 4 classes, 3 parallel loops | | 200 runs × 14 tests, 0 failed |
| Full GameServer suite | | 50 runs × 1834 tests, 0 failed |
| Whole unit suite (`dotnet test`) | | 13 projects, 4239 tests, 0 failed |
