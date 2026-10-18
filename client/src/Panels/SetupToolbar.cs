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
    Session _session = null!; VBoxContainer _selected = null!; Label _selectedHead = null!; SpinBox _width = null!, _length = null!; PieceMenu _menu = null!;
    Action? _onSelection, _onBoard, _onBuiltins; Action<Guid, Vector2>? _onRight; Action<Intent>? _onIntent;
    public SetupToolbar(GameScreen screen) => _screen = screen;

    public override void _ExitTree()
    {
        if (_session is null) return;
        _screen.Input.SelectionChanged -= _onSelection; _session.BoardChanged -= _onBoard;
        _screen.Input.PieceRightClicked -= _onRight; _screen.Input.IntentPressed -= _onIntent;
        BuiltinContent.Changed -= _onBuiltins;
    }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session; var size0 = SizeNow(s);   // the board may not have arrived yet
        AddChild(new Label { Text = "Setup" });

        // First, so a click on the table shows the piece's controls without scrolling.
        AddChild(_selectedHead = new Label { Text = "Selected placement", Visible = false });
        _selected = new VBoxContainer(); AddChild(_selected);

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

        _onSelection = () => RenderSelected(s, _selected);
        _onBoard = () =>
        {
            if (s.Draft is { } d && !_width.HasFocus() && !_length.HasFocus()) { _width.SetValueNoSignal(d.Size.Width); _length.SetValueNoSignal(d.Size.Length); }
            RenderSelected(s, _selected);
            RefreshLimits();
        };
        _screen.Input.SelectionChanged += _onSelection; s.BoardChanged += _onBoard;   // a draft edit changes what the selected piece shows
        RenderSelected(s, _selected);
        RefreshLimits();

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
        foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
        var sel = _screen.Input.Selected; var draft = s.Draft;
        _selectedHead.Visible = false;                                              // the heading only over something to show
        if (sel is null || draft is null) return;
        _selectedHead.Visible = draft.Markers.Any(m => m.Id == sel.PieceId) || draft.Terrain.Any(x => x.Id == sel.PieceId);
        if (draft.Markers.FirstOrDefault(m => m.Id == sel.PieceId) is { } marker) { box.AddChild(Btn("Remove marker", () => Guarded.Send(s, new RemoveSetupMarker(marker.Id)))); return; }
        var t = draft.Terrain.FirstOrDefault(x => x.Id == sel.PieceId); if (t is null) return;
        box.AddChild(new Label { Text = t.Mesh.IsBuiltin ? BuiltinContent.Terrain.GetValueOrDefault(t.Mesh.Builtin!)?.Label ?? t.Mesh.Builtin : $"asset {Names.Short(t.Mesh.AssetId!.Value)}" });
        var scale = new SpinBox { MinValue = 0.25, MaxValue = 4, Step = 0.25, Value = t.Scale }; box.AddChild(scale); scale.ValueChanged += v => Guarded.Send(s, new ScaleTerrain(t.Id, v));
        box.AddChild(Wide("Remove", () => Guarded.Send(s, new RemoveTerrain(t.Id))));
        var swap = new OptionButton(); var names = BuiltinContent.Terrain.Keys.Order().ToList(); foreach (var n in names) swap.AddItem(n); box.AddChild(swap);
        box.AddChild(Wide("Swap mesh for this built-in (keeps volumes)", () => Guarded.Send(s, new SwapTerrainMesh(t.Id, VisualRef.OfBuiltin(names[swap.Selected])))));
        box.AddChild(Wide("Swap mesh for one of my assets (keeps volumes)", () => Guarded.Run(() => SwapForAsset(s, t.Id), _screen.Toast)));
        box.AddChild(Wide("Reset volumes to the source's", () => Guarded.Run(async () =>
            await s.SendEditAsync(new OverrideTerrainVolumes(t.Id, t.Mesh.IsBuiltin ? BuiltinContent.VolumesOf(t.Mesh.Builtin!) : (await s.Management.GetAssetVolumes(t.Mesh.AssetId!.Value)).Select(v => new TerrainVolume(Guid.CreateVersion7(), v.Volume)).ToList())), _screen.Toast)));
        foreach (var (tv, i) in t.Volumes.Select((v, i) => (v, i)))
        {
            var row = new VBoxContainer(); box.AddChild(row);
            row.AddChild(new Label { Text = $"volume {i + 1}" });
            var shape = new HFlowContainer(); row.AddChild(shape);
            var kind = new OptionButton(); foreach (var k in Enum.GetNames<VolumeKind>()) kind.AddItem(k); kind.Select((int)tv.Volume.Kind); shape.AddChild(kind);
            kind.ItemSelected += k => EditVolume(s, t, tv.Id, v => v with { Kind = (VolumeKind)k });
            var fp = tv.Volume.Footprint;
            shape.AddChild(new Label { Text = fp.Kind == FootprintKind.Circle ? "radius" : "w×l" });
            shape.AddChild(Spin(fp.A, 144, x => EditVolume(s, t, tv.Id, v => v with { Footprint = v.Footprint.Kind == FootprintKind.Circle ? new Footprint(FootprintKind.Circle, x, x) : v.Footprint with { A = x } })));
            if (fp.Kind == FootprintKind.Rect) shape.AddChild(Spin(fp.B, 144, x => EditVolume(s, t, tv.Id, v => v with { Footprint = v.Footprint with { B = x } })));
            var heights = new HFlowContainer(); row.AddChild(heights);
            heights.AddChild(new Label { Text = "from" }); heights.AddChild(Spin(tv.Volume.Bottom, 60, x => EditVolume(s, t, tv.Id, v => v with { Bottom = x })));
            heights.AddChild(new Label { Text = "to" }); heights.AddChild(Spin(tv.Volume.Top, 60, x => EditVolume(s, t, tv.Id, v => v with { Top = x })));
            var flags = new HFlowContainer(); row.AddChild(flags);
            foreach (var flag in new[] { TerrainDeclarations.Impassable, TerrainDeclarations.BlocksLineOfSight, TerrainDeclarations.SeeInAndOut, TerrainDeclarations.Cover, TerrainDeclarations.Difficult, TerrainDeclarations.Dangerous })
            {
                var cb = new CheckBox { Text = flag.ToString(), ButtonPressed = tv.Volume.Declarations.HasFlag(flag) };
                cb.Toggled += on => EditVolume(s, t, tv.Id, v => v with { Declarations = on ? v.Declarations | flag : v.Declarations & ~flag });
                flags.AddChild(cb);
            }
            var notes = new LineEdit { Text = tv.Volume.Notes ?? "", PlaceholderText = $"notes, Enter to save (at most {TextLimits.Notes} characters)", MaxLength = TextLimits.Notes }; row.AddChild(notes);
            notes.TextSubmitted += text => EditVolume(s, t, tv.Id, v => v with { Notes = text.Length == 0 ? null : text });
        }
    }
}
