# Performance and network play

Scope: the Release play profile of the game server, the client's Core and the browser viewer (finding 1 of
[2026-10-11-performance-review.md](2026-10-11-performance-review.md)), and what crosses the wire for remote players at
`https://tabletop.cstupi.com`, and the Godot client's frames (rendering, the Hud, the move Ask, loading); the last has its own section and
reference game. Rules behaviour does not change.

Reference game, as the earlier review: 15 units of 5 a side, a ruin and two forests, seed 1, alternating activation,
strict, played to the round-4 end by `sim`'s `AnswerPolicy`: 3,096 events, 370 commands, 2,394 `Advance` calls, one
Postgres append per command. Release builds, tiered JIT on, WSL host. Harnesses live in the git-ignored `.superpowers/perf/`
(`harness/run` for the server, client Core and wire; `viewer/run.sh hot|sizes|load` for the viewer).

## Server and client Core

| Measure | Value |
| --- | --- |
| Whole game, in memory, cold / warm process | 5.1-5.6 s / 2.0-2.2 s |
| Command mean, p90, max (warm) | 4.8-5.1 ms, 14-15 ms, 20-27 ms |
| Scheduler `turn_started` + `ask:activate` per activation (warm) | 7-8 ms |
| `HandleSightAsync`, two 5-model units | 0.3-0.6 ms |
| `CombatLog.Build` at 3,096 events, once per log state | 2.5-4 ms |
| One drag frame of five models (`ContactCheck.Move`) | 0.35-0.4 ms |
| `RangeRings.Of`, one 5-model unit, on a key change | 0.16-0.28 ms |
| Reconnect, server side / client deserialise | 13 ms / 48-66 ms |

The server is no longer what a player waits for: a command answers in single milliseconds warm, and interleaved runs
of this batch against the previous commit agree within the machine's noise. The first game after a process start runs
about 2.5x slower (JIT warm-up) and is left as is.

### The move reach

Picking up a unit in a move Ask drew each held model's area (`MoveRegions.Outline` → `MoveReach.Triangles`) on the main
thread. Most of the time went into other models as obstacles: each sample rested the moving base on the surfaces and ran
`MoveShape.Interval` (a golden-section search and two bisections), about 44k times per pickup. Two exact shortcuts in
`libs/Tabletop.Geometry`: a body whose height range meets the base at every height the base can rest at near it
(`MoveSpace.MeetsAtEveryRest`) is decided without resting, and a segment clearly inside a shape by more than the search's
error (`MoveShape.SurelyEnters`) skips the search. Bounding-circle culls (`Bodies.Reach`, `Bodies.Apart`) skip far models in
`MoveSpace`, `Overlap.Bases`, `Collision.Hover` and `Contacts`. `ReferenceAgreementTests` holds 110 areas, 300 contact
cases and 120 landings from the reference game to the answers recorded before the change, bit for bit.

