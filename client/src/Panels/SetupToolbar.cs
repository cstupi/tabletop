using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Client.Theming;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>
/// The closed set of setup edits, one control each, in folding sections (spec 2026-10-27-godot-setup-screen-design.md): Terrain (open:
/// the catalog searched and grouped, Random table, my terrain, Clear terrain), Markers, Table, Deployment zones and Layouts. Anyone may use
/// every one of them; the server applies in arrival order.
/// </summary>
public partial class SetupToolbar : VBoxContainer
{
    readonly GameScreen _screen; readonly SetupFolds _folds;
    Session _session = null!; SpinBox _width = null!, _length = null!; PieceMenu _menu = null!; ItemList _terrain = null!; LineEdit _search = null!;
    readonly List<BuiltinTerrain?> _rows = new();   // the list's rows: a piece, or null for a group's title
    readonly List<Fold> _foldsShown = new();
    Action? _onBoard, _onBuiltins; Action<Guid, Vector2>? _onRight; Action<Intent>? _onIntent;
    public SetupToolbar(GameScreen screen, SetupFolds folds) { _screen = screen; _folds = folds; }

    public override void _ExitTree()
    {
        if (_session is null) return;
        _session.BoardChanged -= _onBoard;
        _screen.Input.PieceRightClicked -= _onRight; _screen.Input.IntentPressed -= _onIntent;
        BuiltinContent.Changed -= _onBuiltins;
    }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session;
        var sections = SetupFolds.Order(GameStatus.Setup).Where(f => f is not (SetupFold.Game or SetupFold.Players))
            .ToDictionary(f => f, f => Add(new Fold(_folds, f, GameStatus.Setup, () => SetupFolds.Title(f, s.Draft, s.Game))));
        var showTexture = BuildTable(s, sections[SetupFold.Table].Body);
        BuildTerrain(s, sections[SetupFold.Terrain].Body);
        BuildMarkers(s, sections[SetupFold.Markers].Body);
        BuildZones(s, sections[SetupFold.Zones].Body);
        BuildLayouts(s, sections[SetupFold.Layouts].Body);

