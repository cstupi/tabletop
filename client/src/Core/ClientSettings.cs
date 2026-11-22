using System.Text.Json;
using System.Text.Json.Serialization;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Which rolls the combat log shows. The order is the filter menu's.</summary>
public enum LogFilter { All, Rolls, Mine, Unit }

/// <summary>Per-player, per-machine settings. Presentation and convenience only; nothing here reaches the server.</summary>
public sealed record ClientSettings(bool Animate, bool RememberMe, string ManagementUrl, string GameServerUrl, LogFilter LogFilter = LogFilter.All, bool LogOpen = false)
{
    public string Theme { get; init; } = UiThemes.DefaultId;
    public bool CameraFollow { get; init; } = true;
    public bool TurnBanner { get; init; } = true;
    /// <summary>The selected unit's weapon range rings (V). On by default; stored as weaponRanges, so an older build's stored rangeRings, false for everyone, is not read.</summary>
    public bool RangeRings { get; init; } = true;
    /// <summary>Whether the combat log draws each roll as a worked resolution (detailed-log spec); off is the normal log.</summary>
    public bool LogDetailed { get; init; }
    /// <summary>Whether a model's coloured hitbox draws over its mesh (B toggles it); off leaves a model's mesh with a thin base ring. See <see cref="HitboxLook"/>.</summary>
    public bool ShowHitboxes { get; init; } = true;
    /// <summary>Whether a terrain piece's volumes draw faintly over its mesh (Shift+B toggles it); off leaves the mesh bare. See <see cref="HitboxLook"/>.</summary>
    public bool ShowTerrainHitboxes { get; init; } = true;
    /// <summary>The free ruler's last mode (H toggles it): Horizontal (2D) unless the player chose Straight (3D).</summary>
    public MeasureMode MeasureMode { get; init; }
    /// <summary>Whether the table plays its sound effects (<see cref="SoundCues"/>), and how loud: 0 to 100.</summary>
    public bool Sound { get; init; } = true;
    public int Volume { get; init; } = 70;
    /// <summary>Windowed or borderless Fullscreen (Settings → Graphics); <see cref="Window"/> is the windowed rect the player left, none until the first save.</summary>
    public DisplayMode Display { get; init; }
    public WindowRect? Window { get; init; }
    /// <summary>The Quality, and the Render scale percent the player set over it: none follows the Quality's (<see cref="GraphicsSettings.DefaultScale"/>); picking a Quality clears it.</summary>
    public GraphicsQuality Quality { get; init; } = GraphicsQuality.Medium;
    public int? RenderScale { get; init; }
    public bool VSync { get; init; } = true;
    /// <summary>The frame-rate limit in fps, one of <see cref="FpsCaps"/>; 0 is unlimited.</summary>
    public int FpsCap { get; init; }
    public static readonly int[] FpsCaps = [0, 30, 60, 120, 144, 240];
    /// <summary>Whether a wound shows a floating -1 over the model (Options → Gameplay).</summary>
    public bool WoundNumbers { get; init; } = true;
    /// <summary>Camera speeds in percent of the built-in speed, <see cref="MinSpeed"/> to <see cref="MaxSpeed"/> (Options → Camera).</summary>
    public int PanSpeed { get; init; } = 100;
    public int OrbitSpeed { get; init; } = 100;
    public int ZoomSpeed { get; init; } = 100;
    public const int MinSpeed = 50, MaxSpeed = 200;
    /// <summary>The pointer at the window's edge pans the camera.</summary>
    public bool EdgeScroll { get; init; }
    /// <summary>Q/E orbit the other way; the wheel zooms the other way.</summary>
    public bool InvertOrbit { get; init; }
    public bool InvertZoom { get; init; }
    /// <summary>The player's key bindings over the defaults (KeyBindings), by intent.</summary>
    public IReadOnlyDictionary<Intent, IReadOnlyList<KeyBinding>> KeyOverrides { get; init; } = new Dictionary<Intent, IReadOnlyList<KeyBinding>>();
    public KeyBindings Keys => KeyBindings.With(KeyOverrides);
    /// <summary>A frame-rate counter in the window's corner.</summary>
    public bool ShowFps { get; init; }
    /// <summary>The Effects and Interface audio buses, 0 to 100; <see cref="Volume"/> is the Master bus.</summary>
    public int EffectsVolume { get; init; } = 100;
    public int InterfaceVolume { get; init; } = 100;
    /// <summary>Silence while the window is in the background.</summary>
    public bool MuteInBackground { get; init; }
    /// <summary>The window's content scale in percent, <see cref="MinUiScale"/> to <see cref="MaxUiScale"/>.</summary>
    public int UiScale { get; init; } = 100;
    public const int MinUiScale = 75, MaxUiScale = 150;
    /// <summary>The tooltip delay in milliseconds, 0 to 1000.</summary>
    public int TooltipMs { get; init; } = 150;
    /// <summary>How long a toast stays, in seconds, 2 to 10.</summary>
    public int ToastSeconds { get; init; } = 4;
    /// <summary>Team colours from the colour-blind palette (UiThemes.ColourBlindTeams).</summary>
    public bool ColourBlind { get; init; }
    /// <summary>The theme's font sizes in percent, one of <see cref="TextSizes"/>.</summary>
    public int TextSize { get; init; } = 100;
    public static readonly int[] TextSizes = [100, 115, 130];
    /// <summary>No camera glides and no banner slides; moves still animate per <see cref="Animate"/>.</summary>
    public bool ReduceMotion { get; init; }
    /// <summary>What the applier sets on the table's viewport (MSAA, scale, LOD threshold), its environment (SSAO), its key light and the shadow atlas.</summary>
    public GraphicsProfile Graphics => GraphicsSettings.Profile(Quality, RenderScale);
    /// <summary>The web app's origin: the update-required screen sends the player to its Download page.</summary>
    public string WebUrl { get; init; } = DevWebUrl;
    public string DownloadUrl => WebUrl.TrimEnd('/') + "/download";

