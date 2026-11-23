using Godot; using Tabletop.Client.Core; using Tabletop.Client.Hud; using Tabletop.Client.Theming;
namespace Tabletop.Client.Table;

/// <summary>
/// The bar over the acting unit during every step of my activation (spec 2026-11-21-actions-over-unit-design.md, UnitActionsView): a round icon
/// button per option the decision box offers, in the box's order, then the card's foot actions (the primary lit, a dim one with its reason as
/// tooltip), centred above the unit's tallest model and above its marker row and name label, following the camera each frame, inside the table's
/// view and off the Hud's panels (UnitActionsPlace), held still while the pointer is over it or a drag or press is in progress and eased to its
/// place otherwise (UnitActionsHold). A click answers as the box's tile or button does; the pointer on an option lights its tile,
/// and on a tile lights its button. While I pick shooting targets, a target icon stands over each enemy unit I may shoot; a click on it is the
/// table's click on that unit. Its CanvasLayer inherits no theme, so it sets its own.
/// </summary>
public partial class UnitActionsNode : Control
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!; DecisionBox _box = null!;
    readonly HBoxContainer _row = new() { MouseFilter = MouseFilterEnum.Ignore };
    readonly List<(Button Button, OptionTile Option)> _buttons = new();
    readonly List<(Button Button, CardAction Action)> _acts = new();
    readonly List<(Button Button, UnitTarget Target)> _targets = new();
    readonly UnitActionsHold _hold = new();
    UnitActions? _shown; string? _lit; bool _dirty = true, _targetsDirty = true; UnitMarkerNodes? _markers; HoverLabels? _labels; int _labelLines = 1;
    Action? _onDirty, _onTheme, _onTargets; Action<Guid>? _onUnit; Action<string?>? _onTile;

    const int Size = 42, IconSize = 26;   // a button's size, not spacing: the decision tile's 26 px icon in an 8 px inset

    public void Bind(Session session, TableView view, TableInput input, DecisionBox box)
    {
        _s = session; _view = view; _input = input; _box = box;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore;
        _row.AddThemeConstantOverride("separation", Space.XS); _row.Visible = false; AddChild(_row);
        _onDirty = () => _dirty = true; _onUnit = _ => _dirty = true; _onTile = Light; _onTargets = () => _targetsDirty = true;
        _onTheme = () => { Theme = Ui.Theme; Clear(); _dirty = true; };
        box.Rendered += _onDirty; input.SelectionChanged += _onDirty; input.UnitClicked += _onUnit; box.TileHovered += _onTile; box.TargetsChanged += _onTargets;
        Ui.Changed += _onTheme;
        _onTheme();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is not null) _s.UnitBar = null;
        if (_box is not null && IsInstanceValid(_box)) { _box.Rendered -= _onDirty; _box.TileHovered -= _onTile; _box.TargetsChanged -= _onTargets; }
        if (_input is not null && IsInstanceValid(_input)) { _input.SelectionChanged -= _onDirty; _input.UnitClicked -= _onUnit; }
    }

    void Clear()
    {
        foreach (var (b, _) in _buttons) b.QueueFree(); _buttons.Clear();
        foreach (var (b, _) in _acts) b.QueueFree(); _acts.Clear();
        foreach (var (b, _) in _targets) b.QueueFree(); _targets.Clear();
        _shown = null; _targetsDirty = true;
    }

    /// <summary>Which buttons stand now: once per frame after a change, so a click's selection and the box's narrowing land first.</summary>
    void Render()
    {
        _dirty = false; _targetsDirty = true;
        var p = _s.Projection; var game = _s.Game;
        var v = p is null || game is null ? null : UnitActionsView.Of(p, game, _s.MyTeamId, _input.Selected?.PieceId, _s.IsWatching, _box.Numbered);
        if (v is not null && !_box.Answering(v.AskId)) v = null;                            // the answer is sent: the buttons go with it
        if (v is null) { if (_lit is not null) { _box.HoverOption(null); _lit = null; } _shown = null; _row.Visible = false; return; }
        if (_shown is null || _shown.AskId != v.AskId || !Looks(_shown).SequenceEqual(Looks(v)) || !_shown.Actions.SequenceEqual(v.Actions))
        {
            foreach (var (b, _) in _buttons) b.QueueFree(); _buttons.Clear();
            foreach (var (b, _) in _acts) b.QueueFree(); _acts.Clear();
            foreach (var o in v.Options) _buttons.Add((Make(o), o));
            foreach (var a in v.Actions) _acts.Add((MakeAct(a), a));
        }
        _shown = v;
        // The name label's lines as HoverLabels draws them over this unit: the name, and the wounds line when it has one.
        _labelLines = HoverView.Of(p!, game!, new Names(game!, p), v.UnitId, null, _s.MaxWounds) is { } info && HoverView.WoundsLine(info.Wounds) is not null ? 2 : 1;
        Light(_lit);
    }

    static IEnumerable<(string, int, string, string?)> Looks(UnitActions v) => v.Options.Select(o => (o.OptionId, o.Key, o.Word, o.Icon));

    Button Round(string? icon, string word, string tip)
    {
        var b = new Button
        {
            FocusMode = FocusModeEnum.None, ThemeTypeVariation = Variation.RoundTile, CustomMinimumSize = new Vector2(Size, Size), TooltipText = tip,
            Icon = icon is { } i ? Ui.Icon(i) : null, IconAlignment = HorizontalAlignment.Center, ExpandIcon = true,
        };
        if (b.Icon is null) b.Text = word;
        return b;
    }

    Button Make(OptionTile o)
    {
        var b = Round(o.Icon, o.Word, UnitActions.Tip(o));
        var id = o.OptionId;
        b.Pressed += () => { if (_box.Choose(id)) _dirty = true; };
        b.MouseEntered += () => { _lit = id; _box.HoverOption(id); Light(id); };
        b.MouseExited += () => { if (_lit == id) { _lit = null; _box.HoverOption(null); Light(null); } };
        _row.AddChild(b);
        return b;
    }

    /// <summary>A foot action of the card: pressed as its button on the card is; the primary lit.</summary>
    Button MakeAct(CardAction a)
    {
        var b = Round(a.Icon, a.Word, a.Tip);
        var id = a.Id;
        b.Pressed += () => { if (_box.Press(id)) _dirty = true; };
        if (a.Primary) b.ThemeTypeVariation = Variation.RoundTileLit;
        _row.AddChild(b);
        return b;
    }

    /// <summary>The button of the option lit elsewhere (its tile under the pointer), the rest plain.</summary>
    void Light(string? optionId)
    {
        foreach (var (b, o) in _buttons)
        {
            var variation = o.OptionId == optionId ? Variation.RoundTileLit : Variation.RoundTile;
            if (b.ThemeTypeVariation != variation) b.ThemeTypeVariation = variation;
        }
    }

    /// <summary>The foot actions enabled as the card's buttons are now (a move's gate, Fire once a target is aimed), dim with the reason.</summary>
    void SyncActs()
    {
        if (_acts.Count == 0) return;
        var live = _box.Actions;
        foreach (var (b, a) in _acts)
        {
            var now = live.FirstOrDefault(x => x.Id == a.Id) ?? a with { Enabled = false };
            if (b.Disabled != !now.Enabled) b.Disabled = !now.Enabled;
            var tip = now.Tip;
            if (b.TooltipText != tip) b.TooltipText = tip;
        }
    }

    /// <summary>The target icons: one per enemy unit the box's target assignment may aim at now, made again when it changes.</summary>
    void RenderTargets()
    {
        _targetsDirty = false;
        var want = _shown is not null && _box.Targets is { } t && _s.Projection is { } p ? UnitActionsView.TargetsOf(p, t.Candidates) : [];
        if (want.Select(x => x.UnitId).SequenceEqual(_targets.Select(x => x.Target.UnitId))) { for (var i = 0; i < want.Count; i++) _targets[i] = (_targets[i].Button, want[i]); return; }
        foreach (var (b, _) in _targets) b.QueueFree(); _targets.Clear();
        var names = _s.Game is { } game && _s.Projection is { } proj ? new Names(game, proj) : null;
        foreach (var target in want)
        {
            var word = names?.Unit(target.UnitId) ?? "Target";
            var b = Round("shoot", word, $"Aim at {word}");
            var unit = target.UnitId;
            b.Pressed += () => _box.Target(unit);
            b.MouseEntered += () => _input.HoverUnit(unit); b.MouseExited += () => _input.HoverUnit(null);
            AddChild(b); _targets.Add((b, target));
        }
    }

    public override void _Process(double delta)
    {
        if (_dirty) Render();
        if (_targetsDirty) RenderTargets();
        if (_shown is not { } v) { _hold.Reset(); _s.UnitBar = null; foreach (var (b, _) in _targets) b.Visible = false; return; }
        SyncActs();
        var cam = _view.Camera; var screen = GetViewportRect().Size; var view = new HudRect(0, 0, screen.X, screen.Y); var covered = _s.Hud.Obstacles;   // the Hud's panels (the play table fills the window)
        var top = WorldFrame.ToGodot(v.Anchor);
        if (cam.IsPositionBehind(top)) { _row.Visible = false; _hold.Reset(); }
        else
        {
            var models = cam.UnprojectPosition(top).Y;
            if (Markers()?.RowRect(v.UnitId) is { } marks) models = Math.Min(models, marks.Position.Y);   // above the unit's marker row
            var size = _row.GetCombinedMinimumSize();
            var at = UnitActionsPlace.Rect(cam.UnprojectPosition(top).X, models, size.X, size.Y, view, covered, Space.S,
                Labels()?.NameBox(v.UnitId, _labelLines));                                               // above its name label, shown or not
            // Held still while the pointer is over it or anything is held (a drag, a press): a drag moves the name label under it, and a
            // click aimed at Commit right after must land. Otherwise it eases to its place.
            var held = _row.Visible && new Rect2(_row.Position, _row.Size).Grow(Space.S).HasPoint(GetLocalMousePosition())
                || _input.EscState.Holding || Godot.Input.IsMouseButtonPressed(MouseButton.Left);
            at = _hold.Step(v.UnitId, at, held, (float)delta);
            _row.Position = new Vector2(at.X, at.Y); _row.Size = size; _row.Visible = true;
        }
        _s.UnitBar = _row.Visible ? new HudRect(_row.Position.X, _row.Position.Y, _row.Size.X, _row.Size.Y) : null;   // the labels over the table keep off it
        foreach (var (b, t) in _targets)
        {
            var over = WorldFrame.ToGodot(t.Anchor);
            if (cam.IsPositionBehind(over)) { b.Visible = false; continue; }
            var y = cam.UnprojectPosition(over).Y;
            if (Markers()?.RowRect(t.UnitId) is { } marks) y = Math.Min(y, marks.Position.Y);
            var at = UnitActionsPlace.Rect(cam.UnprojectPosition(over).X, y, Size, Size, view, covered, Space.S, Labels()?.NameBox(t.UnitId, 1));
            b.Position = new Vector2(at.X, at.Y); b.Size = new Vector2(Size, Size); b.Visible = true;
        }
    }

    HoverLabels? Labels()
    {
        if (_labels is not null && IsInstanceValid(_labels)) return _labels;
        _labels = _view.GetChildren().OfType<HoverLabels>().FirstOrDefault();
        return _labels;
    }

    UnitMarkerNodes? Markers()
    {
        if (_markers is not null && IsInstanceValid(_markers)) return _markers;
        _markers = _view.GetChildren().OfType<CanvasLayer>().SelectMany(l => l.GetChildren().OfType<UnitMarkerNodes>()).FirstOrDefault();
        return _markers;
    }
}
