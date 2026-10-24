# Graphics settings

Owner, 2026-10-24: *"Do we need to add high and low res options and resolution settings?"* — then *"Build it."*

The client has none: it opens in Godot's default 1152×648 window with no fullscreen, and always renders MSAA 2×
(`project.godot`) with four-split directional shadows to 220" (`TableLighting`).

## Settings

The Settings dialog gains a **Graphics** group, kept in `ClientSettings` and applied at start and on change:

| Setting | Values | Default |
| --- | --- | --- |
| Display | Windowed, Fullscreen | Windowed |
| Quality | Low, Medium, High | Medium |
| Render scale | 50–100 %, step 5 | the Quality's |
| VSync | on, off | on |

- **Display.** Windowed keeps the window size and position the player left it at (saved on change and on quit); the first
  start opens 1280×720 centred. Fullscreen is borderless at the monitor's own resolution. No resolution list.
- **Quality** sets, together:

  | | Low | Medium | High |
  | --- | --- | --- | --- |
  | MSAA 3D | off | 2× | 4× |
  | Shadows | 2 splits, atlas 2048, max distance 120" | 4 splits, atlas 4096, 220" (today) | 4 splits, atlas 8192, 220" |
  | Render scale | 75 % | 100 % | 100 % |

  Medium is exactly today's look. Moving the Render scale slider keeps the Quality and overrides only its scale; picking a
  Quality resets the scale to that Quality's.
- **Render scale** is the 3D viewport's `Scaling3DScale` with FSR 1 upscaling below 100 %; the Hud renders at full resolution.
- **VSync** is the window's VSync mode, Enabled or Disabled.

## Structure

`Core/GraphicsSettings` (Godot-free, tested) maps a Quality and an override to the values above and clamps a loaded file's
values. A Godot-side applier in `src/Screens/` or `src/Table/` sets the window, the viewport and `TableLighting` from it.
`ClientSettings` loads old files without these keys at the defaults.
