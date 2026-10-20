using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Client.Theming;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>The closed set of setup edits, one control each. Anyone may use every one of them; the server applies in arrival order.</summary>
public partial class SetupToolbar : VBoxContainer
{
    readonly GameScreen _screen;
    Session _session = null!; SpinBox _width = null!, _length = null!; PieceMenu _menu = null!;
    Action? _onBoard, _onBuiltins; Action<Guid, Vector2>? _onRight; Action<Intent>? _onIntent;
    public SetupToolbar(GameScreen screen) => _screen = screen;

    public override void _ExitTree()
    {
        if (_session is null) return;
        _session.BoardChanged -= _onBoard;
        _screen.Input.PieceRightClicked -= _onRight; _screen.Input.IntentPressed -= _onIntent;
        BuiltinContent.Changed -= _onBuiltins;
    }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session; var size0 = SizeNow(s);   // the board may not have arrived yet
        AddChild(new Label { Text = "Setup" });

        var table = Section(_ => "Table");
        table.AddChild(new Label { Text = SetupLimits.SizeHint, ThemeTypeVariation = Variation.Dim });
        var size = new HFlowContainer(); table.AddChild(size);
        var width = _width = new SpinBox { MinValue = SetupLimits.MinEdge, MaxValue = SetupLimits.MaxEdge, Value = size0.Width, Suffix = "\"" }; var length = _length = new SpinBox { MinValue = SetupLimits.MinEdge, MaxValue = SetupLimits.MaxEdge, Value = size0.Length, Suffix = "\"" };
        size.AddChild(width); size.AddChild(length);
        size.AddChild(Btn("Set size", () => Guarded.Send(s, new SetTableSize(new TableSize(width.Value, length.Value)))));
        // The lists follow the built-in catalog in use, refilled when a release brings a new one.
        List<string> textures = [], terrainNames = [];
        var texture = new OptionButton(); table.AddChild(texture);
        texture.ItemSelected += i => Guarded.Send(s, new SetTexture(VisualRef.OfBuiltin(textures[(int)i])));

        var terrainSection = Section(SetupLimits.TerrainHeading);
        var terrain = new OptionButton(); terrainSection.AddChild(terrain);
        void Fill()
        {
            textures = [.. BuiltinContent.Textures]; texture.Clear(); foreach (var t in textures) texture.AddItem(t);
            var pieces = BuiltinContent.Terrain.Values.OrderBy(t => t.Label).ToList();
            terrainNames = pieces.Select(t => t.Name).ToList(); terrain.Clear(); foreach (var t in pieces) terrain.AddItem(t.Label, 0);
        }
        Fill(); _onBuiltins = Fill; BuiltinContent.Changed += _onBuiltins;
        terrainSection.AddChild(Gated(Wide("Add built-in terrain (beside the table)", () => Guarded.Send(s, new AddTerrain(BuiltinContent.Place(terrainNames[terrain.Selected], Beside(SizeNow(s)), new Yaw(0))))), d => SetupLimits.CannotAddTerrain(d)));
        terrainSection.AddChild(Gated(Wide("Add my terrain…", () => Guarded.Run(() => AddLibraryTerrain(s), _screen.Toast)), d => SetupLimits.CannotAddTerrain(d)));
        var generators = new HFlowContainer(); terrainSection.AddChild(generators);
        foreach (var planet in Enum.GetValues<Planet>())
            generators.AddChild(Gated(Btn($"{planet} planet", () => Guarded.Run(async () =>
            {
                // The whole set or none: a set that would pass the cap is not sent piece by piece into refusals.
                var adds = TerrainGenerators.Generate(planet, SizeNow(s), Random.Shared);
                if (SetupLimits.CannotAddTerrain(s.Draft, adds.Count) is { } full) { _screen.Toast(full); return; }
                foreach (var add in adds) await s.SendEditAsync(add);
            }, _screen.Toast)), d => SetupLimits.CannotAddTerrain(d)));

