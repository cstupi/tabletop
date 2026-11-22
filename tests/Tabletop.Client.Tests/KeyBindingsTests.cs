using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class KeyBindingsTests
{
    /// <summary>The Hotkeys.Of switch as it stood before the bindings moved into a table (options-screen spec → Controls).</summary>
    static Intent? Legacy(HotKey key, bool shift, bool ctrl) => (key, shift, ctrl) switch
    {
        (HotKey.Ctrl, _, _) => Intent.MeasureHeld,
        (HotKey.Z, _, true) => Intent.Undo,
        (HotKey.H, _, _) => Intent.MeasureMode,
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
        (HotKey.C, _, _) => Intent.StopHere,
        (HotKey.W, _, _) => Intent.CameraForward,
        (HotKey.S, _, _) => Intent.CameraBack,
        (HotKey.A, _, _) => Intent.CameraLeft,
        (HotKey.D, _, _) => Intent.CameraRight,
        (HotKey.R, _, _) => Intent.ToggleRoster,
        (HotKey.V, _, _) => Intent.ToggleRangeRings,
        (HotKey.T, _, _) => Intent.ToggleAllThreats,
        (HotKey.B, true, _) => Intent.ToggleTerrainHitboxes,
        (HotKey.B, false, _) => Intent.ToggleHitboxes,
        (HotKey.Delete or HotKey.Backspace, _, _) => Intent.Remove,
        (HotKey.Pagedown, _, _) => Intent.Lower,
        (HotKey.Pageup, _, _) => Intent.Raise,
        _ => null,
    };

    [Fact]
    public void Default_equals_the_old_switch_for_every_key_and_modifier()
    {
        foreach (var key in Enum.GetValues<HotKey>())
            foreach (var shift in new[] { false, true })
                foreach (var ctrl in new[] { false, true })
                    Assert.True(Legacy(key, shift, ctrl) == KeyBindings.Default.Of(key, shift, ctrl), $"{key} shift={shift} ctrl={ctrl}");
    }

    [Fact]
    public void A_rebind_replaces_the_slot_and_the_old_key_does_nothing()
    {
        var keys = KeyBindings.Default.With(Intent.ToggleRangeRings, 0, new KeyBinding(HotKey.J));
        Assert.Equal(Intent.ToggleRangeRings, keys.Of(HotKey.J, false, false));
        Assert.Equal(Intent.ToggleRangeRings, keys.Of(HotKey.J, true, false));
        Assert.Null(keys.Of(HotKey.V, false, false));
        Assert.Equal("J", keys.Cap(Intent.ToggleRangeRings));
    }

    [Fact]
    public void A_shifted_binding_beats_one_that_ignores_shift()
    {
        var keys = KeyBindings.Default.With(Intent.Ping, 0, new KeyBinding(HotKey.V, Shift: true));
        Assert.Equal(Intent.Ping, keys.Of(HotKey.V, true, false));
        Assert.Equal(Intent.ToggleRangeRings, keys.Of(HotKey.V, false, false));
    }

    [Fact]
    public void Caps_name_the_modifiers_then_the_key()
    {
        Assert.Equal(["Ctrl", "Z"], KeyBindings.Caps(KeyBindings.Default.Of(Intent.Undo)[0]));
        Assert.Equal(["⇧", "B"], KeyBindings.Caps(KeyBindings.Default.Of(Intent.ToggleTerrainHitboxes)[0]));
        Assert.Equal(["PgDn"], KeyBindings.Caps(KeyBindings.Default.Of(Intent.Lower)[0]));
        Assert.Equal("Del", KeyBindings.Default.Cap(Intent.Remove));
    }

    [Fact]
    public void Options_confirm_and_cancel_are_not_rebindable_and_every_bound_intent_is_in_a_group()
    {
        Assert.False(KeyBindings.Rebindable(Intent.Option1));
        Assert.False(KeyBindings.Rebindable(Intent.Confirm));
        Assert.False(KeyBindings.Rebindable(Intent.Cancel));
        Assert.True(KeyBindings.Rebindable(Intent.ToggleLog));
        var grouped = KeyBindings.Groups.SelectMany(g => g.Intents).ToList();
        Assert.Equal(grouped.Count, grouped.Distinct().Count());
        foreach (var intent in Enum.GetValues<Intent>()) Assert.Contains(intent, grouped);
    }

    [Fact]
    public void A_capture_without_modifiers_ignores_shift_and_keeps_the_intents_ctrl_rule()
    {
        Assert.Equal(new KeyBinding(HotKey.J, null, false), KeyBindings.Capture(Intent.Ping, HotKey.J, shift: false, ctrl: false));
        Assert.Equal(new KeyBinding(HotKey.J, true, true), KeyBindings.Capture(Intent.Ping, HotKey.J, shift: true, ctrl: true));
        Assert.Equal(new KeyBinding(HotKey.J, null, null), KeyBindings.Capture(Intent.MeasureMode, HotKey.J, shift: false, ctrl: false));
        Assert.Equal(new KeyBinding(HotKey.Alt, null, false), KeyBindings.Capture(Intent.Ping, HotKey.Alt, shift: false, ctrl: false));
    }
}
