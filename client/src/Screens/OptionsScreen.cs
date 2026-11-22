using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Screens;

/// <summary>
/// Options (spec 2026-11-21-options-screen-design.md): a full-screen page over every screen. A rail of categories on the left, the open
/// category's rows in the middle, the hovered or focused row's description on the right, and a footer with Reset tab, Revert and Apply.
/// Changes are pending until Apply (<see cref="OptionsModel"/>); closing with pending changes asks Apply or Discard; an applied display
/// or resolution change reverts after 15 s unless kept. Arrows move between rows and change a value, Q/E or Tab between categories.
/// Every key goes to the page while it is open.
/// </summary>
public partial class OptionsScreen : CanvasLayer
{
    static OptionsScreen? _open;

    /// <summary>Opens Options over whatever shows (the gear buttons, the game menu); a second call while it is open does nothing.</summary>
    public static void Open(Node from)
    {
        if (_open is not null && IsInstanceValid(_open)) return;
        _open = new OptionsScreen(Main.Instance.Session);
        Main.Instance.AddChild(_open);
    }

    const int RailWidth = 240, ControlWidth = 320, DescriptionWidth = 340, RowHeight = 48, ConfirmSeconds = 15;

    readonly Session _session;
    OptionsModel _model;
    readonly PanelContainer _page = new() { MouseFilter = Control.MouseFilterEnum.Stop };
    readonly VBoxContainer _rail = new(), _rows = new();
    readonly ScrollContainer _scroll = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        VerticalScrollMode = ScrollContainer.ScrollMode.Reserve, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    readonly Label _descTitle = new() { ThemeTypeVariation = Variation.Accent, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    readonly Label _descText = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    readonly VBoxContainer _descExtra = new();
    readonly Button _apply = new() { Text = "Apply", ThemeTypeVariation = Variation.Primary }, _revert = new() { Text = "Revert" }, _reset = new() { Text = "Reset tab" };
    readonly PanelContainer _dialog = new() { Visible = false, ThemeTypeVariation = Variation.Card };
    OptionCategory _category = OptionCategory.Gameplay;
    /// <summary>The rows of the open category in order, each with what moves its value by keys.</summary>
    readonly List<(Control Row, Control Focus, Action<int>? Step)> _rowList = [];
    int _focused = -1;
    bool _quietFocus;   // a focus given back after a rebuild leaves the description as it was
    double _confirmLeft; ClientSettings? _confirmBefore; Label? _confirmLabel;

    OptionsScreen(Session session)
    {
        _session = session; _model = new OptionsModel(session.Settings);
        Layer = 100;
    }

    public override void _Ready()
    {
        _model.Screen = GraphicsApplier.ScreenOf(GetWindow());
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        _page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_page);
        _dialog.SetAnchorsPreset(Control.LayoutPreset.Center);
        root.AddChild(_dialog);
        Restyle(); Ui.Changed += Restyle;
        Build();
        Show(_category);
        FocusRow(0);
    }

    public override void _ExitTree() { Ui.Changed -= Restyle; if (_open == this) _open = null; }

    /// <summary>The page in the current theme: opaque background, the theme on the page and the dialog (a CanvasLayer passes none down).</summary>
    void Restyle()
    {
        foreach (var c in new Control[] { _page, _dialog }) c.Theme = Ui.Theme;
        var bg = new StyleBoxFlat { BgColor = Ui.Background with { A = 1 } };
        bg.SetContentMarginAll(Space.XL);
        _page.AddThemeStyleboxOverride("panel", bg);
        if (_rail.GetChildCount() > 0) { Rail(); Show(_category); }
    }

