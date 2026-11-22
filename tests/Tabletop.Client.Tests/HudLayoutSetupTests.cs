using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>Spec 2026-11-21-setup-screen-layout-design.md → Layout: the setup frame's slots.</summary>
public class HudLayoutSetupTests
{
    public static TheoryData<int, int> Sizes => new() { { 1280, 720 }, { 1366, 768 }, { 1920, 1080 }, { 2560, 1440 } };

    static IEnumerable<SetupVisible> All() => Enumerable.Range(0, 8).Select(i => new SetupVisible((i & 1) != 0, (i & 2) != 0, (i & 4) != 0));

    static bool Inside(HudRect a, HudRect outer) => a.X >= outer.X && a.Y >= outer.Y && a.Right <= outer.Right && a.Bottom <= outer.Bottom;

    [Theory, MemberData(nameof(Sizes))]
    public void No_two_parts_overlap_and_the_table_gets_the_rest(int w, int h)
    {
        foreach (var v in All().Where(v => v.Rail || !v.Panel))
        {
            var r = HudLayout.Setup(w, h, v);
            Assert.All(r.Values, x => Assert.True(x.X >= 0 && x.Y >= 0 && x.Right <= w && x.Bottom <= h, $"{v}: {x} leaves the window"));
            var parts = r.Where(p => p.Key is not (SetupPart.Body or SetupPart.TopBar)).ToList();
            foreach (var a in parts)
                foreach (var b in parts)
                    Assert.True(a.Key == b.Key || !a.Value.Overlaps(b.Value), $"{w}x{h} {v}: {a.Key} {a.Value} overlaps {b.Key} {b.Value}");
            Assert.True(Inside(r[SetupPart.Corner], r[SetupPart.TopBar]), $"{v}: corner");
            foreach (var p in new[] { SetupPart.Card, SetupPart.Toasts, SetupPart.Loading }) Assert.True(Inside(r[p], r[SetupPart.Body]), $"{w}x{h} {v}: {p} {r[p]} outside {r[SetupPart.Body]}");
            foreach (var p in new[] { SetupPart.TopBar, SetupPart.Rail, SetupPart.Panel, SetupPart.Hints })
                if (r.TryGetValue(p, out var x)) Assert.False(x.Overlaps(r[SetupPart.Body]), $"{v}: {p} over the table");
            Assert.Equal(v.Rail, r.ContainsKey(SetupPart.Rail));
            Assert.Equal(v.Panel, r.ContainsKey(SetupPart.Panel));
            Assert.Equal(v.Hints, r.ContainsKey(SetupPart.Hints));
            // The table reaches the right edge and the hint bar (or the foot), and starts where the rail or the panel ends.
            var body = r[SetupPart.Body];
            var left = v.Panel ? r[SetupPart.Panel].Right : v.Rail ? r[SetupPart.Rail].Right : 0;
            Assert.Equal((left, r[SetupPart.TopBar].Bottom, (float)w, v.Hints ? r[SetupPart.Hints].Y : h), (body.X, body.Y, body.Right, body.Bottom));
            Assert.Equal(HudLayout.TopBarH, r[SetupPart.TopBar].H);
            if (v.Rail) Assert.Equal((0f, HudLayout.RailW), (r[SetupPart.Rail].X, r[SetupPart.Rail].W));
            if (v.Panel) Assert.Equal(HudLayout.PanelW, r[SetupPart.Panel].W);
        }
    }

    [Theory, InlineData(1280, 720), InlineData(1281, 721), InlineData(1920, 1009)]
    public void Every_slot_is_whole_pixels(int w, int h)
    {
        foreach (var v in All())
            foreach (var (part, x) in HudLayout.Setup(w, h, v, topH: 57.4f, hintH: 31.2f))
                Assert.True(new[] { x.X, x.Y, x.W, x.H }.All(f => f == MathF.Floor(f)), $"{w}x{h} {v}: {part} {x}");
    }

    [Fact]
    public void A_taller_top_bar_or_hint_bar_moves_the_table_s_edges()
    {
        var r = HudLayout.Setup(1280, 720, new SetupVisible(true, true, true), topH: 70, hintH: 40);
        Assert.Equal((70f, 680f), (r[SetupPart.Body].Y, r[SetupPart.Body].Bottom));
    }
}