| Godot client, Release | Before | After |
| --- | --- | --- |
| Pickup of a 5-model unit (five outlines) | 117-126 ms | 5.4-5.5 ms |
| The move Ask opens (`MoveRegions.Of` + the lead's outline) | 17-18 ms | 0.85-1.3 ms |
| One drag frame (`ContactCheck.Move`) | 0.27-0.32 ms | 0.17-0.19 ms |

## The wire

Bytes are measured at a TCP proxy in front of a Release game server (frame headers and the handshake included), one
socket, the reference game played by the sim's socket driver.

| Per socket | Without extension | permessage-deflate |
| --- | --- | --- |
| Server to client, whole game | 1,949.6 KiB | 176.0 KiB (11.1x) |
| Connect or reconnect at the end (the whole log) | 1,863.5 KiB | 143.7 KiB (13.0x) |
| Client to server, one player, whole game | 115-128 KiB | 23-26 KiB |

What crosses per player action: one `CommandMessage` (242-284 B, a move commit 1.7 KB), then the batches it produced
broadcast to every socket (a `move` 7.8 frames, 11 KB uncompressed; `declare_targets` 19.3 frames, 8.4 KB) and one
`AtRest`. The only request and answer is sight: a `SightQuery` (181 B) gets a `SightAnswer` (4.3-4.7 KB). Model facts are
pushed on change; rule texts and grants are one HTTP GET each per properties hash. Presence goes out at most 20 Hz and
only on change: nothing idle, 283 B hovering, 1.2 KB dragging five models.

### Decisions

- **permessage-deflate on `/ws`.** The server accepts with `DangerousEnableCompression` (context takeover, 15-bit
  window, about 300 KB of zlib state per compressing socket). Godot's `ClientGameSocket` and the sim's connector offer it
  (`SocketCompression` in `Tabletop.Protocol`); browsers always do. The frame caps count decompressed bytes.
- **The JWT stays out of a client's deflate window.** `Connect` and `Reauth` go out with
  `WebSocketMessageFlags.DisableCompression` from Godot and the sim. A browser cannot set the flag and compresses its
  `Connect`; nothing another party writes is echoed in a client's outbound frames, so no guess can be grown beside the
  token. Server-to-client frames carry no secret.
- **Management compresses JSON** (Brotli, gzip; `problem+json` included) with `EnableForHttps`, since forwarded
  headers make every request behind Caddy https. `/v1/auth` and `/v1/dev`, the only answers carrying session tokens, are
  never compressed. Measured: `/v1/properties/{hash}/rules` 108 KB to 12.5 KB, `/v1/layouts/builtin` 34 KB.
- **Content-hash routes are cached.** `/v1/properties/{hash}/rules` and `/grants` answer 200 with
  `Cache-Control: private, max-age=31536000, immutable` (the hash is the document's SHA-256, inserted once, the same for
  every account); a 404 carries none. Godot's `HttpClient` (`ClientHttp`) and the sim decompress automatically.
- **The hovered unit's sight query is asked once per placement.** `SightSlot` (`client/src/Core/SightKey.cs`) re-sends it
  only when the pair, a model placement or the terrain changed, as assigned targets already did.
- **The web image's Caddy** is `caddy:2.11-alpine` (2.10 answered a plain request for a precompressed file with 206, so
  browsers fetched the 1.56 MB geometry again on every visit), and every hashed `/viewer/geometry/_framework/*` file is
  `public, max-age=31536000, immutable`; `dotnet.js`, the one unhashed name, stays `no-cache`.

### Measured and left alone

- **The whole log on every connect.** `Connect` carries no position and `SendStateAsync` sends from seq 0. Compressed it
  is 144 KiB at the end of a 3,096-event game; a resume from a client-held position would also have to trust a prefix
  that was filtered for that viewer's sealed answers.
- **One `EventsMessage` per batch** (6.5 per command, 2,017 extra frames per game): 58 KB of envelopes per game before
  compression; both clients already render once per frame.
- **Verbosity.** Property names are 35 % of raw bytes, GUIDs 25 %, `kind` discriminators 11 %, default-valued fields 7 %;
  `ask_opened` is 39 % of the log, and each answer travels in `ask_answered`, `ask_closed` and `step_completed`. The
  deflate context takes most of this repetition.
- **Presence while dragging**: 23 KiB per 10 s compressed.
- **One serialisation per socket**: 58-85 ms per game across four sockets.

## The browser viewer

Measured in Node through the viewer's real modules and loader against the real wasm Geometry (no DOM; canvas and Svelte
work not counted), on a recording of the reference game.

| Viewer | Before | After |
| --- | --- | --- |
| Pointer move dragging a 4-model ghost (interpreter) | 51 ms: 1 `LandModels`, 4 `Contacts`, 2 `ZonePolygons` | 19.9 ms: 1 `LandModels`, 1 `Contacts`, at most once per animation frame |
| The same on the web image's AOT build (per call: `Contacts` 2.2-2.7 ms, `LandModels` 1.8 ms) | | about 5 ms (from the per-call numbers) |
| Whole game's batches (3,096) | 6.0 s, mean 1.94 ms | 5.3 s, mean 1.70 ms |
| Scene rebuilds over the game | 998 ms, 6,192 `ZonePolygons` | 131 ms, none |
| Hover with a unit selected (`hoverOf`) | 2-7 ms, twice per change and every batch | 0.6-1.5 ms once (one `NearestPair`), 0.01 ms when placements are unchanged |
| Entering a game: Stytch refreshes in series | 4 (60-90 ms each) | 0 while the stored JWT has 60 s left |

### Decisions