        terrainSection.AddChild(Gated(Wide("Clear terrain", () => ClearTerrain(s)), SetupLimits.CannotClearTerrain));

        var markers = new HFlowContainer(); Section(SetupLimits.MarkersHeading).AddChild(markers);
        markers.AddChild(Gated(Btn("Add objective", () => Guarded.Send(s, new AddSetupMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Objective, Spot(s, 3), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"))))), SetupLimits.CannotAddMarker));
        markers.AddChild(Gated(Btn("Add token", () => Guarded.Send(s, new AddSetupMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Token, Spot(s, 1), new Yaw(0), 1, "token", VisualRef.OfBuiltin("chip"))))), SetupLimits.CannotAddMarker));

        var zones = new HFlowContainer(); Section(_ => "Zones").AddChild(zones);
        var style = new OptionButton(); foreach (var z in ZoneStyles.All) style.AddItem(z); zones.AddChild(style);
        zones.AddChild(Btn("Set zones", () => Guarded.Send(s, new SetZoneStyle(ZoneStyles.All[style.Selected], ZoneStyles.ShapesOf(ZoneStyles.All[style.Selected])))));
        var a = new SpinBox { MinValue = 1, MaxValue = 8, Value = 1, Prefix = "Team" }; var b = new SpinBox { MinValue = 1, MaxValue = 8, Value = 2, Prefix = "Team" }; zones.AddChild(a); zones.AddChild(b);
        zones.AddChild(Btn("Swap", () => Guarded.Send(s, new SwapZones((int)a.Value, (int)b.Value))));

        var layouts = new HFlowContainer(); Section(_ => "Layouts").AddChild(layouts);
        layouts.AddChild(Btn("Load layout…", () => Guarded.Run(() => LoadLayout(s), _screen.Toast)));
        var layoutName = new LineEdit { PlaceholderText = "layout name", MaxLength = TextLimits.Name }; layouts.AddChild(layoutName);
        void SaveLayout() => Guarded.Run(async () =>
        {
            if (TextLimits.NameProblem(layoutName.Text, "A layout name") is { } problem) { _screen.Toast(problem); layoutName.GrabFocus(); return; }
            await s.Management.SaveLayout((s.Draft ?? throw new InvalidOperationException("The table has not arrived yet.")) with { Name = layoutName.Text.Trim() });
            _screen.Toast($"Layout {layoutName.Text.Trim()} saved.");
        }, _screen.Toast);
        layoutName.TextSubmitted += _ => SaveLayout();
        layouts.AddChild(Btn("Save as", SaveLayout));

        _onBoard = () =>
        {
            if (s.Draft is { } d && !_width.HasFocus() && !_length.HasFocus()) { _width.SetValueNoSignal(d.Size.Width); _length.SetValueNoSignal(d.Size.Length); }
            RefreshLimits();
        };
        s.BoardChanged += _onBoard;
        RefreshLimits();

        // The selected piece's details and edits are the terrain card's (Hud/TerrainCard), in the unit card's slot.
        // Right-click a terrain piece or setup marker: its menu. Delete or Backspace removes the selected one, unless a text field has the key.
        AddChild(_menu = new PieceMenu(s, _screen.Toast));
        _onRight = (id, at) => { if (s.Draft is { } d) _menu.Open(MenuModel.ForSetupPiece(d, id), at); };
        _onIntent = i =>
        {
            if (i == Intent.Remove && GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit) && s.Draft is { } d && _screen.Input.Selected is { } sel
                && MenuModel.RemoveFromDraft(d, sel.PieceId) is { } remove) Guarded.Send(s, remove);
        };
        _screen.Input.PieceRightClicked += _onRight; _screen.Input.IntentPressed += _onIntent;
    }