    /// <summary>127.0.0.1, not localhost: Windows resolves localhost to ::1 first, WSL's loopback forwarding carries IPv4 only,
    /// and .NET's connect waits out a 21 s timeout on ::1 before falling back.</summary>
    public static ClientSettings Default { get; } = DefaultFor(BuildInfo.Current);
    const string DevWebUrl = "http://127.0.0.1:5173";

    /// <summary>A release build reaches management and the game socket through its web origin, where the reverse proxy fronts
    /// /v1 and /ws; a development build reaches the local stack.</summary>
    public static ClientSettings DefaultFor(BuildInfo build)
    {
        if (build.WebUrl is not { } web) return new(true, false, "http://127.0.0.1:5100", "ws://127.0.0.1:5200/ws");
        var socket = new UriBuilder(web) { Scheme = web.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws", Path = "/ws" };
        return new(true, false, web, socket.Uri.ToString()) { WebUrl = web };
    }

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    sealed record Dto(bool? Animate, bool? RememberMe, string? ManagementUrl, string? GameServerUrl, string? LogFilter, bool? LogOpen,
        string? Theme = null, bool? CameraFollow = null, bool? TurnBanner = null, bool? RangeRings = null, string? WebUrl = null, string? MeasureMode = null, bool? ShowHitboxes = null, bool? ShowTerrainHitboxes = null, bool? Sound = null, int? Volume = null,
        string? Display = null, string? Quality = null, int? RenderScale = null, bool? VSync = null, WindowRect? Window = null, bool? LogDetailed = null, bool? WeaponRanges = null, int? FpsCap = null,
        bool? WoundNumbers = null, int? PanSpeed = null, int? OrbitSpeed = null, int? ZoomSpeed = null, bool? EdgeScroll = null, bool? InvertOrbit = null, bool? InvertZoom = null,
        Dictionary<string, KeyDto[]>? Keys = null, bool? ShowFps = null, int? EffectsVolume = null, int? InterfaceVolume = null, bool? MuteInBackground = null,
        int? UiScale = null, int? TooltipMs = null, int? ToastSeconds = null, bool? ColourBlind = null, int? TextSize = null, bool? ReduceMotion = null);

    sealed record KeyDto(string Key, bool? Shift = null, bool? Ctrl = null);

    static Dictionary<Intent, IReadOnlyList<KeyBinding>> KeysFrom(Dictionary<string, KeyDto[]>? keys)
    {
        var map = new Dictionary<Intent, IReadOnlyList<KeyBinding>>();
        foreach (var (name, list) in keys ?? [])
        {
            if (!Enum.TryParse<Intent>(name, true, out var intent) || !Enum.IsDefined(intent) || int.TryParse(name, out _) || !KeyBindings.Rebindable(intent)) continue;
            var bindings = (list ?? []).Where(k => k?.Key is not null).Select(k => Enum.TryParse<HotKey>(k.Key, true, out var key) && Enum.IsDefined(key) && !int.TryParse(k.Key, out _) ? new KeyBinding(key, k.Shift, k.Ctrl) : null)
                .OfType<KeyBinding>().ToList();
            if (bindings.Count > 0) map[intent] = bindings;
        }
        return map;
    }

    static Dictionary<string, KeyDto[]>? KeysTo(IReadOnlyDictionary<Intent, IReadOnlyList<KeyBinding>> keys) =>
        keys.Count == 0 ? null : keys.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Select(b => new KeyDto(b.Key.ToString(), b.Shift, b.Ctrl)).ToArray());

