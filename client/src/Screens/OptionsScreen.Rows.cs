using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Screens;

/// <summary>Options rows: the label on the left edge, the control right-aligned in a fixed column, 48 px tall; hovering or focusing a
/// row shows its description.</summary>
public partial class OptionsScreen
{
    /// <summary>A row frame: the label and a fixed column for the control, registered for keys and the description pane.</summary>
    HBoxContainer Frame(string label, Func<(string Title, string Text)> describe, Control control, Control focus, Action<int>? step)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowHeight), MouseFilter = Control.MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", Space.M);
        row.AddChild(new Label { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Pass });
        var cell = new HBoxContainer { CustomMinimumSize = new Vector2(ControlWidth, 0), Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Pass };
        cell.AddThemeConstantOverride("separation", Space.S);
        cell.AddChild(control);
        row.AddChild(cell);
        var index = _rowList.Count;
        void Show() { var (t, x) = describe(); Describe(t, x); }
        row.SetMeta("describe", true);
        row.MouseEntered += Show;
        focus.FocusEntered += () => { _focused = index; if (!_quietFocus) Show(); };
        foreach (var c in new[] { control, focus }) c.MouseEntered += Show;
        _rowList.Add((row, focus, step));
        return row;
    }

    Control Row(OptionRow row)
    {
        (string, string) Describe() => (row.Label, _model.Description(row));
        switch (row.Kind)
        {
            case OptionKind.Toggle:
            {
                var pick = Pick(["Off", "On"], _model.Value(row), true, v => { _model.Set(row, v); Changed(); });
                return Frame(row.Label, Describe, pick, pick, d => { _model.Set(row, _model.Value(row) == 0 ? 1 : 0); Changed(); });
            }
            case OptionKind.Choice:
            {
                var choices = _model.Choices(row); var enabled = _model.Enabled(row);
                var pick = Pick(choices, _model.Value(row), enabled, v => { _model.Set(row, v); Changed(); });
                return Frame(row.Label, Describe, pick, pick, enabled ? d =>
                {
                    var v = Math.Clamp(_model.Value(row) + d, 0, choices.Count - 1);
                    if (v < _model.Choices(row).Count && v != _model.Value(row)) { _model.Set(row, v); Changed(); }
                } : null);
            }
            case OptionKind.Slider:
            {
                var box = new HBoxContainer { CustomMinimumSize = new Vector2(ControlWidth, 0) }; box.AddThemeConstantOverride("separation", Space.S);
                var slider = new HSlider { MinValue = row.Min, MaxValue = row.Max, Step = row.Step, Value = _model.Value(row), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.All };
                var value = new Label { Text = row.Format(_model.Value(row)), ThemeTypeVariation = Variation.Dim, HorizontalAlignment = HorizontalAlignment.Right,
                    CustomMinimumSize = new Vector2(Space.XXL + Space.L, 0), VerticalAlignment = VerticalAlignment.Center };
                Track(slider);
                slider.ValueChanged += v => { _model.Set(row, (int)v); value.Text = row.Format(_model.Value(row)); Footer(); };
                box.AddChild(slider); box.AddChild(value);
                return Frame(row.Label, Describe, box, slider, d => slider.Value = Math.Clamp(slider.Value + d * row.Step, row.Min, row.Max));
            }
            default:
            {
                var tiles = new HBoxContainer(); tiles.AddThemeConstantOverride("separation", Space.S);
                var group = new ButtonGroup(); Button? first = null;
                for (var i = 0; i < UiThemes.All.Count; i++)
                {
                    var index = i;
                    var tile = ThemeTile(UiThemes.All[i], group, _model.Value(row) == i, () => { _model.Set(row, index); Changed(); });
                    tiles.AddChild(tile); first ??= tile;
                }
                return Frame(row.Label, Describe, tiles, first!, d =>
                {
                    var v = Math.Clamp(_model.Value(row) + d, 0, UiThemes.All.Count - 1);
                    if (v != _model.Value(row)) { _model.Set(row, v); Changed(); }
                });
            }
        }
    }

    /// <summary>A slider's track in the theme's dim text, filled and grabbed in its accent: Godot's default track does not show on a dark panel.</summary>
    static void Track(HSlider slider)
    {
        StyleBoxFlat Bar(Color c) { var b = new StyleBoxFlat { BgColor = c }; b.SetCornerRadiusAll(Space.XS / 2); b.ContentMarginTop = b.ContentMarginBottom = Space.XS; return b; }
        slider.AddThemeStyleboxOverride("slider", Bar(Ui.DimText with { A = 0.45f }));
        slider.AddThemeStyleboxOverride("grabber_area", Bar(Ui.Accent));
        slider.AddThemeStyleboxOverride("grabber_area_highlight", Bar(Ui.Accent));
    }

    /// <summary>A dropdown filling the control column.</summary>
    static OptionButton Pick(IReadOnlyList<string> items, int selected, bool enabled, Action<int> picked)
    {
        var b = new OptionButton { CustomMinimumSize = new Vector2(ControlWidth, 0), Disabled = !enabled, FocusMode = Control.FocusModeEnum.All, FitToLongestItem = false };
        foreach (var item in items) b.AddItem(item);
        b.Selected = Math.Clamp(selected, -1, items.Count - 1);
        b.ItemSelected += i => picked((int)i);
        return b;
    }

    /// <summary>A theme as a tile drawn in its own colours: its panel, its accent border when picked, its name in its accent.</summary>
    static Button ThemeTile(UiTheme t, ButtonGroup group, bool on, Action pick)
    {
        var tile = new Button { Text = t.Name, ToggleMode = true, ButtonGroup = group, ButtonPressed = on, CustomMinimumSize = new Vector2((ControlWidth - Space.S) / 2, RowHeight), FocusMode = Control.FocusModeEnum.All };
        foreach (var (state, picked) in new[] { ("normal", false), ("hover", false), ("pressed", true), ("hover_pressed", true), ("focus", false) })
        {
            var s = new StyleBoxFlat { BgColor = Ui.Of(t.Panel) with { A = 1 }, BorderColor = picked ? Ui.Of(t.Accent) : Ui.Of(t.PanelBorder) };
            s.SetBorderWidthAll(picked ? 3 : 1); s.SetCornerRadiusAll(t.Shape.CornerRadius); s.SetContentMarginAll(Space.S);
            if (state == "focus") { s.DrawCenter = false; s.BorderColor = Ui.Accent; s.SetBorderWidthAll(1); }
            tile.AddThemeStyleboxOverride(state, s);
        }
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            tile.AddThemeColorOverride(name, Ui.Of(t.Accent));
        tile.Toggled += picked => { if (picked) pick(); };
        return tile;
    }
}
