using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

/// <summary>
/// Play (spec → Layout): the Hud parts float over the table, the overlays draw on it, and the events between them meet here.
/// Everything is built once when play starts and freed when the screen leaves play.
/// </summary>
public partial class GameScreen
{
    HeaderBar? _header; UnitCard? _card; DecisionBox? _decision; DiceTray? _dice; LogDrawer? _logDrawer; UndoButton? _undo; EndMessage? _end; KeyOverlay? _keys;
    HoverLabels? _hover; SightLines? _sight; TargetLines? _targets; BattleFx? _fx; SoundPlayer? _sounds; ViolationMarks? _violations; CanvasLayer? _tableLayer;
    Animator? _animator; PieceMenu? _menu; bool _layoutQueued;
    Guid? _acting;
    ReplayBar? _replay;
    Roster? _roster; TurnBanner? _banner; UnitMarkerNodes? _markers; FloatingText? _floating; RangeRingNode? _rings; ModelMarkNodes? _modelMarks; Action<IReadOnlyList<GameEvent>>? _onFollow;
    // Input outlives the play parts: its subscriptions are fields and come off when they go.
    Action<Guid, Vector2>? _onPieceRight; Action<Vector3>? _onTableRight; Action<MenuItem>? _onItem;
    readonly MenuItem _leave = new("Leave the game", MenuInput.None, _ => null);

