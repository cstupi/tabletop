using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class CoalescedRefreshTests
{
    sealed class Rig
    {
        public int Calls;
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public readonly List<TaskCompletionSource> Runs = [];
        public readonly List<(TimeSpan Wait, TaskCompletionSource Done)> Delays = [];
        public readonly CoalescedRefresh Gate;

        public Rig() => Gate = new CoalescedRefresh(
            () => { Calls++; var run = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); lock (Runs) Runs.Add(run); return run.Task; },
            TimeSpan.FromSeconds(5),
            () => Now,
            wait => { var d = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); lock (Delays) Delays.Add((wait, d)); return d.Task; });

        public void Finish() { lock (Runs) foreach (var r in Runs) r.TrySetResult(); }
    }

    [Fact]
    public async Task Requests_while_one_runs_share_it()
    {
        var rig = new Rig();
        var tasks = Enumerable.Range(0, 20).Select(_ => rig.Gate.RunIfDue()).ToList();
        Assert.Equal(1, rig.Calls);
        rig.Finish();
        await Task.WhenAll(tasks);
        Assert.Equal(1, rig.Calls);
    }

    [Fact]
    public async Task Requests_soon_after_one_finished_wait_for_one_trailing_refresh()
    {
        var rig = new Rig();
        var first = rig.Gate.RunIfDue(); rig.Finish(); await first;
        rig.Now += TimeSpan.FromSeconds(2);
        var late = Enumerable.Range(0, 10).Select(_ => rig.Gate.RunIfDue()).ToList();
        Assert.Equal(1, rig.Calls);
        var delay = Assert.Single(rig.Delays);
        Assert.Equal(TimeSpan.FromSeconds(3), delay.Wait);
        Assert.All(late, t => Assert.False(t.IsCompleted));
        rig.Now += TimeSpan.FromSeconds(3);
        delay.Done.SetResult();
        await WaitFor(() => rig.Calls == 2);
        rig.Finish();
        await Task.WhenAll(late);
        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task A_request_after_the_interval_runs_at_once()
    {
        var rig = new Rig();
        var first = rig.Gate.RunIfDue(); rig.Finish(); await first;
        rig.Now += TimeSpan.FromSeconds(5);
        var second = rig.Gate.RunIfDue();
        Assert.Equal(2, rig.Calls);
        Assert.Empty(rig.Delays);
        rig.Finish(); await second;
    }

    [Fact]
    public async Task Run_ignores_the_interval_but_joins_one_in_flight()
    {
        var rig = new Rig();
        var a = rig.Gate.Run(); var b = rig.Gate.Run();
        Assert.Equal(1, rig.Calls);
        rig.Finish(); await Task.WhenAll(a, b);
        var c = rig.Gate.Run();
        Assert.Equal(2, rig.Calls);
        rig.Finish(); await c;
    }

    [Fact]
    public async Task A_trailing_request_is_met_by_a_run_that_started_after_it()
    {
        var rig = new Rig();
        var first = rig.Gate.RunIfDue(); rig.Finish(); await first;
        rig.Now += TimeSpan.FromSeconds(1);
        var trailing = rig.Gate.RunIfDue();
        rig.Now += TimeSpan.FromSeconds(1);
        var forced = rig.Gate.Run(); rig.Finish(); await forced;
        Assert.Equal(2, rig.Calls);
        rig.Now += TimeSpan.FromSeconds(4);
        Assert.Single(rig.Delays).Done.SetResult();
        await trailing;
        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task A_failing_refresh_does_not_jam_the_gate()
    {
        var calls = 0;
        var gate = new CoalescedRefresh(() => { calls++; return Task.FromException(new InvalidOperationException()); }, TimeSpan.Zero, () => DateTimeOffset.UnixEpoch, _ => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunIfDue());
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunIfDue());
        Assert.Equal(2, calls);
    }

    static async Task WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition()) { if (DateTime.UtcNow > until) throw new TimeoutException(); await Task.Yield(); }
    }
}
