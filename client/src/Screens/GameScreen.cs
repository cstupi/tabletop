using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

public partial class GameScreen : Control
{
    Session _session = null!;
    public TableView Table { get; private set; } = null!;
    public TableInput Input { get; private set; } = null!;
    Label _status = null!, _toast = null!; VBoxContainer _side = null!; HBoxContainer _bottom = null!;
    Control? _lobby, _setup, _ask, _log; readonly List<TeamPanel> _teams = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action<Refused>? _onRefused; Action<SocketClosed, bool>? _onLost;

    public override void _Ready()
    {
        _session = Main.Instance.Session;
        var root = new HBoxContainer(); root.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(root);
        var viewportContainer = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var viewport = new SubViewport { HandleInputLocally = true }; viewportContainer.AddChild(viewport); root.AddChild(viewportContainer);
        Table = GD.Load<PackedScene>("res://scenes/TableView.tscn").Instantiate<TableView>(); viewport.AddChild(Table);
        Input = new TableInput(); Table.AddChild(Input);
        var overlay = new PresenceOverlay(); Table.AddChild(overlay);
        _side = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) }; root.AddChild(_side);
        var top = new HBoxContainer(); _side.AddChild(top);
        var home = new Button { Text = "Home" }; home.Pressed += () => Guarded.Run(_session.DisconnectAsync, Toast); top.AddChild(home);
        top.AddChild(_status = new Label());
        _side.AddChild(_toast = new Label { Modulate = Colors.Orange });
        _bottom = new HBoxContainer(); _side.AddChild(_bottom);
        if (_session.GameId is not null) { Table.Bind(_session); Input.Bind(Table, _session); overlay.Bind(Table, _session); }
        _onEvents = _ => RefreshTeams();
        _onRefused = r => Toast($"{r.Reason}: {r.Detail}");
        _onLost = (c, will) => Toast(c.Error is ProjectionDesyncException
            ? will ? "The table fell out of step with the server; rebuilding it from the log…" : $"This game's log cannot be replayed: {c.Reason}"
            : will ? $"Connection lost ({c.Reason}); reconnecting…" : $"Disconnected: {c.Reason}");
        _session.GameChanged += Rebuild; _session.PhaseChanged += Rebuild; _session.BoardChanged += RefreshTeams; _session.EventsApplied += _onEvents;
        _session.RefusedReceived += _onRefused; _session.ConnectionLost += _onLost;
        Rebuild();
    }

    public override void _ExitTree()
    {
        if (_session is null) return;
        _session.GameChanged -= Rebuild; _session.PhaseChanged -= Rebuild; _session.BoardChanged -= RefreshTeams; _session.EventsApplied -= _onEvents;
        _session.RefusedReceived -= _onRefused; _session.ConnectionLost -= _onLost;
    }

    public void Toast(string text)
    {
        if (!IsInstanceValid(_toast) || !IsInsideTree()) return;
        _toast.Text = text;
        GetTree().CreateTimer(4).Timeout += () => { if (IsInstanceValid(_toast) && _toast.Text == text) _toast.Text = ""; };
    }

    void Rebuild()
    {
        var game = _session.Game;
        _status.Text = game is null ? (_session.Phase == SessionPhase.Reconnecting ? "Reconnecting…" : "Connecting…") : $"{game.Name} — {game.Status.ToString().ToLowerInvariant()}" + (_session.Phase == SessionPhase.Reconnecting ? " (reconnecting…)" : "");
        foreach (var t in _teams) t.QueueFree(); _teams.Clear();
        _lobby?.QueueFree(); _setup?.QueueFree(); _lobby = _setup = null;
        if (game is null) return;
        foreach (var team in game.Teams) { var panel = new TeamPanel(team); _teams.Add(panel); _side.AddChild(panel); }
        RefreshTeams();
        if (game.Status is GameStatus.Lobby or GameStatus.Setup) _side.AddChild(_lobby = new LobbyPanel());
        if (game.Status == GameStatus.Setup) _side.AddChild(_setup = new SetupToolbar(this));
        ShowPlayPanels(game.Status is GameStatus.Playing or GameStatus.Ended);   // Tasks 14–15 fill this in
    }

    void RefreshTeams() { foreach (var t in _teams) if (IsInstanceValid(t)) t.Refresh(_session); }

    partial void ShowPlayPanels(bool playing);
}
