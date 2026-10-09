using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Bottom centre, above the decision box: the latest roll's dice and how many passed, until the next roll.</summary>
public partial class DiceTray : PanelContainer
{
    static readonly Color Pass = new(0.35f, 0.8f, 0.4f), Fail = new(0.5f, 0.5f, 0.5f);
    Session _s = null!; Control? _below;
    readonly VBoxContainer _rolls = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard, _place;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.Minsize, 8);
        GrowHorizontal = GrowDirection.Both; GrowVertical = GrowDirection.Begin;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_rolls);
        _onEvents = _ => Render(); _onBoard = Render;
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard;
        Render();
    }

    /// <summary>Sit above the decision box, whatever its height: re-placed when it resizes or shows or hides.</summary>
    public void Follow(Control below)
    {
        _below = below; _place = Place;
        below.Resized += _place; below.VisibilityChanged += _place;
        Place();
    }

    public override void _ExitTree()
    {
        if (_s is not null) { _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }
        if (_below is not null && IsInstanceValid(_below)) { _below.Resized -= _place; _below.VisibilityChanged -= _place; }
    }

    void Place()
    {
        var lift = _below is not null && IsInstanceValid(_below) && _below.Visible ? _below.Size.Y + 16 : 8;
        OffsetBottom = -lift;
        OffsetTop = OffsetBottom - GetCombinedMinimumSize().Y;
    }

    void Render()
    {
        foreach (var c in _rolls.GetChildren()) { _rolls.RemoveChild(c); c.QueueFree(); }
        var step = EventApplier.EffectiveLog(_s.Log).Select(e => e.Payload).OfType<StepCompleted>().LastOrDefault(x => x.Rolls.Count > 0);
        Visible = step is not null;
        if (step is null) return;
        // A step may hold several rolls (one pool per Quality value, p.4): one row each, told apart by threshold.
        foreach (var roll in step.Rolls)
        {
            var row = new HBoxContainer(); _rolls.AddChild(row);
            row.AddChild(new Label { Text = roll.Threshold is { } t ? $"{roll.Roll} {t}+" : roll.Roll });
            foreach (var die in roll.Dice) row.AddChild(Face(die));
            var mods = roll.Modifiers.Count == 0 ? "" : "  " + string.Join(", ", roll.Modifiers.Select(m => $"{m.Amount:+#;-#;0} {m.Source}"));
            row.AddChild(new Label { Text = $"  {roll.Dice.Count(d => d.Success)} of {roll.Dice.Count}{mods}" });
        }
        Place();
    }

    static Control Face(Die d)
    {
        var face = new PanelContainer { SelfModulate = d.Success ? Pass : Fail, CustomMinimumSize = new Vector2(28, 28) };
        face.AddChild(new Label { Text = d.Modified == d.Raw ? $"{d.Raw}" : $"{d.Raw}→{d.Modified}", HorizontalAlignment = HorizontalAlignment.Center });
        return face;
    }
}
