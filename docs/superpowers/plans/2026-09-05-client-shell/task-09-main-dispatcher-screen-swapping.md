# Task 9: `Main`, the dispatcher, screen swapping, the login and update-required screens

**Files:**
- Modify: `client/src/Main.cs`, `client/scenes/Main.tscn`
- Create: `client/scenes/Login.tscn`, `client/scenes/UpdateRequired.tscn`, `client/src/Screens/LoginScreen.cs`, `client/src/Screens/UpdateRequiredScreen.cs`, `client/src/Screens/GodotDispatcher.cs`

**Interfaces:**
- Consumes: `Session`, `SessionPhase`, `ManagementClient`, `ClientGameSocketFactory`, `SessionStore`, `ClientSettings`, `AssetCache`, `IDispatcher`, `ManagementException`, `UpdateRequiredException` (Tasks 2–7).
- Produces (namespace `Tabletop`): `partial class Main : Node` with `static Main Instance`, `Session Session`, `AssetCache Cache`, `HttpClient Http`, `void Show(string scenePath)` (frees the current screen and instantiates the next under `Main`; a path with no scene logs a warning and keeps the current screen), the 60 s refresh `Timer`; `sealed class GodotDispatcher : IDispatcher` (`Callable.From(action).CallDeferred()`); `partial class LoginScreen : Control` (email, password, "Sign in", "Sign up", "Email me a code" → code field, submitted with Enter, "Remember me", the error label); `partial class UpdateRequiredScreen : Control` (the message and a "Quit" button).
- Screen routing is `Session.PhaseChanged`: `LoggedOut → Login.tscn`, `LoggedIn → Home.tscn` (Task 10), `Connecting/Reconnecting/Connected → Game.tscn` (Task 13), `UpdateRequired → UpdateRequired.tscn`. On start `TryResumeAsync()` decides between Login and Home.
- Routing is posted. `PhaseChanged` is raised on whatever thread finished the call that changed the phase, so `Main` subscribes `() => _dispatcher.Post(Route)`, and `Boot` posts its route the same way. `Route` skips a swap to the scene already shown.
- `Show` guards with `ResourceLoader.Exists`, so a phase whose scene a later task writes (Home before Task 10, Game before Task 13) keeps the current screen instead of throwing.
- Nothing escapes `async void` or a fire-and-forget task. `Boot` catches `Exception`, so `Route` always runs and a startup failure lands on Login with a warning. `LoginScreen.Run` catches `Exception` last, after `UpdateRequiredException`, `ManagementException` and `HttpRequestException`, so a timeout or a malformed body shows on the error label.

Spec: client shell → Structure (three screens, each a Godot scene swapped under the root; one session object beneath), Screens → Login (email and password, or email and a one-time code; remembered in the OS store when chosen), Home ("treats management's update required response as a screen of its own"); No social login; management API → The version check.

- [ ] **Step 1: Rewrite `client/scenes/Main.tscn` and write the two new scenes (minimal, by hand)**

```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Main.cs" id="1_main"]

[node name="Main" type="Node"]
script = ExtResource("1_main")
```

`client/scenes/Login.tscn`:
```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Screens/LoginScreen.cs" id="1"]

[node name="Login" type="Control"]
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
script = ExtResource("1")
```

`client/scenes/UpdateRequired.tscn` is the same with `UpdateRequiredScreen.cs` and root name `UpdateRequired`. The editor rewrites these on first save and adds `uid` attributes; that is expected, and the `.uid` files it creates beside each script are committed.

- [ ] **Step 2: Write `GodotDispatcher.cs` and `Main.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Screens;

/// <summary>Session events arrive on socket and thread-pool threads; Godot nodes may only be touched on the main thread.</summary>
public sealed class GodotDispatcher : IDispatcher
{
    public void Post(Action action) => Callable.From(action).CallDeferred();
}
```

```csharp
using Godot;
using HttpClient = System.Net.Http.HttpClient;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;

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
        var userDir = OS.GetUserDataDir();
        _settingsPath = Path.Combine(userDir, "settings.json");
        var settings = ClientSettings.Load(_settingsPath);
        var store = new SessionStore(Path.Combine(userDir, "session.bin"));
        Session = new Session(new ManagementClient(Http, new Uri(settings.ManagementUrl)), new ClientGameSocketFactory(), store, settings, s => s.Save(_settingsPath), _dispatcher);
        Cache = new AssetCache(Path.Combine(userDir, "cache"), Http);
        Session.PhaseChanged += () => _dispatcher.Post(Route);
        var refresh = new Godot.Timer { WaitTime = 60, Autostart = true };
        refresh.Timeout += () => _ = Session.RefreshAsync();
        AddChild(refresh);
        _ = Boot();
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
        AddChild(_screen);
    }
}
```

