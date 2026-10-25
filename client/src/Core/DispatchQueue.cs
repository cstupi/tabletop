namespace Tabletop.Client.Core;

/// <summary>
/// The main thread's work posted from the socket, the thread pool and the session's own renders, in order. <see cref="Run"/> takes
/// actions until a frame's budget is spent (at least one), so a burst (entering a game: the welcome, the board, the whole log, the
/// first render) spreads over frames instead of stopping one. An action posted while it runs joins the end of the queue.
/// </summary>
public sealed class DispatchQueue
{
    readonly Queue<Action> _queue = new();
    readonly object _gate = new();

    /// <summary>True when the queue was empty: the caller schedules a run.</summary>
    public bool Post(Action action)
    {
        lock (_gate) { _queue.Enqueue(action); return _queue.Count == 1 && !_running; }
    }

    bool _running;
    public bool Pending { get { lock (_gate) return _queue.Count > 0; } }

    /// <summary>Runs queued actions while <paramref name="clockMs"/> has advanced less than <paramref name="budgetMs"/> since the start.
    /// True when some are left. An action that throws ends the run with its exception; the rest stay queued.</summary>
    public bool Run(Func<double> clockMs, double budgetMs)
    {
        var start = clockMs();
        lock (_gate) _running = true;
        var done = false;
        try
        {
            while (true)
            {
                Action next;
                lock (_gate) { if (!_queue.TryDequeue(out next!)) { _running = false; done = true; return false; } }
                next();
                lock (_gate) if (clockMs() - start >= budgetMs) { _running = false; done = true; return _queue.Count > 0; }
            }
        }
        finally { if (!done) lock (_gate) _running = false; }   // a throw: the caller schedules the rest (Post sees a queue it must run)
    }
}
