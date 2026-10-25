using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What a wait shows (spec 2026-10-25-loading-design.md): a line of text, and a bar when the work is counted.</summary>
public sealed record LoadingLine(string Text, int Done = 0, int Total = 0)
{
    public bool HasBar => Total > 0;
    public double Fraction => Total > 0 ? Math.Clamp(Done / (double)Total, 0, 1) : 0;
}

/// <summary>Where entering a game stands. <c>Entered</c>: the game screen has shown its table once since the player chose the game.</summary>
public sealed record EntryState(SessionPhase Phase, GameStatus? Status, bool HasDraft, bool HasProjection, bool CatchingUp, bool TableReady, bool Entered);

public static class LoadingView
{
    public const string Connecting = "Connecting…", Reconnecting = "Reconnecting…", LoadingGame = "Loading game…", PreparingGraphics = "Preparing graphics…";

    /// <summary>The curtain over the game screen until its first table stands: null once the game can be shown. A lobby has no table;
    /// setup waits for its draft; play for the projection, the log caught up and the table's first draw (<c>TableReady</c>).</summary>
    public static LoadingLine? Entry(EntryState s)
    {
        if (s.Entered) return null;
        if (s.Phase == SessionPhase.Connecting) return new(Connecting);
        if (s.Phase == SessionPhase.Reconnecting) return new(Reconnecting);
        if (s.Phase != SessionPhase.Connected) return null;
        return s.Status switch
        {
            null => new(LoadingGame),
            GameStatus.Lobby => null,
            GameStatus.Setup => s.HasDraft ? null : new(LoadingGame),
            _ => s.HasProjection && !s.CatchingUp && s.TableReady ? null : new(LoadingGame),
        };
    }

    /// <summary>The strip over a table already shown: a reconnect, else the models still loading ("Loading models 3 of 12").</summary>
    public static LoadingLine? Strip(SessionPhase phase, bool entered, int modelsDone, int modelsTotal)
    {
        if (!entered) return null;
        if (phase == SessionPhase.Reconnecting) return new(Reconnecting);
        return modelsTotal > 0 && modelsDone < modelsTotal ? new($"Loading models {modelsDone} of {modelsTotal}", modelsDone, modelsTotal) : null;
    }

    public static LoadingLine Graphics(int done, int total) => new(PreparingGraphics, done, total);
}

/// <summary>The models and terrain meshes a table is loading, by file: each counted once per wave, a wave ending when every one started
/// has finished (loaded or failed), so a late army's models count from one again. Main thread only.</summary>
public sealed class AssetProgress
{
    readonly HashSet<string> _started = new(), _finished = new();
    public event Action? Changed;
    public int Done => _finished.Count;
    public int Total => _started.Count;

    public void Started(string key)
    {
        if (!_started.Add(key)) return;
        Changed?.Invoke();
    }

    public void Finished(string key)
    {
        if (!_started.Contains(key) || !_finished.Add(key)) return;
        if (_finished.Count == _started.Count) { _started.Clear(); _finished.Clear(); }
        Changed?.Invoke();
    }
}

/// <summary>A wait shows only once it has lasted <paramref name="delay"/>, so a cached load never flashes a panel.</summary>
public sealed class LoadingGate(TimeSpan delay)
{
    TimeSpan? _since;

    public bool Update(bool waiting, TimeSpan now)
    {
        if (!waiting) { _since = null; return false; }
        _since ??= now;
        return now - _since.Value >= delay;
    }
}