- **One gate per landing.** `DecisionBox.draggedAll` moves every ghost, then regates once (`Contacts` once, not per held
  model); `PlayDrags` lands only the latest pointer move per animation frame. A drop or Esc discards the waiting move;
  Q/E and 1-5 land it first. The ghosts, gate and marks end the same.
- **The session JWT is reused** until 60 s before its `exp`; one refresh is shared by concurrent callers, and a token the
  website refreshed in another tab is read from storage.
- **The Watch tab's picker** mounts the first time the tab shows, and only for accounts that have the tab.
- **Hover distance** is one `NearestPair` (the first least `EdgeToEdge`, as Godot's `HoverView` picks), cached per pair and
  placements; zone polygons are cached per shape and table size.
- **Geometry in the browser is compiled ahead of time** in the web image (`wasm-tools` workload; `scripts/geometry-wasm`
  uses AOT when the workload is present, the interpreter otherwise). Hand-written JSON readers for the per-model values
  (`FacadeReaders`) cut request parsing, which dominated each call: reading a 39 KB `Contacts` request 7.0 ms → 1.3 ms.
  The download is 2.41 MB brotli against 1.67 MB, cached immutable after the first visit.

| Wasm per call (150 models) | Interpreter before | Interpreter after | AOT after |
| --- | --- | --- | --- |
| `Contacts` (5 proposed) | 10.5-11.8 ms | 8.3-9.1 ms | 2.2-2.7 ms |
| `LandModels` (5 held) | 8.2 ms | 6.6 ms | 1.8 ms |
| `EdgeToEdge` | 0.52 ms | 0.40 ms | 0.09 ms |

### Measured and left alone

- **The combat log** is built from the whole log once per log state: 1-2 ms in the viewer, 2.5-4 ms in Godot at 3,000
  events. It reads effective-log liveness, undos and roll amendments, so an incremental build is not cheap to keep equal.
- **A move Ask's first render** in the viewer calls `Contacts` once (the worst batch, 77-84 ms on the interpreter, about
  a quarter of that on AOT).
- **Fonts** in the web app are `.ttf` without hashed names, so they cannot be cached immutable (169 KB on a first visit).

## Page loads

| Page, cold | Requests | Bytes |
| --- | --- | --- |
| web `/armies` | 25 | 230 KB (fonts 169 KB) |
| web `/library` | 32 | 571 KB (three.js 228 KB) |
| viewer `#/games` | 10 | 317 KB |
| viewer `#/game/<id>` | 30 | 2,176 KB (geometry 1.56 MB) |

A warm reload is 2-10 KB for the website and the games list. Entering a game again fetches nothing from the geometry or
the rule texts: both are immutable by hash.

## The Godot client

Target: 60 fps with no visible hitch through a whole game at 1280×720 and 1920×1080 on an integrated GPU, the graphics settings choosing the
lower-cost options there.

Reference game: 15 units of 5 a side on the Ruins predefined table (19 pieces), every model the owner's printed orc warrior (a 1.8 MB GLB,
57,000 triangles in four surfaces), played to the round cap of 4 by the sim's policy over the socket against Release servers, one command every
400 ms. The client is a release export (`--export-release`, the configuration players get) on the RX 6900 XT of the development machine:
Windows with Vulkan, the default driver there, and Linux in WSL with the Compatibility renderer (OpenGL translated to D3D12). Frames come from
`PerfProbe` (below); the harness (`drive`, `measure.sh`, `bench.sh`, `interactive.sh`) is in `.superpowers/perf/`.

### Whole game, watched

Before → after, the game screen only, VSync off.

