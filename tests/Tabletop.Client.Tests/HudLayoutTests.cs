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
            Assert.True(r[HudPart.Loading] is { W: 280, H: 36 } strip && strip.Bottom == h - 8, $"{v}: loading {r[HudPart.Loading]}");
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
            (new HudRect(792, 451, 480, 261), new HudRect(632, 182, 640, 261), new HudRect(8, 56, 420, 324)),
            (r[HudPart.Decision], r[HudPart.LogPanel], r[HudPart.Roster]));
    }

    /// <summary>The open log holds the latest roll too: the dice tray gives its slot to the decision box.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_open_log_takes_the_dice_tray_s_place(int w, int h)
    {
        foreach (var v in All().Where(v => v.LogOpen && v.Dice)) Assert.False(HudLayout.Of(w, h, v).ContainsKey(HudPart.Dice), $"{v}");
    }

    /// <summary>Undo and Log are square icon buttons in the top right corner; a theme's taller button face makes them that size.</summary>
    [Fact]
    public void The_corner_buttons_take_the_buttons_actual_size()
    {
        var r = HudLayout.Of(1280, 720, new HudVisible(true, true, true, true, false), buttonSize: 46);
        Assert.Equal(new HudRect(1226, 8, 46, 46), r[HudPart.LogButton]);
        Assert.Equal(new HudRect(1172, 8, 46, 46), r[HudPart.UndoButton]);
        Assert.Equal(62, r[HudPart.Toasts].Y);
        Assert.Equal(new HudRect(1234, 8, 38, 38), HudLayout.Of(1280, 720, new HudVisible(true, true, true, true, false), buttonSize: 20)[HudPart.LogButton]);   // never below 38
    }

    /// <summary>The decision box leaves the table's centre clear: the right column, from below the toasts down to the screen's margin.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_decision_box_sits_flush_at_the_bottom_right(int w, int h)
    {
        foreach (var v in All().Where(v => v.Decision))
        {
            var r = HudLayout.Of(w, h, v);
            var d = r[HudPart.Decision];
            Assert.Equal(w - 8, d.Right);
            Assert.Equal(h - 8, d.Bottom);
            Assert.True(d.Y >= r[HudPart.Toasts].Bottom + 8, $"{v}: {d}");
            if (!v.LogOpen) Assert.Equal(r[HudPart.Toasts].Bottom + 8, v.Dice ? r[HudPart.Dice].Y : d.Y);
        }
    }

    /// <summary>The open log hangs from the top of the right column, below the corner buttons and the toasts, down the right side.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_open_log_opens_from_the_top_right(int w, int h)
    {
        foreach (var v in All().Where(v => v.LogOpen))
        {
            var r = HudLayout.Of(w, h, v);
            var log = r[HudPart.LogPanel];
            Assert.Equal(w - 8, log.Right);
            Assert.Equal(r[HudPart.Toasts].Bottom + 8, log.Y);
            if (v.Decision) Assert.Equal(r[HudPart.Decision].Y - 8, log.Bottom);
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

    /// <summary>A replay bar taller than 112 px (its three rows in the theme's font): the left column ends M above the bar as it is.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_left_column_ends_above_the_replay_bar_s_actual_height(int w, int h)
    {
        foreach (var v in All().Where(v => v.Replay))
        {
            var r = HudLayout.Of(w, h, v, replayH: 150);
            Assert.Equal(new HudRect(8, h - 8 - 150, 420, 150), r[HudPart.Replay]);
            foreach (var part in new[] { HudPart.Roster, HudPart.UnitCard })
                if (r.TryGetValue(part, out var x)) Assert.True(x.Bottom <= r[HudPart.Replay].Y - 8, $"{w}x{h} {v}: {part} {x} reaches the bar");
        }
        Assert.Equal(112, HudLayout.Of(w, h, new HudVisible(true, true, false, false, false, true), replayH: 90)[HudPart.Replay].H);   // never below 112
    }

    /// <summary>A header taller than 96 px (three lines in the theme's font): its slot is as tall as it is, and a part below it starts M under it.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_header_slot_takes_the_header_s_actual_height(int w, int h)
    {
        foreach (var v in All())
        {
            var r = HudLayout.Of(w, h, v, headerH: 130.4f);
            Assert.Equal(131, r[HudPart.Header].H);
            foreach (var part in new[] { HudPart.Decision, HudPart.Dice, HudPart.LogPanel })
                if (r.TryGetValue(part, out var x) && x.SpansX(r[HudPart.Header])) Assert.True(x.Y >= r[HudPart.Header].Bottom + 8, $"{w}x{h} {v}: {part} {x} under the header");
        }
        Assert.Equal(96, HudLayout.Of(w, h, new HudVisible(true, true, true, true, false), headerH: 60)[HudPart.Header].H);   // never below 96
    }

    [Theory, MemberData(nameof(Sizes))]
    public void Undo_and_log_sit_in_the_top_right_corner_above_the_toasts(int w, int h)
    {
        foreach (var v in All())
        {
            var r = HudLayout.Of(w, h, v);
            var (log, undo) = (r[HudPart.LogButton], r[HudPart.UndoButton]);
            Assert.True(log is { Y: 8, W: 38, H: 38 } && log.Right == w - 8, $"{v}: log {log}");
            Assert.True(undo is { Y: 8, W: 38, H: 38 } && undo.Right == log.X - 8, $"{v}: undo {undo}");
            Assert.Equal(log.Bottom + 8, r[HudPart.Toasts].Y);
            Assert.True(r[HudPart.Header].Right <= undo.X - 8, $"{v}: header {r[HudPart.Header]}");
        }
    }

    [Fact]
    public void Below_1280_every_visibility_still_lays_out()
    {
        foreach (var v in All()) Assert.NotEmpty(HudLayout.Of(800, 600, v));
    }
}
