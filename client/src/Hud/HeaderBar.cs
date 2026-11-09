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
    string? _drawnTop, _drawnMiddle, _drawnWait;   // what each line last drew
    bool _marksOnTop = true;                       // the objectives fit on line 1
    float _width;     // the slot's width (FitTo)
    // Line 1's round box and line 2's and 3's children stand across renders: a render sets them (Kept, Look).
    readonly HBoxContainer _roundBox = new() { MouseFilter = MouseFilterEnum.Stop };
    readonly TextureRect _roundIcon = HudBits.Icon("round", Ui.Accent, 20);
    readonly Label _roundText = new() { ThemeTypeVariation = Variation.Display };
    readonly Kept _tileKids, _middleKids, _waitKids; Kept? _middleMarks;

    public HeaderBar() { _tileKids = new(_tiles); _middleKids = new(_activation); _waitKids = new(_waiting); }

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
        _roundBox.AddThemeConstantOverride("separation", 6); _roundBox.AddChild(_roundIcon); _roundBox.AddChild(_roundText);
        _top.AddChild(_roundBox); _top.AddChild(_tiles); _top.AddChild(_settings);
        _settings.Pressed += () => SettingsRequested?.Invoke();
        _gameOver.Pressed += () => GameOverPressed?.Invoke();
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Game | RenderCause.Board)) != 0) Render(); };
        session.Frame.Render += _onFrame;
        _onTheme = () => { _drawnTop = _drawnMiddle = _drawnWait = null; Restyle(); Render(); }; Ui.Changed += _onTheme;
        Restyle();
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is null) return;
        _s.Frame.Render -= _onFrame;
    }

    /// <summary>The theme's icons on the standing controls.</summary>
    void Restyle()
    {
        Look.Icon(_roundIcon, "round", Ui.Accent);
        _settings.Icon = Ui.Icon("settings");
        _settings.Text = _settings.Icon is null ? "Settings" : "";   // never a zero-width button
    }

    /// <summary>The slot's width: the lines redraw at the fullest fit for it when it changes.</summary>
    public void FitTo(float width)
    {
        if (Math.Abs(width - _width) < 0.5f) return;
        _width = width; _drawnTop = _drawnMiddle = _drawnWait = null;
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
        // Each line is rebuilt only when something it shows changed: a move's step leaves the round and the tiles standing, and every new
        // control costs its layout and text shaping in the frame it appears.
        static string Key(params string?[] parts) => string.Join("\u001f", parts);
        var width = _width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var topKey = Key(round.ToString(), string.Join(",", tiles), string.Join(",", objectives), m.Round.ToString(), game!.Status.ToString(), HeaderView.RuleSetLine(game!), width);
        // Each of the first two lines draws the fullest fit that fits the slot; the objectives leave line 1 for line 2 only when line 1
        // still overflows at Tight.
        var inner = _width - (GetThemeStylebox("panel")?.GetMinimumSize().X ?? 0);
        bool Fits(Control line) => _width <= 0 || line.GetCombinedMinimumSize().X <= inner;
        if (topKey != _drawnTop)
        {
            _drawnTop = topKey; _marksOnTop = true;
            for (HeaderFit? fit = HeaderFit.Full; fit is { } f; fit = HeaderView.Tighter(f))
            {
                Top(f, true, round, tiles, objectives, m, game!);
                if (Fits(_top)) break;
                if (f == HeaderFit.Tight) { _marksOnTop = false; Top(f, false, round, tiles, objectives, m, game!); }
            }
        }
        var middleKey = Key(m.ActiveUnit, HeaderView.ActivationLine(p!, game!, names, _s.MyTeamId), m.Action, string.Join(",", m.Outline), m.Result,
            _marksOnTop ? "" : string.Join(",", objectives), width);
        if (middleKey != _drawnMiddle)
        {
            _drawnMiddle = middleKey;
            for (HeaderFit? fit = HeaderFit.Full; fit is { } f; fit = HeaderView.Tighter(f))
            {
                var line = m.ActiveUnit is { } active ? HeaderView.ActivationLine(p!, game!, names, _s.MyTeamId, f) ?? active : null;
                Middle(f, _marksOnTop ? [] : objectives, m, line, game!);
                if (Fits(_activation)) break;
            }
        }
        var waitKey = Key(quiet, wait?.ToString(), _s.Replaying.ToString(), _s.IsWatching.ToString());
        if (waitKey != _drawnWait) { _drawnWait = waitKey; Waiting(quiet, wait); }
    }

    /// <summary>Line 1: the round, the tiles, the objectives when marksOnTop, the gear. The round box, the tiles box and the gear stand from Bind;
    /// the tiles and marks are kept controls (Kept) a render sets.</summary>
    void Top(HeaderFit fit, bool marksOnTop, (int Round, string? Phase) round, IReadOnlyList<TeamTile> tiles, IReadOnlyList<(Guid MarkerId, Guid? TeamId)> objectives,
        HeaderModel m, GameInfo game)
    {
        Look.Tip(_roundBox, $"{HeaderView.RoundText(round.Round, round.Phase)} · {HeaderView.RuleSetLine(game)}");
        Look.Text(_roundText, HeaderView.RoundText(round.Round, round.Phase, fit).ToUpperInvariant(), Variation.Display);
        _tileKids.Begin();
        foreach (var tile in tiles) Tile(_tileKids.Take("tile", () => new TileControls { Header = this }), tile, game.Status == GameStatus.Playing && m.Round > 0, fit);
        if (marksOnTop) Marks(_tileKids, objectives, fit, game);
        _tileKids.End();
    }

    void Marks(Kept into, IReadOnlyList<(Guid MarkerId, Guid? TeamId)> objectives, HeaderFit fit, GameInfo game)
    {
        foreach (var o in objectives) Objective(into, o.TeamId is { } t ? game.Teams.FirstOrDefault(x => x.Id == t)?.Ordinal ?? 0 : 0, fit);
    }

    /// <summary>Line 2: the objectives line 1 had no room for, the acting unit and its steps, or the game's result.</summary>
    void Middle(HeaderFit fit, IReadOnlyList<(Guid MarkerId, Guid? TeamId)> objectives, HeaderModel m, string? activationLine, GameInfo game)
    {
        _middleKids.Begin();
        if (objectives.Count > 0)
        {
            var row = _middleKids.Take("marks", () => { var r = new HBoxContainer(); r.AddThemeConstantOverride("separation", 4); return r; });
            _middleMarks ??= new Kept(row);
            Marks(_middleMarks.Begin(), objectives, HeaderFit.Tight, game); _middleMarks.End();
        }
        if (m.Result is { } result)
        {
            _gameOver.Text = $"✓ {result}";
            _middleKids.Take("over", () => _gameOver);
        }
        else if (activationLine is not null)
        {
            var who = _middleKids.Take("who", () =>
            {
                var box = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 6);
                box.AddChild(HudBits.Icon("", Ui.Accent, 18)); box.AddChild(OneLine("", ""));
                return box;
            });
            Look.Icon((TextureRect)who.GetChild(0), HeaderView.ActionIcon(m.Action), Ui.Accent);
            var line = (Label)who.GetChild(1); Look.Text(line, activationLine); Look.Tip(line, activationLine);
            foreach (var item in m.Outline)
            {
                var step = _middleKids.Take("step", () => new Label { MouseFilter = MouseFilterEnum.Stop });
                Look.Text(step, HeaderView.StepText(item, fit), item.State switch { OutlineState.Done => Variation.Success, OutlineState.Current => Variation.Accent, _ => Variation.Dim });
                Look.Tip(step, item.Label);
            }
        }
        _activation.Visible = _middleKids.End() > 0;
    }

    /// <summary>Line 3: what the game waits on, one line.</summary>
    void Waiting(string? quiet, WaitLine? wait)
    {
        _waitKids.Begin();
        _waiting.Visible = quiet is not null || wait is not null;
        TextureRect Icon(string name, Color tint) { var r = _waitKids.Take("icon", () => HudBits.Icon(name, tint, 16)); Look.Icon(r, name, tint); return r; }
        Label Line(string text, string variation, Color? ink)
        {
            var l = _waitKids.Take("line", () => OneLine("", "")); Look.Text(l, text, variation); Look.Tip(l, text); Look.Ink(l, ink); return l;
        }
        if (quiet is not null)
        {
            Icon(_s.Replaying || _s.IsWatching ? "watch" : "wait", Ui.DimText);
            Line(quiet, Variation.Dim, null);
        }
        else if (wait is { Mine: true } mine)
        {
            var colour = Ui.Team(mine.Ordinal);
            Icon("turn", colour);
            Line(mine.Text, Variation.Display, colour);
        }
        else if (wait is { } other)
        {
            var band = _waitKids.Take("band", () => HudBits.Band(other.Ordinal, 4));
            var bandColour = other.Ordinal > 0 ? Ui.Team(other.Ordinal) : Ui.DimText with { A = 0 };
            if (band.Color != bandColour) band.Color = bandColour;
            Icon("wait", Ui.DimText);
            Line(other.Text, "", null);
        }
        _waitKids.End();
    }

    /// <summary>A team tile's controls, made once and set by each render: its band down the left edge, the player icon on mine, the name, the
    /// score, VP at Full, the activation dots, the active team's ▶ with a brighter border. A right-click opens the team menu.</summary>
    sealed partial class TileControls : PanelContainer
    {
        public readonly StyleBoxFlat Frame = new();
        public readonly TextureRect Player = HudBits.Icon("player", HudBits.Plain, 16), Turn = HudBits.Icon("turn", HudBits.Plain, 14);
        public readonly Label Title = new() { MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center },
            Score = new() { MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center },
            Vp = new() { Text = "VP", ThemeTypeVariation = Variation.Dim, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center },
            Dots = new() { MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        public Guid TeamId;

        public HeaderBar? Header;

        public TileControls()
        {
            MouseFilter = MouseFilterEnum.Stop;
            Frame.BorderWidthLeft = 6; Frame.ContentMarginLeft = 10; Frame.ContentMarginRight = 8; Frame.ContentMarginTop = 2; Frame.ContentMarginBottom = 2;
            AddThemeStyleboxOverride("panel", Frame);
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 6); AddChild(row);
            foreach (var c in new Control[] { Player, Title, Score, Vp, Dots, Turn }) row.AddChild(c);
            GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) Header?.TeamMenuRequested?.Invoke(TeamId, b.GlobalPosition); };
        }
    }

    static void Tile(TileControls c, TeamTile t, bool dots, HeaderFit fit)
    {
        c.TeamId = t.TeamId;
        var team = Ui.Team(t.Ordinal);
        var tip = $"{t.Name} · {HeaderView.ScoreText(t.Score)}{(t.Mine ? " · you" : "")}{(t.Acting ? " · their turn" : "")}";
        var showDots = dots && t.InPlay > 0;
        if (showDots) tip += $" · {HeaderView.NotActivatedText(t.InPlay - t.Acted)}";
        Look.Tip(c, tip);
        var f = c.Frame;
        var (bg, border, width, detail, radius) = (team with { A = t.Acting ? 0.22f : 0.10f }, team with { A = t.Acting ? 1f : 0.45f }, t.Acting ? 2 : 1,
            Ui.Current.Shape.Clipped ? 1 : 8, Math.Min(Ui.Current.Shape.CornerRadius, 6));
        if (f.BgColor != bg) f.BgColor = bg;
        if (f.BorderColor != border) f.BorderColor = border;
        if (f.BorderWidthTop != width) { f.BorderWidthTop = f.BorderWidthRight = f.BorderWidthBottom = width; }
        if (f.CornerDetail != detail) f.CornerDetail = detail;
        if (f.CornerRadiusTopLeft != radius) f.SetCornerRadiusAll(radius);
        c.Player.Visible = t.Mine; if (t.Mine) Look.Icon(c.Player, "player", team);
        Look.Text(c.Title, t.Name.ToUpperInvariant()); Look.Ink(c.Title, team);
        Look.Text(c.Score, t.Score.ToString()); Look.FontSize(c.Score, Ui.Current.Body.Size + 6);
        c.Vp.Visible = fit == HeaderFit.Full;
        c.Dots.Visible = showDots;
        if (showDots) { Look.Text(c.Dots, t.Dots ? new string('●', t.Acted) + new string('○', t.InPlay - t.Acted) : t.CountText); Look.Ink(c.Dots, team); }
        c.Turn.Visible = t.Acting; if (t.Acting) Look.Icon(c.Turn, "turn", team);
    }

    /// <summary>An objective: its icon in the chip frame, the controller's colour when held; past Full the bare icon in that colour.</summary>
    static void Objective(Kept into, int ordinal, HeaderFit fit)
    {
        if (fit != HeaderFit.Full)
        {
            var icon = into.Take("mark", () => { var r = HudBits.Icon("objective", HudBits.Plain, 18); r.MouseFilter = MouseFilterEnum.Stop; return r; });
            Look.Icon(icon, "objective", ordinal > 0 ? Ui.Team(ordinal) : HudBits.Plain);
            Look.Tip(icon, ordinal > 0 ? $"Objective · Team {ordinal}" : "Objective");
            return;
        }
        var chip = into.Take("chip", () =>
        {
            var c = new PanelContainer { ThemeTypeVariation = Variation.Chip, TooltipText = "Objective", SizeFlagsVertical = SizeFlags.ShrinkCenter };
            c.AddChild(HudBits.Icon("objective", HudBits.Plain, 16));
            return c;
        });
        Look.Icon((TextureRect)chip.GetChild(0), "objective", HudBits.Plain);
        Look.Box(chip, ordinal > 0 ? HeldChip(ordinal) : null);
    }

    /// <summary>The chip frame in a team's colour, one per team and theme.</summary>
    static StyleBox? HeldChip(int ordinal)
    {
        if (_heldChips.Theme != Ui.Theme) _heldChips = (Ui.Theme, new());
        if (_heldChips.Boxes.TryGetValue(ordinal, out var made)) return made;
        if (Ui.Theme.GetStylebox("panel", Variation.Chip).Duplicate() is not StyleBoxFlat box) return null;
        box.BorderColor = Ui.Team(ordinal); box.BgColor = Ui.Team(ordinal) with { A = 0.25f }; box.SetBorderWidthAll(2);
        return _heldChips.Boxes[ordinal] = box;
    }
    static (Theme? Theme, Dictionary<int, StyleBox> Boxes) _heldChips = (null, new());

    /// <summary>One line cut with an ellipsis to the width it gets; the whole text is its tooltip.</summary>
    static Label OneLine(string text, string variation) =>
        new() { Text = text, ThemeTypeVariation = variation, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = text,
            MouseFilter = MouseFilterEnum.Stop, SizeFlagsHorizontal = SizeFlags.ExpandFill };
}
