using Godot; using Tabletop.Client.Core; using Tabletop.Client.Theming; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>My selected unit's range rings when V has them on (ClientSettings.RangeRings, so the choice is remembered), each labelled with its printed range;
/// and the threat rings (ThreatRings over Session.Threats), in the unit's team colour, whatever V says, of the selected unit of another team than the one
/// I act for (kept while I select and place my own units, ThreatRings.Pinned) and of the hovered one; and with T on, every such team's Charge and Shoot areas (ThreatRings.All),
/// in deployment and play alike; and while I place a unit held over a distance from enemy units (the deploy pick's or the move Ask's KeepAway:
/// an Ambusher, a 3.6 Scout, Reinforcement copy, Fanatic or Vanguard), the area it may not reach (KeepAwayArea) in a dark shade; and while I place a
/// unit held within a reach of my zone (the deploy pick's ZoneReach: a Scout), the area it may stand in (ZoneReachArea) in my team colour. Readable rings plan:
/// each ring fills its area in its kind's opacity (RingLook) under a screen-constant outline (RingMesh), and carries a screen-space label, the
/// kind's icon and its words in the team colour, under the outline's point lowest on the screen (RingLabels), never over another label.</summary>
public partial class RangeRingNode : Node3D
{
    Session _s = null!; TableInput _input = null!;
    readonly Layer _picked = new(), _hovered = new(), _range = new(), _keepAway = new(), _reach = new(); readonly List<Layer> _all = new();
    bool _allOn; Guid? _pinned;
    readonly CanvasLayer _canvas = new(); readonly Control _labels = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    Action<RenderCause>? _onFrame; Action? _onChanged, _onTheme; Action<Intent>? _onIntent; Action<Guid?>? _onHover;
    // Built on the thread pool (RingBuilds): a unit's outline takes tens of milliseconds, T's union more. A layer shows nothing until its build lands.
    readonly RingBuilds<Guid, Shaped> _threats = new(), _ranges = new(), _kept = new(capacity: 4); readonly RingBuilds<Guid, IReadOnlyList<(int Ordinal, Shaped Rings)>> _allThreats = new(capacity: 4);
    Action? _onBuilt;
    readonly List<(float X, float Y)> _projected = new(); readonly List<HudRect> _wanted = new(); readonly List<Control> _order = new();