    static bool SameKeys(IReadOnlyDictionary<Intent, IReadOnlyList<KeyBinding>> a, IReadOnlyDictionary<Intent, IReadOnlyList<KeyBinding>> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var o) && o.SequenceEqual(kv.Value));

    static T EnumOr<T>(string? s, T fallback) where T : struct, Enum => Enum.TryParse<T>(s, true, out var v) && Enum.IsDefined(v) && !int.TryParse(s, out _) ? v : fallback;

    public static ClientSettings Load(string path) => Load(path, BuildInfo.Current);

    /// <summary>A release build always reaches the servers it was built for: persisted server addresses are a development
    /// build's override, and a release build neither reads nor writes them.</summary>
    public static ClientSettings Load(string path, BuildInfo build)
    {
        var def = DefaultFor(build);
        var d = Read(path);
        if (d is null) return def;
        var release = build.WebUrl is not null;
        return new ClientSettings(d.Animate ?? def.Animate, d.RememberMe ?? def.RememberMe,
            release ? def.ManagementUrl : d.ManagementUrl ?? def.ManagementUrl, release ? def.GameServerUrl : d.GameServerUrl ?? def.GameServerUrl,
            Enum.TryParse<LogFilter>(d.LogFilter, true, out var f) && Enum.IsDefined(f) && !int.TryParse(d.LogFilter, out _) ? f : def.LogFilter, d.LogOpen ?? def.LogOpen)
            with { Theme = UiThemes.Resolve(d.Theme).Id, CameraFollow = d.CameraFollow ?? true, TurnBanner = d.TurnBanner ?? true, RangeRings = d.WeaponRanges ?? true, LogDetailed = d.LogDetailed ?? false, ShowHitboxes = d.ShowHitboxes ?? true, ShowTerrainHitboxes = d.ShowTerrainHitboxes ?? true, Sound = d.Sound ?? true, Volume = Math.Clamp(d.Volume ?? 70, 0, 100),
                MeasureMode = Enum.TryParse<MeasureMode>(d.MeasureMode, true, out var m) && Enum.IsDefined(m) && !int.TryParse(d.MeasureMode, out _) ? m : MeasureMode.Horizontal,
                Display = EnumOr(d.Display, DisplayMode.Windowed), Quality = EnumOr(d.Quality, GraphicsQuality.Medium), RenderScale = GraphicsSettings.ClampScale(d.RenderScale), VSync = d.VSync ?? true, FpsCap = Array.IndexOf(FpsCaps, d.FpsCap ?? 0) >= 0 ? d.FpsCap ?? 0 : 0, Window = GraphicsSettings.ClampWindow(d.Window),
                WebUrl = release ? def.WebUrl : d.WebUrl ?? def.WebUrl,
                WoundNumbers = d.WoundNumbers ?? true, PanSpeed = Speed(d.PanSpeed), OrbitSpeed = Speed(d.OrbitSpeed), ZoomSpeed = Speed(d.ZoomSpeed),
                EdgeScroll = d.EdgeScroll ?? false, InvertOrbit = d.InvertOrbit ?? false, InvertZoom = d.InvertZoom ?? false, KeyOverrides = KeysFrom(d.Keys),
                ShowFps = d.ShowFps ?? false, EffectsVolume = Math.Clamp(d.EffectsVolume ?? 100, 0, 100), InterfaceVolume = Math.Clamp(d.InterfaceVolume ?? 100, 0, 100),
                MuteInBackground = d.MuteInBackground ?? false, UiScale = Math.Clamp(d.UiScale ?? 100, MinUiScale, MaxUiScale), TooltipMs = Math.Clamp(d.TooltipMs ?? 150, 0, 1000),
                ToastSeconds = Math.Clamp(d.ToastSeconds ?? 4, 2, 10), ColourBlind = d.ColourBlind ?? false,
                TextSize = Array.IndexOf(TextSizes, d.TextSize ?? 100) >= 0 ? d.TextSize ?? 100 : 100, ReduceMotion = d.ReduceMotion ?? false };
        static int Speed(int? v) => Math.Clamp(v ?? 100, MinSpeed, MaxSpeed);
    }

    /// <summary>Whether the file at <paramref name="path"/> carries a server address, which only a development build (or a
    /// release before release builds stopped writing them) saves.</summary>
    public static bool NamesServers(string path) => Read(path) is { } d && (d.ManagementUrl ?? d.GameServerUrl ?? d.WebUrl) is not null;

    /// <summary>Whether the file holds a Quality: none means the player has not chosen one, and a first start picks it (GraphicsSettings.FirstQuality).</summary>
    public static bool NamesQuality(string path) => Read(path)?.Quality is not null;

    static Dto? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Json) : null; }
        catch (Exception e) when (e is JsonException or IOException) { return null; }
    }

    public void Save(string path) => Save(path, BuildInfo.Current);

    public void Save(string path, BuildInfo build)
    {
        var dev = build.WebUrl is null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Dto(Animate, RememberMe, dev ? ManagementUrl : null, dev ? GameServerUrl : null,
            LogFilter.ToString(), LogOpen, Theme, CameraFollow, TurnBanner, null, dev ? WebUrl : null, MeasureMode.ToString(), ShowHitboxes, ShowTerrainHitboxes, Sound, Volume,
            Display.ToString(), Quality.ToString(), RenderScale, VSync, Window, LogDetailed, RangeRings, FpsCap,
            WoundNumbers, PanSpeed, OrbitSpeed, ZoomSpeed, EdgeScroll, InvertOrbit, InvertZoom, KeysTo(KeyOverrides), ShowFps, EffectsVolume, InterfaceVolume, MuteInBackground,
            UiScale, TooltipMs, ToastSeconds, ColourBlind, TextSize, ReduceMotion), Json));
    }

    public bool Equals(ClientSettings? other) => other is not null && Animate == other.Animate && RememberMe == other.RememberMe
        && ManagementUrl == other.ManagementUrl && GameServerUrl == other.GameServerUrl && LogFilter == other.LogFilter && LogOpen == other.LogOpen
        && Theme == other.Theme && CameraFollow == other.CameraFollow && TurnBanner == other.TurnBanner && RangeRings == other.RangeRings && LogDetailed == other.LogDetailed && ShowHitboxes == other.ShowHitboxes && ShowTerrainHitboxes == other.ShowTerrainHitboxes && MeasureMode == other.MeasureMode && Sound == other.Sound && Volume == other.Volume && Display == other.Display && Quality == other.Quality && RenderScale == other.RenderScale && VSync == other.VSync && FpsCap == other.FpsCap && Window == other.Window && WebUrl == other.WebUrl
        && WoundNumbers == other.WoundNumbers && PanSpeed == other.PanSpeed && OrbitSpeed == other.OrbitSpeed && ZoomSpeed == other.ZoomSpeed && EdgeScroll == other.EdgeScroll
        && InvertOrbit == other.InvertOrbit && InvertZoom == other.InvertZoom && SameKeys(KeyOverrides, other.KeyOverrides) && ShowFps == other.ShowFps && EffectsVolume == other.EffectsVolume
        && InterfaceVolume == other.InterfaceVolume && MuteInBackground == other.MuteInBackground && UiScale == other.UiScale && TooltipMs == other.TooltipMs
        && ToastSeconds == other.ToastSeconds && ColourBlind == other.ColourBlind && TextSize == other.TextSize && ReduceMotion == other.ReduceMotion;
    public override int GetHashCode() => HashCode.Combine(Animate, RememberMe, ManagementUrl, GameServerUrl, LogFilter, Theme);
}
