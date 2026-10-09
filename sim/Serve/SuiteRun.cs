using System.Text.Json;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Serve;

/// <summary>One server-sent event: `game` per result, then `done` with the report, or `error` when the run itself failed.</summary>
public sealed record SseMessage(string Event, string Data);

/// <summary>
/// A run in flight or finished: its events so far, kept so a late reader replays them from the start, and its report once done.
/// Readers wait on a signal that every append replaces.
/// </summary>
public sealed class SuiteRun(Guid id, string outDir)
{
    private readonly object gate = new();
    private readonly List<SseMessage> messages = [];
    private readonly List<SuiteGameResult> results = [];
    private TaskCompletionSource tick = NewTick();

    public Guid Id => id;
    public string OutDir => outDir;
    public SuiteReport? Report { get; private set; }
    public bool Finished { get; private set; }

    /// <summary>SuiteReport.Json without indentation: an event's data is one line.</summary>
    private static readonly JsonSerializerOptions Compact = new(SuiteReport.Json) { WriteIndented = false };

    private static TaskCompletionSource NewTick() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>A game's result as soon as it is reported, before the run is done.</summary>
    public SuiteGameResult? ResultOf(string game)
    {
        lock (gate) return results.FirstOrDefault(r => r.Name == game);
    }

    public void Game(SuiteGameResult result)
    {
        lock (gate) results.Add(result);
        Append(new SseMessage("game", JsonSerializer.Serialize(result, Compact)), finished: false);
    }

    public void Done(SuiteReport report)
    {
        Report = report;
        Append(new SseMessage("done", JsonSerializer.Serialize(report, Compact)), finished: true);
    }

    public void Error(string message) =>
        Append(new SseMessage("error", JsonSerializer.Serialize(new { message }, Compact)), finished: true);

    private void Append(SseMessage message, bool finished)
    {
        TaskCompletionSource old;
        lock (gate)
        {
            messages.Add(message);
            Finished = finished;
            old = tick;
            tick = NewTick();
        }
        old.SetResult();
    }

    /// <summary>Every message from the start, waiting for later ones until the run finishes.</summary>
    public async IAsyncEnumerable<SseMessage> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var next = 0;
        while (true)
        {
            SseMessage[] batch;
            Task wait;
            bool finished;
            lock (gate)
            {
                batch = [.. messages.Skip(next)];
                wait = tick.Task;
                finished = Finished;
            }
            foreach (var message in batch) yield return message;
            next += batch.Length;
            if (finished) yield break;
            if (batch.Length == 0) await wait.WaitAsync(ct);
        }
    }
}
