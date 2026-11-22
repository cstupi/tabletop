using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class LoadingViewTests
{
    static EntryState State(SessionPhase phase = SessionPhase.Connected, GameStatus? status = GameStatus.Playing, bool draft = false, bool projection = true,
        bool catchingUp = false, bool tableReady = true, bool entered = false) => new(phase, status, draft, projection, catchingUp, tableReady, entered);

    [Fact]
    public void Entering_says_connecting_then_loading_until_the_table_stands()
    {
        Assert.Equal("Connecting…", LoadingView.Entry(State(SessionPhase.Connecting, status: null, projection: false))?.Text);
        Assert.Equal("Loading game…", LoadingView.Entry(State(status: null, projection: false))?.Text);
        Assert.Equal("Loading game…", LoadingView.Entry(State(projection: false))?.Text);
        Assert.Equal("Loading game…", LoadingView.Entry(State(catchingUp: true))?.Text);
        Assert.Equal("Loading game…", LoadingView.Entry(State(tableReady: false))?.Text);
        Assert.Null(LoadingView.Entry(State()));
    }

    [Fact]
    public void A_lobby_needs_no_board_and_setup_needs_its_draft_drawn()
    {
        Assert.Null(LoadingView.Entry(State(status: GameStatus.Lobby, projection: false, tableReady: false)));
        Assert.Equal("Loading game…", LoadingView.Entry(State(status: GameStatus.Setup, projection: false))?.Text);
        Assert.Equal("Loading game…", LoadingView.Entry(State(status: GameStatus.Setup, draft: true, projection: false, tableReady: false))?.Text);
        Assert.Null(LoadingView.Entry(State(status: GameStatus.Setup, draft: true, projection: false)));
    }

    [Fact]
    public void The_curtain_stays_over_a_standing_table_until_its_meshes_are_in_and_drawn()
    {
        var connecting = LoadingView.Entry(State(SessionPhase.Connecting));
        Assert.Same(connecting, LoadingView.Curtain(connecting, settled: true, 0, 0));
        var models = LoadingView.Curtain(null, settled: false, 3, 12)!;
        Assert.Equal("Loading models 3 of 12", models.Text);
        Assert.True(models.HasBar);
        Assert.Equal("Loading game…", LoadingView.Curtain(null, settled: false, 0, 0)?.Text);   // every file in: the settling frames
        Assert.Null(LoadingView.Curtain(null, settled: true, 0, 0));
        Assert.Null(LoadingView.Curtain(null, settled: true, 13, 14));                          // a late wave: the strip says it
    }

    [Fact]
    public void A_first_connect_that_fails_says_reconnecting_and_a_game_already_shown_keeps_its_table()
    {
        Assert.Equal("Reconnecting…", LoadingView.Entry(State(SessionPhase.Reconnecting, projection: false))?.Text);
        Assert.Null(LoadingView.Entry(State(SessionPhase.Reconnecting, entered: true)));
        Assert.Null(LoadingView.Entry(State(SessionPhase.Connected, catchingUp: true, entered: true)));
    }

    [Fact]
    public void Entry_lines_have_no_bar()
    {
        Assert.False(LoadingView.Entry(State(SessionPhase.Connecting))!.HasBar);
    }

    [Fact]
    public void The_strip_names_a_reconnect_first_then_the_models_left()
    {
        Assert.Equal("Reconnecting…", LoadingView.Strip(SessionPhase.Reconnecting, entered: true, 1, 4)?.Text);
        var models = LoadingView.Strip(SessionPhase.Connected, entered: true, 3, 12)!;
        Assert.Equal("Loading models 3 of 12", models.Text);
        Assert.True(models.HasBar);
        Assert.Equal(0.25, models.Fraction, 3);
        Assert.Null(LoadingView.Strip(SessionPhase.Connected, entered: true, 12, 12));
        Assert.Null(LoadingView.Strip(SessionPhase.Connected, entered: true, 0, 0));
        Assert.Null(LoadingView.Strip(SessionPhase.Reconnecting, entered: false, 0, 3));   // the curtain says it
    }

    [Fact]
    public void Graphics_counts_its_steps()
    {
        var line = LoadingView.Graphics(2, 6);
        Assert.Equal("Preparing graphics…", line.Text);
        Assert.Equal(2, line.Done); Assert.Equal(6, line.Total);
    }
}

