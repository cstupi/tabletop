using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>The Game over modal over the table (UX redesign part 2 → Game over): `✓ GAME OVER`, the result with the rounds played, each team's
/// tile as the header draws it, larger, the reason, Stay and ⌂ Leave; while the game_over Ask is open `END OR CONTINUE?`. While warn's game_over Ask is open (GameOverView): its prompt and each team's score, with End and
/// Continue answering the Ask until my team has answered, then "Waiting for the other player". Once the game has ended: the result, its reason
/// (the mission result or the strict breach's detail) and the scores; Stay (or Esc) closes it and keeps the table, the log and undo, Leave goes
/// home. The header's "Game over" button opens it again.</summary>
public partial class EndMessage : Control
{
    Session _s = null!; Guid? _shownFor; bool _endedShown;
    readonly PanelContainer _panel = new();
    readonly Label _title = new() { ThemeTypeVariation = Variation.Banner, HorizontalAlignment = HorizontalAlignment.Center },
        _result = new() { ThemeTypeVariation = Variation.Display, HorizontalAlignment = HorizontalAlignment.Center },
        _reason = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center },
        _note = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly HBoxContainer _scores = new() { Alignment = BoxContainer.AlignmentMode.Center }, _waiting = new() { Alignment = BoxContainer.AlignmentMode.Center };
    readonly HBoxContainer _buttons = new() { Alignment = BoxContainer.AlignmentMode.Center };
    Action<RenderCause>? _onFrame; Action? _onTheme;
    public event Action? LeaveRequested;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;                                         // modal: the backdrop takes every click that misses the panel
        Visible = false;
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; centre.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(centre);
        centre.AddChild(_panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) }; box.AddThemeConstantOverride("separation", Space.M); _panel.AddChild(box);
        _scores.AddThemeConstantOverride("separation", Space.M); _waiting.AddThemeConstantOverride("separation", Space.S);
        box.AddChild(_title); box.AddChild(_result); box.AddChild(_scores); box.AddChild(_reason); box.AddChild(_note); box.AddChild(_waiting);
        _buttons.AddThemeConstantOverride("separation", Space.M); box.AddChild(_buttons);
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Game)) != 0) Render(); };
        session.Frame.Render += _onFrame;
        _onTheme = Render; Ui.Changed += _onTheme;
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is null) return;
        _s.Frame.Render -= _onFrame;
    }

    public void Open() { if (Fill()) Visible = true; }
    public void Close() => Visible = false;

    GameOverModel? Model() => _s.Projection is { } p && _s.Game is { } game ? GameOverView.Of(p, game, _s.MyTeamId) : null;

    /// <summary>Opens once for each game_over Ask and once when the game ends; an undo past the end closes it and arms it again.</summary>
    void Render()
    {
        var m = Model();
        if (m is null || m.Stage == GameOverStage.None) { _shownFor = null; _endedShown = false; Visible = false; return; }
        if (m.Stage == GameOverStage.Ended ? !_endedShown : _shownFor != m.AskId)
        {
            _endedShown = m.Stage == GameOverStage.Ended; _shownFor = m.AskId;
            Open();
        }
        else if (Visible) Fill();
    }

    bool Fill()
    {
        var p = _s.Projection; var game = _s.Game;
        if (Model() is not { Stage: not GameOverStage.None } model || p is null || game is null) return false;
        var names = new Names(game, p);
        var ended = model.Stage == GameOverStage.Ended;
        _title.Text = ended ? "✓ GAME OVER" : "END OR CONTINUE?";
        _result.Text = model.Headline ?? ""; _result.Visible = model.Headline is not null;
        _reason.Text = model.Reason;
        // A player holding both teams answers for each in turn, the team named over the buttons; once mine has answered, the teams still to.
        var note = model.Stage == GameOverStage.Choose ? Seats.PlayingAs(game, _s.MyTeamIds, _s.MyTeamId) : model.Stage == GameOverStage.Waiting ? null : model.Note;
        _note.Text = note ?? ""; _note.Visible = note is not null;
        HudBits.Clear(_waiting);
        _waiting.Visible = model.Stage == GameOverStage.Waiting;
        if (_waiting.Visible)
        {
            _waiting.AddChild(HudBits.Icon("wait", Ui.DimText, 18));
            _waiting.AddChild(new Label { Text = model.WaitingFor is { Count: > 0 } ? "Waiting for" : model.Note ?? GameOverView.WaitingNote, ThemeTypeVariation = Variation.Dim });
            foreach (var team in model.WaitingFor ?? [])
                if (game.Teams.FirstOrDefault(t => t.Id == team) is { } t)
                {
                    _waiting.AddChild(HudBits.Band(t.Ordinal, 4));
                    var l = new Label { Text = names.Team(team) }; l.AddThemeColorOverride("font_color", Ui.Team(t.Ordinal)); _waiting.AddChild(l);
                }
        }
        // The header's tiles, larger: band, name, score.
        HudBits.Clear(_scores);
        foreach (var tile in HeaderView.Tiles(p, game, names, _s.MyTeamIds)) _scores.AddChild(ScoreTile(tile));
        HudBits.Clear(_buttons);
        if (ended)
        {
            AddButton("Stay", Close);
            AddButton("Leave", () => LeaveRequested?.Invoke(), "home");
        }
        var acting = _s.MyTeamId;
        foreach (var choice in model.Choices)
            AddButton(choice.Label, () => Answer(model, choice.Id, acting));
        return true;
    }

    void AddButton(string text, Action pressed, string? icon = null)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 0), Icon = icon is null ? null : Ui.Icon(icon) };
        b.AddThemeConstantOverride("icon_max_width", 18); b.AddThemeColorOverride("icon_normal_color", Ui.Text);
        b.Pressed += pressed; _buttons.AddChild(b);
    }

    /// <summary>A team's score as the header draws it, larger: the band down its left edge, the name in its colour, the numeral and VP.</summary>
    static Control ScoreTile(TeamTile t)
    {
        var tile = new PanelContainer();
        var box = new StyleBoxFlat { BgColor = Ui.Team(t.Ordinal) with { A = 0.12f }, BorderColor = Ui.Team(t.Ordinal) with { A = 0.6f }, CornerDetail = Ui.Current.Shape.Clipped ? 1 : 8 };
        box.SetBorderWidthAll(1); box.BorderWidthLeft = 8; box.SetCornerRadiusAll(Math.Min(Ui.Current.Shape.CornerRadius, 6));
        box.ContentMarginLeft = box.ContentMarginRight = Space.M; box.ContentMarginTop = box.ContentMarginBottom = Space.S;
        tile.AddThemeStyleboxOverride("panel", box);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Space.S); tile.AddChild(row);
        if (t.Mine) row.AddChild(HudBits.Icon("player", Ui.Team(t.Ordinal), 20));
        var name = new Label { Text = t.Name.ToUpperInvariant(), VerticalAlignment = VerticalAlignment.Center }; name.AddThemeColorOverride("font_color", Ui.Team(t.Ordinal)); row.AddChild(name);
        var score = new Label { Text = t.Score.ToString(), VerticalAlignment = VerticalAlignment.Center };
        score.AddThemeFontSizeOverride("font_size", Ui.Current.Body.Size * 2); row.AddChild(score);
        row.AddChild(new Label { Text = "VP", ThemeTypeVariation = Variation.Dim, VerticalAlignment = VerticalAlignment.Center });
        return tile;
    }

    /// <summary>One answer per Ask: the buttons go at once, and the next render shows the waiting line or the end.</summary>
    void Answer(GameOverModel model, string optionId, Guid? team)
    {
        foreach (var c in _buttons.GetChildren()) if (c is Button b) b.Disabled = true;
        Guarded.Run(() => _s.SendCommandAsync(GameOverView.Answer(model, optionId, team)), message => { GD.PushWarning($"Answer failed: {message}"); if (IsInstanceValid(this)) Fill(); });
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || KeyOverlay.IntentOf(e, GetViewport()) != Intent.Cancel) return;
        Close(); GetViewport().SetInputAsHandled();
    }
}
