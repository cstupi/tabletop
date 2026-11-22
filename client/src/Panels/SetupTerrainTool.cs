using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Client.Theming;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>
/// The Terrain tool (spec 2026-11-07-ux-redesign-1-client-shell.md → Setup, laid out by 2026-11-21-setup-screen-layout-design.md): Random table
/// as the primary tile with the planet inside it, the search, the group legend whose dots filter, the catalog as tiles four a row that take the
/// panel's height (a click adds the piece beside the table, a drag onto the table adds it where it lands), then My terrain and Clear.
/// </summary>
public partial class SetupTerrainTool : SetupToolBody
{
    LineEdit _search = null!; GridContainer _tiles = null!; HFlowContainer _legend = null!; Label _full = null!;
    IReadOnlySet<string> _hidden = new HashSet<string>();
    Action? _onBuiltins, _onTheme;
    bool? _fullShown;

    public SetupTerrainTool() { }
    public SetupTerrainTool(GameScreen screen) : base(screen) { SizeFlagsVertical = SizeFlags.ExpandFill; }

    public override bool Fills => true;

    protected override void Build()
    {
        var s = S;
        var random = new PanelContainer(); random.AddThemeStyleboxOverride("panel", PrimaryTile());
        var line = Row(); random.AddChild(line); AddChild(random);
        var planets = new OptionButton { TooltipText = "Planet", FitToLongestItem = false, CustomMinimumSize = new Vector2(120, 0) };
        foreach (var planet in Enum.GetValues<Planet>()) planets.AddItem($"{planet} planet");
        var roll = Gated(new Button { Text = "Random table", Icon = Ui.Icon("dice"), ExpandIcon = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 40) }, d => d is null ? "The table has not arrived yet." : null);
        roll.AddThemeConstantOverride("icon_max_width", 24);
        roll.Pressed += () => Guarded.Run(async () =>
        {
            // The server rolls and lays the table, balanced across the teams; the draft keeps its size, texture, markers and zones.
            var draft = s.Draft ?? throw new InvalidOperationException("The table has not arrived yet.");
            var laid = await s.Management.RandomTable(new RandomTableRequest(draft.Size, Enum.GetValues<Planet>()[planets.Selected], draft.Zones, null));
            await s.SendEditAsync(SetupEdits.Random(draft, laid));
            if (laid.Unplaced.Count > 0) Screen.Toast($"No room for: {string.Join(", ", laid.Unplaced)}.");
        }, Screen.Toast);
        line.AddChild(roll); line.AddChild(planets);

