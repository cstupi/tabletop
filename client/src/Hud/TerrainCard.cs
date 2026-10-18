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
    Session _s = null!; TableInput _input = null!; Action<string> _toast = null!;
    readonly ScrollContainer _card = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve, SizeFlagsVertical = SizeFlags.ShrinkEnd };
    readonly VBoxContainer _body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    HudRect? _slot; bool _fitQueued; object? _content;
    Action<RenderCause>? _onFrame; Action? _onChanged, _onTheme;

    public void Bind(Session session, TableInput input, Action<string> toast)
    {
        _s = session; _input = input; _toast = toast;
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
        object? content = (object?)TerrainCardContent.Of(table, status, selected) ?? MarkerCardContent.Of(table, status, selected);
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
        _body.AddChild(Wrap(c.Name));
        _body.AddChild(Wrap(c.Source, Variation.Dim));
        var badges = new HFlowContainer(); _body.AddChild(badges);
        if (c.Height.Length > 0) badges.AddChild(Badge($"Height {c.Height}"));
        if (!c.Editable) { badges.AddChild(Badge($"Facing {c.Facing}°")); if (c.Scale != 1) badges.AddChild(Badge($"Scale ×{TerrainLabel.Inches(c.Scale)}")); }
        if (c.Editable) RenderPieceEdits(c, t);
        foreach (var (v, i) in c.Volumes.Select((v, i) => (v, i)))
        {
            _body.AddChild(new HSeparator());
            _body.AddChild(Wrap($"Volume {i + 1} · {v.Kind} · {v.Footprint} · {v.Heights}"));
            if (c.Editable) { RenderVolumeEdits(t, t.Volumes[i]); continue; }
            _body.AddChild(Flow(v.Declarations.Select(d => Chip(d))));
            if (v.Notes is { } notes) _body.AddChild(Wrap(notes, Variation.Dim));
        }
    }

    /// <summary>Setup: facing (MenuModel.TurnInDraft, as the right-click menu and Q/E), scale, swap mesh, reset volumes, remove.</summary>
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
        scale.ValueChanged += v => Guarded.Send(s, new ScaleTerrain(t.Id, v));
        var swapRow = new HFlowContainer(); _body.AddChild(swapRow);
        var swap = new OptionButton(); var names = BuiltinContent.Terrain.Keys.Order().ToList(); foreach (var n in names) swap.AddItem(n);
        if (t.Mesh.Builtin is { } b && names.IndexOf(b) is >= 0 and var at) swap.Select(at);
        swapRow.AddChild(swap);
        swapRow.AddChild(Btn("Swap mesh (keeps volumes)", () => Guarded.Send(s, new SwapTerrainMesh(t.Id, VisualRef.OfBuiltin(names[swap.Selected])))));
        var more = new HFlowContainer(); _body.AddChild(more);
        more.AddChild(Btn("Swap for my asset…", () => Guarded.Run(() => SwapForAsset(s, t.Id), _toast)));
        more.AddChild(Btn("Reset volumes", () => Guarded.Run(async () =>
            await s.SendEditAsync(new OverrideTerrainVolumes(t.Id, t.Mesh.IsBuiltin ? BuiltinContent.VolumesOf(t.Mesh.Builtin!) : (await s.Management.GetAssetVolumes(t.Mesh.AssetId!.Value)).Select(v => new TerrainVolume(Guid.CreateVersion7(), v.Volume)).ToList())), _toast)));
        more.AddChild(Btn("Remove", () => Guarded.Send(s, new RemoveTerrain(t.Id))));
    }

    /// <summary>Every volume field is one OverrideTerrainVolumes carrying the placement's whole volume list with one volume changed.</summary>
    void RenderVolumeEdits(TerrainPlacement t, TerrainVolume tv)
    {
        var s = _s;
        void Edit(Func<Volume, Volume> change) =>
            Guarded.Send(s, new OverrideTerrainVolumes(t.Id, t.Volumes.Select(v => v.Id != tv.Id ? v : v with { Volume = change(v.Volume) }).ToList()));
        var shape = new HFlowContainer(); _body.AddChild(shape);
        var kind = new OptionButton(); foreach (var k in Enum.GetNames<VolumeKind>()) kind.AddItem(k); kind.Select((int)tv.Volume.Kind); shape.AddChild(kind);
        kind.ItemSelected += k => Edit(v => v with { Kind = (VolumeKind)k });
        var fp = tv.Volume.Footprint;
        shape.AddChild(new Label { Text = fp.Kind == FootprintKind.Circle ? "radius" : "w×l" });
        shape.AddChild(Spin(fp.A, 144, x => Edit(v => v with { Footprint = v.Footprint.Kind == FootprintKind.Circle ? new Footprint(FootprintKind.Circle, x, x) : v.Footprint with { A = x } })));
        if (fp.Kind == FootprintKind.Rect) shape.AddChild(Spin(fp.B, 144, x => Edit(v => v with { Footprint = v.Footprint with { B = x } })));
        var heights = new HFlowContainer(); _body.AddChild(heights);
        heights.AddChild(new Label { Text = "from" }); heights.AddChild(Spin(tv.Volume.Bottom, 60, x => Edit(v => v with { Bottom = x })));
        heights.AddChild(new Label { Text = "to" }); heights.AddChild(Spin(tv.Volume.Top, 60, x => Edit(v => v with { Top = x })));
        var flags = new HFlowContainer(); _body.AddChild(flags);
        foreach (var (flag, text) in TerrainLabel.Declarations)
        {
            var cb = new CheckBox { Text = text, ButtonPressed = tv.Volume.Declarations.HasFlag(flag) };
            cb.Toggled += on => Edit(v => v with { Declarations = on ? v.Declarations | flag : v.Declarations & ~flag });
            flags.AddChild(cb);
        }
        var notes = new LineEdit { Text = tv.Volume.Notes ?? "", PlaceholderText = $"notes, Enter to save (at most {TextLimits.Notes} characters)", MaxLength = TextLimits.Notes, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddChild(notes);
        notes.TextSubmitted += text => Edit(v => v with { Notes = text.Length == 0 ? null : text });
    }

    void RenderMarker(MarkerCardContent m)
    {
        _body.AddChild(Wrap(m.Label is { } label ? $"{m.Name} · {label}" : m.Name));
        var badges = new HFlowContainer(); _body.AddChild(badges); badges.AddChild(Badge(m.Diameter));
        var s = _s; var id = m.PieceId;
        _body.AddChild(Btn("Remove marker", () => Guarded.Send(s, new RemoveSetupMarker(id))));
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
        dialog.Confirmed += () => Guarded.Send(s, new SwapTerrainMesh(placementId, VisualRef.OfAsset(assets[pick.Selected].Id)));
        dialog.PopupCentered();
    }

    static SpinBox Spin(double value, double max, Action<double> changed) { var sb = new SpinBox { MinValue = 0, MaxValue = max, Step = 0.25, Value = value, CustomMinimumSize = new Vector2(70, 0) }; sb.ValueChanged += v => changed(v); return sb; }
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
    static PanelContainer Chip(string text) { var chip = new PanelContainer { ThemeTypeVariation = Variation.Chip }; chip.AddChild(new Label { Text = text }); return chip; }
    static PanelContainer Badge(string text) { var badge = new PanelContainer { ThemeTypeVariation = Variation.Badge }; badge.AddChild(new Label { Text = text }); return badge; }
    static Label Wrap(string text, string variation = "") =>
        new() { Text = text, ThemeTypeVariation = variation, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    static HFlowContainer Flow(IEnumerable<Control> items) { var flow = new HFlowContainer(); foreach (var c in items) flow.AddChild(c); return flow; }
    static void Clear(Node box) { foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); } }
}
