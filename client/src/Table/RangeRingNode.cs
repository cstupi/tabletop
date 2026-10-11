using Godot; using Tabletop.Client.Core; using Tabletop.Client.Theming; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>My selected unit's range rings when V has them on (ClientSettings.RangeRings, so the choice is remembered), each labelled with its printed range.</summary>
public partial class RangeRingNode : Node3D
{
    Session _s = null!; TableInput _input = null!; readonly MeshInstance3D _lines = new() { MaterialOverride = LineMesh.Material() }; readonly Node3D _labels = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged; Action<Intent>? _onIntent;

    public void Bind(Session session, TableInput input)
    {
        _s = session; _input = input; AddChild(_lines); AddChild(_labels);
        _onEvents = _ => Draw(); _onChanged = Draw;
        _onIntent = i => { if (i == Intent.ToggleRangeRings) _s.UpdateSettings(x => x with { RangeRings = !x.RangeRings }); };   // SettingsChanged redraws
        session.EventsApplied += _onEvents; session.SettingsChanged += _onChanged; Ui.Changed += _onChanged;                    // the Settings dialog's toggle; the themed team colour
        input.SelectionChanged += _onChanged; input.IntentPressed += _onIntent; Draw();
    }
    public override void _ExitTree()
    {
        Ui.Changed -= _onChanged;
        if (_s is null) return; _s.EventsApplied -= _onEvents; _s.SettingsChanged -= _onChanged;
        if (IsInstanceValid(_input)) { _input.SelectionChanged -= _onChanged; _input.IntentPressed -= _onIntent; }
    }

    void Draw()
    {
        _lines.Mesh = null; foreach (var c in _labels.GetChildren()) c.QueueFree();
        if (!_s.Settings.RangeRings || _s.Projection is not { } p || _s.Game is not { } game || _input.Selected?.PieceId is not { } unit || p.Units.GetValueOrDefault(unit) is not { } u) return;
        if (game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId is not { } team || team != _s.MyTeamId) return;   // mine only
        var colour = Palette.Team(game.Teams.First(t => t.Id == team).Ordinal);
        var rings = RangeRings.Of(p, unit);
        Vector3 At(Point2 q, double z) => WorldFrame.ToGodot(new Point3(q.X, q.Y, z));
        _lines.Mesh = LineMesh.Of(rings.SelectMany(r => r.Segments).Select(s => (At(s.A, 0.05), At(s.B, 0.05), colour)));
        foreach (var r in rings.Where(r => r.Segments.Count > 0))
            _labels.AddChild(new Label3D { Text = r.Label, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = 0.01f, OutlineSize = 10, NoDepthTest = true,
                Modulate = colour, Position = At(r.Segments.MaxBy(s => s.A.Y).A, 0.4) });                                             // at the outline's far edge
    }
}
