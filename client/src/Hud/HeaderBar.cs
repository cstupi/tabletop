using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Top centre (UX redesign part 2 → Header), at most three single lines. The round, then one tile per team side by side (its band,
/// name, score as a numeral, a dot per unit in play filled once it has acted, the ▶ turn mark and a brighter border on the active team, the
/// player icon on mine), the objectives and the gear. The acting unit with its action's icon and the step strip; after the end the Game over
/// button, which reopens the end message. The waiting line: `Your move` in my team's colour, else the hourglass, the waiting team's band and
/// what it does, cut with an ellipsis (the whole line its tooltip). The lines draw the fullest HeaderFit whose first two lines fit the
/// width GameScreen gives (FitTo).</summary>
public partial class HeaderBar : PanelContainer
{
    Session _s = null!;
    readonly HBoxContainer _top = new();
    readonly HBoxContainer _tiles = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }, _activation = new(), _waiting = new();
    readonly Button _gameOver = new() { Flat = true, ThemeTypeVariation = Variation.FlatButton, Visible = false, Alignment = HorizontalAlignment.Left };
    readonly Button _settings = new() { Flat = true, ThemeTypeVariation = Variation.FlatButton, TooltipText = "Settings", FocusMode = FocusModeEnum.None };
    // Session and Ui outlive the header: the subscriptions are fields and come off in _ExitTree.
    Action<RenderCause>? _onFrame; Action? _onTheme;
    string? _drawn;   // what the lines last drew
    float _width;     // the slot's width (FitTo)

    public event Action<Guid, Vector2>? TeamMenuRequested;
    public event Action? GameOverPressed;
    public event Action? SettingsRequested;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        var lines = new VBoxContainer(); lines.AddThemeConstantOverride("separation", 4); AddChild(lines);
        _top.AddThemeConstantOverride("separation", 12); _tiles.AddThemeConstantOverride("separation", 8);
        _activation.AddThemeConstantOverride("separation", 10); _waiting.AddThemeConstantOverride("separation", 8);
        lines.AddChild(_top); lines.AddChild(_activation); lines.AddChild(_waiting);
        _settings.Pressed += () => SettingsRequested?.Invoke();
        _gameOver.Pressed += () => GameOverPressed?.Invoke();
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Game | RenderCause.Board)) != 0) Render(); };
        session.Frame.Render += _onFrame;
        _onTheme = () => { _drawn = null; Render(); }; Ui.Changed += _onTheme;
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is null) return;
        _s.Frame.Render -= _onFrame;
    }

    /// <summary>The slot's width: the lines redraw at the fullest fit for it when it changes.</summary>
    public void FitTo(float width)
    {
        if (Math.Abs(width - _width) < 0.5f) return;
        _width = width; _drawn = null;
        Render();
    }

    void Render()
    {
        var p = _s.Projection; var game = _s.Game;
        Visible = p is not null && game is not null && game.Status is GameStatus.Playing or GameStatus.Ended;
        if (!Visible) return;
        var names = new Names(game!, p!);
        var m = HeaderView.Of(p!, game!, names);
        var tiles = HeaderView.Tiles(p!, game!, names, _s.MyTeamIds);
        var objectives = HeaderView.Objectives(p!).ToList();
        // What the game waits on: deployment's players, a watcher's or a replay's line, else the open Asks (mine first).
        var quiet = HeaderView.WaitingForPlayers(_s.WaitingTeams) ?? (_s.Replaying ? HeaderView.Replaying : _s.IsWatching ? HeaderView.Watching : null);
        var wait = quiet is null ? HeaderView.Wait(p!, game!, names, _s.MyTeamIds) : null;
        var round = (m.Round, p!.Scheduler.Round == 0 ? p.Scheduler.Phase : null);
        // The lines are rebuilt only when something they show changed: most renders leave the header as it was.
        var key = string.Join("\u001f", (string?[])[round.ToString(), string.Join(",", tiles), string.Join(",", objectives), m.ActiveUnit, HeaderView.ActivationLine(p!, game!, names, _s.MyTeamId), m.Action,
            string.Join(",", m.Outline), quiet, wait?.ToString(), m.Result, HeaderView.RuleSetLine(game!), _width.ToString()]);
        if (key == _drawn) return;
        _drawn = key;
        // Each of the first two lines draws the fullest fit that fits the slot; the objectives leave line 1 for line 2 only when line 1
        // still overflows at Tight.
        var inner = _width - (GetThemeStylebox("panel")?.GetMinimumSize().X ?? 0);
        bool Fits(Control line) => _width <= 0 || line.GetCombinedMinimumSize().X <= inner;
        var marksOnTop = true;
        for (HeaderFit? fit = HeaderFit.Full; fit is { } f; fit = HeaderView.Tighter(f))
        {
            Top(f, true, round, tiles, objectives, m, game!);
            if (Fits(_top)) break;
            if (f == HeaderFit.Tight) { marksOnTop = false; Top(f, false, round, tiles, objectives, m, game!); }
        }
        for (HeaderFit? fit = HeaderFit.Full; fit is { } f; fit = HeaderView.Tighter(f))
        {
            var line = m.ActiveUnit is { } active ? HeaderView.ActivationLine(p!, game!, names, _s.MyTeamId, f) ?? active : null;
            Middle(f, marksOnTop ? [] : objectives, m, line, game!);
            if (Fits(_activation)) break;
        }
        Waiting(quiet, wait);
    }

    /// <summary>Line 1: the round, the tiles, the objectives when marksOnTop, the gear.</summary>
    void Top(HeaderFit fit, bool marksOnTop, (int Round, string? Phase) round, IReadOnlyList<TeamTile> tiles, IReadOnlyList<(Guid MarkerId, Guid? TeamId)> objectives,
        HeaderModel m, GameInfo game)
    {
        // The tiles box and the gear are kept nodes the line re-adds: taken out, never freed.
        Detach(_top, _tiles, _settings); HudBits.Clear(_tiles);
        var roundText = HeaderView.RoundText(round.Round, round.Phase, fit);
        var roundBox = new HBoxContainer { TooltipText = $"{HeaderView.RoundText(round.Round, round.Phase)} · {HeaderView.RuleSetLine(game)}", MouseFilter = MouseFilterEnum.Stop };
        roundBox.AddThemeConstantOverride("separation", 6);
        roundBox.AddChild(HudBits.Icon("round", Ui.Accent, 20));
        roundBox.AddChild(new Label { Text = roundText.ToUpperInvariant(), ThemeTypeVariation = Variation.Display });
        _top.AddChild(roundBox);
        _top.AddChild(_tiles);
        foreach (var tile in tiles) _tiles.AddChild(Tile(tile, game.Status == GameStatus.Playing && m.Round > 0, fit));
        if (marksOnTop) foreach (var mark in Marks(objectives, fit, game)) _tiles.AddChild(mark);
        _settings.Icon = Ui.Icon("settings");
        _settings.Text = _settings.Icon is null ? "Settings" : "";   // never a zero-width button
        _top.AddChild(_settings);
    }

    List<Control> Marks(IReadOnlyList<(Guid MarkerId, Guid? TeamId)> objectives, HeaderFit fit, GameInfo game) =>
        objectives.Select(o => Objective(o.TeamId is { } t ? game.Teams.FirstOrDefault(x => x.Id == t)?.Ordinal ?? 0 : 0, fit)).ToList();

    /// <summary>Line 2: the objectives line 1 had no room for, the acting unit and its steps, or the game's result.</summary>
    void Middle(HeaderFit fit, IReadOnlyList<(Guid MarkerId, Guid? TeamId)> objectives, HeaderModel m, string? activationLine, GameInfo game)
    {
        Detach(_activation, _gameOver);   // the Game over button is kept: taken out, never freed
        if (objectives.Count > 0)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4);
            foreach (var mark in Marks(objectives, HeaderFit.Tight, game)) row.AddChild(mark);
            _activation.AddChild(row);
        }
        if (m.Result is { } result)
        {
            _gameOver.Text = $"✓ {result}"; _gameOver.Visible = true;
            _activation.AddChild(_gameOver);
        }
        else if (activationLine is not null)
        {
            var who = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; who.AddThemeConstantOverride("separation", 6);
            who.AddChild(HudBits.Icon(HeaderView.ActionIcon(m.Action), Ui.Accent, 18));
            who.AddChild(OneLine(activationLine, ""));
            _activation.AddChild(who);
            foreach (var item in m.Outline)
                _activation.AddChild(new Label
                {
                    Text = HeaderView.StepText(item, fit), TooltipText = item.Label, MouseFilter = MouseFilterEnum.Stop,
                    ThemeTypeVariation = item.State switch { OutlineState.Done => Variation.Success, OutlineState.Current => Variation.Accent, _ => Variation.Dim },
                });
        }
        _activation.Visible = _activation.GetChildCount() > 0;
    }

    /// <summary>Line 3: what the game waits on, one line.</summary>
    void Waiting(string? quiet, WaitLine? wait)
    {
        HudBits.Clear(_waiting);
        _waiting.Visible = quiet is not null || wait is not null;
        if (quiet is not null)
        {
            _waiting.AddChild(HudBits.Icon(_s.Replaying || _s.IsWatching ? "watch" : "wait", Ui.DimText, 16));
            _waiting.AddChild(OneLine(quiet, Variation.Dim));
        }
        else if (wait is { Mine: true } mine)
        {
            var colour = Ui.Team(mine.Ordinal);
            _waiting.AddChild(HudBits.Icon("turn", colour, 16));
            var line = OneLine(mine.Text, Variation.Display); line.AddThemeColorOverride("font_color", colour);
            _waiting.AddChild(line);
        }
        else if (wait is { } other)
        {
            _waiting.AddChild(HudBits.Band(other.Ordinal, 4));
            _waiting.AddChild(HudBits.Icon("wait", Ui.DimText, 16));
            _waiting.AddChild(OneLine(other.Text, ""));
        }
    }

    static void Detach(Node box, params Node[] kept)
    {
        foreach (var c in box.GetChildren()) { box.RemoveChild(c); if (!kept.Contains(c)) c.QueueFree(); }
    }

    /// <summary>A team's tile: its band down the left edge, the name, the score, the activation dots, mine's player icon and the active
    /// team's ▶ with a brighter border. A right-click opens the team menu.</summary>
    Control Tile(TeamTile t, bool dots, HeaderFit fit)
    {
        var tile = new PanelContainer { MouseFilter = MouseFilterEnum.Stop,
            TooltipText = $"{t.Name} · {HeaderView.ScoreText(t.Score)}{(t.Mine ? " · you" : "")}{(t.Acting ? " · their turn" : "")}" };
        var box = new StyleBoxFlat { BgColor = Ui.Team(t.Ordinal) with { A = t.Acting ? 0.22f : 0.10f }, BorderColor = Ui.Team(t.Ordinal) with { A = t.Acting ? 1f : 0.45f },
            CornerDetail = Ui.Current.Shape.Clipped ? 1 : 8 };
        box.SetBorderWidthAll(t.Acting ? 2 : 1); box.BorderWidthLeft = 6; box.SetCornerRadiusAll(Math.Min(Ui.Current.Shape.CornerRadius, 6));
        box.ContentMarginLeft = 10; box.ContentMarginRight = 8; box.ContentMarginTop = 2; box.ContentMarginBottom = 2;
        tile.AddThemeStyleboxOverride("panel", box);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 6); tile.AddChild(row);
        if (t.Mine) row.AddChild(HudBits.Icon("player", Ui.Team(t.Ordinal), 16));
        var name = new Label { Text = t.Name.ToUpperInvariant(), MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        name.AddThemeColorOverride("font_color", Ui.Team(t.Ordinal)); row.AddChild(name);
        var score = new Label { Text = t.Score.ToString(), MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        score.AddThemeFontSizeOverride("font_size", Ui.Current.Body.Size + 6); row.AddChild(score);
        if (fit == HeaderFit.Full) row.AddChild(new Label { Text = "VP", ThemeTypeVariation = Variation.Dim, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center });
        if (dots && t.InPlay > 0)
        {
            var d = new Label { Text = t.Dots ? new string('●', t.Acted) + new string('○', t.InPlay - t.Acted) : t.CountText, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
            d.AddThemeColorOverride("font_color", Ui.Team(t.Ordinal));
            row.AddChild(d);
            tile.TooltipText += $" · {HeaderView.NotActivatedText(t.InPlay - t.Acted)}";
        }
        if (t.Acting) row.AddChild(HudBits.Icon("turn", Ui.Team(t.Ordinal), 14));
        var id = t.TeamId;
        tile.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) TeamMenuRequested?.Invoke(id, b.GlobalPosition); };
        return tile;
    }

    /// <summary>An objective: its icon in the chip frame, the controller's colour when held; past Full the bare icon in that colour.</summary>
    static Control Objective(int ordinal, HeaderFit fit)
    {
        if (fit != HeaderFit.Full)
        {
            var icon = HudBits.Icon("objective", ordinal > 0 ? Ui.Team(ordinal) : HudBits.Plain, 18, ordinal > 0 ? $"Objective · Team {ordinal}" : "Objective");
            icon.MouseFilter = MouseFilterEnum.Stop;
            return icon;
        }
        var chip = new PanelContainer { ThemeTypeVariation = Variation.Chip, TooltipText = "Objective", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        chip.AddChild(HudBits.Icon("objective", HudBits.Plain, 16));
        if (ordinal > 0 && Ui.Theme.GetStylebox("panel", Variation.Chip).Duplicate() is StyleBoxFlat box)
        {
            box.BorderColor = Ui.Team(ordinal); box.BgColor = Ui.Team(ordinal) with { A = 0.25f }; box.SetBorderWidthAll(2);
            chip.AddThemeStyleboxOverride("panel", box);
        }
        return chip;
    }

    /// <summary>One line cut with an ellipsis to the width it gets; the whole text is its tooltip.</summary>
    static Label OneLine(string text, string variation) =>
        new() { Text = text, ThemeTypeVariation = variation, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = text,
            MouseFilter = MouseFilterEnum.Stop, SizeFlagsHorizontal = SizeFlags.ExpandFill };
}
