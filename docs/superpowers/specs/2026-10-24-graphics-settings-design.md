# Graphics settings

Owner, 2026-10-24: *"Do we need to add high and low res options and resolution settings?"* — then *"Build it."*

The client has none: it opens in Godot's default 1152×648 window with no fullscreen, and renders the table's `SubViewport`
with no MSAA (`project.godot`'s `msaa_3d=2` reaches only the root window, which draws no 3D) and four-split directional
shadows to 220" (`TableLighting`).

## Settings

The Settings dialog gains a **Graphics** group, kept in `ClientSettings` and applied at start and on change:

| Setting | Values | Default |
| --- | --- | --- |
| Display | Windowed, Fullscreen | Windowed |
| Quality | Low, Medium, High | Low on an integrated, virtual or software GPU, else Medium |
| Render scale | 50–100 %, step 5 | the Quality's |
| VSync | on, off | on |

- **Display.** Windowed keeps the window size and position the player left it at (saved on change and on quit); the first
  start opens 1280×720 centred. Fullscreen is borderless at the monitor's own resolution. No resolution list.
- **Quality** sets, together:

  | | Low | Medium | High |
  | --- | --- | --- | --- |
  | MSAA 3D | off | 2× | 4× |
  | Shadows | 2 splits, atlas 2048, max distance 120", hard | 4 splits, atlas 4096, 220", soft | 4 splits, atlas 8192, 220", soft |
  | SSAO | off | on | on |
  | Mesh LOD threshold | 4 px | 2 px | 1 px |
  | Uploaded model detail | at most about 40,000 triangles | full | full |
  | Render scale | 75 % | 100 % | 100 % |

  Soft shadows are the key light's angular size (1°, contact-hardened); hard is 0°. The LOD threshold is the viewport's `mesh_lod_threshold`: how
  many pixels of error a mesh's simplified level may show (meshes get their levels at load, `MeshLoader`). On Low an uploaded model over 40,000
  triangles loads with its first level within that as its base, the finer levels and their vertices freed (`LowDetail`, `LowDetailMesh`), so a
  close-up draws it too; it applies to models loaded after the change, built-in terrain excepted. Each feature's measured cost is in
  [2026-10-25-performance-and-network.md](2026-10-25-performance-and-network.md) → The Godot client. Moving the Render scale slider keeps the Quality
  and overrides only its scale; picking a Quality resets the scale to that Quality's.
- **First start.** With no Quality in `settings.json` the client picks one from the GPU the renderer runs on
  (`GraphicsSettings.FirstQuality` over `RenderingServer.GetVideoAdapterType`) and saves it: Low on an integrated, virtual or software GPU, Medium
  otherwise. The player's choice stands from then on.
- **Render scale** is the 3D viewport's `Scaling3DScale` with FSR 1 upscaling below 100 %; the Hud renders at full resolution.
- **VSync** is the window's VSync mode, Enabled or Disabled.

## Structure

`Core/GraphicsSettings` (Godot-free, tested) maps a Quality and an override to the values above and clamps a loaded file's
values. A Godot-side applier in `src/Screens/` or `src/Table/` sets the window, the viewport and `TableLighting` from it.
`ClientSettings` loads old files without these keys at the defaults.
