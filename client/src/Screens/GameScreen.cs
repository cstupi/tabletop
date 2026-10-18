using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

public partial class GameScreen : Control
{
    Session _session = null!;
    public TableView Table { get; private set; } = null!;
    public TableInput Input { get; private set; } = null!;
    Label _status = null!, _statusTail = null!, _selection = null!; VBoxContainer _corner = null!; Toasts _toasts = null!; readonly HashSet<Guid> _lit = new(); VBoxContainer _side = null!; ScrollContainer _sidePanel = null!; HBoxContainer _bottom = null!;
    Control? _lobby, _setup, _waiting; readonly List<TeamPanel> _teams = new(); bool _wasWaiting;
    Control _hud = null!; Viewport.GuiFocusChangedEventHandler? _onFocus;   // the signal's own delegate type, not Action<Control>
    Action<RenderCause>? _onFrame; Action<Refused>? _onRefused; Action<SocketClosed, bool>? _onLost; Action? _onTheme, _onLayout;

    public override void _Ready()
    {
        _session = Main.Instance.Session;
        var root = new HBoxContainer(); root.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(root);
        var viewportContainer = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var viewport = new SubViewport { HandleInputLocally = true }; viewportContainer.AddChild(viewport); root.AddChild(viewportContainer);
        Table = GD.Load<PackedScene>("res://scenes/TableView.tscn").Instantiate<TableView>(); viewport.AddChild(Table);
        Input = new TableInput(); Table.AddChild(Input);
        viewportContainer.MouseExited += () => Input.ClearHover();   // the pointer over a Hud part is off the table: no hovered unit, no enemy strip
        var overlay = new PresenceOverlay(); Table.AddChild(overlay);
        // The side panel scrolls: a selected terrain piece lists every volume's fields, more than a window's height.
        _sidePanel = new ScrollContainer { CustomMinimumSize = new Vector2(360, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve }; root.AddChild(_sidePanel);
        var inset = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; inset.AddThemeConstantOverride("margin_right", DecisionBox.ScrollGap); _sidePanel.AddChild(inset);   // clear of the bar
        inset.AddChild(_side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _side.AddChild(_selection = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart });
        // Over the table in every status, after the root so it draws on top and hears keys first: Home and the status in the
        // corner, the toasts top right. GameScreen.Play adds the play Hud to it; Layout places all of it.
        _hud = new Control { MouseFilter = MouseFilterEnum.Ignore }; _hud.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(_hud);
        _hud.AddChild(_corner = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore });
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; _corner.AddChild(top);
        var home = new Button { Text = "Home", FocusMode = FocusModeEnum.None }; home.Pressed += () => Guarded.Run(_session.DisconnectAsync, Toast); top.AddChild(home);
        // The game's name clips with an ellipsis inside the corner's slot; the status and "watching" after it always show.
        top.AddChild(_status = new Label { ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Pass });
        top.AddChild(_statusTail = new Label());
        _hud.AddChild(_toasts = new Toasts());
        _onLayout = QueueLayout;
        _hud.Resized += _onLayout; _sidePanel.Resized += _onLayout; _sidePanel.VisibilityChanged += _onLayout;
        _corner.MinimumSizeChanged += QueueLayout; _toasts.MinimumSizeChanged += QueueLayout;   // children of the screen: they die with it
        // A Hud button that keeps the focus would be pressed by Space, the camera key: buttons give it straight back.
        _onFocus = c => { if (c is BaseButton) c.ReleaseFocus(); };
        GetViewport().GuiFocusChanged += _onFocus;
        _bottom = new HBoxContainer(); _side.AddChild(_bottom);
        if (_session.GameId is not null) { Table.Bind(_session); Input.Bind(Table, _session); overlay.Bind(Table, _session); }
        Input.SelectionChanged += ShowSelection;
        // A late army arrives as a board after the game info: the waiting panel goes when the board ends the wait.
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board)) != 0) { if (_wasWaiting != _session.WaitingTeams.Count > 0) Rebuild(); else RefreshAll(); } };
        _onRefused = r => { if (Feedback.Refusal(r) is { } text) Toast(text); };
        // The status line once the session has counted this attempt: the phase stays Reconnecting, so Rebuild does not run for it.
        _onLost = (c, will) => { Toast(Feedback.Closed(c, will)); Callable.From(() => { if (IsInstanceValid(_status)) ShowStatus(); }).CallDeferred(); };
        _session.GameChanged += Rebuild; _session.PhaseChanged += Rebuild; _session.Frame.Render += _onFrame;
        _session.RefusedReceived += _onRefused; _session.ConnectionLost += _onLost;
        _onTheme = OnTheme; Ui.Changed += _onTheme;
        Rebuild();
        QueueLayout();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (IsInstanceValid(_hud)) _hud.Resized -= _onLayout;
        if (IsInstanceValid(_sidePanel)) { _sidePanel.Resized -= _onLayout; _sidePanel.VisibilityChanged -= _onLayout; }
        if (_session is null) return;
        if (_onFocus is not null) GetViewport().GuiFocusChanged -= _onFocus;
        _session.GameChanged -= Rebuild; _session.PhaseChanged -= Rebuild; _session.Frame.Render -= _onFrame;
        _session.RefusedReceived -= _onRefused; _session.ConnectionLost -= _onLost;
        if (Input is not null && IsInstanceValid(Input)) Input.SelectionChanged -= ShowSelection;
        FreePlayPanels();
    }

    public void Toast(string text) { if (IsInstanceValid(_toasts)) _toasts.Show(text); }

    void Rebuild()
    {
        var game = _session.Game;
        ShowStatus();
        foreach (var t in _teams) t.QueueFree(); _teams.Clear();
        _lobby?.QueueFree(); _setup?.QueueFree(); _waiting?.QueueFree(); _lobby = _setup = _waiting = null;
        if (game is null) return;
        var playing = game.Status is GameStatus.Playing or GameStatus.Ended;
        _wasWaiting = _session.WaitingTeams.Count > 0;
        _sidePanel.Visible = !playing || _wasWaiting;                       // play is the table to the edges (spec → Layout), once deployment can begin
        if (_wasWaiting) _side.AddChild(_waiting = new WaitingPanel());
        if (!playing) foreach (var team in game.Teams) { var panel = new TeamPanel(team); _teams.Add(panel); _side.AddChild(panel); }
        RefreshTeams();
        if (game.Status is GameStatus.Lobby or GameStatus.Setup) _side.AddChild(_lobby = new LobbyPanel { Leave = ConfirmLeave });
        if (game.Status == GameStatus.Setup) _side.AddChild(_setup = new SetupToolbar(this));
        ShowPlayPanels(playing);
    }

    /// <summary>The corner's line (CornerLine): the name, clipped to the corner's slot with its whole text as the tooltip, then the tail.</summary>
    void ShowStatus()
    {
        var (name, tail) = CornerLine.Of(_session.Phase, _session.ReconnectAttempt, _session.Game, _session.IsWatching);
        _status.Text = name; _status.TooltipText = name; _statusTail.Text = tail;
        QueueLayout();   // a clipped label's minimum does not follow its text
    }

    /// <summary>A theme switch: the team panels' and the table's team colours, and the undo button's theme, which its CanvasLayer does not
    /// inherit. Nothing is recreated, so the lobby's and the setup toolbar's input survives; the Hud parts re-render themselves.</summary>
    void OnTheme()
    {
        foreach (var t in _teams) if (IsInstanceValid(t)) t.Recolour();
        if (_session.GameId is not null) Table.Redraw();   // the table is bound only for a game
        if (_undo is not null && IsInstanceValid(_undo)) _undo.Theme = Ui.Theme;
    }

    /// <summary>Teams now; the selection once every other handler of this frame's render has run, since the decision box clears highlights when it renders.</summary>
    void RefreshAll() { RefreshTeams(); Callable.From(ShowSelection).CallDeferred(); }

    /// <summary>The selected unit's models lit and its name and model count in the status area. The Ask's own highlights are put back where the selection leaves them.</summary>
    void ShowSelection()
    {
        if (!IsInstanceValid(_selection)) return;
        var p = _session.Projection; var game = _session.Game;
        foreach (var id in _lit) if (Table.PieceAt(id) is { } old && IsInstanceValid(old)) old.SetHighlight(_decision is { } d && IsInstanceValid(d) && d.Glows(old) ? Palette.Selected : null);   // the open Ask's glow, which only play has
        _lit.Clear();
        _selection.Text = p is null || game is null ? "" : SelectionView.Describe(new Names(game, p), p, Input.Selected) ?? "";
        if (p is null) return;
        foreach (var id in SelectionView.Models(p, Input.Selected)) if (Table.PieceAt(id) is { } n && IsInstanceValid(n)) { n.SetHighlight(Palette.Picked); _lit.Add(id); }
    }

    void RefreshTeams() { foreach (var t in _teams) if (IsInstanceValid(t)) t.Refresh(_session); }

    partial void ShowPlayPanels(bool playing);
}
