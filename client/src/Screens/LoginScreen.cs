using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Screens;

/// <summary>Sign-in (UX redesign part 1 → Login): a card with a segmented choice, Password (email, password, Remember me, Sign in and a flat
/// Create account) or Email me a code (email and Send code, then the code and Sign in). Enter moves from email to the next field and signs in
/// from the password or the code; a failure is a line under the field it concerns. The gear opens Settings before sign-in.</summary>
public partial class LoginScreen : Control
{
    LineEdit _email = null!, _password = null!, _code = null!;
    Label _emailProblem = null!, _problem = null!;
    CheckBox _remember = null!;
    Button _byPassword = null!, _byCode = null!, _signIn = null!, _create = null!, _send = null!, _codeSignIn = null!;
    Control _passwordPart = null!, _codePart = null!, _codeEntry = null!;
    readonly List<Button> _buttons = new();
    string? _methodId;

    public override void _Ready()
    {
        var session = Main.Instance.Session;
        AddChild(new Backdrop());
        var center = new CenterContainer(); center.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(center);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(CardWidth, 0) };
        var frame = ThemeBuilder.Nine(Ui.Current.Materials.Panel, Space.XL, Space.XL);
        card.AddThemeStyleboxOverride("panel", frame);
        center.AddChild(card);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Space.M); card.AddChild(box);

        var title = new HBoxContainer(); title.AddThemeConstantOverride("separation", Space.M); box.AddChild(title);
        title.AddChild(new TextureRect { Texture = Ui.Icon("objective"), Modulate = Ui.Accent, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(30, 30) });
        title.AddChild(new Label { Text = "TABLETOP", ThemeTypeVariation = Variation.Display, VerticalAlignment = VerticalAlignment.Center });

        var modes = new HBoxContainer(); modes.AddThemeConstantOverride("separation", 0); box.AddChild(modes);
        var group = new ButtonGroup();
        _byPassword = Mode("Password", group, true); _byCode = Mode("Email me a code", group, false);
        modes.AddChild(_byPassword); modes.AddChild(_byCode);
        _byPassword.Toggled += on => { if (on) ShowMode(code: false); };
        _byCode.Toggled += on => { if (on) ShowMode(code: true); };

        var email = Group(box, "Email");
        email.AddChild(_email = new LineEdit { PlaceholderText = "you@example.com" });
        email.AddChild(_emailProblem = Problem());

        _passwordPart = new VBoxContainer(); _passwordPart.AddThemeConstantOverride("separation", Space.M); box.AddChild(_passwordPart);
        Group(_passwordPart, "Password").AddChild(_password = new LineEdit { Secret = true });
        _passwordPart.AddChild(_remember = new CheckBox { Text = "Remember me on this machine", ButtonPressed = session.Settings.RememberMe });
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Space.M); _passwordPart.AddChild(row);
        row.AddChild(_signIn = Button("Sign in", SignIn, primary: true));
        row.AddChild(_create = Button("Create account", () => Run("Creating your account…", s => s.SignUpAsync(_email.Text.Trim(), _password.Text), _problem)));
        _create.Flat = true; _create.ThemeTypeVariation = Variation.FlatButton;

        _codePart = new VBoxContainer { Visible = false }; _codePart.AddThemeConstantOverride("separation", Space.M); box.AddChild(_codePart);
        _codePart.AddChild(_send = Button("Send code", SendCode, primary: true));
        _codeEntry = new VBoxContainer { Visible = false }; _codeEntry.AddThemeConstantOverride("separation", Space.M); _codePart.AddChild(_codeEntry);
        Group(_codeEntry, "Code").AddChild(_code = new LineEdit { PlaceholderText = "one-time code" });
        _codeEntry.AddChild(_codeSignIn = Button("Sign in", VerifyCode, primary: true));

        box.AddChild(_problem = Problem());

        var foot = new HBoxContainer(); box.AddChild(foot);
        foot.AddChild(new Label { Text = BuildInfo.Current.LoginFooter, ThemeTypeVariation = BuildInfo.Current.IsRelease ? Variation.Dim : Variation.Warning,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center });
        var gear = new Button { Icon = Ui.Icon("settings"), Flat = true, ThemeTypeVariation = Variation.FlatButton, ExpandIcon = false, TooltipText = "Options" };
        gear.AddThemeConstantOverride("icon_max_width", 22); HomeLook.IconInk(gear);
        gear.Pressed += () => OptionsScreen.Open(this);
        foot.AddChild(gear);

        _email.TextSubmitted += _ => { if (_byCode.ButtonPressed) SendCode(); else _password.GrabFocus(); };
        _password.TextSubmitted += _ => SignIn();
        _code.TextSubmitted += _ => VerifyCode();
        _email.CallDeferred(Control.MethodName.GrabFocus);
    }

    const int CardWidth = 440;

    /// <summary>A field's group under its caption: the caption sits closer to its field than the groups do to each other.</summary>
    static VBoxContainer Group(Control parent, string caption)
    {
        var group = new VBoxContainer(); group.AddThemeConstantOverride("separation", Space.XS); parent.AddChild(group);
        group.AddChild(new Label { Text = caption, ThemeTypeVariation = Variation.Dim });
        return group;
    }
    static Label Problem() => new() { ThemeTypeVariation = Variation.Failure, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false, CustomMinimumSize = new Vector2(CardWidth - 2 * Space.XL, 0) };

    static Button Mode(string text, ButtonGroup group, bool on)
    {
        var b = new Button { Text = text, ToggleMode = true, ButtonGroup = group, ButtonPressed = on, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        b.AddThemeColorOverride("font_pressed_color", Ui.Accent);
        return b;
    }

    void ShowMode(bool code)
    {
        _passwordPart.Visible = !code; _codePart.Visible = code;
        Say(_problem, null); Say(_emailProblem, null);
        (code && _codeEntry.Visible ? _code : _email).CallDeferred(Control.MethodName.GrabFocus);
    }

    void SignIn() => Run("Signing in…", s => s.LoginWithPasswordAsync(_email.Text.Trim(), _password.Text), _problem);

    void SendCode() => Run("Sending a code…", async s =>
    {
        _methodId = await s.SendOtpAsync(_email.Text.Trim());
        _codeEntry.Visible = true; _code.GrabFocus();
        Say(_problem, "Code sent.", Variation.Dim);
    }, _problem);

    void VerifyCode()
    {
        if (_methodId is null) { SendCode(); return; }
        Run("Checking the code…", s => s.VerifyOtpAsync(_methodId, _code.Text.Trim()), _problem);
    }

    Button Button(string text, Action onPressed, bool primary = false)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(primary ? 140 : 0, 0) };
        if (primary) HomeLook.Primary(b);
        b.Pressed += onPressed; _buttons.Add(b); return b;
    }

    async void Run(string doing, Func<Session, Task> action, Label problemLine)
    {
        var session = Main.Instance.Session;
        if (_buttons.Any(b => b.Disabled)) return;                      // one request at a time
        Say(_emailProblem, null);
        if (_email.Text.Trim().Length == 0) { Say(_emailProblem, "Enter your email first."); _email.GrabFocus(); return; }
        session.UpdateSettings(s => s with { RememberMe = _remember.ButtonPressed });
        Busy(true); Say(problemLine, doing, Variation.Dim);
        try { await action(session); }                                  // PhaseChanged routes to Home on success
        catch (UpdateRequiredException) { }                             // routed to the update screen
        catch (ManagementException e) when (e.Status == 401) { Say(problemLine, "Wrong email, password or code."); }
        catch (Exception e) { Say(problemLine, Feedback.Error(e, session.Settings.ManagementUrl)); }
        if (IsInstanceValid(this) && problemLine.Text == doing) Say(problemLine, null);
        if (IsInstanceValid(this)) Busy(false);
    }

    void Busy(bool busy) { foreach (var b in _buttons) b.Disabled = busy; }

    /// <summary>A line under its field; null hides it. The screen may have been freed by the navigation the action caused.</summary>
    void Say(Label line, string? message, string variation = Variation.Failure)
    {
        if (!IsInstanceValid(this) || !IsInstanceValid(line)) return;
        line.Text = message ?? ""; line.Visible = message is not null; line.ThemeTypeVariation = variation;
    }
}
