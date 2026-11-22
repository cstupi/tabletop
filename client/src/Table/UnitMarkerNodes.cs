using Godot; using Tabletop.Client.Core; using Tabletop.Client.Hud; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>A row of icons over each unit (UnitMarkers): activated and conditions, a text chip only over the selected or hovered unit; wounds and tokens are per model (ModelMarkNodes).
/// Hidden at table level (End), behind the camera, and where a nearer row already stands (MarkerDeclutter). Its CanvasLayer inherits no theme, so it sets its own.</summary>
public partial class UnitMarkerNodes : Control
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!; Guid? _hovered;
    /// <summary>Per unit, the icons its row draws, where it stands, and the row: a render rebuilds only a row whose icons changed.</summary>
    readonly Dictionary<Guid, (IReadOnlyList<MarkerIcon> Icons, Vector3 At, HBoxContainer Row, bool Focus)> _rows = new();
    readonly List<PlacedMarker> _placed = new();
    Action<RenderCause>? _onFrame; Action? _onTheme, _onSelection; Action<Guid?>? _onHover;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _view = view; _input = input; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore;
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board | RenderCause.Facts)) != 0) Render(); }; _onTheme = () => { Theme = Ui.Theme; Clear(); Render(); };
        _onSelection = Render; _onHover = u => { _hovered = u; Render(); };
        session.Frame.Render += _onFrame; Ui.Changed += _onTheme; input.SelectionChanged += _onSelection; input.HoveredUnitChanged += _onHover; _onTheme();
    }
    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_input is not null) { _input.SelectionChanged -= _onSelection; _input.HoveredUnitChanged -= _onHover; }
        if (_s is null) return; _s.Frame.Render -= _onFrame;
    }

    /// <summary>A unit's row as drawn this frame, in the table viewport's pixels; null when it has none or it is hidden.</summary>
    public Rect2? RowRect(Guid unit) => _rows.TryGetValue(unit, out var r) && r.Row.Visible ? r.Row.GetRect() : null;

    void Clear() { foreach (var (_, _, row, _) in _rows.Values) row.QueueFree(); _rows.Clear(); }

    void Render()
    {
        if (_s.Projection is not { } p) { Clear(); return; }
        var seen = new HashSet<Guid>();
        Guid[] focus = [.. new[] { _input.Selected?.PieceId, _hovered }.OfType<Guid>()];
        foreach (var m in UnitMarkers.Of(p, c => Ui.Icon(c) is not null, focus))
        {
            seen.Add(m.UnitId);
            var at = WorldFrame.ToGodot(m.Anchor) + Vector3.Up * 0.6f;
            if (_rows.TryGetValue(m.UnitId, out var drawn) && drawn.Icons.SequenceEqual(m.Icons)) { _rows[m.UnitId] = drawn with { At = at, Focus = m.Focus }; continue; }
            drawn.Row?.QueueFree();
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            foreach (var i in m.Icons)
            {
                var c = i.Key is { } k && Ui.Icon(k) is not null ? Roster.Icon(k) : Roster.Chip(i.Text);
                c.MouseFilter = MouseFilterEnum.Ignore; row.AddChild(c);
            }
            AddChild(row); _rows[m.UnitId] = (m.Icons, at, row, m.Focus);
        }
        foreach (var gone in _rows.Keys.Where(k => !seen.Contains(k)).ToList()) { _rows[gone].Row.QueueFree(); _rows.Remove(gone); }
    }

    public override void _Process(double delta)
    {
        var cam = _view.Camera; var level = CameraFocus.TableLevel(cam.Pitch); var eye = cam.GlobalPosition;
        _placed.Clear();
        foreach (var (unit, (_, at, row, focus)) in _rows)
        {
            if (level || cam.IsPositionBehind(at)) { row.Visible = false; continue; }
            row.Position = cam.UnprojectPosition(at) - new Vector2(row.Size.X / 2, row.Size.Y);
            _placed.Add(new PlacedMarker(unit, new HudRect(row.Position.X, row.Position.Y, row.Size.X, row.Size.Y), eye.DistanceTo(at), focus));
        }
        var shown = MarkerDeclutter.Shown(_placed);
        foreach (var m in _placed) _rows[m.UnitId].Row.Visible = shown.Contains(m.UnitId);
    }
}
