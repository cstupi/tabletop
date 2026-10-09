using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

public partial class HomeScreen : Control
{
    readonly List<GameSummary> _games = new();
    Guid? _next;
    ItemList _list = null!; OptionButton _status = null!; LineEdit _search = null!; CheckBox _mine = null!; Label _error = null!;

    public override void _Ready()
    {
        var root = new VBoxContainer(); root.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(root);
        var top = new HBoxContainer(); root.AddChild(top);
        top.AddChild(new Label { Text = $"Signed in as {Main.Instance.Session.Account?.Name}" });
        top.AddChild(Btn("Create game", () => Run(CreateDialog)));
        top.AddChild(Btn("Sign out", () => Run(Main.Instance.Session.LogoutAsync)));
        var filters = new HBoxContainer(); root.AddChild(filters);
        filters.AddChild(_status = new OptionButton());
        _status.AddItem("Any status"); foreach (var s in Enum.GetNames<GameStatus>()) _status.AddItem(s);
        _status.ItemSelected += _ => Render();
        filters.AddChild(_search = new LineEdit { PlaceholderText = "name", CustomMinimumSize = new Vector2(200, 0) }); _search.TextChanged += _ => Render();
        filters.AddChild(_mine = new CheckBox { Text = "Created by me" }); _mine.Toggled += _ => Render();
        root.AddChild(_list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill });
        _list.ItemActivated += i => Run(() => Join(Filtered()[(int)i].Id));
        var bottom = new HBoxContainer(); root.AddChild(bottom);
        bottom.AddChild(Btn("Join", () => { if (_list.GetSelectedItems() is { Length: > 0 } sel) Run(() => Join(Filtered()[sel[0]].Id)); }));
        bottom.AddChild(Btn("More", () => Run(LoadPage)));
        bottom.AddChild(Btn("Refresh", () => { _games.Clear(); _next = null; Render(); Run(LoadPage); }));
        root.AddChild(_error = new Label { Modulate = Colors.Orange });
        // Why the last game ended, when it ended without the player asking: a replaced socket, a rejection, or a
        // desync past its bound. Session clears it on the next connect. Without this the drop to Home is silent.
        if (Main.Instance.Session.LastCloseReason is { } why) _error.Text = $"Dropped from the last game: {why}";
        Run(LoadPage);
    }

    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }

    async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (ManagementException e) { Show(e.Message); }
        catch (HttpRequestException e) { Show($"Cannot reach {Main.Instance.Session.Settings.ManagementUrl}: {e.Message}"); }
        catch (Exception e) { Show(e.Message); }
    }

    /// <summary>The screen may have been freed by the navigation the action caused.</summary>
    void Show(string message)
    {
        if (IsInstanceValid(this) && IsInstanceValid(_error)) _error.Text = message;
    }

    List<GameSummary> Filtered()
    {
        var me = Main.Instance.Session.Account?.Id;
        return _games.Where(g => (_status.Selected == 0 || g.Status.ToString() == _status.GetItemText(_status.Selected))
                              && (_search.Text.Length == 0 || g.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase))
                              && (!_mine.ButtonPressed || g.CreatedBy == me)).ToList();
    }

    void Render()
    {
        _list.Clear();
        foreach (var g in Filtered()) _list.AddItem($"{g.Name}  —  {g.Status.ToString().ToLowerInvariant()}, {g.Participants} in, {g.CreatedAt.LocalDateTime:g}");
    }

    async Task LoadPage()
    {
        var page = await Main.Instance.Session.Management.ListGames(_next, 50);
        _games.AddRange(page.Items.Where(g => _games.All(x => x.Id != g.Id))); _next = page.Next;
        Render();
    }

    async Task Join(Guid gameId)
    {
        await Main.Instance.Session.Management.JoinGame(gameId);
        await Main.Instance.Session.ConnectAsync(gameId);
    }

    async Task CreateDialog()
    {
        var session = Main.Instance.Session;
        var packs = await session.Management.ListProperties();
        var armies = (await session.Management.ListArmies(null)).Items;
        var dialog = new ConfirmationDialog { Title = "Create game", OkButtonText = "Create" };
        var form = new VBoxContainer(); dialog.AddChild(form);
        var name = new LineEdit { PlaceholderText = "game name" }; form.AddChild(name);
        var pack = new OptionButton(); foreach (var p in packs) pack.AddItem($"{p.Name} ({p.GameRules})"); form.AddChild(pack);
        var shape = new OptionButton(); shape.AddItem("Alternating activation"); shape.AddItem("Player turn"); form.AddChild(shape);
        var enforcement = new OptionButton(); foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e); enforcement.Select(1); form.AddChild(enforcement);
        var army = new OptionButton(); army.AddItem("No army (watch)"); foreach (var a in armies) army.AddItem($"{a.Name} ({a.Units} units)"); form.AddChild(army);
        dialog.Confirmed += () => Run(async () =>
        {
            if (packs.Count == 0) { _error.Text = "No Properties set is loaded on the server; GET /v1/properties is empty."; return; }
            // WaitsBetweenSteps is a development flag (spec: in-game UX → Flow between steps); players never get the "Continue" stops.
            var game = await session.Management.CreateGame(new CreateGameRequest(name.Text, packs[pack.Selected].ContentHash,
                shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, Enum.Parse<Enforcement>(enforcement.GetItemText(enforcement.Selected)), WaitsBetweenSteps: false));
            Guid? armyId = army.Selected == 0 ? null : armies[army.Selected - 1].Id;
            if (armyId is { } id)
            {
                var gameId = game.Id;
                void PickWhenConnected()
                {
                    if (session.GameId != gameId) { session.PhaseChanged -= PickWhenConnected; return; }
                    if (session.Phase == SessionPhase.Connected)
                    {
                        session.PhaseChanged -= PickWhenConnected;
                        Run(() => session.SendEditAsync(new PickArmy(id)));
                    }
                }
                session.PhaseChanged += PickWhenConnected;
            }
            dialog.QueueFree();             // before the connect: it leaves this screen, and the dialog goes with it
            await session.ConnectAsync(game.Id);
        });
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered(new Vector2I(420, 320));
    }
}
