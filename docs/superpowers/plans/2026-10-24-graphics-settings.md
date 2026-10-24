# Graphics settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Graphics group in the Settings dialog: Display (Windowed, Fullscreen), Quality (Low, Medium, High), Render scale
(50–100 %, step 5) and VSync, kept in `ClientSettings`, applied at start and on change.

**Architecture:** `Core/GraphicsSettings` (Godot-free) maps a `GraphicsQuality` and a Render scale override to a
`GraphicsProfile`, clamps loaded values and decides whether a saved window rect still lies on a screen. `Table/GraphicsApplier`
(Godot) sets the window mode, VSync and the directional shadow atlas (from `Main`), and the table's `SubViewport` and key
light (from `TableView`).

**Tech Stack:** Godot 4.7 C# (`Godot.NET.Sdk/4.7.2`), .NET 10, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-24-graphics-settings-design.md`

## Findings that shape the plan

- **The table is a `SubViewport`.** `GameScreen._Ready` builds `SubViewportContainer { Stretch = true }` → `SubViewport
  { HandleInputLocally = true }` → `TableView`. The Hud is in the root viewport. So `Msaa3D`, `Scaling3DMode` and
  `Scaling3DScale` go on the `SubViewport` (`TableView.GetViewport()`), and the Hud stays at full resolution with no extra work.
- **Today's table has no MSAA.** `rendering/anti_aliasing/quality/msaa_3d=2` in `project.godot` sets only the root window's
  `Msaa3D`; a `SubViewport` made in code starts at `Viewport.Msaa.Disabled`. The root draws no 3D. Medium sets 2× on the
  `SubViewport`, which is the look the spec describes, and the project line goes.
