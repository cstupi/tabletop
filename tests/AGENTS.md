# tests

xUnit projects, one per library, per server and for the client's Godot-free core, plus the sim, the army-book generator and the viewer's contract. `dotnet sln list` shows the set.

## Layout

- `Tabletop.Geometry.Tests`, `Tabletop.Content.Tests`, `Tabletop.Protocol.Tests`: one per lib, headless. `ReferenceAgreementTests` checks the reach
  area, contacts and landings on the reference game's table (`Fixtures/reference-move.json`) bit for bit against `Fixtures/reference-move.expected.json`,
  which `TABLETOP_WRITE_FIXTURES=1` rewrites; a change that is meant to move those answers rewrites it.
- `Tabletop.GameServer.Tests`: the game server and its rules engine; the largest project.
- `Tabletop.Management.Tests`: the management API over in-memory repositories and blob store; the SQL repositories, `S3BlobStore` and migrations are E2E.
- `Tabletop.Client.Tests`: compiles `client/src/Core/**/*.cs` from source, so a `using Godot` there breaks this project. `CombatLogViewerFixtureTests` writes `viewer/tests/fixtures/combatlog.json` (`TABLETOP_WRITE_FIXTURES=1`, `--filter CombatLogViewerFixture`) and `CallsViewerFixtureTests` writes `viewer/tests/fixtures/calls.json` (`--filter CallsViewerFixture`), what Godot's Core builders send for each choice over the recorded logs; both fail when stale.
- `Tabletop.Sim.Tests`: `sim/`; references `Tabletop.GameServer.Tests` for `GameServerFactory`, `InMemorySeeder` and `PostgresFixture`.
- `Tabletop.ArmyBooks.Tests`: the generator, against `Fixtures/`.
- `Tabletop.Viewer.Tests`: generates and checks the viewer's TypeScript types and fixtures, and the call logs `viewer/tests/fixtures/calls/` (`CallLogFixtureTests`, `--filter CallLogFixture`).
- `Tabletop.Geometry.Wasm.Tests`: the browser facade in-process, and the agreement cases (`TABLETOP_WRITE_FIXTURES=1` writes `viewer/tests/fixtures/geometry.cases.json`, which the viewer replays through the WebAssembly).

## Commands

```bash
dotnet test                                          # the unit suite; nothing running
dotnet test tests/Tabletop.Client.Tests
dotnet test --settings tests/e2e.runsettings         # the E2E suite; needs docker compose up -d
```

- Two suites (spec `docs/superpowers/specs/2026-10-16-test-suites-design.md`). The unit suite is every test without
  `[Trait("Category", "E2E")]` and reaches no live service: servers run over the in-memory fakes in each test project's
  `Infrastructure/`. E2E tests are the Postgres and S3 adapters, migrations, suite games over the socket driver, and the lifecycle
  shapes (`LifecycleTests`, against the compose stack's management and game server).
- Game server and sim: `InMemorySeeder` (`Tabletop.GameServer.Tests/Infrastructure/`) is one test's database: `InMemoryGameRows`,
  `InMemoryEventLog` and `InMemoryPropertiesStore`, seeded as `PostgresFixture` seeds Postgres (both are `IRowSeeder`).
  `new GameServerFactory(db)` serves that seeder's stores; `GameServerFactory.OverPostgres(pg.Db)` is the E2E suite's. The sim's
  socket runs write through `InMemorySocketRows` (`Tabletop.Sim.Tests/Infrastructure/`). A unit test news its own seeder, so
  no collection serializes it.
- E2E in these two projects: the Postgres adapters (`PostgresGameRowsTests`, `PostgresEventLogTests`, `PropertiesStoreTests`,
  `PostgresSocketRowsTests`), the suite games over the socket against Postgres, the suite games in process (too slow for the unit
  suite; `dotnet run --project sim -- suite` plays them all), and `LifecycleTests` (each lifecycle shape against the running stack,
  as `dotnet run --project sim -- lifecycle` plays them). `GameRowsContract`, `EventLogContract` and `SocketRowsContract` run
  against both the fake and the adapter.
- `tests/default.runsettings` (from `Directory.Build.props`) excludes E2E; `tests/e2e.runsettings` selects only E2E. A
  command-line `--filter` narrows within the chosen suite.
- Never `docker compose down -v`: the Postgres volume holds the schema the server tests read.

## Regenerating checked-in outputs

```bash
TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests   # viewer/src/lib/protocol/{recording,wire}.generated.ts
TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests    # viewer/tests/fixtures/
TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Management.Tests --filter BuiltinTables   # client/content/builtin/tables/*.json, after a catalog change or a terrain declaration change (a hill's Steep)
```

- A plain `dotnet test` fails when the generated types are stale.
- 3.6 fixtures: `Tabletop.ArmyBooks.Tests/Fixtures/` holds a few books, `rules/` and `index.json` (`ArmyRules36Tests` reads the 3.6 dump and skips without it); a 3.6.0 game-rules test runs `Sim.Gf36(state, dice…)` under `TestProperties.Core36`; `Core36TextTests` pins 3.6 core texts to the skill's `v3.6.0/pages/`. The viewer's `combatlog.json` carries each army snapshot's `edition`, and `recording.generated.ts` the recording's `ruleSet`; both regenerate when a snapshot or the recording meta changes.
- Fixtures are one recording plus the welcome, board and events frames per scenario. Ids differ between runs, so they are regenerated deliberately and never diffed against a previous version.
- The call logs are regenerated before `calls.json`: `--filter CallLogFixture` in `Tabletop.Viewer.Tests`, then `--filter CallsViewerFixture` in `Tabletop.Client.Tests`.
- After regenerating, run `cd viewer && npm test`.

## Conventions and pitfalls

- Test-driven for everything with logic. The client's Godot layer is tested by running it.
- Tests never call Army Forge.
- A test socket opens only once its first frame is serialized (`TestClient` opens at the first send; `SocketDriver` builds
  its connect frame first): the server's handshake timeout (2 s in `GameServerFactory`) runs from the open, and a cold
  `WireJson` or another socket's connect in between overruns it when every project runs at once.
- Wait for a condition, never a fixed delay, for work the code under test puts on the thread pool (the client's reconnect:
  `FakeSocketFactory.ConnectedAsync`).
- The web apps' tests are Vitest in `web/` and `viewer/`, not here.
