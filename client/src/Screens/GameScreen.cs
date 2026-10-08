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
    Label _status = null!, _toast = null!, _selection = null!; readonly HashSet<Guid> _lit = new(); VBoxContainer _side = null!; HBoxContainer _bottom = null!;
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
        _side.AddChild(_selection = new Label());
        _bottom = new HBoxContainer(); _side.AddChild(_bottom);
        if (_session.GameId is not null) { Table.Bind(_session); Input.Bind(Table, _session); overlay.Bind(Table, _session); }
        Input.SelectionChanged += ShowSelection;
        _onEvents = _ => RefreshAll();
        _onRefused = r => Toast($"{r.Reason}: {r.Detail}");
        _onLost = (c, will) => Toast(c.Error is ProjectionDesyncException
            ? will ? "The table fell out of step with the server; rebuilding it from the log…" : $"This game's log cannot be replayed: {c.Reason}"
            : will ? $"Connection lost ({c.Reason}); reconnecting…" : $"Disconnected: {c.Reason}");
        _session.GameChanged += Rebuild; _session.PhaseChanged += Rebuild; _session.BoardChanged += RefreshAll; _session.EventsApplied += _onEvents;
        _session.RefusedReceived += _onRefused; _session.ConnectionLost += _onLost;
        Rebuild();
    }

    public override void _ExitTree()
    {
        if (_session is null) return;
        _session.GameChanged -= Rebuild; _session.PhaseChanged -= Rebuild; _session.BoardChanged -= RefreshAll; _session.EventsApplied -= _onEvents;
        _session.RefusedReceived -= _onRefused; _session.ConnectionLost -= _onLost;
        if (Input is not null && IsInstanceValid(Input)) Input.SelectionChanged -= ShowSelection;
        FreePlayPanels();
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

    /// <summary>Teams now; the selection once every other handler of this change has run, since the Ask panel clears highlights when it renders.</summary>
    void RefreshAll() { RefreshTeams(); Callable.From(ShowSelection).CallDeferred(); }

    /// <summary>The selected unit's models lit and its name and model count in the status area. The Ask's own highlights are put back where the selection leaves them.</summary>
    void ShowSelection()
    {
        if (!IsInstanceValid(_selection)) return;
        var p = _session.Projection; var game = _session.Game;
        var ask = p is not null && game is not null ? AskViewModel.Mine(p, game, _session.MyTeamId, _session.Settings.MutedWindowKinds)?.Highlights : null;
        foreach (var id in _lit) if (Table.PieceAt(id) is { } old && IsInstanceValid(old)) old.SetHighlight(ask?.Contains(id) == true ? Palette.Selected : null);
        _lit.Clear();
        _selection.Text = p is null || game is null ? "" : SelectionView.Describe(new Names(game, p), p, Input.Selected) ?? "";
        if (p is null) return;
        foreach (var id in SelectionView.Models(p, Input.Selected)) if (Table.PieceAt(id) is { } n && IsInstanceValid(n)) { n.SetHighlight(Palette.Picked); _lit.Add(id); }
    }

    void RefreshTeams() { foreach (var t in _teams) if (IsInstanceValid(t)) t.Refresh(_session); }

    partial void ShowPlayPanels(bool playing);
}
