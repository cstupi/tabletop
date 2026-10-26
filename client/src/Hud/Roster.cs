using Godot; using Tabletop.Client.Core; using Tabletop.Client.Table; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Hud;

/// <summary>Left column, above the unit card: team headers and unit rows; a click selects and centres the unit, a right-click asks for the unit's menu, R collapses to the headers.</summary>
public partial class Roster : PanelContainer
{
    Session _s = null!; readonly VBoxContainer _list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; bool _collapsed;
    Action<RenderCause>? _onFrame; Action? _onChanged;
    HudRect? _slot; bool _fitQueued; ScrollContainer _scroll = null!;
    public event Action<Guid>? UnitPicked;
    public event Action<Guid, Vector2>? UnitMenuRequested;

    public void Bind(Session session)
    {
        _s = session; _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve }; AddChild(_scroll); _scroll.AddChild(_list);
        _list.MinimumSizeChanged += QueueFit;                                                                           // a ScrollContainer reports no content height
        _onFrame = _ => Render(); _onChanged = () => { Clear(); Render(); };
        session.Frame.Render += _onFrame;                                                                              // every cause the frame carries
        Ui.Changed += _onChanged;                                                                                      // the theme's icons
        Render();
    }
    public override void _ExitTree() { Ui.Changed -= _onChanged; if (_s is null) return; _s.Frame.Render -= _onFrame; }
    /// <summary>Into its HudLayout slot: the slot's width, the list's height up to the slot's, against the slot's top.</summary>
    public void Place(HudRect slot) { _slot = slot; Fit(); HudPlacement.Top(this, slot); }

    void QueueFit() { if (_fitQueued) return; _fitQueued = true; Callable.From(() => { _fitQueued = false; Fit(); }).CallDeferred(); }

    void Fit()
    {
        if (_slot is not { } slot || _scroll is null || !IsInstanceValid(_scroll)) return;
        var chrome = GetCombinedMinimumSize().Y - _scroll.GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, MathF.Floor(Math.Max(0, Math.Min(_list.GetCombinedMinimumSize().Y, slot.H - chrome))));   // whole pixels, as DecisionBox.Fit
    }

    public override void _Input(InputEvent e) { if (KeyOverlay.IntentOf(e, GetViewport()) == Intent.ToggleRoster) { _collapsed = !_collapsed; Render(); GetViewport().SetInputAsHandled(); } }

    IReadOnlyList<RosterTeam>? _drawn; bool _drawnCollapsed;
    /// <summary>Per drawn row, its line and its chip line, by (team, row) index of <see cref="_drawn"/>.</summary>
    readonly List<List<(Control Line, Control? Chips)>> _rows = new();

    /// <summary>Only the rows whose content changed are rebuilt (RosterView.Changed); a new team line, unit order or the collapse rebuilds the list.</summary>
    void Render()
    {
        Visible = _s.Projection is not null && _s.Game is not null;
        if (_s.Projection is not { } p || _s.Game is not { } game) { Clear(); return; }
        var teams = RosterView.Of(p, game, new Names(game, p), _s.MyTeamId, _s.MaxWounds);
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
            var h = new Label { Text = team.Name }; h.AddThemeColorOverride("font_color", Palette.Team(team.Ordinal)); _list.AddChild(h);
            var lines = new List<(Control, Control?)>(); _rows.Add(lines);
            if (_collapsed) continue;
            foreach (var row in team.Rows)
            {
                var (line, chips) = Row(row);
                _list.AddChild(line); if (chips is not null) _list.AddChild(chips);
                lines.Add((line, chips));
            }
        }
    }

    void Clear() { foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); } _rows.Clear(); _drawn = null; }

    void Replace(int team, int index, RosterRow row)
    {
        var (oldLine, oldChips) = _rows[team][index];
        if (_drawn![team].Rows[index] is var was && was.Conditions.SequenceEqual(row.Conditions) && was.Inside == row.Inside && was.Reserve == row.Reserve
            && oldLine.GetChildCount() >= 3 && oldLine.GetChild(0) is Label name && oldLine.GetChild(2) is Label pips)
        {
            // The same icons and chips: the line keeps its controls and takes the new words (a round's activations, wounds).
            var v = row.OffTable ? Variation.Dim : "";
            name.Text = row.Name; name.ThemeTypeVariation = v;
            pips.Text = PipsOf(row); pips.ThemeTypeVariation = v;
            if (was.Activated != row.Activated) { var old = oldLine.GetChild(1); oldLine.RemoveChild(old); old.QueueFree(); var mark = ActivatedMark(row); oldLine.AddChild(mark); oldLine.MoveChild(mark, 1); }
            return;
        }
        var at = oldLine.GetIndex();
        _list.RemoveChild(oldLine); oldLine.QueueFree();
        if (oldChips is not null) { _list.RemoveChild(oldChips); oldChips.QueueFree(); }
        var (line, chips) = Row(row);
        _list.AddChild(line); _list.MoveChild(line, at);
        if (chips is not null) { _list.AddChild(chips); _list.MoveChild(chips, at + 1); }
        _rows[team][index] = (line, chips);
    }

    static Control ActivatedMark(RosterRow row) => row.Activated ? Icon("activated") : new Control { CustomMinimumSize = new Vector2(16, 16), MouseFilter = MouseFilterEnum.Ignore };   // an empty spacer keeps the columns lined up
    static string PipsOf(RosterRow row) => row.Max <= 12 ? RosterView.Pips(row.Left, row.Max) : $"{row.Left}/{row.Max}";

    (Control Line, Control? Chips) Row(RosterRow row)
    {
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop }; var v = row.OffTable ? Variation.Dim : "";   // off the table: dimmed
        line.AddChild(new Label { Text = row.Name, ThemeTypeVariation = v, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        line.AddChild(ActivatedMark(row));
        line.AddChild(new Label { Text = PipsOf(row), ThemeTypeVariation = v });
        foreach (var c in row.Conditions.Where(c => Ui.Icon(c) is not null)) line.AddChild(Icon(c));   // an icon with a tooltip; a condition without one goes on the chip line
        var id = row.UnitId;
        void Clicked(InputEvent e)
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) UnitPicked?.Invoke(id);
            else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) UnitMenuRequested?.Invoke(id, b.GlobalPosition);
        }
        line.GuiInput += Clicked;
        // Where an off-table unit is, and the conditions the theme draws no icon for, on their own line under the name so a long chip
        // never squeezes the name: inside a Transport (passengers spec), or held off the table until its property places it.
        var words = row.Conditions.Where(c => Ui.Icon(c) is null).ToList();
        if (row.Inside is null && row.Reserve is null && words.Count == 0) return (line, null);
        var chips = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var c in words) chips.AddChild(Chip(RosterView.Words(c)));
        if (row.Inside is { } transport) chips.AddChild(Chip($"in {transport}"));
        if (row.Reserve is { } reserve) chips.AddChild(Chip(reserve));
        var indent = new MarginContainer { MouseFilter = MouseFilterEnum.Stop }; indent.AddThemeConstantOverride("margin_left", 16);   // the chip line is the row's too
        indent.GuiInput += Clicked; indent.AddChild(chips);
        return (line, indent);
    }

    /// <summary>The theme's icon for a condition name or marker kind, else a text chip of the name.</summary>
    public static Control Icon(string key) => Ui.Icon(key) is { } texture
        ? new TextureRect { Texture = texture, MouseFilter = MouseFilterEnum.Pass, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(16, 16), TooltipText = RosterView.Words(key) }
        : Chip(RosterView.Words(key));

    /// <summary>Text in the theme's chip box. Icons and chips pass clicks to their row, keeping their tooltips.</summary>
    public static Control Chip(string text) { var chip = new PanelContainer { ThemeTypeVariation = Variation.Chip, MouseFilter = MouseFilterEnum.Pass }; chip.AddChild(new Label { Text = text }); return chip; }
}
