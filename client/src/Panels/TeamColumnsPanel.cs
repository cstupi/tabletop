using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>
/// The team columns (spec 2026-11-07-lobby-and-setup-redesign-design.md, TeamColumns): one column per team, then Watching, side by side and
/// wrapping. The lobby's are wide with room to spare; setup's head holds them compact, two to a row. A column's Join, a row's ▸ menu and a row
/// dragged onto another column each send one PickTeam; my row holds my army picker.
/// </summary>
public partial class TeamColumnsPanel : HFlowContainer
{
    readonly bool _compact;
    Session _s = null!;
    readonly Dictionary<Guid, Control> _editing = new();
    Action<Guid>? _onPresence; Action? _onTheme;
    IReadOnlyList<ArmyOption>? _options;
    readonly List<(ArmySeat Seat, OptionButton Box)> _pickers = new();

    public TeamColumnsPanel(bool compact)
    {
        _compact = compact;
        AddThemeConstantOverride("h_separation", compact ? 8 : 20);
        AddThemeConstantOverride("v_separation", compact ? 8 : 20);
    }

    /// <summary>A watcher of an inline report: a failed army list goes to the panel that owns the columns.</summary>
    public Action<string>? Report { get; init; }

    public override void _Ready()
    {
        _s = Main.Instance.Session;
        _onTheme = Build; Ui.Changed += _onTheme;
        if (_compact) { _onPresence = _ => ShowEditing(); _s.PresenceChanged += _onPresence; }
        Build();
        Guarded.Run(async () =>
        {
            var list = (await _s.Management.ListArmies(null)).Items;
            if (!IsInstanceValid(this) || _s.Game is not { } game) return;
            _options = LobbyView.ArmyOptions(game, list);
            FillPickers();
        }, text => Report?.Invoke(text));
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_onPresence is not null) _s.PresenceChanged -= _onPresence;
    }

    void Build()
    {
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        _editing.Clear(); _pickers.Clear();
        if (_s.Game is not { } game) return;
        foreach (var column in TeamColumns.Of(game, _s.ParticipantId, _s.IsCreator, _compact)) AddChild(Column(column));
        if (_s.IsCreator)
        {
            var add = new Button { Text = _compact ? TeamColumns.AddTeamCompact : TeamColumns.AddTeam,
                TooltipText = "Add a team", CustomMinimumSize = _compact ? new Vector2(0, 0) : new Vector2(220, 160),
                SizeFlagsVertical = _compact ? SizeFlags.ShrinkBegin : SizeFlags.Fill };
            add.Pressed += () => Guarded.Send(_s, LobbyEdits.CreateTeam());
            AddChild(add);
        }
        FillPickers();
        ShowEditing();
    }

    Control Column(TeamColumn column)
    {
        var box = new ColumnBox(this, column) { CustomMinimumSize = new Vector2(_compact ? 150 : 250, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeStyleboxOverride("panel", Ui.TeamColumn(column.Ordinal));
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 0); box.AddChild(stack);
        stack.AddChild(Band(column));
        var body = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) body.AddThemeConstantOverride(edge, _compact ? 6 : 16);
        stack.AddChild(body);
        var rows = new VBoxContainer(); rows.AddThemeConstantOverride("separation", _compact ? 4 : 14); body.AddChild(rows);
        if (column.Members.Count == 0)
            rows.AddChild(new Label { Text = column.TeamId is null ? "No one" : "No players yet", ThemeTypeVariation = Variation.Dim });
        foreach (var m in column.Members) rows.AddChild(Member(box, column, m));
        // Compact (setup): no buttons; a row's ▸ menu and drag make every move a Join would.
        if (column.Join is { } join && !_compact)
        {
            var b = new Button { Text = join.Text, Disabled = !join.Enabled, TooltipText = join.Why ?? "", SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(140, 0) };
            if (join.Edit is { } edit) b.Pressed += () => Guarded.Send(_s, edit);
            rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
            rows.AddChild(b);
            if (join.Why is { } why) rows.AddChild(Wrapping(why, Variation.Dim));
        }
        if (column.AlsoPlay is { } also && !_compact)
        {
            var b = new Button { Text = TeamColumns.AlsoPlay, Flat = true, ThemeTypeVariation = Variation.FlatButton, SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                TooltipText = "Keep your team and play this side too" };
            b.Pressed += () => Guarded.Send(_s, also);
            rows.AddChild(b);
        }
        return box;
    }

    /// <summary>The header band: the team icon, the title and the state on the team's colour.</summary>
    Control Band(TeamColumn column)
    {
        var band = new PanelContainer { TooltipText = column.StateText }; band.AddThemeStyleboxOverride("panel", Ui.TeamBand(column.Ordinal, _compact));
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 10); band.AddChild(line);
        var ink = Ui.OnBand(column.Ordinal);
        line.AddChild(Icon(column.TeamId is null ? "watch" : "team", ink, _compact ? 18 : 28));
        var words = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; words.AddThemeConstantOverride("separation", 0); line.AddChild(words);
        var title = new Label { Text = column.Title.ToUpperInvariant(), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        if (!_compact) title.ThemeTypeVariation = Variation.Display;
        title.AddThemeColorOverride("font_color", ink); words.AddChild(title);
        // Compact: the state is an icon after the title, its words the band's tooltip.
        if (_compact)
        {
            if (column.State != ColumnState.Watching) line.AddChild(Icon(column.State == ColumnState.Ready ? "ready" : "warning", ink, 16));
        }
        else
        {
            var state = new HBoxContainer(); state.AddThemeConstantOverride("separation", 6); words.AddChild(state);
            if (column.State is ColumnState.Ready or ColumnState.NeedsPlayer or ColumnState.NeedsArmy)
                state.AddChild(Icon(column.State == ColumnState.Ready ? "ready" : "warning", ink, 16));
            var text = new Label { Text = column.StateText, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddThemeColorOverride("font_color", ink); state.AddChild(text);
        }
        return band;
    }

    /// <summary>A player row: the name with its chips and ▸ menu, then the army line or my picker. Dragged onto another column, it moves.</summary>
    Control Member(ColumnBox box, TeamColumn column, ColumnMember m)
    {
        var row = new MemberRowBox(box, column, m) { TooltipText = m.Moves.Count > 0 ? "Drag to another column to move" : "" };
        row.AddThemeStyleboxOverride("panel", Ui.MemberRow(m.You));
        var lines = new VBoxContainer(); lines.AddThemeConstantOverride("separation", _compact ? 0 : 4); row.AddChild(lines);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 8); lines.AddChild(top);
        top.AddChild(Icon("player", column.Ordinal > 0 ? Ui.Team(column.Ordinal) : Ui.DimText, _compact ? 16 : 22));
        top.AddChild(new Label { Text = m.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = m.Name, MouseFilter = MouseFilterEnum.Pass });
        var editing = Icon("editing", Ui.Accent, 16); editing.Visible = false; editing.TooltipText = "Editing the table"; editing.MouseFilter = MouseFilterEnum.Pass;
        top.AddChild(editing); _editing[m.ParticipantId] = editing;
        if (m.You) top.AddChild(new Label { Text = TeamColumns.You, ThemeTypeVariation = Variation.Accent });
        if (m.Moves.Count > 0)
        {
            var menu = new MenuButton { Text = "▸", Flat = true, ThemeTypeVariation = Variation.FlatButton, TooltipText = "Move", FocusMode = FocusModeEnum.None };
            for (var i = 0; i < m.Moves.Count; i++) menu.GetPopup().AddItem(m.Moves[i].Text, i);
            var moves = m.Moves;
            menu.GetPopup().IdPressed += id => Guarded.Send(_s, moves[(int)id].Edit);
            top.AddChild(menu);
        }
        // Watching rows name an army only when they have one, or on my row with its picker.
        if (column.TeamId is null && m.Army is null && m.MyArmy is null) return row;
        var army = new HBoxContainer(); army.AddThemeConstantOverride("separation", 8); lines.AddChild(army);
        army.AddChild(new Control { CustomMinimumSize = new Vector2(_compact ? 4 : 14, 0) });
        var warn = m.Army is null && m.NoArmyWarns;
        army.AddChild(Icon(m.Army is null ? (warn ? "warning" : "army") : "army", warn ? Ui.Warning : m.Army is null ? Ui.DimText : Ui.Text, _compact ? 14 : 18));
        // My picker: always in the lobby; in setup only while my seat has no army.
        if (m.MyArmy is { } seat && (!_compact || seat.ArmyId is null))
        {
            var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, FitToLongestItem = false, TooltipText = seat.Label };
            picker.AddItem(TeamColumns.NoArmy);
            army.AddChild(picker); _pickers.Add((seat, picker));
        }
        else
            army.AddChild(new Label { Text = m.Army ?? TeamColumns.NoArmy, ThemeTypeVariation = warn ? Variation.Warning : m.Army is null ? Variation.Dim : "",
                SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = m.Army ?? "",
                MouseFilter = MouseFilterEnum.Pass });
        return row;
    }

    /// <summary>The army list, once it arrives, into every picker on my rows, the picked one selected.</summary>
    void FillPickers()
    {
        if (_options is not { } options) return;
        foreach (var (seat, box) in _pickers)
        {
            if (!IsInstanceValid(box) || box.ItemCount > 1) continue;
            LobbyPanel.AddArmies(box, options);
            if (seat.ArmyId is { } picked) { var at = options.ToList().FindIndex(o => o.Army.Id == picked); if (at >= 0) box.Select(at + 1); }
            box.ItemSelected += i => Guarded.Send(_s, seat.Pick(i == 0 ? null : options[(int)i - 1].Army.Id));
        }
    }

    void ShowEditing()
    {
        if (!_compact) return;
        var editing = TeamColumns.Editing(_s.Others.All);
        foreach (var (id, icon) in _editing) if (IsInstanceValid(icon)) icon.Visible = editing.Contains(id);
    }

    /// <summary>The move a row dropped on a column makes, or null.</summary>
    internal PickTeam? DropOn(TeamColumn column, Variant data) =>
        _s.Game is { } game && data.VariantType == Variant.Type.String && Guid.TryParse(data.AsString(), out var row)
            ? TeamColumns.MoveTo(game, _s.ParticipantId, _s.IsCreator, row, column.TeamId) : null;

    internal void Drop(TeamColumn column, Variant data) { if (DropOn(column, data) is { } edit) Guarded.Send(_s, edit); }

    static TextureRect Icon(string name, Color tint, int size) => new()
    {
        Texture = Ui.Icon(name), Modulate = tint, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore,
    };

    static Label Wrapping(string text, string variation) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = variation };
}

