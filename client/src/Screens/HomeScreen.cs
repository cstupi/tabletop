using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

/// <summary>
/// Home (UX redesign part 1 → Home, HomeView): the header with the gear and the account menu; New game, the name filter, refresh and Ended too;
/// my games as cards banded in their status colour, each with its state line, its primary action and a ▸ menu; the open games as rows with
/// Join; my older ended games as rows with Replay after Your games, folded until Ended too. The line at the foot says what is loading, why the last game closed, or
/// what went wrong.
/// </summary>
public partial class HomeScreen : Control
{
    readonly Dictionary<Guid, GameSummary> _games = new();
    Guid? _next;
    bool _loaded, _busy, _endedToo;
    LineEdit _search = null!;
    VBoxContainer _body = null!;
    Label _status = null!, _hint = null!; PanelContainer _statusBox = null!;
    MenuButton _account = null!;
    Action? _onAccount, _onTheme;

    const int CardWidth = 280;

    public override void _Ready()
    {
        var session = Main.Instance.Session;
        _onAccount = ShowName; session.AccountChanged += _onAccount;
        _onTheme = Rebuild; Ui.Changed += _onTheme;
        Rebuild();
        // Why the last game ended, when it ended without the player asking: a replaced socket, a rejection, or a
        // desync past its bound. Session clears it on the next connect. Without this the drop to Home is silent.
        if (session.LastCloseReason is { } why) Show($"Left the last game: {why}");
        Run(LoadAll);
    }

    public override void _ExitTree()
    {
        if (_onAccount is not null) Main.Instance.Session.AccountChanged -= _onAccount;
        if (_onTheme is not null) Ui.Changed -= _onTheme;
    }

