using Godot;
using HttpClient = System.Net.Http.HttpClient;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Screens;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;

namespace Tabletop;

/// <summary>The root. Owns the session object and the asset cache; swaps one screen at a time beneath itself.</summary>
public partial class Main : Node
{
    public static Main Instance { get; private set; } = null!;
    public Session Session { get; private set; } = null!;
    public AssetCache Cache { get; private set; } = null!;
    public HttpClient Http { get; } = ClientHttp.Create();
    Node? _screen;
    string _settingsPath = "", _warmupPath = "";
    bool _warming = true;
    string _themeKey = "";
    readonly FpsCounter _fps = new();   // no screen until the shader warm-up has run (or was not needed)
    readonly GodotDispatcher _dispatcher = new();

    public override void _Ready()
    {
        Instance = this;
        DialogLayout.Install(GetTree());
        GetWindow().Title = BuildInfo.Current.WindowTitle;
        var data = UserData.Open(BuildInfo.Current, OS.GetUserDataDir());
        _settingsPath = data.Settings; _warmupPath = Path.Combine(data.Dir, "graphics-warmup.txt");
        var settings = ClientSettings.Load(_settingsPath);
        var store = new SessionStore(data.Session);
        var management = new ManagementClient(Http, new Uri(settings.ManagementUrl));
        Session = new Session(management, new ClientGameSocketFactory(), store, settings, s => s.Save(_settingsPath), _dispatcher);
        // A first start's Quality follows the GPU: Low where it shares the CPU's memory.
        if (!ClientSettings.NamesQuality(_settingsPath)) Session.UpdateSettings(s => s with { Quality = GraphicsSettings.FirstQuality(GraphicsApplier.Gpu()) });
        Hotkeys.Bindings = Session.Settings.Keys;
        AudioBuses.Ensure();
        ApplyTheme();
        AddChild(_fps);
        ApplyInterface();
        GetWindow().MinSize = new Vector2I(GraphicsSettings.MinWindowWidth, GraphicsSettings.MinWindowHeight);
        ApplyGraphics(start: true);
        Session.SettingsChanged += () =>
        {
            // A theme, its text size or team colours, or a key binding: every part draws again with the new theme (and the new key caps).
            if (ThemeKey(Session.Settings) != _themeKey) { Hotkeys.Bindings = Session.Settings.Keys; ApplyTheme(); }
            ApplyGraphics(start: false); ApplyInterface();
        };
        Cache = new AssetCache(data.Cache, Http);
        // Built-in content: the saved server catalog, else the packed one; a release's new version syncs while signed in. Its files come
        // from the cache, from res:// where this build packs them (a development run), else on demand.
        BuiltinContent.Cache = Cache;
        BuiltinContent.Packs = path => Godot.FileAccess.FileExists($"res://content/builtin/{path}") || ResourceLoader.Exists($"res://content/builtin/{path}");
        var builtins = new BuiltinSync(data.Builtin, Cache, management.GetBuiltin, () => BuiltinContent.Catalog,
            catalog => _dispatcher.Post(() => { BuiltinContent.Use(catalog); GD.Print($"built-in content {catalog.Version}"); }));
        BuiltinContent.Sync = builtins;
        if (builtins.LoadCached() is { } saved) BuiltinContent.Use(saved);
        var keep = BuiltinContent.Catalog.Files.Values.Select(f => f.Sha256).ToHashSet();
        _ = Task.Run(() =>
        {
            try { if (Cache.Evict(keep) is > 0 and var n) GD.Print($"asset cache: evicted {n} files"); }
            catch (Exception e) { GD.PushWarning($"asset cache eviction: {e.Message}"); }
        });
        management.BuiltinVersionSeen += version => { if (management.Jwt is not null) _ = builtins.Notice(version); };
        Session.PhaseChanged += () => _dispatcher.Post(Route);
        var refresh = new Godot.Timer { WaitTime = 60, Autostart = true };
        refresh.Timeout += () => _ = Session.RefreshAsync();
        AddChild(refresh);
        var rect = new Godot.Timer { WaitTime = 1, Autostart = true }; rect.Timeout += SaveWindowRect; AddChild(rect);
        if (PerfProbe.OutPath is not null) AddChild(new PerfProbe());
        _ = Boot();
    }

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

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) SaveWindowRect();
        else if (what is (int)NotificationApplicationFocusIn or (int)NotificationApplicationFocusOut && Session is not null)
            AudioBuses.Apply(Session.Settings, what == NotificationApplicationFocusIn);   // Mute in background
    }

    /// <summary>What outlives the screens (the built-in meshes' templates, the prints) goes before Godot's servers do.</summary>
    public override void _ExitTree()
    {
        _screen?.Free(); _screen = null;
        MeshLoader.FreeBuiltins(); PrintTextures.Clear();
    }

    /// <summary>The theme images at 2× when the UI draws above 1.2× (the UI scale times the screen's own scale).</summary>
    static bool HiDpi(ClientSettings s) => s.UiScale / 100f * DisplayServer.ScreenGetScale() > 1.2f;

    /// <summary>What the built theme depends on: the theme, its text size and team colours, whether its images are at 2×, and the key bindings the parts draw as caps.</summary>
    static string ThemeKey(ClientSettings s) => $"{UiThemes.Resolve(s.Theme).Id}|{s.TextSize}|{s.ColourBlind}|{HiDpi(s)}|{string.Join(",", s.KeyOverrides.OrderBy(k => k.Key).Select(k => $"{k.Key}={string.Join("/", k.Value)}"))}";

    /// <summary>Options → Interface and Audio: the UI scale (the window's content scale), the tooltip delay, the toast time, the bus volumes, the frame counter.</summary>
    void ApplyInterface()
    {
        var s = Session.Settings; var w = GetWindow();
        if (!Mathf.IsEqualApprox(w.ContentScaleFactor, s.UiScale / 100f)) w.ContentScaleFactor = s.UiScale / 100f;
        ProjectSettings.SetSetting("gui/timers/tooltip_delay_sec", s.TooltipMs / 1000.0);
        Toasts.Seconds = s.ToastSeconds;
        AudioBuses.Apply(s, w.HasFocus());
        _fps.Visible = s.ShowFps;
    }

    void ApplyTheme()
    {
        _themeKey = ThemeKey(Session.Settings); Ui.HiDpi = HiDpi(Session.Settings);
        Ui.Apply(UiThemes.Adjusted(UiThemes.Resolve(Session.Settings.Theme), Session.Settings.TextSize, Session.Settings.ColourBlind), theme =>
        {
            GetTree().Root.Theme = theme;
            if (_screen is Control c) c.Theme = theme;
            RenderingServer.SetDefaultClearColor(Ui.Of(Ui.Current.Background));
        });
    }

    async Task Boot()
    {
        var resume = Session.TryResumeAsync();
        await WarmUp();
        try { await resume; } catch (Exception e) { GD.PushWarning($"resume failed: {e.Message}"); }
        _dispatcher.Post(Route);
        if (PerfProbe.GameId is { } game && Session.Phase == SessionPhase.LoggedIn)
            try { await (PerfProbe.Replay ? Session.ReplayAsync(game) : Session.ConnectAsync(game)); } catch (Exception e) { GD.PushWarning($"perf game: {e.Message}"); }
    }

    /// <summary>The shader warm-up behind "Preparing graphics…", when the stamp (Godot, the renderer, the GPU and its driver, the client's
    /// version) differs from the last one that ran; development builds run it again with <c>TABLETOP_WARMUP=1</c>.</summary>
    async Task WarmUp()
    {
        try
        {
            var stamp = GraphicsWarmup.Stamp((string)Engine.GetVersionInfo()["string"], RenderingServer.GetCurrentRenderingMethod(), RenderingServer.GetCurrentRenderingDriverName(),
                RenderingServer.GetVideoAdapterName(), RenderingServer.GetVideoAdapterApiVersion(), BuildInfo.Current.Version);
            var saved = File.Exists(_warmupPath) ? await File.ReadAllTextAsync(_warmupPath) : null;
            var forced = !BuildInfo.Current.IsRelease && System.Environment.GetEnvironmentVariable("TABLETOP_WARMUP") == "1";
            if (!GraphicsWarmup.Needed(saved, stamp) && !forced) return;
            var curtain = new LoadingCurtain { Delay = TimeSpan.Zero, Theme = Ui.Theme }; AddChild(curtain);   // a Node parent passes no theme down
            var scene = new WarmupScene(); AddChild(scene);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await scene.Run(Session.Settings.Quality, curtain.Set);
            GD.Print($"graphics warm-up {clock.ElapsedMilliseconds} ms");
            scene.QueueFree(); curtain.QueueFree();
            GraphicsApplier.ApplyAtlas(Session.Settings.Graphics);
            await File.WriteAllTextAsync(_warmupPath, stamp);
        }
        catch (Exception e) { GD.PushWarning($"graphics warm-up: {e.Message}"); }
        finally { _warming = false; }
    }

    void Route()
    {
        if (_warming) return;
        var path = Session.Phase switch
        {
            SessionPhase.LoggedOut => "res://scenes/Login.tscn",
            SessionPhase.LoggedIn => "res://scenes/Home.tscn",
            SessionPhase.UpdateRequired => "res://scenes/UpdateRequired.tscn",
            _ => "res://scenes/Game.tscn",
        };
        if (_screen is not null && _screen.SceneFilePath == path) return;
        Show(path);
    }

    public void Show(string scenePath)
    {
        if (!ResourceLoader.Exists(scenePath)) { GD.PushWarning($"no scene at {scenePath}"); return; }
        _screen?.QueueFree();
        _screen = GD.Load<PackedScene>(scenePath).Instantiate();
        if (_screen is Control c) c.Theme = Ui.Theme;
        AddChild(_screen);
    }
}
