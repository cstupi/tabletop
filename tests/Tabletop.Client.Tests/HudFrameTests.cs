using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>A play visibility, as the tests name it: the parts that come and go.</summary>
public sealed record PlayShown(bool Roster, bool UnitCard, bool Decision, bool Dice, bool LogOpen, bool Replay = false)
{
    public IReadOnlySet<HudPart> Set
    {
        get
        {
            var s = new HashSet<HudPart> { HudPart.Corner, HudPart.Header, HudPart.LogButton, HudPart.UndoButton };
            if (Roster) s.Add(HudPart.Roster);
            if (UnitCard) s.Add(HudPart.UnitCard);
            if (Decision) s.Add(HudPart.Decision);
            if (Dice) s.Add(HudPart.Dice);
            if (LogOpen) s.Add(HudPart.LogPanel);
            if (Replay) s.Add(HudPart.Replay);
            return s;
        }
    }

    public static IEnumerable<PlayShown> All() =>
        Enumerable.Range(0, 64).Select(i => new PlayShown((i & 1) != 0, (i & 2) != 0, (i & 4) != 0, (i & 8) != 0, (i & 16) != 0, (i & 32) != 0));
}

public class HudFrameTests
{
    public static TheoryData<int, int> Sizes => new() { { 1280, 720 }, { 1366, 768 }, { 1920, 1080 }, { 2560, 1440 } };

    static HudFit Measured(IReadOnlyDictionary<HudPart, (float W, float H)> sizes) => (part, _) => sizes.TryGetValue(part, out var s) ? s : null;

    static HudFrame Of(int w, int h, PlayShown v, float buttonSize = HudFrame.CornerButton, float replayH = HudFrame.ReplayH, float headerH = HudFrame.HeaderH) =>
        HudFrame.Of(w, h, v.Set, Measured(new Dictionary<HudPart, (float, float)>
        {
            [HudPart.LogButton] = (buttonSize, buttonSize), [HudPart.UndoButton] = (buttonSize, buttonSize),
            [HudPart.Replay] = (HudFrame.Column, replayH), [HudPart.Header] = (0, headerH),
        }));

    static readonly HudPart[] Overlays = [HudPart.Banner, HudPart.EndMessage, HudPart.Toasts];

    [Theory, MemberData(nameof(Sizes))]
    public void No_two_parts_overlap_at_any_visibility(int w, int h)
    {
        foreach (var v in PlayShown.All())
        {
            var r = Of(w, h, v).Rects;
            Assert.All(r.Values, x => Assert.True(x.X >= 8 && x.Y >= 8 && x.Right <= w - 8 && x.Bottom <= h - 8, $"{v}: {x} leaves the margin"));
            var parts = r.Where(p => !Overlays.Contains(p.Key)).ToList();
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
                Assert.True(r[HudPart.Roster].Bottom + 8 == r[HudPart.UnitCard].Y && Math.Abs(r[HudPart.Roster].H - r[HudPart.UnitCard].H) <= 1);
        }
    }

    /// <summary>A box container sums its children's minimums in whole pixels, so a fractional slot height never settles (DecisionBox.Fit).</summary>
    [Theory, InlineData(1280, 720), InlineData(1920, 1009), InlineData(1152, 449), InlineData(1366, 705), InlineData(1281, 721)]
    public void Every_slot_is_whole_pixels(int w, int h)
    {
        foreach (var v in PlayShown.All())
            foreach (var (part, x) in Of(w, h, v, buttonSize: 41.3f, replayH: 117.5f, headerH: 101.2f).Rects)
                Assert.True(new[] { x.X, x.Y, x.W, x.H }.All(f => f == MathF.Floor(f)), $"{w}x{h} {v}: {part} {x}");
    }

    [Fact]
    public void At_1280_the_open_log_takes_the_upper_half_of_the_right_column()
    {
        var r = Of(1280, 720, new PlayShown(Roster: true, UnitCard: true, Decision: true, Dice: true, LogOpen: true));
        Assert.Equal(
            (new HudRect(792, 416, 480, 296), new HudRect(632, 112, 640, 296), new HudRect(8, 56, 420, 324)),
            (r[HudPart.Decision], r[HudPart.LogPanel], r[HudPart.Roster]));
    }

    /// <summary>The open log holds the latest roll too: the dice tray gives its slot to the decision box, and says it is covered.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_open_log_takes_the_dice_tray_s_place(int w, int h)
    {
        foreach (var v in PlayShown.All())
        {
            var f = Of(w, h, v);
            Assert.Equal(v.LogOpen, f.Covered(HudPart.Dice));
            if (v.LogOpen) Assert.False(f.Has(HudPart.Dice), $"{v}");
        }
    }

