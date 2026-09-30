# Task 13: The Game screen — status routing, the lobby panel and the setup toolbar

**Files:**
- Create: `client/scenes/Game.tscn`, `client/src/Screens/GameScreen.cs`, `client/src/Panels/Guarded.cs`, `client/src/Panels/LobbyPanel.cs`, `client/src/Panels/SetupToolbar.cs`, `client/src/Panels/TeamPanel.cs`

**Interfaces:**
- Consumes: `Session` (`Game`, `Draft`, `Phase`, `IsCreator`, `ParticipantId`, `GameChanged`, `BoardChanged`, `PhaseChanged`, `RefusedReceived`, `ConnectionLost`, `SendEditAsync`, `DisconnectAsync`, `Management.ListArmies/ListLayouts/GetLayout/SaveLayout/ListAssets/GetAssetVolumes`), `TableView`, `TableInput`, `PresenceOverlay`, `BuiltinContent`, `TerrainGenerators`, `ZoneStyles`; every `LobbyEdit` and `SetupEdit`, `ProjectionDesyncException` (Protocol).
- Produces: `partial class GameScreen : Control` — hosts the `TableView` in a `SubViewportContainer`, `TableInput`, `PresenceOverlay`, the Home button (`DisconnectAsync`), a status line (`{game.name} — {status}`, `Reconnecting…` while `Phase == Reconnecting`), the panels by status: `LobbyPanel` at `lobby` and `setup`; `SetupToolbar` at `setup`; the play panels (Tasks 14–15) at `playing`/`ended` through `partial void ShowPlayPanels(bool)`; `TableView Table`, `TableInput Input`, `void Toast(string)` (a four-second line; a no-op outside the tree); a toast for `Refused` (`reason: detail`) and for `ConnectionLost`. `static class Guarded` — `async void Run(Func<Task>, Action<string> report)` (every exception reported) and `async void Send(Session, Edit)` (a failed edit send logged). `partial class LobbyPanel : Control` — teams with their members, "Create team", "Join team N"/"Watch" (`PickTeam(null)`), "Pick army" from `ListArmies` (`PickArmy`), the configuration form (`SetConfiguration` with the current values pre-filled), and for the creator "Start setup" (at `lobby`) and "End setup" (at `setup`, its label naming how many placements are off the slab, recomputed on every `BoardChanged`). `partial class SetupToolbar : Control` — table width/length (`SetTableSize`; the spin boxes follow the draft on every `BoardChanged` unless one has focus, and show 72×48 until the draft arrives), texture (`SetTexture` from `BuiltinContent.Textures` or my texture assets), add built-in terrain (a dropdown over `BuiltinContent.Terrain` → `AddTerrain(BuiltinContent.Place(name, beside-the-table, 0))`), add my library terrain (`ListAssets(Terrain)` → `GetAssetVolumes` → `AddTerrain` with `VisualRef.OfAsset`, the asset's volumes and placement scale 1), the four generators (`TerrainGenerators.Generate(planet, size, Random.Shared)` → one `SendEditAsync` per add, awaited in turn on the main thread), add objective / token (`AddSetupMarker`), zone style (`SetZoneStyle(style, ZoneStyles.ShapesOf(style))`), swap zones (two ordinals → `SwapZones`), layouts ("Load…" → `ListLayouts` → `GetLayout` → `ReplaceDraft`; "Save as…" → `SaveLayout(Draft with { Name })`), and for the selected placement: scale (`ScaleTerrain`), remove (`RemoveTerrain`/`RemoveSetupMarker`), swap mesh (`SwapTerrainMesh` to a built-in or my asset), edit volumes (each volume's kind, footprint, heights, six declaration checkboxes and notes → `OverrideTerrainVolumes`), and "Reset volumes" (`OverrideTerrainVolumes` with `BuiltinContent.VolumesOf(name)` or the asset's `GetAssetVolumes`). `partial class TeamPanel : Control` — one per team: ordinal, colour, members, score (from `Projection.Scores`), a right-click hook for Task 15.

Spec: client shell → Screens → Game (one screen for all four statuses; a status change is a change inside the screen; Home disconnects); setup phase → The lifecycle (lobby picks and configuration; the creator's two transitions; teams and armies editable during setup), Editing is free for all (the closed set of edits), Layouts (load replaces the whole draft; save is a management call from the client's own copy), Zones (style; anyone swaps), Terrain (built-in or asset; swapping the mesh keeps the volumes; clearing re-copies the source's volumes; generators as a batch of adds beside the table), Joining (a participant with no team watches); geometry plan → `ZoneStyles.ShapesOf`; game server plan Tasks 10–12 (which edits are accepted at which status; `Refused` reasons).

- [ ] **Step 1: Write `client/scenes/Game.tscn`** as in Task 9 Step 1 with `res://src/Screens/GameScreen.cs`, root `Game` of type `Control`.

- [ ] **Step 2: Write `GameScreen.cs`**

```csharp
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
```

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>The error-handling pattern for panel code that awaits management calls or sends edits: nothing escapes, nothing is left unobserved.</summary>
public static class Guarded
{
    public static async void Run(Func<Task> work, Action<string> report)
    {
        try { await work(); }
        catch (ManagementException e) { report(e.Message); }
        catch (HttpRequestException e) { report($"Cannot reach the management API: {e.Message}"); }
        catch (Exception e) { report(e.Message); }
    }

    /// <summary>A socket send that fails is logged, never thrown into the engine.</summary>
    public static async void Send(Session s, Edit edit)
    {
        try { await s.SendEditAsync(edit); }
        catch (Exception e) { GD.PushWarning($"Edit send failed: {e.Message}"); }
    }
}
```

`partial void ShowPlayPanels` is declared here and given its body in Task 14 (`GameScreen.Play.cs`); until then the partial method compiles to nothing.

`Guarded` is the Godot layer's one error pattern for panel code: an `async void` that catches every exception and reports it, so nothing escapes into the engine. The panels keep their Session handlers as fields and remove them in `_ExitTree`. `Toast` returns when the screen is outside the tree, because a handler can fire during teardown.

- [ ] **Step 3: Write `TeamPanel.cs` and `LobbyPanel.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

public partial class TeamPanel : PanelContainer
{
    public Team Team { get; }
    readonly Label _text = new();
    public event Action<Vector2>? RightClicked;
    public TeamPanel(Team team) { Team = team; AddChild(_text); SelfModulate = Palette.Team(team.Ordinal); }

    public void Refresh(Session s)
    {
        var members = s.Game?.Participants.Where(p => p.TeamId == Team.Id).Select(p => p.Name + (p.ArmyId is null ? "" : " ⚔")) ?? [];
        var score = s.Projection?.Scores.GetValueOrDefault(Team.Id);
        var active = s.Projection?.Scheduler.ActiveTeamId == Team.Id ? "  ◀ active" : "";
        _text.Text = $"Team {Team.Ordinal}{(score is null ? "" : $"  score {score}")}{active}\n{string.Join(", ", members)}";
    }

    public override void _GuiInput(InputEvent e) { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) RightClicked?.Invoke(b.GlobalPosition); }
}
```

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>Lobby edits: written to the game's rows, broadcast, never logged. Shown at lobby and setup.</summary>
public partial class LobbyPanel : VBoxContainer
{
    Label _error = null!; Button? _endSetup; Session _session = null!; Action? _onBoard;

    public override void _ExitTree()
    {
        if (_session is null || _onBoard is null) return;
        _session.BoardChanged -= _onBoard;
    }

    static string EndText(Session s)
    {
        var off = s.Draft is { } d ? d.Terrain.Count(t => !Tabletop.Geometry.Table.ContainsPoint(d.Size, t.Position.Horizontal)) : 0;   // the game server's DropOffSlab rule
        return off == 0 ? "End setup" : $"End setup ({off} off-slab pieces will be dropped)";
    }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session; var game = s.Game!;
        AddChild(new Label { Text = "Lobby" });
        var teams = new HBoxContainer(); AddChild(teams);
        foreach (var t in game.Teams) teams.AddChild(Btn($"Join team {t.Ordinal}", () => Guarded.Send(s, new PickTeam(t.Id))));
        teams.AddChild(Btn("Watch", () => Guarded.Send(s, new PickTeam(null))));
        teams.AddChild(Btn("Create team", () => Guarded.Send(s, new CreateTeam())));
        var armies = new OptionButton(); armies.AddItem("No army"); AddChild(armies);
        AddChild(_error = new Label { Modulate = Colors.Orange });
        Guarded.Run(async () =>
        {
            var list = (await s.Management.ListArmies(null)).Items;
            if (!IsInstanceValid(this)) return;
            foreach (var a in list) armies.AddItem(a.Name);
            var mine = game.Participants.FirstOrDefault(p => p.Id == s.ParticipantId)?.ArmyId;
            if (mine is not null) { var at = list.ToList().FindIndex(a => a.Id == mine); if (at >= 0) armies.Select(at + 1); }
            armies.ItemSelected += i => Guarded.Send(s, new PickArmy(i == 0 ? null : list[(int)i - 1].Id));
        }, e => { if (IsInstanceValid(this)) _error.Text = e; });
        var config = new VBoxContainer(); AddChild(config);
        var name = new LineEdit { Text = game.Name }; config.AddChild(name);
        var shape = new OptionButton(); shape.AddItem("Alternating activation"); shape.AddItem("Player turn"); shape.Select(game.SchedulerShape == SchedulerShape.PlayerTurn ? 1 : 0); config.AddChild(shape);
        var enforcement = new OptionButton(); foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e); enforcement.Select((int)game.Enforcement); config.AddChild(enforcement);
        var waits = new CheckBox { Text = "Wait between steps", ButtonPressed = game.WaitsBetweenSteps }; config.AddChild(waits);
        config.AddChild(Btn("Apply configuration", () => Guarded.Send(s, new SetConfiguration(name.Text, game.PropertiesHash,
            shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, (Enforcement)enforcement.Selected, waits.ButtonPressed))));
        if (s.IsCreator && game.Status == GameStatus.Lobby) AddChild(Btn("Start setup", () => Guarded.Send(s, new StartSetup())));
        if (s.IsCreator && game.Status == GameStatus.Setup)
        {
            AddChild(_endSetup = Btn(EndText(s), () => Guarded.Send(s, new EndSetup())));
            _onBoard = () => { if (IsInstanceValid(_endSetup)) _endSetup!.Text = EndText(s); };
            s.BoardChanged += _onBoard;
        }
    }

    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
}
```

`LobbyPanel` is the only control that sends `CreateTeam` or `PickTeam`, and `GameScreen.Rebuild` adds it only at `lobby` and `setup`, which is where the server accepts those edits: teams are locked at the flip to `playing` (game flow → Teams are frozen at the flip, and a team is a slot). Nothing offers a team pick from `playing` on, and nothing asks for one after a reconnect — a `TeamPanel` lists whoever `Game.Participants` puts on its team, and the `Welcome` that ends a reconnect names the same team it named before, so a returning player reappears in the panel they left. `TeamPanel.Refresh` keys on `Team.Id` and never on `MyTeamId`, so it draws a returning player and one who never dropped identically.

- [ ] **Step 4: Write `SetupToolbar.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>The closed set of setup edits, one control each. Anyone may use every one of them; the server applies in arrival order.</summary>
public partial class SetupToolbar : VBoxContainer
{
    readonly GameScreen _screen;
    Session _session = null!; VBoxContainer _selected = null!; SpinBox _width = null!, _length = null!;
    Action? _onSelection, _onBoard;
    public SetupToolbar(GameScreen screen) => _screen = screen;

    public override void _ExitTree()
    {
        if (_session is null) return;
        _screen.Input.SelectionChanged -= _onSelection; _session.BoardChanged -= _onBoard;
    }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session; var size0 = SizeNow(s);   // the board may not have arrived yet
        AddChild(new Label { Text = "Setup" });

        var size = new HBoxContainer(); AddChild(size);
        var width = _width = new SpinBox { MinValue = 12, MaxValue = 144, Value = size0.Width, Suffix = "\"" }; var length = _length = new SpinBox { MinValue = 12, MaxValue = 144, Value = size0.Length, Suffix = "\"" };
        size.AddChild(width); size.AddChild(length);
        size.AddChild(Btn("Set size", () => Guarded.Send(s, new SetTableSize(new TableSize(width.Value, length.Value)))));

        var texture = new OptionButton(); foreach (var t in BuiltinContent.Textures) texture.AddItem(t); AddChild(texture);
        texture.ItemSelected += i => Guarded.Send(s, new SetTexture(VisualRef.OfBuiltin(BuiltinContent.Textures[(int)i])));

        var terrain = new OptionButton(); foreach (var t in BuiltinContent.Terrain.Values.OrderBy(t => t.Label)) terrain.AddItem(t.Label, 0); AddChild(terrain);
        var terrainNames = BuiltinContent.Terrain.Values.OrderBy(t => t.Label).Select(t => t.Name).ToList();
        AddChild(Btn("Add built-in terrain (beside the table)", () => Guarded.Send(s, new AddTerrain(BuiltinContent.Place(terrainNames[terrain.Selected], Beside(SizeNow(s)), new Yaw(0))))));
        AddChild(Btn("Add my terrain…", () => Guarded.Run(() => AddLibraryTerrain(s), _screen.Toast)));

        var generators = new HBoxContainer(); AddChild(generators);
        foreach (var planet in Enum.GetValues<Planet>())
            generators.AddChild(Btn($"{planet} planet", () => Guarded.Run(async () => { foreach (var add in TerrainGenerators.Generate(planet, SizeNow(s), Random.Shared)) await s.SendEditAsync(add); }, _screen.Toast)));

        var markers = new HBoxContainer(); AddChild(markers);
        markers.AddChild(Btn("Add objective", () => Guarded.Send(s, new AddSetupMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Objective, Centre(SizeNow(s)), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"))))));
        markers.AddChild(Btn("Add token", () => Guarded.Send(s, new AddSetupMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Token, Centre(SizeNow(s)), new Yaw(0), 1, "token", VisualRef.OfBuiltin("chip"))))));

        var zones = new HBoxContainer(); AddChild(zones);
        var style = new OptionButton(); foreach (var z in ZoneStyles.All) style.AddItem(z); zones.AddChild(style);
        zones.AddChild(Btn("Set zones", () => Guarded.Send(s, new SetZoneStyle(ZoneStyles.All[style.Selected], ZoneStyles.ShapesOf(ZoneStyles.All[style.Selected])))));
        var a = new SpinBox { MinValue = 1, MaxValue = 8, Value = 1 }; var b = new SpinBox { MinValue = 1, MaxValue = 8, Value = 2 }; zones.AddChild(a); zones.AddChild(b);
        zones.AddChild(Btn("Swap", () => Guarded.Send(s, new SwapZones((int)a.Value, (int)b.Value))));

        var layouts = new HBoxContainer(); AddChild(layouts);
        layouts.AddChild(Btn("Load layout…", () => Guarded.Run(() => LoadLayout(s), _screen.Toast)));
        var layoutName = new LineEdit { PlaceholderText = "layout name" }; layouts.AddChild(layoutName);
        layouts.AddChild(Btn("Save as", () => Guarded.Run(async () => { await s.Management.SaveLayout((s.Draft ?? throw new InvalidOperationException("The table has not arrived yet.")) with { Name = layoutName.Text }); _screen.Toast("Layout saved."); }, _screen.Toast)));

        AddChild(new Label { Text = "Selected placement" });
        _selected = new VBoxContainer(); AddChild(_selected);
        _onSelection = () => RenderSelected(s, _selected);
        _onBoard = () =>
        {
            if (s.Draft is { } d && !_width.HasFocus() && !_length.HasFocus()) { _width.SetValueNoSignal(d.Size.Width); _length.SetValueNoSignal(d.Size.Length); }
            RenderSelected(s, _selected);
        };
        _screen.Input.SelectionChanged += _onSelection; s.BoardChanged += _onBoard;   // a draft edit changes what the selected piece shows
        RenderSelected(s, _selected);
    }

    static TableSize SizeNow(Session s) => s.Draft?.Size ?? new TableSize(72, 48);
    static Point3 Beside(TableSize d) => new(d.Width + 4 + Random.Shared.Next(0, 3) * 8, 4 + Random.Shared.Next(0, 4) * 8, 0);
    static Point3 Centre(TableSize d) => new(d.Width / 2, d.Length / 2, 0);
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }

    ConfirmationDialog Dialog(string title, OptionButton pick)
    {
        var dialog = new ConfirmationDialog { Title = title }; dialog.AddChild(pick);
        dialog.VisibilityChanged += () => { if (!dialog.Visible) dialog.QueueFree(); };
        AddChild(dialog); return dialog;
    }

    async Task AddLibraryTerrain(Session s)
    {
        var assets = (await s.Management.ListAssets(AssetKind.Terrain, null)).Items.Where(x => x.Uploaded).ToList();
        if (!IsInstanceValid(this)) return;
        if (assets.Count == 0) { _screen.Toast("No terrain in your library; upload some in the web app."); return; }
        var pick = new OptionButton(); foreach (var x in assets) pick.AddItem(x.Name);
        var dialog = Dialog("Add terrain from my library", pick);
        dialog.Confirmed += () => Guarded.Run(async () =>
        {
            // Scale 1: MeshLoader.Orient already applies the asset's scale to the mesh, and its volumes are table-frame after scale.
            var asset = assets[pick.Selected]; var volumes = await s.Management.GetAssetVolumes(asset.Id);
            await s.SendEditAsync(new AddTerrain(new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfAsset(asset.Id), Beside(SizeNow(s)), new Yaw(0), 1, volumes.Select(v => new TerrainVolume(Guid.CreateVersion7(), v.Volume)).ToList())));
        }, _screen.Toast);
        dialog.PopupCentered();
    }

    /// <summary>Setup phase -> Terrain: anyone may swap a piece's built-in mesh for an asset from their library, and the
    /// placement keeps its volumes and declarations. "Reset volumes" is how a user takes the new mesh's own instead.</summary>
    async Task SwapForAsset(Session s, Guid placementId)
    {
        var assets = (await s.Management.ListAssets(AssetKind.Terrain, null)).Items.Where(x => x.Uploaded).ToList();
        if (!IsInstanceValid(this)) return;
        if (assets.Count == 0) { _screen.Toast("No terrain in your library; upload some in the web app."); return; }
        var pick = new OptionButton(); foreach (var x in assets) pick.AddItem(x.Name);
        var dialog = Dialog("Swap this piece's mesh (its volumes and declarations stay)", pick);
        dialog.Confirmed += () => Guarded.Send(s, new SwapTerrainMesh(placementId, VisualRef.OfAsset(assets[pick.Selected].Id)));
        dialog.PopupCentered();
    }

    async Task LoadLayout(Session s)
    {
        var layouts = await s.Management.ListLayouts();
        if (!IsInstanceValid(this)) return;
        var pick = new OptionButton(); foreach (var l in layouts) pick.AddItem($"{l.Name} ({l.Size.Width}×{l.Size.Length}, {l.Terrain} terrain)");
        var dialog = Dialog("Load a layout (replaces the whole draft, size included)", pick);
        dialog.Confirmed += () => Guarded.Run(async () => await s.SendEditAsync(new ReplaceDraft(await s.Management.GetLayout(layouts[pick.Selected].Id))), _screen.Toast);
        dialog.PopupCentered();
    }

    /// <summary>Every volume field is one OverrideTerrainVolumes carrying the placement's whole volume list with one volume changed.</summary>
    static void EditVolume(Session s, TerrainPlacement t, Guid volumeId, Func<Volume, Volume> change) =>
        Guarded.Send(s, new OverrideTerrainVolumes(t.Id, t.Volumes.Select(v => v.Id != volumeId ? v : v with { Volume = change(v.Volume) }).ToList()));

    static SpinBox Spin(double value, double max, Action<double> changed) { var sb = new SpinBox { MinValue = 0, MaxValue = max, Step = 0.25, Value = value, CustomMinimumSize = new Vector2(70, 0) }; sb.ValueChanged += v => changed(v); return sb; }

    void RenderSelected(Session s, VBoxContainer box)
    {
        foreach (var c in box.GetChildren()) c.QueueFree();
        var sel = _screen.Input.Selected; var draft = s.Draft;
        if (sel is null || draft is null) return;
        if (draft.Markers.FirstOrDefault(m => m.Id == sel.PieceId) is { } marker) { box.AddChild(Btn("Remove marker", () => Guarded.Send(s, new RemoveSetupMarker(marker.Id)))); return; }
        var t = draft.Terrain.FirstOrDefault(x => x.Id == sel.PieceId); if (t is null) return;
        box.AddChild(new Label { Text = t.Mesh.IsBuiltin ? BuiltinContent.Terrain.GetValueOrDefault(t.Mesh.Builtin!)?.Label ?? t.Mesh.Builtin : $"asset {Names.Short(t.Mesh.AssetId!.Value)}" });
        var scale = new SpinBox { MinValue = 0.25, MaxValue = 4, Step = 0.25, Value = t.Scale }; box.AddChild(scale); scale.ValueChanged += v => Guarded.Send(s, new ScaleTerrain(t.Id, v));
        box.AddChild(Btn("Remove", () => Guarded.Send(s, new RemoveTerrain(t.Id))));
        var swap = new OptionButton(); var names = BuiltinContent.Terrain.Keys.Order().ToList(); foreach (var n in names) swap.AddItem(n); box.AddChild(swap);
        box.AddChild(Btn("Swap mesh for this built-in (keeps volumes)", () => Guarded.Send(s, new SwapTerrainMesh(t.Id, VisualRef.OfBuiltin(names[swap.Selected])))));
        box.AddChild(Btn("Swap mesh for one of my assets (keeps volumes)", () => Guarded.Run(() => SwapForAsset(s, t.Id), _screen.Toast)));
        box.AddChild(Btn("Reset volumes to the source's", () => Guarded.Run(async () =>
            await s.SendEditAsync(new OverrideTerrainVolumes(t.Id, t.Mesh.IsBuiltin ? BuiltinContent.VolumesOf(t.Mesh.Builtin!) : (await s.Management.GetAssetVolumes(t.Mesh.AssetId!.Value)).Select(v => new TerrainVolume(Guid.CreateVersion7(), v.Volume)).ToList())), _screen.Toast)));
        foreach (var (tv, i) in t.Volumes.Select((v, i) => (v, i)))
        {
            var row = new VBoxContainer(); box.AddChild(row);
            row.AddChild(new Label { Text = $"volume {i + 1}" });
            var shape = new HBoxContainer(); row.AddChild(shape);
            var kind = new OptionButton(); foreach (var k in Enum.GetNames<VolumeKind>()) kind.AddItem(k); kind.Select((int)tv.Volume.Kind); shape.AddChild(kind);
            kind.ItemSelected += k => EditVolume(s, t, tv.Id, v => v with { Kind = (VolumeKind)k });
            var fp = tv.Volume.Footprint;
            shape.AddChild(new Label { Text = fp.Kind == FootprintKind.Circle ? "radius" : "w×l" });
            shape.AddChild(Spin(fp.A, 144, x => EditVolume(s, t, tv.Id, v => v with { Footprint = v.Footprint.Kind == FootprintKind.Circle ? new Footprint(FootprintKind.Circle, x, x) : v.Footprint with { A = x } })));
            if (fp.Kind == FootprintKind.Rect) shape.AddChild(Spin(fp.B, 144, x => EditVolume(s, t, tv.Id, v => v with { Footprint = v.Footprint with { B = x } })));
            var heights = new HBoxContainer(); row.AddChild(heights);
            heights.AddChild(new Label { Text = "from" }); heights.AddChild(Spin(tv.Volume.Bottom, 60, x => EditVolume(s, t, tv.Id, v => v with { Bottom = x })));
            heights.AddChild(new Label { Text = "to" }); heights.AddChild(Spin(tv.Volume.Top, 60, x => EditVolume(s, t, tv.Id, v => v with { Top = x })));
            var flags = new HFlowContainer(); row.AddChild(flags);
            foreach (var flag in new[] { TerrainDeclarations.Impassable, TerrainDeclarations.BlocksLineOfSight, TerrainDeclarations.SeeInAndOut, TerrainDeclarations.Cover, TerrainDeclarations.Difficult, TerrainDeclarations.Dangerous })
            {
                var cb = new CheckBox { Text = flag.ToString(), ButtonPressed = tv.Volume.Declarations.HasFlag(flag) };
                cb.Toggled += on => EditVolume(s, t, tv.Id, v => v with { Declarations = on ? v.Declarations | flag : v.Declarations & ~flag });
                flags.AddChild(cb);
            }
            var notes = new LineEdit { Text = tv.Volume.Notes ?? "", PlaceholderText = "notes" }; row.AddChild(notes);
            notes.TextSubmitted += text => EditVolume(s, t, tv.Id, v => v with { Notes = text.Length == 0 ? null : text });
        }
    }
}
```

Each volume row edits its `Kind`, its footprint (`A`, and `B` for a rect), `Bottom`, `Top`, the six declarations and the notes, each change one `OverrideTerrainVolumes`.

Library terrain is placed at `Scale = 1`: `MeshLoader.Orient` already applies the asset's scale to the mesh, and the volumes `GetAssetVolumes` answers are table-frame after scale. A placement scale equal to the asset's would apply it twice. The selection's panel rebuilds on `BoardChanged`, so it shows the draft the last edit produced. The generator batch is sent from `Guarded.Run` on the main thread, one awaited send per add.

- [ ] **Step 5: Run it with two clients.** This run carries Task 11 Step 7's table checks too. Lobby: teams, picks, configuration and "Start setup" (creator only; the other client's button is absent and a forged `StartSetup` gets `NotPermitted`). Setup: size, texture, built-in terrain beside the table, a *Hospitable planet* batch of 13–21 pieces beside the slab, dragging pieces on with the ruler, zones with team colours (grey for an ordinal with no team), swap, a layout saved then loaded on a fresh game (size included), a volume's declaration toggled and the proxy recoloured, "End setup" dropping the off-slab count. The desync arm of the `ConnectionLost` toast has no manual trigger, because nothing a player can do makes the server emit an event the applier cannot apply; `SessionTests` covers the resync and the drop to Home, and this toast's two desync arms are read rather than run. `./scripts/check-client` prints nothing. Commit: `Add the game screen with the lobby panel and the setup toolbar`.
