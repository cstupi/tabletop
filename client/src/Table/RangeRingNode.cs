using Godot; using Tabletop.Client.Core; using Tabletop.Client.Theming; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>My selected unit's range rings when V has them on (ClientSettings.RangeRings, so the choice is remembered), each labelled with its printed range;
/// and the threat rings (ThreatRings over Session.Threats), in the unit's team colour, whatever V says, of the selected unit of another team than the one
/// I act for (kept while I select and place my own units, ThreatRings.Pinned) and of the hovered one; and with T on, every such team's Charge and Shoot areas (ThreatRings.All),
/// in deployment and play alike. Readable rings plan:
/// each ring fills its area in its kind's opacity (RingLook) under a screen-constant outline (RingMesh), and carries a screen-space label, the
/// kind's icon and its words in the team colour, under the outline's point lowest on the screen (RingLabels), never over another label.</summary>
public partial class RangeRingNode : Node3D
{
    Session _s = null!; TableInput _input = null!;
    readonly Layer _picked = new(), _hovered = new(), _range = new(); readonly List<Layer> _all = new();
    bool _allOn; Guid? _pinned;
    readonly CanvasLayer _canvas = new(); readonly Control _labels = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    Action<RenderCause>? _onFrame; Action? _onChanged, _onTheme; Action<Intent>? _onIntent; Action<Guid?>? _onHover;
    readonly RangeRingCache _rings = new(); readonly ThreatRingCache _pickedThreat = new(), _hoveredThreat = new(); readonly AllThreatCache _allThreats = new();
    readonly List<(float X, float Y)> _projected = new(); readonly List<HudRect> _wanted = new(); readonly List<Control> _order = new();

