using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Client.Theming;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>
/// The setup side panel's body (spec 2026-11-07-ux-redesign-1-client-shell.md → Setup), folding sections in <see cref="SetupFolds.Order"/>:
/// Terrain (Random table as the primary tile with the planet inside it, the search, the group legend whose dots filter, the catalog as tiles
/// that add beside the table on a click or where they land when dragged onto the table, My terrain and Clear), Objectives (a row per marker),
/// Table (size and the board prints as swatches), Zones (a diagram in the team colours, the styles as tiles, ⇄ swap) and Layouts. Anyone may
/// use every control; the server applies in arrival order.
/// </summary>
public partial class SetupToolbar : VBoxContainer
{
    readonly GameScreen _screen; readonly SetupFolds _folds;
    Session _session = null!; SpinBox _width = null!, _length = null!; PieceMenu _menu = null!; LineEdit _search = null!;
    ScrollContainer _tileBox = null!; GridContainer _tiles = null!; HFlowContainer _legend = null!; VBoxContainer _objectives = null!, _layouts = null!;
    HFlowContainer _swatches = null!, _styles = null!; ZoneDiagramControl _diagram = null!; Control _swap = null!;
    IReadOnlySet<string> _hidden = new HashSet<string>();
    readonly List<Fold> _foldsShown = new();
    Action? _onBoard, _onBuiltins, _onTheme; Action<Guid, Vector2>? _onRight; Action<Intent>? _onIntent;
    public SetupToolbar(GameScreen screen, SetupFolds folds) { _screen = screen; _folds = folds; }

    public override void _ExitTree()
    {
        if (_session is null) return;
        _session.BoardChanged -= _onBoard; _session.GameChanged -= _onBoard;
        _screen.Input.PieceRightClicked -= _onRight; _screen.Input.IntentPressed -= _onIntent;
        BuiltinContent.Changed -= _onBuiltins; Ui.Changed -= _onTheme;
    }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session;
        AddThemeConstantOverride("separation", 10);
        var sections = SetupFolds.Order(GameStatus.Setup).Where(f => f is not SetupFold.Game)
            .ToDictionary(f => f, f => Add(new Fold(_folds, f, GameStatus.Setup, () => SetupFolds.Title(f, s.Draft, s.Game))));
        BuildTerrain(s, sections[SetupFold.Terrain].Body);
        BuildObjectives(s, sections[SetupFold.Objectives]);
        BuildTable(s, sections[SetupFold.Table].Body);
        BuildZones(s, sections[SetupFold.Zones].Body);
        BuildLayouts(s, sections[SetupFold.Layouts]);

        _onBoard = () =>
        {
            if (s.Draft is { } d && !_width.HasFocus() && !_length.HasFocus()) { _width.SetValueNoSignal(d.Size.Width); _length.SetValueNoSignal(d.Size.Length); }
            ShowObjectives(); ShowSwatches(); ShowZones();
            RefreshLimits();
        };
        s.BoardChanged += _onBoard; s.GameChanged += _onBoard;   // the diagram's ⇄ follows the teams
        _onBuiltins = () => { FillTiles(); FillSwatches(); };
        BuiltinContent.Changed += _onBuiltins;
        _onTheme = () => { FillLegend(); FillTiles(); ShowZones(); };
        Ui.Changed += _onTheme;
        _onBoard();

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

    /// <summary>The tile box's height for this window and head (SetupLayout), set again when either changes size.</summary>
    public void FitList(float windowHeight, float headHeight) { if (IsInstanceValid(_tileBox)) _tileBox.CustomMinimumSize = new Vector2(0, (float)SetupLayout.TerrainListHeight(windowHeight, headHeight)); }

    // ---- Terrain ----