    partial void ShowPlayPanels(bool playing)
    {
        if (!playing) { FreePlayPanels(); return; }
        if (_header is not null) return;

        // Hud parts, bottom of the input order first: the end message and the key overlay are added last and hear keys first.
        _hud.AddChild(_header = new HeaderBar()); _header.Bind(_session, Table, Input);
        _hud.AddChild(_roster = new Roster()); _roster.Bind(_session, Input);
        _hud.AddChild(_card = new UnitCard()); _card.Bind(_session, Table, Input);
        _hud.AddChild(_decision = new DecisionBox()); _decision.Bind(_session, Table, Input);
        _hud.AddChild(_dice = new DiceTray()); _dice.Bind(_session, Table, Input);
        _hud.AddChild(_logDrawer = new LogDrawer()); _logDrawer.Bind(_session, Table, Input);
        _hud.AddChild(_undo = new UndoButton()); _undo.Bind(_session, Input);
        if (_session.Replaying) { _hud.AddChild(_replay = new ReplayBar()); _replay.Bind(_session); }
        _dice.RollClicked += _logDrawer.OpenAt;
        _card.FocusChanged += id => { if (_logDrawer is { } d) d.CombatLog.FocusUnit = id; };
        _logDrawer.CombatLog.FocusUnit = _card.UnitId;
        _hud.AddChild(_banner = new TurnBanner()); _banner.Bind(_session);
        _hud.AddChild(_end = new EndMessage()); _end.Bind(_session, Table, Input);
        _hud.AddChild(_keys = new KeyOverlay()); _keys.Bind(_session, Table, Input);
        _decision.KeysRequested += () => { if (IsInstanceValid(_keys) && _keys.Visible) _keys.Visible = false; else KeyOverlay.Request(); };   // the card's ⌨

        // Table overlays: Node3D children of the table, and the markers and floating text in a canvas layer of the table's own viewport.
        Table.AddChild(_hover = new HoverLabels()); _hover.Bind(_session, Table, Input);
        Table.AddChild(_violations = new ViolationMarks()); _violations.Bind(Table, Input);
        Table.AddChild(_sight = new SightLines()); _sight.Bind(_session, Table, Input);
        Table.AddChild(_targets = new TargetLines()); _targets.Bind(_session, Table, Input);
        Table.AddChild(_rings = new RangeRingNode()); _rings.Bind(_session, Input); _rings.Covered = Covered;
        Table.AddChild(_modelMarks = new ModelMarkNodes()); _modelMarks.Bind(_session, Table);
        Table.AddChild(_tableLayer = new CanvasLayer());

        // The floating text subscribes before the Animator is made, so it hears each batch before the Animator plays it.
        _tableLayer.AddChild(_markers = new UnitMarkerNodes()); _markers.Bind(_session, Table, Input);
        _tableLayer.AddChild(_floating = new FloatingText()); _floating.Bind(_session, Table);
        AddChild(_sounds = new SoundPlayer()); _sounds.Bind(_session);
        Table.AddChild(_fx = new BattleFx()); _fx.Bind(_session, Table, _sounds);

        _animator = new Animator(_session, Table, Input);
        _menu = new PieceMenu(_session, Toast); AddChild(_menu);
        _menu.PickPieceRequested += Input.PickNextPiece; _menu.PickPointRequested += Input.PickNextPoint;
        _onItem = item =>
        {
            if (ReferenceEquals(item, _leave)) ConfirmLeave();                             // Leave is a management call, not a command
            else Input.Locks.Chosen(item);                                                // Unlock and Lock are local
        };
        _menu.ItemChosen += _onItem;
        // A watcher changes nothing (Session.IsWatching): no piece, team or roster menu. The table's menu offers Leave; a replay offers none.
        _onPieceRight = (id, at) => { if (MenuCtx() is { } ctx) _menu?.Open(Menus.Piece(ctx, id, Godot.Input.IsKeyPressed(Key.Shift)), at); };
        _onTableRight = at => { if (!_session.Replaying) _menu?.Open([_leave], GetViewport().GetMousePosition()); };
        Input.PieceRightClicked += _onPieceRight; Input.TableRightClicked += _onTableRight;

        // The parts die with the screen, so their own events need no unsubscribing.
        _header.TeamMenuRequested += OpenTeamMenu;
        _header.GameOverPressed += _end.Open;
        _header.SettingsRequested += () => OptionsScreen.Open(this);
        _card.ModelPicked += SelectModel;
        _card.Lit += pieces => { if (_decision is { } d && IsInstanceValid(d)) d.Light(pieces); };   // a weapon or group row under the pointer lights its models
        _decision.TargetsChanged += ShowTargets;
        _logDrawer.Reported += Toast;
        _undo.Reported += Toast;
        _animator.Played += _floating.Show;
        _roster.UnitPicked += id => { Input.Select(id); Input.Focus(id); };
        _roster.UnitMenuRequested += (id, at) => { if (MenuCtx() is { } ctx) _menu?.Open(Menus.Roster(ctx, id), at); };
        _end.LeaveRequested += () => Guarded.Run(_session.DisconnectAsync, Toast);
        // Follow (spec → Camera): an action frame starting glides to its actor, unless the player moved the camera in the last 2 s
        // or holds a unit; never for the history replayed on join.
        _onFollow = batch =>
        {
            if (_session.CatchingUp || _session.Projection is not { } p) return;
            if (CameraFocus.FollowTarget(batch, p) is { } at) Table.Camera.Follow(WorldFrame.ToGodot(at), _session.Settings.CameraFollow);
            // Playing both sides: when the acting team changes between activations, the camera goes to that team's side of the table, by
            // the same follow rule; inside a frame (a melee's Asks to either team) it stays.
            var acting = _session.MyTeamId;
            if (_session.MyTeamIds.Count > 1 && _session.Game is { } g && CameraFocus.SideOnSwitch(p, g, _acting, acting, _session.Zones) is { } side)
                Table.Camera.Follow(WorldFrame.ToGodot(side), _session.Settings.CameraFollow);
            _acting = acting;
        };
        _session.EventsApplied += _onFollow;
        foreach (var part in new Control?[] { _header, _roster, _card, _decision, _dice, _replay }) if (part is not null) { part.MinimumSizeChanged += QueueLayout; part.VisibilityChanged += QueueLayout; }
        _logDrawer.OpenChanged += QueueLayout;
        foreach (var button in new Control[] { _undo, _logDrawer.Toggle }) button.MinimumSizeChanged += QueueLayout;   // a theme's taller button face moves the column's foot
        QueueLayout();
    }

