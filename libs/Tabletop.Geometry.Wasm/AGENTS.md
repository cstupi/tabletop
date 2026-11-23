# libs/Tabletop.Geometry.Wasm

`Tabletop.Geometry` for the browser: `Microsoft.NET.Sdk.WebAssembly`, `net10.0`, `browser-wasm`; compiled ahead of time with the `wasm-tools` workload (`scripts/geometry-wasm`), else run on the runtime's interpreter. References `Tabletop.Geometry` only. The viewer calls it through `viewer/src/lib/geometry/`.

## Layout

- `Requests.cs`: the request and answer records, the wire's own shapes (`Piece` reads a wire `TerrainPlacement`; its `mesh` is ignored).
- `FacadeJson.cs`: `FacadeJsonContext` (System.Text.Json source generation, trim-safe) and `FacadeJson.Options` (camelCase, nulls omitted, enums as snake_case names, integers refused, as `ContentJson`).
- `FacadeReaders.cs`: hand-written converters for `Point2`, `Point3`, `Yaw`, `Base`, `ModelBody`, `Placement`, `PlacedModel` and `ContactBody`, the values a request carries per model. The generated metadata reads a record through its constructor-parameter path, which costs the browser several times more. They read and write what the generated contract does; `FacadeReadersTests` checks both against `FacadeJson.Build(readers: false)`.
- `Facade.cs`: one `string -> string` static method per call, JSON in and JSON out.
- `MoveAsks.cs`: `MoveAsk`, one move Ask's reaches kept between calls (the Godot client's `MoveRegions`), and `MoveAsks`, the kept Asks by key (at most four, the oldest dropped).
- `Exports.cs`: one `[JSExport]` per `Facade` method, same name, body `=> Facade.X(json)`.
- `Program.cs`: `return;`; the runtime needs an entry assembly, the browser never runs `Main`.

## How it works

- Held pieces are always lifted: `LandModels` and `LandPiece` are `DragBodies.Models` and `DragBodies.Piece` with `free` false and no level, `within` null; `LandRequest.Abut` is a charge's `Abutment` (the viewer's `abutmentOf`). A landing past a move's allowance is the server's to refuse. `MoveRoutes` is each held model's `Movement.Measured` path over the Ask's volumes (with `Capped`, the shortest within the cap) with its `SweptPath` (footprints, and the volumes dangerous to the path its base crosses): the path the
  Godot client's `MoveReach.Route` draws. `CoherencyGaps` is `Coherency.Gaps`. `MoveBands` is `MoveReach.Bands` composed as the Godot client's `MoveRegions` composes it (steep volumes the model starts outside made difficult, a `MinDistance` read as `MoveRuler.Minimum`). `MoveAskOpen` keeps a move Ask's reaches under a key (`Prepare`: every reach and its route's fields built at once), `MoveAskArea` is a kept model's `MoveReach.Bands` as `MoveBands` composes it, whole or one share of its coarse cells, as flat coordinates, `MoveAskFrame` one drag frame (each moved model's `MoveReach.Route`, the shortest once a route goes in difficult ground and the unit is held to the cap, its `SweptPath`, the unit's allowance, and `Contacts.Of` with `RoundModels` from the kept reaches, as Godot's `ContactCheck.Move`), `MoveAskClose` drops it; the viewer's `MoveWorkers` calls them in Web Workers. `Slide`, `Follow`, `Separate`, `MoveReach`'s clamp, `Movement.Region` and line of sight are not exported.
- A JSON that does not read throws `JsonException`, which reaches JavaScript as an `Error` with its message.
- `scripts/geometry-wasm` publishes the project and copies `_framework/` to `viewer/public/geometry/_framework/` (git-ignored); publish writes `.br` and `.gz` beside each file. With the `wasm-tools` workload it publishes with `RunAOTCompilation` in its own build slot (`TabletopBuildSlot` `-aot`): an AOT publish over the interpreter's `obj/` gives a runtime that does not load. `web/Dockerfile` installs the workload and `python3`.
- A five-rider 16" Rush (60 × 35 mm ovals, a dev game's table) on the interpreter: one rider's whole area 2–11 s in one call (`MoveBands`), each drag frame's one-off routes and contacts 300–500 ms; kept (`MoveAsk`), a frame 50–140 ms and an area split across four workers about 1.5 s.
- Sizes and speed, with 150 models on the table: ahead of time, `_framework` is 2.4 MB brotli (10.9 MB raw), `Contacts` 2.2-2.7 ms and `LandModels` 1.8 ms per call; on the interpreter, 1.7 MB brotli (5.3 MB raw), 8-9 ms and 6.5-7 ms. Reading the request JSON is most of either.

## Commands

```bash
dotnet test tests/Tabletop.Geometry.Wasm.Tests                                  # the facade in-process, and the agreement cases
TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Geometry.Wasm.Tests        # rewrite viewer/tests/fixtures/geometry.cases.json
./scripts/geometry-wasm                                                         # publish into viewer/public/geometry/
```

## Conventions and pitfalls

- The WSL SDK at `/usr/lib/dotnet` takes no workload without root; an SDK put in a user directory by `dotnet-install.sh` does (`dotnet workload install wasm-tools`).
- A new facade method needs its `[JSExport]`, a `[JsonSerializable]` entry for every new request and answer type, an agreement case in `tests/Tabletop.Geometry.Wasm.Tests/AgreementCases.cs` and a method on the viewer's `Geometry`. A new enum needs a `JsonStringEnumConverter<T>` in `FacadeJson`.
- Nothing here names a rule; Ask parameters arrive in the request.
