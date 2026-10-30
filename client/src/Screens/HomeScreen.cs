using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

/// <summary>
/// The game list: filters, Join (or double-click / Enter on a row), Replay (a game the player is in, running or ended), More, Refresh,
/// Create game, the player's name with Rename, Settings and Sign out. The line under the list says what is loading, why the list is empty, why the last game closed, or what went wrong.
/// </summary>
public partial class HomeScreen : Control
{
    readonly List<GameSummary> _games = new();
    Guid? _next;
    bool _loaded, _busy;
    ItemList _list = null!; OptionButton _status = null!; LineEdit _search = null!; CheckBox _mine = null!, _joined = null!; Label _error = null!, _name = null!, _hint = null!;
    Button _join = null!, _replay = null!, _more = null!, _end = null!, _delete = null!;
    Action? _onAccount;

    public override void _Ready()
    {
        var session = Main.Instance.Session;
        var margin = new MarginContainer(); margin.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(margin);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" }) margin.AddThemeConstantOverride(side, Margin);
        var root = new VBoxContainer(); margin.AddChild(root);
        var top = new HBoxContainer(); root.AddChild(top);
        top.AddChild(_name = new Label());
        top.AddChild(Btn("Rename…", RenameDialog));
        top.AddChild(Btn("Create game", () => Run(CreateDialog)));
        top.AddChild(Btn("Settings", () => SettingsDialog.Open(this, session)));
        top.AddChild(Btn("Sign out", () => Run(session.LogoutAsync)));
        root.AddChild(_hint = new Label { ThemeTypeVariation = Variation.Accent, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _onAccount = ShowName; session.AccountChanged += _onAccount;
        ShowName();
        var filters = new HBoxContainer(); root.AddChild(filters);
        filters.AddChild(_status = new OptionButton());
        _status.AddItem("Any status"); foreach (var s in Enum.GetNames<GameStatus>()) _status.AddItem(s);
        _status.ItemSelected += _ => Render();
        filters.AddChild(_search = new LineEdit { PlaceholderText = "name", CustomMinimumSize = new Vector2(200, 0) }); _search.TextChanged += _ => Render();
        filters.AddChild(_mine = new CheckBox { Text = "Created by me" }); _mine.Toggled += _ => Render();
        // The server keeps only this account's games, so every page of them is reachable: the list reloads.
        filters.AddChild(_joined = new CheckBox { Text = "Games I'm in" }); _joined.Toggled += _ => Reload();
        root.AddChild(_list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill });
        _list.ItemActivated += i => JoinRow((int)i);                    // double-click or Enter
        _list.ItemSelected += _ => ShowButtons();
        var bottom = new HBoxContainer(); root.AddChild(bottom);
        bottom.AddChild(_join = Btn("Join", () => { if (_list.GetSelectedItems() is { Length: > 0 } sel) JoinRow(sel[0]); }));
        bottom.AddChild(_replay = Btn("Replay", ReplaySelected));
        bottom.AddChild(_end = Btn("End game", () => ConfirmOnSelected(false)));
        bottom.AddChild(_delete = Btn("Delete game", () => ConfirmOnSelected(true)));
        bottom.AddChild(_more = Btn("More", () => Run(LoadPage)));
        bottom.AddChild(Btn("Refresh", Reload));
        root.AddChild(_error = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        // Why the last game ended, when it ended without the player asking: a replaced socket, a rejection, or a
        // desync past its bound. Session clears it on the next connect. Without this the drop to Home is silent.
        if (session.LastCloseReason is { } why) _error.Text = $"Left the last game: {why}";
        Render();
        Run(LoadPage);
    }

    public override void _ExitTree() { if (_onAccount is not null) Main.Instance.Session.AccountChanged -= _onAccount; }

    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }

    /// <summary>The screen's margin to the window, the Hud's 8 px.</summary>
    const int Margin = (int)HudLayout.M;

    /// <summary>A dialog's width: its wrapping labels wrap at it, so the dialog's height follows its text and never the window's.</summary>
    const int DialogWidth = 420;

    /// <summary>A dialog field under its name.</summary>
    static Control Field(string label, Control field)
    {
        var box = new VBoxContainer(); box.AddChild(new Label { Text = label, ThemeTypeVariation = Variation.Dim }); box.AddChild(field); return box;
    }

    /// <summary>A label that wraps inside a dialog: a fixed width, since a wrapping label in an auto-sized window otherwise measures one word a line.</summary>
    static Label DialogText(string text = "", string variation = "") => new()
    {
        Text = text, ThemeTypeVariation = variation, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(DialogWidth - 40, 0),
    };

    /// <summary>The name everyone sees, and while it is still the default Player-xxxx, a line asking for a real one.</summary>
    void ShowName()
    {
        if (!IsInstanceValid(_name)) return;
        var name = Main.Instance.Session.Account?.Name ?? "";
        _name.Text = $"Signed in as {name}";
        _hint.Text = TextLimits.IsDefaultName(name) ? $"Other players see you as {name}." : "";
        _hint.Visible = _hint.Text.Length > 0;
    }

    async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { Show(Feedback.Error(e, Main.Instance.Session.Settings.ManagementUrl)); }
    }

