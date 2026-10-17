# tests

xUnit projects, one per library, per server and for the client's Godot-free core, plus the sim, the army-book generator and the viewer's contract. `dotnet sln list` shows the set.

## Layout

- `Tabletop.Geometry.Tests`, `Tabletop.Content.Tests`, `Tabletop.Protocol.Tests`: one per lib, headless.
- `Tabletop.GameServer.Tests`: the game server and its rules engine; the largest project.
- `Tabletop.Management.Tests`: the management API over in-memory repositories and blob store; the SQL repositories, `S3BlobStore` and migrations are E2E.
- `Tabletop.Client.Tests`: compiles `client/src/Core/**/*.cs` from source, so a `using Godot` there breaks this project.
- `Tabletop.Sim.Tests`: `sim/`; references `Tabletop.GameServer.Tests` for its Postgres fixture.
- `Tabletop.ArmyBooks.Tests`: the generator, against `Fixtures/`.
- `Tabletop.Viewer.Tests`: generates and checks the viewer's TypeScript types and fixtures.

## Commands

```bash
dotnet test                                          # the unit suite; nothing running
dotnet test tests/Tabletop.Client.Tests
dotnet test --settings tests/e2e.runsettings         # the E2E suite; needs docker compose up -d
```

- Two suites (spec `docs/superpowers/specs/2026-10-16-test-suites-design.md`). The unit suite is every test without
  `[Trait("Category", "E2E")]` and reaches no live service: servers run over the in-memory fakes in each test project's
  `Infrastructure/`. E2E tests are the Postgres and S3 adapters, migrations and suite games over the socket driver.
- `tests/default.runsettings` (from `Directory.Build.props`) excludes E2E; `tests/e2e.runsettings` selects only E2E. A
  command-line `--filter` narrows within the chosen suite.
- Never `docker compose down -v`: the Postgres volume holds the schema the server tests read.

## Regenerating checked-in outputs

```bash
TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests   # viewer/src/lib/protocol/{recording,wire}.generated.ts
TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests    # viewer/tests/fixtures/
```

- A plain `dotnet test` fails when the generated types are stale.
- Fixtures are one recording plus the welcome, board and events frames per scenario. Ids differ between runs, so they are regenerated deliberately and never diffed against a previous version.
- After regenerating, run `cd viewer && npm test`.

## Conventions and pitfalls

- Test-driven for everything with logic. The client's Godot layer is tested by running it.
- Tests never call Army Forge.
- The web apps' tests are Vitest in `web/` and `viewer/`, not here.
