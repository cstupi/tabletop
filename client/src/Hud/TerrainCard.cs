using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>
/// The selected terrain piece (or setup marker) in the unit card's slot, in the same frame: TerrainCardContent, read-only in play,
/// with the piece's edits in setup (facing, scale, swap mesh, reset and override volumes, notes, remove). Placed by GameScreen.Layout
/// in every status; hidden when the selection is no terrain piece or marker it shows. The unit card hides while terrain is selected
/// (SheetFocus), so the two share the slot.
/// </summary>
public partial class TerrainCard : VBoxContainer
{
    Session _s = null!; TableInput _input = null!; Action<string> _toast = null!; SetupFolds _folds = new();
    readonly ScrollContainer _card = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve, SizeFlagsVertical = SizeFlags.ShrinkEnd };
    readonly VBoxContainer _body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    HudRect? _slot; bool _fitQueued; object? _content;
    Action<RenderCause>? _onFrame; Action? _onChanged, _onTheme;

    public void Bind(Session session, TableInput input, Action<string> toast, SetupFolds folds)
    {
        _s = session; _input = input; _toast = toast; _folds = folds;
        var panel = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkEnd }; AddChild(panel); panel.AddChild(_card); _card.AddChild(_body);
        _body.MinimumSizeChanged += QueueFit;
        _onFrame = _ => Render(); _onChanged = Render;
        session.Frame.Render += _onFrame; input.SelectionChanged += _onChanged;
        _onTheme = () => { _content = null; Render(); }; Ui.Changed += _onTheme;
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is not null) _s.Frame.Render -= _onFrame;
        if (_input is not null && IsInstanceValid(_input)) _input.SelectionChanged -= _onChanged;
    }

    /// <summary>As the unit card: the slot's width, the content's height up to the slot's, against the slot's bottom.</summary>
    public void Place(HudRect slot)
    {
        _slot = slot;
        Fit();
        HudPlacement.Bottom(this, slot);
    }

    void QueueFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Callable.From(() => { _fitQueued = false; Fit(); }).CallDeferred();
    }

    void Fit()
    {
        if (_slot is not { } slot || !IsInstanceValid(_card)) return;
        var chrome = GetCombinedMinimumSize().Y - _card.GetCombinedMinimumSize().Y;
        _card.CustomMinimumSize = new Vector2(0, MathF.Floor(Math.Max(0, Math.Min(_body.GetCombinedMinimumSize().Y, slot.H - chrome))));
    }

    void Render()
    {
        var status = _s.Game?.Status; var selected = _input.Selected?.PieceId;
        var table = TerrainCardContent.TableOf(status, _s.Draft, _s.Projection);
        object? content = (object?)TerrainCardContent.Of(table, status, selected, _s.TerrainWords, _s.TerrainRules) ?? MarkerCardContent.Of(table, status, selected);
        Visible = content is not null;
        if (Equals(content, _content)) return;
        var scroll = content is TerrainCardContent t && _content is TerrainCardContent was && was.PieceId == t.PieceId ? _card.ScrollVertical : 0;
        _content = content;
        Clear(_body);
        switch (content)
        {
            case TerrainCardContent terrain: RenderTerrain(terrain, table!.Terrain.First(x => x.Id == terrain.PieceId)); break;
            case MarkerCardContent marker: RenderMarker(marker); break;
        }
        if (scroll > 0) GetTree().Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() => { if (IsInstanceValid(_card)) _card.ScrollVertical = scroll; }), (uint)ConnectFlags.OneShot);
    }

    void RenderTerrain(TerrainCardContent c, TerrainPlacement t)
    {
        if (c.Editable) { RenderSetupHead(c, t); RenderPieceEdits(c, t); return; }
        RenderEffects(c);
    }

    /// <summary>Setup (spec 2026-11-07-ux-redesign-1-client-shell.md → Terrain and marker cards): a band in the piece's
    /// group's colour (TerrainTiles.KindOf, the tile's) with its name, height and Remove as the danger icon at the right, then its effects as
    /// chips, so the piece reads as its tile did.</summary>
    void RenderSetupHead(TerrainCardContent c, TerrainPlacement t)
    {
        var s = _s;
        var all = c.Effects.Aggregate(Tabletop.Geometry.TerrainDeclarations.None, (d, e) => d | e.Flag);
        var band = new PanelContainer(); band.AddThemeStyleboxOverride("panel", Ui.EdgeBand(Ui.Of(TerrainTiles.KindOf(all).Colour))); _body.AddChild(band);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 10); band.AddChild(head);
        head.AddChild(new Label { Text = c.Name, ThemeTypeVariation = Variation.Display, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        if (c.Height.Length > 0) head.AddChild(new Label { Text = $"{c.Height} high", ThemeTypeVariation = Variation.Dim, VerticalAlignment = VerticalAlignment.Center });
        var remove = HudBits.IconButton("remove", "Remove this piece", 20); remove.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        remove.AddThemeColorOverride("icon_normal_color", Ui.Failure); remove.AddThemeColorOverride("icon_hover_color", Ui.Failure.Lightened(0.3f));
        remove.Pressed += () => Guarded.Send(s, SetupEdits.RemoveTerrain(t.Id)); head.AddChild(remove);
        if (c.Effects.Count == 0) return;
        var chips = new HFlowContainer(); chips.AddThemeConstantOverride("h_separation", 6); chips.AddThemeConstantOverride("v_separation", 6); _body.AddChild(chips);
        foreach (var e in c.Effects) chips.AddChild(HudBits.EffectChip(e, 16));
    }

    /// <summary>
    /// Play (owner, 2026-11-07: "when terrain is SELECTED during play it should better show the effects"): a band in the piece's strongest
    /// effect's colour with its name and height, then each effect as a large icon in its colour, its word and the server's sentence of what it
    /// does to a unit (Session.TerrainRules); the table tints each volume in the same colour while the piece is selected. A piece of several
    /// volumes lists each with its tint, then facing and scale, dim.
    /// </summary>
    void RenderEffects(TerrainCardContent c)
    {
        var all = c.Effects.Aggregate(Tabletop.Geometry.TerrainDeclarations.None, (d, e) => d | e.Flag);
        var band = new PanelContainer(); band.AddThemeStyleboxOverride("panel", Ui.EdgeBand(Ui.Of(TerrainEffects.Primary(all).Colour))); _body.AddChild(band);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 10); band.AddChild(head);
        head.AddChild(new Label { Text = c.Name, ThemeTypeVariation = Variation.Display, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        if (c.Height.Length > 0) head.AddChild(new Label { Text = $"{c.Height} high", ThemeTypeVariation = Variation.Dim, VerticalAlignment = VerticalAlignment.Center });
        var rows = new VBoxContainer(); rows.AddThemeConstantOverride("separation", 10); _body.AddChild(rows);
        foreach (var e in c.Effects)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); rows.AddChild(row);
            var icon = HudBits.Icon(e.Icon, Ui.Of(e.Colour), 36); icon.SizeFlagsVertical = SizeFlags.ShrinkBegin; row.AddChild(icon);
            var words = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; words.AddThemeConstantOverride("separation", 0); row.AddChild(words);
            var word = new Label { Text = e.Word }; word.AddThemeColorOverride("font_color", Ui.Of(e.Colour).Lerp(Ui.Text, 0.35f)); word.AddThemeFontSizeOverride("font_size", 20); words.AddChild(word);
            if (e.Rule is { } rule) words.AddChild(Wrap(rule));
        }
        if (c.Volumes.Count > 1)
            foreach (var (v, i) in c.Volumes.Select((v, i) => (v, i)))
            {
                var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 8); _body.AddChild(line);
                var swatch = new PanelContainer { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter }; swatch.AddThemeStyleboxOverride("panel", Ui.EffectChip(v.Colour)); line.AddChild(swatch);
                line.AddChild(Wrap($"{v.Heights} · {string.Join(", ", v.Declarations)}", Variation.Dim));
            }
        foreach (var v in c.Volumes) if (v.Notes is { } notes) _body.AddChild(Wrap(notes, Variation.Dim));
        _body.AddChild(Wrap(c.Scale != 1 ? $"Facing {c.Facing}° · Scale ×{TerrainLabel.Inches(c.Scale)}" : $"Facing {c.Facing}°", Variation.Dim));
    }

    /// <summary>
    /// Setup (spec 2026-10-27-godot-setup-screen-design.md → Terrain card): facing (MenuModel.TurnInDraft, as the right-click menu and Q/E)
    /// and scale; then Mesh (the source, swap, my asset, reset volumes) and Volumes, folded as the player left them.
    /// </summary>
    void RenderPieceEdits(TerrainCardContent c, TerrainPlacement t)
    {
        var s = _s;
        var facing = new HFlowContainer(); _body.AddChild(facing);
        void TurnTo(double degrees) { if (s.Draft is { } now && MenuModel.TurnInDraft(now, t.Id, degrees) is { } turn) Guarded.Send(s, turn); }
        facing.AddChild(new Label { Text = "Facing" });
        var yaw = new SpinBox { MinValue = 0, MaxValue = 359, Step = 1, Value = c.Facing, Suffix = "°", CustomMinimumSize = new Vector2(70, 0), TooltipText = "Degrees counter-clockwise from above" };
        yaw.ValueChanged += TurnTo; facing.AddChild(yaw);
        facing.AddChild(Btn($"⟲ {MenuModel.TerrainTurn}°", () => TurnTo(t.Yaw.Degrees + MenuModel.TerrainTurn)));
        facing.AddChild(Btn($"⟳ {MenuModel.TerrainTurn}°", () => TurnTo(t.Yaw.Degrees - MenuModel.TerrainTurn)));
        var size = new HFlowContainer(); _body.AddChild(size);
        size.AddChild(new Label { Text = "Scale" });
        var scale = new SpinBox { MinValue = 0.25, MaxValue = 4, Step = 0.25, Value = t.Scale, Prefix = "×" }; size.AddChild(scale);
        scale.ValueChanged += v => Guarded.Send(s, SetupEdits.Scale(t.Id, v));

        var mesh = new Fold(_folds, SetupFold.PieceMesh, GameStatus.Setup, () => SetupFolds.Title(SetupFold.PieceMesh, null, null)); _body.AddChild(mesh);
        mesh.Body.AddChild(Wrap(c.Source, Variation.Dim));
        var swapRow = new HFlowContainer(); mesh.Body.AddChild(swapRow);
        var swap = new OptionButton(); var names = BuiltinContent.Terrain.Keys.Order().ToList(); foreach (var n in names) swap.AddItem(n);
        if (t.Mesh.Builtin is { } b && names.IndexOf(b) is >= 0 and var at) swap.Select(at);
        swapRow.AddChild(swap);
        swapRow.AddChild(Btn("Swap", () => Guarded.Send(s, SetupEdits.SwapBuiltin(t.Id, names[swap.Selected]))));
        swapRow.TooltipText = "Swap the mesh; the volumes stay";
        var more = new HFlowContainer(); mesh.Body.AddChild(more);
        more.AddChild(Btn("Use my terrain…", () => Guarded.Run(() => SwapForAsset(s, t.Id), _toast)));
        more.AddChild(Btn("Reset volumes", () => Guarded.Run(async () =>
            await s.SendEditAsync(SetupEdits.ResetVolumes(t.Id, t.Mesh.IsBuiltin ? BuiltinContent.VolumesOf(t.Mesh.Builtin!).Select(v => v.Volume).ToList() : (await s.Management.GetAssetVolumes(t.Mesh.AssetId!.Value)).Select(v => v.Volume).ToList())), _toast)));

        var volumes = new Fold(_folds, SetupFold.PieceVolumes, GameStatus.Setup, () => SetupFolds.PieceVolumesTitle(c.Volumes.Count)); _body.AddChild(volumes);
        foreach (var (v, i) in c.Volumes.Select((v, i) => (v, i)))
        {
            if (i > 0) volumes.Body.AddChild(new HSeparator());
            volumes.Body.AddChild(Wrap(c.Volumes.Count == 1 ? $"{v.Kind} · {v.Footprint} · {v.Heights}" : $"Volume {i + 1} · {v.Kind} · {v.Footprint} · {v.Heights}"));
            RenderVolumeEdits(t, t.Volumes[i], volumes.Body);
        }
    }

    /// <summary>Every volume field is one OverrideTerrainVolumes carrying the placement's whole volume list with one volume changed.</summary>
    void RenderVolumeEdits(TerrainPlacement t, TerrainVolume tv, VBoxContainer into)
    {
        var s = _s;
        void Edit(Func<Volume, Volume> change) => Guarded.Send(s, SetupEdits.ChangeVolume(t, tv.Id, change));
        var shape = new HFlowContainer(); into.AddChild(shape);
        var kind = new OptionButton(); foreach (var k in Enum.GetNames<VolumeKind>()) kind.AddItem(k); kind.Select((int)tv.Volume.Kind); shape.AddChild(kind);
        kind.ItemSelected += k => Edit(SetupEdits.WithKind((VolumeKind)k));
        var fp = tv.Volume.Footprint;
        shape.AddChild(new Label { Text = fp.Kind == FootprintKind.Circle ? "radius" : "w×l" });
        shape.AddChild(Spin(fp.A, 144, x => Edit(SetupEdits.WithFootprintA(x))));
        if (fp.Kind == FootprintKind.Rect) shape.AddChild(Spin(fp.B, 144, x => Edit(SetupEdits.WithFootprintB(x))));
        var heights = new HFlowContainer(); into.AddChild(heights);
        heights.AddChild(new Label { Text = "from" }); heights.AddChild(Spin(tv.Volume.Bottom, 60, x => Edit(SetupEdits.WithBottom(x))));
        heights.AddChild(new Label { Text = "to" }); heights.AddChild(Spin(tv.Volume.Top, 60, x => Edit(SetupEdits.WithTop(x))));
        var flags = new HFlowContainer(); into.AddChild(flags);
        foreach (var (flag, text) in TerrainLabel.DeclarationsIn(_s.TerrainWords))
        {
            var cb = new CheckBox { Text = text, ButtonPressed = tv.Volume.Declarations.HasFlag(flag) };
            cb.Toggled += on => Edit(SetupEdits.WithDeclaration(flag, on));
            flags.AddChild(cb);
        }
        var notes = new LineEdit { Text = tv.Volume.Notes ?? "", PlaceholderText = $"notes, Enter to save (at most {TextLimits.Notes} characters)", MaxLength = TextLimits.Notes, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        into.AddChild(notes);
        notes.TextSubmitted += text => Edit(SetupEdits.WithNotes(text));
    }

    void RenderMarker(MarkerCardContent m)
    {
        var s = _s; var id = m.PieceId;
        var band = new PanelContainer(); band.AddThemeStyleboxOverride("panel", Ui.EdgeBand(Ui.Accent)); _body.AddChild(band);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 10); band.AddChild(head);
        head.AddChild(HudBits.Icon("objective", Ui.Accent, 20));
        head.AddChild(new Label { Text = m.Name, ThemeTypeVariation = Variation.Display, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        var remove = HudBits.IconButton("remove", "Remove this marker", 20); remove.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        remove.AddThemeColorOverride("icon_normal_color", Ui.Failure); remove.AddThemeColorOverride("icon_hover_color", Ui.Failure.Lightened(0.3f));
        remove.Pressed += () => Guarded.Send(s, SetupEdits.RemoveMarker(id)); head.AddChild(remove);
        _body.AddChild(Wrap(m.Label is { } label ? $"{label} · {m.Diameter}" : m.Diameter, Variation.Dim));
    }

    /// <summary>Setup phase -> Terrain: anyone may swap a piece's mesh for an asset from their library, and the placement keeps its
    /// volumes and declarations. "Reset volumes" is how a user takes the new mesh's own instead.</summary>
    async Task SwapForAsset(Session s, Guid placementId)
    {
        var assets = (await s.Management.ListAssets(AssetKind.Terrain, null)).Items.Where(x => x.Uploaded).ToList();
        if (!IsInstanceValid(this)) return;
        if (assets.Count == 0) { _toast("No terrain in your library; upload some in the web app."); return; }
        var pick = new OptionButton(); foreach (var x in assets) pick.AddItem(x.Name);
        var dialog = new ConfirmationDialog { Title = "Swap this piece's mesh (its volumes and declarations stay)" }; dialog.AddChild(pick);
        dialog.VisibilityChanged += () => { if (!dialog.Visible) dialog.QueueFree(); };
        AddChild(dialog);
        dialog.Confirmed += () => Guarded.Send(s, SetupEdits.SwapAsset(placementId, assets[pick.Selected].Id));
        dialog.PopupCentered();
    }

    static SpinBox Spin(double value, double max, Action<double> changed) { var sb = new SpinBox { MinValue = 0, MaxValue = max, Step = 0.25, Value = value, CustomMinimumSize = new Vector2(70, 0) }; sb.ValueChanged += v => changed(v); return sb; }
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
    static Label Wrap(string text, string variation = "") =>
        new() { Text = text, ThemeTypeVariation = variation, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    static void Clear(Node box) { foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); } }
}
