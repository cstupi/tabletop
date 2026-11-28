using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The Options screen's categories, in rail order (options-screen spec → Categories and options).</summary>
public enum OptionCategory { Gameplay, Camera, Controls, Video, Audio, Interface, Accessibility }

public enum OptionKind { Toggle, Choice, Slider, Themes }

/// <summary>A window or screen size in pixels.</summary>
public readonly record struct Resolution(int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height}";
}

/// <summary>One row of a category: its label and description, the control it draws, and its value as an int read from and written to
/// a ClientSettings (a toggle 0 or 1, a choice its index, a slider its number).</summary>
public sealed record OptionRow(string Id, OptionCategory Category, string Label, string Description, OptionKind Kind,
    Func<ClientSettings, int> Get, Func<ClientSettings, int, ClientSettings> Set)
{
    public IReadOnlyList<string> Choices { get; init; } = [];
    public int Min { get; init; }
    public int Max { get; init; } = 1;
    public int Step { get; init; } = 1;
    /// <summary>A slider's value as shown beside it.</summary>
    public Func<int, string> Format { get; init; } = v => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The Options screen's state: changes are pending until Apply, Revert drops them, Reset tab returns one category to its defaults.
/// The Resolution row's choices depend on the screen (<see cref="Screen"/>): the common sizes that fit it, its own size as native,
/// and Custom for a window sized by hand; it is disabled outside a window.
/// </summary>
public sealed class OptionsModel
{
    public ClientSettings Applied { get; private set; }
    public ClientSettings Pending { get; private set; }
    public bool Dirty => !Pending.Equals(Applied);
    /// <summary>The screen the window is on (its usable rect), set by the screen that draws the model; none in tests that do not care.</summary>
    public WindowRect? Screen { get; set; }

    public OptionsModel(ClientSettings applied) { Applied = applied; Pending = applied; }

    /// <summary>The settings as the window stands: windowed, its rect now (the saved one may lag a resize by up to a second, or a start the OS
    /// clamped), so the Resolution row reads the real size; outside a window the settings as saved.</summary>
    public static ClientSettings WithWindow(ClientSettings s, WindowRect? window) =>
        s.Display == DisplayMode.Windowed && window is { } w ? s with { Window = w } : s;

    public static IReadOnlyList<OptionCategory> Categories { get; } = Enum.GetValues<OptionCategory>();

    public static string Title(OptionCategory c) => c.ToString();

    /// <summary>A theme icon key per category.</summary>
    public static string Icon(OptionCategory c) => c switch
    {
        OptionCategory.Gameplay => "dice", OptionCategory.Camera => "camera", OptionCategory.Controls => "key", OptionCategory.Video => "display",
        OptionCategory.Audio => "sound", OptionCategory.Interface => "settings", _ => "accessibility",
    };

    public IEnumerable<OptionRow> In(OptionCategory c) => Rows.Where(r => r.Category == c);

    public int Value(OptionRow row) => row.Id == ResolutionId ? ResolutionIndex(Pending) : row.Get(Pending);

    public void Set(OptionRow row, int value)
    {
        if (row.Id == ResolutionId) { SetResolution(value); return; }
        Pending = row.Set(Pending, Math.Clamp(value, row.Min, row.Kind is OptionKind.Choice or OptionKind.Themes ? Math.Max(0, row.Choices.Count - 1) : row.Max));
    }

    public IReadOnlyList<string> Choices(OptionRow row) => row.Id == ResolutionId ? ResolutionLabels() : row.Choices;

    /// <summary>Only the Resolution row is ever disabled: outside a window the screen's size is the resolution.</summary>
    public bool Enabled(OptionRow row) => row.Id != ResolutionId || Pending.Display == DisplayMode.Windowed;

    public string Description(OptionRow row) => row.Id == ResolutionId && !Enabled(row)
        ? "Fullscreen uses the screen's size. Render scale sets the 3D resolution." : row.Description;

    public void Revert() => Pending = Applied;

    public ClientSettings Apply() => Applied = Pending;

    /// <summary>Every row of the category back to its default; Controls drops every key override.</summary>
    public void ResetTab(OptionCategory c)
    {
        if (c == OptionCategory.Controls) { Pending = Pending with { KeyOverrides = new Dictionary<Intent, IReadOnlyList<KeyBinding>>() }; return; }
        foreach (var row in In(c))
            if (row.Id != ResolutionId) Pending = row.Set(Pending, row.Get(Defaults));
    }

    /// <summary>A change the player confirms within 15 s or it reverts: the display mode or the window's size.</summary>
    public static bool NeedsConfirm(ClientSettings before, ClientSettings after) =>
        before.Display != after.Display || SizeOf(before.Window) != SizeOf(after.Window);

    static Resolution? SizeOf(WindowRect? r) => r is { } w ? new(w.Width, w.Height) : null;

    // Controls

    public KeyBindings Keys => Pending.Keys;

    /// <summary>The other intent a binding would take a key from, and its slot.</summary>
    public (Intent Intent, int Slot)? Conflict(Intent intent, KeyBinding binding) => Keys.Conflict(intent, binding);

    /// <summary>Binds the slot. A conflicting binding elsewhere is replaced by this slot's old key when swapping, else dropped.</summary>
    public void Bind(Intent intent, int slot, KeyBinding binding, bool swap)
    {
        if (!KeyBindings.Rebindable(intent)) return;
        var keys = Keys;
        var old = slot < keys.Of(intent).Count ? keys.Of(intent)[slot] : null;
        if (keys.Conflict(intent, binding) is var (other, otherSlot))
        {
            if (!KeyBindings.Rebindable(other)) return;
            keys = keys.With(other, otherSlot, swap ? old : null);
        }
        Pending = Pending with { KeyOverrides = keys.With(intent, slot, binding).Overrides };
    }

    /// <summary>Removes the slot's binding.</summary>
    public void Unbind(Intent intent, int slot)
    {
        if (KeyBindings.Rebindable(intent)) Pending = Pending with { KeyOverrides = Keys.With(intent, slot, null).Overrides };
    }

    // Resolution

    public const string ResolutionId = "resolution";

    /// <summary>The common window sizes, 16:9 then 16:10.</summary>
    public static IReadOnlyList<Resolution> Resolutions { get; } =
    [
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160),
        new(1280, 800), new(1440, 900), new(1680, 1050), new(1920, 1200), new(2560, 1600),
    ];

    /// <summary>The sizes that fit the screen, smallest first, then the screen's own size unless it is one of them; all of them with no screen.</summary>
    public static IReadOnlyList<Resolution> Fitting(Resolution? screen)
    {
        if (screen is not { } s) return Resolutions;
        var fit = Resolutions.Where(r => r.Width <= s.Width && r.Height <= s.Height).OrderBy(r => r.Width).ThenBy(r => r.Height).ToList();
        if (!fit.Contains(s)) fit.Add(s);
        return fit;
    }

    Resolution? ScreenSize => Screen is { } s ? new Resolution(s.Width, s.Height) : null;

    IReadOnlyList<Resolution> Sizes => Fitting(ScreenSize);

    IReadOnlyList<string> ResolutionLabels()
    {
        var labels = Sizes.Select(r => r == ScreenSize ? $"{r} (native)" : r.ToString()).ToList();
        if (Pending.Display == DisplayMode.Windowed && ResolutionIndex(Pending) == labels.Count) labels.Add(Pending.Window is { } w ? $"Custom ({w.Width}×{w.Height})" : "Custom");
        return labels;
    }

    /// <summary>The window's size in the list; outside a window the native size; past the list (Custom) for a size set by hand.</summary>
    int ResolutionIndex(ClientSettings s)
    {
        var sizes = Sizes;
        if (s.Display != DisplayMode.Windowed) return ScreenSize is { } n ? Math.Max(0, IndexOf(sizes, n)) : 0;
        return SizeOf(s.Window) is { } size && IndexOf(sizes, size) is >= 0 and var i ? i : sizes.Count;
    }

    static int IndexOf(IReadOnlyList<Resolution> list, Resolution r) { for (var i = 0; i < list.Count; i++) if (list[i] == r) return i; return -1; }

    /// <summary>A listed size, centred on the screen.</summary>
    void SetResolution(int index)
    {
        if (!Enabled(Rows.First(r => r.Id == ResolutionId)) || index < 0 || index >= Sizes.Count) return;
        var size = Sizes[index];
        var screen = Screen ?? new WindowRect(0, 0, size.Width, size.Height);
        Pending = Pending with { Window = new WindowRect(screen.X + (screen.Width - size.Width) / 2, screen.Y + (screen.Height - size.Height) / 2, size.Width, size.Height) };
    }

    // Rows

    static ClientSettings Defaults => ClientSettings.Default;

    static OptionRow Toggle(string id, OptionCategory c, string label, string description, Func<ClientSettings, bool> get, Func<ClientSettings, bool, ClientSettings> set) =>
        new(id, c, label, description, OptionKind.Toggle, s => get(s) ? 1 : 0, (s, v) => set(s, v != 0));

    static OptionRow Choice(string id, OptionCategory c, string label, string description, IReadOnlyList<string> choices, Func<ClientSettings, int> get, Func<ClientSettings, int, ClientSettings> set) =>
        new(id, c, label, description, OptionKind.Choice, get, set) { Choices = choices, Max = choices.Count - 1 };

    static OptionRow Slider(string id, OptionCategory c, string label, string description, int min, int max, int step, Func<int, string> format, Func<ClientSettings, int> get, Func<ClientSettings, int, ClientSettings> set) =>
        new(id, c, label, description, OptionKind.Slider, get, set) { Min = min, Max = max, Step = step, Format = format };

    static string Percent(int v) => $"{v} %";
    static string Seconds(int ms) => $"{ms / 1000.0:0.00} s";

    const OptionCategory G = OptionCategory.Gameplay, C = OptionCategory.Camera, V = OptionCategory.Video, A = OptionCategory.Audio, I = OptionCategory.Interface, X = OptionCategory.Accessibility;

    public static IReadOnlyList<OptionRow> Rows { get; } =
    [
        Toggle("animate", G, "Animate moves", "Models slide to where they move. Off: they jump. Default on.", s => s.Animate, (s, v) => s with { Animate = v }),
        Toggle("turn-banner", G, "Turn banner", "A banner names the team whose turn starts. Default on.", s => s.TurnBanner, (s, v) => s with { TurnBanner = v }),
        Toggle("range-rings", G, "Range rings at start", "The selected unit's weapon ranges show when a game opens. The Range rings key shows or hides them in play. Default on.", s => s.RangeRings, (s, v) => s with { RangeRings = v }),
        Choice("measure-mode", G, "Measure mode", "The ruler measures flat across the table (2D) or the straight line between points (3D). The Ruler 2D or 3D key switches it. Default 2D.",
            ["2D", "3D"], s => (int)s.MeasureMode, (s, v) => s with { MeasureMode = (MeasureMode)v }),
        Toggle("hitboxes", G, "Model hitboxes", "A coloured box over each model's mesh. The Model hitboxes key switches it. Default on.", s => s.ShowHitboxes, (s, v) => s with { ShowHitboxes = v }),
        Toggle("terrain-hitboxes", G, "Terrain hitboxes", "Terrain volumes drawn faintly over their meshes. The Terrain hitboxes key switches it. Default on.", s => s.ShowTerrainHitboxes, (s, v) => s with { ShowTerrainHitboxes = v }),
        Toggle("wound-numbers", G, "Wound numbers", "A red -1 rises over a model for each wound it takes. Default on.", s => s.WoundNumbers, (s, v) => s with { WoundNumbers = v }),
        Toggle("log-open", G, "Combat log open at start", "The combat log is open when a game opens. Default off.", s => s.LogOpen, (s, v) => s with { LogOpen = v }),
        Toggle("log-detailed", G, "Detailed log", "Each roll in the log shows its dice, modifiers and thresholds. Default off.", s => s.LogDetailed, (s, v) => s with { LogDetailed = v }),

        Toggle("camera-follow", C, "Camera follows", "The camera moves to the unit that acts, unless you moved it in the last few seconds. Default on.", s => s.CameraFollow, (s, v) => s with { CameraFollow = v }),
        Slider("pan-speed", C, "Pan speed", "How fast the camera keys and the window edge move the camera. Default 100 %.", ClientSettings.MinSpeed, ClientSettings.MaxSpeed, 10, Percent, s => s.PanSpeed, (s, v) => s with { PanSpeed = v }),
        Slider("orbit-speed", C, "Orbit speed", "How fast the orbit keys and right-drag turn the camera. Default 100 %.", ClientSettings.MinSpeed, ClientSettings.MaxSpeed, 10, Percent, s => s.OrbitSpeed, (s, v) => s with { OrbitSpeed = v }),
        Slider("zoom-speed", C, "Zoom speed", "How far each wheel step zooms. Default 100 %.", ClientSettings.MinSpeed, ClientSettings.MaxSpeed, 10, Percent, s => s.ZoomSpeed, (s, v) => s with { ZoomSpeed = v }),
        Toggle("edge-scroll", C, "Edge scrolling", "The pointer at the window's edge pans the camera. Default off.", s => s.EdgeScroll, (s, v) => s with { EdgeScroll = v }),
        Toggle("invert-orbit", C, "Invert orbit", "The orbit keys and right-drag turn the camera the other way. Default off.", s => s.InvertOrbit, (s, v) => s with { InvertOrbit = v }),
        Toggle("invert-zoom", C, "Invert zoom", "The wheel zooms the other way. Default off.", s => s.InvertZoom, (s, v) => s with { InvertZoom = v }),

        Choice("display", V, "Display", "A window, a borderless window over the whole screen, or exclusive fullscreen. Default windowed.",
            ["Windowed", "Borderless", "Fullscreen"], s => (int)s.Display, (s, v) => s with { Display = (DisplayMode)v }),
        new(ResolutionId, V, "Resolution", "The window's size, centred on the screen.", OptionKind.Choice, s => 0, (s, _) => s),
        Choice("quality", V, "Quality preset", "Antialiasing, shadows, ambient occlusion, model detail and the default render scale together. Picking one resets Render scale.",
            Enum.GetNames<GraphicsQuality>(), s => (int)s.Quality, (s, v) => s with { Quality = (GraphicsQuality)v, RenderScale = null }),
        Slider("render-scale", V, "Render scale", "The table's 3D resolution as a share of the window's; below 100 % it is upscaled. The interface stays sharp.",
            GraphicsSettings.MinScale, GraphicsSettings.MaxScale, GraphicsSettings.ScaleStep, Percent, s => s.Graphics.RenderScalePercent,
            (s, v) => s with { RenderScale = v == GraphicsSettings.DefaultScale(s.Quality) ? null : v }),
        Toggle("vsync", V, "VSync", "Frames wait for the screen's refresh. Default on.", s => s.VSync, (s, v) => s with { VSync = v }),
        Choice("fps-cap", V, "Frame rate limit", "The most frames a second the client draws. Default unlimited.",
            ClientSettings.FpsCaps.Select(c => c == 0 ? "Unlimited" : $"{c} fps").ToList(), s => Math.Max(0, Array.IndexOf(ClientSettings.FpsCaps, s.FpsCap)), (s, v) => s with { FpsCap = ClientSettings.FpsCaps[v] }),
        Toggle("show-fps", V, "Show frame rate", "Frames a second in the window's top-right corner. Default off.", s => s.ShowFps, (s, v) => s with { ShowFps = v }),

        Toggle("sound", A, "Sound", "Table sound effects. Default on.", s => s.Sound, (s, v) => s with { Sound = v }),
        Slider("master-volume", A, "Master volume", "Every sound. Default 70 %.", 0, 100, 5, Percent, s => s.Volume, (s, v) => s with { Volume = v }),
        Slider("effects-volume", A, "Effects volume", "Shots, hits, wounds, spells and moves on the table. Default 100 %.", 0, 100, 5, Percent, s => s.EffectsVolume, (s, v) => s with { EffectsVolume = v }),
        Toggle("mute-background", A, "Mute in background", "No sound while another window has focus. Default off.", s => s.MuteInBackground, (s, v) => s with { MuteInBackground = v }),

        new("theme", I, "Theme", "Colours, fonts and panels of the whole client.", OptionKind.Themes, s => Math.Max(0, UiThemes.All.ToList().IndexOf(UiThemes.Resolve(s.Theme))),
            (s, v) => s with { Theme = UiThemes.All[v].Id }) { Choices = UiThemes.All.Select(t => t.Name).ToList(), Max = UiThemes.All.Count - 1 },
        Slider("ui-scale", I, "UI scale", "The size of every panel, button and text. Default 100 %.", ClientSettings.MinUiScale, ClientSettings.MaxUiScale, 5, Percent, s => s.UiScale, (s, v) => s with { UiScale = v }),
        Slider("tooltip-delay", I, "Tooltip delay", "How long the pointer rests before a tooltip shows. Default 0.15 s.", 0, 1000, 50, Seconds, s => s.TooltipMs, (s, v) => s with { TooltipMs = v }),
        Slider("toast-duration", I, "Toast duration", "How long a message stays in the top right. Default 4 s.", 2, 10, 1, v => $"{v} s", s => s.ToastSeconds, (s, v) => s with { ToastSeconds = v }),

        Toggle("colour-blind", X, "Colour-blind team colours", "Team colours that stay distinct with deuteranopia, protanopia and tritanopia. Default off.", s => s.ColourBlind, (s, v) => s with { ColourBlind = v }),
        Choice("text-size", X, "Text size", "The size of every font. Default 100 %.", ClientSettings.TextSizes.Select(Percent).ToList(),
            s => Math.Max(0, Array.IndexOf(ClientSettings.TextSizes, s.TextSize)), (s, v) => s with { TextSize = ClientSettings.TextSizes[v] }),
        Toggle("reduce-motion", X, "Reduce motion", "The camera jumps to where it goes instead of gliding. Default off.", s => s.ReduceMotion, (s, v) => s with { ReduceMotion = v }),
    ];
}
