# Performance and network play

Scope: the Release play profile of the game server, the client's Core and the browser viewer (finding 1 of
[2026-10-11-performance-review.md](2026-10-11-performance-review.md)), and what crosses the wire for remote players at
`https://tabletop.cstupi.com`. Rules behaviour does not change.

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
