namespace Tabletop.Client.Core;

/// <summary>What a key asks for (in-game UX spec → Hotkeys). The caller reads it in context: options or rows, Q/E turn the held models (as one body about their centre, or the rows; with Shift each where it stands) and otherwise orbit the camera.</summary>
public enum Intent
{
    Option1, Option2, Option3, Option4, Option5, Option6, Option7, Option8, Option9, Confirm, Cancel, SingleModel, Sight,
    Measure, MeasureHeld, Ping, NextUnit, PreviousUnit, FocusCamera, Undo, ToggleLog, CameraTop, CameraLevel, KeyOverlay, TurnLeft, TurnRight, StopHere,
    CameraForward, CameraBack, CameraLeft, CameraRight, ToggleRoster, ToggleRangeRings, ToggleAllThreats, Remove, MeasureMode, ToggleHitboxes, ToggleTerrainHitboxes, Lower, Raise,
}

/// <summary>The keys the game screen binds, named as Godot.Key names them so the Godot side maps by name. Not `Key`: that would
/// collide with Godot.Key in every file that imports both namespaces.</summary>
public enum HotKey { Key1, Key2, Key3, Key4, Key5, Key6, Key7, Key8, Key9, Enter, KpEnter, Escape, Shift, Q, E, C, L, M, P, G, Z, Tab, Backtab, Space, Home, End, F1, Slash, Question, Ctrl, W, A, S, D, R, V, T, Delete, Backspace, H, B, Pageup, Pagedown,
    F, I, J, K, N, O, U, X, Y, Key0, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, Left, Right, Up, Down, Insert, Alt,
    Comma, Period, Minus, Equal, Semicolon, Apostrophe, Bracketleft, Bracketright, Backslash, Quoteleft }

/// <summary>The current bindings (Main sets them from ClientSettings.KeyOverrides); Of resolves a press through them.</summary>
public static class Hotkeys
{
    public static KeyBindings Bindings { get; set; } = KeyBindings.Default;
    public static Intent? Of(HotKey key, bool shift, bool ctrl) => Bindings.Of(key, shift, ctrl);
}
