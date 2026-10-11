# Performance review: what the player waits for

Scope: the latency of one player action, from the click that answers an Ask to the next Ask on screen, plus reconnect, undo
and the client's per-frame work. Intended behaviour does not change under any proposal here. Measurements and the commands
that reproduce them are in [2026-10-11-performance-review-appendix.md](2026-10-11-performance-review-appendix.md).

Reference game for every number: two teams, 15 units of 5 models each (30 units, 150 models), a ruin and two forests,
alternating activation, strict, played to the round-4 end by `sim`'s `AnswerPolicy`. It produces 2,879 events from 368
commands. Server numbers are Release builds on the WSL host unless marked; client numbers are `client/src/Core` logic only
(Godot node, layout and GPU cost were not measured).

## The action, end to end

An Advance-and-shoot activation is four commands: `activate`, `move`, `shoot_or_not`, `declare_targets` (plus
`remove_casualties` from the defender when there is a choice). Per command, averaged over the reference game:

| Command | Events | Batches (= Postgres transactions = `EventsMessage`s per client) |
| --- | --- | --- |
| `activate` | 7.7 | 5.6 |
| `move` | 7.6 | 5.9 |
| `shoot_or_not` | 5.2 | 3.8 |
| `declare_targets` | 20.6 | 17.1 |
| `remove_casualties` | 17.5 | 11.8 |

One activation is therefore about 41 events in about 32 batches: 32 Postgres commits, 32 broadcasts to each client, 32
`EventsApplied` rounds on each client, each re-rendering every Hud part.

Where one command's time goes (Release, tiered JIT, one process playing the whole game):

| Stage | Cost now | Source |
| --- | --- | --- |
| Engine `Advance` at a turn boundary (`turn_started`, `ask:activate`) | 38-46 ms mean, 345-493 ms max, per call; two calls per activation | measured |
| Engine `Advance` elsewhere (shooting, moving, steps) | 0.2-7 ms mean per call | measured |
| Postgres append | 6.1 ms per batch (p50 5.9), about 6 per command, 13.5 s over the game | measured |
| Whole-log scans (`EffectiveLog`, `FrameReader.Read`, `DiceCursor.DrawnSoFar`, `SchedulerLog.Of`) at 2,879 events | 0.04-0.06 ms each | measured |
| `ModelFacts` after each batch (150 models) | 0.02 ms | measured |
| JSON per message (`WireJson.Serialize`) | 0.07 ms; 785 B per `EventsMessage` | measured |
| Client: `CombatLog.Build` (run twice per batch: `DiceTray`, `UndoButton`) at 2,879 events | 9 ms each, linear in the log | measured |
| Client: `CombatLogPanel` rebuild while the drawer is open | `Build` plus about 1,050 rows of Controls, freed and recreated per batch | reasoned |
| Client: `TableView.Redraw` per batch | every terrain proxy, marker disc and zone mesh reallocated | reasoned |

Per command in the reference game: 36 ms mean in memory, 116 ms mean with Postgres (p90 322 ms, max 1.7 s). With the first
fix below applied in a scratch copy: 13 ms in memory, 52 ms with Postgres. What remains with Postgres is the commits.

The compose stack the owner plays against runs none of these Release numbers: see finding 1.

## Do first

Ranked by player-felt gain per effort.

### 1. The dev stack runs the server unoptimised, under hot reload

