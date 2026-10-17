using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Screens;

/// <summary>Email and password, or a one-time code. Enter moves from email to password and signs in from the password or the code
/// field; the buttons are disabled while a request is out, and the line under them says what is happening or what went wrong.</summary>
public partial class LoginScreen : Control
{
    LineEdit _email = null!, _password = null!, _code = null!;
    Label _error = null!;
    CheckBox _remember = null!;
    readonly List<Button> _buttons = new();
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
        row.AddChild(Button("Sign in", SignIn));
        row.AddChild(Button("Sign up", () => Run("Creating your account…", s => s.SignUpAsync(_email.Text.Trim(), _password.Text))));
        row.AddChild(Button("Email me a code", () => Run("Sending a code…", async s =>
        {
            _methodId = await s.SendOtpAsync(_email.Text.Trim());
            _code.Visible = true; _code.GrabFocus();
            _error.Text = "Code sent: type it below and press Enter.";
        })));
        box.AddChild(_code = new LineEdit { PlaceholderText = "one-time code", Visible = false });
        _email.TextSubmitted += _ => _password.GrabFocus();
        _password.TextSubmitted += _ => SignIn();
        _code.TextSubmitted += _ => Run("Checking the code…", s => s.VerifyOtpAsync(_methodId!, _code.Text.Trim()));
        box.AddChild(_error = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        box.AddChild(new Label { Text = BuildInfo.Current.LoginFooter, ThemeTypeVariation = BuildInfo.Current.IsRelease ? Variation.Dim : Variation.Warning, HorizontalAlignment = HorizontalAlignment.Center });
        _email.CallDeferred(Control.MethodName.GrabFocus);
    }

    void SignIn() => Run("Signing in…", s => s.LoginWithPasswordAsync(_email.Text.Trim(), _password.Text));

    Button Button(string text, Action onPressed) { var b = new Button { Text = text }; b.Pressed += onPressed; _buttons.Add(b); return b; }

    async void Run(string doing, Func<Session, Task> action)
    {
        var session = Main.Instance.Session;
        if (_buttons.Any(b => b.Disabled)) return;                      // one request at a time
        if (_email.Text.Trim().Length == 0) { _error.Text = "Enter your email first."; _email.GrabFocus(); return; }
        session.UpdateSettings(s => s with { RememberMe = _remember.ButtonPressed });
        Busy(true); _error.Text = doing;
        try { await action(session); }                                  // PhaseChanged routes to Home on success
        catch (UpdateRequiredException) { }                             // routed to the update screen
        catch (ManagementException e) when (e.Status == 401) { Show("Wrong email, password or code."); }
        catch (Exception e) { Show(Feedback.Error(e, session.Settings.ManagementUrl)); }
        if (IsInstanceValid(this) && _error.Text == doing) _error.Text = "";
        if (IsInstanceValid(this)) Busy(false);
    }

    void Busy(bool busy) { foreach (var b in _buttons) b.Disabled = busy; }

    /// <summary>The screen may have been freed by the navigation the action caused.</summary>
    void Show(string message) { if (IsInstanceValid(this)) _error.Text = message; }
}
