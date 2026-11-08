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
        Assert.Contains(new KeyRow(["T"], "all enemy threats"), KeyOverlayView.Table.Rows);
    }

    [Fact]
    public void Each_key_the_overlay_names_is_one_the_hotkeys_map()
    {
        // The single-letter caps of Camera, Measure and Table each map to an intent.
        foreach (var row in new[] { KeyOverlayView.Camera, KeyOverlayView.Measure, KeyOverlayView.Table }.SelectMany(g => g.Rows))
            foreach (var key in row.Keys.Where(k => k.Length == 1 && char.IsAsciiLetterUpper(k[0])))
                Assert.NotNull(Hotkeys.Of(Enum.Parse<HotKey>(key), row.Keys.Contains("⇧"), row.Keys.Contains("Ctrl")));
    }
}
