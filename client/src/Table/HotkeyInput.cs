using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Table;

/// <summary>The current key bindings (Hotkeys.Bindings) against Godot's input: a key event's HotKey, and whether an intent's key is
/// held now, for the keys read every frame (camera pan and orbit, turning what is in hand).</summary>
public static class HotkeyInput
{
    /// <summary>HotKey's members carry Godot's names. A key HotKey does not name is no key; IsDefined stops a numeric name parsing.</summary>
    public static HotKey? Translate(Key key) => Enum.TryParse<HotKey>(key.ToString(), out var k) && Enum.IsDefined(k) ? k : null;

    public static Key GodotKey(HotKey key) => Enum.TryParse<Key>(key.ToString(), out var k) ? k : Key.None;

    /// <summary>Whether a key bound to the intent is down with its Ctrl state; Shift is read by the caller (Shift+Q turns each held model).</summary>
    public static bool Held(Intent intent, bool ctrl)
    {
        foreach (var b in Hotkeys.Bindings.Of(intent))
            if ((b.Ctrl is null || b.Ctrl == ctrl) && Input.IsKeyPressed(GodotKey(b.Key))) return true;
        return false;
    }

    /// <summary>A key press resolved through the bindings, or none.</summary>
    public static Intent? Of(InputEventKey k) => Translate(k.Keycode) is { } key ? Hotkeys.Of(key, k.ShiftPressed, k.CtrlPressed) : null;
}
