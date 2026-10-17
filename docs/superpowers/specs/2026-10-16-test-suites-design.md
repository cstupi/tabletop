# Test suites: unit and E2E

Two suites, chosen by run settings.

| Suite | Command | Needs | Holds |
| --- | --- | --- | --- |
| Unit | `dotnet test` | nothing running: no Docker, Postgres, MinIO, Stytch or compose servers | every test of logic, endpoints and sessions, over in-memory fakes |
| E2E | `dotnet test --settings tests/e2e.runsettings` | `docker compose up -d` | `[Trait("Category", "E2E")]` tests: the Postgres and S3 adapters themselves, migrations, suite games over the socket driver (against Postgres) and in process (whole games, too slow for the unit suite) |

`Directory.Build.props` points every project at `tests/default.runsettings` (`Category!=E2E`); `tests/e2e.runsettings`
(`Category=E2E`) replaces it. A `--filter` on the command line is ANDed with the settings file's filter, so it narrows a
suite and never crosses into the other. `dotnet run --project sim -- suite --driver socket` stays the full rules
regression against the stack.

The unit suite finishes in about a minute. A unit test that times out on a connection is a defect: it is reaching a live
service.

## Seams

Every live dependency sits behind an interface registered in the server's `Program.cs`; a test factory swaps in an
in-memory fake from the test project's `Infrastructure/`.

- **Game server:** `IGameRows`, `IEventLog`, `IPropertiesStore`. `InMemoryGameRows`, `InMemoryEventLog` and
  `InMemoryPropertiesStore` hold the rows a test seeds; `GameServerFactory` registers them by default. The seeder that
  `PostgresFixture` is today writes the same rows into the in-memory stores, so a test seeds a game, account, team and
  properties without SQL. `PostgresEventLogTests`, `GameRowsReadTests` and `PropertiesStoreTests` test the Postgres
  adapters and are E2E.
- **Management:** each repository (`AccountRepository`, `ArmyRepository`, `AssetRepository`, `FolderRepository`,
  `LayoutRepository`, `GameRepository`, and the reads behind rule text, the manifest and starter assets) gets an
  interface the endpoints depend on, with the SQL class as its Postgres implementation and an in-memory fake in the tests.
  `IBlobStore` already has its seam. `ManagementFactory` registers the fakes. The SQL classes' own tests and
  `MigrationTests` are E2E.
- **Sim:** the socket driver runs against `GameServerFactory`, so it inherits the game server's fakes. A socket run's own rows
  (management's rows for a scenario, the spectator, the run lock and sweep) sit behind `ISocketRows`: `SocketRowSeeder` over Postgres,
  `InMemorySocketRows` over the in-memory seeder in the tests. `TestPool` seeds through the in-memory seeder.

A fake behaves as the adapter's contract says, not as a stub: a game's status guards, `EndGameAsync`'s `playing → ended`
guard, the event log's sequence check. Where an adapter's contract is subtle, one contract test runs against both the
fake (unit) and the Postgres adapter (E2E).
