using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>Spec 2026-11-21-setup-screen-layout-design.md → Layout: the setup frame's slots.</summary>
public class HudFrameSetupTests
{
    public static TheoryData<int, int> Sizes => new() { { 1280, 720 }, { 1366, 768 }, { 1920, 1080 }, { 2560, 1440 } };

    public sealed record SetupVisible(bool Rail, bool Panel, bool Hints);

    static IEnumerable<SetupVisible> All() => Enumerable.Range(0, 8).Select(i => new SetupVisible((i & 1) != 0, (i & 2) != 0, (i & 4) != 0));

    static IReadOnlyDictionary<HudPart, HudRect> Setup(float w, float h, SetupVisible v, float topH = HudFrame.TopBarH, float hintH = HudFrame.HintH)
    {
        var shown = new HashSet<HudPart> { HudPart.TopBar, HudPart.Corner };
        if (v.Rail) shown.Add(HudPart.Rail);
        if (v.Panel) shown.Add(HudPart.ToolPanel);
        if (v.Hints) shown.Add(HudPart.Hints);
        return HudFrame.Of(w, h, shown, (part, _) => part switch { HudPart.TopBar => (w, topH), HudPart.Hints => (w, hintH), _ => null }, HudFrame.Setup).Rects;
    }

    static bool Inside(HudRect a, HudRect outer) => a.X >= outer.X && a.Y >= outer.Y && a.Right <= outer.Right && a.Bottom <= outer.Bottom;

    [Theory, MemberData(nameof(Sizes))]
    public void No_two_parts_overlap_and_the_table_gets_the_rest(int w, int h)
    {
        foreach (var v in All().Where(v => v.Rail || !v.Panel))
        {
            var r = Setup(w, h, v);
            Assert.All(r.Values, x => Assert.True(x.X >= 0 && x.Y >= 0 && x.Right <= w && x.Bottom <= h, $"{v}: {x} leaves the window"));
            var parts = r.Where(p => p.Key is not (HudPart.Table or HudPart.TopBar)).ToList();
            foreach (var a in parts)
                foreach (var b in parts)
                    Assert.True(a.Key == b.Key || !a.Value.Overlaps(b.Value), $"{w}x{h} {v}: {a.Key} {a.Value} overlaps {b.Key} {b.Value}");
            Assert.True(Inside(r[HudPart.Corner], r[HudPart.TopBar]), $"{v}: corner");
            foreach (var p in new[] { HudPart.TerrainCard, HudPart.Toasts, HudPart.Loading }) Assert.True(Inside(r[p], r[HudPart.Table]), $"{w}x{h} {v}: {p} {r[p]} outside {r[HudPart.Table]}");
            foreach (var p in new[] { HudPart.TopBar, HudPart.Rail, HudPart.ToolPanel, HudPart.Hints })
                if (r.TryGetValue(p, out var x)) Assert.False(x.Overlaps(r[HudPart.Table]), $"{v}: {p} over the table");
            Assert.Equal(v.Rail, r.ContainsKey(HudPart.Rail));
            Assert.Equal(v.Panel, r.ContainsKey(HudPart.ToolPanel));
            Assert.Equal(v.Hints, r.ContainsKey(HudPart.Hints));
            // The table reaches the right edge and the hint bar (or the foot), and starts where the rail or the panel ends.
            var body = r[HudPart.Table];
            var left = v.Panel ? r[HudPart.ToolPanel].Right : v.Rail ? r[HudPart.Rail].Right : 0;
            Assert.Equal((left, r[HudPart.TopBar].Bottom, (float)w, v.Hints ? r[HudPart.Hints].Y : h), (body.X, body.Y, body.Right, body.Bottom));
            Assert.Equal(HudFrame.TopBarH, r[HudPart.TopBar].H);
            if (v.Rail) Assert.Equal((0f, HudFrame.RailW), (r[HudPart.Rail].X, r[HudPart.Rail].W));
            if (v.Panel) Assert.Equal(HudFrame.PanelW, r[HudPart.ToolPanel].W);
        }
    }

    [Theory, InlineData(1280, 720), InlineData(1281, 721), InlineData(1920, 1009)]
    public void Every_slot_is_whole_pixels(int w, int h)
    {
        foreach (var v in All())
            foreach (var (part, x) in Setup(w, h, v, topH: 57.4f, hintH: 31.2f))
                Assert.True(new[] { x.X, x.Y, x.W, x.H }.All(f => f == MathF.Floor(f)), $"{w}x{h} {v}: {part} {x}");
    }

    [Fact]
    public void A_taller_top_bar_or_hint_bar_moves_the_table_s_edges()
    {
        var r = Setup(1280, 720, new SetupVisible(true, true, true), topH: 70, hintH: 40);
        Assert.Equal((70f, 680f), (r[HudPart.Table].Y, r[HudPart.Table].Bottom));
    }
}
