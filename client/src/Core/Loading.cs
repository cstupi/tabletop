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

    /// <summary>Whether the game's first table stands: null once it does. A lobby has no table; setup waits for its draft and the table's
    /// first draw (<c>TableReady</c>); play for the projection, the log caught up and the first draw. <see cref="Curtain"/> keeps the
    /// curtain up past it until the table's meshes are in.</summary>
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
            GameStatus.Setup => s.HasDraft && s.TableReady ? null : new(LoadingGame),
            _ => s.HasProjection && !s.CatchingUp && s.TableReady ? null : new(LoadingGame),
        };
    }

    /// <summary>The curtain over the game screen: <paramref name="entry"/> (<see cref="Entry"/>) until the table stands, then the meshes still
    /// loading and the settling frames (<see cref="LoadSettled"/>), so no mesh is generated, added or first drawn after the curtain lifts.</summary>
    public static LoadingLine? Curtain(LoadingLine? entry, bool settled, int modelsDone, int modelsTotal) =>
        entry ?? (settled ? null : modelsTotal > 0 && modelsDone < modelsTotal ? Models(modelsDone, modelsTotal) : new(LoadingGame));

    static LoadingLine Models(int done, int total) => new($"Loading models {done} of {total}", done, total);

    /// <summary>The strip over a table already shown: a reconnect, else the models still loading ("Loading models 3 of 12").</summary>
    public static LoadingLine? Strip(SessionPhase phase, bool entered, int modelsDone, int modelsTotal)
    {
        if (!entered) return null;
        if (phase == SessionPhase.Reconnecting) return new(Reconnecting);
        return modelsTotal > 0 && modelsDone < modelsTotal ? Models(modelsDone, modelsTotal) : null;
    }

    public static LoadingLine Graphics(int done, int total) => new(PreparingGraphics, done, total);
}

/// <summary>The models, terrain meshes and built-in files (a board print among them) a table is loading, by file: each counted once per wave, a wave ending when every one started
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

/// <summary>Entering a game is over once its table stands with every file of its loading wave in. <see cref="Update"/> is true once, the frame the
/// client starts its settling work behind the curtain (the unit card's controls, the move warm-up, the play warm-up, the loading's garbage),
/// queued as steps (<see cref="Then(Func{bool})"/>) a frame apart; <see cref="Done"/> follows <paramref name="quietFrames"/> frames after the last, so the new meshes' first draws are paid behind the curtain too. A wave still loading
/// <paramref name="patience"/> after the table stood settles anyway, and the strip shows the rest. Main thread only.</summary>
public sealed class LoadSettled(int quietFrames = 2, TimeSpan? patience = null)
{
    readonly TimeSpan _patience = patience ?? TimeSpan.FromSeconds(20);
    TimeSpan? _standingSince;
    int _after = -1;

    readonly Queue<Func<bool>> _steps = new();

    public bool Done => _after >= quietFrames && _steps.Count == 0;

    /// <summary>A step of the settling work, run in a frame of its own after the settling frame (so no frame behind the curtain carries all of
    /// it), repeated each frame until it returns true. The quiet frames count from the last step.</summary>
    public void Then(Func<bool> step) => _steps.Enqueue(step);
    public void Then(Action step) => Then(() => { step(); return true; });

    /// <summary>Per frame. <paramref name="standing"/>: the table stands (<see cref="LoadingView.Entry"/> has nothing to say).</summary>
    public bool Update(bool standing, int modelsDone, int modelsTotal, TimeSpan now)
    {
        if (_after >= 0)
        {
            if (_steps.TryPeek(out var step)) { if (step()) _steps.Dequeue(); return false; }
            if (_after < quietFrames) _after++;
            return false;
        }
        if (!standing) return false;
        _standingSince ??= now;
        if (modelsDone < modelsTotal && now - _standingSince.Value < _patience) return false;
        _after = 0;
        return true;
    }
}

/// <summary>Setup's loading state over the table: a request that replaces the draft (a Random table, a loaded layout), from <see cref="Begin"/>
/// through the server's answer until every file of the meshes' wave is in and <paramref name="quietFrames"/> frames have drawn them; and any
/// wave of new meshes on its own (a tile of a kind not yet on the table). Shown once it has lasted <paramref name="delay"/>, so a quick one
/// never flashes; given up after <paramref name="patience"/>. Main thread only.</summary>
public sealed class TableLoad(TimeSpan delay, int quietFrames = 2, TimeSpan? patience = null)
{
    public const string Text = "Laying out the table…";
    readonly TimeSpan _patience = patience ?? TimeSpan.FromSeconds(30);
    object? _from;
    bool _asked;
    TimeSpan? _since;
    int _quiet;

    /// <summary>A request that will replace <paramref name="draft"/>: the wait starts now, before the server answers.</summary>
    public void Begin(object? draft, TimeSpan now) { _asked = true; _from = draft; _since ??= now; _quiet = 0; }

    /// <summary>The request failed: no new draft is coming for it.</summary>
    public void Cancel() => _asked = false;

    /// <summary>Per frame, with the draft in use and the table's file wave: the line over the table, else null.</summary>
    public LoadingLine? Update(object? draft, int modelsDone, int modelsTotal, TimeSpan now)
    {
        if (_asked && !ReferenceEquals(draft, _from)) _asked = false;   // the new draft has arrived
        if (_asked || modelsDone < modelsTotal)
        {
            _since ??= now; _quiet = 0;
            if (now - _since.Value >= _patience) { _asked = false; _since = null; _from = null; return null; }
        }
        else if (_since is not null && ++_quiet > quietFrames) { _since = null; _quiet = 0; }
        if (_since is not { } since || now - since < delay) return null;
        return modelsTotal > 0 && modelsDone < modelsTotal ? new($"{Text} {modelsDone} of {modelsTotal}", modelsDone, modelsTotal) : new(Text);
    }
}
