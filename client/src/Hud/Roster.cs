using Godot; using Tabletop.Client.Core; using Tabletop.Client.Table; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Hud;

/// <summary>Left column, above the unit card (UX redesign part 2 → Roster): a header band per team (its icon, name, `★ you` on a team I hold,
/// the collapse chevron) and a row per unit, sorted by RosterView (may act, activated, off the table). A row: ▶ on the acting unit, its name,
/// ○ may act / ✓ activated / — off the table, its wound pips, its condition icons, and a ▸ on hover for the roster menu; the selected unit's
/// row lit at its edge. A click selects and centres the unit, a right-click or ▸ opens the menu, R collapses to the headers.</summary>
public partial class Roster : PanelContainer
{
    Session _s = null!; TableInput? _input; readonly VBoxContainer _list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; bool _collapsed;
    Action<RenderCause>? _onFrame; Action? _onChanged, _onSelection;
    HudRect? _slot; bool _fitQueued; ScrollContainer _scroll = null!;
    Guid? _selected;
    public event Action<Guid>? UnitPicked;
    public event Action<Guid, Vector2>? UnitMenuRequested;

    public void Bind(Session session, TableInput? input = null)
    {
        _s = session; _input = input;
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve }; AddChild(_scroll); _scroll.AddChild(_list);
        _list.AddThemeConstantOverride("separation", 2);
        _list.MinimumSizeChanged += QueueFit;                                                                           // a ScrollContainer reports no content height
        _onFrame = _ => Render(); _onChanged = () => { Clear(); Render(); };
        session.Frame.Render += _onFrame;                                                                              // every cause the frame carries
        Ui.Changed += _onChanged;                                                                                      // the theme's icons
        if (input is not null) { _onSelection = () => { if (input.Selected?.PieceId == _selected) return; _selected = input.Selected?.PieceId; Clear(); Render(); }; input.SelectionChanged += _onSelection; }
        Render();
    }
    public override void _ExitTree()
    {
        Ui.Changed -= _onChanged;
        if (_input is not null && IsInstanceValid(_input)) _input.SelectionChanged -= _onSelection;
        if (_s is null) return; _s.Frame.Render -= _onFrame;
    }
    /// <summary>Into its HudLayout slot: the slot's width, the list's height up to the slot's, against the slot's top.</summary>
    public void Place(HudRect slot) { _slot = slot; Fit(); HudPlacement.Top(this, slot); }

    void QueueFit() { if (_fitQueued) return; _fitQueued = true; Callable.From(() => { _fitQueued = false; Fit(); }).CallDeferred(); }

    void Fit()
    {
        if (_slot is not { } slot || _scroll is null || !IsInstanceValid(_scroll)) return;
        var chrome = GetCombinedMinimumSize().Y - _scroll.GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, MathF.Floor(Math.Max(0, Math.Min(_list.GetCombinedMinimumSize().Y, slot.H - chrome))));   // whole pixels, as DecisionBox.Fit
    }

    public override void _Input(InputEvent e) { if (KeyOverlay.IntentOf(e, GetViewport()) == Intent.ToggleRoster) { Collapse(); GetViewport().SetInputAsHandled(); } }

    void Collapse() { _collapsed = !_collapsed; Render(); }

    IReadOnlyList<RosterTeam>? _drawn; bool _drawnCollapsed;
    /// <summary>Per drawn row, its line and its chip line, by (team, row) index of <see cref="_drawn"/>.</summary>
    readonly List<List<(Control Line, Control? Chips)>> _rows = new();

    /// <summary>Only the rows whose content changed are rebuilt (RosterView.Changed); a new team line, unit order, the collapse or the
    /// selection rebuilds the list.</summary>
    void Render()
    {
        Visible = _s.Projection is not null && _s.Game is not null;
        if (_s.Projection is not { } p || _s.Game is not { } game) { Clear(); return; }
        var teams = RosterView.Of(p, game, new Names(game, p), _s.MyTeamId, _s.MaxWounds, _s.MyTeamIds);
        if (_drawnCollapsed == _collapsed && RosterView.Changed(_drawn, teams) is { } changed)
        {
            foreach (var (t, r) in changed) if (!_collapsed) Replace(t, r, teams[t].Rows[r]);
            _drawn = teams;
            return;
        }
        Clear();
        _drawn = teams; _drawnCollapsed = _collapsed;
        foreach (var team in teams)
        {
            _list.AddChild(Header(team));
            var lines = new List<(Control, Control?)>(); _rows.Add(lines);
            if (_collapsed) continue;
            foreach (var row in team.Rows)
            {
                var (line, chips) = Row(row);
                _list.AddChild(line); if (chips is not null) _list.AddChild(chips);
                lines.Add((line, chips));
            }
            _list.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4), MouseFilter = MouseFilterEnum.Ignore });
        }
    }

    void Clear() { foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); } _rows.Clear(); _drawn = null; }

    /// <summary>The team's band: its icon, its name, `★ you` on mine, the chevron that collapses the roster (R).</summary>
    Control Header(RosterTeam team)
    {
        var band = new PanelContainer(); band.AddThemeStyleboxOverride("panel", Ui.TeamBand(team.Ordinal, compact: true));
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 8); band.AddChild(line);
        var ink = Ui.OnBand(team.Ordinal);
        line.AddChild(HudBits.Icon("team", ink, 16));
        var name = new Label { Text = team.Name.ToUpperInvariant(), SizeFlagsHorizontal = SizeFlags.ExpandFill }; name.AddThemeColorOverride("font_color", ink); line.AddChild(name);
        if (team.Mine) { var you = new Label { Text = "★ you" }; you.AddThemeColorOverride("font_color", ink); line.AddChild(you); }
        var chevron = new Button { Text = _collapsed ? "›" : "⌄", Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None, TooltipText = _collapsed ? "Show the units" : "Hide the units" };
        chevron.AddThemeColorOverride("font_color", ink); chevron.AddThemeColorOverride("font_hover_color", ink);
        chevron.Pressed += Collapse;
        line.AddChild(chevron);
        return band;
    }

    void Replace(int team, int index, RosterRow row)
    {
        var (oldLine, oldChips) = _rows[team][index];
        var at = oldLine.GetIndex();
        _list.RemoveChild(oldLine); oldLine.QueueFree();
        if (oldChips is not null) { _list.RemoveChild(oldChips); oldChips.QueueFree(); }
        var (line, chips) = Row(row);
        _list.AddChild(line); _list.MoveChild(line, at);
        if (chips is not null) { _list.AddChild(chips); _list.MoveChild(chips, at + 1); }
        _rows[team][index] = (line, chips);
    }

    static string PipsOf(RosterRow row) => row.Max <= 12 ? RosterView.Pips(row.Left, row.Max) : $"{row.Left}/{row.Max}";

    static readonly bool Touch = DisplayServer.IsTouchscreenAvailable();

    (Control Line, Control? Chips) Row(RosterRow row)
    {
        var frame = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, TooltipText = row.Name };
        var edge = new StyleBoxFlat { BgColor = row.UnitId == _selected ? Ui.Accent with { A = 0.12f } : Ui.Accent with { A = 0 }, BorderColor = Ui.Accent };
        edge.BorderWidthLeft = row.UnitId == _selected ? 3 : 0; edge.ContentMarginLeft = 6; edge.ContentMarginRight = 2; edge.ContentMarginTop = edge.ContentMarginBottom = 1;
        frame.AddThemeStyleboxOverride("panel", edge);
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; line.AddThemeConstantOverride("separation", 8); frame.AddChild(line);
        var v = row.OffTable ? Variation.Dim : "";   // off the table: dimmed
        var acting = new Label { Text = row.Acting ? "▶" : "", CustomMinimumSize = new Vector2(14, 0), MouseFilter = MouseFilterEnum.Ignore };
        acting.AddThemeColorOverride("font_color", Ui.Accent); line.AddChild(acting);
        line.AddChild(new Label { Text = row.Name, ThemeTypeVariation = v, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        line.AddChild(Mark(row.Mark));
        line.AddChild(new Label { Text = PipsOf(row), ThemeTypeVariation = v, MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(48, 0), HorizontalAlignment = HorizontalAlignment.Right });
        var conditions = new HBoxContainer { CustomMinimumSize = new Vector2(20, 0), MouseFilter = MouseFilterEnum.Ignore }; line.AddChild(conditions);
        foreach (var c in row.Conditions.Where(c => Ui.Icon(c) is not null)) conditions.AddChild(Icon(c));   // an icon with a tooltip; a condition without one goes on the chip line
        var id = row.UnitId;
        var menu = new Button { Text = "▸", Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None, TooltipText = "Unit menu",
            Modulate = Touch ? HudBits.Plain : HudBits.Plain with { A = 0 }, Visible = !_s.IsWatching };
        menu.Pressed += () => UnitMenuRequested?.Invoke(id, menu.GlobalPosition + new Vector2(menu.Size.X, 0));
        line.AddChild(menu);
        if (!Touch)
        {
            frame.MouseEntered += () => menu.Modulate = HudBits.Plain;
            frame.MouseExited += () => { if (!frame.GetGlobalRect().HasPoint(frame.GetGlobalMousePosition())) menu.Modulate = HudBits.Plain with { A = 0 }; };
        }
        void Clicked(InputEvent e)
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) UnitPicked?.Invoke(id);
            else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) UnitMenuRequested?.Invoke(id, b.GlobalPosition);
        }
        frame.GuiInput += Clicked;
        // Where an off-table unit is, and the conditions the theme draws no icon for, on their own line under the name so a long chip
        // never squeezes the name: inside a Transport (passengers spec), or held off the table until its property places it.
        var words = row.Conditions.Where(c => Ui.Icon(c) is null).ToList();
        if (row.Inside is null && row.Reserve is null && words.Count == 0) return (frame, null);
        var chips = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var c in words) chips.AddChild(Chip(RosterView.Words(c)));
        if (row.Inside is { } transport) chips.AddChild(Chip($"in {transport}"));
        if (row.Reserve is { } reserve) chips.AddChild(Chip(reserve));
        var indent = new MarginContainer { MouseFilter = MouseFilterEnum.Stop }; indent.AddThemeConstantOverride("margin_left", 28);   // the chip line is the row's too
        indent.GuiInput += Clicked; indent.AddChild(chips);
        return (frame, indent);
    }

    /// <summary>○ may still act, ✓ (the activated icon) activated, — off the table.</summary>
    static Control Mark(RosterMark mark)
    {
        Control c = mark switch
        {
            RosterMark.Activated when Ui.Icon("activated") is not null => HudBits.Icon("activated", HudBits.Plain, 16, "Activated"),
            RosterMark.Activated => new Label { Text = "✓", ThemeTypeVariation = Variation.Success, TooltipText = "Activated" },
            RosterMark.MayAct => new Label { Text = "○", TooltipText = "Not yet activated" },
            _ => new Label { Text = "—", ThemeTypeVariation = Variation.Dim, TooltipText = "Off the table" },
        };
        c.MouseFilter = MouseFilterEnum.Pass;
        if (c is Label l) { l.HorizontalAlignment = HorizontalAlignment.Center; l.CustomMinimumSize = new Vector2(16, 0); }
        return c;
    }

    /// <summary>The theme's icon for a condition name or marker kind, else a text chip of the name.</summary>
    public static Control Icon(string key) => Ui.Icon(key) is { } texture
        ? new TextureRect { Texture = texture, MouseFilter = MouseFilterEnum.Pass, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(16, 16), TooltipText = RosterView.Words(key) }
        : Chip(RosterView.Words(key));

    /// <summary>Text in the theme's chip box. Icons and chips pass clicks to their row, keeping their tooltips.</summary>
    public static Control Chip(string text) { var chip = new PanelContainer { ThemeTypeVariation = Variation.Chip, MouseFilter = MouseFilterEnum.Pass }; chip.AddChild(new Label { Text = text }); return chip; }
}
