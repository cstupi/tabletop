using Godot; using Tabletop.Client.Core; using Tabletop.Client.Hud; using Tabletop.Client.Theming;
namespace Tabletop.Client.Table;

/// <summary>
/// The action buttons over the selected unit during my activation (spec 2026-11-21-actions-over-unit-design.md, UnitActionsView): a round icon
/// button per option the decision box offers that unit, in the box's order, centred above its tallest model and above its marker row, following
/// the camera each frame, inside the table's view and off the Hud's panels (UnitActionsPlace). A click answers as the box's tile does; the
/// pointer on a button lights its tile, and on a tile lights its button. Its CanvasLayer inherits no theme, so it sets its own.
/// </summary>
public partial class UnitActionsNode : Control
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!; DecisionBox _box = null!;
    readonly HBoxContainer _row = new() { MouseFilter = MouseFilterEnum.Ignore };
    readonly List<(Button Button, OptionTile Option)> _buttons = new();
    readonly List<HudRect> _covered = new();
    UnitActions? _shown; string? _lit; bool _dirty = true; UnitMarkerNodes? _markers; HoverLabels? _labels; int _labelLines = 1;
    Action? _onDirty, _onTheme; Action<Guid>? _onUnit; Action<string?>? _onTile;

    const int Size = 42, IconSize = 26;   // a button's size, not spacing: the decision tile's 26 px icon in an 8 px inset

    public void Bind(Session session, TableView view, TableInput input, DecisionBox box)
    {
        _s = session; _view = view; _input = input; _box = box;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore;
        _row.AddThemeConstantOverride("separation", Space.XS); _row.Visible = false; AddChild(_row);
        _onDirty = () => _dirty = true; _onUnit = _ => _dirty = true; _onTile = Light;
        _onTheme = () => { Theme = Ui.Theme; Clear(); _dirty = true; };
        box.Rendered += _onDirty; input.SelectionChanged += _onDirty; input.UnitClicked += _onUnit; box.TileHovered += _onTile; Ui.Changed += _onTheme;
        _onTheme();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_box is not null && IsInstanceValid(_box)) { _box.Rendered -= _onDirty; _box.TileHovered -= _onTile; }
        if (_input is not null && IsInstanceValid(_input)) { _input.SelectionChanged -= _onDirty; _input.UnitClicked -= _onUnit; }
    }

    void Clear() { foreach (var (b, _) in _buttons) b.QueueFree(); _buttons.Clear(); _shown = null; }

    /// <summary>Which buttons stand now: once per frame after a change, so a click's selection and the box's narrowing land first.</summary>
    void Render()
    {
        _dirty = false;
        var p = _s.Projection; var game = _s.Game;
        var v = p is null || game is null ? null : UnitActionsView.Of(p, game, _s.MyTeamId, _input.Selected?.PieceId, _s.IsWatching, _box.Numbered);
        if (v is not null && !_box.Answering(v.AskId)) v = null;                            // the answer is sent: the buttons go with it
        if (v is null) { if (_lit is not null) { _box.HoverOption(null); _lit = null; } _shown = null; _row.Visible = false; return; }
        if (_shown is null || _shown.AskId != v.AskId || !Looks(_shown).SequenceEqual(Looks(v)))
        {
            foreach (var (b, _) in _buttons) b.QueueFree();
            _buttons.Clear();
            foreach (var o in v.Options) _buttons.Add((Make(o), o));
        }
        _shown = v;
        // The name label's lines as HoverLabels draws them over this unit: the name, and the wounds line when it has one.
        _labelLines = HoverView.Of(p!, game!, new Names(game!, p), v.UnitId, null, _s.MaxWounds) is { } info && HoverView.WoundsLine(info.Wounds) is not null ? 2 : 1;
        Light(_lit);
    }

    static IEnumerable<(string, int, string, string?)> Looks(UnitActions v) => v.Options.Select(o => (o.OptionId, o.Key, o.Word, o.Icon));

    Button Make(OptionTile o)
    {
        var b = new Button
        {
            FocusMode = FocusModeEnum.None, ThemeTypeVariation = Variation.RoundTile, CustomMinimumSize = new Vector2(Size, Size), TooltipText = UnitActions.Tip(o),
            Icon = o.Icon is { } icon ? Ui.Icon(icon) : null, IconAlignment = HorizontalAlignment.Center, ExpandIcon = true,
        };
        if (b.Icon is null) b.Text = o.Word;
        var id = o.OptionId;
        b.Pressed += () => { if (_box.Choose(id)) _dirty = true; };
        b.MouseEntered += () => { _lit = id; _box.HoverOption(id); Light(id); };
        b.MouseExited += () => { if (_lit == id) { _lit = null; _box.HoverOption(null); Light(null); } };
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

    public override void _Process(double delta)
    {
        if (_dirty) Render();
        if (_shown is not { } v) return;
        var cam = _view.Camera; var top = WorldFrame.ToGodot(v.Anchor);
        if (cam.IsPositionBehind(top)) { _row.Visible = false; return; }
        var models = cam.UnprojectPosition(top).Y;
        if (Markers()?.RowRect(v.UnitId) is { } marks) models = Math.Min(models, marks.Position.Y);   // above the unit's marker row
        var size = _row.GetCombinedMinimumSize(); var screen = GetViewportRect().Size;
        var at = UnitActionsPlace.Rect(cam.UnprojectPosition(top).X, models, size.X, size.Y, new HudRect(0, 0, screen.X, screen.Y), Covered(), Space.S,
            Labels()?.NameBox(v.UnitId, _labelLines));                                                   // above its name label, shown or not
        _row.Position = new Vector2(at.X, at.Y); _row.Size = size; _row.Visible = true;
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

    /// <summary>The Hud's panels over the table, in the table viewport's pixels: the box's siblings as drawn, a full-window layer's own children.</summary>
    IReadOnlyList<HudRect> Covered()
    {
        _covered.Clear();
        if (_box.GetParent() is not Control hud || _view.GetViewport().GetParent() is not Control table) return _covered;
        var origin = table.GlobalPosition; var whole = hud.Size;
        void Add(Node parent, int depth)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child is not Control c || !c.IsVisibleInTree()) continue;
                var r = c.GetGlobalRect();
                if (r.Size.X >= whole.X * 0.9f && r.Size.Y >= whole.Y * 0.9f) { if (depth < 2) Add(c, depth + 1); continue; }   // a layer over the window: its panels
                if (r.Size.X > 0 && r.Size.Y > 0) _covered.Add(new HudRect(r.Position.X - origin.X, r.Position.Y - origin.Y, r.Size.X, r.Size.Y));
            }
        }
        Add(hud, 0);
        return _covered;
    }
}