    public void Bind(Session session, TableInput input)
    {
        _s = session; _input = input;
        AddChild(_canvas); _canvas.AddChild(_labels); _labels.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _picked.Attach(this, _labels); _hovered.Attach(this, _labels); _range.Attach(this, _labels); _keepAway.Attach(this, _labels); _reach.Attach(this, _labels);
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Facts | RenderCause.Game)) != 0) Draw(); }; _onChanged = Draw; _onHover = _ => Draw();
        _onTheme = () => { _labels.Theme = Ui.Theme; foreach (var l in Layers) l.Forget(); Draw(); };                           // the themed team colour, font and icons
        _onIntent = i =>
        {
            if (i == Intent.ToggleRangeRings) _s.UpdateSettings(x => x with { RangeRings = !x.RangeRings });                   // SettingsChanged redraws
            else if (i == Intent.ToggleAllThreats) { _allOn = !_allOn; Draw(); }
        };
        _onBuilt = () => Callable.From(() => { if (IsInstanceValid(this)) Draw(); }).CallDeferred();                          // a build landed, on a pool thread
        _threats.Built += _onBuilt; _ranges.Built += _onBuilt; _allThreats.Built += _onBuilt; _kept.Built += _onBuilt;
        session.Frame.Render += _onFrame; session.SettingsChanged += _onChanged; Ui.Changed += _onTheme;                       // the Settings dialog's toggle
        input.SelectionChanged += _onChanged; input.IntentPressed += _onIntent; input.HoveredUnitChanged += _onHover; _onTheme();
    }
    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        _threats.Built -= _onBuilt; _ranges.Built -= _onBuilt; _allThreats.Built -= _onBuilt; _kept.Built -= _onBuilt;
        if (_s is null) return; _s.Frame.Render -= _onFrame; _s.SettingsChanged -= _onChanged;
        if (IsInstanceValid(_input)) { _input.SelectionChanged -= _onChanged; _input.IntentPressed -= _onIntent; _input.HoveredUnitChanged -= _onHover; }
    }

    /// <summary>Every layer, in the order their labels claim room: the selected and hovered units' first, then T's areas, then V's rings. Rebuilt
    /// only when T adds a layer, so a frame walks it without allocating.</summary>
    Layer[] _layers = [];
    Layer[] Layers => _layers.Length == _all.Count + 5 ? _layers : _layers = [_reach, _keepAway, _picked, _hovered, .. _all, _range];

    void Draw()
    {
        DrawAll();
        var hovered = _input.HoveredUnit;
        IReadOnlyList<Guid> shown = [];
        if (_s.Projection is { } p && _s.Game is { } game)
        {
            _pinned = ThreatRings.Pinned(game, p, _s.MyTeamId, _input.Selected?.PieceId, _pinned);
            if (ThreatRings.Wanted(_s.IsWatching ? null : AskViewModel.Mine(p, game, _s.MyTeamId)))
                shown = ThreatRings.Shown(game, p, _s.MyTeamId, _pinned, hovered, _s.Threats);
        }
        _picked.Show(_pinned is { } a && shown.Contains(a) ? ThreatOf(a) : null);
        _hovered.Show(hovered is { } b && b != _pinned && shown.Contains(b) ? ThreatOf(b) : null);
        _range.Show(RangeWanted());
        _keepAway.Show(KeepAwayWanted(), Ui.Text, Ui.Of(KeepAwayArea.Rim));
        _reach.Show(ReachWanted());
    }

    /// <summary>My team's deploy pick's MidDeploy, a pick by any team of mine.</summary>
    MidDeploy? MyMidDeploy(Tabletop.Protocol.Projection p) =>
        new[] { _s.MyTeamId }.Concat(_s.MyTeamIds.Select(t => (Guid?)t)).Select(t => DeployView.MidDeploy(p, _s.Log, t)).FirstOrDefault(m => m is not null);

    (object Key, Shaped Rings)? _reachKept;
    /// <summary>The area my deploy pick's unit may stand in (ZoneReachArea): its zone grown by the pick's reach, in the team colour; built again only when
    /// the unit, reach, zone, table or legs change.</summary>
    (Shaped Rings, Color Colour)? ReachWanted()
    {
        if (_s.IsWatching || _s.Projection is not { } p || _s.Game is not { } game || MyMidDeploy(p) is not { ZoneReach: not null } mid
            || ZoneReachArea.OrdinalOf(p, game, mid) is not { } ordinal) return null;
        var colour = Palette.Team(ordinal);
        var key = (mid.UnitId, mid.ZoneReach, ordinal, p.Board.Table.Size, p.Board.Table.Zones.Count, _s.Zones);
        if (_reachKept is not { } kept || !Equals(kept.Key, key))
            _reachKept = kept = (key, Shape(ZoneReachArea.Of(p, game, mid, _s.Zones) is { } ring ? [ring] : [], colour));
        return kept.Rings.Rings.Count > 0 ? (kept.Rings, colour) : null;
    }

    /// <summary>The area the placement under way may not reach: my team's deploy pick's KeepAway (DeployView.MidDeploy, a pick by any team of mine), else
    /// my open move Ask's; rebuilt when a listed unit's models move.</summary>
    (Shaped Rings, Color Colour)? KeepAwayWanted()
    {
        if (_s.IsWatching || _s.Projection is not { } p) return null;
        var mine = _s.MyTeamIds;
        var mid = MyMidDeploy(p);
        var move = p.OpenAsks.Values.Select(o => o.Ask).FirstOrDefault(a => a.Legal is MoveRegion { KeepAway: not null } && a.Teams.Any(mine.Contains));
        if (KeepAwayArea.For(mid, move) is not { } keep) return null;
        var build = KeepAwayArea.Builder(p, keep);
        var key = mid?.UnitId ?? move!.Id;
        return _kept.Get(key, KeepAwayArea.Stamps(p, keep), () => Shape(build() is { } ring ? [ring] : [])) is { Rings.Count: > 0 } rings ? (rings, Ui.Of(KeepAwayArea.Shade)) : null;
    }

    /// <summary>T: a layer per enemy team, each its Charge and Shoot areas in the team's colour; the cache keeps the list while nothing it reads changed.</summary>
    void DrawAll()
    {
        IReadOnlyList<(int Ordinal, Shaped Rings)> teams = [];
        if (_allOn && _s.Projection is { } p && _s.Game is { } game)
        {
            var acting = _s.MyTeamId; var threats = _s.Threats;
            IReadOnlyList<RingStamp> stamps = threats.Where(t => ThreatRings.OfAnotherTeam(game, p, acting, t.Key)).OrderBy(t => t.Key).Select(t => RingStamp.Of(p, t.Key, t.Value)).ToList();
            var build = ThreatRings.AllBuilder(game, p, acting, threats);
            teams = _allThreats.Get(acting ?? Guid.Empty, stamps, () => build().Select(t => (t.Ordinal, Shape(t.Rings))).ToList()) ?? [];
        }
        while (_all.Count < teams.Count) { var layer = new Layer(); layer.Attach(this, _labels); _all.Add(layer); }
        for (var i = 0; i < _all.Count; i++) _all[i].Show(i < teams.Count ? (teams[i].Rings, Palette.Team(teams[i].Ordinal)) : null);
    }

    (Shaped Rings, Color Colour)? RangeWanted()
    {
        if (!_s.Settings.RangeRings || _s.Projection is not { } p || _s.Game is not { } game || _s.MyTeamId is not { } team) return null;
        if (RangeRings.UnitFor(p, game, team, _input.Selected?.PieceId) is not { } unit) return null;   // mine only: selected, else my team's activating unit
        var build = RangeRings.Builder(p, unit); var colour = Palette.Team(game.Teams.First(t => t.Id == team).Ordinal);
        return _ranges.Get(unit, [RingStamp.Of(p, unit, null)], () => Shape(build(), colour)) is { } rings ? (rings, colour) : null;
    }

    (Shaped Rings, Color Colour)? ThreatOf(Guid unit)
    {
        if (_s.Projection is not { } p || _s.Game is not { } game || _s.Threats.GetValueOrDefault(unit) is not { } threat) return null;
        var build = ThreatRings.Builder(p, unit, threat);
        var team = game.Participants.FirstOrDefault(x => x.Id == p.Units[unit].ParticipantId)?.TeamId;
        var colour = Palette.Team(game.Teams.FirstOrDefault(t => t.Id == team)?.Ordinal ?? 0);
        return _threats.Get(unit, [RingStamp.Of(p, unit, threat)], () => Shape(build(), colour)) is { } rings ? (rings, colour) : null;
    }

    /// <summary>A set of rings with what each draws from: its look and its fill and label shape, built with the outline off the main thread.</summary>
    /// <summary>With the fill's and the outline's vertex arrays for <c>Colour</c> when the build knew the colour, so the layer only uploads them.</summary>
    public sealed record Shaped(IReadOnlyList<(RangeRing Ring, RingLook Look, RingShape Shape)> Rings, Color? Colour = null, RingArrays.Surface? Fill = null, RingArrays.Surface? Line = null);
    static Shaped Shape(IReadOnlyList<RangeRing> rings, Color? colour = null)
    {
        var shaped = rings.Where(r => r.Segments.Count > 0).Select(r => (r, RingLook.Of(r.Kind), RingShape.Of(r.Segments))).ToList();
        if (colour is not { } c) return new(shaped);
        return new(shaped, c, RingArrays.Fill(shaped.Select(x => (x.Item3, x.Item2)).ToList(), c), RingArrays.Line(shaped.Select(x => (x.r, x.Item2)).ToList(), c));
    }

    /// <summary>Each frame: every shown label under its ring's lowest point on the screen clear of the Hud's panels, then moved apart and off them (RingLabels.Place). A frame projects
    /// at most RingShape.Labels points per ring and builds nothing.</summary>
    public override void _Process(double delta)
    {
        var layers = Layers; var any = false;
        foreach (var l in layers) any |= l.Shown;
        if (!any) return;
        if (GetViewport().GetCamera3D() is not { } cam) return;
        var size = GetViewport().GetVisibleRect().Size; var view = new HudRect(0, 0, size.X, size.Y); var covered = _s.Hud.Obstacles;   // the Hud's panels (the play table fills the window): labels keep off them
        _wanted.Clear(); _order.Clear();
        foreach (var layer in layers)
        foreach (var (candidates, label) in layer.Labels)
        {
            _projected.Clear();
            foreach (var q in candidates) if (!cam.IsPositionBehind(q)) { var s = cam.UnprojectPosition(q); _projected.Add((s.X, s.Y)); }
            if (RingLabels.Anchor(_projected, view, HudFrame.M, covered) is not { } at) { label.Visible = false; continue; }
            var box = label.GetCombinedMinimumSize();
            _wanted.Add(RingLabels.Under(at, box.X, box.Y)); _order.Add(label);
        }
        var placed = RingLabels.Place(_wanted, view, covered);
        for (var i = 0; i < placed.Count; i++) { _order[i].Position = new Vector2(placed[i].X, placed[i].Y); _order[i].Size = new Vector2(placed[i].W, placed[i].H); _order[i].Visible = true; }
    }

    /// <summary>One set of rings: fill, outline and labels. Rebuilds them only when the rings or their colour differ from what is drawn; the rings
    /// come from a cache that recomputes only when the unit, its placements (or its threat) change.</summary>
    sealed class Layer
    {
        readonly MeshInstance3D _fill = new() { MaterialOverride = RingMesh.FillMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        readonly MeshInstance3D _line = new() { MaterialOverride = RingMesh.LineMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        readonly MeshInstance3D _behind = new() { MaterialOverride = RingMesh.BehindMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        Control _parent = null!;
        (Shaped Rings, Color Colour)? _drawn; Color? _words, _rim;
        public readonly List<(Vector3[] Candidates, Control Label)> Labels = new();
        public bool Shown => _drawn is not null;

        public void Attach(Node parent, Control labels) { parent.AddChild(_fill); parent.AddChild(_line); parent.AddChild(_behind); _parent = labels; }
        public void Forget() { Clear(); _drawn = null; }

        /// <summary>The label panels made so far, kept hidden between shows: a new set of rings sets them, making only the ones it lacks.</summary>
        readonly List<PanelContainer> _made = new();

        void Clear()
        {
            _fill.Mesh = null; _line.Mesh = null; _behind.Mesh = null;
            foreach (var (_, label) in Labels) label.Visible = false;
            Labels.Clear();
        }

        /// <summary>`words` and `rim`: the labels' and the outline's colours when they are not the rings' (the keep-away's dark shade would not read on a panel or a dark board).</summary>
        public void Show((Shaped Rings, Color Colour)? want, Color? words = null, Color? rim = null)
        {
            if (want is { } w && _drawn is { } d && ReferenceEquals(w.Rings, d.Rings) && w.Colour == d.Colour && words == _words && rim == _rim) return;
            _words = words; _rim = rim;
            if (want is null && _drawn is null) return;
            Clear(); _drawn = want;
            if (want is not { } shown) return;
            var (rings, colour) = shown;
            var drawn = rings.Rings;
            var built = rings.Colour == colour;   // the build's arrays are for this colour (the outline's too, unless a rim colour is asked)
            _fill.Mesh = built ? RingMesh.Fill(rings.Fill) : RingMesh.Fill(drawn.Select(x => (x.Shape, x.Look)).ToList(), colour);
            _line.Mesh = _behind.Mesh = built && rim is null ? RingMesh.Line(rings.Line) : RingMesh.Line(drawn.Select(x => (x.Ring, x.Look)).ToList(), rim ?? colour);
            foreach (var (ring, look, shape) in drawn)
            {
                if (Labels.Count == _made.Count) { var made = Label(); _parent.AddChild(made); _made.Add(made); }
                var label = _made[Labels.Count];
                Fill(label, ring.Label, look.Icon, words ?? colour);
                Labels.Add((shape.Candidates.Select(q => WorldFrame.ToGodot(new Point3(q.X, q.Y, RingMesh.LineLift))).ToArray(), label));
            }
        }

        /// <summary>The theme's quiet band holding the kind's icon and the label; hidden until _Process places it.</summary>
        static PanelContainer Label()
        {
            var panel = new PanelContainer { ThemeTypeVariation = Variation.Quiet, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };   // a flat band, S inside: the panel material is too heavy over the table
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", Space.S); panel.AddChild(row);
            row.AddChild(new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(22, 22), MouseFilter = Control.MouseFilterEnum.Ignore });
            row.AddChild(new Label { MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        /// <summary>The kind's icon tinted and the words in the team colour, each set only when it differs.</summary>
        static void Fill(PanelContainer panel, string text, string icon, Color colour)
        {
            var row = panel.GetChild(0);
            var image = (TextureRect)row.GetChild(0); var texture = Ui.Icon(icon);
            if (!ReferenceEquals(image.Texture, texture)) image.Texture = texture;
            image.Visible = texture is not null;
            if (image.SelfModulate != colour) image.SelfModulate = colour;
            var words = (Label)row.GetChild(1);
            if (words.Text != text) words.Text = text;
            if (!words.HasThemeColorOverride("font_color") || words.GetThemeColor("font_color") != colour) words.AddThemeColorOverride("font_color", colour);
        }
    }
}
