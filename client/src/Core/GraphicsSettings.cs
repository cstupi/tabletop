namespace Tabletop.Client.Core;

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

    public static GraphicsProfile Profile(GraphicsQuality quality, int? renderScale)
    {
        var scale = ClampScale(renderScale) ?? DefaultScale(quality);
        return quality switch
        {
            GraphicsQuality.Low => new GraphicsProfile(0, 2, 2048, 120f, scale),
            GraphicsQuality.High => new GraphicsProfile(4, 4, 8192, 220f, scale),
            _ => new GraphicsProfile(2, 4, 4096, 220f, scale),
        };
    }

    public static int? ClampScale(int? percent) => percent is { } p ? Math.Clamp((int)Math.Round(p / (double)ScaleStep, MidpointRounding.AwayFromZero) * ScaleStep, MinScale, MaxScale) : null;

    public static WindowRect? ClampWindow(WindowRect? r) => r is { Width: >= MinWindowWidth, Height: >= MinWindowHeight } ? r : null;

    /// <summary>The saved rect when some screen (usable rects) shows at least 100×50 px of it; otherwise none, and the window opens where Godot puts it.</summary>
    public static WindowRect? Place(WindowRect? saved, IReadOnlyList<WindowRect> screens)
    {
        if (saved is not { } r) return null;
        foreach (var s in screens)
        {
            var w = Math.Min(r.X + r.Width, s.X + s.Width) - Math.Max(r.X, s.X);
            var h = Math.Min(r.Y + r.Height, s.Y + s.Height) - Math.Max(r.Y, s.Y);
            if (w >= MinVisibleWidth && h >= MinVisibleHeight) return r;
        }
        return null;
    }
}