    /// <summary>Undo and Log are square icon buttons in the top right corner; a theme's taller button face makes them that size.</summary>
    [Fact]
    public void The_corner_buttons_take_the_buttons_actual_size()
    {
        var r = Of(1280, 720, new PlayShown(true, true, true, true, false), buttonSize: 46);
        Assert.Equal(new HudRect(1226, 8, 46, 46), r[HudPart.LogButton]);
        Assert.Equal(new HudRect(1172, 8, 46, 46), r[HudPart.UndoButton]);
        Assert.Equal(62, r[HudPart.Toasts].Y);
        Assert.Equal(new HudRect(1234, 8, 38, 38), Of(1280, 720, new PlayShown(true, true, true, true, false), buttonSize: 20)[HudPart.LogButton]);   // never below 38
    }

    /// <summary>A part's top in the right column: M below the corner buttons and the header where they span it.</summary>
    static float ColumnTop(HudFrame r, HudRect part) =>
        new[] { r[HudPart.LogButton], r[HudPart.UndoButton], r[HudPart.Header] }.Where(part.SpansX).Select(x => x.Bottom).DefaultIfEmpty(0).Max() + 8;

    /// <summary>The decision box leaves the table's centre clear: the right column, from below the corner buttons and the header down to the screen's margin.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_decision_box_sits_flush_at_the_bottom_right(int w, int h)
    {
        foreach (var v in PlayShown.All().Where(v => v.Decision))
        {
            var r = Of(w, h, v);
            var d = r[HudPart.Decision];
            Assert.Equal(w - 8, d.Right);
            Assert.Equal(h - 8, d.Bottom);
            Assert.True(d.Y >= ColumnTop(r, d), $"{v}: {d}");
            if (!v.LogOpen) Assert.Equal(ColumnTop(r, d), v.Dice ? r[HudPart.Dice].Y : d.Y);
        }
    }

    /// <summary>The open log hangs from the top of the right column, below the corner buttons, down the right side.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_open_log_opens_from_the_top_right(int w, int h)
    {
        foreach (var v in PlayShown.All().Where(v => v.LogOpen))
        {
            var r = Of(w, h, v);
            var log = r[HudPart.LogPanel];
            Assert.Equal(w - 8, log.Right);
            Assert.Equal(ColumnTop(r, log), log.Y);
            if (v.Decision && log.H < HudFrame.LogMaxH) Assert.Equal(r[HudPart.Decision].Y - 8, log.Bottom);
        }
    }

    /// <summary>The unit card starts M below the roster as it measures: a collapsed roster gives it the column below it.</summary>
    [Fact]
    public void A_collapsed_roster_gives_the_unit_card_the_column_below_it()
    {
        var shown = new PlayShown(Roster: true, UnitCard: true, Decision: false, Dice: false, LogOpen: false).Set;
        var r = HudFrame.Of(1280, 720, shown, (part, slot) => part == HudPart.Roster ? (slot.W, 48) : null);   // roster's headers end at 104
        Assert.Equal(new HudRect(8, 56, 420, 48), r[HudPart.Roster]);
        Assert.Equal(new HudRect(8, 56, 420, 324), r.Slots[HudPart.Roster]);
        Assert.Equal(new HudRect(8, 112, HudFrame.CardW, 600), r[HudPart.UnitCard]);
        var full = HudFrame.Of(1280, 720, shown);
        Assert.Equal(new HudRect(8, 388, HudFrame.CardW, 324), full[HudPart.UnitCard]);   // a full roster keeps the half
    }

    /// <summary>A short unit card hangs from its slot's foot (UnitCard.Place against the slot's bottom), and its obstacle is where it draws.</summary>
    [Fact]
    public void A_short_unit_card_keeps_its_slot_s_foot()
    {
        var f = HudFrame.Of(1280, 720, new PlayShown(true, true, false, false, false).Set, (part, slot) => part == HudPart.UnitCard ? (slot.W, 200) : null);
        Assert.Equal(new HudRect(8, 512, HudFrame.CardW, 200), f[HudPart.UnitCard]);
        Assert.Contains(f[HudPart.UnitCard], f.Obstacles);
    }

    /// <summary>The dice tray stands M above the decision box as it measures, and the open log ends M above the box as it measures, up to
    /// LogMaxH; the log starts M below the header as it measures.</summary>
    [Fact]
    public void The_right_column_follows_the_measured_decision_box_and_header()
    {
        var shut = HudFrame.Of(1920, 1080, new PlayShown(true, true, true, true, false).Set, (part, slot) => part switch
        {
            HudPart.Decision => (slot.W, 200), HudPart.Dice => (slot.W, 80), _ => null,
        });
        Assert.Equal(1072 - 200, shut[HudPart.Decision].Y);
        Assert.Equal(new HudRect(1432, 872 - 8 - 80, 480, 80), shut[HudPart.Dice]);
        var open = HudFrame.Of(1280, 720, new PlayShown(true, true, true, false, true).Set, (part, slot) => part switch
        {
            HudPart.Decision => (slot.W, 150), HudPart.Header => (0, 120), _ => null,
        });
        Assert.Equal(new HudRect(632, 136, 640, 712 - 150 - 8 - 136), open[HudPart.LogPanel]);
    }