        _onBoard = () =>
        {
            if (s.Draft is { } d && !_width.HasFocus() && !_length.HasFocus()) { _width.SetValueNoSignal(d.Size.Width); _length.SetValueNoSignal(d.Size.Length); }
            showTexture();
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

    Fold Add(Fold fold) { AddChild(fold); _foldsShown.Add(fold); return fold; }

    /// <summary>The terrain list's height for this window (SetupLayout), set again when the window changes size.</summary>
    public void FitList(float windowHeight) { if (IsInstanceValid(_terrain)) _terrain.CustomMinimumSize = new Vector2(0, (float)SetupLayout.TerrainListHeight(windowHeight)); }

    void BuildTerrain(Session s, VBoxContainer body)
    {
        body.AddChild(_search = new LineEdit { PlaceholderText = "Search terrain", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        body.AddChild(_terrain = new ItemList { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutoHeight = false, SameColumnWidth = true });
        FitList(GetViewport().GetVisibleRect().Size.Y);
        _search.TextChanged += _ => FillTerrain();
        // A click on a piece adds it beside the table; a group's title does nothing.
        _terrain.ItemClicked += (index, _, button) =>
        {
            _terrain.DeselectAll();
            if (button != (long)MouseButton.Left || _rows[(int)index] is not { } entry || SetupLimits.CannotAddTerrain(s.Draft) is not null) return;
            Guarded.Send(s, SetupEdits.AddBuiltin(entry.Name, SizeNow(s), Random.Shared.NextDouble));
        };
        _onBuiltins = () => { FillTerrain(); _fillTextures?.Invoke(); };
        BuiltinContent.Changed += _onBuiltins;
        FillTerrain();
        body.AddChild(_terrainFull = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });

        var generators = new HFlowContainer(); body.AddChild(generators);
        var planets = new OptionButton(); foreach (var planet in Enum.GetValues<Planet>()) planets.AddItem($"{planet} planet"); generators.AddChild(planets);
        generators.AddChild(Gated(Btn("Random table", () => Guarded.Run(async () =>
        {
            // The server rolls and lays the table, balanced across the teams; the draft keeps its size, texture, markers and zones.
            var draft = s.Draft ?? throw new InvalidOperationException("The table has not arrived yet.");
            var laid = await s.Management.RandomTable(new RandomTableRequest(draft.Size, Enum.GetValues<Planet>()[planets.Selected], draft.Zones, null));
            await s.SendEditAsync(SetupEdits.Random(draft, laid));
            if (laid.Unplaced.Count > 0) _screen.Toast($"No room for: {string.Join(", ", laid.Unplaced)}.");
        }, _screen.Toast)), d => d is null ? "The table has not arrived yet." : null));
        var more = new HFlowContainer(); body.AddChild(more);
        more.AddChild(Gated(Btn("Add my terrain…", () => Guarded.Run(() => AddLibraryTerrain(s), _screen.Toast)), d => SetupLimits.CannotAddTerrain(d)));
        more.AddChild(Gated(Btn("Clear terrain", () => ClearTerrain(s)), SetupLimits.CannotClearTerrain));
        body.AddChild(new Label { Text = "Q/E turn, Del removes, right-click for more.", ThemeTypeVariation = Variation.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    Label _terrainFull = null!; Action? _fillTextures;

    void FillTerrain()
    {
        if (!IsInstanceValid(_terrain)) return;
        _terrain.Clear(); _rows.Clear();
        var groups = TerrainGroups.Of(BuiltinContent.Terrain.Values, _search.Text);
        foreach (var group in groups)
        {
            var title = _terrain.AddItem(group.Title, null, false);
            _terrain.SetItemDisabled(title, true); _terrain.SetItemTooltipEnabled(title, false); _rows.Add(null);
            foreach (var entry in group.Entries)
            {
                var row = _terrain.AddItem($"   {entry.Label}");
                _terrain.SetItemTooltip(row, $"Add {entry.Label} beside the table: {TerrainGroups.Summary(entry)}"); _rows.Add(entry);
            }
        }
        if (groups.Count == 0) { var none = _terrain.AddItem($"No terrain matches “{_search.Text.Trim()}”.", null, false); _terrain.SetItemDisabled(none, true); _rows.Add(null); }
    }

    /// <summary>Size and surface; returns what shows the board's print in the dropdown.</summary>
    Action BuildTable(Session s, VBoxContainer body)
    {
        var size0 = SizeNow(s);   // the board may not have arrived yet
        body.AddChild(new Label { Text = SetupLimits.SizeHint, ThemeTypeVariation = Variation.Dim });
        var size = new HFlowContainer(); body.AddChild(size);
        var width = _width = new SpinBox { MinValue = SetupLimits.MinEdge, MaxValue = SetupLimits.MaxEdge, Value = size0.Width, Suffix = "\"" }; var length = _length = new SpinBox { MinValue = SetupLimits.MinEdge, MaxValue = SetupLimits.MaxEdge, Value = size0.Length, Suffix = "\"" };
        size.AddChild(width); size.AddChild(length);
        size.AddChild(Btn("Set size", () => Guarded.Send(s, SetupEdits.TableSize(width.Value, length.Value))));
        // The list follows the built-in catalog in use, refilled when a release brings a new one.
        List<string> textures = [];
        var texture = new OptionButton(); body.AddChild(texture);
        texture.ItemSelected += i => Guarded.Send(s, SetupEdits.Texture(textures[(int)i]));
        // The dropdown names the board's print: a rejoin or another player's change shows what the table wears.
        void ShowTexture() { if (s.Draft?.Texture.Builtin is { } name && textures.IndexOf(name) is var at and >= 0 && texture.Selected != at) texture.Select(at); }
        _fillTextures = () => { textures = [.. BuiltinContent.Textures]; texture.Clear(); foreach (var t in textures) texture.AddItem(SetupWords.Texture(t)); ShowTexture(); };
        _fillTextures();
        return ShowTexture;
    }

    void BuildMarkers(Session s, VBoxContainer body)
    {
        var markers = new HFlowContainer(); body.AddChild(markers);
        markers.AddChild(Gated(Btn("Add objective", () => { if (s.Draft is { } d) Guarded.Send(s, SetupEdits.Objective(d)); }), SetupLimits.CannotAddMarker));
        markers.AddChild(Gated(Btn("Add token", () => { if (s.Draft is { } d) Guarded.Send(s, SetupEdits.Token(d)); }), SetupLimits.CannotAddMarker));
    }

    static void BuildZones(Session s, VBoxContainer body)
    {
        var zones = new HFlowContainer(); body.AddChild(zones);
        var style = new OptionButton(); foreach (var z in ZoneStyles.All) style.AddItem(SetupWords.ZoneStyle(z)); zones.AddChild(style);
        zones.AddChild(Btn("Set zones", () => Guarded.Send(s, SetupEdits.Zones(ZoneStyles.All[style.Selected]))));
        var swap = new HFlowContainer(); body.AddChild(swap);
        var a = new SpinBox { MinValue = 1, MaxValue = 8, Value = 1, Prefix = "Team" }; var b = new SpinBox { MinValue = 1, MaxValue = 8, Value = 2, Prefix = "Team" }; swap.AddChild(a); swap.AddChild(b);
        swap.AddChild(Btn("Swap zones", () => Guarded.Send(s, SetupEdits.SwapZones((int)a.Value, (int)b.Value))));
    }

    void BuildLayouts(Session s, VBoxContainer body)
    {
        var layouts = new HFlowContainer(); body.AddChild(layouts);
        layouts.AddChild(Btn("Load layout…", () => Guarded.Run(() => LoadLayout(s), _screen.Toast)));
        var layoutName = new LineEdit { PlaceholderText = "Layout name", MaxLength = TextLimits.Name, CustomMinimumSize = new Vector2(140, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }; layouts.AddChild(layoutName);   // the placeholder whole
        void SaveLayout() => Guarded.Run(async () =>
        {
            if (TextLimits.NameProblem(layoutName.Text, "A layout name") is { } problem) { _screen.Toast(problem); layoutName.GrabFocus(); return; }
            await s.Management.SaveLayout((s.Draft ?? throw new InvalidOperationException("The table has not arrived yet.")) with { Name = layoutName.Text.Trim() });
            _screen.Toast($"Layout {layoutName.Text.Trim()} saved.");
        }, _screen.Toast);
        layoutName.TextSubmitted += _ => SaveLayout();
        layouts.AddChild(Btn("Save as", SaveLayout));
    }

    /// <summary>Add buttons disabled with the reason once the draft is at a cap, and the headings' counts.</summary>
    readonly List<(Button Button, Func<TableState?, string?> Why)> _gates = new();

    void RefreshLimits()
    {
        var d = _session.Draft;
        foreach (var fold in _foldsShown) fold.Refresh();
        foreach (var (button, why) in _gates)
        {
            if (!IsInstanceValid(button)) continue;
            var reason = why(d);
            button.Disabled = reason is not null; button.TooltipText = reason ?? "";
        }
        if (IsInstanceValid(_terrainFull)) { var full = SetupLimits.CannotAddTerrain(d); _terrainFull.Text = full ?? ""; _terrainFull.Visible = full is not null && d is not null; }
    }

    Button Gated(Button button, Func<TableState?, string?> why) { _gates.Add((button, why)); return button; }

    static TableSize SizeNow(Session s) => s.Draft?.Size ?? new TableSize(72, 48);
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }

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
        d.Confirmed += () => { if (s.Draft is { } now) Guarded.Send(s, SetupEdits.ClearTerrain(now)); };
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
            await s.SendEditAsync(SetupEdits.AddAsset(asset.Id, volumes.Select(v => v.Volume).ToList(), SizeNow(s), Random.Shared.NextDouble));
        }, _screen.Toast);
        dialog.PopupCentered();
    }

    async Task LoadLayout(Session s)
    {
        var (builtin, saved) = (s.Management.BuiltinLayouts(), s.Management.ListLayouts());
        var choices = LayoutChoices.Of(await builtin, await saved);
        if (!IsInstanceValid(this)) return;
        var pick = new OptionButton(); foreach (var c in choices) pick.AddItem(c.Label);
        var dialog = Dialog("Load a layout (replaces the whole draft, size included)", pick);
        dialog.Confirmed += () => Guarded.Run(async () =>
        {
            var choice = choices[pick.Selected];
            await s.SendEditAsync(SetupEdits.Load(choice.Builtin ?? await s.Management.GetLayout(choice.SavedId!.Value)));
        }, _screen.Toast);
        dialog.PopupCentered();
    }
}
