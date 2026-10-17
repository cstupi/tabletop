using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Engine;

public sealed class PropertiesNotFoundException(string hash) : Exception($"No Properties set with hash {hash} is loaded; the game cannot run.");

/// <summary>Replaces InertEngineFactory. The pinned set is loaded once per hash; Create is synchronous, so the load blocks at hydration. acceptsSetDice is RulesEngine's.</summary>
public sealed class RulesEngineFactory(IPropertiesStore store, IDiceSource dice, ISchedulerFactory schedulers, bool acceptsSetDice = false) : IRulesEngineFactory
{
    private readonly Dictionary<string, PropertySet> _cache = new();

    public IRulesEngine Create(IGameRules rules, GameInfo game)
    {
        PropertySet set;
        lock (_cache)
        {
            if (!_cache.TryGetValue(game.PropertiesHash, out set!))
                _cache[game.PropertiesHash] = set = store.LoadAsync(game.PropertiesHash, CancellationToken.None).GetAwaiter().GetResult() ?? throw new PropertiesNotFoundException(game.PropertiesHash);
        }
        return new RulesEngine(rules, set, dice, schedulers.Create(rules, game, set), acceptsSetDice);
    }
}
