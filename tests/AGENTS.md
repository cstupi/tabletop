# tests

xUnit projects, one per library, per server and for the client's Godot-free core, plus the sim, the army-book generator and the viewer's contract. `dotnet sln list` shows the set.

## Layout

- `Tabletop.Geometry.Tests`, `Tabletop.Content.Tests`, `Tabletop.Protocol.Tests`: one per lib, headless.
- `Tabletop.GameServer.Tests`: the game server and its rules engine; the largest project.
- `Tabletop.Management.Tests`: the management API.
- `Tabletop.Client.Tests`: compiles `client/src/Core/**/*.cs` from source, so a `using Godot` there breaks this project.
- `Tabletop.Sim.Tests`: `sim/`; references `Tabletop.GameServer.Tests` for its Postgres fixture.
- `Tabletop.ArmyBooks.Tests`: the generator, against `Fixtures/`.
- `Tabletop.Viewer.Tests`: generates and checks the viewer's TypeScript types and fixtures.

## Commands

```bash
dotnet test                              # everything; needs docker compose up -d
dotnet test tests/Tabletop.Geometry.Tests
dotnet test tests/Tabletop.Client.Tests
```

- Projects needing the Docker stack: `GameServer.Tests` and `Management.Tests` (Postgres, MinIO), plus the socket-driver tests in `Sim.Tests`.
- Tests in the `[Collection("Postgres")]` collection need the Docker Postgres up.
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