| Run | fps | frame p50 ms | p99 ms | frames > 33 ms | > 50 ms | max ms | draw calls | primitives (M) | video MB |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Vulkan Low 1280×720 | 336 → 1049 | 2.68 → 0.85 | 11.2 → 2.8 | 106 → 0 | 3 → 0 | 55 → 23 | 328 → 214 | 17.0 → 2.0 | 170 → 153 |
| Vulkan Low 1920×1080 | 310 → 858 | 2.94 → 1.07 | 12.1 → 3.1 | 123 → 1 | 2 → 0 | 75 → 34 | 363 → 246 | 17.0 → 3.2 | 216 → 193 |
| Vulkan Medium 1280×720 | 263 → 755 | 3.43 → 1.25 | 15.3 → 2.4 | 133 → 2 | 13 → 2 | 1256 → 108 | 390 → 280 | 25.9 → 6.0 | 228 → 218 |
| Vulkan Medium 1920×1080 | 237 → 539 | 3.87 → 1.80 | 17.8 → 2.8 | 142 → 1 | 6 → 1 | 69 → 82 | 417 → 324 | 25.8 → 7.0 | 318 → 308 |
| Vulkan High 1280×720 | 243 → 562 | 3.69 → 1.66 | 16.7 → 4.1 | 113 → 0 | 8 → 0 | 3887 → 30 | 392 → 281 | 25.9 → 7.6 | 358 → 347 |
| Vulkan High 1920×1080 | 218 → 420 | 4.38 → 2.31 | 18.9 → 3.6 | 133 → 0 | 5 → 0 | 55 → 31 | 418 → 305 | 25.8 → 8.9 | 482 → 471 |
| Compatibility Medium 1280×720 | 57 → 105 | 16.2 → 9.5 | 76.1 → 16.9 | 334 → 26 | 330 → 0 | 170 → 46 | 2282 → 2281 | 25.8 → 6.0 | 100 → 99 |

After, the four frames over 33 ms on Vulkan are frames with no batch, no allocation and the usual GPU time: the host's, not the client's. Before,
the 1.3 s and 3.9 s maxima are the first run of a Quality: shader compiles (Loading, below). The managed heap peaks at 61 MB; a game
allocates 4.8 MB/s and spends 68 ms in collections.

### What each setting costs

GPU time of the table's viewport and the Hud (D3D12, which reports it; 1280×720), the table held at round 1 with every model on it, the camera
where the game left it, one change at a time (`TABLETOP_PERF_BENCH`):

| | Before (Medium) | Low | Medium | High |
| --- | --- | --- | --- | --- |
| GPU ms | 2.67 | 0.50 | 1.18 | 1.55 |
| Primitives (M) | 26.5 | 2.0 | 6.1 | 7.6 |

| One change from Medium | GPU ms |
| --- | --- |
| SSAO off | −0.20 |
| Soft shadows off | −0.04 |
| Shadows off / 4 splits → 2 | −0.31 / −0.12 |
| MSAA 2× → off / → 4× | −0.17 / +0.17 |
| Render scale 75 % / 50 % | −0.11 / −0.26 |
| LOD threshold 2 → 4 px / 8 px | −0.29 / −0.46 |
| Miniatures hidden / casting no shadow | −0.54 / −0.16 |
| Terrain hidden | −0.31 |
| Hud hidden, fill light off | −0.02 each |

An integrated GPU has a tenth (Intel Iris Xe) to a third (Radeon 780M) of this one's throughput, so Medium is about 3.5–12 ms and Low 1.5–5 ms
of GPU a frame there, an estimate by that ratio; before, Medium was 8–27 ms.

### Hud and the move Ask, played

Team 1's player in the reference game (Compatibility, Medium, 1280×720; the sim plays the rest, a command every 300 ms), through
`interactive.sh` and `int-run.sh`: idle at a move Ask, pick up a five-model unit, drag it 4 s, drop, Esc, open the log, range rings on, 40 s
of play with the log open and 40 s with it closed.

| Action | Before: p50 / p99 / max ms, frames > 33 ms | After |
| --- | --- | --- |
| Dragging five models | 55 / 108 / 182, 81 of 90 | 9.6 / 24 / 73, 2 of 450 |
| A move Ask of mine opens | up to 1,529 | under 10 (the area follows) |
| Play, log open (40 s) | 16.4 / 102 / 1,529, 124 | 10.4 / 27 / 94, 20 |
| Play, log closed (40 s) | 16.5 / 108 / 1,464, 124 | 10.3 / 28 / 157, 31 |
| Log opens, range rings on | one frame of 40–70 | the same |

### Decisions

