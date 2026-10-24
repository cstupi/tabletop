# libs/Tabletop.Geometry.Wasm

`Tabletop.Geometry` for the browser: `Microsoft.NET.Sdk.WebAssembly`, `net10.0`, `browser-wasm`, no workload. References `Tabletop.Geometry` only. The viewer calls it through `viewer/src/lib/geometry/`.

## Layout

- `Requests.cs`: the request and answer records, the wire's own shapes (`Piece` reads a wire `TerrainPlacement`; its `mesh` is ignored).
- `FacadeJson.cs`: `FacadeJsonContext` (System.Text.Json source generation, trim-safe) and `FacadeJson.Options` (camelCase, nulls omitted, enums as snake_case names, integers refused, as `ContentJson`).
- `Facade.cs`: one `string -> string` static method per call, JSON in and JSON out.
- `Exports.cs`: one `[JSExport]` per `Facade` method, same name, body `=> Facade.X(json)`.
- `Program.cs`: `return;`; the runtime needs an entry assembly, the browser never runs `Main`.

## How it works

- Held pieces are always lifted: `LandModels` and `LandPiece` are `DragBodies.Models` and `DragBodies.Piece` with `free` false and no level, `within` null. A landing past a move's allowance is the server's to refuse. `Slide`, `Follow`, `Separate`, `MoveReach`, `Movement.Region` and line of sight are not exported.
- A JSON that does not read throws `JsonException`, which reaches JavaScript as an `Error` with its message.
- `scripts/geometry-wasm` publishes the project and copies `_framework/` to `viewer/public/geometry/_framework/` (git-ignored); publish writes `.br` and `.gz` beside each file.

## Commands

```bash
dotnet test tests/Tabletop.Geometry.Wasm.Tests                                  # the facade in-process, and the agreement cases
TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Geometry.Wasm.Tests        # rewrite viewer/tests/fixtures/geometry.cases.json
./scripts/geometry-wasm                                                         # publish into viewer/public/geometry/
```

## Conventions and pitfalls

- A new facade method needs its `[JSExport]`, a `[JsonSerializable]` entry for every new request and answer type, an agreement case in `tests/Tabletop.Geometry.Wasm.Tests/AgreementCases.cs` and a method on the viewer's `Geometry`. A new enum needs a `JsonStringEnumConverter<T>` in `FacadeJson`.
- Nothing here names a rule; Ask parameters arrive in the request.