    public void Bind(Session session, TableInput input)
    {
        _s = session; _input = input;
        AddChild(_canvas); _canvas.AddChild(_labels); _labels.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _picked.Attach(this, _labels); _hovered.Attach(this, _labels); _range.Attach(this, _labels);
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Facts | RenderCause.Game)) != 0) Draw(); }; _onChanged = Draw; _onHover = _ => Draw();
        _onTheme = () => { _labels.Theme = Ui.Theme; foreach (var l in Layers) l.Forget(); Draw(); };                           // the themed team colour, font and icons
        _onIntent = i =>
        {
            if (i == Intent.ToggleRangeRings) _s.UpdateSettings(x => x with { RangeRings = !x.RangeRings });                   // SettingsChanged redraws
            else if (i == Intent.ToggleAllThreats) { _allOn = !_allOn; Draw(); }
        };
        session.Frame.Render += _onFrame; session.SettingsChanged += _onChanged; Ui.Changed += _onTheme;                       // the Settings dialog's toggle
        input.SelectionChanged += _onChanged; input.IntentPressed += _onIntent; input.HoveredUnitChanged += _onHover; _onTheme();
    }
    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is null) return; _s.Frame.Render -= _onFrame; _s.SettingsChanged -= _onChanged;
        if (IsInstanceValid(_input)) { _input.SelectionChanged -= _onChanged; _input.IntentPressed -= _onIntent; _input.HoveredUnitChanged -= _onHover; }
    }

    /// <summary>Every layer, in the order their labels claim room: the selected and hovered units' first, then T's areas, then V's rings. Rebuilt
    /// only when T adds a layer, so a frame walks it without allocating.</summary>
    Layer[] _layers = [];
    Layer[] Layers => _layers.Length == _all.Count + 3 ? _layers : _layers = [_picked, _hovered, .. _all, _range];

    void Draw()
    {
        DrawAll();
        var hovered = _input.HoveredUnit;
        IReadOnlyList<Guid> shown = [];
        if (_s.Projection is { } p && _s.Game is { } game)
        {
            _pinned = ThreatRings.Pinned(game, p, _s.MyTeamId, _input.Selected?.PieceId, _pinned);
            shown = ThreatRings.Shown(game, p, _s.MyTeamId, _pinned, hovered, _s.Threats);
        }
        _picked.Show(_pinned is { } a && shown.Contains(a) ? ThreatOf(a, _pickedThreat) : null);
        _hovered.Show(hovered is { } b && b != _pinned && shown.Contains(b) ? ThreatOf(b, _hoveredThreat) : null);
        _range.Show(RangeWanted());
    }

    /// <summary>T: a layer per enemy team, each its Charge and Shoot areas in the team's colour; the cache keeps the list while nothing it reads changed.</summary>
    void DrawAll()
    {
        IReadOnlyList<TeamThreat> teams = _allOn && _s.Projection is { } p && _s.Game is { } game ? _allThreats.Of(game, p, _s.MyTeamId, _s.Threats) : [];
        while (_all.Count < teams.Count) { var layer = new Layer(); layer.Attach(this, _labels); _all.Add(layer); }
        for (var i = 0; i < _all.Count; i++) _all[i].Show(i < teams.Count ? (teams[i].Rings, Palette.Team(teams[i].Ordinal)) : null);
    }

    (IReadOnlyList<RangeRing> Rings, Color Colour)? RangeWanted()
    {
        if (!_s.Settings.RangeRings || _s.Projection is not { } p || _s.Game is not { } game || _input.Selected?.PieceId is not { } unit || p.Units.GetValueOrDefault(unit) is not { } u) return null;
        if (game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId is not { } team || team != _s.MyTeamId) return null;   // mine only
        return (_rings.Of(p, unit), Palette.Team(game.Teams.First(t => t.Id == team).Ordinal));
    }

    (IReadOnlyList<RangeRing> Rings, Color Colour)? ThreatOf(Guid unit, ThreatRingCache cache)
    {
        if (_s.Projection is not { } p || _s.Game is not { } game || _s.Threats.GetValueOrDefault(unit) is not { } threat) return null;
        var team = game.Participants.FirstOrDefault(x => x.Id == p.Units[unit].ParticipantId)?.TeamId;
        return (cache.Of(p, unit, threat), Palette.Team(game.Teams.FirstOrDefault(t => t.Id == team)?.Ordinal ?? 0));
    }

    /// <summary>Each frame: every shown label under its ring's lowest point on the screen, then moved apart (RingLabels.Place). A frame projects
    /// at most RingShape.Labels points per ring and builds nothing.</summary>
    public override void _Process(double delta)
    {
        var layers = Layers; var any = false;
        foreach (var l in layers) any |= l.Shown;
        if (!any) return;
        if (GetViewport().GetCamera3D() is not { } cam) return;
        var size = GetViewport().GetVisibleRect().Size; var view = new HudRect(0, 0, size.X, size.Y);
        _wanted.Clear(); _order.Clear();
        foreach (var layer in layers)
        foreach (var (candidates, label) in layer.Labels)
        {
            _projected.Clear();
            foreach (var q in candidates) if (!cam.IsPositionBehind(q)) { var s = cam.UnprojectPosition(q); _projected.Add((s.X, s.Y)); }
            if (RingLabels.Anchor(_projected, view, HudLayout.M) is not { } at) { label.Visible = false; continue; }
            var box = label.GetCombinedMinimumSize();
            _wanted.Add(RingLabels.Under(at, box.X, box.Y)); _order.Add(label);
        }
        var placed = RingLabels.Place(_wanted, view);
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
        (IReadOnlyList<RangeRing> Rings, Color Colour)? _drawn;
        public readonly List<(Vector3[] Candidates, Control Label)> Labels = new();
        public bool Shown => _drawn is not null;

        public void Attach(Node parent, Control labels) { parent.AddChild(_fill); parent.AddChild(_line); parent.AddChild(_behind); _parent = labels; }
        public void Forget() { Clear(); _drawn = null; }

        void Clear()
        {
            _fill.Mesh = null; _line.Mesh = null; _behind.Mesh = null;
            foreach (var (_, label) in Labels) label.QueueFree();
            Labels.Clear();
        }

        public void Show((IReadOnlyList<RangeRing> Rings, Color Colour)? want)
        {
            if (want is { } w && _drawn is { } d && ReferenceEquals(w.Rings, d.Rings) && w.Colour == d.Colour) return;
            if (want is null && _drawn is null) return;
            Clear(); _drawn = want;
            if (want is not { } shown) return;
            var (rings, colour) = shown;
            var drawn = rings.Where(r => r.Segments.Count > 0).Select(r => (Ring: r, Look: RingLook.Of(r.Kind), Shape: RingShape.Of(r.Segments))).ToList();
            _fill.Mesh = RingMesh.Fill(drawn.Select(x => (x.Shape, x.Look)).ToList(), colour);
            _line.Mesh = _behind.Mesh = RingMesh.Line(drawn.Select(x => (x.Ring, x.Look)).ToList(), colour);
            foreach (var (ring, look, shape) in drawn)
            {
                var label = Label(ring.Label, look.Icon, colour); _parent.AddChild(label);
                Labels.Add((shape.Candidates.Select(q => WorldFrame.ToGodot(new Point3(q.X, q.Y, RingMesh.LineLift))).ToArray(), label));
            }
        }

        /// <summary>The theme's panel holding the kind's icon, tinted, and the label in the team colour.</summary>
        static Control Label(string text, string icon, Color colour)
        {
            var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 6); panel.AddChild(row);
            if (Ui.Icon(icon) is { } texture)
                row.AddChild(new TextureRect { Texture = texture, SelfModulate = colour, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(22, 22), MouseFilter = Control.MouseFilterEnum.Ignore });
            var words = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
            words.AddThemeColorOverride("font_color", colour);
            row.AddChild(words);
            return panel;
        }
    }
}
