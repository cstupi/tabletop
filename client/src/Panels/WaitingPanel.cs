using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>
/// Waiting for players (spec 2026-11-07-ux-redesign-1-client-shell.md → Waiting for players), in the decision slot: the quiet band "Deployment
/// waits for every team" over the compact team columns (WaitingView.Columns), each band reading Ready, Needs a player or Needs an army, with Join
/// on a team no one holds or my army picker on a team of mine with none, inside the column. The screen calls Refresh on every game or board
/// change; the columns are rebuilt only when what they show changes, so an open dropdown survives unrelated updates.
/// </summary>
public partial class WaitingPanel : PanelContainer
{
    IReadOnlyList<WaitingColumn>? _shown;
    Action? _onTheme;

    public override void _Ready()
    {
        _onTheme = () => { _shown = null; Refresh(); };
        Ui.Changed += _onTheme;
        Refresh();
    }

    public override void _ExitTree() => Ui.Changed -= _onTheme;

    public void Refresh()
    {
        var s = Main.Instance.Session;
        if (s.Game is not { } game) return;
        var columns = WaitingView.Columns(game, s.WaitingTeams, s.ParticipantId);
        if (!WaitingView.Changed(_shown, columns)) return;
        _shown = columns;
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        Build(s, game, columns);
    }

    void Build(Session s, Tabletop.Protocol.GameInfo game, IReadOnlyList<WaitingColumn> columns)
    {
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 10); AddChild(stack);
        var band = new HBoxContainer(); band.AddThemeConstantOverride("separation", 8); stack.AddChild(band);
        band.AddChild(Icon("watch", Ui.DimText, 18));
        band.AddChild(new Label { Text = WaitingView.Band, ThemeTypeVariation = Variation.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var row = new HFlowContainer(); row.AddThemeConstantOverride("h_separation", 8); row.AddThemeConstantOverride("v_separation", 8); stack.AddChild(row);
        var pickers = new List<(ArmySeat Seat, OptionButton Box)>();
        foreach (var column in columns) row.AddChild(Column(s, column, pickers));
        var error = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        stack.AddChild(error);
        if (pickers.Count == 0) return;
        Guarded.Run(async () =>
        {
            var options = LobbyView.ArmyOptions(game, (await s.Management.ListArmies(null)).Items);
            foreach (var (seat, box) in pickers)
            {
                if (!IsInstanceValid(box) || !box.IsInsideTree()) continue;   // the columns were rebuilt while the list loaded
                if (options.Count == 0) { box.SetItemText(0, "No armies: import one in the web app"); continue; }
                LobbyPanel.AddArmies(box, options);
                box.Disabled = false;
                box.ItemSelected += i => { if (i > 0) Guarded.Send(s, seat.Pick(options[(int)i - 1].Army.Id)); };
            }
        }, text => { if (IsInstanceValid(error)) { error.Text = text; error.Visible = true; } });
    }

    static Control Column(Session s, WaitingColumn column, List<(ArmySeat, OptionButton)> pickers)
    {
        var box = new PanelContainer { CustomMinimumSize = new Vector2(170, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeStyleboxOverride("panel", Ui.TeamColumn(column.Ordinal));
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 0); box.AddChild(stack);
        var band = new PanelContainer(); band.AddThemeStyleboxOverride("panel", Ui.TeamBand(column.Ordinal, compact: true)); stack.AddChild(band);
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 8); band.AddChild(line);
        var ink = Ui.OnBand(column.Ordinal);
        line.AddChild(Icon("team", ink, 18));
        var title = new Label { Text = column.Title.ToUpperInvariant(), SizeFlagsHorizontal = SizeFlags.ExpandFill }; title.AddThemeColorOverride("font_color", ink); line.AddChild(title);
        line.AddChild(Icon(column.State == ColumnState.Ready ? "ready" : "warning", ink, 16));
        var state = new Label { Text = column.StateText }; state.AddThemeColorOverride("font_color", ink); line.AddChild(state);
        var body = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) body.AddThemeConstantOverride(edge, 8);
        stack.AddChild(body);
        var rows = new VBoxContainer(); rows.AddThemeConstantOverride("separation", 4); body.AddChild(rows);
        if (column.Members.Count == 0) rows.AddChild(new Label { Text = "No players yet", ThemeTypeVariation = Variation.Dim });
        foreach (var (name, army) in column.Members)
        {
            var who = new HBoxContainer(); who.AddThemeConstantOverride("separation", 6); rows.AddChild(who);
            who.AddChild(Icon("player", Ui.Team(column.Ordinal), 16));
            who.AddChild(new Label { Text = army is null ? name : $"{name} · {army}", ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = army ?? name, MouseFilter = MouseFilterEnum.Pass });
        }
        if (column.Join is { } join)
        {
            var b = new Button { Text = "Join", TooltipText = join.Why, SizeFlagsHorizontal = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(120, 0) };
            b.Pressed += () => Guarded.Send(s, join.Edit);
            rows.AddChild(b);
        }
        if (column.Army is { } seat)
        {
            var army = new HBoxContainer(); army.AddThemeConstantOverride("separation", 6); rows.AddChild(army);
            army.AddChild(Icon("army", Ui.Warning, 16));
            var picker = new OptionButton { Disabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, FitToLongestItem = false, TooltipText = seat.Label };
            picker.AddItem(TeamColumns.NoArmy); army.AddChild(picker);
            pickers.Add((seat, picker));
        }
        return box;
    }

    static TextureRect Icon(string name, Color tint, int size) => new()
    {
        Texture = Ui.Icon(name), Modulate = tint, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore,
    };
}
