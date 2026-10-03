using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Engine;

/// <summary>
/// The scheduler plan's seam. Called by RulesEngine.Advance only when no framed Ask is open and the resolution stack is empty; returns the
/// scheduler's next payloads, or nothing. It reads the log for its own Asks' ask_closed and for sequence_finished, returns nothing while an
/// Ask of its own is live, and closes one of its own Asks that a menu edit superseded (the engine's one ask_closed).
/// </summary>
public interface IScheduler
{
    IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter);
    /// <summary>The shape's round-start changes then the ruleset's OnRoundStarted; BeginRound and a menu set_round both write them.</summary>
    IReadOnlyList<StateChange> RoundStartChanges(GameState state, int round) => [];
}

/// <summary>Per game: the ruleset, the game, and its pinned set (the scheduler reads the action rows' names and hands the set to the hooks).</summary>
public interface ISchedulerFactory { IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties); }

/// <summary>Until the scheduler plan lands: nothing drives turns, so sequences start only through tests or the scheduler's future registration.</summary>
public sealed class NoScheduler : IScheduler { public IReadOnlyList<EventPayload> Advance(GameState state, ISequenceStarter starter) => []; }
public sealed class NoSchedulerFactory : ISchedulerFactory { public IScheduler Create(IGameRules rules, GameInfo game, PropertySet properties) => new NoScheduler(); }
