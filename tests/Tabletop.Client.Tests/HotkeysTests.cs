using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class HotkeysTests
{
    [Fact]
    public void Every_key_in_the_spec_s_table_maps_to_its_intent()
    {
        var table = new (HotKey Key, bool Shift, bool Ctrl, Intent? Intent)[]
        {
            (HotKey.Key1, false, false, Intent.Option1), (HotKey.Key5, false, false, Intent.Option5), (HotKey.Key9, false, false, Intent.Option9),
            (HotKey.Enter, false, false, Intent.Confirm), (HotKey.KpEnter, false, false, Intent.Confirm), (HotKey.Escape, false, false, Intent.Cancel),
            (HotKey.Shift, true, false, Intent.SingleModel), (HotKey.L, false, false, Intent.Sight), (HotKey.M, false, false, Intent.Measure),
            (HotKey.P, false, false, Intent.Ping), (HotKey.Tab, false, false, Intent.NextUnit), (HotKey.Tab, true, false, Intent.PreviousUnit),
            (HotKey.Backtab, true, false, Intent.PreviousUnit), (HotKey.Space, false, false, Intent.FocusCamera),
            (HotKey.Z, false, true, Intent.Undo), (HotKey.G, false, false, Intent.ToggleLog),
            (HotKey.Home, false, false, Intent.CameraTop), (HotKey.End, false, false, Intent.CameraLevel),
            (HotKey.F1, false, false, Intent.KeyOverlay), (HotKey.Question, true, false, Intent.KeyOverlay), (HotKey.Slash, true, false, Intent.KeyOverlay),
            (HotKey.Q, false, false, Intent.TurnLeft), (HotKey.E, false, false, Intent.TurnRight), (HotKey.C, false, false, Intent.StopHere),
            (HotKey.Ctrl, false, true, Intent.MeasureHeld), (HotKey.Ctrl, false, false, Intent.MeasureHeld),
            (HotKey.Z, false, false, null), (HotKey.Slash, false, false, null), (HotKey.G, false, true, null), (HotKey.Key1, false, true, null),
            (HotKey.W, false, false, Intent.CameraForward), (HotKey.S, false, false, Intent.CameraBack), (HotKey.A, false, false, Intent.CameraLeft), (HotKey.D, false, false, Intent.CameraRight), (HotKey.R, false, false, Intent.ToggleRoster), (HotKey.V, false, false, Intent.ToggleRangeRings), (HotKey.T, false, false, Intent.ToggleAllThreats), (HotKey.S, false, true, null),
            (HotKey.Delete, false, false, Intent.Remove), (HotKey.H, false, false, Intent.MeasureMode), (HotKey.H, false, true, Intent.MeasureMode), (HotKey.H, true, false, Intent.MeasureMode), (HotKey.Backspace, false, false, Intent.Remove), (HotKey.Delete, false, true, null),
            (HotKey.Pagedown, false, false, Intent.Lower), (HotKey.Pageup, false, false, Intent.Raise), (HotKey.Pagedown, true, false, Intent.Lower), (HotKey.Pageup, true, false, Intent.Raise),
        };
        foreach (var (key, shift, ctrl, intent) in table) Assert.True(intent == Hotkeys.Of(key, shift, ctrl), $"{key} shift={shift} ctrl={ctrl}");
    }

    [Fact]
    public void Hot_key_names_are_godot_key_names()
    {
        // The Godot side maps with Enum.TryParse(keycode.ToString()); these are Godot.Key members as Godot 4.7 spells them.
        string[] godot = ["Key1", "Key2", "Key3", "Key4", "Key5", "Key6", "Key7", "Key8", "Key9", "Enter", "KpEnter", "Escape", "Shift",
            "Q", "E", "C", "L", "M", "P", "G", "Z", "Tab", "Backtab", "Space", "Home", "End", "F1", "Slash", "Question", "Ctrl", "W", "A", "S", "D", "R", "V", "T", "Delete", "Backspace", "H", "B", "Pageup", "Pagedown",
            "F", "I", "J", "K", "N", "O", "U", "X", "Y", "Key0", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Left", "Right", "Up", "Down", "Insert", "Alt",
            "Comma", "Period", "Minus", "Equal", "Semicolon", "Apostrophe", "Bracketleft", "Bracketright", "Backslash", "Quoteleft"];
        Assert.Equal(godot.Order(), Enum.GetNames<HotKey>().Order());
    }
}
