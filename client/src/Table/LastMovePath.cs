using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>
/// The last activation's move path kept on the table until the next activation starts (MovePaths.Last), for every player and in a replay at its
/// position: each segment measured as the server measured it (MovePaths.Measured) on the thread pool, drawn by a MovePathNode in the moving
/// team's colour. A newer log state drops an older answer.
/// </summary>
public partial class LastMovePath : MovePathNode
{
    Session _session = null!; TableView _view = null!; Action<RenderCause>? _onFrame;
    IReadOnlyList<MoveSegment> _shown = []; int _request;

    public void Bind(Session session, TableView view)
    {
        _session = session; _view = view;
        _onFrame = _ => Refresh(); session.Frame.Render += _onFrame;
        Refresh();
    }

    public override void _ExitTree() { if (_session is not null && _onFrame is not null) _session.Frame.Render -= _onFrame; }

    async void Refresh()
    {
        var log = _session.Log;
        var segments = MovePaths.Last(log);
        if (segments.Count == _shown.Count && segments.Zip(_shown).All(x => x.First.Legal == x.Second.Legal && x.First.Moves.SequenceEqual(x.Second.Moves))) return;
        _shown = segments;
        var request = ++_request;
        if (segments.Count == 0 || _session.Projection is not { } p) { Hide(); return; }
        var unit = p.Models.GetValueOrDefault(segments[0].Moves.FirstOrDefault()?.ModelId ?? Guid.Empty)?.UnitId;
        var ordinal = unit is { } u && _session.Game is { } g ? SheetFocus.TeamOf(p, g, u)?.Ordinal : null;
        var measure = MovePaths.KeptLater(segments, p, _view.Volumes, _view.Size);
        var paths = await Task.Run(measure);
        if (request != _request || !IsInstanceValid(this)) return;
        Show(paths, ordinal is { } o ? Palette.Team(o) : Palette.Unassigned);
    }
}
