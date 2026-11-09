# Frame drops in play

The owner sees the Godot client drop frames in a real game (Windows, RX 6900 XT, release export). This plan measures where a
frame goes, compares the client at `97f5fc1c` (v0.1.37) with main at `7400a219`, fixes the largest costs, and lists what is left.

## Method

- Driver: `client/src/PerfScript.cs` (`TABLETOP_PERF_SCRIPT=play`) plays a fixed session through real input events and names each phase
  in the `PerfProbe` CSV: idle; a hover sweep over every enemy unit; T, the sweep, T off; a click on a unit yet to act; V, the sweep, a
  pan with D held; 2 (Advance) and the move area; a 24 px drag of the acting unit; Enter (the move); 1 (Shoot); the sweep under the
  shooting Ask; a click on an eligible target; Enter (the volley); a rest. Recipe: `client/AGENTS.md` → Measuring frame times.
- Game: a copy of the owner's "Test" game (3.6.0, 8 units, 45 models, 18 terrain pieces) cut at seq 672, Team 1's Specialists to
  activate with Railguns in range, both teams on one dev account; a fresh copy per run, since the script moves and shoots.
- Builds: each tree `git archive`d, `exclude_filter` cleared (a dev export packs the built-in terrain), imported, and exported with
  `godot --export-release Linux` (ExportRelease C#, the release template). A dev build, so the probe runs.
- Recorded per frame: `dt_ms` (frame start to frame start), `script_ms` (the main thread's `_Process` span), `physics_ms`,
  `cpu_render_ms`/`gpu_ms` (viewport measured), allocations and collections, pipeline compiles, and `probes`: `PerfProbe.Time`
  around each suspect (input handling, the dispatcher's turn, the Hud layout, each Hud part's render, the rings, sight lines, hover
  labels, markers, BattleFx, the decision box's move render and its parts, the drag's placement and its parts). The probes are a
  scratch patch, not in the tree.
- Runs: WSLg, `--rendering-driver opengl3` (Vulkan under WSLg is llvmpipe; GL runs on the RX 6900 XT through D3D12), 1600×900
  windowed, VSync off, Medium quality, three runs per build, builds alternating.
- Trust: the main thread's C# (`script_ms`, `probes`) is the owner's code on the owner's runtime and is CPU-bound; read it as is.
  `gpu_ms`, `cpu_render_ms` and a first-draw shader compile are GL-through-D3D12, not Forward+ on Windows: they show where a compile
  happens, not what it costs there. Another agent's Godot (a movie writer at 90 % of a core) ran throughout, so frames over 33 ms with
  no main-thread work are partly that machine's noise; compare the main-thread columns.

## Findings

Frames over 33 ms per run, and frames whose main-thread C# alone took over 33 ms:

| Build | > 33 ms | > 50 ms | main-thread C# > 33 ms |
| --- | --- | --- | --- |
| old `97f5fc1c` | 19.7 | 9.7 | 5.7 |
| new `7400a219` | 22.7 | 16.3 | 9.0 |
| fixed | 27.3 | 11.7 | 3.7 |

The fixed build's extra frames over 33 ms are frames with no main-thread work (one run had 24 in its rest phase with the GPU at
12 ms); its main-thread spikes are the fewest of the three.

Worst frame per phase (ms, mean over runs) and what filled it:

| Phase | old | new | fixed | Cost in new, by probe |
| --- | --- | --- | --- | --- |
| hover (first enemy) | 46 | 113 | 57 | threat outline built on the main thread 71, its labels and meshes 29, then a 190 ms frame compiling the ring shaders |
| T on | 18 | 145 | 38 | T's union of every enemy unit's outline on the main thread, 126 |
| V on | 71 | 92 | 36 | range outlines on the main thread 47, shapes and labels 26 |
| select | 83 | 150 | 105 | unit card 33, roster rebuilt in full 23 |
| advance (move Ask opens) | 135 | 192 | 145 | decision box 50 (contacts of unmoved ghosts 17, regions 8), roster 22, header 12 |
| drag (first frame) | 79 | 83 | 95 | the placement 45: landings 14–17, contacts 7–19, the decision box's gate 8–20, landing rings and rulers 10 |
| volley | 108 | 104 | 65 | roster rebuilt in full 22 (the acting team changed sides), decision 10, dice 10 |
| hover under the shooting Ask | 55 | 63 | 54 | sight marks 22 (a new box mesh per mark per answer), hover label 10 |

Regressions since v0.1.37, all fixed here: the threat rings (hover, T, the pinned enemy) computed their outlines synchronously; the
ring materials were missing from the shader warm-up; the roster rebuilt every row on a selection or on the acting team changing.
The decision box opening a move, the first drag frame and the unit card were as slow in v0.1.37.

Measured and not offenders (worst single frame over six runs; a typical frame under 0.1 ms each): BattleFx 5 ms, unit markers' and
model marks' per-frame follow 8 and 1 ms, the ring labels' per-frame placement with `GameScreen.Covered` 8 ms (the first frame a
label shows; 0.1 ms after), the Hud layout 8 ms, region builds on the pool (off the main thread; the 70 ms 16" oval area no longer
shows). Model marks' render on an event batch reaches 11 ms. Collections: about 12 gen 0, 6 gen 1 and 3 gen 2 a run, the longest
pause 22 ms.

## Fixes

1. Rings build on the thread pool (`RingBuilds`, `RingStamp`): `ThreatRings.Builder`, `ThreatRings.AllBuilder` and
   `RangeRings.Builder` read the projection on the main thread and build the outline and its fill shape on the pool, one build per
   unit kept while its stamp (models, slots, placements, threat) stands; `RangeRingNode` draws a layer when its build lands.
   `RangeRingCache`, `ThreatRingCache` and `AllThreatCache` are gone. Hover input 86 → 28 ms, T 125 → 8 ms, V 48 → 8 ms (22 ms when
   the build lands: labels and meshes).
2. The shader warm-up draws a ring's fill, outline and behind pass (`WarmupScene`, `GraphicsWarmup.SceneVersion` 2): the first
   threat ring's 190 ms frame is gone.
3. Roster keeps rows (`RosterView.Keep`): a render keeps each row whose content and selection stand, teams matched by id in any
   order, and rebuilds the rest: a selection, an activation or the acting team changing sides costs 3 ms, not 22.
4. `MoveRegions.Prepare` starts every model's reach on the pool when the move Ask opens, so a pickup's first frame does not build
   them on the main thread.
5. `Contacts.Of` returns at once when every proposed model stands where it is: a move Ask's opening gate 31 → 15 ms.
6. Sight marks share one unit box mesh scaled per mark: 22 → 10 ms per answer.

Tests: `RingBuildsTests`, `ThreatRingsTests.A_builder_reads_the_projection_when_made_and_builds_the_same_rings_later`,
`RosterDiffTests` (`Keep`), `MoveRulerTests.Prepare_builds_every_models_reach_on_the_pool_so_a_later_drag_finds_it_built`,
`ContactsTests.Models_proposed_where_they_stand_have_no_contact_whatever_they_stand_on_or_beside`.

## Second round: the remaining spikes

Main at `ad868022` against this branch, three runs each, on a quiet machine (no other Godot). The script now plays a second activation
(the next team's selection, Advance, drag and move) after the casualties, so each action is measured the first time a session does it
(first use: .NET compiling the code, Godot shaping and caching new text) and again (what every later turn pays). New columns split a
frame: `script_ms` (the `_Process` span), `flush_ms` (from there to the draw), `draw_ms` (the draw's submission), and the physics step's
spans (`physics_steps`, `before_physics_ms`, `after_physics_ms`); each probe also counts its calls and the kilobytes it allocated.

### What fills a spike

The dispatcher's turn (events, the Hud renders) runs as a deferred call flushed in the physics step, and so does what Godot does with
the controls a render made: their layout, theme lookups, text and the old controls' deletion. A spike's time is almost all there: the
`_Process` span stays under 3 ms, the flush before the draw under 1 ms and the draw 7–9 ms, while the physics step's flush takes 40–100
ms. The C# probes account for 15–25 ms of that (the dispatcher's turn); the rest is Godot's work on what the turn changed, which no C#
probe reaches. Measured by removal (a part's render skipped by a scratch switch): on the second move, skipping the header saved 17 ms,
the unit card 13 ms, the dice tray nothing; on a selection the unit card's first build costs 65 ms. Turning the Hud's word wrap off
changed nothing. Under WSLg this work also includes GL calls through D3D12 (new glyphs uploaded to the font cache, new meshes); a native
profile on Windows is the next step to split Godot's layout from the driver.

### Worst frame per phase, ms (mean of three runs)

| Phase | main `ad868022` | this branch |
| --- | --- | --- |
| select (first) | 103 | 113 |
| select (second) | 62 | 58 |
| advance (first) | 120 | 101 |
| advance (second) | 87 | 76 |
| drag (first) | 91 | 88 |
| drag (second) | 46 | 45 |
| move (first) | 77 | 60 |
| move (second) | 102 | 85 |
| volley | 65 | 55 |
| casualties | 83 | 69 |
| hover with V on | 69 | 64 |
| frames over 33 ms per run | 27.7 | 23.7 |
| frames with over 33 ms of main-thread C# per run | 4.0 | 3.3 |

### Fixes

1. Unit card parts (`UnitCardParts.Changed`): a change rebuilds only the parts it shows in (title, count, state, note, models,
   weapons, rules); a move's activated chip rebuilds the state row and leaves the weapons table and the rules standing.
2. Header lines: each of the three lines rebuilds only when what it shows changed (`HeaderBar`), so a move's step leaves the round and
   the team tiles standing; before, any change rebuilt all three and refitted each.
3. Loading garbage (`LoadSettled`): once the table stands with its models, `GameScreen` runs a full collection. The 16 ms gen 1 pause that
   fell on the first hover is gone; the longest pause in play is now 6–9 ms (one gen 2 at the move Ask).

Tests: `UnitCardPartsTests`, `LoadSettledTests`.

### First use and steady state

The first time a session opens a move Ask, drags or selects a unit costs 50–100 ms; the second time 45–85 ms, of which C# is 10–20 ms.
The first-use C# (the decision box opening a move 37 ms, the first drag frame 50 ms, the unit card's first build 32 ms) is mostly the
.NET JIT: ReadyToRun for the client's own assemblies (`PublishReadyToRun` under ExportRelease, `GodotSharp.dll` left out; `Tabletop.dll`
1.7 → 4.7 MB, `Tabletop.Geometry.dll` 0.2 → 0.5 MB) cut those to 26, 46 and 21 ms and the first advance's frame 121 → 90 ms, with no
change to the second time. Not adopted here: the release exports macOS universal and Windows from Linux, and a cross-target ReadyToRun
build of those is untested.

## Left

- Godot's work on rebuilt Hud controls in the physics step's flush: 30–60 ms a game event under WSLg (above). Profile natively on Windows
  (Forward+, the owner's renderer) before changing the Hud further; the candidates are fewer rebuilt controls (labels updated in place)
  and new glyphs uploaded to the font cache.
- The move Ask's opening render: 35–40 ms of C# in the decision box the first time (regions 8, the gate 14, the card's controls), 3 ms
  the second time.
- The first drag frame: 50 ms of C# the first time (landings, contacts, the gate), 10 ms the second; each drag frame allocates 0.3–1.6 MB
  (landings and contacts in `Tabletop.Geometry`), which keeps gen 0 collections at 1–4 ms.
- The unit card's first build on a selection: 32 ms of C#, 65 ms with Godot's work.
- The hover label: `HoverView.Of` 3 ms a hover, 9 ms the first time.
- A ring layer when its build lands: 20 ms for V's rings, 16 of them creating the fill's mesh (building its vertex arrays on the pool
  instead was measured and saved nothing).
- ReadyToRun: measured above, not adopted.