    void BuildTerrain(Session s, VBoxContainer body)
    {
        // The section's primary: Random table, the planet picker inside it.
        var random = new PanelContainer(); random.AddThemeStyleboxOverride("panel", PrimaryTile());
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 8); random.AddChild(line); body.AddChild(random);
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
            if (laid.Unplaced.Count > 0) _screen.Toast($"No room for: {string.Join(", ", laid.Unplaced)}.");
        }, _screen.Toast);
        line.AddChild(roll); line.AddChild(planets);

        body.AddChild(_search = new LineEdit { PlaceholderText = "Search terrain", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _search.TextChanged += _ => FillTiles();
        _legend = new HFlowContainer(); _legend.AddThemeConstantOverride("h_separation", 2); _legend.AddThemeConstantOverride("v_separation", 0); body.AddChild(_legend);
        FillLegend();

        _tileBox = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve };
        _tiles = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _tiles.AddThemeConstantOverride("h_separation", 6); _tiles.AddThemeConstantOverride("v_separation", 6);
        _tileBox.AddChild(_tiles); body.AddChild(_tileBox);
        FitList(GetViewport().GetVisibleRect().Size.Y, 110);
        FillTiles();
        body.AddChild(_terrainFull = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });

        var more = new HBoxContainer(); more.AddThemeConstantOverride("separation", 8); body.AddChild(more);
        var mine = Gated(new Button { Text = "My terrain…", Icon = Ui.Icon("add"), SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left }, d => SetupLimits.CannotAddTerrain(d));
        mine.AddThemeConstantOverride("icon_max_width", 18);
        mine.Pressed += () => Guarded.Run(() => AddLibraryTerrain(s), _screen.Toast); more.AddChild(mine);
        var clear = Gated(new Button { Text = "Clear", Icon = Ui.Icon("remove") }, SetupLimits.CannotClearTerrain);
        clear.AddThemeConstantOverride("icon_max_width", 18); clear.AddThemeColorOverride("icon_normal_color", Ui.Failure);
        clear.Pressed += () => ClearTerrain(s); more.AddChild(clear);
    }

    Label _terrainFull = null!;

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
        var tiles = TerrainTiles.Of(BuiltinContent.Terrain.Values, _search.Text, _hidden, _session.TerrainWords);
        _tiles.Columns = tiles.Count == 0 ? 1 : 4;
        if (tiles.Count == 0) { _tiles.AddChild(new Label { Text = $"No terrain matches", ThemeTypeVariation = Variation.Dim }); return; }
        foreach (var tile in tiles)
        {
            // A tile of one height: the group's icon over the name, two lines at most.
            var button = new TerrainTileButton(tile.Entry.Name, tile.Entry.Label, Ui.Icon(tile.Kind.Key))
            {
                CustomMinimumSize = new Vector2(74, 80), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = $"{tile.Entry.Label}: {tile.Summary}\nClick: beside the table · Drag: onto the table",
                Disabled = SetupLimits.CannotAddTerrain(_session.Draft) is not null,
            };
            var face = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            face.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 6);
            face.AddThemeConstantOverride("separation", 4);
            var glyph = Icon(tile.Kind.Key, Ui.Of(tile.Kind.Colour), 28); glyph.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; face.AddChild(glyph);
            var label = new Label { Text = tile.Entry.Label, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(60, 0) };
            label.AddThemeFontSizeOverride("font_size", 12); face.AddChild(label);
            button.AddChild(face);
            var name = tile.Entry.Name;
            button.Pressed += () => { if (SetupLimits.CannotAddTerrain(_session.Draft) is null) Guarded.Send(_session, SetupEdits.AddBuiltin(name, SizeNow(_session), Random.Shared.NextDouble)); };
            _tiles.AddChild(button);
        }
    }

    /// <summary>A tile dropped on the table at this point (GameScreen forwards the table's drop): the piece where it lands.</summary>
    public void DropTile(string name, Point2 at)
    {
        if (_session.Draft is not { } d || SetupLimits.CannotAddTerrain(d) is { } full) { _screen.Toast(SetupLimits.CannotAddTerrain(_session.Draft) ?? "The table has not arrived yet."); return; }
        Guarded.Send(_session, SetupEdits.DropBuiltin(name, d.Terrain, at));
    }

    // ---- Objectives ----

    void BuildObjectives(Session s, Fold fold)
    {
        var add = Gated(new Button { Text = "Objective", Icon = Ui.Icon("add"), TooltipText = "Add an objective beside the others" }, SetupLimits.CannotAddMarker);
        add.AddThemeConstantOverride("icon_max_width", 16);
        add.Pressed += () => { if (s.Draft is { } d) Guarded.Send(s, SetupEdits.Objective(d)); };
        fold.Actions.AddChild(add);
        fold.Body.AddChild(_objectives = new VBoxContainer());
        _objectives.AddThemeConstantOverride("separation", 2);
    }

    IReadOnlyList<ObjectiveRow>? _objectivesShown;

    void ShowObjectives()
    {
        var rows = ObjectiveRows.Of(_session.Draft);
        if (_objectivesShown is { } was && was.SequenceEqual(rows)) return;
        _objectivesShown = rows;
        foreach (var c in _objectives.GetChildren()) { _objectives.RemoveChild(c); c.QueueFree(); }
        if (rows.Count == 0) { _objectives.AddChild(new Label { Text = "No objectives", ThemeTypeVariation = Variation.Dim }); return; }
        foreach (var row in rows)
        {
            var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 8); _objectives.AddChild(line);
            line.AddChild(Icon("objective", Ui.Accent, 16));
            line.AddChild(new Label { Text = row.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
            line.AddChild(new Label { Text = row.Position, ThemeTypeVariation = Variation.Dim });
            var menu = new MenuButton { Text = "▸", Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None, TooltipText = row.Label };
            menu.GetPopup().AddItem("Remove", 0);
            var id = row.Id;
            menu.GetPopup().IdPressed += _ => Guarded.Send(_session, SetupEdits.RemoveMarker(id));
            line.AddChild(menu);
        }
    }

    // ---- Table ----

    void BuildTable(Session s, VBoxContainer body)
    {
        var size0 = SizeNow(s);   // the board may not have arrived yet
        var size = new HBoxContainer(); size.AddThemeConstantOverride("separation", 6); body.AddChild(size);
        _width = new SpinBox { MinValue = SetupLimits.MinEdge, MaxValue = SetupLimits.MaxEdge, Value = size0.Width, Suffix = "\"", TooltipText = SetupLimits.SizeHint, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _length = new SpinBox { MinValue = SetupLimits.MinEdge, MaxValue = SetupLimits.MaxEdge, Value = size0.Length, Suffix = "\"", TooltipText = SetupLimits.SizeHint, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        size.AddChild(_width); size.AddChild(new Label { Text = "×", ThemeTypeVariation = Variation.Dim }); size.AddChild(_length);
        size.AddChild(Btn("Set", () => Guarded.Send(s, SetupEdits.TableSize(_width.Value, _length.Value))));
        body.AddChild(_swatches = new HFlowContainer());
        _swatches.AddThemeConstantOverride("h_separation", 6); _swatches.AddThemeConstantOverride("v_separation", 6);
        FillSwatches();
    }

    readonly ButtonGroup _swatchGroup = new() { AllowUnpress = false };

    /// <summary>The board prints as 48 px swatches, the one the table wears framed; they follow the catalog in use.</summary>
    void FillSwatches()
    {
        if (!IsInstanceValid(_swatches)) return;
        foreach (var c in _swatches.GetChildren()) { _swatches.RemoveChild(c); c.QueueFree(); }
        foreach (var name in BuiltinContent.Textures)
        {
            var swatch = new Button { ToggleMode = true, ButtonGroup = _swatchGroup, TooltipText = SetupWords.Texture(name), CustomMinimumSize = new Vector2(56, 56),
                ExpandIcon = true, FocusMode = FocusModeEnum.None };
            swatch.SetMeta("texture", name);
            swatch.AddThemeStyleboxOverride("pressed", Framed());
            swatch.AddThemeStyleboxOverride("hover_pressed", Framed());
            var pick = name;
            swatch.Pressed += () => { if (_session.Draft?.Texture.Builtin != pick) Guarded.Send(_session, SetupEdits.Texture(pick)); };
            _swatches.AddChild(swatch);
            Guarded.Run(async () => { if (await Swatches.Of(pick) is { } texture && IsInstanceValid(swatch)) swatch.Icon = texture; }, _ => { });
        }
        ShowSwatches();
    }

    void ShowSwatches()
    {
        if (!IsInstanceValid(_swatches)) return;
        var current = _session.Draft?.Texture.Builtin;
        foreach (var c in _swatches.GetChildren()) if (c is Button b) b.SetPressedNoSignal(b.GetMeta("texture").AsString() == current);
    }

    // ---- Zones ----

    void BuildZones(Session s, VBoxContainer body)
    {
        body.AddChild(_diagram = new ZoneDiagramControl { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        body.AddChild(_styles = new HFlowContainer());
        _styles.AddThemeConstantOverride("h_separation", 6); _styles.AddThemeConstantOverride("v_separation", 6);
    }

    readonly ButtonGroup _styleGroup = new() { AllowUnpress = false };
    string? _zonesShown;

    void ShowZones()
    {
        if (!IsInstanceValid(_diagram)) return;
        var d = _session.Draft;
        _diagram.Show(d?.Size ?? new TableSize(72, 48), d?.Zones ?? [], _session.Zones);
        var style = d is null ? null : ZoneDiagram.StyleOf(d.Zones);
        var teams = _session.Game?.Teams.OrderBy(t => t.Ordinal).Select(t => t.Ordinal).ToList() ?? [];
        var key = $"{style}|{string.Join(",", teams)}|{Ui.Current.Id}";
        if (key == _zonesShown) return;
        _zonesShown = key;
        foreach (var c in _styles.GetChildren()) { _styles.RemoveChild(c); c.QueueFree(); }
        foreach (var z in ZoneStyles.All)
        {
            var tile = new Button { Text = SetupWords.ZoneStyle(z), ToggleMode = true, ButtonGroup = _styleGroup, ButtonPressed = z == style, FocusMode = FocusModeEnum.None };
            tile.AddThemeStyleboxOverride("pressed", Framed()); tile.AddThemeStyleboxOverride("hover_pressed", Framed());
            tile.AddThemeColorOverride("font_pressed_color", Ui.Text); tile.AddThemeColorOverride("font_hover_pressed_color", Ui.Text);
            var pick = z;
            tile.Pressed += () => { if (ZoneDiagram.StyleOf(_session.Draft?.Zones ?? []) != pick) Guarded.Send(_session, SetupEdits.Zones(pick)); };
            _styles.AddChild(tile);
        }
        // ⇄ on the diagram: one button for two teams, a menu of the pairs for more.
        if (IsInstanceValid(_swap)) _swap.QueueFree();
        if (teams.Count < 2) return;
        if (teams.Count == 2)
        {
            var swap = new Button { Text = "⇄", TooltipText = $"Swap Team {teams[0]} and Team {teams[1]}", FocusMode = FocusModeEnum.None };
            swap.Pressed += () => Guarded.Send(_session, SetupEdits.SwapZones(teams[0], teams[1]));
            _swap = swap;
        }
        else
        {
            var menu = new MenuButton { Text = "⇄", TooltipText = "Swap two teams' zones", FocusMode = FocusModeEnum.None, Flat = false };
            var pairs = teams.SelectMany((a, i) => teams.Skip(i + 1).Select(b => (a, b))).ToList();
            for (var i = 0; i < pairs.Count; i++) menu.GetPopup().AddItem($"Team {pairs[i].a} ⇄ Team {pairs[i].b}", i);
            menu.GetPopup().IdPressed += id => Guarded.Send(_session, SetupEdits.SwapZones(pairs[(int)id].a, pairs[(int)id].b));
            _swap = menu;
        }
        _diagram.AddChild(_swap);
        _swap.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.KeepSize, 4);
    }

    // ---- Layouts ----

    void BuildLayouts(Session s, Fold fold)
    {
        var save = new Button { Text = "Save…", TooltipText = "Save this table as a layout" };
        save.Pressed += () => PieceMenu.AskText(this, "Save the table as", name => Guarded.Run(async () =>
        {
            if (TextLimits.NameProblem(name, "A layout name") is { } problem) { _screen.Toast(problem); return; }
            await s.Management.SaveLayout((s.Draft ?? throw new InvalidOperationException("The table has not arrived yet.")) with { Name = name.Trim() });
            _screen.Toast($"Layout {name.Trim()} saved.");
            await FillLayouts();
        }, _screen.Toast));
        fold.Actions.AddChild(save);
        fold.Body.AddChild(_layouts = new VBoxContainer());
        _layouts.AddThemeConstantOverride("separation", 2);
        Guarded.Run(FillLayouts, _screen.Toast);
    }

    /// <summary>The predefined tables first, then mine, a row each with ▸ Load (and Delete for mine). Loading replaces the whole draft, size included.</summary>
    async Task FillLayouts()
    {
        var s = _session;
        var (builtin, saved) = (s.Management.BuiltinLayouts(), s.Management.ListLayouts());
        var choices = LayoutChoices.Of(await builtin, await saved);
        if (!IsInstanceValid(_layouts)) return;
        foreach (var c in _layouts.GetChildren()) { _layouts.RemoveChild(c); c.QueueFree(); }
        foreach (var choice in choices)
        {
            var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 8); _layouts.AddChild(line);
            line.AddChild(Icon("layout", choice.Builtin is null ? Ui.Accent : Ui.DimText, 16));
            line.AddChild(new Label { Text = choice.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                TooltipText = choice.Label, MouseFilter = MouseFilterEnum.Pass });
            var menu = new MenuButton { Text = "▸", Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None };
            menu.GetPopup().AddItem("Load", 0);
            if (choice.SavedId is not null) menu.GetPopup().AddItem("Delete", 1);
            var pick = choice;
            menu.GetPopup().IdPressed += id => Guarded.Run(async () =>
            {
                if (id == 0) await s.SendEditAsync(SetupEdits.Load(pick.Builtin ?? await s.Management.GetLayout(pick.SavedId!.Value)));
                else Confirm($"Delete {pick.Label}?", "Delete", () => Guarded.Run(async () => { await s.Management.DeleteLayout(pick.SavedId!.Value); await FillLayouts(); }, _screen.Toast));
            }, _screen.Toast);
            line.AddChild(menu);
        }
    }

    // ---- Shared ----

    /// <summary>Add buttons disabled with the reason once the draft is at a cap, and the headings' counts.</summary>
    readonly List<(Button Button, Func<TableState?, string?> Why)> _gates = new();
    bool? _fullShown;

    void RefreshLimits()
    {
        var d = _session.Draft;
        foreach (var fold in _foldsShown) fold.Refresh();
        foreach (var (button, why) in _gates)
        {
            if (!IsInstanceValid(button)) continue;
            var reason = why(d);
            button.Disabled = reason is not null; button.TooltipText = reason ?? button.TooltipText;
        }
        var full = SetupLimits.CannotAddTerrain(d);
        if (IsInstanceValid(_terrainFull)) { _terrainFull.Text = full ?? ""; _terrainFull.Visible = full is not null && d is not null; }
        if (_fullShown != (full is not null)) { _fullShown = full is not null; FillTiles(); }
    }

    T Gated<T>(T button, Func<TableState?, string?> why) where T : Button { _gates.Add((button, why)); return button; }

    static TableSize SizeNow(Session s) => s.Draft?.Size ?? new TableSize(72, 48);
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }

    static TextureRect Icon(string name, Color tint, int size) => new()
    {
        Texture = Ui.Icon(name), Modulate = tint, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore,
    };

    /// <summary>The section's primary tile: a faint accent panel with an accent edge.</summary>
    static StyleBoxFlat PrimaryTile()
    {
        var b = new StyleBoxFlat { BgColor = Ui.Accent with { A = 0.12f }, BorderColor = Ui.Accent with { A = 0.7f } };
        b.SetBorderWidthAll(1); b.BorderWidthLeft = 4; b.SetCornerRadiusAll(Math.Min(Ui.Current.Shape.CornerRadius, 6)); b.SetContentMarginAll(8);
        return b;
    }

    /// <summary>The chosen swatch or style: framed in the accent colour.</summary>
    static StyleBoxFlat Framed()
    {
        var b = new StyleBoxFlat { BgColor = Ui.Accent with { A = 0.18f }, BorderColor = Ui.Accent };
        b.SetBorderWidthAll(2); b.SetCornerRadiusAll(Math.Min(Ui.Current.Shape.CornerRadius, 4)); b.SetContentMarginAll(4);
        return b;
    }

    void Confirm(string title, string ok, Action then)
    {
        var d = new ConfirmationDialog { Title = title, OkButtonText = ok, DialogText = title };
        d.Confirmed += then; d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        AddChild(d); d.PopupCentered(new Vector2I(320, 0));
    }

    /// <summary>Every terrain piece off the table in one edit (the draft without its terrain), asked first.</summary>
    void ClearTerrain(Session s)
    {
        if (s.Draft is not { Terrain.Count: > 0 } draft) return;
        var d = new ConfirmationDialog { Title = "Clear terrain?", OkButtonText = "Clear terrain", DialogAutowrap = true,
            DialogText = $"All {draft.Terrain.Count} terrain pieces come off the table. Objectives and zones stay." };
        d.Confirmed += () => { if (s.Draft is { } now) Guarded.Send(s, SetupEdits.ClearTerrain(now)); };
        d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        AddChild(d); d.PopupCentered(new Vector2I(360, 0));
    }

    async Task AddLibraryTerrain(Session s)
    {
        var assets = (await s.Management.ListAssets(AssetKind.Terrain, null)).Items.Where(x => x.Uploaded).ToList();
        if (!IsInstanceValid(this)) return;
        if (assets.Count == 0) { _screen.Toast("No terrain in your library; upload some in the web app."); return; }
        PieceMenu.AskChoice(this, "Add terrain from my library", assets.Select(x => x.Name).ToList(), picked => Guarded.Run(async () =>
        {
            // Scale 1: MeshLoader.Orient already applies the asset's scale to the mesh, and its volumes are table-frame after scale.
            var asset = assets.First(x => x.Name == picked); var volumes = await s.Management.GetAssetVolumes(asset.Id);
            await s.SendEditAsync(SetupEdits.AddAsset(asset.Id, volumes.Select(v => v.Volume).ToList(), SizeNow(s), Random.Shared.NextDouble));
        }, _screen.Toast));
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

/// <summary>The zones diagram: the table's outline, each team's zone filled in its colour, the style's name under it.</summary>
public partial class ZoneDiagramControl : Control
{
    TableSize _size = new(72, 48); IReadOnlyList<DeploymentZone> _zones = []; ZoneLegs _legs = ZoneLegs.Default;
    ZoneDiagramView? _view;
    const float MaxHeight = 200, Caption = 22;

    public override void _Ready() { Resized += Recompute; MouseFilter = MouseFilterEnum.Pass; }

    public void Show(TableSize size, IReadOnlyList<DeploymentZone> zones, ZoneLegs legs) { _size = size; _zones = zones; _legs = legs; Recompute(); }

    void Recompute()
    {
        var width = Math.Max(Size.X, 120);
        _view = ZoneDiagram.Of(_size, _zones, _legs, width, MaxHeight);
        CustomMinimumSize = new Vector2(0, (float)_view.Height + Caption);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_view is not { } v) return;
        var x0 = (Size.X - (float)v.Width) / 2;
        var outline = new Rect2(x0, 0, (float)v.Width, (float)v.Height);
        DrawRect(outline, Ui.Background with { A = 0.6f });
        foreach (var zone in v.Zones)
            foreach (var polygon in zone.Polygons)
            {
                var points = polygon.Select(p => new Vector2(x0 + (float)p.X, (float)p.Y)).ToArray();
                if (points.Length >= 3) DrawColoredPolygon(points, Ui.Team(zone.Ordinal) with { A = 0.55f });
                DrawPolyline([.. points, points[0]], Ui.Team(zone.Ordinal), 2);
            }
        DrawRect(outline, Ui.DimText, false, 1.5f);
        var font = GetThemeDefaultFont(); var fontSize = GetThemeDefaultFontSize();
        var caption = v.Style;
        var w = font.GetStringSize(caption, HorizontalAlignment.Left, -1, fontSize).X;
        DrawString(font, new Vector2((Size.X - w) / 2, (float)v.Height + Caption - 5), caption, HorizontalAlignment.Left, -1, fontSize, Ui.DimText);
    }
}

/// <summary>A board print as a small swatch, fetched by hash as the slab fetches it (the packed file in a development run), its height channel dropped.</summary>
public static class Swatches
{
    static readonly Dictionary<string, Task<Texture2D?>> Made = new();

    public static Task<Texture2D?> Of(string name)
    {
        if (!Made.TryGetValue(name, out var task) || task.IsFaulted) Made[name] = task = Build(name);
        return task;
    }

    static async Task<Texture2D?> Build(string name)
    {
        var path = BuiltinCatalog.TexturePath(name);
        var file = BuiltinContent.File(path) ?? await BuiltinContent.FileAsync(path);
        if (file is null) return null;
        Image? image;
        if (file.StartsWith("res://", StringComparison.Ordinal))
        {
            if (!ResourceLoader.Exists(file) || ResourceLoader.Load<Texture2D>(file)?.GetImage() is not { } packed) return null;
            image = (Image)packed.Duplicate();
            if (image.IsCompressed()) image.Decompress();
        }
        else
        {
            var bytes = await Task.Run(() => System.IO.File.ReadAllBytes(file));
            image = new Image();
            if (image.LoadPngFromBuffer(bytes) != Error.Ok) return null;
        }
        image.Convert(Image.Format.Rgb8);   // the alpha is the print's height
        image.Resize(48, 48, Image.Interpolation.Lanczos);
        return ImageTexture.CreateFromImage(image);
    }
}
