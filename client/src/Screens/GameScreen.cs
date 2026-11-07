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
    Label _status = null!, _statusTail = null!, _selection = null!; VBoxContainer _corner = null!; Toasts _toasts = null!; readonly HashSet<Guid> _lit = new(); VBoxContainer _side = null!, _sideColumn = null!; MarginContainer _sideHead = null!; ScrollContainer _sidePanel = null!; HBoxContainer _bottom = null!;
    Control? _lobby, _setup, _head; WaitingPanel? _waiting; bool _wasWaiting;
    readonly SetupFolds _folds = new();   // the sections' open state outlives each rebuild
    SubViewportContainer _tableBox = null!; Control _hud = null!; TerrainCard? _terrainCard; Viewport.GuiFocusChangedEventHandler? _onFocus;   // the signal's own delegate type, not Action<Control>
    LoadingCurtain _curtain = null!; LoadingPanel _strip = null!; readonly LoadingGate _stripGate = new(TimeSpan.FromMilliseconds(200)); bool _entered;
    Action<RenderCause>? _onFrame; Action<Refused>? _onRefused; Action<SocketClosed, bool>? _onLost; Action? _onTheme, _onLayout;

    public override void _Ready()
    {
        _session = Main.Instance.Session;
        var root = new HBoxContainer(); root.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(root);
        var viewportContainer = _tableBox = new SubViewportContainer { Stretch = true, MouseTarget = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var viewport = new SubViewport { HandleInputLocally = true }; viewportContainer.AddChild(viewport); root.AddChild(viewportContainer);
        Table = GD.Load<PackedScene>("res://scenes/TableView.tscn").Instantiate<TableView>(); viewport.AddChild(Table);
        Input = new TableInput(); Table.AddChild(Input);
        viewportContainer.MouseExited += () => Input.ClearHover();   // the pointer over a Hud part is off the table: no hovered unit, no enemy strip
        // A setup terrain tile dragged from the side panel lands where it is dropped on the table (SetupToolbar.DropTile). MouseTarget
        // makes the container itself the drop target: without it Godot looks for a Control inside the SubViewport, finds none, and refuses the drop.
        viewportContainer.SetDragForwarding(Callable.From((Vector2 _) => default(Variant)),
            Callable.From((Vector2 at, Variant data) => TileOf(data) is not null && _setup is SetupToolbar && Input.TableAt(at) is not null),
            Callable.From((Vector2 at, Variant data) => { if (TileOf(data) is { } name && _setup is SetupToolbar tools && Input.TableAt(at) is { } p) tools.DropTile(name, new Tabletop.Geometry.Point2(p.X, p.Y)); }));
        var overlay = new PresenceOverlay(); Table.AddChild(overlay);
        // The side column: a fixed head (the phase and Start or End setup) over a body that scrolls (spec 2026-10-27-godot-setup-screen-design.md).
        _sideColumn = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) }; root.AddChild(_sideColumn);
        _sideHead = new MarginContainer(); _sideColumn.AddChild(_sideHead);
        foreach (var edge in new[] { "margin_left", "margin_top", "margin_right" }) _sideHead.AddThemeConstantOverride(edge, (int)HudLayout.M);
        _sidePanel = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve }; _sideColumn.AddChild(_sidePanel);
        var inset = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; inset.AddThemeConstantOverride("margin_right", DecisionBox.ScrollGap); _sidePanel.AddChild(inset);   // clear of the bar
        foreach (var edge in new[] { "margin_left", "margin_top", "margin_bottom" }) inset.AddThemeConstantOverride(edge, (int)HudLayout.M);   // off the panel's edges
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
        _hud.AddChild(_strip = new LoadingPanel { Strip = true, Visible = false });
        _onLayout = QueueLayout;
        _hud.Resized += _onLayout; _sideColumn.Resized += _onLayout; _sideColumn.VisibilityChanged += _onLayout; _sideHead.Resized += _onLayout;   // the head grows with its team columns: the terrain list follows
        _corner.MinimumSizeChanged += QueueLayout; _toasts.MinimumSizeChanged += QueueLayout;   // children of the screen: they die with it
        // A Hud button that keeps the focus would be pressed by Space, the camera key: buttons give it straight back.
        _onFocus = c => { if (c is BaseButton) c.ReleaseFocus(); };
        GetViewport().GuiFocusChanged += _onFocus;
        _bottom = new HBoxContainer(); _side.AddChild(_bottom);
        if (_session.GameId is not null)
        {
            Table.Bind(_session); Input.Bind(Table, _session); overlay.Bind(Table, _session);
            Table.Selected = () => Input.Selected?.PieceId; Input.SelectionChanged += Table.RefreshLooks;
            Input.IntentPressed += i => { if (i == Intent.ToggleHitboxes) _session.UpdateSettings(x => x with { ShowHitboxes = !x.ShowHitboxes }); if (i == Intent.ToggleTerrainHitboxes) _session.UpdateSettings(x => x with { ShowTerrainHitboxes = !x.ShowTerrainHitboxes }); };
            // The selected terrain piece or setup marker, in setup and play, in the unit card's slot (GameScreen.Layout).
            _hud.AddChild(_terrainCard = new TerrainCard()); _terrainCard.Bind(_session, Input, Toast, _folds);
            _terrainCard.MinimumSizeChanged += QueueLayout; _terrainCard.VisibilityChanged += QueueLayout;
        }
        Input.SelectionChanged += ShowSelection;
        // A late army arrives as a board after the game info: the waiting panel follows the board, and goes when the board ends the wait.
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board)) != 0) { if (_wasWaiting != _session.WaitingTeams.Count > 0) Rebuild(); else { _waiting?.Refresh(); Callable.From(ShowSelection).CallDeferred(); } } };
        _onRefused = r => { if (Feedback.Refusal(r) is { } text) Toast(text); };
        // The status line once the session has counted this attempt: the phase stays Reconnecting, so Rebuild does not run for it.
        _onLost = (c, will) => { Toast(Feedback.Closed(c, will)); Callable.From(() => { if (IsInstanceValid(_status)) ShowStatus(); }).CallDeferred(); };
        _session.GameChanged += Rebuild; _session.PhaseChanged += Rebuild; _session.Frame.Render += _onFrame;
        _session.RefusedReceived += _onRefused; _session.ConnectionLost += _onLost;
        _onTheme = OnTheme; Ui.Changed += _onTheme;
        Rebuild();
        // Over everything, the Hud included, until the game's first table stands (LoadingView.Entry).
        AddChild(_curtain = new LoadingCurtain());
        UpdateLoading();
        QueueLayout();
    }

    public override void _Process(double delta) => UpdateLoading();

    /// <summary>The curtain while entering the game, then the strip over the table: a reconnect, the models still loading.</summary>
    void UpdateLoading()
    {
        var entry = LoadingView.Entry(new EntryState(_session.Phase, _session.Game?.Status, _session.Draft is not null, _session.Projection is not null,
            _session.CatchingUp, _session.GameId is null || Table.SlabReady, _entered));
        if (entry is null && _session.Phase == SessionPhase.Connected) _entered = true;
        _curtain.Set(entry, CurtainHead.Of(_session.Game));
        var meshes = _session.GameId is null ? null : Table.MeshProgress;
        var strip = LoadingView.Strip(_session.Phase, _entered, meshes?.Done ?? 0, meshes?.Total ?? 0);
        var shown = _stripGate.Update(strip is not null, TimeSpan.FromMilliseconds(Time.GetTicksMsec()));
        if (strip is not null) _strip.Set(strip);
        if (shown != _strip.Visible) { _strip.Visible = shown; QueueLayout(); }
    }

    /// <summary>The catalog name a dragged terrain tile carries, else null.</summary>
    static string? TileOf(Variant data) =>
        data.VariantType == Variant.Type.String && data.AsString() is var text && text.StartsWith(TerrainTileButton.DragPrefix, StringComparison.Ordinal)
            ? text[TerrainTileButton.DragPrefix.Length..] : null;

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (IsInstanceValid(_hud)) _hud.Resized -= _onLayout;
        if (IsInstanceValid(_sideColumn)) { _sideColumn.Resized -= _onLayout; _sideColumn.VisibilityChanged -= _onLayout; }
        if (IsInstanceValid(_sideHead)) _sideHead.Resized -= _onLayout;
        if (_session is null) return;
        if (_onFocus is not null) GetViewport().GuiFocusChanged -= _onFocus;
        _session.GameChanged -= Rebuild; _session.PhaseChanged -= Rebuild; _session.Frame.Render -= _onFrame;
        _session.RefusedReceived -= _onRefused; _session.ConnectionLost -= _onLost;
        if (Input is not null && IsInstanceValid(Input)) { Input.SelectionChanged -= ShowSelection; Input.SelectionChanged -= Table.RefreshLooks; }
        FreePlayPanels();
    }

    public void Toast(string text) { if (IsInstanceValid(_toasts)) _toasts.Show(text); }

    void Rebuild()
    {
        var game = _session.Game;
        ShowStatus();
        _lobby?.QueueFree(); _setup?.QueueFree(); _head?.QueueFree(); _lobby = _setup = _head = null;
        _wasWaiting = game is not null && _session.WaitingTeams.Count > 0;
        // The waiting panel is kept and refreshed, not rebuilt: a game update that leaves its lines alone keeps an open army dropdown open.
        if (!_wasWaiting) { _waiting?.QueueFree(); _waiting = null; }
        if (game is null) return;
        var playing = game.Status is GameStatus.Playing or GameStatus.Ended;
        _sideColumn.Visible = !playing;                                     // play is the table to the edges (spec → Layout); waiting for players is the decision slot's
        // The lobby has no table to edit: its panel takes the whole window, below the corner's line; setup puts the table back.
        var lobbyOnly = game.Status == GameStatus.Lobby;
        _tableBox.Visible = !lobbyOnly;
        _sideColumn.SizeFlagsHorizontal = lobbyOnly ? SizeFlags.ExpandFill : SizeFlags.Fill;
        _sideHead.AddThemeConstantOverride("margin_top", (int)(lobbyOnly ? HudLayout.M + HudLayout.CornerH + HudLayout.M : HudLayout.M));
        if (_wasWaiting)
        {
            if (_waiting is null) { _hud.AddChild(_waiting = new WaitingPanel()); _waiting.MinimumSizeChanged += QueueLayout; }
            else _waiting.Refresh();
            QueueLayout();
        }
        if (!playing)
        {
            // The head: the phase, Start or End setup, Still to do, and in setup the compact team columns (spec 2026-11-07-lobby-and-setup-redesign-design.md).
            _sideHead.AddChild(_head = new SidePanelHead { Folds = _folds });
            var settings = new LobbyPanel { Leave = ConfirmLeave, Folds = _folds };
            if (game.Status == GameStatus.Setup) { _side.AddChild(_setup = new SetupToolbar(this, _folds)); _side.AddChild(_lobby = settings); }
            else
            {
                // The lobby: the team columns across the window, Game settings in a column of its own on the right.
                var page = new HBoxContainer(); page.AddThemeConstantOverride("separation", 32);
                page.AddChild(new TeamColumnsPanel(compact: false) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkBegin });
                settings.CustomMinimumSize = new Vector2(380, 0); settings.SizeFlagsVertical = SizeFlags.ShrinkBegin;
                page.AddChild(settings);
                _side.AddChild(_lobby = page);
            }
        }
        ShowPlayPanels(playing);
    }

    /// <summary>The corner's line (CornerLine): the name, clipped to the corner's slot with its whole text as the tooltip, then the tail.</summary>
    void ShowStatus()
    {
        var (name, tail) = CornerLine.Of(_session.Phase, _session.ReconnectAttempt, _session.Game, _session.IsWatching, _session.Replaying);
        _status.Text = name; _status.TooltipText = name; _statusTail.Text = tail;
        QueueLayout();   // a clipped label's minimum does not follow its text
    }

    /// <summary>A theme switch: the table's team colours, and the undo button's theme, which its CanvasLayer does not inherit. Nothing is
    /// recreated, so the setup toolbar's input survives; the Hud parts and the team columns re-render themselves.</summary>
    void OnTheme()
    {
        if (_session.GameId is not null) Table.Redraw();   // the table is bound only for a game
        if (_undo is not null && IsInstanceValid(_undo)) _undo.Theme = Ui.Theme;
    }


    /// <summary>The selected unit's models lit and its name and model count in the status area. The Ask's own highlights are put back where the selection leaves them.</summary>
    void ShowSelection()
    {
        if (!IsInstanceValid(_selection)) return;
        var p = _session.Projection; var game = _session.Game;
        foreach (var id in _lit) if (Table.PieceAt(id) is { } old && IsInstanceValid(old)) old.SetHighlight(_decision is { } d && IsInstanceValid(d) && d.Glows(old) ? Palette.Selected : null);   // the open Ask's glow, which only play has
        _lit.Clear();
        _selection.Text = p is null || game is null ? "" : SelectionView.Describe(new Names(game, p), p, Input.Selected) ?? "";
        _selection.Visible = _selection.Text.Length > 0;   // no empty line over the panel
        if (p is null) return;
        foreach (var id in SelectionView.Models(p, Input.Selected)) if (Table.PieceAt(id) is { } n && IsInstanceValid(n)) { n.SetHighlight(Palette.Picked); _lit.Add(id); }
    }

    partial void ShowPlayPanels(bool playing);
}
