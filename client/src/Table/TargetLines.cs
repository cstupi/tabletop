using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>Each assigned weapon group's shooters joined to its target, in that target's colour: first target orange, second cyan.</summary>
public partial class TargetLines : Node3D
{
    static readonly Color[] TargetColours = [new(1f, 0.55f, 0.1f), new(0.2f, 0.85f, 0.95f)];
    Session _s = null!; TableView _view = null!;
    readonly MeshInstance3D _lines = new() { MaterialOverride = LineMesh.Material() };
    IReadOnlyList<AskOption> _groups = []; IReadOnlyDictionary<string, Guid> _assigned = new Dictionary<string, Guid>();
    Action<RenderCause>? _onFrame;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _view = view;
        AddChild(_lines);
        _onFrame = c => { if ((c & RenderCause.Events) != 0) Draw(); };   // a model removed or moved mid-pick
        session.Frame.Render += _onFrame;
    }

    public override void _ExitTree() { if (_s is not null) _s.Frame.Render -= _onFrame; }

    /// <summary>The decision box's assignment: its weapon groups and group id → target unit. Empty draws nothing.</summary>
    public void Show(IReadOnlyList<AskOption> groups, IReadOnlyDictionary<string, Guid> assigned) { _groups = groups; _assigned = assigned; Draw(); }

    public void Clear() => Show([], new Dictionary<string, Guid>());

    void Draw()
    {
        _lines.Mesh = null;
        if (_s.Projection is not { } p || _assigned.Count == 0) return;
        var targets = _assigned.Values.Distinct().ToList();
        Vector3? At(Guid model) => p.Models.GetValueOrDefault(model) is { Location: OnTable on } m
            ? WorldFrame.ToGodot(on.Placement.Position) + Vector3.Up * (float)(m.Slot.Height / 2) : null;
        Vector3? Centre(Guid unit)
        {
            var at = p.Models.Values.Where(m => m.UnitId == unit).Select(m => At(m.Id)).OfType<Vector3>().ToList();
            return at.Count == 0 ? null : at.Aggregate(Vector3.Zero, (a, v) => a + v) / at.Count;
        }
        var segments = new List<(Vector3, Vector3, Color)>();
        foreach (var group in _groups)
        {
            if (!_assigned.TryGetValue(group.Id, out var target) || Centre(target) is not { } to) continue;
            var colour = TargetColours[targets.IndexOf(target) % TargetColours.Length];
            var shooters = p.Models.Values.Where(m => m.Slot.Weapons.Any(w => group.Pieces.Contains(w.Id)));
            foreach (var shooter in shooters) if (At(shooter.Id) is { } from) segments.Add((from, to, colour));
        }
        _lines.Mesh = LineMesh.Of(segments);
    }
}