        var find = new VBoxContainer(); find.AddThemeConstantOverride("separation", Space.S); AddChild(find);
        find.AddChild(_search = new LineEdit { PlaceholderText = "Search terrain", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _search.TextChanged += _ => FillTiles();
        _legend = new HFlowContainer(); _legend.AddThemeConstantOverride("h_separation", Space.XS); _legend.AddThemeConstantOverride("v_separation", Space.XS); find.AddChild(_legend);
        FillLegend();

        var box = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve,
            SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 180) };
        _tiles = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _tiles.AddThemeConstantOverride("h_separation", Space.S); _tiles.AddThemeConstantOverride("v_separation", Space.S);
        box.AddChild(_tiles); AddChild(box);
        FillTiles();
        AddChild(_full = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });

        var more = Row(); AddChild(more);
        var mine = Gated(new Button { Text = "My terrain…", Icon = Ui.Icon("add"), SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left }, d => SetupLimits.CannotAddTerrain(d));
        mine.AddThemeConstantOverride("icon_max_width", 18);
        mine.Pressed += () => Guarded.Run(() => AddLibraryTerrain(s), Screen.Toast); more.AddChild(mine);
        var clear = Gated(new Button { Text = "Clear", Icon = Ui.Icon("remove") }, SetupLimits.CannotClearTerrain);
        clear.AddThemeConstantOverride("icon_max_width", 18); clear.AddThemeColorOverride("icon_normal_color", Ui.Failure);
        clear.Pressed += () => ClearTerrain(s); more.AddChild(clear);

        _onBuiltins = FillTiles; BuiltinContent.Changed += _onBuiltins;
        _onTheme = () => { FillLegend(); FillTiles(); }; Ui.Changed += _onTheme;
    }

    protected override void Gone() { BuiltinContent.Changed -= _onBuiltins; Ui.Changed -= _onTheme; }

    protected override void Refresh()
    {
        var full = SetupLimits.CannotAddTerrain(S.Draft);
        _full.Text = full ?? ""; _full.Visible = full is not null && S.Draft is not null;
        if (_fullShown != (full is not null)) { _fullShown = full is not null; FillTiles(); }
    }

    /// <summary>The six groups as a legend of colour dots; a dot shows or hides its group's tiles.</summary>
    void FillLegend()
    {
        if (!IsInstanceValid(_legend)) return;
        foreach (var c in _legend.GetChildren()) { _legend.RemoveChild(c); c.QueueFree(); }
        foreach (var kind in TerrainTiles.Legend)
        {
            var shown = !_hidden.Contains(kind.Key);
            var dot = new Button { Text = kind.Legend, Icon = Ui.Icon(kind.Key), Flat = true, ThemeTypeVariation = Variation.FlatButton, ToggleMode = true, ButtonPressed = shown,
                FocusMode = FocusModeEnum.None, TooltipText = shown ? $"Hide {kind.Legend}" : $"Show {kind.Legend}", Modulate = shown ? Ui.Text with { A = 1 } : Ui.Faded };
            dot.AddThemeConstantOverride("icon_max_width", 14); dot.AddThemeFontSizeOverride("font_size", 13);
            foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" }) dot.AddThemeColorOverride(state, Ui.Of(kind.Colour));
            var key = kind.Key;
            dot.Toggled += _ => { _hidden = TerrainTiles.Toggle(_hidden, key); Callable.From(() => { FillLegend(); FillTiles(); }).CallDeferred(); };
            _legend.AddChild(dot);
        }
    }

    void FillTiles()
    {
        if (!IsInstanceValid(_tiles)) return;
        foreach (var c in _tiles.GetChildren()) { _tiles.RemoveChild(c); c.QueueFree(); }
        var tiles = TerrainTiles.Of(BuiltinContent.Terrain.Values, _search.Text, _hidden, S.TerrainWords);
        _tiles.Columns = tiles.Count == 0 ? 1 : 4;
        if (tiles.Count == 0) { _tiles.AddChild(new Label { Text = "No terrain matches", ThemeTypeVariation = Variation.Dim }); return; }
        foreach (var tile in tiles)
        {
            // A tile of one height: the group's icon over the name, two lines at most.
            var button = new TerrainTileButton(tile.Entry.Name, tile.Entry.Label, Ui.Icon(tile.Kind.Key))
            {
                CustomMinimumSize = new Vector2(74, 80), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = $"{tile.Entry.Label}: {tile.Summary}\nClick: beside the table · Drag: onto the table",
                Disabled = SetupLimits.CannotAddTerrain(S.Draft) is not null,
            };
            var face = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            face.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, Space.XS);
            face.AddThemeConstantOverride("separation", Space.XS);
            var glyph = Icon(tile.Kind.Key, Ui.Of(tile.Kind.Colour), 28); glyph.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; face.AddChild(glyph);
            var label = new Label { Text = tile.Entry.Label, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(60, 0) };
            label.AddThemeFontSizeOverride("font_size", 12); face.AddChild(label);
            button.AddChild(face);
            var name = tile.Entry.Name;
            button.Pressed += () => { if (SetupLimits.CannotAddTerrain(S.Draft) is null) Guarded.Send(S, SetupEdits.AddBuiltin(name, SizeNow(S), Random.Shared.NextDouble)); };
            _tiles.AddChild(button);
        }
    }

    /// <summary>A tile dropped on the table at this point (GameScreen forwards the table's drop): the piece where it lands.</summary>
    public void DropTile(string name, Point2 at)
    {
        if (S.Draft is not { } d || SetupLimits.CannotAddTerrain(d) is not null) { Screen.Toast(SetupLimits.CannotAddTerrain(S.Draft) ?? "The table has not arrived yet."); return; }
        Guarded.Send(S, SetupEdits.DropBuiltin(name, d.Terrain, at));
    }

    /// <summary>The tool's primary tile: a faint accent panel with an accent edge.</summary>
    static StyleBoxFlat PrimaryTile()
    {
        var b = new StyleBoxFlat { BgColor = Ui.Accent with { A = 0.12f }, BorderColor = Ui.Accent with { A = 0.7f } };
        b.SetBorderWidthAll(1); b.BorderWidthLeft = 4; b.SetCornerRadiusAll(Math.Min(Ui.Current.Shape.CornerRadius, 6)); b.SetContentMarginAll(Space.S);
        return b;
    }

    /// <summary>Every terrain piece off the table in one edit (the draft without its terrain), asked first.</summary>
    void ClearTerrain(Session s)
    {
        if (s.Draft is not { Terrain.Count: > 0 } draft) return;
        Confirm("Clear terrain?", "Clear terrain", $"All {draft.Terrain.Count} terrain pieces come off the table. Objectives and zones stay.",
            () => { if (s.Draft is { } now) Guarded.Send(s, SetupEdits.ClearTerrain(now)); });
    }

    async Task AddLibraryTerrain(Session s)
    {
        var assets = (await s.Management.ListAssets(AssetKind.Terrain, null)).Items.Where(x => x.Uploaded).ToList();
        if (!IsInstanceValid(this)) return;
        if (assets.Count == 0) { Screen.Toast("No terrain in your library; upload some in the web app."); return; }
        PieceMenu.AskChoice(this, "Add terrain from my library", assets.Select(x => x.Name).ToList(), picked => Guarded.Run(async () =>
        {
            // Scale 1: MeshLoader.Orient already applies the asset's scale to the mesh, and its volumes are table-frame after scale.
            var asset = assets.First(x => x.Name == picked); var volumes = await s.Management.GetAssetVolumes(asset.Id);
            await s.SendEditAsync(SetupEdits.AddAsset(asset.Id, volumes.Select(v => v.Volume).ToList(), SizeNow(s), Random.Shared.NextDouble));
        }, Screen.Toast));
    }
}

/// <summary>A terrain tile: a click adds the piece beside the table; dragged, it carries the catalog name to the table's drop.</summary>
public partial class TerrainTileButton : Button
{
    public const string DragPrefix = "terrain-tile:";
    readonly string _name = "", _label = ""; readonly Texture2D? _glyph;
    public TerrainTileButton() { }
    public TerrainTileButton(string name, string label, Texture2D? glyph) { _name = name; _label = label; _glyph = glyph; }

    public override Variant _GetDragData(Vector2 at)
    {
        if (Disabled || _name.Length == 0) return default;
        var preview = new Button { Text = _label, Icon = _glyph, ExpandIcon = false, Modulate = Ui.Faded };
        preview.AddThemeConstantOverride("icon_max_width", 24);
        SetDragPreview(preview);
        return DragPrefix + _name;
    }
}
