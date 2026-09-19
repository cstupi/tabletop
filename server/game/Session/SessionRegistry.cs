namespace Tabletop.GameServer.Session;

/// <summary>Which games are in memory. Get-or-hydrate is serialised per process so two sockets arriving together hydrate once.</summary>
public sealed class SessionRegistry(SessionServices services)
{
    private readonly Dictionary<Guid, GameSession> _live = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool Draining { get; private set; }
    public IReadOnlyCollection<GameSession> Live { get { lock (_live) return _live.Values.ToList(); } }

    public async Task<GameSession> GetOrHydrateAsync(Guid gameId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (Draining) throw new InvalidOperationException("shutting down");
            if (_live.TryGetValue(gameId, out var existing) && !existing.Faulted) return existing;
            var session = await Hydrator.HydrateAsync(gameId, services, ct);
            await session.StartAsync();
            lock (_live) _live[gameId] = session;
            return session;
        }
        finally { _gate.Release(); }
    }

    /// <summary>When the last socket goes, the game leaves memory; the next socket hydrates it again. Nothing is lost: the log and the rows hold everything.</summary>
    public async Task ReleaseIfEmptyAsync(GameSession session)
    {
        await _gate.WaitAsync();
        try
        {
            if (session.Clients.Count > 0 && !session.Faulted) return;
            lock (_live) if (_live.TryGetValue(session.GameId, out var s) && s == session) _live.Remove(session.GameId);
            await session.DrainAsync("released");
        }
        finally { _gate.Release(); }
    }

    public async Task DrainAllAsync(string reason)
    {
        await _gate.WaitAsync();
        try
        {
            Draining = true;
            foreach (var s in Live) await s.DrainAsync(reason);
            lock (_live) _live.Clear();
        }
        finally { _gate.Release(); }
    }
}
