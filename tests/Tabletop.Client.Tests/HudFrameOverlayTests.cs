using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>Every overlay over the table keeps off HudFrame.Obstacles, and nothing else: moving a part's row in the table moves what every
/// overlay keeps off (spec 2026-11-21-one-hud-frame-design.md → Tests).</summary>
public class HudFrameOverlayTests
{
    static readonly HudRect View = new(0, 0, 1280, 720);
    static readonly IReadOnlySet<HudPart> Shown = new PlayShown(Roster: false, UnitCard: false, Decision: true, Dice: false, LogOpen: false).Set;

    /// <summary>Log (and Undo beside it) moved from the top right to the bottom left: one row.</summary>
    static readonly IReadOnlyList<HudRow> Moved = HudFrame.Play.Select(r => r.Part == HudPart.LogButton ? r with { X = HudX.Left, Y = HudY.Bottom } : r).ToList();

    /// <summary>Each overlay's placement of a label wanted at the rect, against the frame's obstacles.</summary>
    static readonly (string Name, Func<HudRect, IReadOnlyList<HudRect>, HudRect> Place)[] Overlays =
    [
        ("ring label", (want, o) => RingLabels.Place([want], View, o)[0]),
        ("mark label", (want, o) => MarkLabels.Place([new MarkLabel(Guid.Empty, (want.X + want.W / 2, want.Y), want.W, want.H)], View, o)[0]),
        ("unit actions", (want, o) => UnitActionsPlace.Rect(want.X + want.W / 2, want.Bottom + 8, want.W, want.H, View, o, 8)),
        ("terrain hover", (want, o) => { var (x, y) = CursorPanel.Place((want.X - 22, want.Y - 22), (want.W, want.H), View, o); return new HudRect(x, y, want.W, want.H); }),
    ];

    [Fact]
    public void Moving_a_part_s_row_moves_its_obstacle_for_every_overlay()
    {
        var before = HudFrame.Of(1280, 720, Shown);
        var after = HudFrame.Of(1280, 720, Shown, rows: Moved);
        Assert.Equal(new HudRect(8, 674, 38, 38), after[HudPart.LogButton]);
        Assert.Equal(new HudRect(54, 674, 38, 38), after[HudPart.UndoButton]);
        Assert.True(HudFrame.Of(1280, 720, new PlayShown(true, true, false, false, false).Set, rows: Moved)[HudPart.UnitCard].Bottom <= 674 - 8, "the left column ends above the buttons");
        Assert.Contains(after[HudPart.LogButton], after.Obstacles);
        Assert.DoesNotContain(before[HudPart.LogButton], after.Obstacles);

        // A small label wanted where each button stands: kept off it where it is, left where it was.
        foreach (var (name, place) in Overlays)
            foreach (var part in new[] { HudPart.LogButton, HudPart.UndoButton })
            {
                var oldSpot = before[part] with { W = 30, H = 14 };
                var newSpot = after[part] with { X = after[part].X + 4, W = 30, H = 14 };
                Assert.False(after.Obstacles.Any(place(newSpot, after.Obstacles).Overlaps), $"{name} under the moved {part}");
                Assert.False(before.Obstacles.Any(place(oldSpot, before.Obstacles).Overlaps), $"{name} under {part}");
                Assert.Equal(place(oldSpot, []), place(oldSpot, after.Obstacles));   // nothing there now: placed as with no panel
            }
    }

    [Fact]
    public void A_ring_label_s_anchor_keeps_off_the_moved_part()
    {
        var shown = new PlayShown(Roster: false, UnitCard: false, Decision: true, Dice: false, LogOpen: false).Set;
        var after = HudFrame.Of(1280, 720, shown, rows: Moved);
        var log = after[HudPart.LogButton];
        (float, float) under = (log.X + 10, log.Y + 10), clear = (600, 300);
        Assert.Equal(clear, RingLabels.Anchor([under, clear], View, 8, after.Obstacles));
        Assert.Equal(under, RingLabels.Anchor([under, clear], View, 8, HudFrame.Of(1280, 720, shown).Obstacles));
    }

    [Fact]
    public void The_terrain_hover_panel_flips_off_a_panel_beside_the_cursor()
    {
        var panel = new HudRect(700, 300, 200, 200);
        Assert.Equal((622f, 422f), CursorPanel.Place((600, 400), (100, 50), View, []));
        Assert.Equal((484f, 422f), CursorPanel.Place((600, 400), (100, 50), View, [panel with { X = 610, Y = 410, W = 50, H = 50 }]));
        Assert.Equal((1154f, 634f), CursorPanel.Place((1270, 700), (100, 50), View, []));   // the view's corner: left of and above the cursor
        Assert.Equal((622f, 422f), CursorPanel.Place((600, 400), (100, 50), View, [panel with { X = 0, Y = 0, W = 10, H = 10 }]));
    }
}

/// <summary>Session.Hud: the last layout pass's frame, published once per pass; HudChanged only when it changed.</summary>
public class SessionHudTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-hud-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Publishing_raises_only_on_a_change()
    {
        var s = new Session(new ManagementClient(new HttpClient(new FakeManagement()), new Uri("http://localhost:5100")), new FakeSocketFactory(),
            new SessionStore(Path.Combine(_dir, "s.bin")), ClientSettings.Default, _ => { }, new ImmediateDispatcher(), _ => TimeSpan.Zero);
        var raised = 0; s.HudChanged += () => raised++;
        Assert.Empty(s.Hud.Obstacles);
        var shown = new PlayShown(true, true, true, true, false).Set;
        s.PublishHud(HudFrame.Of(1280, 720, shown));
        s.PublishHud(HudFrame.Of(1280, 720, shown));
        Assert.Equal(1, raised);
        s.PublishHud(HudFrame.Of(1280, 720, new PlayShown(true, true, true, true, true).Set));
        Assert.Equal(2, raised);
        Assert.True(s.Hud.Covered(HudPart.Dice));
    }
}