/// <summary>A team column: takes a dragged row, lit while one hovers that may land here.</summary>
public partial class ColumnBox : PanelContainer
{
    readonly TeamColumnsPanel _panel = null!; readonly TeamColumn _column = null!;
    bool _lit;
    public ColumnBox() { }
    internal ColumnBox(TeamColumnsPanel panel, TeamColumn column) { _panel = panel; _column = column; }
    public override bool _CanDropData(Vector2 at, Variant data) { var ok = _panel.DropOn(_column, data) is not null; Light(ok); return ok; }
    public override void _DropData(Vector2 at, Variant data) { Light(false); _panel.Drop(_column, data); }
    public override void _Notification(int what) { if (what is (int)NotificationDragEnd or (int)NotificationMouseExit) Light(false); }
    internal void Light(bool on) { if (on == _lit || _column is null) return; _lit = on; AddThemeStyleboxOverride("panel", Ui.TeamColumn(_column.Ordinal, on)); }
}

/// <summary>A player row: dragged when the viewer may move it; a drop over it lands on its column.</summary>
public partial class MemberRowBox : PanelContainer
{
    readonly ColumnBox _box = null!; readonly TeamColumn _column = null!; readonly ColumnMember _member = null!;
    public MemberRowBox() { }
    internal MemberRowBox(ColumnBox box, TeamColumn column, ColumnMember member) { _box = box; _column = column; _member = member; }
    public override Variant _GetDragData(Vector2 at)
    {
        if (_member is null || _member.Moves.Count == 0) return default;
        var preview = new PanelContainer(); preview.AddThemeStyleboxOverride("panel", Ui.TeamColumn(_column.Ordinal));
        preview.AddChild(new Label { Text = _member.Name }); SetDragPreview(preview);
        return _member.ParticipantId.ToString();
    }
    public override bool _CanDropData(Vector2 at, Variant data) => _box is not null && _box._CanDropData(at, data);
    public override void _DropData(Vector2 at, Variant data) => _box?._DropData(at, data);
}
