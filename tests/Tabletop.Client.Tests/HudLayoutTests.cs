using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class HudLayoutTests
{
    public static TheoryData<int, int> Sizes => new() { { 1280, 720 }, { 1920, 1080 }, { 2560, 1440 } };

    static IEnumerable<HudVisible> All() =>
        Enumerable.Range(0, 32).Select(i => new HudVisible((i & 1) != 0, (i & 2) != 0, (i & 4) != 0, (i & 8) != 0, (i & 16) != 0));

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
            if (r.TryGetValue(HudPart.LogPanel, out var log)) Assert.True(log.W == 640 && log.H >= 240, $"{v}: log {log}");
            if (v.Decision && v.Dice)
                Assert.True(r[HudPart.Dice].Bottom + 8 == r[HudPart.Decision].Y && r[HudPart.Dice].X == r[HudPart.Decision].X);
            if (v.Roster && v.UnitCard)
                Assert.True(r[HudPart.Roster].Bottom + 8 == r[HudPart.UnitCard].Y && r[HudPart.Roster].H == r[HudPart.UnitCard].H);
        }
    }

    [Fact]
    public void At_1280_the_open_log_sits_above_the_dice_and_beside_the_header()
    {
        var r = HudLayout.Of(1280, 720, new HudVisible(Roster: true, UnitCard: true, Decision: true, Dice: true, LogOpen: true));
        Assert.Equal(
            (new HudRect(436, 512, 480, 200), new HudRect(632, 136, 640, 264), new HudRect(8, 56, 420, 324)),
            (r[HudPart.Decision], r[HudPart.LogPanel], r[HudPart.Roster]));
    }

    [Fact]
    public void Below_1280_every_visibility_still_lays_out()
    {
        foreach (var v in All()) Assert.NotEmpty(HudLayout.Of(800, 600, v));
    }
}
