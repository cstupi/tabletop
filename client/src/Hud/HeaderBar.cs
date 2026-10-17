using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Top centre, three lines: round, turn and scores; the active unit, its action and the step strip; the waiting
/// line ("Your move: …" first; the watching line for a watcher). After the end the first line carries "Game over", which reopens the end message.</summary>
public partial class HeaderBar : PanelContainer
{
    Session _s = null!;
    readonly HBoxContainer _top = new();
    readonly HFlowContainer _teams = new(), _activation = new();   // both wrap inside the header's slot
    readonly Label _waiting = new() { ThemeTypeVariation = Variation.Warning };
    readonly Button _gameOver = new() { Flat = true, ThemeTypeVariation = Variation.FlatButton, Visible = false };
    readonly Button _settings = new() { Flat = true, ThemeTypeVariation = Variation.FlatButton, TooltipText = "Settings", FocusMode = FocusModeEnum.None };
    // Session and Ui outlive the header: the subscriptions are fields and come off in _ExitTree.
    Action<RenderCause>? _onFrame; Action? _onTheme;

    public event Action<Guid, Vector2>? TeamMenuRequested;
    public event Action? GameOverPressed;
    public event Action? SettingsRequested;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        var lines = new VBoxContainer(); AddChild(lines);
        lines.AddChild(_top); lines.AddChild(_teams); lines.AddChild(_activation); lines.AddChild(_waiting);
        _top.AddChild(_gameOver); _top.AddChild(_settings);
        _settings.Pressed += () => SettingsRequested?.Invoke();
        _gameOver.Pressed += () => GameOverPressed?.Invoke();
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Game | RenderCause.Board)) != 0) Render(); };
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

    void Render()
    {
        var p = _s.Projection; var game = _s.Game;
        Visible = p is not null && game is not null && game.Status is GameStatus.Playing or GameStatus.Ended;
        if (!Visible) return;
        var names = new Names(game!, p);
        var m = HeaderView.Of(p!, game!, names);
        Clear(_top, _gameOver, _settings); Clear(_teams); Clear(_activation);
        var waiting = HeaderView.NotActivated(p!, game!);

        _top.AddChild(new Label { Text = m.TopLine });
        foreach (var team in game!.Teams.OrderBy(t => t.Ordinal))
        {
            var name = names.Team(team.Id);
            var score = m.Scores.FirstOrDefault(s => s.Team == name).Score;
            var label = new Label { Text = $"  {name} {score}", MouseFilter = MouseFilterEnum.Stop, TooltipText = "Right-click: team menu" };
            label.AddThemeColorOverride("font_color", Palette.Team(team.Ordinal));
            var id = team.Id;
            label.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) TeamMenuRequested?.Invoke(id, b.GlobalPosition); };
            var cell = new HBoxContainer(); cell.AddChild(label);
            cell.AddChild(new Label { Text = $" · {waiting.GetValueOrDefault(team.Id)} not activated", ThemeTypeVariation = Variation.Dim });
            _teams.AddChild(cell);
        }
        foreach (var (_, owner) in HeaderView.Objectives(p!))
        {
            var chip = new PanelContainer { ThemeTypeVariation = Variation.Chip, TooltipText = "Objective" };
            chip.AddChild(Roster.Icon("objective"));
            if (owner is { } t && Ui.Theme.GetStylebox("panel", Variation.Chip).Duplicate() is StyleBoxFlat box)
            {
                var colour = Palette.Team(game.Teams.First(x => x.Id == t).Ordinal);
                box.BorderColor = colour; box.BgColor = colour.Lightened(0.6f); box.SetBorderWidthAll(2);
                chip.AddThemeStyleboxOverride("panel", box);
            }
            _teams.AddChild(chip);
        }
        _gameOver.Visible = m.Result is not null;
        _gameOver.Text = m.Result is { } result ? $"  {result}" : "";
        _top.MoveChild(_gameOver, -1); _top.MoveChild(_settings, -1);
        _settings.Icon = Ui.Icon("settings");
        _settings.Text = _settings.Icon is null ? "Settings" : "";   // never a zero-width button

        _activation.Visible = m.ActiveUnit is not null;
        if (m.ActiveUnit is { } unit)
        {
            _activation.AddChild(new Label { Text = HeaderView.ActivationLine(p!, game!, names, _s.MyTeamId) ?? unit });
            foreach (var item in m.Outline)
                _activation.AddChild(new Label
                {
                    Text = item.State == OutlineState.Done ? $"  ✓ {item.Label}" : $"  {item.Label}",
                    ThemeTypeVariation = item.State switch { OutlineState.Done => Variation.Success, OutlineState.Current => Variation.Accent, _ => Variation.Dim },
                });
        }

        // Whom the game waits on, my team's move first; a watcher is told they watch.
        var waitingText = _s.IsWatching ? HeaderView.Watching : HeaderView.Waiting(p!, names, _s.MyTeamId);
        _waiting.Text = waitingText ?? "";
        _waiting.Visible = waitingText is not null;
    }

    static void Clear(Node box, params Node[] keep) { foreach (var c in box.GetChildren()) if (!keep.Contains(c)) { box.RemoveChild(c); c.QueueFree(); } }
}