public class AssetProgressTests
{
    [Fact]
    public void Counts_each_asset_once_and_starts_over_after_a_wave_ends()
    {
        var p = new AssetProgress();
        p.Started("a"); p.Started("b"); p.Started("a");
        Assert.Equal((0, 2), (p.Done, p.Total));
        p.Finished("a");
        Assert.Equal((1, 2), (p.Done, p.Total));
        p.Finished("a");                                  // a second piece of the same file
        Assert.Equal((1, 2), (p.Done, p.Total));
        p.Started("c");
        Assert.Equal((1, 3), (p.Done, p.Total));
        p.Finished("c"); p.Finished("b");
        Assert.Equal((0, 0), (p.Done, p.Total));          // nothing left: the next wave counts from one
        p.Started("d");
        Assert.Equal((0, 1), (p.Done, p.Total));
    }

    [Fact]
    public void A_finish_without_a_start_is_ignored_and_a_finished_asset_started_again_in_the_wave_is_not_counted_twice()
    {
        var p = new AssetProgress();
        p.Finished("x");
        Assert.Equal((0, 0), (p.Done, p.Total));
        p.Started("a"); p.Started("b"); p.Finished("a"); p.Started("a");
        Assert.Equal((1, 2), (p.Done, p.Total));
    }

    [Fact]
    public void Changed_fires_on_a_change_only()
    {
        var p = new AssetProgress(); var n = 0; p.Changed += () => n++;
        p.Started("a"); p.Started("a"); p.Finished("a"); p.Finished("a");
        Assert.Equal(2, n);
    }
}

public class LoadingGateTests
{
    [Fact]
    public void Shows_only_a_wait_that_lasts_past_the_delay()
    {
        var g = new LoadingGate(TimeSpan.FromMilliseconds(200));
        Assert.False(g.Update(true, TimeSpan.FromMilliseconds(0)));
        Assert.False(g.Update(true, TimeSpan.FromMilliseconds(150)));
        Assert.True(g.Update(true, TimeSpan.FromMilliseconds(200)));
        Assert.False(g.Update(false, TimeSpan.FromMilliseconds(300)));
        Assert.False(g.Update(true, TimeSpan.FromMilliseconds(400)));   // a new wait waits again
        Assert.False(g.Update(true, TimeSpan.FromMilliseconds(590)));
        Assert.True(g.Update(true, TimeSpan.FromMilliseconds(600)));
    }

    [Fact]
    public void A_short_wait_never_shows()
    {
        var g = new LoadingGate(TimeSpan.FromMilliseconds(200));
        Assert.False(g.Update(true, TimeSpan.Zero));
        Assert.False(g.Update(false, TimeSpan.FromMilliseconds(120)));
        Assert.False(g.Update(true, TimeSpan.FromMilliseconds(250)));
    }
}

public class DispatchQueueTests
{
    [Fact]
    public void Runs_in_order_until_the_budget_is_spent_and_always_at_least_one()
    {
        var q = new DispatchQueue(); var ran = new List<int>(); var clock = 0.0;
        for (var i = 0; i < 5; i++) { var n = i; q.Post(() => { ran.Add(n); clock += 10; }); }
        Assert.True(q.Run(() => clock, budgetMs: 25));     // 0, 1, 2: the third pushes past 25 ms
        Assert.Equal([0, 1, 2], ran);
        clock = 1000;
        Assert.False(q.Run(() => clock, budgetMs: 1000));
        Assert.Equal([0, 1, 2, 3, 4], ran);
    }

    [Fact]
    public void A_single_long_action_runs_and_the_rest_wait()
    {
        var q = new DispatchQueue(); var ran = new List<int>(); var clock = 0.0;
        q.Post(() => { ran.Add(0); clock += 500; }); q.Post(() => ran.Add(1));
        Assert.True(q.Run(() => clock, budgetMs: 12));
        Assert.Equal([0], ran);
    }

