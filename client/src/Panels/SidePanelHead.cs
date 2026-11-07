using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>The fixed head of lobby and setup (SetupHead, spec 2026-11-07-lobby-and-setup-redesign-design.md): the phase beside the creator's
/// Start setup or End setup, Still to do, and in setup the teams (spec 2026-11-07-ux-redesign-1-client-shell.md → Setup): one line a team, its band
/// and state then its players and armies, opened by ▸ into the compact columns with their ▸ moves, drag and army picker. It never scrolls.</summary>
public partial class SidePanelHead : VBoxContainer
{
    Session _session = null!; Action? _onBoard;
    Button? _action;
    /// <summary>The game screen's: whether the team lines are open, kept across rebuilds.</summary>
    public SetupFolds? Folds { get; init; }

    public override void _ExitTree() { if (_session is not null && _onBoard is not null) _session.BoardChanged -= _onBoard; }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session;
        if (s.Game is not { } game || SetupHead.Of(game, s.IsCreator, s.Draft) is not { } head) return;
        var lobby = game.Status == GameStatus.Lobby;
        AddThemeConstantOverride("separation", lobby ? 12 : 6);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", lobby ? 24 : 12); AddChild(top);
        top.AddChild(new Label { Text = lobby ? head.Phase : head.Phase.ToUpperInvariant(), ThemeTypeVariation = Variation.Display, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = lobby ? SizeFlags.Fill : SizeFlags.ExpandFill });
        if (head.Edit is { } edit)
        {
            _action = new Button { Text = head.Action, SizeFlagsHorizontal = SizeFlags.ShrinkEnd, CustomMinimumSize = new Vector2(lobby ? 220 : 0, 0), 
                TooltipText = head.Action };
            if (!lobby) _action.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            _action.Pressed += () => Guarded.Send(s, edit);
            top.AddChild(_action);
            // End setup counts the pieces off the table, so it follows the draft.
            _onBoard = () => { if (IsInstanceValid(_action) && s.Game is { } g && SetupHead.Of(g, s.IsCreator, s.Draft) is { Action: { } text }) { _action.Text = text; _action.TooltipText = text; } };
            s.BoardChanged += _onBoard;
        }
        var todo = new HFlowContainer();
        todo.AddThemeConstantOverride("h_separation", lobby ? 28 : 12); todo.AddThemeConstantOverride("v_separation", 0);
        AddChild(todo);
        foreach (var line in head.ToDo)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4); todo.AddChild(row);
            if (line.Missing) row.AddChild(Icon("warning", Ui.Warning, lobby ? 16 : 14));
            var text = new Label { Text = line.Text, ThemeTypeVariation = line.Missing ? Variation.Warning : Variation.Accent };
            if (!lobby) text.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(text);
        }
        if (lobby) return;
        if (Folds is { TeamsOpen: true }) { AddChild(Toggle(true)); AddChild(new TeamColumnsPanel(compact: true)); return; }
        Lines(game);
    }

    /// <summary>One line a team: its band (icon, title, state icon) and its players with their armies; Watching last while someone watches; ▸ opens
    /// the columns.</summary>
    void Lines(GameInfo game)
    {
        var columns = TeamColumns.Of(game, _session.ParticipantId, _session.IsCreator, compact: true);
        var lines = new VBoxContainer(); lines.AddThemeConstantOverride("separation", 3);
        var wrap = new HBoxContainer(); wrap.AddThemeConstantOverride("separation", 4); AddChild(wrap);
        lines.SizeFlagsHorizontal = SizeFlags.ExpandFill; wrap.AddChild(lines);
        foreach (var column in columns)
        {
            var row = new HBoxContainer { TooltipText = $"{column.Title}: {column.StateText}" }; row.AddThemeConstantOverride("separation", 6); lines.AddChild(row);
            var band = new PanelContainer { TooltipText = column.StateText, CustomMinimumSize = new Vector2(84, 0) };
            band.AddThemeStyleboxOverride("panel", Ui.TeamBand(column.Ordinal, compact: true)); row.AddChild(band);
            var chip = new HBoxContainer(); chip.AddThemeConstantOverride("separation", 4); band.AddChild(chip);
            var ink = Ui.OnBand(column.Ordinal);
            chip.AddChild(Icon(column.TeamId is null ? "watch" : "team", ink, 14));
            var title = new Label { Text = column.TeamId is null ? "WATCH" : $"TEAM {column.Ordinal}", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            title.AddThemeColorOverride("font_color", ink); title.AddThemeFontSizeOverride("font_size", 13); chip.AddChild(title);
            if (column.State != ColumnState.Watching) chip.AddChild(Icon(column.State == ColumnState.Ready ? "ready" : "warning", ink, 14));
            var names = new Label { Text = TeamColumns.Line(column), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, ThemeTypeVariation = column.State is ColumnState.NeedsArmy or ColumnState.NeedsPlayer ? Variation.Warning : "",
                MouseFilter = MouseFilterEnum.Pass, TooltipText = TeamColumns.Line(column) };
            names.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(names);
        }
        wrap.AddChild(Toggle(false));
    }

    /// <summary>▸ opens the team lines into the columns, ▾ folds them again; the head is rebuilt with the screen's next layout.</summary>
    Button Toggle(bool open)
    {
        var b = new Button { Text = open ? "▾ Teams" : "▸", Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None,
            TooltipText = open ? "Fold the teams to a line each" : "Open the teams: move players, pick an army", SizeFlagsVertical = SizeFlags.ShrinkBegin,
            SizeFlagsHorizontal = open ? SizeFlags.ShrinkBegin : SizeFlags.ShrinkEnd };
        b.Pressed += () =>
        {
            if (Folds is null) return;
            Folds.TeamsOpen = !open;
            foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
            if (_onBoard is not null) { _session.BoardChanged -= _onBoard; _onBoard = null; }
            _Ready();
        };
        return b;
    }

    static TextureRect Icon(string name, Color tint, int size) => new()
    {
        Texture = Ui.Icon(name), Modulate = tint, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore,
    };
}