- **Settings reach live nodes through `Session.SettingsChanged`** (raised by `Session.UpdateSettings`, which also persists).
  `Main` re-themes on it; `TableView.Bind` subscribes `RefreshLooks`; `RangeRingNode` redraws. The event fires for every
  setting (Volume's slider included), so the applier skips work when its inputs are unchanged.
- **Shadow atlas.** `rendering/lights_and_shadows/directional_shadow/size` is read once at start; a runtime change goes through
  `RenderingServer.DirectionalShadowAtlasSetSize(int size, bool is16Bits)` (global, every viewport; `is16Bits: true`, the
  project default). Splits and distance are per light: `DirectionalLight3D.DirectionalShadowMode`
  (`ShadowMode.Parallel2Splits` / `Parallel4Splits`) and `DirectionalShadowMaxDistance`.
- **Window.** `Window.Mode = Window.ModeEnum.Fullscreen` is Godot 4's borderless fullscreen at the screen's resolution
  (`ExclusiveFullscreen` is the exclusive one, not used). VSync: `DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode
  .Enabled | Disabled)`. Size and position: `Window.Size`, `Window.Position` (screen pixels), screens through
  `DisplayServer.GetScreenCount()` / `ScreenGetUsableRect(i)`. `Window` has `SizeChanged` but no position-changed signal, so a
  1 s timer in `Main` saves the rect when it differs, and `NotificationWMCloseRequest` saves it on quit.
- **First start 1280×720 centred:** `display/window/size/viewport_width=1280` and `viewport_height=720` in `project.godot`
  (stretch mode stays disabled, so this is the window size only); Godot centres the first window.
- **FSR 1** needs Forward+ or Mobile; the project is Forward+. At 100 % the applier uses `Scaling3DMode.Bilinear` at 1.0,
  which is no scaling.

## Global Constraints

- `client/src/Core/` stays Godot-free (`check-client` fails on `using Godot` there).
- No colour literals in `src/Screens` (`check-client`); headings use `Variation.Accent`.
- Each new public member gets a one-line `<summary>` in the repo's dense style; no change-history prose.
- Commands: `dotnet test tests/Tabletop.Client.Tests`, `dotnet build client/Tabletop.csproj`, `./scripts/check-client`.

---

### Task 1: `Core/GraphicsSettings`

**Files:** create `client/src/Core/GraphicsSettings.cs`; test `tests/Tabletop.Client.Tests/GraphicsSettingsTests.cs`.

- [ ] Write the tests first:

```csharp
public class GraphicsSettingsTests
{
    [Fact] public void Medium_is_todays_shadows_with_msaa_2x() =>
        Assert.Equal(new GraphicsProfile(2, 4, 4096, 220f, 100), GraphicsSettings.Profile(GraphicsQuality.Medium, null));
    [Fact] public void Low_and_high_match_the_spec_table()
    {
        Assert.Equal(new GraphicsProfile(0, 2, 2048, 120f, 75), GraphicsSettings.Profile(GraphicsQuality.Low, null));
        Assert.Equal(new GraphicsProfile(4, 4, 8192, 220f, 100), GraphicsSettings.Profile(GraphicsQuality.High, null));
    }
    [Fact] public void An_override_replaces_only_the_scale() =>
        Assert.Equal(GraphicsSettings.Profile(GraphicsQuality.High, null) with { RenderScalePercent = 60 }, GraphicsSettings.Profile(GraphicsQuality.High, 60));
    [Fact] public void Below_100_percent_upscales() =>
        Assert.Equal((true, 0.75f, false), (GraphicsSettings.Profile(GraphicsQuality.Low, null).Upscaled, GraphicsSettings.Profile(GraphicsQuality.Low, null).RenderScale, GraphicsSettings.Profile(GraphicsQuality.Medium, null).Upscaled));
    [Theory, InlineData(null, null), InlineData(40, 50), InlineData(120, 100), InlineData(52, 50), InlineData(53, 55), InlineData(75, 75)]
    public void The_scale_clamps_and_snaps_to_steps_of_five(int? given, int? expected) => Assert.Equal(expected, GraphicsSettings.ClampScale(given));
    [Fact] public void A_window_under_640_by_360_reads_as_none()
    {
        Assert.Null(GraphicsSettings.ClampWindow(new WindowRect(0, 0, 639, 720)));
        Assert.Null(GraphicsSettings.ClampWindow(new WindowRect(0, 0, 1280, 359)));
        Assert.Equal(new WindowRect(10, 20, 640, 360), GraphicsSettings.ClampWindow(new WindowRect(10, 20, 640, 360)));
        Assert.Null(GraphicsSettings.ClampWindow(null));
    }
    static readonly WindowRect[] TwoScreens = [new(0, 0, 1920, 1040), new(1920, 0, 2560, 1400)];
    [Fact] public void A_saved_window_on_a_screen_is_kept()
    {
        Assert.Equal(new WindowRect(100, 80, 1280, 720), GraphicsSettings.Place(new WindowRect(100, 80, 1280, 720), TwoScreens));
        Assert.Equal(new WindowRect(2200, 100, 1600, 900), GraphicsSettings.Place(new WindowRect(2200, 100, 1600, 900), TwoScreens));
    }
    [Fact] public void A_saved_window_off_every_screen_or_absent_is_none()
    {
        Assert.Null(GraphicsSettings.Place(new WindowRect(5000, 100, 1280, 720), TwoScreens));   // a monitor since unplugged
        Assert.Null(GraphicsSettings.Place(new WindowRect(-1260, 100, 1280, 720), TwoScreens));  // 20 px showing
        Assert.Null(GraphicsSettings.Place(null, TwoScreens));
    }
}
```

- [ ] Run `dotnet test tests/Tabletop.Client.Tests --filter GraphicsSettingsTests`: fails to compile.
- [ ] Implement `client/src/Core/GraphicsSettings.cs` (namespace `Tabletop.Client.Core`):

```csharp
/// <summary>Windowed keeps the player's window rect; Fullscreen is borderless at the screen's resolution.</summary>
public enum DisplayMode { Windowed, Fullscreen }
/// <summary>The Settings dialog's Quality: MSAA, shadows and the default Render scale together (spec 2026-10-24-graphics-settings-design.md).</summary>
public enum GraphicsQuality { Low, Medium, High }
/// <summary>What a Quality and a Render scale override set: the table viewport's MSAA (samples: 0, 2, 4) and 3D scale, the key light's splits and distance (inches), the directional shadow atlas (px).</summary>
public sealed record GraphicsProfile(int MsaaSamples, int ShadowSplits, int ShadowAtlasSize, float ShadowMaxDistance, int RenderScalePercent)
{
    public float RenderScale => RenderScalePercent / 100f;
    /// <summary>Below 100 % the table renders smaller and FSR 1 upscales it; the Hud is in the root viewport at full resolution.</summary>
    public bool Upscaled => RenderScalePercent < 100;
}
/// <summary>A window's rect in screen pixels, as <c>Window.Position</c> and <c>Window.Size</c> give it.</summary>
public readonly record struct WindowRect(int X, int Y, int Width, int Height);

public static class GraphicsSettings
{
    public const int MinScale = 50, MaxScale = 100, ScaleStep = 5, MinWindowWidth = 640, MinWindowHeight = 360;
    /// <summary>A saved rect is restored only when one screen shows at least this much of it.</summary>
    const int MinVisibleWidth = 100, MinVisibleHeight = 50;
    public static int DefaultScale(GraphicsQuality q) => q == GraphicsQuality.Low ? 75 : 100;
    public static GraphicsProfile Profile(GraphicsQuality quality, int? renderScale) { /* switch on quality per the spec table; RenderScalePercent = ClampScale(renderScale) ?? DefaultScale(quality) */ }
    public static int? ClampScale(int? percent) => percent is { } p ? Math.Clamp((int)Math.Round(p / (double)ScaleStep, MidpointRounding.AwayFromZero) * ScaleStep, MinScale, MaxScale) : null;
    public static WindowRect? ClampWindow(WindowRect? r) => r is { Width: >= MinWindowWidth, Height: >= MinWindowHeight } ? r : null;
    /// <summary>The saved rect when some screen (usable rects) shows at least 100×50 px of it; otherwise none, and the window opens where Godot puts it.</summary>
    public static WindowRect? Place(WindowRect? saved, IReadOnlyList<WindowRect> screens) { /* overlap = min(right edges) − max(left edges), likewise vertically */ }
}
```

- [ ] Run `dotnet test tests/Tabletop.Client.Tests --filter GraphicsSettingsTests`: passes.

### Task 2: `ClientSettings` graphics fields

**Files:** `client/src/Core/ClientSettings.cs`; test `tests/Tabletop.Client.Tests/SettingsTests.cs`.

- [ ] Add the tests to `SettingsTests`:

```csharp
[Fact]
public void Graphics_default_to_windowed_medium_vsync_with_no_override()
{
    var d = ClientSettings.Default;
    Assert.Equal((DisplayMode.Windowed, GraphicsQuality.Medium, (int?)null, true, (WindowRect?)null), (d.Display, d.Quality, d.RenderScale, d.VSync, d.Window));
    Assert.Equal(GraphicsSettings.Profile(GraphicsQuality.Medium, null), d.Graphics);
}

[Fact]
public void Graphics_round_trip_and_each_field_counts_for_equality()
{
    var path = Path.Combine(_dir, "settings.json"); var d = ClientSettings.Default;
    Assert.NotEqual(d, d with { Display = DisplayMode.Fullscreen }); Assert.NotEqual(d, d with { Quality = GraphicsQuality.High });
    Assert.NotEqual(d, d with { RenderScale = 80 }); Assert.NotEqual(d, d with { VSync = false }); Assert.NotEqual(d, d with { Window = new WindowRect(1, 2, 1280, 720) });
    var s = d with { Display = DisplayMode.Fullscreen, Quality = GraphicsQuality.Low, RenderScale = 60, VSync = false, Window = new WindowRect(100, 80, 1600, 900) }; s.Save(path);
    var back = ClientSettings.Load(path);
    Assert.Equal(s, back);
    Assert.Equal(new GraphicsProfile(0, 2, 2048, 120f, 60), back.Graphics);
}

[Fact]
public void An_old_file_loads_graphics_at_the_defaults()
{
    var path = Path.Combine(_dir, "settings.json"); File.WriteAllText(path, """{ "animate": false }"""); var s = ClientSettings.Load(path);
    Assert.Equal((DisplayMode.Windowed, GraphicsQuality.Medium, (int?)null, true, (WindowRect?)null), (s.Display, s.Quality, s.RenderScale, s.VSync, s.Window));
}

[Theory]
[InlineData("""{ "display": "borderless", "quality": "ultra", "renderScale": 30, "window": { "x": 0, "y": 0, "width": 200, "height": 100 } }""", 50)]
[InlineData("""{ "display": "1", "quality": "2", "renderScale": 130 }""", 100)]
[InlineData("""{ "renderScale": 62 }""", 60)]
public void Bad_graphics_values_load_at_defaults_or_clamped(string json, int scale)
{
    var path = Path.Combine(_dir, "settings.json"); File.WriteAllText(path, json); var s = ClientSettings.Load(path);
    Assert.Equal((DisplayMode.Windowed, GraphicsQuality.Medium, (int?)scale, (WindowRect?)null), (s.Display, s.Quality, s.RenderScale, s.Window));
}
```

- [ ] Run `dotnet test tests/Tabletop.Client.Tests --filter SettingsTests`: fails to compile.
- [ ] In `ClientSettings` add, after `Volume`:

```csharp
/// <summary>Windowed or borderless Fullscreen (Settings → Graphics); <see cref="Window"/> is the windowed rect the player left, none until the first save.</summary>
public DisplayMode Display { get; init; }
public WindowRect? Window { get; init; }
/// <summary>The Quality, and the Render scale percent the player set over it: none follows the Quality's (<see cref="GraphicsSettings.DefaultScale"/>); picking a Quality clears it.</summary>
public GraphicsQuality Quality { get; init; } = GraphicsQuality.Medium;
public int? RenderScale { get; init; }
public bool VSync { get; init; } = true;
/// <summary>What the applier sets on the table's viewport, its key light and the shadow atlas.</summary>
public GraphicsProfile Graphics => GraphicsSettings.Profile(Quality, RenderScale);
```

- [ ] Extend `Dto` with trailing optional parameters `string? Display = null, string? Quality = null, int? RenderScale = null,
  bool? VSync = null, WindowRect? Window = null` (JSON keys `display`, `quality`, `renderScale`, `vSync`, `window` with
  `x`/`y`/`width`/`height` under the existing web-case options).
- [ ] Add `static T EnumOr<T>(string? s, T fallback) where T : struct, Enum => Enum.TryParse<T>(s, true, out var v) &&
  Enum.IsDefined(v) && !int.TryParse(s, out _) ? v : fallback;` (the same rule `LogFilter` and `MeasureMode` load with; leave
  those two as they are).
- [ ] In `Load`'s `with { … }` add `Display = EnumOr(d.Display, DisplayMode.Windowed), Quality = EnumOr(d.Quality,
  GraphicsQuality.Medium), RenderScale = GraphicsSettings.ClampScale(d.RenderScale), VSync = d.VSync ?? true, Window =
  GraphicsSettings.ClampWindow(d.Window)`.
- [ ] In `Save` pass `Display.ToString(), Quality.ToString(), RenderScale, VSync, Window` (written in both build kinds).
- [ ] In `Equals` add `&& Display == other.Display && Quality == other.Quality && RenderScale == other.RenderScale && VSync ==
  other.VSync && Window == other.Window`.
- [ ] Run `dotnet test tests/Tabletop.Client.Tests`: all pass.
- [ ] Commit: `Client: graphics settings in ClientSettings, GraphicsSettings profiles`.

### Task 3: `GraphicsApplier`, the table viewport and the key light

**Files:** create `client/src/Table/GraphicsApplier.cs`; modify `client/src/Table/TableLighting.cs`,
`client/src/Table/TableView.cs`, `client/project.godot`.

- [ ] `GraphicsApplier` (namespace `Tabletop.Client.Table`), a static class:

```csharp
/// <summary>Sets Settings → Graphics on Godot: the window (mode, VSync, windowed rect) and the global shadow atlas from Main, the
/// table's SubViewport and key light from TableView. Callers apply on every SettingsChanged; each setter skips an unchanged value.</summary>
public static class GraphicsApplier
{
    static int _atlas;   // the directional shadow atlas size last set; 0 before the first

