# Loading feedback

Owner: *"We should have some loading indicators when downloading other players' models and compiling shaders to help the user
understand what is happening and prevent confusing lockups."* Scope: the Godot client's waits over about 200 ms and the browser
viewer's, the shader compiles of a first start and the frame that entering a game took
([2026-10-25-performance-and-network.md](2026-10-25-performance-and-network.md) → The Godot client → Loading).

## What each wait shows

One treatment in both clients: a panel in the theme with a line of text and a thin bar, filled to the count when the work is
counted, running back and forth when it is not. Text is plain and short.

| Wait | Godot | Browser |
| --- | --- | --- |
| Shader warm-up (a first start, a new GPU driver, Godot or client version) | Window curtain, "Preparing graphics…", counted | none (the browser compiles nothing ahead) |
| Socket not yet welcomed | Game screen curtain, "Connecting…" | "Connecting…" |
| A first connect that failed and retries | Curtain, "Reconnecting…" | "Reconnecting…" |
| Welcomed, until the board, the log caught up (`Session.CatchingUp`) and the board's print drawn | Curtain, "Loading game…" | "Loading game…" until the scene is built |
| The table stands, its models and terrain meshes still loading, then two frames to draw them | Curtain, "Loading models 3 of 12", counted, then "Loading game…" | none |
| A reconnect with the table already shown | Strip, "Reconnecting…" | header line, as before |
| A later wave of models (a late army), or meshes still loading 20 s after the table stood | Strip, "Loading models 3 of 12", counted | none (the board draws bases) |
| Setup: a Random table or a loaded layout (from the click, through the server's answer, until its new meshes are drawn), or new meshes for over 150 ms | Veil over the table's box, "Laying out the table… 7 of 23", counted once meshes load | none |
| The WebAssembly geometry download | none | strip, "Loading table tools 3 of 13", counted; setup blocks on it |

- A curtain covers the window or the game screen and takes the mouse. Its panel shows only once the wait has lasted 200 ms
  (`LoadingGate`), so a short wait is the plain background, never a flashing panel; the warm-up's shows at once.
- The veil (`LoadingVeil`, `TableLoad`) dims the table's box and takes its mouse; the top bar and the tool panel stay usable. It shows
  only past 150 ms, so a tile of a kind already on the table never shows it.
- The strip is not blocking: the table plays with each model's base and hitbox, and its mesh takes their place as it arrives.
  It sits in `HudPart.Loading`, the button row's height at the foot's centre, clear of both columns.
- `LoadingView` (`client/src/Core/Loading.cs`) decides the line; `AssetProgress` counts the files a table loads, each once per wave,
  a wave ending when every file started has finished (loaded or failed), so a late army counts from one.

## Shader warm-up

Godot compiles a material's shaders and pipelines when it is first drawn, and the export's shader baker covers only shaders saved
in the pack; the table's materials are made at run time. `WarmupScene` draws, in a viewport set up as the table's, everything a table
draws, made by the code a game uses: the board and the table under it, every built-in terrain mesh dressed in its grains, a
terrain piece's hitboxes, models in each look (hitbox, ring, highlight, warning, ghost) wearing a miniature of each kind of glTF
material a player's file brings (`WarmupGlb`: the web app's STL conversion, a base-colour texture, textured PBR with normal and
occlusion maps, vertex colour, blended, cut out; imported by Godot's glTF import as a player's file is), markers, a zone, a move
area, a ruler, lines, a laser glow, the Shaken ring, the 3D labels and the Hud controls the table's viewport draws. It applies each
Quality in turn, the player's first, and draws until the renderer's pipeline compilations have stood still for 4 frames.

It runs at start, before the first screen, when `GraphicsWarmup.Stamp` (Godot's version, the rendering method and driver, the GPU's
name, its API and driver version, the client's version) differs from `<user dir>/graphics-warmup.txt`; Godot's and the driver's
caches keep what compiled, so later starts skip it. A development build runs it again with `TABLETOP_WARMUP=1`. The built-in meshes'
templates (`MeshLoader`, shared by every game) and the prints it loads stay for the game.

## Entering a game

The frame that applied the welcome, the board, the whole log and the first render is spread over frames:

- `GodotDispatcher` runs posted work in order within an 8 ms budget a frame (`DispatchQueue`, `FrameBudget`), leaving a 60 Hz
  frame room for its own process and draw; what is left runs in the next frame. One action is never split.
- A board's print and the table's wood decode, take their normal maps and mipmaps on the thread pool (`PrintTextures.LoadAsync`);
  the slab drawn before stays until the new one is built. A built-in mesh's grains load the same way before the mesh is handed out
  (`TerrainLook.ApplyAsync`).
- A mesh file is parsed, given its levels of detail and generated into its template scene on the thread pool (`MeshLoader`); the main
  thread only duplicates the template per piece. A terrain piece's hitboxes share one unit box and one unit cylinder, scaled.
- The curtain stays until the table's meshes are in (`LoadSettled`, at most 20 s after the table stood). Behind it the unit card makes
  its controls, the move warm-up runs and the loading's garbage is collected, and two more frames draw the new meshes, so their first
  draw is not paid after it lifts.

## Measured

Release export, the reference game of the performance spec entered at its end (2,493 events, 150 orc models, the Ruins table),
Windows, Vulkan, Medium, 1280×720, RX 6900 XT. Cold: a new export directory (the driver's cache cold) and Godot's user directory
cleared.

| | Before | After |
| --- | --- | --- |
| First start, cold | in game: frames of 1.81 s and 1.67 s with the table shown | warm-up 7.3-7.5 s behind "Preparing graphics…" (its longest frame 2.0 s); entering, behind "Loading game…": one frame of 0.7-1.0 s, then none over 100 ms |
| Entering, warm caches | one frame of 0.6-1.1 s, one of 0.26-0.42 s | longest 0.17-0.20 s, behind the curtain |
| A later start | no warm-up | no warm-up (the stamp matches); forced, 2.1 s |

The cold entry's remaining frame happens once, on the first game after the warm-up, at the first Hud control the play screen adds;
with warm caches the same step takes 0.1 s.

The Compatibility renderer (Linux under WSL) draws the warm-up's compiles in frames of 1.8-4.7 s, all behind the curtain.