    [Fact]
    public void An_action_posted_while_running_runs_after_the_queue_in_the_same_turn_when_the_budget_allows()
    {
        var q = new DispatchQueue(); var ran = new List<string>();
        q.Post(() => { ran.Add("a"); q.Post(() => ran.Add("c")); }); q.Post(() => ran.Add("b"));
        Assert.False(q.Run(() => 0, budgetMs: 12));
        Assert.Equal(["a", "b", "c"], ran);
    }

    [Fact]
    public void A_throwing_action_does_not_lose_the_rest()
    {
        var q = new DispatchQueue(); var ran = new List<int>();
        q.Post(() => throw new InvalidOperationException("x")); q.Post(() => ran.Add(1));
        Assert.Throws<InvalidOperationException>(() => q.Run(() => 0, 12));
        q.Run(() => 0, 12);
        Assert.Equal([1], ran);
    }

    [Fact]
    public void Post_reports_when_the_queue_was_idle()
    {
        var q = new DispatchQueue();
        Assert.True(q.Post(() => { }));
        Assert.False(q.Post(() => { }));
        q.Run(() => 0, 12);
        Assert.True(q.Post(() => { }));
    }
}

public class GraphicsWarmupTests
{
    [Fact]
    public void Warms_the_current_quality_first_then_the_others()
    {
        Assert.Equal([GraphicsQuality.Low, GraphicsQuality.Medium, GraphicsQuality.High], GraphicsWarmup.Qualities(GraphicsQuality.Low));
        Assert.Equal([GraphicsQuality.High, GraphicsQuality.Low, GraphicsQuality.Medium], GraphicsWarmup.Qualities(GraphicsQuality.High));
    }

    [Fact]
    public void Runs_again_when_anything_in_the_stamp_changes()
    {
        var stamp = GraphicsWarmup.Stamp("4.7.2", "forward_plus", "vulkan", "AMD Radeon RX 6900 XT", "1.3.260", "0.1.26");
        Assert.False(GraphicsWarmup.Needed(stamp, stamp));
        Assert.True(GraphicsWarmup.Needed(null, stamp));
        Assert.True(GraphicsWarmup.Needed(GraphicsWarmup.Stamp("4.7.2", "forward_plus", "vulkan", "AMD Radeon RX 6900 XT", "1.3.261", "0.1.26"), stamp));   // a driver update
        Assert.True(GraphicsWarmup.Needed(GraphicsWarmup.Stamp("4.7.2", "gl_compatibility", "opengl3", "AMD Radeon RX 6900 XT", "1.3.260", "0.1.26"), stamp));
        Assert.True(GraphicsWarmup.Needed(GraphicsWarmup.Stamp("4.7.2", "forward_plus", "vulkan", "AMD Radeon RX 6900 XT", "1.3.260", "0.1.25"), stamp));
        Assert.True(GraphicsWarmup.Needed(stamp + "\n", stamp) is false);    // a trailing newline in the file is not a change
    }

    [Fact]
    public void Each_quality_is_a_step_of_the_bar()
    {
        Assert.Equal(new LoadingLine("Preparing graphics…", 0, 4), GraphicsWarmup.Line(0, extraSteps: 1));
        Assert.Equal(new LoadingLine("Preparing graphics…", 4, 4), GraphicsWarmup.Line(4, extraSteps: 1));
    }
}

public class WarmupGlbTests
{
    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP8z8Dwn4GBgYGJAQoAHxcCAgAqVtsAAAAASUVORK5CYII=");

    [Fact]
    public void Every_variant_is_a_GLB_the_preflight_accepts()
    {
        foreach (var v in Enum.GetValues<WarmupGlb.Variant>())
            Assert.Null(GlbPreflight.Check(WarmupGlb.Build(v, Png)));
    }

    [Fact]
    public void The_variants_differ()
    {
        var all = Enum.GetValues<WarmupGlb.Variant>().Select(v => Convert.ToBase64String(WarmupGlb.Build(v, Png))).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }
}