    /// <summary>Lay the Hud out once, at the end of this frame, however many changes asked for it. A layout that keeps changing a
    /// part's minimum size would queue itself again inside the same deferred flush, which Godot drains until its message queue
    /// overflows; past <see cref="LayoutPasses"/> passes in one frame the next pass waits for the next frame.</summary>
    void QueueLayout()
    {
        if (_layoutQueued) return;
        _layoutQueued = true;
        var frame = Engine.GetProcessFrames();
        if (frame != _layoutFrame) { _layoutFrame = frame; _layoutPasses = 0; }
        if (++_layoutPasses <= LayoutPasses) { Callable.From(Layout).CallDeferred(); return; }
        if (_layoutPasses == LayoutPasses + 1) GD.PushWarning($"Hud layout did not settle in {LayoutPasses} passes this frame; continuing next frame. Changed in the last pass: {_layoutChanged}");
        GetTree().Connect(SceneTree.SignalName.ProcessFrame, Callable.From(Layout), (uint)ConnectFlags.OneShot);
    }
    const int LayoutPasses = 8;   // the first frame of play settles in five or six: the header wraps, then the decision box, then the dice
    ulong _layoutFrame; int _layoutPasses;
    // Each part's minimum size, size and visibility after the last pass, so an unsettled layout names what keeps changing.
    readonly Dictionary<string, string> _layoutSeen = new(); string _layoutChanged = "";

    void NoteLayout()
    {
        var changed = new List<string>();
        foreach (var (name, part) in new (string, Control?)[] { ("corner", _corner), ("toasts", _toasts), ("header", _header), ("roster", _roster), ("card", _card), ("terrain", _terrainCard),
            ("decision", _decision), ("dice", _dice), ("log", _logDrawer), ("undo", _undo), ("replay", _replay) })
        {
            if (part is null || !IsInstanceValid(part)) continue;
            var now = $"min {part.GetCombinedMinimumSize()} size {part.Size} at {part.Position} visible {part.Visible}";
            if (_layoutSeen.GetValueOrDefault(name) != now) changed.Add($"{name} [{_layoutSeen.GetValueOrDefault(name) ?? "-"} -> {now}]");
            _layoutSeen[name] = now;
        }
        _layoutChanged = changed.Count == 0 ? "nothing" : string.Join("; ", changed);
    }

    /// <summary>Every Hud part into its HudLayout slot (spec → Layout), over the width the side panel leaves. Outside play only the
    /// corner, the toasts and the terrain card exist. The unit card (or the terrain card, in the same slot) starts 8 px below the roster's actual bottom, so a collapsed roster gives it the
    /// column back; the dice end 8 px above the decision box's actual top; the end message stays a full-screen backdrop.</summary>
    void Layout()
    {
        _layoutQueued = false;
        if (!IsInstanceValid(_hud) || _hud.Size.X <= 0 || _hud.Size.Y <= 0) return;
        var w = _hud.Size.X - (IsInstanceValid(_sideColumn) && _sideColumn.Visible && _tableBox.Visible ? _sideColumn.Size.X : 0);   // the lobby's panel fills the window
        if (_setup is SetupToolbar tools && IsInstanceValid(tools)) tools.FitList(_hud.Size.Y, _sideHead.Size.Y);   // the terrain list fills what the window and the head leave
        if (IsInstanceValid(_sideHead)) _sideHead.AddThemeConstantOverride("margin_right", DecisionBox.ScrollGap + (int)_sidePanel.GetVScrollBar().Size.X);   // the head's right edge on the body's
        var logOpen = _logDrawer is { } l && IsInstanceValid(l) && l.Open;
        var buttonH = _undo is { } u && IsInstanceValid(u) ? Math.Max(u.GetCombinedMinimumSize().Y, _logDrawer?.Toggle.GetCombinedMinimumSize().Y ?? 0) : HudLayout.LogButtonH;
        var replayH = Shows(_replay) ? _replay!.GetCombinedMinimumSize().Y : HudLayout.ReplayH;   // its three rows in the theme's font: the left column ends above it
        // The unit card and the terrain card share the slot: SheetFocus names no unit while terrain is selected.
        var visible = new HudVisible(Shows(_roster), Shows(_card) || Shows(_terrainCard), Shows(_decision) || Shows(_waiting), Shows(_dice), logOpen, Shows(_replay));
        // The header draws the fullest fit for its slot's width, then its slot takes the height that fit draws at: the right column starts below it.
        if (Shows(_header)) _header!.FitTo(HudLayout.Of(w, _hud.Size.Y, visible, buttonH, replayH)[HudPart.Header].W);
        var headerH = Shows(_header) ? _header!.GetCombinedMinimumSize().Y : HudLayout.HeaderH;
        var r = HudLayout.Of(w, _hud.Size.Y, visible, buttonH, replayH, headerH);
        if (_dice is { } tray && IsInstanceValid(tray)) tray.Covered = logOpen;   // the open log holds the latest roll: the tray gives its slot up
        PlaceCorner(r[HudPart.Corner]);
        HudPlacement.Top(_toasts, r[HudPart.Toasts]);
        if (Shows(_strip)) HudPlacement.Bottom(_strip, r[HudPart.Loading]);
        if (Shows(_header)) HudPlacement.Top(_header!, r[HudPart.Header]);
        if (Shows(_roster)) _roster!.Place(r[HudPart.Roster]);
        if (r.TryGetValue(HudPart.UnitCard, out var cardSlot))
        {
            if (Shows(_roster)) cardSlot = HudLayout.Below(cardSlot, _roster!.Position.Y + _roster.Size.Y);
            if (Shows(_card)) _card!.Place(cardSlot);
            if (Shows(_terrainCard)) _terrainCard!.Place(cardSlot);
        }
        if (Shows(_decision)) _decision!.Place(r[HudPart.Decision]);
        if (Shows(_waiting)) HudPlacement.Bottom(_waiting!, r[HudPart.Decision]);   // waiting for players: the team columns in the decision slot
        if (Shows(_replay)) _replay!.Place(r[HudPart.Replay]);
        if (Shows(_dice) && r.TryGetValue(HudPart.Dice, out var slot))   // the open log covers the tray: HudLayout gives it no slot
        {
            if (Shows(_decision)) slot = slot with { Y = _decision!.Position.Y - HudLayout.M - slot.H };
            HudPlacement.Bottom(_dice!, slot);
        }
        if (_logDrawer is { } log && IsInstanceValid(log))
            log.Place(r[HudPart.LogButton], r.TryGetValue(HudPart.LogPanel, out var panel) ? AboveBottomStack(BelowHeader(panel)) : null);
        if (_undo is { } undo && IsInstanceValid(undo)) HudPlacement.Bottom(undo, r[HudPart.UndoButton]);
        // Placed while hidden too: the banner shows itself between layouts, and nothing queues one when it does.
        if (_banner is { } banner && IsInstanceValid(banner)) HudPlacement.Fill(banner, r[HudPart.Banner]);
        NoteLayout();
    }

