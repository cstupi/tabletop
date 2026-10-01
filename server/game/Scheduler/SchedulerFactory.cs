using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>Two implementations behind one interface, selected by game.scheduler_shape (scheduler → The line). Registered as Engine.ISchedulerFactory; RulesEngineFactory calls it per game with the pinned set.</summary>
public sealed class SchedulerFactory : ISchedulerFactory
{
    public IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties) => game.SchedulerShape switch
    {
        SchedulerShape.AlternatingActivation => new AlternatingActivationScheduler(properties),
        SchedulerShape.PlayerTurn => new PlayerTurnScheduler(properties),
        _ => throw new NotSupportedException($"No scheduler for shape {game.SchedulerShape}."),
    };
}
