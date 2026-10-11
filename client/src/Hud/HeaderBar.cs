using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Top centre, three lines: round, turn and scores; the active unit, its action and the step strip; the waiting
/// line. After the end the first line carries "Game over", which reopens the end message.</summary>
public partial class HeaderBar : PanelContainer
{
    static readonly Color Dim = new(1, 1, 1, 0.45f), Lit = new(1f, 0.85f, 0.2f), Ticked = new(0.6f, 0.85f, 0.6f);
    Session _s = null!;
    readonly HBoxContainer _top = new(), _activation = new();
    readonly Label _waiting = new() { Modulate = new Color(1f, 0.8f, 0.4f) };
    readonly Button _gameOver = new() { Flat = true, Visible = false };
    readonly Button _settings = new() { Flat = true, TooltipText = "Settings", FocusMode = FocusModeEnum.None };
    // Session outlives the header: the subscriptions are fields and come off in _ExitTree.
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged;

    public event Action<Guid, Vector2>? TeamMenuRequested;
    public event Action? GameOverPressed;
    public event Action? SettingsRequested;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.Minsize, 8);
        GrowHorizontal = GrowDirection.Both;
        var lines = new VBoxContainer(); AddChild(lines);
        lines.AddChild(_top); lines.AddChild(_activation); lines.AddChild(_waiting);
        _top.AddChild(_gameOver); _top.AddChild(_settings);
        _settings.Pressed += () => SettingsRequested?.Invoke();
        _gameOver.Pressed += () => GameOverPressed?.Invoke();
        _onEvents = _ => Render(); _onChanged = Render;
        session.EventsApplied += _onEvents; session.GameChanged += _onChanged; session.BoardChanged += _onChanged;
        Render();
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.GameChanged -= _onChanged; _s.BoardChanged -= _onChanged;
    }

    void Render()
    {
        var p = _s.Projection; var game = _s.Game;
        Visible = p is not null && game is not null && game.Status is GameStatus.Playing or GameStatus.Ended;
        if (!Visible) return;
        var names = new Names(game!, p);
        var m = HeaderView.Of(p!, game!, names);
        Clear(_top, _gameOver, _settings); Clear(_activation);

        _top.AddChild(new Label { Text = $"Round {m.Round} · {m.Turn}" });
        foreach (var team in game!.Teams.OrderBy(t => t.Ordinal))
        {
            var name = names.Team(team.Id);
            var score = m.Scores.FirstOrDefault(s => s.Team == name).Score;
            var label = new Label { Text = $"  {name} {score}", Modulate = Palette.Team(team.Ordinal), MouseFilter = MouseFilterEnum.Stop, TooltipText = "Right-click: team menu" };
            var id = team.Id;
            label.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) TeamMenuRequested?.Invoke(id, b.GlobalPosition); };
            _top.AddChild(label);
        }
        _gameOver.Visible = m.Result is not null;
        _gameOver.Text = m.Result is { } result ? $"  {result}" : "";
        _top.MoveChild(_gameOver, -1); _top.MoveChild(_settings, -1);
        _settings.Icon = Ui.Icon("settings");

        _activation.Visible = m.ActiveUnit is not null;
        if (m.ActiveUnit is { } unit)
        {
            _activation.AddChild(new Label { Text = m.Action is { } action ? $"{unit} · {action}" : unit });
            foreach (var item in m.Outline)
                _activation.AddChild(new Label
                {
                    Text = item.State == OutlineState.Done ? $"  ✓ {item.Label}" : $"  {item.Label}",
                    Modulate = item.State switch { OutlineState.Done => Ticked, OutlineState.Current => Lit, _ => Dim },
                });
        }

        _waiting.Text = m.Waiting ?? "";
        _waiting.Visible = m.Waiting is not null;
    }

    static void Clear(Node box, params Node[] keep) { foreach (var c in box.GetChildren()) if (!keep.Contains(c)) { box.RemoveChild(c); c.QueueFree(); } }
}
