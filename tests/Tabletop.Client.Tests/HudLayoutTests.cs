using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class HudLayoutTests
{
    public static TheoryData<int, int> Sizes => new() { { 1280, 720 }, { 1920, 1080 }, { 2560, 1440 } };

    static IEnumerable<HudVisible> All() =>
        Enumerable.Range(0, 64).Select(i => new HudVisible((i & 1) != 0, (i & 2) != 0, (i & 4) != 0, (i & 8) != 0, (i & 16) != 0, (i & 32) != 0));

    [Theory, MemberData(nameof(Sizes))]
    public void No_two_parts_overlap_at_any_visibility(int w, int h)
    {
        foreach (var v in All())
        {
            var r = HudLayout.Of(w, h, v);
            Assert.All(r.Values, x => Assert.True(x.X >= 8 && x.Y >= 8 && x.Right <= w - 8 && x.Bottom <= h - 8, $"{v}: {x} leaves the margin"));
            var parts = r.Where(p => !HudLayout.Overlays.Contains(p.Key)).ToList();
            foreach (var a in parts)
                foreach (var b in parts)
                    Assert.True(a.Key == b.Key || !a.Value.Overlaps(b.Value), $"{w}x{h} {v}: {a.Key} overlaps {b.Key}");
            Assert.Equal(v.LogOpen, r.ContainsKey(HudPart.LogPanel));
            Assert.Equal(v.Replay, r.ContainsKey(HudPart.Replay));
            if (v.Replay) Assert.True(r[HudPart.Replay] is { X: 8, W: 420, H: 112 } bar && bar.Bottom == h - 8, $"{v}: replay {r[HudPart.Replay]}");
            if (r.TryGetValue(HudPart.LogPanel, out var log)) Assert.True(log.W == 640 && log.H >= 240, $"{v}: log {log}");
            if (v.Decision && v.Dice && !v.LogOpen)
                Assert.True(r[HudPart.Dice].Bottom + 8 == r[HudPart.Decision].Y && r[HudPart.Dice].X == r[HudPart.Decision].X);
            Assert.True(r[HudPart.Loading] is { W: 280 } strip && strip.Y == r[HudPart.LogButton].Y && strip.H == r[HudPart.LogButton].H, $"{v}: loading {r[HudPart.Loading]}");
            if (v.Roster && v.UnitCard)
                Assert.True(r[HudPart.Roster].Bottom + 8 == r[HudPart.UnitCard].Y && r[HudPart.Roster].H == r[HudPart.UnitCard].H);
        }
    }

    /// <summary>A box container sums its children's minimums in whole pixels, so a fractional slot height never settles (DecisionBox.Fit).</summary>
    [Theory, InlineData(1280, 720), InlineData(1920, 1009), InlineData(1152, 449), InlineData(1366, 705), InlineData(1281, 721)]
    public void Every_slot_is_whole_pixels(int w, int h)
    {
        foreach (var v in All())
            foreach (var (part, x) in HudLayout.Of(w, h, v))
                Assert.True(new[] { x.X, x.Y, x.W, x.H }.All(f => f == MathF.Floor(f)), $"{w}x{h} {v}: {part} {x}");
    }

    [Fact]
    public void At_1280_the_open_log_takes_the_upper_half_of_the_right_column()
    {
        var r = HudLayout.Of(1280, 720, new HudVisible(Roster: true, UnitCard: true, Decision: true, Dice: true, LogOpen: true));
        Assert.Equal(
            (new HudRect(792, 406, 480, 262), new HudRect(632, 136, 640, 262), new HudRect(8, 56, 420, 324)),
            (r[HudPart.Decision], r[HudPart.LogPanel], r[HudPart.Roster]));
    }

    /// <summary>The open log holds the latest roll too: the dice tray gives its slot to the decision box.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_open_log_takes_the_dice_tray_s_place(int w, int h)
    {
        foreach (var v in All().Where(v => v.LogOpen && v.Dice)) Assert.False(HudLayout.Of(w, h, v).ContainsKey(HudPart.Dice), $"{v}");
    }

    /// <summary>A theme whose buttons are taller than 36 px: the button row is as tall as they are and the right column ends above it.</summary>
    [Fact]
    public void The_button_row_takes_the_buttons_actual_height()
    {
        var r = HudLayout.Of(1280, 720, new HudVisible(true, true, true, true, false), buttonH: 46);
        Assert.Equal(new HudRect(1132, 666, 140, 46), r[HudPart.LogButton]);
        Assert.Equal(r[HudPart.LogButton].Y, r[HudPart.UndoButton].Y);
        Assert.Equal(r[HudPart.LogButton].Y - 8, r[HudPart.Decision].Bottom);
        Assert.Equal(new HudRect(1132, 676, 140, 36), HudLayout.Of(1280, 720, new HudVisible(true, true, true, true, false), buttonH: 20)[HudPart.LogButton]);   // never below 36
    }

    /// <summary>The decision box leaves the table's centre clear: the right column, from below the toasts down to the undo and log buttons.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_decision_box_takes_the_right_column_above_the_buttons(int w, int h)
    {
        foreach (var v in All().Where(v => v.Decision))
        {
            var r = HudLayout.Of(w, h, v);
            var d = r[HudPart.Decision];
            Assert.Equal(w - 8, d.Right);
            Assert.Equal(r[HudPart.LogButton].Y - 8, d.Bottom);
            Assert.True(d.Y >= r[HudPart.Toasts].Bottom + 8, $"{v}: {d}");
            if (!v.LogOpen) Assert.Equal(r[HudPart.Toasts].Bottom + 8, v.Dice ? r[HudPart.Dice].Y : d.Y);
        }
    }

    [Fact]
    public void A_collapsed_roster_gives_the_unit_card_the_column_below_it()
    {
        var r = HudLayout.Of(1280, 720, new HudVisible(Roster: true, UnitCard: true, Decision: false, Dice: false, LogOpen: false));
        Assert.Equal(new HudRect(8, 112, 420, 600), HudLayout.Below(r[HudPart.UnitCard], aboveBottom: 104));   // roster's headers end at 104
        Assert.Equal(r[HudPart.UnitCard], HudLayout.Below(r[HudPart.UnitCard], r[HudPart.Roster].Bottom));     // a full roster keeps the half
        Assert.Equal(0, HudLayout.Below(r[HudPart.UnitCard], 800).H);
    }

    [Theory, MemberData(nameof(Sizes))]
    public void The_undo_button_sits_left_of_the_log_button_in_its_row(int w, int h)
    {
        foreach (var v in All())
        {
            var r = HudLayout.Of(w, h, v);
            Assert.Equal(r[HudPart.LogButton].Y, r[HudPart.UndoButton].Y);
            Assert.Equal(r[HudPart.LogButton].X - 8, r[HudPart.UndoButton].Right);
        }
    }

    [Fact]
    public void Below_1280_every_visibility_still_lays_out()
    {
        foreach (var v in All()) Assert.NotEmpty(HudLayout.Of(800, 600, v));
    }
}