    /// <summary>The list refreshes when the window comes back to the front.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn && _loaded && !_busy) Reload();
    }

    void Rebuild()
    {
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        AddChild(new Backdrop());
        var margin = new MarginContainer(); margin.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(margin);
        foreach (var side in new[] { "margin_left", "margin_right" }) margin.AddThemeConstantOverride(side, Space.XL);
        foreach (var side in new[] { "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(side, Space.XL);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", Space.L); margin.AddChild(root);
        root.AddChild(Header());
        root.AddChild(_hint = new Label { ThemeTypeVariation = Variation.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        root.AddChild(Toolbar());
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        // The lists in one framed panel down to the page's margin: rows scroll inside the frame and are cut at its inner edge.
        var frame = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", ThemeBuilder.Nine(Ui.Current.Materials.Panel, Space.M, Space.M));
        root.AddChild(frame); frame.AddChild(scroll);
        scroll.AddChild(_body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _body.AddThemeConstantOverride("separation", Space.S);
        // The status line floats over the list's foot, shown only while it has words: the list runs down to the page's margin.
        _statusBox = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _statusBox.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.Minsize, Space.XL); _statusBox.GrowHorizontal = GrowDirection.Both; _statusBox.GrowVertical = GrowDirection.Begin;
        AddChild(_statusBox);
        _statusBox.AddChild(_status = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.Off });
        ShowName();
        Render();
    }

    Control Header()
    {
        var session = Main.Instance.Session;
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Space.M);
        row.AddChild(Icon("objective", Ui.Accent, 30));
        row.AddChild(new Label { Text = "TABLETOP", ThemeTypeVariation = Variation.Display, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center });
        var gear = IconButton("settings", "Options", () => OptionsScreen.Open(this));
        row.AddChild(gear);
        _account = new MenuButton { Flat = true, ThemeTypeVariation = Variation.FlatButton, Icon = Ui.Icon("player"), ExpandIcon = false, TooltipText = "Your account" };
        _account.AddThemeConstantOverride("icon_max_width", 22); HomeLook.IconInk(_account);
        var menu = _account.GetPopup();
        menu.AddIconItem(Ui.Icon("editing"), "Rename…", 0); menu.AddIconItem(Ui.Icon("sign-out"), "Sign out", 1);
        menu.SetItemIconMaxWidth(0, 18); menu.SetItemIconMaxWidth(1, 18);
        menu.IdPressed += id => { if (id == 0) RenameDialog(); else Run(session.LogoutAsync); };
        row.AddChild(_account);
        return row;
    }

    Control Toolbar()
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Space.S);
        var create = new Button { Text = "New game", Icon = Ui.Icon("add"), ExpandIcon = false };
        create.AddThemeConstantOverride("icon_max_width", 18);
        HomeLook.Primary(create);
        create.Pressed += () => Run(CreateDialog);
        row.AddChild(create);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(Icon("search", Ui.DimText, 20));
        row.AddChild(_search = new LineEdit { PlaceholderText = "name", CustomMinimumSize = new Vector2(220, 0), ClearButtonEnabled = true });
        _search.TextChanged += _ => Render();
        row.AddChild(IconButton("refresh", "Refresh the list", Reload));
        var ended = new CheckBox { Text = "Ended too", ButtonPressed = _endedToo, TooltipText = "List your ended games" };
        ended.Toggled += on => { _endedToo = on; Render(); };
        row.AddChild(ended);
        return row;
    }

    static TextureRect Icon(string name, Color tint, int size) => new()
    {
        Texture = Ui.Icon(name), Modulate = tint, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore,
    };

    static Button IconButton(string icon, string tip, Action a)
    {
        var b = new Button { Icon = Ui.Icon(icon), TooltipText = tip, Flat = true, ThemeTypeVariation = Variation.IconOnly, ExpandIcon = false };
        b.AddThemeConstantOverride("icon_max_width", 24); HomeLook.IconInk(b);
        b.Pressed += a;
        return b;
    }

    /// <summary>A dialog's width: its wrapping labels wrap at it, so the dialog's height follows its text and never the window's.</summary>
    const int DialogWidth = 440;

    /// <summary>A dialog field under its name.</summary>
    static Control Field(string label, Control field)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Space.XS);
        box.AddChild(new Label { Text = label, ThemeTypeVariation = Variation.Dim }); box.AddChild(field); return box;
    }

    /// <summary>A label that wraps inside a dialog: a fixed width, since a wrapping label in an auto-sized window otherwise measures one word a line.</summary>
    static Label DialogText(string text = "", string variation = "") => new()
    {
        Text = text, ThemeTypeVariation = variation, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(DialogWidth - 2 * Space.L, 0),
    };

    /// <summary>The account menu's name, and while it is still the default Player-xxxx, the line naming it.</summary>
    void ShowName()
    {
        if (!IsInstanceValid(_account)) return;
        var name = Main.Instance.Session.Account?.Name ?? "";
        _account.Text = name;
        _hint.Text = TextLimits.IsDefaultName(name) ? $"Other players see you as {name}." : "";
        _hint.Visible = _hint.Text.Length > 0;
    }

    async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { Show(Feedback.Error(e, Main.Instance.Session.Settings.ManagementUrl)); }
    }

    /// <summary>The line at the foot: a problem in warning colour, a finished action in success colour, progress plain. The screen
    /// may have been freed by the navigation the action caused.</summary>
    void Show(string message, string variation = Variation.Warning)
    {
        if (!IsInstanceValid(this) || !IsInstanceValid(_status)) return;
        _status.Text = message; _status.ThemeTypeVariation = variation; _statusBox.Visible = message.Length > 0;
    }

    void Render()
    {
        if (!IsInstanceValid(_body)) return;
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        var me = Main.Instance.Session.Account?.Id;
        var lists = HomeView.Of(_games.Values, me, _search.Text.Trim(), _endedToo, DateTimeOffset.Now);

        _body.AddChild(Caption("YOUR GAMES"));
        if (lists.Yours.Count == 0)
            _body.AddChild(Quiet(!_loaded ? "Loading games…" : _search.Text.Length > 0 ? "No game of yours matches" : lists.HasAny ? "None in play" : "No games yet"));
        else
        {
            var cards = new HFlowContainer(); cards.AddThemeConstantOverride("h_separation", Space.L); cards.AddThemeConstantOverride("v_separation", Space.L);
            foreach (var card in lists.Yours) cards.AddChild(Card(card));
            _body.AddChild(cards);
        }

        if (_endedToo)
        {
            _body.AddChild(Spacer());
            _body.AddChild(Caption("ENDED"));
            if (lists.Ended.Count == 0) _body.AddChild(Quiet("None"));
            else foreach (var row in lists.Ended) _body.AddChild(Row(row));
        }

        _body.AddChild(Spacer());
        _body.AddChild(Caption("OPEN GAMES"));
        if (lists.Open.Count == 0) _body.AddChild(Quiet(_loaded ? "None" : "Loading games…"));
        else foreach (var row in lists.Open) _body.AddChild(Row(row));
        if (_next is not null)
        {
            var more = new Button { Text = "More games", Flat = true, ThemeTypeVariation = Variation.FlatButton, SizeFlagsHorizontal = SizeFlags.ShrinkBegin, Disabled = _busy };
            more.Pressed += () => Run(LoadPage);
            _body.AddChild(more);
        }
    }

    static Label Caption(string text) => new() { Text = text, ThemeTypeVariation = Variation.Accent };
    static Control Spacer() => new() { CustomMinimumSize = new Vector2(0, Space.M) };

    static Control Quiet(string text)
    {
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", Space.S);
        line.AddChild(new Label { Text = text, ThemeTypeVariation = Variation.Dim });
        return line;
    }

    /// <summary>The status mark and word, then the edition, in the status colour.</summary>
    static Control StatusLine(GameSummary g, string edition)
    {
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", Space.S);
        line.AddChild(new HomeLook.StatusMark(g.Status));
        var words = new Label { Text = g.Status.ToString().ToUpperInvariant() + (edition.Length > 0 ? $"  ·  {edition}" : "") };
        words.AddThemeColorOverride("font_color", HomeLook.Status(g.Status));
        line.AddChild(words);
        return line;
    }

    /// <summary>The players, three by name, then "+N"; the names clip at the line's end.</summary>
    static Control Players(HomeCard c)
    {
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", Space.XS);
        line.AddChild(Icon("player", Ui.DimText, 16));
        line.AddChild(new Label { Text = c.Players.Count == 0 ? "No players" : string.Join("   ", c.Players), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = string.Join(", ", c.Game.ParticipantNames ?? []), MouseFilter = MouseFilterEnum.Pass });
        line.AddChild(new Label { Text = c.MorePlayers > 0 ? $"+{c.MorePlayers}" : "", ThemeTypeVariation = Variation.Dim, CustomMinimumSize = new Vector2(28, 0) });
        return line;
    }

    Control Card(HomeCard c)
    {
        var g = c.Game;
        var card = new PanelContainer { CustomMinimumSize = new Vector2(CardWidth, 0) };
        card.AddThemeStyleboxOverride("panel", HomeLook.Card(g.Status));
        var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", Space.S); card.AddChild(col);
        col.AddChild(StatusLine(g, c.Edition));
        col.AddChild(new Label { Text = g.Name, ThemeTypeVariation = Variation.Display, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = g.Name, MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(CardWidth - 40, 0) });
        col.AddChild(Players(c));
        col.AddChild(new Label { Text = c.State, ThemeTypeVariation = c.StateWarns ? Variation.Warning : Variation.Dim });
        var foot = new HBoxContainer(); foot.AddThemeConstantOverride("separation", Space.S); col.AddChild(foot);
        foot.AddChild(PrimaryButton(c));
        foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        if (c.Menu.Count > 0) foot.AddChild(MenuOf(c));
        return card;
    }

    Control Row(HomeCard c)
    {
        var g = c.Game;
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", HomeLook.Row(g.Status));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Space.M); panel.AddChild(row);
        row.AddChild(new HomeLook.StatusMark(g.Status));
        row.AddChild(new Label { Text = g.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = g.Name, MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(120, 0) });
        row.AddChild(new Label { Text = c.Edition, ThemeTypeVariation = Variation.Dim, CustomMinimumSize = new Vector2(60, 0) });
        var players = Players(c); players.SizeFlagsHorizontal = SizeFlags.ExpandFill; players.SizeFlagsStretchRatio = 2;
        row.AddChild(players);
        row.AddChild(new Label { Text = g.Status == GameStatus.Ended && c.State != HomeView.Ago(g.CreatedAt, DateTimeOffset.Now) ? c.State : HomeView.Ago(g.CreatedAt, DateTimeOffset.Now),
            ThemeTypeVariation = Variation.Dim, CustomMinimumSize = new Vector2(110, 0), HorizontalAlignment = HorizontalAlignment.Right });
        row.AddChild(PrimaryButton(c, filled: false));
        if (c.Menu.Count > 0) row.AddChild(MenuOf(c)); else row.AddChild(new Control { CustomMinimumSize = new Vector2(32, 0) });
        return panel;
    }

    Button PrimaryButton(HomeCard c, bool filled = true)
    {
        var icon = c.Primary switch { HomeAction.Replay => "replay", HomeAction.Join => "add", _ => "play" };
        var b = new Button { Text = HomeView.Label(c.Primary), Icon = Ui.Icon(icon), ExpandIcon = false, Disabled = _busy, CustomMinimumSize = new Vector2(110, 0) };
        b.AddThemeConstantOverride("icon_max_width", 16);
        if (filled) HomeLook.Primary(b);
        b.Pressed += () => Act(c.Game, c.Primary);
        return b;
    }

    MenuButton MenuOf(HomeCard c)
    {
        var m = new MenuButton { Icon = Ui.Icon("menu"), Flat = true, ThemeTypeVariation = Variation.FlatButton, ExpandIcon = false, TooltipText = "More", Disabled = _busy };
        m.AddThemeConstantOverride("icon_max_width", 18); HomeLook.IconInk(m);
        var popup = m.GetPopup();
        foreach (var a in c.Menu) popup.AddItem(HomeView.Label(a), (int)a);
        popup.IdPressed += id => Act(c.Game, (HomeAction)(int)id);
        return m;
    }

    void Act(GameSummary game, HomeAction action)
    {
        switch (action)
        {
            case HomeAction.Replay: Busy($"Opening the replay of {game.Name}…", () => Main.Instance.Session.ReplayAsync(game.Id)); break;
            case HomeAction.End: Confirm(game, "End this game?", "End game", $"{game.Name} ends for every player.", m => m.EndGame(game.Id), $"Ended {game.Name}."); break;
            case HomeAction.Delete:
                Confirm(game, "Delete this game?", "Delete", $"{game.Name} and its whole log are deleted for every player. This cannot be undone.", m => m.DeleteGame(game.Id), $"Deleted {game.Name}.");
                break;
            case HomeAction.Leave:
                Confirm(game, "Leave this game?", "Leave", $"Leave {game.Name}? Your seat and the army you brought go.", m => m.LeaveGame(game.Id), $"Left {game.Name}.");
                break;
            default: Busy($"Opening {game.Name}…", () => Join(game.Id)); break;
        }
    }

    /// <summary>One navigation at a time: the buttons are disabled until it returns (or the screen goes with it).</summary>
    void Busy(string doing, Func<Task> work)
    {
        if (_busy) return;
        _busy = true; Render(); Show(doing, "");
        Run(async () =>
        {
            try { await work(); }
            finally { if (IsInstanceValid(this)) { _busy = false; Render(); } }
        });
    }

    /// <summary>End, Delete or Leave, asked first; the list reloads after.</summary>
    void Confirm(GameSummary game, string title, string ok, string text, Func<ManagementClient, Task> work, string done)
    {
        var d = new ConfirmationDialog { Title = title, OkButtonText = ok, DialogAutowrap = true, DialogText = text };
        d.Confirmed += () => Run(async () =>
        {
            await work(Main.Instance.Session.Management);
            if (!IsInstanceValid(this)) return;
            Show(done, Variation.Success);
            Reload();
        });
        d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        AddChild(d); d.PopupCentered(new Vector2I(DialogWidth, 0));
    }

    void Reload() { _games.Clear(); _next = null; _loaded = false; Render(); Run(LoadAll); }

    /// <summary>Every page of my games (GET /v1/games?joined=true), then the first page of all games for the open ones.</summary>
    async Task LoadAll()
    {
        var m = Main.Instance.Session.Management;
        Guid? after = null;
        var mine = new List<GameSummary>();
        do
        {
            var page = await m.ListGames(after, 200, joined: true);
            mine.AddRange(page.Items); after = page.Next;
        } while (after is not null);
        if (!IsInstanceValid(this)) return;
        foreach (var g in mine) _games[g.Id] = g;
        await LoadPage();
    }

    async Task LoadPage()
    {
        var page = await Main.Instance.Session.Management.ListGames(_next, 100);
        if (!IsInstanceValid(this)) return;
        foreach (var g in page.Items) _games.TryAdd(g.Id, g);
        _next = page.Next; _loaded = true;
        Render();
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

    /// <summary>Create game (part 1 → Create game): the name's problem after the first keystroke or an attempt, the enforcement's line under
    /// its picker. Created, the lobby opens at once, where each player picks an army in their team's column.</summary>
    async Task CreateDialog()
    {
        var session = Main.Instance.Session;
        Show("Loading rule sets…", "");
        var packs = await session.Management.ListProperties();
        if (!IsInstanceValid(this)) return;
        if (packs.Count == 0) { Show("The server has no rule set loaded (GET /v1/properties is empty), so no game can be created."); return; }
        Show("");
        var dialog = new ConfirmationDialog { Title = "Create game", OkButtonText = "Create game" };
        HomeLook.Primary(dialog.GetOkButton());
        var form = new VBoxContainer(); form.AddThemeConstantOverride("separation", Space.M); dialog.AddChild(form);
        var touched = false;
        var problem = DialogText(variation: Variation.Warning); problem.Visible = false;
        var name = new LineEdit { MaxLength = TextLimits.Name, PlaceholderText = "Friday night skirmish" };
        void Check()
        {
            var why = CreateDefaults.NameShown(name.Text, touched);
            problem.Text = why ?? ""; problem.Visible = why is not null;
            dialog.GetOkButton().Disabled = TextLimits.NameProblem(name.Text, "A game name") is not null;
        }
        name.TextChanged += _ => { touched = true; Check(); };
        var nameField = Field("Name", name); ((VBoxContainer)nameField).AddChild(problem); form.AddChild(nameField);
        var pack = new OptionButton(); foreach (var p in packs) pack.AddItem(CreateDefaults.PackLabel(p)); pack.Select(CreateDefaults.DefaultIndex(packs));
        if (packs.Count > 1) form.AddChild(Field("Rule set", pack));
        var two = new HBoxContainer(); two.AddThemeConstantOverride("separation", Space.M); form.AddChild(two);
        var shape = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill }; shape.AddItem("Alternating activation"); shape.AddItem("Player turn");
        var shapeField = Field("Activation", shape); shapeField.SizeFlagsHorizontal = SizeFlags.ExpandFill; two.AddChild(shapeField);
        var enforcement = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill }; foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e); enforcement.Select(1);
        var enforcementField = Field("Enforcement", enforcement); enforcementField.SizeFlagsHorizontal = SizeFlags.ExpandFill; two.AddChild(enforcementField);
        var hint = DialogText(CreateDefaults.EnforcementHint(Enforcement.Warn), Variation.Dim); form.AddChild(hint);
        enforcement.ItemSelected += i => hint.Text = CreateDefaults.EnforcementHint(Enum.Parse<Enforcement>(enforcement.GetItemText((int)i)));
        // Create waits for a good name; Enter on a bad one is an attempt, and shows its problem.
        Check();
        name.TextSubmitted += _ => { touched = true; Check(); if (!problem.Visible) { dialog.Hide(); Create(); } };
        dialog.Confirmed += Create;
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered(new Vector2I(DialogWidth + 60, 0)); name.CallDeferred(Control.MethodName.GrabFocus);

        void Create() => Run(async () =>
        {
            Show("Creating the game…", "");
            // WaitsBetweenSteps is a development flag (spec: in-game UX → Flow between steps); players never get the "Continue" stops.
            var game = await session.Management.CreateGame(new CreateGameRequest(name.Text.Trim(), packs[pack.Selected].ContentHash,
                shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, Enum.Parse<Enforcement>(enforcement.GetItemText(enforcement.Selected)), WaitsBetweenSteps: false));
            dialog.QueueFree();             // before the connect: it leaves this screen, and the dialog goes with it
            await session.ConnectAsync(game.Id);
        });
    }
}
