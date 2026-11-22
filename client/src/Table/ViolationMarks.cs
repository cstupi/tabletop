using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Table;

/// <summary>
/// The models in breach (TableInput.Violations) drawn in the theme's warning colour, and each reason once in a label over its first model
/// ("within 1" of Orcs", "path crosses a model", "overlaps a base", or the server's own reason for a refused commit). The labels ride the
/// models, so a model in hand carries its label with the cursor.
/// </summary>
public partial class ViolationMarks : Node3D
{
    TableView _view = null!; TableInput _input = null!;
    readonly HashSet<Guid> _tinted = new();
    readonly List<(Guid ModelId, Label3D Label)> _labels = [];
    bool _dirty = true;
    Action? _onChanged;

    public void Bind(TableView view, TableInput input)
    {
        _view = view; _input = input;
        _onChanged = () => _dirty = true;
        input.Violations.Changed += _onChanged; input.PickedUp += OnPickedUp;
    }

    /// <summary>The models now in hand come first, so a reason they share rides the cursor.</summary>
    void OnPickedUp(IReadOnlyList<Guid> _) => _dirty = true;

    public override void _ExitTree()
    {
        if (_input is not null && IsInstanceValid(_input)) { _input.Violations.Changed -= _onChanged; _input.PickedUp -= OnPickedUp; }
        if (_view is not null && IsInstanceValid(_view)) foreach (var id in _tinted) if (_view.PieceAt(id) is { } n && IsInstanceValid(n)) n.SetWarning(null);
    }

    /// <summary>Every frame: a redraw can replace a node, so the tint is put on whatever node draws a marked model now, and the labels follow the models.</summary>
    public override void _Process(double delta)
    {
        var marked = _input.Violations.Marked; var warned = _input.Violations.Warned;   // a dangerous terrain warning is drawn like a mark
        var warning = Ui.Warning;
        foreach (var id in _tinted.Where(id => !marked.ContainsKey(id) && !warned.ContainsKey(id)).ToList())
        {
            if (_view.PieceAt(id) is { } n && IsInstanceValid(n)) n.SetWarning(null);
            _tinted.Remove(id);
        }
        foreach (var id in marked.Keys.Concat(warned.Keys))
            if (_view.PieceAt(id) is { } n && IsInstanceValid(n)) { n.SetWarning(warning); _tinted.Add(id); }
        if (_dirty) { _dirty = false; Relabel(); }
        foreach (var (id, label) in _labels)
            if (_view.PieceAt(id) is { } n && IsInstanceValid(n)) { label.Visible = true; label.Position = new Vector3(n.GlobalPosition.X, n.Bounds.End.Y + 0.6f, n.GlobalPosition.Z); }
            else label.Visible = false;
    }

    void Relabel()
    {
        foreach (var (_, label) in _labels) label.QueueFree();
        _labels.Clear();
        var stacked = new Dictionary<Guid, int>();                                 // a model's second label (a mark and a warning) stands over its first
        foreach (var (id, reason) in _input.Violations.Labels(_input.HeldModels.Concat(_input.Violations.Marked.Keys.Concat(_input.Violations.Warned.Keys).Distinct().OrderBy(k => k))))
        {
            var below = stacked.GetValueOrDefault(id); stacked[id] = below + 1;
            var label = new Label3D
            {
                Text = reason, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 36, PixelSize = Palette.ScreenPixel, FixedSize = true,
                OutlineSize = 10, NoDepthTest = true, Modulate = Ui.Warning, Visible = false, Offset = new Vector2(0, 44 * below),
            };
            AddChild(label); _labels.Add((id, label));
        }
    }
}