    void Build()
    {
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", Space.L);
        _page.AddChild(column);

        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", Space.M);
        head.AddChild(new Label { Text = "OPTIONS", ThemeTypeVariation = Variation.Display, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Text = "✕", ThemeTypeVariation = Variation.FlatButton, TooltipText = "Close", FocusMode = Control.FocusModeEnum.None };
        close.Pressed += RequestClose;
        head.AddChild(close);
        column.AddChild(head);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", Space.XL);
        _rail.CustomMinimumSize = new Vector2(RailWidth, 0); _rail.AddThemeConstantOverride("separation", Space.XS);
        body.AddChild(_rail);
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _rows.AddThemeConstantOverride("separation", Space.S);
        _scroll.AddChild(_rows);
        body.AddChild(_scroll);
        var desc = new PanelContainer { ThemeTypeVariation = Variation.Card, CustomMinimumSize = new Vector2(DescriptionWidth, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        var descBox = new VBoxContainer(); descBox.AddThemeConstantOverride("separation", Space.S);
        descBox.AddChild(_descTitle); descBox.AddChild(_descText); descBox.AddChild(_descExtra);
        _descExtra.AddThemeConstantOverride("separation", Space.S);
        desc.AddChild(descBox);
        body.AddChild(desc);
        column.AddChild(body);

        var foot = new HBoxContainer(); foot.AddThemeConstantOverride("separation", Space.M);
        foreach (var b in new[] { _reset, _revert, _apply }) b.FocusMode = Control.FocusModeEnum.None;
        _reset.Pressed += () => { _model.ResetTab(_category); Changed(); };
        _revert.Pressed += () => { _model.Revert(); Changed(); };
        _apply.Pressed += Apply;
        foot.AddChild(_reset);
        foot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        foot.AddChild(_revert); foot.AddChild(_apply);
        foot.AddChild(new Label { Text = "Esc", ThemeTypeVariation = Variation.KeyCap, VerticalAlignment = VerticalAlignment.Center, TooltipText = "Close" });
        column.AddChild(foot);
        Rail();
    }

    /// <summary>The categories as tall tabs, icon and word; the open one lit with an accent bar on its left edge.</summary>
    void Rail()
    {
        HudBits.Clear(_rail);
        foreach (var c in OptionsModel.Categories)
        {
            var open = c == _category;
            var tab = new Button { ThemeTypeVariation = Variation.FlatButton, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(RailWidth, RowHeight),
                Alignment = HorizontalAlignment.Left, Text = OptionsModel.Title(c), Icon = Ui.Icon(OptionsModel.Icon(c)), ExpandIcon = false };
            var look = new StyleBoxFlat { BgColor = open ? Ui.Accent with { A = 0.14f } : Ui.Accent with { A = 0 }, BorderColor = Ui.Accent };
            look.BorderWidthLeft = open ? Space.XS : 0; look.SetContentMarginAll(Space.S); look.ContentMarginLeft = Space.M;
            var hover = (StyleBoxFlat)look.Duplicate(); hover.BgColor = Ui.Accent with { A = open ? 0.2f : 0.08f };
            foreach (var state in new[] { "normal", "pressed", "focus" }) tab.AddThemeStyleboxOverride(state, look);
            tab.AddThemeStyleboxOverride("hover", hover);
            tab.AddThemeConstantOverride("h_separation", Space.S);
            tab.AddThemeConstantOverride("icon_max_width", Space.L);
            foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color" })
                tab.AddThemeColorOverride(name, open ? Ui.Accent : Ui.Text);
            tab.Pressed += () => { Show(c); FocusRow(0); };
            _rail.AddChild(tab);
        }
    }

    void Show(OptionCategory c)
    {
        var changed = c != _category; _category = c;
        if (changed) Rail();
        HudBits.Clear(_rows); _rowList.Clear(); _focused = -1;
        if (c == OptionCategory.Controls) ControlsRows(); else foreach (var row in _model.In(c)) _rows.AddChild(Row(row));
        _reset.TooltipText = $"{OptionsModel.Title(c)} back to its defaults";
        Describe(OptionsModel.Title(c), c == OptionCategory.Controls ? "Click a key, then press the new key. Esc cancels." : "");
        Footer();
        if (changed) _scroll.ScrollVertical = 0;
    }

    void Describe(string title, string text) { _descTitle.Text = title; _descText.Text = text; HudBits.Clear(_descExtra); }

    void Footer() { _apply.Disabled = !_model.Dirty; _revert.Disabled = !_model.Dirty; }

    /// <summary>A value changed: the rows show the pending settings again (one row can change another: Quality resets Render scale).</summary>
    void Changed()
    {
        var focused = _focused; var scroll = _scroll.ScrollVertical; var (title, text) = (_descTitle.Text, _descText.Text);
        Show(_category);
        Describe(title, text);
        Footer();
        // The new rows lay out next frame: the scroll and focus go back then, or the scroll clamps to the top.
        GetTree().ProcessFrame += Restore;
        void Restore()
        {
            GetTree().ProcessFrame -= Restore;
            if (!IsInstanceValid(this)) return;
            _scroll.ScrollVertical = scroll;
            if (focused >= 0 && focused < _rowList.Count && _rowList[focused].Focus.FocusMode != Control.FocusModeEnum.None) { _focused = focused; _quietFocus = true; _rowList[focused].Focus.GrabFocus(); _quietFocus = false; }
        }
    }

    void Apply()
    {
        var before = _model.Applied;
        var after = _model.Apply();
        Commit(after, before);
        if (OptionsModel.NeedsConfirm(before, after)) AskKeep(before);
        Changed();
    }

    /// <summary>The settings into the session (which saves and applies them), and the window to its new size.</summary>
    void Commit(ClientSettings to, ClientSettings from)
    {
        _session.UpdateSettings(_ => to);
        if (to.Window is { } w && to.Window != from.Window && to.Display == DisplayMode.Windowed) Callable.From(() => GraphicsApplier.Resize(GetWindow(), w)).CallDeferred();
    }

    /// <summary>"Keep these settings?" with a 15 s countdown that reverts on timeout.</summary>
    void AskKeep(ClientSettings before)
    {
        _confirmBefore = before; _confirmLeft = ConfirmSeconds;
        Dialog("Keep these settings?", _confirmLabel = new Label { Text = Reverting() }, ("Keep", () => { _confirmBefore = null; }, true), ("Revert", RevertDisplay, false));
    }

    string Reverting() => $"Reverting in {Math.Ceiling(_confirmLeft)} s.";

    /// <summary>Back to the display mode and window size from before Apply; every other applied change stays.</summary>
    void RevertDisplay()
    {
        if (_confirmBefore is not { } before) return;
        _confirmBefore = null;
        var now = _model.Applied;
        var back = now with { Display = before.Display, Window = before.Window };
        Commit(back, now);
        _model = new OptionsModel(back) { Screen = _model.Screen };
        Changed();
    }

    public override void _Process(double delta)
    {
        if (_confirmBefore is null) return;
        _confirmLeft -= delta;
        if (_confirmLabel is not null && IsInstanceValid(_confirmLabel)) _confirmLabel.Text = Reverting();
        if (_confirmLeft <= 0) { CloseDialog(); RevertDisplay(); }
    }

    void RequestClose()
    {
        if (!_model.Dirty) { Close(); return; }
        Dialog("Apply changes?", new Label { Text = "Some options have changed." },
            ("Apply", () => { Apply(); Close(); }, true), ("Discard", Close, false), ("Cancel", () => { }, false));
    }

    void Close() { _open = null; QueueFree(); }

    /// <summary>A small modal card in the middle of the page: a title, a body and buttons, the first primary.</summary>
    void Dialog(string title, Control body, params (string Text, Action Pressed, bool Primary)[] buttons)
    {
        HudBits.Clear(_dialog);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Space.M);
        box.AddChild(new Label { Text = title, ThemeTypeVariation = Variation.Accent });
        box.AddChild(body);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; row.AddThemeConstantOverride("separation", Space.S);
        foreach (var (text, pressed, primary) in buttons)
        {
            var b = new Button { Text = text, ThemeTypeVariation = primary ? Variation.Primary : "", FocusMode = Control.FocusModeEnum.None };
            b.Pressed += () => { CloseDialog(); pressed(); };
            row.AddChild(b);
        }
        box.AddChild(row);
        _dialog.AddChild(box);
        _dialog.Visible = true;
        _page.MouseFilter = Control.MouseFilterEnum.Stop;
        _dialog.ResetSize();
        _dialog.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
        _dialog.GrowHorizontal = Control.GrowDirection.Both; _dialog.GrowVertical = Control.GrowDirection.Both;
    }

    void CloseDialog() { _dialog.Visible = false; _confirmLabel = null; }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey k) return;
        GetViewport().SetInputAsHandled();   // every key is the page's while it is open
        if (Capturing) { Capture(k); return; }
        if (!k.Pressed) return;
        if (_dialog.Visible) { if (k.Keycode == Key.Escape && _confirmBefore is null) CloseDialog(); return; }
        switch (k.Keycode)
        {
            case Key.Escape: RequestClose(); break;
            case Key.Tab when k.ShiftPressed: case Key.Q: StepCategory(-1); break;
            case Key.Tab or Key.Backtab: case Key.E: StepCategory(1); break;
            case Key.Up: FocusRow(Math.Max(0, _focused - 1)); break;
            case Key.Down: FocusRow(Math.Min(_rowList.Count - 1, _focused + 1)); break;
            case Key.Left when _focused >= 0: _rowList[_focused].Step?.Invoke(-1); break;
            case Key.Right when _focused >= 0: _rowList[_focused].Step?.Invoke(1); break;
            case Key.Enter or Key.KpEnter or Key.Space when _focused >= 0 && _rowList[_focused].Focus is BaseButton b && !b.Disabled: b.EmitSignal(BaseButton.SignalName.Pressed); break;
        }
    }

    void StepCategory(int step)
    {
        var all = OptionsModel.Categories; var i = (all.ToList().IndexOf(_category) + step + all.Count) % all.Count;
        Show(all[i]);
        if (_rowList.Count > 0) FocusRow(0);
    }

    void FocusRow(int i, bool describe = true)
    {
        if (i < 0 || i >= _rowList.Count) return;
        _focused = i;
        var (row, focus, _) = _rowList[i];
        if (focus.FocusMode != Control.FocusModeEnum.None) focus.GrabFocus();
        _scroll.EnsureControlVisible(row);
        if (describe && row.HasMeta("describe")) row.EmitSignal(Control.SignalName.MouseEntered);
    }
}