    /// <summary>A replay bar taller than 112 px (its three rows in the theme's font): the left column ends M above the bar as it is.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_left_column_ends_above_the_replay_bar_s_actual_height(int w, int h)
    {
        foreach (var v in PlayShown.All().Where(v => v.Replay))
        {
            var r = Of(w, h, v, replayH: 150);
            Assert.Equal(new HudRect(8, h - 8 - 150, 420, 150), r[HudPart.Replay]);
            foreach (var part in new[] { HudPart.Roster, HudPart.UnitCard })
                if (r.Rects.TryGetValue(part, out var x)) Assert.True(x.Bottom <= r[HudPart.Replay].Y - 8, $"{w}x{h} {v}: {part} {x} reaches the bar");
        }
        Assert.Equal(112, Of(w, h, new PlayShown(true, true, false, false, false, true), replayH: 90)[HudPart.Replay].H);   // never below 112
    }

    /// <summary>A header taller than 96 px (three lines in the theme's font): its slot is as tall as it is, and a part below it starts M under it.</summary>
    [Theory, MemberData(nameof(Sizes))]
    public void The_header_slot_takes_the_header_s_actual_height(int w, int h)
    {
        foreach (var v in PlayShown.All())
        {
            var r = Of(w, h, v, headerH: 130.4f);
            Assert.Equal(131, r[HudPart.Header].H);
            foreach (var part in new[] { HudPart.Decision, HudPart.Dice, HudPart.LogPanel })
                if (r.Rects.TryGetValue(part, out var x) && x.SpansX(r[HudPart.Header])) Assert.True(x.Y >= r[HudPart.Header].Bottom + 8, $"{w}x{h} {v}: {part} {x} under the header");
        }
        Assert.Equal(96, Of(w, h, new PlayShown(true, true, true, true, false), headerH: 60)[HudPart.Header].H);   // never below 96
    }

    [Theory, MemberData(nameof(Sizes))]
    public void Undo_and_log_sit_in_the_top_right_corner_above_the_toasts(int w, int h)
    {
        foreach (var v in PlayShown.All())
        {
            var r = Of(w, h, v);
            var (log, undo) = (r[HudPart.LogButton], r[HudPart.UndoButton]);
            Assert.True(log is { Y: 8, W: 38, H: 38 } && log.Right == w - 8, $"{v}: log {log}");
            Assert.True(undo is { Y: 8, W: 38, H: 38 } && undo.Right == log.X - 8, $"{v}: undo {undo}");
            Assert.Equal(log.Bottom + 8, r[HudPart.Toasts].Y);
            Assert.True(r[HudPart.Header].Right <= r[HudPart.Toasts].X - 8, $"{v}: header {r[HudPart.Header]}");
        }
    }

    /// <summary>The header is centred on the window where the band beside the corner and the toasts allows.</summary>
    [Fact]
    public void The_header_is_centred_between_the_corner_and_the_toasts()
    {
        Assert.Equal(new HudRect(436, 8, 468, 96), Of(1280, 720, new PlayShown(true, true, true, true, false))[HudPart.Header]);
        Assert.Equal(new HudRect(580, 8, 760, 96), Of(1920, 1080, new PlayShown(true, true, true, true, false))[HudPart.Header]);
    }

    /// <summary>Obstacles: every shown panel as it measures; the toasts, the banner and the end message come and go over everything.</summary>
    [Fact]
    public void Obstacles_are_the_shown_panels_as_they_measure()
    {
        var v = new PlayShown(true, true, true, false, false);
        var f = HudFrame.Of(1280, 720, v.Set, (part, slot) => part == HudPart.Decision ? (slot.W, 100) : null);
        Assert.Equal(new[] { HudPart.Corner, HudPart.LogButton, HudPart.UndoButton, HudPart.Header, HudPart.Roster, HudPart.UnitCard, HudPart.Decision }.Select(p => f[p]), f.Obstacles);
        Assert.Contains(new HudRect(792, 612, 480, 100), f.Obstacles);
        Assert.DoesNotContain(f[HudPart.Loading], f.Obstacles);   // the strip takes its slot always, and is an obstacle only while it shows
        var loading = HudFrame.Of(1280, 720, new HashSet<HudPart>(v.Set) { HudPart.Loading });
        Assert.Contains(loading[HudPart.Loading], loading.Obstacles);
    }

    [Fact]
    public void Below_1280_every_visibility_still_lays_out()
    {
        foreach (var v in PlayShown.All()) Assert.NotEmpty(Of(800, 600, v).Rects);
    }
}
