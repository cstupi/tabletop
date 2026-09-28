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
