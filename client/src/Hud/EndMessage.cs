using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>The Game over modal over the table. While warn's game_over Ask is open (GameOverView): its prompt and each team's score, with End and
/// Continue answering the Ask until my team has answered, then "Waiting for the other player". Once the game has ended: the result, its reason
/// (the mission result or the strict breach's detail) and the scores; Stay (or Esc) closes it and keeps the table, the log and undo, Leave goes
/// home. The header's "Game over" button opens it again.</summary>
public partial class EndMessage : Control
{
    Session _s = null!; Guid? _shownFor; bool _endedShown;
    readonly PanelContainer _panel = new();
    readonly Label _result = new() { ThemeTypeVariation = Variation.Display, HorizontalAlignment = HorizontalAlignment.Center },
        _reason = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center },
        _note = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly VBoxContainer _scores = new();
    readonly HBoxContainer _buttons = new() { Alignment = BoxContainer.AlignmentMode.Center };
    Action<RenderCause>? _onFrame; Action? _onTheme;
    public event Action? LeaveRequested;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;                                         // modal: the backdrop takes every click that misses the panel
        Visible = false;
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; centre.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(centre);
        centre.AddChild(_panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) }; box.AddThemeConstantOverride("separation", 12); _panel.AddChild(box);
        box.AddChild(new Label { Text = "Game over", ThemeTypeVariation = Variation.Banner, HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(_result); box.AddChild(_reason); box.AddChild(_scores); box.AddChild(_note);
        _buttons.AddThemeConstantOverride("separation", 16); box.AddChild(_buttons);
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
        var m = HeaderView.Of(p, game, names);
        _result.Text = model.Result ?? "End or continue?";
        _reason.Text = model.Reason;
        // A player holding both teams answers for each in turn, the team named over the buttons.
        var note = model.Stage == GameOverStage.Choose ? Seats.PlayingAs(game, _s.MyTeamIds, _s.MyTeamId) : model.Note;
        _note.Text = note ?? ""; _note.Visible = note is not null;
        foreach (var c in _scores.GetChildren()) { _scores.RemoveChild(c); c.QueueFree(); }
        foreach (var team in game.Teams.OrderBy(t => t.Ordinal))
        {
            var name = names.Team(team.Id);
            var score = new Label { Text = $"{name}  {m.Scores.FirstOrDefault(s => s.Team == name).Score}", HorizontalAlignment = HorizontalAlignment.Center };
            score.AddThemeColorOverride("font_color", Palette.Team(team.Ordinal));
            _scores.AddChild(score);
        }
        foreach (var c in _buttons.GetChildren()) { _buttons.RemoveChild(c); c.QueueFree(); }
        if (model.Stage == GameOverStage.Ended)
        {
            AddButton("Stay", Close);
            AddButton("Leave", () => LeaveRequested?.Invoke());
        }
        var acting = _s.MyTeamId;
        foreach (var choice in model.Choices)
            AddButton(choice.Label, () => Answer(model.AskId!.Value, choice.Id, acting));
        return true;
    }

    void AddButton(string text, Action pressed)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None }; b.Pressed += pressed; _buttons.AddChild(b);
    }

    /// <summary>One answer per Ask: the buttons go at once, and the next render shows the waiting line or the end.</summary>
    void Answer(Guid askId, string optionId, Guid? team)
    {
        foreach (var c in _buttons.GetChildren()) if (c is Button b) b.Disabled = true;
        Guarded.Run(() => _s.SendCommandAsync(new AnswerAsk(askId, new Chosen(optionId), team)), message => { GD.PushWarning($"Answer failed: {message}"); if (IsInstanceValid(this)) Fill(); });
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || KeyOverlay.IntentOf(e, GetViewport()) != Intent.Cancel) return;
        Close(); GetViewport().SetInputAsHandled();
    }
}
