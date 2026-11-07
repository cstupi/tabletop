using Godot; using Tabletop.Client.Core; using Tabletop.Client.Theming; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>My selected unit's range rings when V has them on (ClientSettings.RangeRings, so the choice is remembered), each labelled with its printed range;
/// and the hovered unit of another team's threat rings (ThreatRings over Session.Threats), in its team colour, whatever V says.</summary>
public partial class RangeRingNode : Node3D
{
    Session _s = null!; TableInput _input = null!;
    readonly Layer _range = new(), _threat = new();
    Action<RenderCause>? _onFrame; Action? _onChanged; Action<Intent>? _onIntent; Action<Guid?>? _onHover;
    readonly RangeRingCache _rings = new(); readonly ThreatRingCache _threats = new();

    public void Bind(Session session, TableInput input)
    {
        _s = session; _input = input; _range.Attach(this); _threat.Attach(this);
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Facts | RenderCause.Game)) != 0) Draw(); }; _onChanged = Draw; _onHover = _ => Draw();
        _onIntent = i => { if (i == Intent.ToggleRangeRings) _s.UpdateSettings(x => x with { RangeRings = !x.RangeRings }); };   // SettingsChanged redraws
        session.Frame.Render += _onFrame; session.SettingsChanged += _onChanged; Ui.Changed += _onChanged;                    // the Settings dialog's toggle; the themed team colour
        input.SelectionChanged += _onChanged; input.IntentPressed += _onIntent; input.HoveredUnitChanged += _onHover; Draw();
    }
    public override void _ExitTree()
    {
        Ui.Changed -= _onChanged;
        if (_s is null) return; _s.Frame.Render -= _onFrame; _s.SettingsChanged -= _onChanged;
        if (IsInstanceValid(_input)) { _input.SelectionChanged -= _onChanged; _input.IntentPressed -= _onIntent; _input.HoveredUnitChanged -= _onHover; }
    }

    void Draw() { _range.Show(RangeWanted()); _threat.Show(ThreatWanted()); }

    (IReadOnlyList<RangeRing> Rings, Color Colour)? RangeWanted()
    {
        if (!_s.Settings.RangeRings || _s.Projection is not { } p || _s.Game is not { } game || _input.Selected?.PieceId is not { } unit || p.Units.GetValueOrDefault(unit) is not { } u) return null;
        if (game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId is not { } team || team != _s.MyTeamId) return null;   // mine only
        return (_rings.Of(p, unit), Palette.Team(game.Teams.First(t => t.Id == team).Ordinal));
    }

    (IReadOnlyList<RangeRing> Rings, Color Colour)? ThreatWanted()
    {
        if (_s.Projection is not { } p || _s.Game is not { } game || _input.HoveredUnit is not { } unit || !ThreatRings.OfAnotherTeam(game, p, _s.MyTeamIds, unit)) return null;
        if (_s.Threats.GetValueOrDefault(unit) is not { } threat) return null;
        var team = game.Participants.FirstOrDefault(x => x.Id == p.Units[unit].ParticipantId)?.TeamId;
        return (_threats.Of(p, unit, threat), Palette.Team(game.Teams.FirstOrDefault(t => t.Id == team)?.Ordinal ?? 0));
    }

    /// <summary>One set of outlines and labels. Rebuilds the mesh and labels only when the rings or their colour differ from what is drawn; the rings
    /// themselves come from a cache that recomputes only when the unit, its placements (or its threat) change.</summary>
    sealed class Layer
    {
        readonly MeshInstance3D _lines = new() { MaterialOverride = LineMesh.Material() }; readonly Node3D _labels = new();
        (IReadOnlyList<RangeRing> Rings, Color Colour)? _drawn;      // what the mesh and labels show now
        public void Attach(Node parent) { parent.AddChild(_lines); parent.AddChild(_labels); }

        public void Show((IReadOnlyList<RangeRing> Rings, Color Colour)? want)
        {
            if (want is { } w && _drawn is { } d && ReferenceEquals(w.Rings, d.Rings) && w.Colour == d.Colour) return;
            if (want is null && _drawn is null) return;
            _lines.Mesh = null; foreach (var c in _labels.GetChildren()) c.QueueFree();
            _drawn = want;
            if (want is not { } shown) return;
            var (rings, colour) = shown;
            Vector3 At(Point2 q, double z) => WorldFrame.ToGodot(new Point3(q.X, q.Y, z));
            _lines.Mesh = LineMesh.Of(rings.SelectMany(r => r.Segments).Select(s => (At(s.A, 0.05), At(s.B, 0.05), colour)));
            foreach (var r in rings.Where(r => r.Segments.Count > 0))
                _labels.AddChild(new Label3D { Text = r.Label, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = 0.01f, OutlineSize = 10, NoDepthTest = true,
                    Modulate = colour, Position = At(r.Segments.MaxBy(s => s.A.Y).A, 0.4) });                                             // at the outline's far edge
        }
    }
}
