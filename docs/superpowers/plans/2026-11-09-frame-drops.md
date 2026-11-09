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

## Left

- The move Ask's opening render: 40 ms in the decision box (`RenderMove`: regions 8, the gate 10, building the card's controls), 70 ms
  for the batch with the header and unit card. As slow in v0.1.37.
- The first drag frame: 45–55 ms (landings, contacts, the gate, rulers and landing rings created); later frames 6 ms but allocating
  500 KB each.
- The unit card's build on a selection: 25–33 ms. The header's rebuild per changed line: 5–12 ms.
- The hover label's draw: 7–11 ms per hovered unit (`HoverView.Of` and the Label3D's text).
- A ring layer's labels and meshes when its build lands: up to 22 ms for V's rings.
- Gen 1 and gen 2 collections: a pause up to 22 ms; the drag allocates 500 KB a frame.
- ReadyToRun (`PublishReadyToRun` under ExportRelease) was measured: first-use costs fell by about a third (advance 44 → 30 ms, the
  first drag frame 42 → 31 ms), spikes per run did not change, and `GodotSharp.dll` grew from 6 to 16 MB. Not adopted.