    /// <summary>The line under the list: a problem in warning colour, a finished action in success colour, progress plain. The screen
    /// may have been freed by the navigation the action caused.</summary>
    void Show(string message, string variation = Variation.Warning)
    {
        if (!IsInstanceValid(this) || !IsInstanceValid(_error)) return;
        _error.Text = message; _error.ThemeTypeVariation = variation;
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
        var shown = Filtered();
        foreach (var g in shown) _list.AddItem($"{g.Name}  —  {g.Status.ToString().ToLowerInvariant()}, {g.Participants} in{(g.Joined ? " (you)" : "")}, {g.CreatedAt.LocalDateTime:g}{(g.RuleSet is { } set ? $" · {set}" : "")}");
        // The empty list says why: still loading, no games at all, or none passing the filters.
        // A placeholder row only on an empty list, so row indices still match Filtered().
        if (shown.Count == 0)
        {
            var row = _list.AddItem(!_loaded ? "Loading games…" : _games.Count == 0 ? "No games yet. Create one with Create game." : "No game matches these filters.", null, selectable: false);
            _list.SetItemDisabled(row, true);
        }
        ShowButtons();
    }

    void ShowButtons()
    {
        _join.Disabled = _busy || _list.GetSelectedItems().Length == 0;
        _join.TooltipText = _busy ? "Joining…" : _join.Disabled ? "Select a game first" : "Join the selected game (or double-click it)";
        var actions = GameRowActions.Of(Selected(), Main.Instance.Session.Account?.Id);
        _end.Disabled = _busy || !actions.CanEnd; _delete.Disabled = _busy || !actions.CanDelete; _replay.Disabled = _busy || !actions.CanReplay;
        _replay.TooltipText = actions.CanReplay ? "Replay the selected game from its start" : "Replay a game you are in once it is playing or ended";
        _end.TooltipText = actions.CanEnd ? "End the selected game for everyone" : "Only the game's creator ends a game that has not ended";
        _delete.TooltipText = actions.CanDelete ? "Delete the selected game and its log" : "Only the game's creator deletes it";
        _more.Disabled = _busy || _loaded && _next is null;
        _more.TooltipText = _loaded && _next is null ? "Every game is listed" : "Load the next 50 games";
    }

    GameSummary? Selected() => _list.GetSelectedItems() is { Length: > 0 } sel && sel[0] < Filtered().Count ? Filtered()[sel[0]] : null;

    /// <summary>End or Delete on the selected game, the creator's only, asked first; the list reloads after.</summary>
    void ConfirmOnSelected(bool delete)
    {
        if (Selected() is not { } game) return;
        var d = new ConfirmationDialog
        {
            Title = delete ? "Delete this game?" : "End this game?", OkButtonText = delete ? "Delete" : "End game", DialogAutowrap = true,
            DialogText = delete ? $"{game.Name} and its whole log are deleted for every player. This cannot be undone." : $"{game.Name} ends for every player.",
        };
        d.Confirmed += () => Run(async () =>
        {
            var m = Main.Instance.Session.Management;
            if (delete) await m.DeleteGame(game.Id); else await m.EndGame(game.Id);
            if (!IsInstanceValid(this)) return;
            Show(delete ? $"Deleted {game.Name}." : $"Ended {game.Name}.", Variation.Success);
            _games.Clear(); _next = null; _loaded = false; Render(); await LoadPage();
        });
        d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        AddChild(d); d.PopupCentered(new Vector2I(DialogWidth, 0));
    }

    void Reload() { _games.Clear(); _next = null; _loaded = false; Render(); Run(LoadPage); }

    async Task LoadPage()
    {
        var page = await Main.Instance.Session.Management.ListGames(_next, 50, _joined.ButtonPressed);
        if (!IsInstanceValid(this)) return;
        _games.AddRange(page.Items.Where(g => _games.All(x => x.Id != g.Id))); _next = page.Next; _loaded = true;
        Render();
    }

    void JoinRow(int row)
    {
        if (_busy || row < 0 || row >= Filtered().Count) return;
        var game = Filtered()[row];
        _busy = true; ShowButtons(); Show($"Joining {game.Name}…", "");
        Run(async () =>
        {
            try { await Join(game.Id); }
            finally { if (IsInstanceValid(this)) { _busy = false; ShowButtons(); } }
        });
    }

