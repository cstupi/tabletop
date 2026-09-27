# Task 10: The Home screen — the game list, create game, join

**Files:**
- Create: `client/scenes/Home.tscn`, `client/src/Screens/HomeScreen.cs`

**Interfaces:**
- Consumes: `Session.Management` (`ListGames`, `ListProperties`, `ListArmies`, `CreateGame`, `JoinGame`), `Session.ConnectAsync`, `Session.SendEditAsync(PickArmy)`, `Session.LogoutAsync`, `Session.LastCloseReason`; `GameSummary`, `PropertiesSummary`, `ArmySummary`, `CreateGameRequest` (Task 3); `SchedulerShape`, `Enforcement`, `GameStatus`, `PickArmy` (Protocol).
- Produces: `partial class HomeScreen : Control` — the game list (`ItemList`) with a status filter, a name filter and "created by me", a "More" button for the next keyset page, "Join", "Create game" (name, pack from `GET /v1/properties`, shape, enforcement, wait-between-steps, army from `GET /v1/armies`), "Sign out". Creating: `CreateGame` → `ConnectAsync(game.Id)` → once `Phase == Connected`, `SendEditAsync(new PickArmy(armyId))`. Joining: `JoinGame` → `ConnectAsync`.
- `POST /v1/games` takes no army (management plan → HTTP surface; whole-design review → Gaps filled); the army is a lobby edit after the socket opens (game server plan Task 10; setup phase → Editing is free for all).

Spec: client shell → Screens → Home (the game list from `GET /v1/games`, every status, filtered by the player; create game with name, pack, shape, enforcement, wait and one of the player's armies; creating or joining leads to the same place); accounts → Games are public; setup phase → Joining is allowed at any status.

- [ ] **Step 1: Write `client/scenes/Home.tscn`** as in Task 9 Step 1 with `res://src/Screens/HomeScreen.cs` and root `Home`.

- [ ] **Step 2: Write `HomeScreen.cs`**

```csharp
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
        top.AddChild(Btn("Create game", () => _ = CreateDialog()));
        top.AddChild(Btn("Sign out", () => _ = Main.Instance.Session.LogoutAsync()));
        var filters = new HBoxContainer(); root.AddChild(filters);
        filters.AddChild(_status = new OptionButton());
        _status.AddItem("Any status"); foreach (var s in Enum.GetNames<GameStatus>()) _status.AddItem(s);
        _status.ItemSelected += _ => Render();
        filters.AddChild(_search = new LineEdit { PlaceholderText = "name", CustomMinimumSize = new Vector2(200, 0) }); _search.TextChanged += _ => Render();
        filters.AddChild(_mine = new CheckBox { Text = "Created by me" }); _mine.Toggled += _ => Render();
        root.AddChild(_list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill });
        _list.ItemActivated += i => _ = Join(Visible()[(int)i].Id);
        var bottom = new HBoxContainer(); root.AddChild(bottom);
        bottom.AddChild(Btn("Join", () => { if (_list.GetSelectedItems() is { Length: > 0 } sel) _ = Join(Visible()[sel[0]].Id); }));
        bottom.AddChild(Btn("More", () => _ = LoadPage()));
        bottom.AddChild(Btn("Refresh", () => { _games.Clear(); _next = null; _ = LoadPage(); }));
        root.AddChild(_error = new Label { Modulate = Colors.Orange });
        // Why the last game ended, when it ended without the player asking: a replaced socket, a rejection, or a
        // desync past its bound. Session clears it on the next connect. Without this the drop to Home is silent.
        if (Main.Instance.Session.LastCloseReason is { } why) _error.Text = $"Dropped from the last game: {why}";
        _ = LoadPage();
    }

    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }

    List<GameSummary> Visible()
    {
        var me = Main.Instance.Session.Account?.Id;
        return _games.Where(g => (_status.Selected == 0 || g.Status.ToString() == _status.GetItemText(_status.Selected))
                              && (_search.Text.Length == 0 || g.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase))
                              && (!_mine.ButtonPressed || g.CreatedBy == me)).ToList();
    }

    void Render()
    {
        _list.Clear();
        foreach (var g in Visible()) _list.AddItem($"{g.Name}  —  {g.Status.ToString().ToLowerInvariant()}, {g.Participants} in, {g.CreatedAt.LocalDateTime:g}");
    }

    async Task LoadPage()
    {
        try
        {
            var page = await Main.Instance.Session.Management.ListGames(_next, 50);
            _games.AddRange(page.Items.Where(g => _games.All(x => x.Id != g.Id))); _next = page.Next;
            Render();
        }
        catch (ManagementException e) { _error.Text = e.Message; }
    }

    async Task Join(Guid gameId)
    {
        try { await Main.Instance.Session.Management.JoinGame(gameId); await Main.Instance.Session.ConnectAsync(gameId); }
        catch (ManagementException e) { _error.Text = e.Message; }
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
        var waits = new CheckBox { Text = "Wait between steps" }; form.AddChild(waits);
        var army = new OptionButton(); army.AddItem("No army (watch)"); foreach (var a in armies) army.AddItem($"{a.Name} ({a.Units} units)"); form.AddChild(army);
        dialog.Confirmed += async () =>
        {
            if (packs.Count == 0) { _error.Text = "No Properties set is loaded on the server; GET /v1/properties is empty."; return; }
            try
            {
                var game = await session.Management.CreateGame(new CreateGameRequest(name.Text, packs[pack.Selected].ContentHash,
                    shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, Enum.Parse<Enforcement>(enforcement.GetItemText(enforcement.Selected)), waits.ButtonPressed));
                Guid? armyId = army.Selected == 0 ? null : armies[army.Selected - 1].Id;
                if (armyId is { } id)
                {
                    void PickWhenConnected() { if (session.Phase == SessionPhase.Connected) { session.PhaseChanged -= PickWhenConnected; _ = session.SendEditAsync(new PickArmy(id)); } }
                    session.PhaseChanged += PickWhenConnected;
                }
                await session.ConnectAsync(game.Id);
            }
            catch (ManagementException e) { _error.Text = e.Message; }
        };
        AddChild(dialog); dialog.PopupCentered(new Vector2I(420, 320));
    }
}
```

- [ ] **Step 3: Run it.** With a `properties` row seeded (management plan Task 12's `Seed()` insert, run once by hand: `docker compose exec postgres psql -U tabletop -d tabletop -c "insert into management.properties values ('sha256:gf-dev','Grimdark Future core','grimdark-future') on conflict do nothing"`) and `dotnet run --project server/game` running: Create game → the screen goes to the Game scene (Task 13; until then the Main root shows nothing and the log prints `Connect` sent). `Refresh` lists the game; a second account in a second client instance sees and joins it. `./scripts/check-client` prints nothing. Commit: `Add the Home screen: the game list, create game and join`.
