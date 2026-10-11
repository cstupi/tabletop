using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Centre: the result, its reason (the mission result or the strict breach's detail) and each team's score.</summary>
public partial class EndMessage : Control
{
    Session _s = null!; bool _opened;
    readonly PanelContainer _panel = new();
    readonly Label _result = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }, _reason = new();
    readonly VBoxContainer _scores = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged, _onTheme;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; centre.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(centre);
        centre.AddChild(_panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) }; _panel.AddChild(box);
        var top = new HBoxContainer(); box.AddChild(top);
        top.AddChild(_result);
        var close = new Button { Text = "✕", Flat = true, ThemeTypeVariation = Variation.FlatButton }; close.Pressed += Close; top.AddChild(close);
        box.AddChild(_reason); box.AddChild(_scores);
        _onEvents = _ => Render(); _onChanged = Render;
        session.EventsApplied += _onEvents; session.GameChanged += _onChanged;
        _onTheme = Render; Ui.Changed += _onTheme;
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.GameChanged -= _onChanged;
    }

    public void Open() { if (Fill()) Visible = true; }
    public void Close() => Visible = false;

    void Render()
    {
        if (_s.Projection?.Ended is null) { _opened = false; Visible = false; return; }
        if (!_opened) { _opened = true; Open(); }
        else if (Visible) Fill();
    }

    bool Fill()
    {
        var p = _s.Projection; var game = _s.Game;
        if (p?.Ended is not { } ended || game is null) return false;
        var names = new Names(game, p);
        var m = HeaderView.Of(p, game, names);
        _result.Text = m.Result ?? "Game over";
        _reason.Text = ended.Reason;
        foreach (var c in _scores.GetChildren()) { _scores.RemoveChild(c); c.QueueFree(); }
        foreach (var team in game.Teams.OrderBy(t => t.Ordinal))
        {
            var name = names.Team(team.Id);
            var score = new Label { Text = $"{name}  {m.Scores.FirstOrDefault(s => s.Team == name).Score}" };
            score.AddThemeColorOverride("font_color", Palette.Team(team.Ordinal));
            _scores.AddChild(score);
        }
        return true;
    }

    /// <summary>The backdrop takes the clicks that miss the panel: a click outside closes the message.</summary>
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } b && !_panel.GetGlobalRect().HasPoint(b.GlobalPosition)) { Close(); AcceptEvent(); }
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || KeyOverlay.IntentOf(e, GetViewport()) != Intent.Cancel) return;
        Close(); GetViewport().SetInputAsHandled();
    }
}