- **Miniatures and terrain get levels of detail at load.** Godot's runtime glTF import makes none, so every model drew its 57,000 triangles in
  the view and in every shadow split: 26 M primitives a frame. `MeshLoader.Template` calls `ImporterMesh.GenerateLods` (normal merge angle 60°,
  the editor's) on the thread pool beside the parse: 74 ms for the orc, 18–28 ms a terrain mesh, once per file. Godot picks a level per instance
  by screen size, in the shadow passes too.
- **The Quality presets** add SSAO, soft shadows and the LOD threshold to MSAA, splits and scale
  ([2026-10-24-graphics-settings-design.md](2026-10-24-graphics-settings-design.md)): Low drops SSAO and soft shadows and takes a 4 px threshold
  (bench: indistinguishable at the table's overview), Medium 2 px, High 1 px. A first start picks Low on an integrated, virtual or software GPU.
- **The libs are optimized in an export.** Godot builds the client in its `ExportRelease` configuration and its project references in the same
  one, which the SDK does not optimize: Geometry, Content and Protocol shipped as debug code, about ten times slower on a drag frame.
  `Directory.Build.props` sets `Optimize` for `ExportRelease`.
- **A render rebuilds only what changed.** Each coalesced render rebuilt every roster row (14 ms mean, 23 ms max, two a second), the header
  (to 20 ms), the dice tray (2.3 ms) and the unit markers. `RosterView.Changed` names the rows that differ (`RosterRow` equal by value) and a
  row whose icons are unchanged takes its new words in place; the header compares what its lines say; the dice tray repaints for a new or
  amended roll; `UnitMarkerNodes` keeps a row per unit while its icons stand; the decision box builds `DecisionView` only for an Ask it has
  not drawn.
- **The move Ask's areas are drawn off the main thread.** On the Ruins table a 12" move's areas take 45 ms (the lead) to 290 ms (five models)
  warm, over a second cold. `MoveRegions` is safe across threads (each reach and area built once, `Lazy`), `OutlinesAsync` builds them on the
  pool and the decision box draws the newest answer for the Ask still open. Opening the Ask builds no reach: an unmoved model's distance is 0.
- **One gate per drag frame.** `TableInput.ModelsDragged` carries every held model of a frame, so the decision box measures contacts once, not
  once per model; the landing test is `MoveReach.Reaches` (whether `Clamp` leaves the spot alone, without the nearest-point search), 5.6 ms to
  0.8 ms for five models held at the edge of their reach; each ruler refills one mesh and shares its material.
- **Shared resources.** `Palette.Translucent` and `Unshaded` give one material per colour and `Palette.Body` one mesh per base and height, so a
  highlight swaps a material instead of making one and like pieces draw together (draw calls 390 → 275); a base ring's torus has 6 segments
  round its tube, not 32. `TableView.PieceAt` reads an index by model id instead of walking the model layer, which the marks did for each mark
  every frame; the camera's held-key check allocates nothing.
- **`PerfProbe`** (`client/src/PerfProbe.cs`, development builds only): `TABLETOP_PERF_OUT=<csv>` writes a row per frame (frame time, measured
  render times, draw calls, objects, primitives, nodes, managed heap, allocation, collections, batches); `TABLETOP_PERF_GAME` enters a game at
  start (`TABLETOP_PERF_REPLAY=1` its replay), `TABLETOP_PERF_SECONDS` quits, `TABLETOP_PERF_BENCH` runs the settings bench, and `<csv>.mark` and
  `<csv>.shot` beside it name frames and take a screenshot for a driving script.

### Loading

- Entering a game with warm caches: one frame of about 400 ms (the whole log applied, the first table and Hud built, the board's print and
  normal map) and one of about 160 ms (terrain templates, the surface grains). Left as they are: the screen is changing.
- The first game after an install (or a driver or Godot update): three frames of 1.8, 1.25 and 0.95 s while the materials made at run time
  (`TerrainLook`, `Palette`, the glTF materials) compile their shaders on first draw; Godot's shader and pipeline caches hold them afterwards,
  and the first use of another Quality compiles its variants once. The export's shader baker (`shader_baker/enabled`) was
  measured and changes nothing: it bakes only shaders saved in the pack.

### Measured and left alone

- **The Compatibility renderer's draw calls** (2,281): it does not batch like pieces. Players get Forward+ unless their GPU has no Vulkan.
- **The JIT.** Turning quick JIT off (`TieredCompilationQuickJit`) left the interactive frames as they were once the libs were optimized.
- **The remaining play hitches** (Compatibility, a command every 300 ms): frames where a batch's render (about 8 ms of Hud) meets Godot's
  layout of the controls it made and the renderer's own work; at a player's pace and on Vulkan they do not show (the watched games above).
