namespace Tabletop.Client.Core;

/// <summary>What a key asks for (in-game UX spec → Hotkeys). The caller reads it in context: options or rows, Q/E only while holding a unit.</summary>
public enum Intent
{
    Option1, Option2, Option3, Option4, Option5, Option6, Option7, Option8, Option9, Confirm, Cancel, SingleModel, Sight,
    Measure, MeasureHeld, Ping, NextUnit, PreviousUnit, FocusCamera, UndoMove, ToggleLog, CameraTop, CameraLevel, KeyOverlay, TurnLeft, TurnRight,
}

/// <summary>The keys the game screen binds, named as Godot.Key names them so the Godot side maps by name. Not `Key`: that would
/// collide with Godot.Key in every file that imports both namespaces.</summary>
public enum HotKey { Key1, Key2, Key3, Key4, Key5, Key6, Key7, Key8, Key9, Enter, KpEnter, Escape, Shift, Q, E, L, M, P, G, Z, Tab, Backtab, Space, Home, End, F1, Slash, Question, Ctrl }

public static class Hotkeys
{
    public static Intent? Of(HotKey key, bool shift, bool ctrl) => (key, shift, ctrl) switch
    {
        (HotKey.Ctrl, _, _) => Intent.MeasureHeld,
        (HotKey.Z, _, true) => Intent.UndoMove,
        (_, _, true) => null,
        (>= HotKey.Key1 and <= HotKey.Key9, _, _) => Intent.Option1 + (key - HotKey.Key1),
        (HotKey.Enter or HotKey.KpEnter, _, _) => Intent.Confirm,
        (HotKey.Escape, _, _) => Intent.Cancel,
        (HotKey.Shift, _, _) => Intent.SingleModel,
        (HotKey.L, _, _) => Intent.Sight,
        (HotKey.M, _, _) => Intent.Measure,
        (HotKey.P, _, _) => Intent.Ping,
        (HotKey.G, _, _) => Intent.ToggleLog,
        (HotKey.Tab, false, _) => Intent.NextUnit,
        (HotKey.Tab, true, _) or (HotKey.Backtab, _, _) => Intent.PreviousUnit,
        (HotKey.Space, _, _) => Intent.FocusCamera,
        (HotKey.Home, _, _) => Intent.CameraTop,
        (HotKey.End, _, _) => Intent.CameraLevel,
        (HotKey.F1 or HotKey.Question, _, _) or (HotKey.Slash, true, _) => Intent.KeyOverlay,
        (HotKey.Q, _, _) => Intent.TurnLeft,
        (HotKey.E, _, _) => Intent.TurnRight,
        _ => null,
    };
}
