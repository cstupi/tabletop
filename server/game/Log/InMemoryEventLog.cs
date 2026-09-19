using Tabletop.Protocol;

namespace Tabletop.GameServer.Log;

public sealed class InMemoryEventLog : IEventLog
{
    private readonly Dictionary<Guid, SortedDictionary<long, GameEvent>> _logs = new();
    private readonly object _gate = new();

    public Task AppendAsync(Guid gameId, IReadOnlyList<GameEvent> events, CancellationToken ct)
    {
        lock (_gate)
        {
            var log = _logs.TryGetValue(gameId, out var l) ? l : _logs[gameId] = new();
            var dup = events.FirstOrDefault(e => log.ContainsKey(e.Seq)) ?? events.GroupBy(e => e.Seq).FirstOrDefault(g => g.Count() > 1)?.First();
            if (dup is not null) throw new DuplicateSequenceException(gameId, dup.Seq);
            foreach (var e in events) log[e.Seq] = e;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GameEvent>> ReadAsync(Guid gameId, long afterSeq, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<GameEvent>>(_logs.TryGetValue(gameId, out var l) ? l.Values.Where(e => e.Seq > afterSeq).ToList() : []);
    }

    public Task<long> LastSeqAsync(Guid gameId, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_logs.TryGetValue(gameId, out var l) && l.Count > 0 ? l.Keys.Max() : 0L);
    }
}