Nothing in `Main` is platform-specific. `OS.GetUserDataDir()` resolves under `%APPDATA%` on Windows, `~/.local/share` on Linux and `~/Library/Application Support` on macOS, and `SessionStore` is the same file on all three (Task 2). Windows is the first export target; other desktop platforms are possible and no decision here forecloses them (`architecture.md` → Deliverables).

- [ ] **Step 3: Write `LoginScreen.cs` and `UpdateRequiredScreen.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Screens;

public partial class LoginScreen : Control
{
    LineEdit _email = null!, _password = null!, _code = null!;
    Label _error = null!;
    CheckBox _remember = null!;
    string? _methodId;

    public override void _Ready()
    {
        var session = Main.Instance.Session;
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        box.SetAnchorsPreset(LayoutPreset.Center); box.GrowHorizontal = GrowDirection.Both; box.GrowVertical = GrowDirection.Both;
        AddChild(box);
        box.AddChild(new Label { Text = "Tabletop", HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(_email = new LineEdit { PlaceholderText = "email" });
        box.AddChild(_password = new LineEdit { PlaceholderText = "password", Secret = true });
        box.AddChild(_remember = new CheckBox { Text = "Remember me on this machine", ButtonPressed = session.Settings.RememberMe });
        var row = new HBoxContainer(); box.AddChild(row);
        row.AddChild(Button("Sign in", () => Run(s => s.LoginWithPasswordAsync(_email.Text, _password.Text))));
        row.AddChild(Button("Sign up", () => Run(s => s.SignUpAsync(_email.Text, _password.Text))));
        row.AddChild(Button("Email me a code", () => Run(async s => { _methodId = await s.SendOtpAsync(_email.Text); _code.Visible = true; _error.Text = "Code sent."; })));
        box.AddChild(_code = new LineEdit { PlaceholderText = "one-time code", Visible = false });
        _code.TextSubmitted += _ => Run(s => s.VerifyOtpAsync(_methodId!, _code.Text));
        box.AddChild(_error = new Label { Modulate = Colors.Orange });
    }

    static Button Button(string text, Action onPressed) { var b = new Button { Text = text }; b.Pressed += onPressed; return b; }

    async void Run(Func<Session, Task> action)
    {
        var session = Main.Instance.Session;
        session.UpdateSettings(s => s with { RememberMe = _remember.ButtonPressed });
        try { await action(session); }                                  // PhaseChanged routes to Home on success
        catch (UpdateRequiredException) { }                             // routed to the update screen
        catch (ManagementException e) { _error.Text = e.Status == 401 ? "Wrong email, password or code." : e.Message; }
        catch (HttpRequestException e) { _error.Text = $"Cannot reach {session.Settings.ManagementUrl}: {e.Message}"; }
        catch (Exception e) { _error.Text = e.Message; }
    }
}
```

```csharp
using Godot;

namespace Tabletop.Client.Screens;

public partial class UpdateRequiredScreen : Control
{
    public override void _Ready()
    {
        var box = new VBoxContainer(); box.SetAnchorsPreset(LayoutPreset.Center); AddChild(box);
        box.AddChild(new Label { Text = "This version of Tabletop is older than the server accepts. Install the current build and start it again." });
        var quit = new Button { Text = "Quit" }; quit.Pressed += () => GetTree().Quit(); box.AddChild(quit);
    }
}
```

- [ ] **Step 4: Run it.** `docker compose up -d`, `./scripts/management` in one WSL shell, then `./scripts/godot`. Expected: the login screen; a wrong password shows *Wrong email, password or code.*; a correct one routes to `LoggedIn`, and the login screen stays with the warning `no scene at res://scenes/Home.tscn` until Task 10 writes it; with "Remember me" ticked, `%APPDATA%\Godot\app_userdata\Tabletop\session.bin` exists and the next launch skips login. Set `MinimumClientVersion` to `2` in `server/management/appsettings.json`, restart management, launch: the update-required screen. Put it back.

- [ ] **Step 5: `./scripts/check-client` prints nothing. Commit: `Add the root, the dispatcher and the login and update-required screens`.**
