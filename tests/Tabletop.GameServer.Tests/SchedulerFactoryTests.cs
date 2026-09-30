using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class SchedulerFactoryTests
{
    [Fact]
    public void The_factory_picks_the_shape_from_the_game_and_the_engine_factory_composes_it()
    {
        var factory = new SchedulerFactory();
        var alternating = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)]);
        var playerTurn = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)], shape: SchedulerShape.PlayerTurn);
        Assert.IsType<AlternatingActivationScheduler>(factory.Create(GrimdarkFutureRules.Instance, alternating.Info, TestProperties.Core));
        Assert.IsType<PlayerTurnScheduler>(factory.Create(GrimdarkFutureRules.Instance, playerTurn.Info, TestProperties.Core));
        var store = new InMemoryPropertiesStore().Add(TestProperties.Core);
        var engines = new RulesEngineFactory(store, new ScriptedDice(6, 1), factory);
        var engine = Assert.IsType<RulesEngine>(engines.Create(GrimdarkFutureRules.Instance, alternating.Info with { PropertiesHash = TestProperties.Core.ContentHash }));
        var first = engine.Advance(alternating.State);
        Assert.IsType<SequenceStarted>(Assert.Single(first));                                            // the scheduler spoke through the engine: the roll-off begins
        Assert.Equal(GfNames.RollOffSeq, ((SequenceStarted)first[0]).Sequence);
    }
}