    readonly List<HudRect> _covered = new();

    /// <summary>The Hud's panels as they stand over the table, in the table viewport's pixels: the ring labels keep off them. The banner, the
    /// end message and the toasts come and go over everything and are left out.</summary>
    IReadOnlyList<HudRect> Covered()
    {
        _covered.Clear();
        if (!IsInstanceValid(_tableBox)) return _covered;
        var origin = _tableBox.GlobalPosition;
        foreach (var part in new Control?[] { _corner, _header, _roster, _card, _terrainCard, _decision, _waiting, _dice, _undo, _replay, _logDrawer?.Toggle, _logDrawer?.Panel })
            if (part is not null && IsInstanceValid(part) && part.IsVisibleInTree() && part.GetGlobalRect() is var r && r.Size.X > 0 && r.Size.Y > 0)
                _covered.Add(new HudRect(r.Position.X - origin.X, r.Position.Y - origin.Y, r.Size.X, r.Size.Y));
        return _covered;
    }

    /// <summary>The corner keeps to its slot's width: the game's name gives up what Home and the tail take, and clips.</summary>
    void PlaceCorner(HudRect slot)
    {
        var others = _corner.GetCombinedMinimumSize().X - _status.GetCombinedMinimumSize().X;   // Home, the tail and the gaps
        var full = _status.GetThemeFont("font").GetStringSize(_status.Text, HorizontalAlignment.Left, -1, _status.GetThemeFontSize("font_size")).X;
        var width = new Vector2(MathF.Ceiling(Math.Min(full, Math.Max(0, slot.W - others))), 0);
        if (_status.CustomMinimumSize != width) _status.CustomMinimumSize = width;   // only a change: each set queues another layout
        HudPlacement.Top(_corner, slot);
    }

    /// <summary>The log panel ends HudLayout.M above the decision box and dice tray as they are, not as their slots assume: either
    /// may grow upward past its slot, and a short decision box leaves the log the room above it, up to HudLayout.LogMaxH. Only
    /// when the panel's columns span theirs.</summary>
    HudRect AboveBottomStack(HudRect panel)
    {
        float? top = null;
        foreach (var part in new Control?[] { _decision, _waiting, _dice })
            if (Shows(part) && panel.SpansX(new HudRect(part!.Position.X, 0, part.Size.X, 1))) top = Math.Min(top ?? float.MaxValue, part.Position.Y);
        var bottom = top is { } t ? t - HudLayout.M : panel.Bottom;
        return panel with { H = Math.Clamp(bottom - panel.Y, 0, HudLayout.LogMaxH) };
    }

