namespace Tabletop.Client.Core;

/// <summary>
/// One refresh at a time, at most one start per <c>minInterval</c> for requests. <see cref="Run"/> starts one now unless one is in
/// flight; <see cref="RunIfDue"/> joins one in flight, starts one if the interval has passed since the last start, and otherwise
/// waits for a single trailing refresh at the interval's end, shared by every request in the window.
/// </summary>
public sealed class CoalescedRefresh(Func<Task> refresh, TimeSpan minInterval, Func<DateTimeOffset> now, Func<TimeSpan, Task> delay)
{
    readonly object _lock = new();
    Task? _running, _trailing;
    DateTimeOffset? _lastStart;

    public CoalescedRefresh(Func<Task> refresh, TimeSpan minInterval)
        : this(refresh, minInterval, () => DateTimeOffset.UtcNow, wait => Task.Delay(wait)) { }

    /// <summary>The refresh in flight, or a completed task.</summary>
    public Task Current { get { lock (_lock) return _running is { IsCompleted: false } r ? r : Task.CompletedTask; } }

    public Task Run()
    {
        lock (_lock) return _running is { IsCompleted: false } r ? r : Start();
    }

    public Task RunIfDue()
    {
        lock (_lock)
        {
            if (_running is { IsCompleted: false } r) return r;
            var t = now();
            if (_lastStart is not { } last || t - last >= minInterval) return Start();
            return _trailing ??= Trail(last + minInterval - t, t);
        }
    }

    Task Start()
    {
        _lastStart = now();
        try { return _running = refresh(); }
        catch (Exception e) { return _running = Task.FromException(e); }
    }

    async Task Trail(TimeSpan wait, DateTimeOffset requestedAt)
    {
        await delay(wait);
        Task t;
        lock (_lock)
        {
            _trailing = null;
            if (_running is { IsCompleted: false } r) t = r;
            else if (_lastStart > requestedAt) t = Task.CompletedTask;   // a refresh started after the request has already run
            else t = Start();
        }
        await t;
    }
}