    void ReplaySelected()
    {
        if (_busy || Selected() is not { } game || !GameRowActions.Of(game, Main.Instance.Session.Account?.Id).CanReplay) return;
        _busy = true; ShowButtons(); Show($"Opening the replay of {game.Name}…", "");
        Run(async () =>
        {
            try { await Main.Instance.Session.ReplayAsync(game.Id); }
            finally { if (IsInstanceValid(this)) { _busy = false; ShowButtons(); } }
        });
    }

    static async Task Join(Guid gameId)
    {
        await Main.Instance.Session.Management.JoinGame(gameId);
        await Main.Instance.Session.ConnectAsync(gameId);
    }

    /// <summary>A name field that says what is wrong with it as the player types, and an OK button that waits until nothing is.</summary>
    static LineEdit NameField(ConfirmationDialog dialog, Label problem, string text, string field)
    {
        var line = new LineEdit { Text = text, MaxLength = TextLimits.Name, PlaceholderText = $"at most {TextLimits.Name} characters", SelectAllOnFocus = true };
        void Check(string value)
        {
            var why = TextLimits.NameProblem(value, field);
            problem.Text = why ?? "";
            dialog.GetOkButton().Disabled = why is not null;
        }
        line.TextChanged += Check;
        dialog.RegisterTextEnter(line);
        Check(text);
        return line;
    }

    void RenameDialog()
    {
        var session = Main.Instance.Session;
        var dialog = new ConfirmationDialog { Title = "Your name", OkButtonText = "Rename" };
        var form = new VBoxContainer(); dialog.AddChild(form);
        form.AddChild(DialogText("The name other players see in games and lobbies."));
        var problem = DialogText(variation: Variation.Warning);
        var name = NameField(dialog, problem, session.Account?.Name ?? "", "A name");
        form.AddChild(name); form.AddChild(problem);
        dialog.Confirmed += () => Run(async () => { await session.RenameAsync(name.Text); Show($"Renamed to {session.Account?.Name}.", Variation.Success); });
        dialog.VisibilityChanged += () => { if (!dialog.Visible) dialog.QueueFree(); };
        AddChild(dialog); dialog.PopupCentered(new Vector2I(DialogWidth, 0)); name.CallDeferred(Control.MethodName.GrabFocus);
    }

    async Task CreateDialog()
    {
        var session = Main.Instance.Session;
        Show("Loading rule sets and armies…", "");
        var packs = await session.Management.ListProperties();
        var armies = (await session.Management.ListArmies(null)).Items;
        if (!IsInstanceValid(this)) return;
        if (packs.Count == 0) { Show("The server has no rule set loaded (GET /v1/properties is empty), so no game can be created."); return; }
        Show("");
        var dialog = new ConfirmationDialog { Title = "Create game", OkButtonText = "Create" };
        var form = new VBoxContainer(); dialog.AddChild(form);
        var problem = DialogText(variation: Variation.Warning);
        var name = NameField(dialog, problem, "", "A game name"); name.PlaceholderText = "game name"; form.AddChild(Field("Game name", name)); form.AddChild(problem);
        var pack = new OptionButton(); foreach (var p in packs) pack.AddItem(CreateDefaults.PackLabel(p)); pack.Select(CreateDefaults.DefaultIndex(packs)); form.AddChild(Field("Rule set", pack));
        var shape = new OptionButton(); shape.AddItem("Alternating activation"); shape.AddItem("Player turn"); form.AddChild(Field("Activation", shape));
        var enforcement = new OptionButton(); foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e); enforcement.Select(1); form.AddChild(Field("Enforcement", enforcement));
        var army = new OptionButton(); army.AddItem("No army (pick one in the lobby)"); foreach (var a in armies) army.AddItem($"{LobbyView.ArmyLabel(a)}, {a.Units} units"); form.AddChild(Field("Army", army));
        if (armies.Count == 0) form.AddChild(DialogText("You have no armies yet: import one in the web app.", Variation.Dim));
        dialog.Confirmed += () => Run(async () =>
        {
            Show("Creating the game…", "");
            // WaitsBetweenSteps is a development flag (spec: in-game UX → Flow between steps); players never get the "Continue" stops.
            var game = await session.Management.CreateGame(new CreateGameRequest(name.Text.Trim(), packs[pack.Selected].ContentHash,
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
                        Run(() => session.SendEditAsync(LobbyEdits.ArmyFromCreate(id)));
                    }
                }
                session.PhaseChanged += PickWhenConnected;
            }
            dialog.QueueFree();             // before the connect: it leaves this screen, and the dialog goes with it
            await session.ConnectAsync(game.Id);
        });
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered(new Vector2I(DialogWidth, 0)); name.CallDeferred(Control.MethodName.GrabFocus);
    }
}