    /// <summary>The log panel starts HudLayout.M below the header as it is, not as its 96 px slot assumes: three lines can be
    /// taller. Only when the panel's columns span the header's; the height never goes negative.</summary>
    HudRect BelowHeader(HudRect panel)
    {
        if (!Shows(_header) || !panel.SpansX(new HudRect(_header!.Position.X, 0, _header.Size.X, 1))) return panel;
        var top = Math.Max(panel.Y, _header.Position.Y + _header.Size.Y + HudLayout.M);
        return panel with { Y = top, H = Math.Max(0, panel.Bottom - top) };
    }

    static bool Shows(Control? c) => c is not null && IsInstanceValid(c) && c.Visible;

    void FreePlayPanels()
    {
        if (Input is not null && IsInstanceValid(Input)) { Input.PieceRightClicked -= _onPieceRight; Input.TableRightClicked -= _onTableRight; }
        if (_onFollow is not null) _session.EventsApplied -= _onFollow;
        _onFollow = null;
        _animator?.Dispose(); _animator = null;
        foreach (var node in new Node?[] { _replay, _header, _roster, _card, _decision, _dice, _logDrawer, _undo, _banner, _end, _keys, _hover, _violations, _sight, _targets, _fx, _sounds, _rings, _modelMarks, _markers, _floating, _tableLayer, _menu })
            if (node is not null && IsInstanceValid(node)) node.QueueFree();
        _header = null; _card = null; _decision = null; _dice = null; _logDrawer = null; _end = null; _keys = null;
        _hover = null; _violations = null; _sight = null; _targets = null; _fx = null; _sounds = null; _tableLayer = null; _undo = null; _menu = null;
        _replay = null; _roster = null; _banner = null; _markers = null; _floating = null; _rings = null; _modelMarks = null;
        _onPieceRight = null; _onTableRight = null; _onItem = null;
    }

    /// <summary>Leave gives the seat up for good, unlike Home, which keeps it: asked once, Esc cancels.</summary>
    void ConfirmLeave()
    {
        var d = new ConfirmationDialog
        {
            Title = "Leave this game?", OkButtonText = "Leave", CancelButtonText = "Stay",
            DialogText = "Leaving gives up your seat in this game. Home closes the game and keeps your seat.", DialogAutowrap = true,
        };
        d.Confirmed += () => Guarded.Run(_session.LeaveAsync, Toast);
        d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        AddChild(d); d.PopupCentered(new Vector2I(380, 0));
    }

    /// <summary>The team menu, from the header's team names (spec → Layout): what the team panel's right-click opened.</summary>
    void OpenTeamMenu(Guid teamId, Vector2 at)
    {
        if (MenuCtx() is { } ctx) _menu?.Open(Menus.Team(ctx, teamId), at);
    }

    /// <summary>A unit card group row's click (ModelGroup.Next) selects that one model on the table (spec → Unit card).</summary>
    void SelectModel(Guid modelId)
    {
        if (_session.Projection?.Models.GetValueOrDefault(modelId) is { } model) Input.Select(model.UnitId, modelId);
    }

    void ShowTargets()
    {
        if (_targets is null || !IsInstanceValid(_targets)) return;
        if (_decision?.Targets is { } t) _targets.Show(t.Groups, t.Assigned); else _targets.Clear();
        if (_sight is not null && IsInstanceValid(_sight))
            _sight.ShowAssigned(_decision?.Targets?.Groups ?? [], _decision?.Targets is { } a ? new Dictionary<string, Guid>(a.Assigned) : new Dictionary<string, Guid>());
    }

    /// <summary>What the menus read, built once per open; null before the game and its projection arrive.</summary>
    MenuContext? MenuCtx() => _session.Game is { } game && _session.Projection is { } p
        ? new MenuContext(game, p, _session.Log, _session.IsWatching, _session.MyTeamId, AskViewModel.Mine(p, game, _session.MyTeamId),
            _session.Grantable, _session.DeployInside, _session.MaySetAside, Input.Locks)
        : null;
}
