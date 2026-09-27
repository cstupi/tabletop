# Task 13: The Game screen — status routing, the lobby panel and the setup toolbar

**Files:**
- Create: `client/scenes/Game.tscn`, `client/src/Screens/GameScreen.cs`, `client/src/Panels/LobbyPanel.cs`, `client/src/Panels/SetupToolbar.cs`, `client/src/Panels/TeamPanel.cs`

**Interfaces:**
- Consumes: `Session` (`Game`, `Draft`, `Phase`, `IsCreator`, `ParticipantId`, `GameChanged`, `BoardChanged`, `PhaseChanged`, `RefusedReceived`, `ConnectionLost`, `SendEditAsync`, `DisconnectAsync`, `Management.ListArmies/ListLayouts/GetLayout/SaveLayout/ListAssets/GetAssetVolumes`), `TableView`, `TableInput`, `PresenceOverlay`, `BuiltinContent`, `TerrainGenerators`, `ZoneStyles`; every `LobbyEdit` and `SetupEdit`, `ProjectionDesyncException` (Protocol).
- Produces: `partial class GameScreen : Control` — hosts the `TableView` in a `SubViewportContainer`, `TableInput`, `PresenceOverlay`, the Home button (`DisconnectAsync`), a status line (`{game.name} — {status}`, `Reconnecting…` while `Phase == Reconnecting`), the panels by status: `LobbyPanel` at `lobby` and `setup`; `SetupToolbar` at `setup`; the play panels (Tasks 14–15) at `playing`/`ended`; a toast for `Refused` (`reason: detail`). `partial class LobbyPanel : Control` — teams with their members, "Create team", "Join team N"/"Watch" (`PickTeam(null)`), "Pick army" from `ListArmies` (`PickArmy`), the configuration form (`SetConfiguration` with the current values pre-filled), and for the creator "Start setup" (at `lobby`) and "End setup" (at `setup`, with a note of how many placements are off the slab). `partial class SetupToolbar : Control` — table width/length (`SetTableSize`), texture (`SetTexture` from `BuiltinContent.Textures` or my texture assets), add built-in terrain (a dropdown over `BuiltinContent.Terrain` → `AddTerrain(BuiltinContent.Place(name, beside-the-table, 0))`), add my library terrain (`ListAssets(Terrain)` → `GetAssetVolumes` → `AddTerrain` with `VisualRef.OfAsset` and the asset's volumes), the four generators (`TerrainGenerators.Generate(planet, size, Random.Shared)` → one `SendEditAsync` per add), add objective / token (`AddSetupMarker`), zone style (`SetZoneStyle(style, ZoneStyles.ShapesOf(style))`), swap zones (two ordinals → `SwapZones`), layouts ("Load…" → `ListLayouts` → `GetLayout` → `ReplaceDraft`; "Save as…" → `SaveLayout(Draft with { Name })`), and for the selected placement: scale (`ScaleTerrain`), remove (`RemoveTerrain`/`RemoveSetupMarker`), swap mesh (`SwapTerrainMesh` to a built-in or my asset), edit volumes (each volume's kind, footprint, heights, six declaration checkboxes and notes → `OverrideTerrainVolumes`), and "Reset volumes" (`OverrideTerrainVolumes` with `BuiltinContent.VolumesOf(name)` or the asset's `GetAssetVolumes`). `partial class TeamPanel : Control` — one per team: ordinal, colour, members, score (from `Projection.Scores`), a right-click hook for Task 15.

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
        var home = new Button { Text = "Home" }; home.Pressed += () => _ = _session.DisconnectAsync(); top.AddChild(home);
        top.AddChild(_status = new Label());
        _side.AddChild(_toast = new Label { Modulate = Colors.Orange });
        _bottom = new HBoxContainer(); _side.AddChild(_bottom);
        if (_session.GameId is not null) { Table.Bind(_session); Input.Bind(Table, _session); overlay.Bind(Table, _session); }
        _session.GameChanged += Rebuild; _session.PhaseChanged += Rebuild; _session.BoardChanged += RefreshTeams; _session.EventsApplied += _ => RefreshTeams();
        _session.RefusedReceived += r => Toast($"{r.Reason}: {r.Detail}");
        _session.ConnectionLost += (c, will) => Toast(c.Error is ProjectionDesyncException
            ? will ? "The table fell out of step with the server; rebuilding it from the log…" : $"This game's log cannot be replayed: {c.Reason}"
            : will ? $"Connection lost ({c.Reason}); reconnecting…" : $"Disconnected: {c.Reason}");
        Rebuild();
    }

    public void Toast(string text) { _toast.Text = text; GetTree().CreateTimer(4).Timeout += () => { if (_toast.Text == text) _toast.Text = ""; }; }

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

    void RefreshTeams() { foreach (var t in _teams) t.Refresh(_session); }

    partial void ShowPlayPanels(bool playing);
}
```

`partial void ShowPlayPanels` is declared here and given its body in Task 14 (`GameScreen.Play.cs`); until then the partial method compiles to nothing.

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

/// <summary>Lobby edits: written to the game's rows, broadcast, never logged. Shown at lobby and setup.</summary>
public partial class LobbyPanel : VBoxContainer
{
    public override void _Ready()
    {
        var s = Main.Instance.Session; var game = s.Game!;
        AddChild(new Label { Text = "Lobby" });
        var teams = new HBoxContainer(); AddChild(teams);
        foreach (var t in game.Teams) teams.AddChild(Btn($"Join team {t.Ordinal}", () => _ = s.SendEditAsync(new PickTeam(t.Id))));
        teams.AddChild(Btn("Watch", () => _ = s.SendEditAsync(new PickTeam(null))));
        teams.AddChild(Btn("Create team", () => _ = s.SendEditAsync(new CreateTeam())));
        var armies = new OptionButton(); armies.AddItem("No army"); AddChild(armies);
        _ = Task.Run(async () => { var list = (await s.Management.ListArmies(null)).Items; Callable.From(() => { foreach (var a in list) armies.AddItem(a.Name, 0); armies.ItemSelected += i => _ = s.SendEditAsync(new PickArmy(i == 0 ? null : list[(int)i - 1].Id)); }).CallDeferred(); });
        var config = new VBoxContainer(); AddChild(config);
        var name = new LineEdit { Text = game.Name }; config.AddChild(name);
        var shape = new OptionButton(); shape.AddItem("Alternating activation"); shape.AddItem("Player turn"); shape.Select(game.SchedulerShape == SchedulerShape.PlayerTurn ? 1 : 0); config.AddChild(shape);
        var enforcement = new OptionButton(); foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e); enforcement.Select((int)game.Enforcement); config.AddChild(enforcement);
        var waits = new CheckBox { Text = "Wait between steps", ButtonPressed = game.WaitsBetweenSteps }; config.AddChild(waits);
        config.AddChild(Btn("Apply configuration", () => _ = s.SendEditAsync(new SetConfiguration(name.Text, game.PropertiesHash,
            shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, (Enforcement)enforcement.Selected, waits.ButtonPressed))));
        if (s.IsCreator && game.Status == GameStatus.Lobby) AddChild(Btn("Start setup", () => _ = s.SendEditAsync(new StartSetup())));
        if (s.IsCreator && game.Status == GameStatus.Setup)
        {
            var off = s.Draft?.Terrain.Count(t => !Tabletop.Geometry.Table.ContainsPoint(s.Draft.Size, t.Position.Horizontal)) ?? 0;   // the game server's DropOffSlab rule
            AddChild(Btn(off == 0 ? "End setup" : $"End setup ({off} off-slab pieces will be dropped)", () => _ = s.SendEditAsync(new EndSetup())));
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
    public SetupToolbar(GameScreen screen) => _screen = screen;

    public override void _Ready()
    {
        var s = Main.Instance.Session; var draft = s.Draft!;
        AddChild(new Label { Text = "Setup" });

        var size = new HBoxContainer(); AddChild(size);
        var width = new SpinBox { MinValue = 12, MaxValue = 144, Value = draft.Size.Width, Suffix = "\"" }; var length = new SpinBox { MinValue = 12, MaxValue = 144, Value = draft.Size.Length, Suffix = "\"" };
        size.AddChild(width); size.AddChild(length);
        size.AddChild(Btn("Set size", () => _ = s.SendEditAsync(new SetTableSize(new TableSize(width.Value, length.Value)))));

        var texture = new OptionButton(); foreach (var t in BuiltinContent.Textures) texture.AddItem(t); AddChild(texture);
        texture.ItemSelected += i => _ = s.SendEditAsync(new SetTexture(VisualRef.OfBuiltin(BuiltinContent.Textures[(int)i])));

        var terrain = new OptionButton(); foreach (var t in BuiltinContent.Terrain.Values.OrderBy(t => t.Label)) terrain.AddItem(t.Label, 0); AddChild(terrain);
        var terrainNames = BuiltinContent.Terrain.Values.OrderBy(t => t.Label).Select(t => t.Name).ToList();
        AddChild(Btn("Add built-in terrain (beside the table)", () => _ = s.SendEditAsync(new AddTerrain(BuiltinContent.Place(terrainNames[terrain.Selected], Beside(s.Draft!), new Yaw(0))))));
        AddChild(Btn("Add my terrain…", () => _ = AddLibraryTerrain(s)));

        var generators = new HBoxContainer(); AddChild(generators);
        foreach (var planet in Enum.GetValues<Planet>())
            generators.AddChild(Btn($"{planet} planet", () => _ = Task.Run(async () => { foreach (var add in TerrainGenerators.Generate(planet, s.Draft!.Size, Random.Shared)) await s.SendEditAsync(add); })));

        var markers = new HBoxContainer(); AddChild(markers);
        markers.AddChild(Btn("Add objective", () => _ = s.SendEditAsync(new AddSetupMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Objective, Centre(s.Draft!), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"))))));
        markers.AddChild(Btn("Add token", () => _ = s.SendEditAsync(new AddSetupMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Token, Centre(s.Draft!), new Yaw(0), 1, "token", VisualRef.OfBuiltin("chip"))))));

        var zones = new HBoxContainer(); AddChild(zones);
        var style = new OptionButton(); foreach (var z in ZoneStyles.All) style.AddItem(z); zones.AddChild(style);
        zones.AddChild(Btn("Set zones", () => _ = s.SendEditAsync(new SetZoneStyle(ZoneStyles.All[style.Selected], ZoneStyles.ShapesOf(ZoneStyles.All[style.Selected])))));
        var a = new SpinBox { MinValue = 1, MaxValue = 8, Value = 1 }; var b = new SpinBox { MinValue = 1, MaxValue = 8, Value = 2 }; zones.AddChild(a); zones.AddChild(b);
        zones.AddChild(Btn("Swap", () => _ = s.SendEditAsync(new SwapZones((int)a.Value, (int)b.Value))));

        var layouts = new HBoxContainer(); AddChild(layouts);
        layouts.AddChild(Btn("Load layout…", () => _ = LoadLayout(s)));
        var layoutName = new LineEdit { PlaceholderText = "layout name" }; layouts.AddChild(layoutName);
        layouts.AddChild(Btn("Save as", () => _ = Guarded(async () => { await s.Management.SaveLayout(s.Draft! with { Name = layoutName.Text }); _screen.Toast("Layout saved."); })));

        AddChild(new Label { Text = "Selected placement" });
        var selected = new VBoxContainer(); AddChild(selected);
        _screen.Input.SelectionChanged += () => RenderSelected(s, selected);
        RenderSelected(s, selected);
    }

    static Point3 Beside(TableState d) => new(d.Size.Width + 4 + Random.Shared.Next(0, 3) * 8, 4 + Random.Shared.Next(0, 4) * 8, 0);
    static Point3 Centre(TableState d) => new(d.Size.Width / 2, d.Size.Length / 2, 0);
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
    async Task Guarded(Func<Task> work) { try { await work(); } catch (ManagementException e) { _screen.Toast(e.Message); } }

    async Task AddLibraryTerrain(Session s) => await Guarded(async () =>
    {
        var assets = (await s.Management.ListAssets(AssetKind.Terrain, null)).Items.Where(x => x.Uploaded).ToList();
        if (assets.Count == 0) { _screen.Toast("No terrain in your library; upload some in the web app."); return; }
        var dialog = new ConfirmationDialog { Title = "Add terrain from my library" }; var pick = new OptionButton(); foreach (var x in assets) pick.AddItem(x.Name); dialog.AddChild(pick);
        dialog.Confirmed += () => _ = Guarded(async () =>
        {
            var asset = assets[pick.Selected]; var volumes = await s.Management.GetAssetVolumes(asset.Id);
            await s.SendEditAsync(new AddTerrain(new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfAsset(asset.Id), Beside(s.Draft!), new Yaw(0), asset.Scale, volumes.Select(v => new TerrainVolume(Guid.CreateVersion7(), v.Volume)).ToList())));
        });
        AddChild(dialog); dialog.PopupCentered();
    });

    /// <summary>Setup phase -> Terrain: anyone may swap a piece's built-in mesh for an asset from their library, and the
    /// placement keeps its volumes and declarations. "Reset volumes" is how a user takes the new mesh's own instead.</summary>
    async Task SwapForAsset(Session s, Guid placementId) => await Guarded(async () =>
    {
        var assets = (await s.Management.ListAssets(AssetKind.Terrain, null)).Items.Where(x => x.Uploaded).ToList();
        if (assets.Count == 0) { _screen.Toast("No terrain in your library; upload some in the web app."); return; }
        var dialog = new ConfirmationDialog { Title = "Swap this piece's mesh (its volumes and declarations stay)" };
        var pick = new OptionButton(); foreach (var x in assets) pick.AddItem(x.Name); dialog.AddChild(pick);
        dialog.Confirmed += () => _ = s.SendEditAsync(new SwapTerrainMesh(placementId, VisualRef.OfAsset(assets[pick.Selected].Id)));
        AddChild(dialog); dialog.PopupCentered();
    });

    async Task LoadLayout(Session s) => await Guarded(async () =>
    {
        var layouts = await s.Management.ListLayouts();
        var dialog = new ConfirmationDialog { Title = "Load a layout (replaces the whole draft, size included)" }; var pick = new OptionButton(); foreach (var l in layouts) pick.AddItem($"{l.Name} ({l.Size.Width}×{l.Size.Length}, {l.Terrain} terrain)"); dialog.AddChild(pick);
        dialog.Confirmed += () => _ = Guarded(async () => await s.SendEditAsync(new ReplaceDraft(await s.Management.GetLayout(layouts[pick.Selected].Id))));
        AddChild(dialog); dialog.PopupCentered();
    });

    void RenderSelected(Session s, VBoxContainer box)
    {
        foreach (var c in box.GetChildren()) c.QueueFree();
        var sel = _screen.Input.Selected; var draft = s.Draft;
        if (sel is null || draft is null) return;
        if (draft.Markers.FirstOrDefault(m => m.Id == sel.PieceId) is { } marker) { box.AddChild(Btn("Remove marker", () => _ = s.SendEditAsync(new RemoveSetupMarker(marker.Id)))); return; }
        var t = draft.Terrain.FirstOrDefault(x => x.Id == sel.PieceId); if (t is null) return;
        box.AddChild(new Label { Text = t.Mesh.IsBuiltin ? BuiltinContent.Terrain.GetValueOrDefault(t.Mesh.Builtin!)?.Label ?? t.Mesh.Builtin : $"asset {Names.Short(t.Mesh.AssetId!.Value)}" });
        var scale = new SpinBox { MinValue = 0.25, MaxValue = 4, Step = 0.25, Value = t.Scale }; box.AddChild(scale); scale.ValueChanged += v => _ = s.SendEditAsync(new ScaleTerrain(t.Id, v));
        box.AddChild(Btn("Remove", () => _ = s.SendEditAsync(new RemoveTerrain(t.Id))));
        var swap = new OptionButton(); var names = BuiltinContent.Terrain.Keys.Order().ToList(); foreach (var n in names) swap.AddItem(n); box.AddChild(swap);
        box.AddChild(Btn("Swap mesh for this built-in (keeps volumes)", () => _ = s.SendEditAsync(new SwapTerrainMesh(t.Id, VisualRef.OfBuiltin(names[swap.Selected])))));
        box.AddChild(Btn("Swap mesh for one of my assets (keeps volumes)", () => _ = SwapForAsset(s, t.Id)));
        box.AddChild(Btn("Reset volumes to the source's", () => _ = Guarded(async () =>
            await s.SendEditAsync(new OverrideTerrainVolumes(t.Id, t.Mesh.IsBuiltin ? BuiltinContent.VolumesOf(t.Mesh.Builtin!) : (await s.Management.GetAssetVolumes(t.Mesh.AssetId!.Value)).Select(v => new TerrainVolume(Guid.CreateVersion7(), v.Volume)).ToList())))));
        foreach (var (tv, i) in t.Volumes.Select((v, i) => (v, i)))
        {
            var row = new HBoxContainer(); box.AddChild(row);
            row.AddChild(new Label { Text = $"volume {i + 1} {tv.Volume.Kind} {tv.Volume.Bottom}–{tv.Volume.Top}\"" });
            foreach (var flag in new[] { TerrainDeclarations.Impassable, TerrainDeclarations.BlocksLineOfSight, TerrainDeclarations.SeeInAndOut, TerrainDeclarations.Cover, TerrainDeclarations.Difficult, TerrainDeclarations.Dangerous })
            {
                var cb = new CheckBox { Text = flag.ToString(), ButtonPressed = tv.Volume.Declarations.HasFlag(flag) };
                cb.Toggled += on => _ = s.SendEditAsync(new OverrideTerrainVolumes(t.Id, t.Volumes.Select(v => v.Id != tv.Id ? v : v with { Volume = v.Volume with { Declarations = on ? v.Volume.Declarations | flag : v.Volume.Declarations & ~flag } }).ToList()));
                row.AddChild(cb);
            }
            var notes = new LineEdit { Text = tv.Volume.Notes ?? "", PlaceholderText = "notes" }; row.AddChild(notes);
            notes.TextSubmitted += text => _ = s.SendEditAsync(new OverrideTerrainVolumes(t.Id, t.Volumes.Select(v => v.Id != tv.Id ? v : v with { Volume = v.Volume with { Notes = text.Length == 0 ? null : text } }).ToList()));
        }
    }
}
```

Editing a volume's footprint and heights is the same `OverrideTerrainVolumes` with `SpinBox`es for `Footprint.A/B`, `Bottom`, `Top` and an `OptionButton` for `Kind`; add them to the row in the same pattern as the declaration checkboxes.

- [ ] **Step 5: Run it with two clients.** Lobby: teams, picks, configuration and "Start setup" (creator only; the other client's button is absent and a forged `StartSetup` gets `NotPermitted`). Setup: size, texture, built-in terrain beside the table, a *Hospitable planet* batch of 13–21 pieces beside the slab, dragging pieces on with the ruler, zones with team colours (grey for an ordinal with no team), swap, a layout saved then loaded on a fresh game (size included), a volume's declaration toggled and the proxy recoloured, "End setup" dropping the off-slab count. The desync arm of the `ConnectionLost` toast has no manual trigger, because nothing a player can do makes the server emit an event the applier cannot apply; `SessionTests` covers the resync and the drop to Home, and this toast's two desync arms are read rather than run. `./scripts/check-client` prints nothing. Commit: `Add the game screen with the lobby panel and the setup toolbar`.
