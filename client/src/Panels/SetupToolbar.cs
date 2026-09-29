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
    Session _session = null!; VBoxContainer _selected = null!;
    Action? _onSelection;
    public SetupToolbar(GameScreen screen) => _screen = screen;

    public override void _ExitTree()
    {
        if (_session is null) return;
        _screen.Input.SelectionChanged -= _onSelection; _session.BoardChanged -= _onSelection;
    }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session; var size0 = SizeNow(s);   // the board may not have arrived yet
        AddChild(new Label { Text = "Setup" });

        var size = new HBoxContainer(); AddChild(size);
        var width = new SpinBox { MinValue = 12, MaxValue = 144, Value = size0.Width, Suffix = "\"" }; var length = new SpinBox { MinValue = 12, MaxValue = 144, Value = size0.Length, Suffix = "\"" };
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
        _screen.Input.SelectionChanged += _onSelection; s.BoardChanged += _onSelection;   // a draft edit changes what the selected piece shows
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
            var asset = assets[pick.Selected]; var volumes = await s.Management.GetAssetVolumes(asset.Id);
            await s.SendEditAsync(new AddTerrain(new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfAsset(asset.Id), Beside(SizeNow(s)), new Yaw(0), asset.Scale, volumes.Select(v => new TerrainVolume(Guid.CreateVersion7(), v.Volume)).ToList())));
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
