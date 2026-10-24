using Godot;
using HttpClient = System.Net.Http.HttpClient;
using Tabletop.Client.Core;
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
    public HttpClient Http { get; } = new();
    Node? _screen;
    string _settingsPath = "";
    readonly GodotDispatcher _dispatcher = new();

    public override void _Ready()
    {
        Instance = this;
        GetWindow().Title = BuildInfo.Current.WindowTitle;
        var data = UserData.Open(BuildInfo.Current, OS.GetUserDataDir());
        _settingsPath = data.Settings;
        var settings = ClientSettings.Load(_settingsPath);
        var store = new SessionStore(data.Session);
        var management = new ManagementClient(Http, new Uri(settings.ManagementUrl));
        Session = new Session(management, new ClientGameSocketFactory(), store, settings, s => s.Save(_settingsPath), _dispatcher);
        ApplyTheme();
        GetWindow().MinSize = new Vector2I(GraphicsSettings.MinWindowWidth, GraphicsSettings.MinWindowHeight);
        ApplyGraphics(start: true);
        Session.SettingsChanged += () => { if (UiThemes.Resolve(Session.Settings.Theme) != Ui.Current) ApplyTheme(); ApplyGraphics(start: false); };
        Cache = new AssetCache(data.Cache, Http);
        // Built-in content: the saved server catalog when complete, else the packed one; a release's new version syncs while signed in.
        BuiltinContent.Cache = Cache;
        var builtins = new BuiltinSync(data.Builtin, Cache, management.GetBuiltin, () => BuiltinContent.Catalog,
            catalog => _dispatcher.Post(() => { BuiltinContent.Use(catalog); GD.Print($"built-in content {catalog.Version}"); }));
        if (builtins.LoadCached() is { } saved) BuiltinContent.Use(saved);
        management.BuiltinVersionSeen += version => { if (management.Jwt is not null) _ = builtins.Notice(version); };
        Session.PhaseChanged += () => _dispatcher.Post(Route);
        var refresh = new Godot.Timer { WaitTime = 60, Autostart = true };
        refresh.Timeout += () => _ = Session.RefreshAsync();
        AddChild(refresh);
        var rect = new Godot.Timer { WaitTime = 1, Autostart = true }; rect.Timeout += SaveWindowRect; AddChild(rect);
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

    public override void _Notification(int what) { if (what == NotificationWMCloseRequest) SaveWindowRect(); }

    void ApplyTheme()
    {
        Ui.Apply(UiThemes.Resolve(Session.Settings.Theme));
        GetTree().Root.Theme = Ui.Theme;
        if (_screen is Control c) c.Theme = Ui.Theme;
        RenderingServer.SetDefaultClearColor(Ui.Of(Ui.Current.Background));
    }

    async Task Boot()
    {
        try { await Session.TryResumeAsync(); } catch (Exception e) { GD.PushWarning($"resume failed: {e.Message}"); }
        _dispatcher.Post(Route);
    }

    void Route()
    {
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