    /// <summary>VSync, then the mode. <paramref name="start"/> restores the saved rect when windowed; leaving Fullscreen restores it too, deferred so the OS's own restore lands first.</summary>
    public static void ApplyWindow(Window window, ClientSettings s, bool start);
    public static void ApplyAtlas(GraphicsProfile p);
    public static void ApplyViewport(Viewport viewport, GraphicsProfile p);
    public static void ApplyKeyLight(DirectionalLight3D key, GraphicsProfile p);
    /// <summary>The usable rect of each screen, in screen pixels.</summary>
    static IReadOnlyList<WindowRect> Screens();
    public static WindowRect RectOf(Window window) => new(window.Position.X, window.Position.Y, window.Size.X, window.Size.Y);
}
```

  - `ApplyWindow`: `var vsync = s.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled; if
    (DisplayServer.WindowGetVsyncMode() != vsync) DisplayServer.WindowSetVsyncMode(vsync);`. Then `var full = window.Mode is
    Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;` — `Fullscreen` setting and `!full` → `window.Mode =
    Window.ModeEnum.Fullscreen`; `Windowed` setting and `full` → `window.Mode = Window.ModeEnum.Windowed` and
    `Callable.From(() => Restore(window, s.Window)).CallDeferred()`; `Windowed` and `start` → `Restore(window, s.Window)`.
    `Restore`: `if (GraphicsSettings.Place(saved, Screens()) is { } r) { window.Size = new Vector2I(r.Width, r.Height);
    window.Position = new Vector2I(r.X, r.Y); }`.
  - `Screens`: `Enumerable.Range(0, DisplayServer.GetScreenCount()).Select(i => DisplayServer.ScreenGetUsableRect(i)).Select(r
    => new WindowRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y)).ToList()`.
  - `ApplyAtlas`: `if (p.ShadowAtlasSize == _atlas) return; RenderingServer.DirectionalShadowAtlasSetSize(p.ShadowAtlasSize,
    true); _atlas = p.ShadowAtlasSize;`.
  - `ApplyViewport`: `viewport.Msaa3D = p.MsaaSamples switch { 4 => Viewport.Msaa.Msaa4X, 2 => Viewport.Msaa.Msaa2X, _ =>
    Viewport.Msaa.Disabled }; viewport.Scaling3DMode = p.Upscaled ? Viewport.Scaling3DModeEnum.Fsr :
    Viewport.Scaling3DModeEnum.Bilinear; viewport.Scaling3DScale = p.RenderScale;`.
  - `ApplyKeyLight`: `key.DirectionalShadowMode = p.ShadowSplits == 2 ? DirectionalLight3D.ShadowMode.Parallel2Splits :
    DirectionalLight3D.ShadowMode.Parallel4Splits; key.DirectionalShadowMaxDistance = p.ShadowMaxDistance;`.
- [ ] `TableLighting.AddTo(Node3D parent)` returns the key `DirectionalLight3D` (`public static DirectionalLight3D
  AddTo(Node3D parent)`); drop `DirectionalShadowMode` and `DirectionalShadowMaxDistance` from its initializer and add to the
  class summary: "The key light's shadow splits and distance, and the atlas, follow Settings → Graphics (`GraphicsApplier`)."
- [ ] `TableView`: field `DirectionalLight3D _key = null!; GraphicsProfile? _graphics;`. `_Ready`: `_key =
  TableLighting.AddTo(this); ApplyGraphics();`. Add:

```csharp
/// <summary>Settings → Graphics on the table's SubViewport (MSAA, Render scale) and the key light (shadow splits, distance); the Hud is in the root viewport.</summary>
void ApplyGraphics()
{
    var p = Main.Instance.Session.Settings.Graphics;
    if (p == _graphics) return;
    GraphicsApplier.ApplyViewport(GetViewport(), p); GraphicsApplier.ApplyKeyLight(_key, p); _graphics = p;
}
```

  In `Bind`, `_onSettings = () => { RefreshLooks(); ApplyGraphics(); };` (the `_ExitTree` unsubscribe stays as is).
- [ ] `client/project.godot`: delete `anti_aliasing/quality/msaa_3d=2` under `[rendering]` (it set only the root window, which
  draws no 3D); add, between `[application]` and `[dotnet]`,

```ini
[display]

