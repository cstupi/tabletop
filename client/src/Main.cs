using Godot;
using HttpClient = System.Net.Http.HttpClient;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
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
        GetWindow().Title = $"Tabletop {BuildInfo.Current.Version}";
        var userDir = OS.GetUserDataDir();
        _settingsPath = Path.Combine(userDir, "settings.json");
        var settings = ClientSettings.Load(_settingsPath);
        var store = new SessionStore(Path.Combine(userDir, "session.bin"));
        Session = new Session(new ManagementClient(Http, new Uri(settings.ManagementUrl)), new ClientGameSocketFactory(), store, settings, s => s.Save(_settingsPath), _dispatcher);
        ApplyTheme();
        Session.SettingsChanged += () => { if (UiThemes.Resolve(Session.Settings.Theme) != Ui.Current) ApplyTheme(); };
        Cache = new AssetCache(Path.Combine(userDir, "cache"), Http);
        Session.PhaseChanged += () => _dispatcher.Post(Route);
        var refresh = new Godot.Timer { WaitTime = 60, Autostart = true };
        refresh.Timeout += () => _ = Session.RefreshAsync();
        AddChild(refresh);
        _ = Boot();
    }

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
