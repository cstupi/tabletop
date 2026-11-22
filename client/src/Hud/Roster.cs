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
        _list.AddThemeConstantOverride("separation", Space.XS);
        _list.MinimumSizeChanged += QueueFit;                                                                           // a ScrollContainer reports no content height
        if (!Touch) _list.SortChildren += SyncMenus;                                                                     // a row moved from under a still pointer hears no exit
        _onFrame = _ => Render(); _onChanged = () => { Clear(); Render(); };
        session.Frame.Render += _onFrame;                                                                              // every cause the frame carries
        Ui.Changed += _onChanged;                                                                                      // the theme's icons
        if (input is not null) { _onSelection = () => { if (input.Selected?.PieceId == _selected) return; _selected = input.Selected?.PieceId; Render(); }; input.SelectionChanged += _onSelection; }
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

    IReadOnlyList<RosterTeam>? _drawn; bool _drawnCollapsed; Guid? _drawnSelected;
    /// <summary>Each drawn team's band and the spacer under its rows, and each drawn unit's line and chip line.</summary>
    readonly Dictionary<Guid, (Control Header, Control Spacer)> _teams = new();
    Dictionary<Guid, (Control Line, Control? Chips)> _lines = new();

    /// <summary>A render keeps every row whose content and selection stand (RosterView.Keep), sets the rest again on their standing controls (a new unit's
    /// row is made) and puts the list in order; a new team line or the collapse rebuilds it. A selection or an activation sets two rows.</summary>
    void Render()
    {
        Visible = _s.Projection is not null && _s.Game is not null;
        if (_s.Projection is not { } p || _s.Game is not { } game) { Clear(); return; }
        var teams = RosterView.Of(p, game, new Names(game, p), _s.MyTeamId, _s.MaxWounds, _s.MyTeamIds, _s.SetAsideRules);
        var keep = _drawnCollapsed == _collapsed && !_collapsed ? RosterView.Keep(_drawn, _drawnSelected, teams, _selected) : null;
        if (keep is null) { Rebuild(teams); return; }
        var next = new Dictionary<Guid, (Control Line, Control? Chips)>(); var order = new List<Control>();
        for (var t = 0; t < teams.Count; t++)
        {
            var (header, spacer) = _teams[teams[t].TeamId]; order.Add(header);
            for (var r = 0; r < teams[t].Rows.Count; r++)
            {
                var (unit, kept) = keep[t][r];
                if (!_lines.TryGetValue(unit, out var drawn)) drawn = Row(teams[t].Rows[r]);
                else if (!kept) Fill((RowControls)drawn.Line, teams[t].Rows[r]);   // a changed row is set again in place
                next[unit] = drawn; order.Add(drawn.Line); if (drawn.Chips is not null) order.Add(drawn.Chips);
            }
            order.Add(spacer);
        }
        foreach (var (unit, (line, chips)) in _lines)
            if (!next.TryGetValue(unit, out var now) || now.Line != line) { Free(line); if (chips is not null) Free(chips); }
        for (var i = 0; i < order.Count; i++) { if (order[i].GetParent() is null) _list.AddChild(order[i]); _list.MoveChild(order[i], i); }
        (_lines, _drawn, _drawnSelected) = (next, teams, _selected);
    }

    void Rebuild(IReadOnlyList<RosterTeam> teams)
    {
        Clear();
        (_drawn, _drawnCollapsed, _drawnSelected) = (teams, _collapsed, _selected);
        foreach (var team in teams)
        {
            var header = Header(team); _list.AddChild(header);
            var spacer = new Control { CustomMinimumSize = new Vector2(0, Space.XS), MouseFilter = MouseFilterEnum.Ignore };
            _teams[team.TeamId] = (header, spacer);
            if (_collapsed) continue;
            foreach (var row in team.Rows)
            {
                var (line, chips) = Row(row);
                _list.AddChild(line); if (chips is not null) _list.AddChild(chips);
                _lines[row.UnitId] = (line, chips);
            }
            _list.AddChild(spacer);
        }
    }

    void Free(Control c) { if (c.GetParent() == _list) _list.RemoveChild(c); if (!c.IsQueuedForDeletion()) c.QueueFree(); }

    void Clear()
    {
        foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        foreach (var (_, spacer) in _teams.Values) if (IsInstanceValid(spacer) && !spacer.IsQueuedForDeletion()) spacer.QueueFree();   // a collapsed list never added them
        _teams.Clear(); _lines.Clear(); _drawn = null;
    }

    /// <summary>The team's band: its icon, its name, `★ you` on mine, the chevron that collapses the roster (R).</summary>
    Control Header(RosterTeam team)
    {
        var band = new PanelContainer(); band.AddThemeStyleboxOverride("panel", Ui.TeamBand(team.Ordinal, compact: true));
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", Space.S); band.AddChild(line);
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

    /// <summary>After the list lays out: each row's ▸ shows only while the pointer is over the row. Rows move when a render re-sorts them, and Godot
    /// sends no MouseExited to a control that moves out from under a pointer that stays still.</summary>
    void SyncMenus()
    {
        var at = _list.GetGlobalMousePosition();
        foreach (var child in _list.GetChildren())
            if (child is RowControls row)
            {
                var shown = row.Visible && row.GetGlobalRect().HasPoint(at) ? HudBits.Plain : HudBits.Plain with { A = 0 };
                if (row.Menu.Modulate != shown) row.Menu.Modulate = shown;
            }
    }

    /// <summary>A dot per model up to ten; more read as a count, so a long row of dots never cuts the unit's name.</summary>
    static string PipsOf(RosterRow row) => row.Max <= 10 ? RosterView.Pips(row.Left, row.Max) : $"{row.Left}/{row.Max}";

    static readonly bool Touch = DisplayServer.IsTouchscreenAvailable();

    /// <summary>A unit's row, made once per unit and set by Fill whenever what it shows changes: the frame lit at its edge when selected, ▶ on the
    /// acting unit, the name, the mark, the wound pips, the condition icons, the ▸ menu; under it the chip line (where an off-table unit is, and the
    /// conditions the theme draws no icon for), hidden when empty.</summary>
    sealed partial class RowControls : PanelContainer
    {
        public readonly StyleBoxFlat Edge = new() { ContentMarginLeft = Space.S, ContentMarginRight = Space.S, ContentMarginTop = Space.S, ContentMarginBottom = Space.S };   // S inside the lit row's box (spacing rule 5)
        public readonly Label Acting = new() { Text = "▶", CustomMinimumSize = new Vector2(14, 0), MouseFilter = MouseFilterEnum.Ignore },
            Title = new() { ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore },
            MarkText = new() { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(16, 0), MouseFilter = MouseFilterEnum.Pass },
            Pips = new() { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(48, 0), HorizontalAlignment = HorizontalAlignment.Right };
        public readonly TextureRect MarkIcon = HudBits.Icon("activated", HudBits.Plain, 16, "Activated");
        public readonly HBoxContainer Conditions = new() { CustomMinimumSize = new Vector2(20, 0), MouseFilter = MouseFilterEnum.Ignore };
        public readonly Button Menu = new() { Text = "▸", Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None, TooltipText = "Unit menu" };
        public readonly MarginContainer ChipLine = new() { MouseFilter = MouseFilterEnum.Stop };
        public readonly HFlowContainer Chips = new() { MouseFilter = MouseFilterEnum.Ignore };
        public readonly Kept ConditionKids, ChipKids;
        public Guid UnitId;
        public Roster? Owner;

        public RowControls()
        {
            ConditionKids = new(Conditions); ChipKids = new(Chips);
            MouseFilter = MouseFilterEnum.Stop;
            AddThemeStyleboxOverride("panel", Edge);
            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; line.AddThemeConstantOverride("separation", Space.S); AddChild(line);
            MarkIcon.MouseFilter = MouseFilterEnum.Pass;
            foreach (var c in new Control[] { Acting, Title, MarkIcon, MarkText, Pips, Conditions, Menu }) line.AddChild(c);
            Menu.Modulate = Touch ? HudBits.Plain : HudBits.Plain with { A = 0 };
            Menu.Pressed += () => Owner?.UnitMenuRequested?.Invoke(UnitId, Menu.GlobalPosition + new Vector2(Menu.Size.X, 0));
            if (!Touch)
            {
                MouseEntered += () => Menu.Modulate = HudBits.Plain;
                MouseExited += () => { if (!GetGlobalRect().HasPoint(GetGlobalMousePosition())) Menu.Modulate = HudBits.Plain with { A = 0 }; };
            }
            GuiInput += Clicked;
            ChipLine.AddThemeConstantOverride("margin_left", Space.S + (int)Acting.CustomMinimumSize.X + Space.S);   // the chip line starts on the title's edge
            ChipLine.GuiInput += Clicked; ChipLine.AddChild(Chips);
        }

        void Clicked(InputEvent e)
        {
            if (Owner is not { } roster) return;
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) roster.UnitPicked?.Invoke(UnitId);
            else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) roster.UnitMenuRequested?.Invoke(UnitId, b.GlobalPosition);
        }

    }

    (Control Line, Control? Chips) Row(RosterRow row)
    {
        var c = new RowControls { Owner = this };
        Fill(c, row);
        return (c, c.ChipLine);
    }

    void Fill(RowControls c, RosterRow row)
    {
        c.UnitId = row.UnitId;
        var selected = row.UnitId == _selected;
        var bg = Ui.Accent with { A = selected ? 0.12f : 0 };
        if (c.Edge.BgColor != bg) c.Edge.BgColor = bg;
        if (c.Edge.BorderColor != Ui.Accent) c.Edge.BorderColor = Ui.Accent;
        var edge = selected ? 3 : 0;
        if (c.Edge.BorderWidthLeft != edge) c.Edge.BorderWidthLeft = edge;
        Look.Tip(c, row.Name);
        var v = row.OffTable ? Variation.Dim : "";   // off the table: dimmed
        Look.Text(c.Acting, row.Acting ? "▶" : ""); Look.Ink(c.Acting, Ui.Accent);
        Look.Text(c.Title, row.Name, v);
        Mark(c, row.Mark);
        Look.Text(c.Pips, PipsOf(row), v);
        var conditions = c.ConditionKids.Begin();
        foreach (var k in row.Conditions.Where(k => Ui.Icon(k) is not null))   // an icon with a tooltip; a condition without one goes on the chip line
        {
            var icon = conditions.Take("icon", () => new TextureRect { MouseFilter = MouseFilterEnum.Pass, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(16, 16) });
            var texture = Ui.Icon(k); if (!ReferenceEquals(icon.Texture, texture)) icon.Texture = texture;
            Look.Tip(icon, RosterView.Words(k));
        }
        conditions.End();
        c.Menu.Visible = !_s.IsWatching;
        // Where an off-table unit is, and the conditions the theme draws no icon for, on their own line under the name so a long chip
        // never squeezes the name: inside a Transport (passengers spec), or held off the table until its property places it.
        var chips = c.ChipKids.Begin();
        void Chip(string text) => Look.Text((Label)chips.Take("chip", () => Roster.Chip("")).GetChild(0), text);
        foreach (var k in row.Conditions.Where(k => Ui.Icon(k) is null)) Chip(RosterView.Words(k));
        if (row.Inside is { } transport) Chip($"in {transport}");
        if (row.Reserve is { } reserve) Chip(reserve);
        if (row.SetAside is { } aside) HudBits.FillAside(chips.Take("aside", HudBits.AsideChip), aside);   // the rule that lets it be held off the table (deployment only)
        c.ChipLine.Visible = chips.End() > 0;
    }

    /// <summary>○ may still act, ✓ (the activated icon) activated, — off the table.</summary>
    static void Mark(RowControls c, RosterMark mark)
    {
        var icon = mark == RosterMark.Activated && Ui.Icon("activated") is not null;
        c.MarkIcon.Visible = icon; c.MarkText.Visible = !icon;
        if (icon) { Look.Icon(c.MarkIcon, "activated", HudBits.Plain); return; }
        var (text, variation, tip) = mark switch
        {
            RosterMark.Activated => ("✓", Variation.Success, "Activated"),
            RosterMark.MayAct => ("○", "", "Not yet activated"),
            _ => ("—", Variation.Dim, "Off the table"),
        };
        Look.Text(c.MarkText, text, variation); Look.Tip(c.MarkText, tip);
    }

    /// <summary>The theme's icon for a condition name or marker kind, else a text chip of the name.</summary>
    public static Control Icon(string key) => Ui.Icon(key) is { } texture
        ? new TextureRect { Texture = texture, MouseFilter = MouseFilterEnum.Pass, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(16, 16), TooltipText = RosterView.Words(key) }
        : Chip(RosterView.Words(key));

    /// <summary>Text in the theme's chip box. Icons and chips pass clicks to their row, keeping their tooltips.</summary>
    public static Control Chip(string text) { var chip = new PanelContainer { ThemeTypeVariation = Variation.Chip, MouseFilter = MouseFilterEnum.Pass }; chip.AddChild(new Label { Text = text }); return chip; }
}
