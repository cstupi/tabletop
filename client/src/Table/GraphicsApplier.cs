using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Table;

/// <summary>Sets Settings → Graphics on Godot: the window (mode, VSync, windowed rect) and the global shadow atlas from Main, the
/// table's SubViewport, environment and key light from TableView. Callers apply on every SettingsChanged; each setter skips an unchanged value.</summary>
public static class GraphicsApplier
{
    static int _atlas;   // the directional shadow atlas size last set; 0 before the first

    /// <summary>VSync, then the mode. <paramref name="start"/> restores the saved rect when windowed; leaving Fullscreen restores it too, deferred so the OS's own restore lands first.</summary>
    public static void ApplyWindow(Window window, ClientSettings s, bool start)
    {
        var vsync = s.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled;
        if (DisplayServer.WindowGetVsyncMode() != vsync) DisplayServer.WindowSetVsyncMode(vsync);
        if (Engine.MaxFps != s.FpsCap) Engine.MaxFps = s.FpsCap;
        var full = window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;
        var mode = s.Display == DisplayMode.Exclusive ? Window.ModeEnum.ExclusiveFullscreen : Window.ModeEnum.Fullscreen;
        if (s.Display != DisplayMode.Windowed) { if (window.Mode != mode) window.Mode = mode; }
        else if (full)
        {
            window.Mode = Window.ModeEnum.Windowed;
            Callable.From(() => Restore(window, s.Window)).CallDeferred();
        }
        else if (start) Restore(window, s.Window);
    }

    static void Restore(Window window, WindowRect? saved)
    {
        if (GraphicsSettings.Place(saved, Screens()) is not { } r) return;
        window.Size = new Vector2I(r.Width, r.Height);
        window.Position = new Vector2I(r.X, r.Y);
    }

    /// <summary>Options → Video → Resolution: the window at the rect's size and place, when windowed.</summary>
    public static void Resize(Window window, WindowRect r)
    {
        if (window.Mode != Window.ModeEnum.Windowed) return;
        window.Size = new Vector2I(r.Width, r.Height);
        window.Position = new Vector2I(r.X, r.Y);
    }

    /// <summary>The rect of the screen the window is on: its position and native size.</summary>
    public static WindowRect ScreenOf(Window window)
    {
        var at = DisplayServer.ScreenGetPosition(window.CurrentScreen); var size = DisplayServer.ScreenGetSize(window.CurrentScreen);
        return new WindowRect(at.X, at.Y, size.X, size.Y);
    }

    public static void ApplyAtlas(GraphicsProfile p)
    {
        if (p.ShadowAtlasSize == _atlas) return;
        RenderingServer.DirectionalShadowAtlasSetSize(p.ShadowAtlasSize, true);
        _atlas = p.ShadowAtlasSize;
    }

    public static void ApplyViewport(Viewport viewport, GraphicsProfile p)
    {
        viewport.Msaa3D = p.MsaaSamples switch { 4 => Viewport.Msaa.Msaa4X, 2 => Viewport.Msaa.Msaa2X, _ => Viewport.Msaa.Disabled };
        viewport.Scaling3DMode = p.Upscaled ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = p.RenderScale;
        viewport.MeshLodThreshold = p.LodThreshold;
    }

    public static void ApplyEnvironment(Godot.Environment environment, GraphicsProfile p) => environment.SsaoEnabled = p.Ssao;

    public static void ApplyKeyLight(DirectionalLight3D key, GraphicsProfile p)
    {
        key.DirectionalShadowMode = p.ShadowSplits == 2 ? DirectionalLight3D.ShadowMode.Parallel2Splits : DirectionalLight3D.ShadowMode.Parallel4Splits;
        key.DirectionalShadowMaxDistance = p.ShadowMaxDistance;
        key.LightAngularDistance = p.SoftShadows ? TableLighting.KeyAngularDistance : 0;
    }

    /// <summary>The GPU the renderer runs on, for the first start's Quality.</summary>
    public static GpuKind Gpu() => RenderingServer.GetVideoAdapterType() switch
    {
        RenderingDevice.DeviceType.IntegratedGpu => GpuKind.Integrated,
        RenderingDevice.DeviceType.DiscreteGpu => GpuKind.Discrete,
        RenderingDevice.DeviceType.VirtualGpu => GpuKind.Virtual,
        RenderingDevice.DeviceType.Cpu => GpuKind.Cpu,
        _ => GpuKind.Other,
    };

    /// <summary>The usable rect of each screen, in screen pixels.</summary>
    static IReadOnlyList<WindowRect> Screens() => Enumerable.Range(0, DisplayServer.GetScreenCount()).Select(i => DisplayServer.ScreenGetUsableRect(i))
        .Select(r => new WindowRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y)).ToList();

    public static WindowRect RectOf(Window window) => new(window.Position.X, window.Position.Y, window.Size.X, window.Size.Y);
}
