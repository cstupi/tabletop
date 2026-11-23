using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Screens;

/// <summary>Options → Controls: every intent with a key, grouped, each row its name and two key cells. Click a cell and press a key to bind
/// it (Esc cancels, right-click clears); a key bound elsewhere shows the conflict and offers Swap. Options 1–9 and 0, Confirm and Cancel are
/// listed but keep their keys.</summary>
public partial class OptionsScreen
{
    const int KeyCellWidth = 136;
    (Intent Intent, int Slot, Button Cell)? _capture;
    Key _modifierDown = Key.None;

    bool Capturing => _capture is not null;

    void ControlsRows()
    {
        var keys = _model.Keys;
        foreach (var group in KeyBindings.Groups)
        {
            var head = new Label { Text = group.Title.ToUpperInvariant(), ThemeTypeVariation = Variation.Dim, CustomMinimumSize = new Vector2(0, Space.XL), VerticalAlignment = VerticalAlignment.Bottom };
            _rows.AddChild(head);
            var options = group.Intents.Where(i => i is >= Intent.Option1 and <= Intent.Option10).ToList();
            if (options.Count > 0) _rows.AddChild(Fixed("Options 1–9, 0", "1–9, 0", "Pick an option of the open decision."));
            foreach (var intent in group.Intents.Except(options))
                _rows.AddChild(KeyBindings.Rebindable(intent) ? KeyRow(intent, keys) : Fixed(KeyBindings.Name(intent), string.Join(" ", keys.Of(intent).Select(b => string.Join(" + ", KeyBindings.Caps(b))).Distinct()), "This key is fixed."));
        }
    }

    /// <summary>A key that does not change: its caps in a disabled cell.</summary>
    Control Fixed(string name, string caps, string text)
    {
        var cell = new Button { Text = caps, Disabled = true, CustomMinimumSize = new Vector2(KeyCellWidth * 2 + Space.S, 0), FocusMode = Control.FocusModeEnum.All };
        return Frame(name, () => (name, text), cell, cell, null);
    }

    Control KeyRow(Intent intent, KeyBindings keys)
    {
        var cells = new HBoxContainer(); cells.AddThemeConstantOverride("separation", Space.S);
        var bound = keys.Of(intent);
        Button? first = null;
        for (var slot = 0; slot < 2; slot++)
        {
            var s = slot;
            var cell = new Button { Text = slot < bound.Count ? string.Join(" + ", KeyBindings.Caps(bound[slot])) : "—", CustomMinimumSize = new Vector2(KeyCellWidth, 0),
                FocusMode = Control.FocusModeEnum.All, TooltipText = "Click, then press a key. Right-click clears." };
            if (slot < bound.Count) cell.ThemeTypeVariation = "";
            else cell.AddThemeColorOverride("font_color", Ui.DimText);
            cell.Pressed += () => StartCapture(intent, s, cell);
            cell.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } && s < _model.Keys.Of(intent).Count) { _model.Unbind(intent, s); Changed(); }
            };
            cells.AddChild(cell); first ??= cell;
        }
        var name = KeyBindings.Name(intent);
        return Frame(name, () => (name, "Click a key, then press the new key. Esc cancels; right-click clears."), cells, first!, null);
    }

    void StartCapture(Intent intent, int slot, Button cell)
    {
        if (_capture is { } old && IsInstanceValid(old.Cell)) Changed();
        _capture = (intent, slot, cell); _modifierDown = Key.None;
        cell.Text = "Press a key"; cell.AddThemeColorOverride("font_color", Ui.Accent);
        Describe(KeyBindings.Name(intent), "Press the new key. Esc cancels.");
    }

    void EndCapture() { _capture = null; _modifierDown = Key.None; Changed(); }

    /// <summary>A key during capture: Esc cancels; a modifier alone binds when released with no other key between; any other key binds
    /// with the modifiers held. A key the client cannot name keeps waiting.</summary>
    void Capture(InputEventKey k)
    {
        if (_capture is not { } c || k.Echo) return;
        var modifier = k.Keycode is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta;
        if (k.Pressed && k.Keycode == Key.Escape) { EndCapture(); return; }
        if (modifier)
        {
            if (k.Pressed) { _modifierDown = k.Keycode; return; }
            if (_modifierDown != k.Keycode) return;
        }
        else if (!k.Pressed) return;
        if (HotkeyInput.Translate(k.Keycode) is not { } key) { Describe(KeyBindings.Name(c.Intent), $"{OS.GetKeycodeString(k.Keycode)} cannot be bound. Press another key, or Esc."); return; }
        var binding = KeyBindings.Capture(c.Intent, key, k.ShiftPressed, k.CtrlPressed);
        _capture = null; _modifierDown = Key.None;
        Offer(c.Intent, c.Slot, binding);
    }

    /// <summary>Binds at once when the key is free; otherwise names the intent holding it and offers Swap (it takes this slot's old key) or Replace.</summary>
    void Offer(Intent intent, int slot, KeyBinding binding)
    {
        var caps = string.Join(" + ", KeyBindings.Caps(binding));
        if (_model.Conflict(intent, binding) is not var (other, _)) { _model.Bind(intent, slot, binding, swap: false); Changed(); return; }
        if (other == intent) { Changed(); return; }
        Changed();
        if (!KeyBindings.Rebindable(other)) { Describe(KeyBindings.Name(intent), $"{caps} is the {KeyBindings.Name(other)} key, which is fixed."); return; }
        var old = slot < _model.Keys.Of(intent).Count ? string.Join(" + ", KeyBindings.Caps(_model.Keys.Of(intent)[slot])) : null;
        Describe(KeyBindings.Name(intent), old is null ? $"{caps} is bound to {KeyBindings.Name(other)}. Replace leaves {KeyBindings.Name(other)} without it."
            : $"{caps} is bound to {KeyBindings.Name(other)}. Swap gives {KeyBindings.Name(other)} {old}; Replace leaves it without {caps}.");
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Space.S);
        if (old is not null) row.AddChild(Act("Swap", () => { _model.Bind(intent, slot, binding, swap: true); Changed(); }, primary: true));
        row.AddChild(Act("Replace", () => { _model.Bind(intent, slot, binding, swap: false); Changed(); }, primary: old is null));
        row.AddChild(Act("Cancel", () => Describe(KeyBindings.Name(intent), ""), primary: false));
        _descExtra.AddChild(row);
    }

    static Button Act(string text, Action pressed, bool primary)
    {
        var b = new Button { Text = text, ThemeTypeVariation = primary ? Variation.Primary : "", FocusMode = Control.FocusModeEnum.None };
        b.Pressed += pressed;
        return b;
    }
}
