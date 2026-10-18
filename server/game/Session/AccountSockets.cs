using System.Net.WebSockets;

namespace Tabletop.GameServer.Session;

/// <summary>Open sockets per account across every game. A socket over the cap closes the account's oldest: under the cap every window
/// of the account plays (self-play: two windows, one per side).</summary>
public sealed class AccountSockets(Configuration.GameServerOptions options)
{
    private readonly Dictionary<Guid, List<SocketConnection>> _open = new();

    public async Task AddAsync(SocketConnection connection)
    {
        List<SocketConnection> evicted;
        lock (_open)
        {
            if (!_open.TryGetValue(connection.AccountId, out var list)) _open[connection.AccountId] = list = new();
            list.Add(connection);
            var over = Math.Max(0, list.Count - options.SocketsPerAccount);
            evicted = list.Take(over).ToList();
            list.RemoveRange(0, over);
        }
        foreach (var old in evicted) await old.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced: too many connections for this account");   // 1000, as a replaced socket: the client does not reconnect it
    }

    public void Remove(SocketConnection connection)
    {
        lock (_open)
            if (_open.TryGetValue(connection.AccountId, out var list) && list.Remove(connection) && list.Count == 0) _open.Remove(connection.AccountId);
    }

    public int CountOf(Guid accountId) { lock (_open) return _open.TryGetValue(accountId, out var list) ? list.Count : 0; }
}