    /// <summary>Headings that count against a cap, and add buttons disabled with the reason once the draft is at it.</summary>
    readonly List<(Button Head, Func<TableState?, string> Title)> _heads = new();
    readonly List<(Button Button, Func<TableState?, string?> Why)> _gates = new();

    void RefreshLimits()
    {
        var d = _session.Draft;
        foreach (var (head, title) in _heads) if (IsInstanceValid(head)) head.Text = $"{(head.ButtonPressed ? "▾" : "▸")} {title(d)}";
        foreach (var (button, why) in _gates)
        {
            if (!IsInstanceValid(button)) continue;
            var reason = why(d);
            button.Disabled = reason is not null; button.TooltipText = reason ?? "";
        }
    }

    Button Gated(Button button, Func<TableState?, string?> why) { _gates.Add((button, why)); return button; }

    /// <summary>A heading that shows and hides the section under it; every section starts open. The title is read from the draft.</summary>
    VBoxContainer Section(Func<TableState?, string> title)
    {
        var body = new VBoxContainer();
        var head = new Button { Text = $"▾ {title(_session.Draft)}", Flat = true, ThemeTypeVariation = Variation.FlatButton, ToggleMode = true, ButtonPressed = true, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None };
        head.Toggled += open => { body.Visible = open; head.Text = $"{(open ? "▾" : "▸")} {title(_session.Draft)}"; };
        _heads.Add((head, title));
        AddChild(head); AddChild(body); return body;
    }

    static TableSize SizeNow(Session s) => s.Draft?.Size ?? new TableSize(72, 48);
    static Point3 Beside(TableSize d) => new(d.Width + 4 + Random.Shared.Next(0, 3) * 8, 4 + Random.Shared.Next(0, 4) * 8, 0);
    /// <summary>A new marker's spot: the centre, else clear of the markers already placed (SetupLimits.FreeSpot).</summary>
    static Point3 Spot(Session s, double diameter) => SetupLimits.FreeSpot(SizeNow(s), s.Draft?.Markers ?? [], diameter);
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
    /// <summary>A button across the panel: its text wraps, so a long label never widens the side panel.</summary>
    static Button Wide(string text, Action a) { var b = Btn(text, a); b.AutowrapMode = TextServer.AutowrapMode.WordSmart; return b; }

    ConfirmationDialog Dialog(string title, OptionButton pick)
    {
        var dialog = new ConfirmationDialog { Title = title }; dialog.AddChild(pick);
        dialog.VisibilityChanged += () => { if (!dialog.Visible) dialog.QueueFree(); };
        AddChild(dialog); return dialog;
    }

    /// <summary>Every terrain piece off the table in one edit (the draft without its terrain), asked first.</summary>
    void ClearTerrain(Session s)
    {
        if (s.Draft is not { Terrain.Count: > 0 } draft) return;
        var d = new ConfirmationDialog { Title = "Clear terrain?", OkButtonText = "Clear terrain", DialogAutowrap = true,
            DialogText = $"All {draft.Terrain.Count} terrain pieces come off the table. Markers and zones stay." };
        d.Confirmed += () => { if (s.Draft is { } now) Guarded.Send(s, new ReplaceDraft(now with { Terrain = [] })); };
        d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        AddChild(d); d.PopupCentered(new Vector2I(360, 0));
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

    async Task LoadLayout(Session s)
    {
        var layouts = await s.Management.ListLayouts();
        if (!IsInstanceValid(this)) return;
        var pick = new OptionButton(); foreach (var l in layouts) pick.AddItem($"{l.Name} ({l.Size.Width}×{l.Size.Length}, {l.Terrain} terrain)");
        var dialog = Dialog("Load a layout (replaces the whole draft, size included)", pick);
        dialog.Confirmed += () => Guarded.Run(async () => await s.SendEditAsync(new ReplaceDraft(await s.Management.GetLayout(layouts[pick.Selected].Id))), _screen.Toast);
        dialog.PopupCentered();
    }
}
