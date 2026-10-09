using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules;

/// <summary>Who sent a command: the participant, and the team they are on (null for a watcher).</summary>
public sealed record Actor(Guid ParticipantId, Guid? TeamId);

/// <summary>What a game is, in memory, from playing on: configuration, the initial state, and the applier holding the projection and the log.</summary>
public sealed class GameState(GameInfo info, Board board, EventApplier applier, IGameRules rules)
{
    public GameInfo Info { get; set; } = info;
    /// <summary>The game's stored dice seed, generated once when the game row was created and never written again. The cursor is Dice.DiceCursor.
    /// The column is bigint and the generator wants a ulong, so the sign bit is reinterpreted rather than clamped: every one of the 2^64 stored values is a distinct seed.</summary>
    public ulong RngSeed { get; } = unchecked((ulong)info.RngSeed);
    public Board Board { get; } = board;
    public EventApplier Applier { get; } = applier;
    public Projection Projection => Applier.Projection;
    public IGameRules Rules { get; } = rules;
}

public abstract record EngineResult;
/// <summary>Events to append, in order, and the warning the first of them carries (enforcement spec → How a warning is recorded). Empty is a valid answer.</summary>
public sealed record Emit(IReadOnlyList<EventPayload> Events, string? Warning = null) : EngineResult
{
    public static Emit None { get; } = new([]);
}
/// <summary>Not a legality refusal — the command cannot be carried out (no such frame, no interpreter). Independent of enforcement.</summary>
public sealed record RefuseCommand(RefusalReason Reason, string Detail) : EngineResult;

/// <summary>
/// The generic engine the scheduler and interpreter plans implement. Judge says whether a command is inside the rules;
/// Execute turns an accepted command into events; Advance runs the engine forward with no input and is called after every
/// appended batch until it returns nothing. The engine never appends: the session does, and it never assumes its last output
/// is still true (scheduler → Nothing is an invariant) — it reads state.Projection every call.
/// </summary>
public interface IRulesEngine
{
    Legality Judge(GameState state, Command command, Actor actor);
    EngineResult Execute(GameState state, Command command, Actor actor);
    Emit Advance(GameState state);
    /// <summary>The changes a round start carries for this game's scheduler and ruleset; what a menu set_round writes (enforcement spec → A menu round start).</summary>
    IReadOnlyList<StateChange> RoundStartChanges(GameState state, int round) => [];

    /// <summary>The game's pinned property set, for a question asked outside Advance (the sight check's who-can-shoot). Null for an engine with none.</summary>
    PropertySet? Properties => null;
}

public interface IRulesEngineFactory
{
    IRulesEngine Create(IGameRules rules, GameInfo game);
}
