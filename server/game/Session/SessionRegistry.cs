namespace Tabletop.GameServer.Session;

/// <summary>Which games are in memory. Get-or-hydrate is serialised per game, so two sockets arriving together hydrate once and a slow
/// hydration (a long log, a slow database) holds up only its own game's sockets (security review M5).</summary>
public sealed class SessionRegistry(SessionServices services)
{
    private readonly Dictionary<Guid, GameSession> _live = new();
    /// <summary>One gate per game while anyone holds or waits on it; the entry goes when the last holder releases it.</summary>
    private readonly Dictionary<Guid, (SemaphoreSlim Gate, int Holders)> _gates = new();

    public bool Draining { get; private set; }
    public IReadOnlyCollection<GameSession> Live { get { lock (_live) return _live.Values.ToList(); } }

    private async Task<IDisposable> EnterAsync(Guid gameId, CancellationToken ct)
    {
        SemaphoreSlim gate;
        lock (_gates)
        {
            var (g, holders) = _gates.TryGetValue(gameId, out var entry) ? entry : (new SemaphoreSlim(1, 1), 0);
            _gates[gameId] = (g, holders + 1);
            gate = g;
        }
        try { await gate.WaitAsync(ct); }
        catch { Leave(gameId, gate, entered: false); throw; }
        return new Held(() => Leave(gameId, gate, entered: true));
    }

    private void Leave(Guid gameId, SemaphoreSlim gate, bool entered)
    {
        if (entered) gate.Release();
        lock (_gates)
        {
            var (g, holders) = _gates[gameId];
            if (holders == 1) _gates.Remove(gameId); else _gates[gameId] = (g, holders - 1);
        }
    }

    private sealed class Held(Action release) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) release(); }
    }

    public async Task<GameSession> GetOrHydrateAsync(Guid gameId, CancellationToken ct)
    {
        using var _ = await EnterAsync(gameId, ct);
        if (Draining) throw new InvalidOperationException("shutting down");
        GameSession? existing;
        lock (_live) _live.TryGetValue(gameId, out existing);
        if (existing is not null)
        {
            if (!existing.Faulted) return existing;
            await existing.DrainAsync("faulted");   // its clients are already closed; this only stops its mailbox loop
        }
        var session = await Hydrator.HydrateAsync(gameId, services, ct);
        await session.StartAsync();
        lock (_live)
        {
            // A drain that began while this game hydrated has already swept _live: the new session is drained here instead of left running.
            if (!Draining) { _live[gameId] = session; return session; }
        }
        await session.DrainAsync("shutting down");
        throw new InvalidOperationException("shutting down");
    }

    /// <summary>When the last socket goes, the game leaves memory; the next socket hydrates it again. Nothing is lost: the log and the rows hold everything.
    /// The session decides it is empty in its own mailbox, so a socket whose connect is already queued keeps the game, and one queued after
    /// the decision hydrates again.</summary>
    public async Task ReleaseIfEmptyAsync(GameSession session)
    {
        using var _ = await EnterAsync(session.GameId, CancellationToken.None);
        if (!await session.RetireIfEmptyAsync()) return;
        lock (_live) if (_live.TryGetValue(session.GameId, out var s) && s == session) _live.Remove(session.GameId);
        await session.DrainAsync("released");
    }

    public async Task DrainAllAsync(string reason)
    {
        List<GameSession> sessions;
        lock (_live) { Draining = true; sessions = _live.Values.ToList(); _live.Clear(); }
        foreach (var s in sessions) await s.DrainAsync(reason);
    }
}