window/size/viewport_width=1280
window/size/viewport_height=720
```

- [ ] Run `dotnet build client/Tabletop.csproj` and `./scripts/check-client`: both clean.

### Task 4: `Main` applies the window and keeps its rect

**Files:** `client/src/Main.cs`.

- [ ] In `_Ready`, after `ApplyTheme();`: `GetWindow().MinSize = new Vector2I(GraphicsSettings.MinWindowWidth,
  GraphicsSettings.MinWindowHeight); ApplyGraphics(start: true);`, and make the `SettingsChanged` handler
  `() => { if (UiThemes.Resolve(Session.Settings.Theme) != Ui.Current) ApplyTheme(); ApplyGraphics(start: false); }`.
- [ ] After the refresh timer, a rect timer: `var rect = new Godot.Timer { WaitTime = 1, Autostart = true }; rect.Timeout +=
  SaveWindowRect; AddChild(rect);`.
- [ ] Add:

```csharp
/// <summary>The window's mode and VSync, and the shadow atlas every viewport shares; the table's own part is TableView's.</summary>
void ApplyGraphics(bool start) { GraphicsApplier.ApplyWindow(GetWindow(), Session.Settings, start); GraphicsApplier.ApplyAtlas(Session.Settings.Graphics); }

/// <summary>Windowed, the window's rect when it moved or resized (Window has no position signal: a 1 s timer and the close request call this). Never a fullscreen or maximized rect.</summary>
void SaveWindowRect()
{
    var w = GetWindow();
    if (w.Mode != Window.ModeEnum.Windowed || Session.Settings.Display != DisplayMode.Windowed) return;
    var r = GraphicsApplier.RectOf(w);
    if (r != Session.Settings.Window) Session.UpdateSettings(s => s with { Window = r });
}

