using Godot; using Tabletop.Client.Core; using Tabletop.Client.Hud; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>A row of icons over each unit (UnitMarkers): activated and conditions; wounds and tokens are per model (ModelMarkNodes). Hidden at table level (End) and behind the camera. Its CanvasLayer inherits no theme, so it sets its own.</summary>
public partial class UnitMarkerNodes : Control
{
    Session _s = null!; TableView _view = null!;
    /// <summary>Per unit, the icons its row draws, where it stands, and the row: a render rebuilds only a row whose icons changed.</summary>
    readonly Dictionary<Guid, (IReadOnlyList<MarkerIcon> Icons, Vector3 At, HBoxContainer Row)> _rows = new();
    Action<RenderCause>? _onFrame; Action? _onTheme;

    public void Bind(Session session, TableView view)
    {
        _s = session; _view = view; SetAnchorsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore;
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board | RenderCause.Facts)) != 0) Render(); }; _onTheme = () => { Theme = Ui.Theme; Clear(); Render(); };
        session.Frame.Render += _onFrame; Ui.Changed += _onTheme; _onTheme();
    }
    public override void _ExitTree() { Ui.Changed -= _onTheme; if (_s is null) return; _s.Frame.Render -= _onFrame; }

    void Clear() { foreach (var (_, _, row) in _rows.Values) row.QueueFree(); _rows.Clear(); }

    void Render()
    {
        if (_s.Projection is not { } p) { Clear(); return; }
        var seen = new HashSet<Guid>();
        foreach (var m in UnitMarkers.Of(p))
        {
            seen.Add(m.UnitId);
            var at = WorldFrame.ToGodot(m.Anchor) + Vector3.Up * 0.6f;
            if (_rows.TryGetValue(m.UnitId, out var drawn) && drawn.Icons.SequenceEqual(m.Icons)) { _rows[m.UnitId] = drawn with { At = at }; continue; }
            drawn.Row?.QueueFree();
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            foreach (var i in m.Icons)
            {
                var c = i.Key is { } k && Ui.Icon(k) is not null ? Roster.Icon(k) : Roster.Chip(i.Text);
                c.MouseFilter = MouseFilterEnum.Ignore; row.AddChild(c);
            }
            AddChild(row); _rows[m.UnitId] = (m.Icons, at, row);
        }
        foreach (var gone in _rows.Keys.Where(k => !seen.Contains(k)).ToList()) { _rows[gone].Row.QueueFree(); _rows.Remove(gone); }
    }

    public override void _Process(double delta)
    {
        var cam = _view.Camera; var shown = !CameraFocus.TableLevel(cam.Pitch);
        foreach (var (_, at, row) in _rows.Values)
        {
            row.Visible = shown && !cam.IsPositionBehind(at);
            if (row.Visible) row.Position = cam.UnprojectPosition(at) - new Vector2(row.Size.X / 2, row.Size.Y);
        }
    }
}
