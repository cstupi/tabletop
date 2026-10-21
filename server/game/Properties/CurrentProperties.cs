using System.Collections.Concurrent;

namespace Tabletop.GameServer.Properties;

/// <summary>The set this server seeded for each game rules at startup: the set every game that has not ended runs (games-follow-the-current-rules
/// spec). Empty when nothing was seeded (the Test environment), and then no game moves.</summary>
public sealed class CurrentProperties
{
    readonly ConcurrentDictionary<string, string> _hashes = new(StringComparer.Ordinal);
    public string? For(string gameRules) => _hashes.GetValueOrDefault(gameRules);
    public void Set(string gameRules, string hash) => _hashes[gameRules] = hash;
}
