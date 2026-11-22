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
    Button _home = null!; PanelContainer _chip = null!; Label _chipText = null!;
    Label _status = null!, _statusTail = null!, _selection = null!; VBoxContainer _corner = null!; Toasts _toasts = null!; readonly HashSet<Guid> _lit = new(); VBoxContainer _side = null!; ScrollContainer _sideColumn = null!; HBoxContainer _bottom = null!;
    Control? _lobby; WaitingPanel? _waiting; bool _wasWaiting;
    readonly SetupFolds _folds = new();   // the terrain card's folds outlive each rebuild
    /// <summary>The open setup tool, remembered for the session (spec 2026-11-21-setup-screen-layout-design.md → Tool rail).</summary>
    static readonly SetupTools Tools = new();
    SetupTopBar? _topBar; SetupRail? _rail; SetupToolPanel? _toolPanel; KeyHintBar? _hints; GameMenu? _gameMenu; SetupPieceKeys? _pieceKeys;
    SubViewportContainer _tableBox = null!; Control _hud = null!; TerrainCard? _terrainCard; Viewport.GuiFocusChangedEventHandler? _onFocus;   // the signal's own delegate type, not Action<Control>
    LoadingCurtain _curtain = null!; LoadingPanel _strip = null!; readonly LoadingGate _stripGate = new(TimeSpan.FromMilliseconds(200)); bool _entered;
    Action<RenderCause>? _onFrame; Action<Refused>? _onRefused; Action<SocketClosed, bool>? _onLost; Action? _onTheme, _onLayout;

    public override void _Ready()
    {
        _session = Main.Instance.Session;
        // The table's box and the lobby's page, each placed by Layout: the table fills play, setup's frame slot (HudLayout.Setup's Body) in setup.
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore }; root.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(root);
        var viewportContainer = _tableBox = new SubViewportContainer { Stretch = true, MouseTarget = true };
        viewportContainer.SetAnchorsPreset(LayoutPreset.FullRect);
        var viewport = new SubViewport { HandleInputLocally = true }; viewportContainer.AddChild(viewport); root.AddChild(viewportContainer);
        Table = GD.Load<PackedScene>("res://scenes/TableView.tscn").Instantiate<TableView>(); viewport.AddChild(Table);
        Input = new TableInput(); Table.AddChild(Input);
        viewportContainer.MouseExited += () => Input.ClearHover();   // the pointer over a Hud part is off the table: no hovered unit, no enemy strip
        // A setup terrain tile dragged from the Terrain tool lands where it is dropped on the table (SetupTerrainTool.DropTile). MouseTarget
        // makes the container itself the drop target: without it Godot looks for a Control inside the SubViewport, finds none, and refuses the drop.
        viewportContainer.SetDragForwarding(Callable.From((Vector2 _) => default(Variant)),
            Callable.From((Vector2 at, Variant data) => TileOf(data) is not null && Terrain is not null && Input.TableAt(at) is not null),
            Callable.From((Vector2 at, Variant data) => { if (TileOf(data) is { } name && Terrain is { } tools && Input.TableAt(at) is { } p) tools.DropTile(name, new Tabletop.Geometry.Point2(p.X, p.Y)); }));
        var overlay = new PresenceOverlay(); Table.AddChild(overlay);
        // The lobby's page under the top bar: the team columns and Game settings, scrolling (spec 2026-11-21-setup-screen-layout-design.md → Lobby).
        _sideColumn = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve, Visible = false }; root.AddChild(_sideColumn);
        var inset = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _sideColumn.AddChild(inset);
        foreach (var edge in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" }) inset.AddThemeConstantOverride(edge, Space.XL);   // a full-screen page: room to breathe
        inset.AddChild(_side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _side.AddChild(_selection = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart });
        // Over the table in every status, after the root so it draws on top and hears keys first: Home and the status in the
        // corner, the toasts top right. GameScreen.Play adds the play Hud to it; Layout places all of it.
        _hud = new Control { MouseFilter = MouseFilterEnum.Ignore }; _hud.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(_hud);
        _hud.AddChild(_corner = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore });
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; _corner.AddChild(top);
        top.AddThemeConstantOverride("separation", Space.S);
        _home = new Button { Icon = Ui.Icon("home"), TooltipText = "Home", FocusMode = FocusModeEnum.None, ExpandIcon = false };
        _home.Text = _home.Icon is null ? "Home" : "";   // never a zero-width button
        _home.AddThemeConstantOverride("icon_max_width", 22);
        _home.Pressed += () => Guarded.Run(_session.DisconnectAsync, Toast); top.AddChild(_home);
        // The game's name clips with an ellipsis inside the corner's slot; the status chip and a reconnect after it always show.
        top.AddChild(_status = new Label { ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Pass, VerticalAlignment = VerticalAlignment.Center });
        top.AddChild(_chip = new PanelContainer { ThemeTypeVariation = Variation.Chip, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore, Visible = false });
        _chip.AddChild(_chipText = new Label());
        top.AddChild(_statusTail = new Label { ThemeTypeVariation = Variation.Warning, VerticalAlignment = VerticalAlignment.Center });
        _hud.AddChild(_toasts = new Toasts());
        _hud.AddChild(_strip = new LoadingPanel { Strip = true, Visible = false });
        _onLayout = QueueLayout;
        _hud.Resized += _onLayout;
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
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board)) != 0) { ShowStatus(); if (_wasWaiting != _session.WaitingTeams.Count > 0) Rebuild(); else { _waiting?.Refresh(); Callable.From(ShowSelection).CallDeferred(); } } };
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

    public override void _Process(double delta) { UpdateLoading(); UpdateHints(); }

    /// <summary>The key hint bar hides while a text field has the focus.</summary>
    void UpdateHints()
    {
        if (_hints is not { } h || !IsInstanceValid(h)) return;
        var shown = GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit or SpinBox);
        if (h.Visible != shown) h.Visible = shown;
    }

    /// <summary>The Terrain tool while it is open: a tile dropped on the table lands there.</summary>
    SetupTerrainTool? Terrain => _toolPanel is { } p && IsInstanceValid(p) && p.Body is SetupTerrainTool t && IsInstanceValid(t) ? t : null;

    readonly LoadSettled _settled = new();
    /// <summary>The curtain while entering the game, then the strip over the table: a reconnect, the models still loading.</summary>
    void UpdateLoading()
    {
        var entry = LoadingView.Entry(new EntryState(_session.Phase, _session.Game?.Status, _session.Draft is not null, _session.Projection is not null,
            _session.CatchingUp, _session.GameId is null || Table.SlabReady, _entered));
        if (entry is null && _session.Phase == SessionPhase.Connected) _entered = true;
        _curtain.Set(entry, CurtainHead.Of(_session.Game));
        var meshes = _session.GameId is null ? null : Table.MeshProgress;
        var strip = LoadingView.Strip(_session.Phase, _entered, meshes?.Done ?? 0, meshes?.Total ?? 0);
        // The table stands with its models: the loading's garbage (tens of megabytes) is collected now, not in a later frame of play.
        // First the unit card makes its controls (UnitCard.Prebuild) and a move is measured once (MoveWarmup), so the first selection and the
        // first move Ask of the session neither make controls nor compile code.
        if (_session.GameId is not null && _settled.Update(_entered, meshes?.Done ?? 0, meshes?.Total ?? 0)) { _card?.Prebuild(); if (_session.Projection is { } p) MoveWarmup.Run(p, Table.Volumes, Table.Size); GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); }
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
        _lobby?.QueueFree(); _lobby = null;
        FreeSetupFrame();
        _wasWaiting = game is not null && _session.WaitingTeams.Count > 0;
        // The waiting panel is kept and refreshed, not rebuilt: a game update that leaves its lines alone keeps an open army dropdown open.
        if (!_wasWaiting) { _waiting?.QueueFree(); _waiting = null; }
        if (game is null) return;
        var playing = game.Status is GameStatus.Playing or GameStatus.Ended;
        // The lobby has no table to edit: its page takes the window under the top bar; setup puts the table back in its frame.
        var lobbyOnly = game.Status == GameStatus.Lobby;
        _sideColumn.Visible = lobbyOnly;
        _tableBox.Visible = !lobbyOnly;
        if (_wasWaiting)
        {
            if (_waiting is null) { _hud.AddChild(_waiting = new WaitingPanel()); _waiting.MinimumSizeChanged += QueueLayout; }
            else _waiting.Refresh();
            QueueLayout();
        }
        if (!playing)
        {
            BuildSetupFrame(game.Status == GameStatus.Setup);
            if (lobbyOnly)
            {
                // The lobby: the team columns across the window, Game settings in a column of its own on the right.
                var page = new HBoxContainer(); page.AddThemeConstantOverride("separation", Space.XL);
                page.AddChild(new TeamColumnsPanel(compact: false) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkBegin, Report = Toast });
                page.AddChild(new LobbyPanel { CustomMinimumSize = new Vector2(380, 0), SizeFlagsVertical = SizeFlags.ShrinkBegin });
                _side.AddChild(_lobby = page);
            }
        }
        ShowPlayPanels(playing);
        QueueLayout();
    }

    /// <summary>Lobby and setup's frame (spec 2026-11-21-setup-screen-layout-design.md): the top bar and the game menu in both; in setup the tool
    /// rail, the open tool's panel, the key hint bar and the table's piece keys. Under the corner, the toasts and the cards.</summary>
    void BuildSetupFrame(bool setup)
    {
        var under = new List<Control>();
        under.Add(_topBar = new SetupTopBar());
        _topBar.MenuRequested += () => _gameMenu?.Open();
        _topBar.TeamsRequested += () => OpenTool(SetupTool.Teams, show: true);
        if (setup)
        {
            under.Add(_rail = new SetupRail());
            _rail.Pressed += t => OpenTool(t, show: false);
            under.Add(_toolPanel = new SetupToolPanel(this));
            under.Add(_hints = new KeyHintBar());
            AddChild(_pieceKeys = new SetupPieceKeys(this));
        }
        for (var i = 0; i < under.Count; i++) { _hud.AddChild(under[i]); _hud.MoveChild(under[i], i); under[i].MinimumSizeChanged += QueueLayout; }
        _hud.AddChild(_gameMenu = new GameMenu
        {
            TableBusy = () => Input.Selected is not null || Input.Holding || Input.HasPicks,
            CanLeave = () => _session.ParticipantId is { } me && me != Guid.Empty,
            Options = () => OptionsScreen.Open(this),
            Leave = ConfirmLeave,
            Home = () => Guarded.Run(_session.DisconnectAsync, Toast),
        });
        if (setup) { _toolPanel!.Open(Tools.Open); _rail!.Show(Tools.Open); }
    }

    /// <summary>A rail press toggles its tool; a team chip opens Teams whatever is open.</summary>
    void OpenTool(SetupTool tool, bool show)
    {
        if (_toolPanel is not { } panel || !IsInstanceValid(panel)) return;
        if (show) Tools.Show(tool); else Tools.Press(tool);
        panel.Open(Tools.Open); _rail?.Show(Tools.Open);
        QueueLayout();
    }

    void FreeSetupFrame()
    {
        foreach (var node in new Node?[] { _topBar, _rail, _toolPanel, _hints, _gameMenu, _pieceKeys })
            if (node is not null && IsInstanceValid(node)) { node.GetParent()?.RemoveChild(node); node.QueueFree(); }
        _topBar = null; _rail = null; _toolPanel = null; _hints = null; _gameMenu = null; _pieceKeys = null;
    }

    /// <summary>The corner's line (CornerLine): the name, clipped to the corner's slot with its whole text as the tooltip, the status chip,
    /// then a reconnect. Only a change relays out.</summary>
    void ShowStatus()
    {
        var line = CornerLine.Of(_session.Phase, _session.ReconnectAttempt, _session.Game, _session.Projection?.Scheduler.Round ?? 0, _session.IsWatching, _session.Replaying);
        if (_status.Text == line.Name && _chipText.Text == (line.Chip ?? "") && _statusTail.Text == line.Tail) return;
        _status.Text = line.Name; _status.TooltipText = line.Name; _statusTail.Text = line.Tail; _statusTail.Visible = line.Tail != "";
        _chipText.Text = line.Chip ?? ""; _chip.Visible = line.Chip is not null;
        QueueLayout();   // a clipped label's minimum does not follow its text
    }

    /// <summary>A theme switch: the table's team colours, and the undo button's theme, which its CanvasLayer does not inherit. Nothing is
    /// recreated, so the setup toolbar's input survives; the Hud parts and the team columns re-render themselves.</summary>
    void OnTheme()
    {
        if (_session.GameId is not null) Table.Redraw();   // the table is bound only for a game
        if (_undo is not null && IsInstanceValid(_undo)) _undo.Theme = Ui.Theme;
        _home.Icon = Ui.Icon("home"); _home.Text = _home.Icon is null ? "Home" : "";
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
