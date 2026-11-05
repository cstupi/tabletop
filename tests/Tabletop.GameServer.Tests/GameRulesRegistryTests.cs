using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;

namespace Tabletop.GameServer.Tests;

public class GameRulesRegistryTests
{
    [Fact]
    public void Both_editions_resolve_to_their_own_instance_and_name()
    {
        Assert.Same(GrimdarkFutureRules.V351, GameRulesRegistry.Resolve("grimdark-future"));
        Assert.Same(GrimdarkFutureRules.V360, GameRulesRegistry.Resolve("grimdark-future-3.6"));
        Assert.Same(GrimdarkFutureRules.V351, GrimdarkFutureRules.Instance);
        Assert.Equal("grimdark-future", GrimdarkFutureRules.V351.Id);
        Assert.Equal("grimdark-future-3.6", GrimdarkFutureRules.V360.Id);
        Assert.Throws<UnknownGameRulesException>(() => GameRulesRegistry.Resolve("grimdark-future-3.7"));
    }
}
