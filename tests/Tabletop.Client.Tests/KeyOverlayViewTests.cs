using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class KeyOverlayViewTests
{
    [Fact]
    public void With_no_ask_the_overlay_is_camera_measure_and_table()
    {
        Assert.Equal(["Camera", "Measure", "Table"], KeyOverlayView.Of(null, null, false).Select(g => g.Title));
    }

    [Fact]
    public void A_move_ask_s_keys_come_first_under_its_title_with_stop_here_only_when_stoppable()
    {
        var groups = KeyOverlayView.Of(AskMode.Move, "Advance", stoppable: true);
        Assert.Equal("Now · Advance", groups[0].Title);
        Assert.Equal(new KeyRow(["⏎"], "commit the move"), groups[0].Rows[0]);
        Assert.Contains(new KeyRow(["C"], "stop here"), groups[0].Rows);
        Assert.Contains(new KeyRow(["Alt"], "place freely"), groups[0].Rows);
        Assert.DoesNotContain(new KeyRow(["C"], "stop here"), KeyOverlayView.Of(AskMode.Move, "Advance", stoppable: false)[0].Rows);
    }

    [Fact]
    public void Every_open_asks_keys_end_with_T_for_every_enemy_threat_and_the_table_lists_it()
    {
        foreach (var mode in new[] { AskMode.Move, AskMode.PickPieces, AskMode.Choose, AskMode.Assign })
            Assert.Equal(new KeyRow(["T"], "all enemy threats"), KeyOverlayView.Of(mode, "Deploy", false)[0].Rows[^1]);
        Assert.Contains(new KeyRow(["T"], "all enemy threats"), KeyOverlayView.Table(KeyBindings.Default).Rows);
    }

    [Fact]
    public void Each_key_the_overlay_names_is_one_the_hotkeys_map()
    {
        // The single-letter caps of Camera, Measure and Table each map to an intent.
        foreach (var row in new[] { KeyOverlayView.Camera(KeyBindings.Default), KeyOverlayView.Measure(KeyBindings.Default), KeyOverlayView.Table(KeyBindings.Default) }.SelectMany(g => g.Rows))
            foreach (var key in row.Keys.Where(k => k.Length == 1 && char.IsAsciiLetterUpper(k[0])))
                Assert.NotNull(KeyBindings.Default.Of(Enum.Parse<HotKey>(key), row.Keys.Contains("⇧"), row.Keys.Contains("Ctrl")));
    }

    static KeyRow R(string does, params string[] keys) => new(keys, does);

    [Fact]
    public void The_default_bindings_give_the_overlay_s_original_caps()
    {
        Assert.Equal([R("move", "W", "A", "S", "D"), R("orbit", "Q", "E"), R("zoom", "Wheel"), R("top-down", "Home"), R("level", "End"),
            R("to the active unit", "Space"), R("next of mine", "Tab"), R("previous of mine", "⇧", "Tab")], KeyOverlayView.Camera(KeyBindings.Default).Rows);
        Assert.Equal([R("ruler from the point", "Ctrl"), R("measure tool", "M"), R("2D or 3D", "H"), R("sight check", "L")], KeyOverlayView.Measure(KeyBindings.Default).Rows);
        Assert.Equal([R("range rings", "V"), R("all enemy threats", "T"), R("model hitboxes", "B"), R("terrain hitboxes", "⇧", "B"), R("roster", "R"), R("log", "G"),
            R("undo", "Ctrl", "Z"), R("ping", "P"), R("remove (setup)", "Del"), R("this overlay", "F1")], KeyOverlayView.Table(KeyBindings.Default).Rows);
        var move = KeyOverlayView.Of(AskMode.Move, "Advance", true, KeyBindings.Default)[0].Rows;
        Assert.Contains(R("turn each held model", "⇧", "Q", "E"), move);
        Assert.Contains(R("lower or raise held", "PgDn", "PgUp"), move);
    }

    [Fact]
    public void A_rebind_shows_in_the_overlay()
    {
        var keys = KeyBindings.Default.With(Intent.ToggleRangeRings, 0, new KeyBinding(HotKey.J));
        Assert.Contains(R("range rings", "J"), KeyOverlayView.Table(keys).Rows);
        Assert.Contains(R("range rings", "J"), KeyOverlayView.Of(null, null, false, keys)[2].Rows);
    }
}