public override void _Notification(int what) { if (what == NotificationWMCloseRequest) SaveWindowRect(); }
```

- [ ] `using Tabletop.Client.Table;` at the top.
- [ ] Run `dotnet build client/Tabletop.csproj` and `./scripts/check-client`.

### Task 5: The Settings dialog's Graphics group

**Files:** `client/src/Screens/SettingsDialog.cs`.

- [ ] After the Volume row, a heading `box.AddChild(new HSeparator()); box.AddChild(new Label { Text = "Graphics",
  ThemeTypeVariation = Variation.Accent });` (`using Tabletop.Client.Theming;`), then:
  - Build the Render scale slider and its label before the Quality handler, which writes to them.
  - **Display:** `OptionButton` with `Windowed`, `Fullscreen` (index = `(int)DisplayMode`); `ItemSelected += i =>
    session.UpdateSettings(s => s with { Display = (DisplayMode)(int)i })`.
  - **Quality:** `OptionButton` with `Low`, `Medium`, `High`; `ItemSelected += i => { session.UpdateSettings(s => s with {
    Quality = (GraphicsQuality)(int)i, RenderScale = null }); scale.SetValueNoSignal(session.Settings.Graphics
    .RenderScalePercent); scaleValue.Text = …; }` (picking a Quality resets the scale; no signal, so the slider does not write
    an override back).
  - **Render scale:** `HSlider { MinValue = GraphicsSettings.MinScale, MaxValue = GraphicsSettings.MaxScale, Step =
    GraphicsSettings.ScaleStep, Value = session.Settings.Graphics.RenderScalePercent, CustomMinimumSize = new Vector2(160, 0),
    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }` and a `Label scaleValue` reading `"{n} %"`; `ValueChanged += v => {
    session.UpdateSettings(s => s with { RenderScale = (int)v }); scaleValue.Text = $"{(int)v} %"; }`.
  - **VSync:** `Check(box, "VSync", session.Settings.VSync, session, (s, on) => s with { VSync = on });`.
- [ ] Each labelled control in an `HBoxContainer` row like Theme and Volume; factor a `static HBoxContainer Row(Container box,
  string label, Control control)` and use it for the existing Theme and Volume rows too.
- [ ] Class summary: "The settings that apply at once: theme, animation, camera follow, turn banner, hitboxes, range rings,
  sound, and Graphics (display, quality, render scale, VSync)."
- [ ] Run `dotnet build client/Tabletop.csproj` and `./scripts/check-client` (no colour literal in `src/Screens`).

### Task 6: Docs, full run, commit

**Files:** `client/AGENTS.md`.

- [ ] Layout → `src/Core/`: after `ClientSettings,` add `GraphicsSettings` (a Quality and a Render scale override to a
  `GraphicsProfile`: MSAA, shadow splits, atlas and distance, scale; clamps loaded values; `Place` keeps a saved window rect
  only while a screen shows it).
- [ ] Layout → `src/Table/`: add `GraphicsApplier` after `TableLighting`.
- [ ] The `settings.json` list: after `Volume` add `Display`, `Quality`, `RenderScale`, `VSync`, `Window`.
- [ ] A bullet beside Sound: "Graphics: `GraphicsApplier` sets the window (borderless Fullscreen or the saved windowed rect,
  `Main` saving it each second it changed and on close; first start 1280×720 from `project.godot`), VSync and the directional
  shadow atlas from `Main`, and the table's `SubViewport` (`Msaa3D`, FSR 1 `Scaling3DScale` below 100 %) and key light (splits,
  max distance) from `TableView`, on `SettingsChanged`. The Hud is in the root viewport at full resolution."
- [ ] Run `dotnet test tests/Tabletop.Client.Tests`, `dotnet build client/Tabletop.csproj`, `./scripts/check-client`: all clean.
- [ ] Commit: `Client: graphics settings — display, quality, render scale, VSync`.
