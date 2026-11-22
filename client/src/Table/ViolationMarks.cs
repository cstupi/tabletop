using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Table;

/// <summary>
/// The models in breach (TableInput.Violations) drawn in the theme's warning colour, and each reason once in a short label over its first model
/// (`Too close 1"`, `Through a model`, `On a base`, `Out of coherency`, `Dangerous terrain test`: Violations.Labels, MarkWords); the full
/// sentence is the decision box's. The labels are controls in the table viewport's own canvas layer, placed each frame by MarkLabels: a model's
/// labels stacked Space.XS apart over it, no two overlapping, all off the Hud's panels (Covered). A model in hand carries its labels with the cursor.
/// </summary>
public partial class ViolationMarks : Node3D
{
    TableView _view = null!; TableInput _input = null!;
    readonly HashSet<Guid> _tinted = new();
    readonly CanvasLayer _layer = new();
    readonly List<(Guid ModelId, Label Label)> _labels = [];
    readonly List<MarkLabel> _wanted = [];
    readonly List<Label> _order = [];
    LabelSettings? _look;
    bool _dirty = true;
    Action? _onChanged, _onTheme;

    /// <summary>The Hud's panels over the table, in the table viewport's pixels (GameScreen.Covered).</summary>
    public Func<IReadOnlyList<HudRect>>? Covered;

    public void Bind(TableView view, TableInput input)
    {
        _view = view; _input = input;
        AddChild(_layer);
        _onChanged = () => _dirty = true;
        _onTheme = () => { _look = null; _dirty = true; };
        input.Violations.Changed += _onChanged; input.PickedUp += OnPickedUp; Ui.Changed += _onTheme;
    }

    /// <summary>The models now in hand come first, so a reason they share rides the cursor.</summary>
    void OnPickedUp(IReadOnlyList<Guid> _) => _dirty = true;

    public override void _ExitTree()
    {
        if (_input is not null && IsInstanceValid(_input)) { _input.Violations.Changed -= _onChanged; _input.PickedUp -= OnPickedUp; }
        if (_onTheme is not null) Ui.Changed -= _onTheme;
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
        Place();
    }

    void Place()
    {
        if (_labels.Count == 0) return;
        if (GetViewport().GetCamera3D() is not { } cam) return;
        _wanted.Clear(); _order.Clear();
        foreach (var (id, label) in _labels)
        {
            label.Visible = false;
            if (_view.PieceAt(id) is not { } n || !IsInstanceValid(n)) continue;
            var head = new Vector3(n.GlobalPosition.X, n.Bounds.End.Y + 0.3f, n.GlobalPosition.Z);
            if (cam.IsPositionBehind(head)) continue;
            var at = cam.UnprojectPosition(head); var box = label.GetCombinedMinimumSize();
            _wanted.Add(new MarkLabel(id, (at.X, at.Y), box.X, box.Y)); _order.Add(label);
        }
        var size = GetViewport().GetVisibleRect().Size;
        var placed = MarkLabels.Place(_wanted, new HudRect(0, 0, size.X, size.Y), Covered?.Invoke());
        for (var i = 0; i < placed.Count; i++) { _order[i].Position = new Vector2(placed[i].X, placed[i].Y); _order[i].Visible = true; }
    }

    void Relabel()
    {
        foreach (var (_, label) in _labels) label.QueueFree();
        _labels.Clear();
        _look ??= new LabelSettings { FontSize = 18, FontColor = Ui.Warning, OutlineSize = 6, OutlineColor = Ui.Background };
        var order = _input.HeldModels.Concat(_input.Violations.Marked.Keys.Concat(_input.Violations.Warned.Keys).Distinct().OrderBy(k => k));
        foreach (var (id, words) in _input.Violations.Labels(order))
        {
            var label = new Label { Text = words, LabelSettings = _look, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _layer.AddChild(label); _labels.Add((id, label));
        }
    }
}
