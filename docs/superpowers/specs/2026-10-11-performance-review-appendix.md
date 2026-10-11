# Performance review: measurements

Raw numbers behind [2026-10-11-performance-review.md](2026-10-11-performance-review.md), and how to reproduce them. All runs
on the WSL host (Ubuntu 26.04, .NET 10.0.400) against the compose Postgres on 127.0.0.1:5432, 2026-10-11, at `cd7b3c8`.
Nothing here is in the repo: the harness lived in a scratch directory and changed no source.

## The harness

A console project outside the repo referencing `sim/Tabletop.Sim.csproj` (and through it `server/game`), with
`client/src/Core/**/*.cs` compiled in as `tests/Tabletop.Client.Tests` does:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
  </PropertyGroup>
  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
  <ItemGroup>
    <ProjectReference Include="<repo>/sim/Tabletop.Sim.csproj" />
    <Compile Include="<repo>/client/src/Core/**/*.cs" LinkBase="ClientCore" />
  </ItemGroup>
</Project>
```

It plays one game through a copy of `sim/Drivers/InProcessDriver.cs` whose `SessionServices` wrap the engine factory and
the event log, so every stage of the real `GameSession` path runs (mailbox, `CommandDispatcher`, `AppendAsync`, broadcast,
`AdvanceAsync`, `ModelFacts`):

- `TimedEngine : IRulesEngine` forwards to the real `RulesEngine` and times each `Advance`, keyed by the top frame's
  sequence (or `scheduler`) and what the call emitted (`ask:<question>`, `step:<name>`, `push:<sequence>`, or the event kind).
- `TimedLog : IEventLog` forwards to `InMemoryEventLog` or `PostgresEventLog` and times each `AppendAsync`.
- `WireClient : ISessionClient` serialises every frame with `WireJson.Serialize`, as `SocketConnection` does, and counts bytes.
- `SubmitAsync` times `GameSession.ReceiveAsync` for each command: dispatch, appends, broadcasts and the whole advance loop.
- `RunLoop` with `RunSettings(CheckInvariants: false, Record: false)` and the scenario's own `AnswerPolicy` plays it.

The scenario:

```csharp
new ScenarioBuilder("perf", seed: 1)
    .Army("Red", 15, 5).Army("Blue", 15, 5)
    .Terrain("ruin", new Point3(36, 24, 0), volumes: [Solid 6×6, 0-3", BlocksLineOfSight | Cover])
    .Terrain("forest", new Point3(18, 30, 0), volumes: [Area circle r4, 0-2", Difficult | Cover])
    .Terrain("forest", new Point3(54, 18, 0), volumes: [Area circle r4, 0-2", Difficult | Cover])
    .Build();   // alternating activation, strict, 72×48, round cap 8; ends at round 4
```

After the game it times, on the final `GameState`: `EventApplier.EffectiveLog`, `FrameReader.Read`, `DiceCursor.DrawnSoFar`,
`SchedulerLog.Of`, `GameSession.Facts` (by reflection), a full replay, `ProjectTo`, `Replay.To`, `WireJson` over the log,
`PropertySet.AllEffects().FirstOrDefault(last id)`, and `GameSession.HandleSightAsync` (by reflection) between two 5-model
units. Then the client Core: `CombatLog.Build`, `CombatLog.Latest`, `UndoPlan.Of`, `RosterView.Of`, `HeaderView.Of`,
`UnitMarkers.Of`, `DecisionView.Of` on the final projection, and on `ProjectTo(round 1 start + 1)` (150 models on the table):
`RangeRings.Of` for one 5-model unit, `MoveRuler.Region` for its lead model with and without the 145 others,
`DragBodies.Models` for its five models moved 1" and 3".

A probe, run at every seventh scheduler `Advance` from round 1, timed `LegalTurn` per team and `GfMelee.ChargeTargets` per
offered unit, then the same expression with the distance test first, and compared the two lists.

```bash
cd <scratch>/perf
dotnet build -c Release
dotnet bin/Release/net10.0/perf.dll 15 5                    # in-memory log
PGPW=<password from .env> dotnet bin/Release/net10.0/perf.dll 15 5 --pg   # PostgresEventLog; deletes its rows after
NOPROBE=1 dotnet bin/Release/net10.0/perf.dll 15 5          # without the probe's extra work
NOPROBE=1 DOTNET_TieredCompilation=0 dotnet bin/Release/net10.0/perf.dll 15 5
dotnet build -c Debug && NOPROBE=1 DOTNET_MODIFIABLE_ASSEMBLIES=debug dotnet bin/Debug/net10.0/perf.dll 15 5
```

The `--pg` run appends under the scenario's game id to `game.game_event` (no foreign key) and deletes those rows at the end.

The fix in finding 2 was measured in a copy (`git archive HEAD | tar -x -C <scratch>/patched`) with `GfMelee.cs:126-127`
reordered, and the same harness pointed at the copy. The copy's rules suite:

```bash
cd <scratch>/patched && dotnet run -c Release --project sim -- suite
# PASSED: 193 of 193 games passed, 0 failing on known defects, 0 unexpected; 428 of 428 entries covered, 108 not automated
```

The Postgres append alternatives were timed by a second scratch project inserting 400-byte jsonb rows under a random game id:
a transaction with one `INSERT` per row (as `PostgresEventLog`), against one `NpgsqlBatch`, 60 repetitions each.

Management, with a dev session minted from `POST /v1/dev/sessions` for `sim-player-0`:

```bash
curl -s -o /dev/null -w "%{time_total}\n" -H "Authorization: Bearer $JWT" -H "Tabletop-Client-Version: 999" http://127.0.0.1:5100/v1/games
curl -s -o /dev/null -w "%{time_total}\n" -H "Authorization: Bearer $JWT" -H "Tabletop-Client-Version: 999" http://127.0.0.1:5100/v1/properties/$HASH/rules
```

## Results: the game

2,879 events, 368 commands, 2,197 `Advance` calls, 2,196 appends (1.31 events per batch, at most 2).

| Run | Whole game | Command mean | p50 | p90 | p99 | max |
| --- | --- | --- | --- | --- | --- | --- |
| Release, in memory | 13.8-14.0 s | 35-36 ms | 5.0-5.2 | 130 | 200-204 | 516-602 |
| Release, Postgres | 43.7 s | 116 ms | 37 | 322 | 629 | 1,734 |
| Release, tiering off, in memory | 22.0 s | 56.8 ms | 7.4 | 218 | 331 | 406 |
| Debug + modifiable assemblies, in memory | 180-183 s | 485-495 ms | 29-30 | 1,978-2,031 | 2,886-2,953 | 2,929-3,075 |
| Finding 2 applied, Release, in memory | 5.8-6.1 s | 12.9-14.4 ms | 7.5-8.6 | 29-37 | 106 | 147-149 |
| Finding 2 applied, Release, Postgres | 20.1 s | 51.8 ms | 34.6 | 96.6 | 330 | 413 |
| Finding 2 applied, tiering off, in memory | 4.6 s | 9.5 ms | 7.0 | 19.0 | 81.5 | 183 |

Per command kind, Release in memory (main / finding 2 applied):

| Command | n | Mean ms | Batches | Events |
| --- | --- | --- | --- | --- |
| `move` | 92 | 63-64 / 19-22 | 5.9 | 7.6 |
| `shoot_or_not` | 46 | 43-45 / 13-16 | 3.8 | 5.2 |
| `activate` | 120 | 13 / 8-9 | 5.6 | 7.7 |
| `declare_targets` | 27 | 50-51 / 32-33 | 17.1 | 20.6 |
| `remove_casualties` | 13 | 85 / 19 | 11.8 | 17.5 |
| `end_round` | 4 | 157 / 25 | 3.5 | 4.5 |
| `deploy` | 30 | 0.5 / 0.5 | 1.0 | 2.0 |

Per command kind, Postgres (main / finding 2 applied): `activate` 86 / 48 ms, `move` 160 / 60 ms, `declare_targets` – / 128 ms,
`remove_casualties` – / 87 ms, `deploy` – / 7.6 ms.

`Advance` by where it ran (Release, in memory, main, no probe; mean and max ms):

| Top frame -> emitted | n | Total | Mean | Max |
| --- | --- | --- | --- | --- |
| scheduler -> `turn_started` | 150 | 5.6-5.8 s | 37.6-39.0 | 345-493 |
| scheduler -> `ask:activate` | 120 | 5.4-5.5 s | 45.1-46.0 | 112-165 |
| shooting -> `ask:declare_targets` | 27 | 0.19 s | 7.2-7.4 | 42-44 |
| advance -> `ask:shoot_or_not` | 26 | 0.17 s | 6.7-6.9 | 47-48 |
| shooting -> `step:fire-groups` | 54 | 0.16 s | 3.0-3.1 | 32-36 |
| hold -> `ask:shoot_or_not` | 20 | 0.10 s | 4.9-5.2 | 11-13 |

The same two scheduler rows: 61.8 / 77.2 ms mean with tiering off; 4.2 / 4.6 ms with finding 2 and tiering off; 4.4-6.6 /
5.1-8.0 ms with finding 2 and tiering on; 576-587 / 711-725 ms under Debug. With tiering on, the shooting rows read 2x
higher in the patched runs than in main's (13-15 ms against 7 ms); with tiering off they are equal (7.3-9.0 ms against
7.6-7.8 ms), so that difference is JIT warm-up, not the change.

`Advance` mean by log length (main, Release, in memory): 9.6, 8.3, 4.4, 6.3, 5.7, 2.8 ms for 0-499 ... 2,500-2,999 events.
No growth with the log.

Probe (main, 176 samples): `LegalTurn` 46.95 ms mean, 520.1 max; `ChargeTargets` over the offered units 43.80 ms; with the
distance test first 1.88 ms; 7.6 units offered on average; every list equal.

## Results: Postgres

| Measure | ms |
| --- | --- |
| `PostgresEventLog.AppendAsync` in the game, per batch | 6.13 mean, 5.88 p50, 7.35 p90, 10.1 p99, 135 max; 13.5 s in total |
| `select 1` on a pooled connection | 0.86 |
| one transaction, one `INSERT` per row: 1 / 2 / 8 / 20 rows | 5.87 / 7.08 / 9.97 / 18.60 |
| one `NpgsqlBatch`: 1 / 2 / 8 / 20 rows | 5.08 / 5.02 / 5.52 / 6.00 |

`synchronous_commit` is `on`; the commit's flush on the Docker volume is most of the 5 ms floor.

## Results: whole-log work and the wire (final state, 2,879 events)

| Measure | Release ms | Debug ms |
| --- | --- | --- |
| `EventApplier.EffectiveLog` | 0.055 | 0.210 |
| `FrameReader.Read` (empty stack) | 0.041 | 0.117 |
| `DiceCursor.DrawnSoFar` | 0.039 | 0.071 |
| `SchedulerLog.Of` | 0.048 | 0.099 |
| `GameSession.Facts`, 150 models | 0.019 | 0.034 |
| full replay | 0.49 | 1.05 |
| `ProjectTo(seq - 10)` | 1.04 | 0.80 |
| `Replay.To` | 0.38 | 1.17 |
| `WireJson.Serialize` of every event | 17.5 (1,620 KiB) | 17.4 |
| `WireJson.Deserialize` of every event | 80.8 | 93.2 |
| `AllEffects().FirstOrDefault` (536 entries, 625 effects) | 0.125 | 0.217 |
| `SightQuery`, 5 × 5 models | 5.4 | 62.1 |

Frames sent over the game to two players and the recorder: 7,701 messages, 5.1 MiB; `EventsMessage` 6,582 (785 B mean),
`AtRest` 1,107, `BoardMessage` 3 (49 KiB each), `ModelFacts` 3 (6 KiB each). Serialising all of them: 0.4-0.6 s over the game.
By event kind: `ask_opened` 338 × 1,648 B, `step_completed` 960 × 477 B, `ask_answered` 338 × 661 B, `ask_closed` 338 × 649 B,
`sequence_started` 223 × 356 B, `models_moved` 30 × 1,074 B.

Hydration from Postgres of the finished game (three runs): `Hydrator.HydrateAsync` 102 / 103 / 92 ms, of which
`ReadAsync` 98 / 67 / 69 ms; `StartAsync` 0 ms (ended); `ConnectAsync` for a player 17 / 14 / 12 ms, 10 frames, 1,679 KiB.

## Results: client Core

| Measure | Release ms | Debug ms |
| --- | --- | --- |
| `CombatLog.Build` (1,050 top-level entries, 2,995 in all) | 9.12 | 9.48 |
| `CombatLog.Latest` | 8.95 | 8.38 |
| `UndoPlan.Of` | 9.02 | 8.12 |
| `RosterView.Of` | 0.49 | 0.46 |
| `UnitMarkers.Of` | 0.17 | 0.23 |
| `HeaderView.Of`, `DecisionView.Of`, `ShotLines.Of` | < 0.01 | < 0.01 |
| `RangeRings.Of`, one 5-model unit | 24.1 | 113.4 |
| `MoveRuler.Region` 6", 145 other models | 6.28 | 40.3 |
| `MoveRuler.Region` 12", 145 other models | 4.63 | 60.5 |
| `MoveRuler.Region` 6", no other models | 0.44 | 5.86 |
| `DragBodies.Models`, five models, 1" / 3" per call | 0.69 / 1.66 | 1.95 / 5.17 |
| `DragBodies.Stationary` | 0.045 | 0.064 |

## Results: management

`GET /v1/games` 321 ms on the first request after a while idle, then 11-13 ms (5.6 KB). `GET /v1/properties/{hash}/rules`
23 ms first, then 4.5-4.7 ms (8 KB). `POST /v1/dev/sessions` 1.9 s (a Stytch round trip; development only).

## Not measured

- Anything inside Godot: Control and Node3D creation and freeing, layout, mesh and material allocation, GLB parse time, frame
  times. Findings 4, 5, 6 and 10 reason from the code about those costs.
- The Windows host's CPU, where the client runs; the numbers above are the WSL host's.
- Network latency beyond loopback, and the socket driver end to end (`--driver socket` adds the real WebSocket and the
  Debug container; not timed per command here).
- Postgres on storage other than the Docker Desktop volume; the 5 ms commit floor is that volume's.
- A game with live grants (finding 11 is computed from the per-lookup cost).