- **Where.** `docker-compose.yml:132`: `game` runs `dotnet watch run` with no configuration, so Debug, and dotnet-watch sets
  `DOTNET_MODIFIABLE_ASSEMBLIES=debug` (seen in the container's process list), which keeps the JIT at minimum optimisation.
  `scripts/client` builds the client with a plain `dotnet build client/Tabletop.csproj` (Debug) as well.
- **Now.** The reference game under Debug plus modifiable assemblies: 180 s of server compute against 14 s in Release (12.8x).
  Command mean 485 ms, p90 2.0 s, max 3.1 s; the turn-boundary `Advance` 576-711 ms mean, up to 1.6 s. The gap between a
  unit finishing and the next "Activate a unit" Ask is two of those, about 1.3 s, before any Postgres or client time.
  Geometry suffers most: a sight query 62 ms (5.3 ms Release). The client's Core logic under Debug: range rings for one
  5-model unit 113 ms (24 ms), a move Ask's region 40-60 ms (4.6-6.3 ms), a drag frame of five models 2-5 ms (0.7-1.7 ms);
  `CombatLog.Build` is library-bound and the same 9 ms in both.
- **Proposal.** A play profile for on-screen checks: the `game` service (or an override file) running
  `dotnet run -c Release --project server/game/...` without watch, and the Godot client run from a Release build
  (`dotnet build -c Release`, or a release export, which Godot builds as `ExportRelease`). Keep watch for editing sessions.
- **Guarantee.** No code changes; the rules suite on the socket driver against the Release container, 193/193.
- **Effort.** An hour (compose override, a `scripts/` line, a sentence in `AGENTS.md`).
- **Gain.** About 10x on every server stage the owner feels on the stack; 5-10x on the client's geometry.

### 2. `ChargeTargets` measures every path before it checks the distance

- **Where.** `server/game/Rules/GrimdarkFuture/GfMelee.cs:111-129`, reached from `GfRules.Scheduling.cs:53` (`ActionsFor`,
  for each unit not yet activated), from `LegalTurn` (`GfRules.Scheduling.cs:30-39`), which the scheduler calls at least
  twice per activation: `SchedulerBase.IsFinished` (`SchedulerBase.cs:159-160`, via `NextTeam`) for `turn_started`, and
  `TurnCycle.Step` (`TurnCycle.cs:70-71`) for the activate Ask.
- **Now.** For every own model and every enemy model it runs `Movement.Check` (a 0.25" path walk that shifts the base
  outline and tests it against the table slab at every step) and only then compares `Battlefield.Distance` with the
  charge distance (`GfMelee.cs:126-127`). With 15 units a side that is up to 15 enemy units × 25 model pairs of path walks
  per unit asked, for each of up to 15 unactivated units per `LegalTurn`. Measured over 176 samples: `LegalTurn` 47 ms mean
  (max 520 ms), of which `ChargeTargets` 44 ms. The two turn-boundary `Advance` kinds are 12.2 s of the game's 13.9 s of
  engine time.
- **Proposal.** Test the distance first and walk the path only for pairs inside it:
  `Battlefield.Distance(state, m, e) <= distance && !Movement.Check(...).CrossesImpassable`. Both are pure, so the
  answer is the same. Hoisting `Lookup.LivingModels(state, t)` out of the inner lambda is a further small saving.
- **Guarantee.** Measured in a scratch copy: identical `ChargeTargets` lists on all 176 sampled states (both orders run
  side by side); the reference game plays the same 2,879 events; the in-process rules suite passes 193/193 with 428/428
  covered. To run before merging: `dotnet test` and the suite on both drivers.
- **Effort.** Ten minutes.
- **Gain.** `ChargeTargets` 44 ms to 1.9 ms per `LegalTurn` (23x). Turn-boundary `Advance` 62-77 ms to 4-5 ms mean with JIT
  tiering off (38-46 ms to 4-8 ms with it on); command mean 57 ms to 9.5 ms in memory. On the Debug stack this is most of
  the 1.3 s pause between activations.

### 3. One Postgres transaction per command, not per batch

- **Where.** `GameSession.AdvanceAsync` (`server/game/Session/GameSession.cs:150-163`) calls `AppendAsync`
  (`GameSession.cs:126-144`) once per `Advance` result; each opens a pooled connection, a transaction, one `INSERT` per
  event and a commit (`server/game/Log/PostgresEventLog.cs:10-30`).
- **Now.** Batches average 1.31 events, so a command is about 6 commits, 32 per activation. Measured: 6.1 ms per append; a
  bare commit of one row is 5.9 ms, one round trip 0.9 ms (WSL Docker volume; `synchronous_commit=on`). That is about
  36 ms of every command after finding 2, and 13.5 s over the reference game.
- **Proposal.** Inside one command (`HandleCommandAsync`, `StartAsync`'s catch-up), apply each batch to `Play.Applier` as now
  so the next `Advance` reads it, collect the batches, append them in one transaction at the end, then broadcast each batch's
  `EventsMessage` exactly as today (per batch, per viewer, with that batch's `sealedBefore`), then `ModelFacts` and
  `MarkEndedAsync`. Send the inserts as one `NpgsqlBatch` (or binary `COPY`) rather than one command per event.
  - Wire unchanged: clients receive the same messages in the same order, only after the commit.
  - The log still precedes the broadcast. What changes is that the in-memory projection runs ahead of the log inside
    one mailbox turn; a failed append already faults the session (`FaultAsync`), which closes every socket, and the next
    hydration rebuilds from the log and catches up through `AdvanceAsync`. Nothing unlogged was ever sent.
- **Guarantee.** `EventLogContract` against both logs; `tests/Tabletop.GameServer.Tests` (session, fault and end-of-game
  tests); the suite on the socket driver, which goes through Postgres, 193/193. A new test: a command whose advance runs
  many batches produces the same log rows and the same frames as before.
- **Effort.** Half a day.
- **Gain.** Measured on the same database: one batch of 8 events 5.5 ms, of 20 events 6.0 ms, against 6 × 6 ms. Command
  mean with Postgres 52 ms to about 22 ms; `declare_targets` (17 batches) 128 ms to about 32 ms.

### 4. Every Hud part re-renders on every batch, and two of them rebuild the whole combat log

- **Where.** `Session.Handle` raises `EventsApplied` once per `EventsMessage` (`client/src/Core/Session.cs:299-310`).
  Subscribers that rebuild their whole subtree on each: `UnitCard`, `Roster`, `HeaderBar`, `DiceTray`, `UndoButton`,
  `DecisionBox`, `CombatLogPanel`, `UnitMarkerNodes`, `HoverLabels`, `RangeRingNode`, `TableView` (twice: its own handler
  and `Animator.Pump`, `client/src/Table/Animator.cs:55`), `SightLines`, `GameScreen.RefreshAll`. `DiceTray.Render`
  (`client/src/Hud/DiceTray.cs:47`) calls `CombatLog.Latest`, which is a full `CombatLog.Build`; `UndoButton.Render`
  (`client/src/Hud/UndoButton.cs:43`) calls `UndoPlan.Of`, which builds it again (`client/src/Core/UndoPlan.cs:26`).
- **Now.** `CombatLog.Build` is 9 ms at 2,879 events in Release, linear in the log, so 18 ms per batch at the end of a game
  and about 0.6 s of main-thread work per activation (32 batches), before any Control is created. While frames arrive
  faster than one per frame (`GodotDispatcher` posts each with `CallDeferred`), several renders land in one frame.
- **Proposal.**
  - Each Hud part marks itself dirty on `EventsApplied` and renders once in the next `_Process` (or one deferred call per
    part per frame). The final state is the one the last render produces today.
  - One combat-log model per log state: a `Session` member (or a small `CombatLogCache` in Core) holding `CombatLog.Build`
    keyed on `(Log.Count, PendingUndoTo)`; `DiceTray`, `UndoButton` (through an `UndoPlan.Of` overload taking the built
    entries) and `CombatLogPanel` read it.
- **Guarantee.** `tests/Tabletop.Client.Tests` (`CombatLog`, `UndoPlan` tests unchanged; a new test that the cache returns
  the same entries as `Build` across appends and an `Undone`); the owner's on-screen check of log, dice tray and undo.
- **Effort.** Half a day for the cache; a day for the per-frame render across the parts.
- **Gain.** Combat-log work from 2 × batches × 9 ms to one build per frame with new events: about 0.6 s to under 0.1 s per
  activation late in a game (Release; several times that on the Debug client). Fewer Control rebuilds by the same ratio.

### 5. `TableView.Redraw` reallocates every terrain, marker and zone mesh on every batch

- **Where.** `client/src/Table/TableView.cs:44` subscribes `Redraw` to `EventsApplied`; `Sync`'s update callbacks call
  `TerrainNode.Set` (`TerrainNode.cs:15-29`: frees and recreates each volume's `BoxMesh`/`CylinderMesh` and material),
  `MarkerNode.Set` (`MarkerNode.cs:15-24`: new `CylinderMesh`, new `StandardMaterial3D`, new `Label3D`) and, through round 0,
  `ZoneNode.Set` (`ZoneNode.cs:14-28`: `SurfaceTool` meshes) for every item, changed or not (`TableView.cs:59-67`).
  `Animator.Pump` calls `Redraw` again after each pump.
- **Now.** Terrain never changes in play, yet each of its proxy meshes and materials is rebuilt on every batch, twice; each
  marker's disc and label likewise. Not measured (Godot); the cost is resource churn on the main and render threads,
  roughly 64 times per activation.
- **Proposal.** `Sync`'s update runs only when the item differs from what the node last drew: keep the last
  `TerrainPlacement`, `(Marker, Color?)` and `(DeploymentZone, TableSize, Color)` on the node and return early on equality
  (records compare by value; `Volumes` is a list, so compare the placement's own fields and the list by sequence). Pieces
  keep their cheap `Place` path.
- **Guarantee.** Client tests (no Core change); the owner's on-screen check of terrain, objectives (control colours) and
  deployment zones through round 0.
- **Effort.** Two hours.
- **Gain.** Removes all terrain, marker and zone allocation from the per-batch path; the table redraw becomes the model diff only.

### 6. Every model parses its GLB again, on the main thread

- **Where.** `client/src/Table/MeshLoader.cs:98-106` (`Build`): a new `GltfDocument.AppendFromFile` and `GenerateScene` for
  each piece node, called from `TableView.LoadPieceMesh` for every model that appears (deploy, reconnect, a new screen) and
  `LoadTerrainMesh` for every terrain piece.
- **Now.** 150 models from a handful of distinct assets mean 150 parses of the same few files on the main thread at load and
  at each reconnect. Not measured (Godot); a miniature GLB of a few hundred KB parses in milliseconds to tens of
  milliseconds, so the load hitch is of the order of seconds.
- **Proposal.** Cache the generated scene per cache path (asset hash) and per built-in name, and hand each node
  `Duplicate()` of the cached root, with `Orient` applied once to the cached copy.
- **Guarantee.** No Core change; the owner's check that every mesh still sits on its base at the right scale and facing.
- **Effort.** Two hours.
- **Gain.** One parse per distinct asset instead of one per model.

## Later

### 7. Sight lines: a list per pair and no broad phase

- `Sight.Line` copies all ~150 blockers into a new list for every pair whose target is a body (`server/game/Interpreter/Sight.cs:45`),
  and `LineOfSight.Check` tests every blocker against up to 27 rays per pair (`libs/Tabletop.Geometry/LineOfSight.cs`).
  Measured: one `SightQuery` between two 5-model units 5.3 ms; the `shoot_or_not` and `declare_targets` Asks 7 ms per `Advance`
  (shooting validity over every enemy unit).
- Proposal: build the per-target exclusion without copying (skip `toModel` inside `Obstructions`), and prefilter blockers
  by the segment's 2D bounding box or the observer-target capsule. Same answers.
- Guarantee: `tests/Tabletop.Geometry.Tests` (line of sight), `GfShooting`/`GfAttack` tests, the suite. Effort: half a day.
  Gain: several-fold on sight; 5-10 ms per shooting command.

### 8. `Movement.Region` tests every model on the table at every step of every ray

- `libs/Tabletop.Geometry/Movement.cs:35` keeps every other base, and `:56` tests each against the moving base at every 0.25"
  of 180 rays. The client draws a move Ask's region with all other models (`client/src/Hud/DecisionBox.Move.cs:46`).
  Measured: 6.3 ms with 145 other models against 0.44 ms with none (6"); 4.6 ms at 12" with the others.
- Proposal: drop bodies whose bounding circle lies beyond `allowance + baseRadius` of the origin, as `Prefilter` already does
  for volumes. Exact: a body that far cannot touch the swept base.
- Guarantee: `Tabletop.Geometry.Tests` (`Movement`), client tests. Effort: an hour. Gain: about 10x on drawing a move Ask.

### 9. Range rings: about 30k distance evaluations per model, on every batch

- `client/src/Core/RangeRings.cs:26-67`: 769 scan angles per model, each a 40-step bisection (`Reach`), then crossings
  bisected 50 times; `RangeRingNode.Draw` reruns it on every `EventsApplied` and selection change
  (`client/src/Table/RangeRingNode.cs:10-36`). Measured: 24 ms for one 5-model unit (Release); quadratic in the unit's
  carriers, which is the 1.5M distance calls `docs/handoff.md` records for 20 packed carriers.
- Proposal: redraw only when the selected unit or its models' placements change (key on them), and solve `Reach` in closed
  form for circular bases (the ring is the base circle grown by the range). Guarantee: `RangeRings` tests. Effort: half a day.

### 10. The combat log panel rebuilds every row

- `CombatLogPanel.Render` frees every row and builds about 1,050 top-level rows (each an `HBoxContainer`, a `Button` and an
  "Undo to here" `Button`, more when expanded) on every batch while the drawer is open (`client/src/Hud/CombatLogPanel.cs:59-71`),
  after its own `CombatLog.Build`.
- Proposal: append rows for new entries and replace only the last open block, or show the last N blocks with a "show
  earlier" row. Guarantee: client tests on `CombatLog`; on-screen check of filters, reveal and undo-to-here. Effort: a day.

### 11. Grant rows are found by a linear walk of the whole catalog

- `server/game/Effects/LiveProperties.cs:195` and `server/game/Effects/Lookup.cs:32` call
  `PropertySet.AllEffects().FirstOrDefault(e => e.Id == g.EffectId)` for each grant; `AllEffects` flattens 536 entries
  (625 effects) through nested iterators. Measured: 0.125 ms per lookup. The reference game has no grants. Each
  `Lookup.UnitHas` and each `Settle` walks it once per grant on the unit asked about, and `LegalTurn` asks `UnitHas(Aircraft)`
  of every enemy unit for every unit it offers: with one grant on each of 15 enemy units that is about 114 walks, 14 ms,
  per `LegalTurn`, twice per activation.
- Proposal: an `EffectById` dictionary built once on the immutable `PropertySet`. Guarantee: the army-book and spell suite
  games. Effort: an hour.

### 12. Smaller items

- `DiceTray._Process` frees and recreates its label and dice faces every frame for 0.4 s after each new roll
  (`client/src/Hud/DiceTray.cs:56-61`); setting the face textures in place does the same.
- `Battlefield.Terrain` rebuilds the world volumes on every call (`server/game/Interpreter/Battlefield.cs:32`); the board is
  fixed for the game, so `GameState` can hold them once.
- `SightLines` re-sends one `SightQuery` per assigned target on every batch during a target assignment
  (`client/src/Table/SightLines.cs:28`), each 5 ms on the server and queued behind commands on the mailbox.

## Measured and not worth changing now

- Whole-log reads per `Advance`: `EventApplier.EffectiveLog` 0.055 ms, `FrameReader.Read` 0.041 ms, `DiceCursor.DrawnSoFar`
  0.039 ms, `SchedulerLog.Of` 0.048 ms at 2,879 events. Linear, so a log ten times longer costs about half a millisecond.
  `Advance` time does not grow with the log in the reference game (it falls as models die).
- `ModelFacts` after every batch: 0.02 ms for 150 models; sent only when a value changes.
- Undo: `EventApplier.ProjectTo` 0.55-1.0 ms, `Replay.To` 0.4 ms, a full replay 0.5 ms at 2,879 events, on server and client.
- Hydration from Postgres at 2,879 events: 92-103 ms (the read and JSON parse are 67-98 ms of it); the connect sends
  1.68 MiB in 10 frames, serialised in 11-16 ms. The client parses those pages on its socket thread
  (`client/src/Core/ClientGameSocket.cs:65`), about 80 ms of `WireJson.Deserialize` for the whole log.
- Wire volume: 4.9 MiB of `EventsMessage` over the game across three clients, 785 B mean; `ask_opened` is the largest
  kind (1.6 KB mean, the activate options). Negligible next to the latencies above.
- Presence: dirty-only sends at 20 Hz (`client/src/Table/TableInput.cs:89`), relayed by the server's tick; no cost found.
- The 60 s JWT refresh is asynchronous and off the frame (`client/src/Core/Session.cs:85-98`).
- Management on the play path: `GET /v1/games` 11-12 ms warm (320 ms on the first request after start),
  `GET /v1/properties/{hash}/rules` 4.5-5 ms warm, fetched once per hash.
- Socket sends never block the mailbox: `SocketConnection.SendAsync` writes to a channel drained by its own loop.

## Correctness risks found on the way

- **`Animator` glides only the first relocation of an event.** `Pump` returns after starting the first tween
  (`client/src/Table/Animator.cs:52`), and the event has already been dequeued, so the other models of a `ModelsMoved` (a
  5-model unit's move) jump while one glides. A presentation defect, not a performance one; the fix (tween every relocation
  of the event in parallel, one `Done` when the last finishes) also shortens the queue.
- **Finding 3 changes failure timing.** The projection runs ahead of the log within one mailbox turn. The session must
  fault on any append failure after applying (it does today through `FaultAsync`), and no message may be sent before the
  commit. The test listed under finding 3 holds that.
- **Finding 4's per-frame render.** A part that reads the batch argument rather than the projection (`FloatingText`,
  `TurnBanner`, `DecisionBox.Reveal`, `Animator`) keeps its per-batch handler; only whole-part re-renders are coalesced.

## Effort and gain, ranked

| # | Finding | Effort | Gain the player feels |
| --- | --- | --- | --- |
| 1 | Release play profile for the stack and the client | 1 h | about 10x on everything on the dev stack |
| 2 | `ChargeTargets` distance first | 10 min | turn boundary 38-77 ms to 4-8 ms per `Advance` (Release); most of the 1.3 s pause per activation on Debug |
| 3 | One transaction per command | 0.5 d | about 30 ms per command, 100 ms on `declare_targets` |
| 4 | Coalesced Hud renders, one combat-log build | 1-1.5 d | about 0.5 s of main thread per activation late in a game |
| 5 | Redraw only changed table nodes | 2 h | per-batch GPU resource churn removed |
| 6 | GLB parse cache | 2 h | load and reconnect hitch of the order of seconds |
| 7 | Sight broad phase | 0.5 d | 5-10 ms per shooting command and per sight check |
| 8 | `Movement.Region` body prefilter | 1 h | move region 6 ms to under 1 ms |
| 9 | Range rings | 0.5 d | 24 ms per redraw for 5 models, more for packed units |
| 10 | Incremental combat-log panel | 1 d | removes about 2,000 Controls per batch while the log is open |
| 11 | Effect index | 1 h | up to about 14 ms per `LegalTurn` in games with grants |
