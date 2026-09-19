using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules;

/// <summary>Who sent a command: the participant, and the team they are on (null for a watcher).</summary>
public sealed record Actor(Guid ParticipantId, Guid? TeamId);

/// <summary>What a game is, in memory, from playing on: configuration, the initial state, and the applier holding the projection and the log.</summary>
public sealed class GameState(GameInfo info, Board board, EventApplier applier, IGameRules rules)
{
    public GameInfo Info { get; set; } = info;
    public Board Board { get; } = board;
    public EventApplier Applier { get; } = applier;
    public Projection Projection => Applier.Projection;
    public IGameRules Rules { get; } = rules;
}

public abstract record EngineResult;
/// <summary>Events to append, in order. Empty is a valid answer (the command changed nothing).</summary>
public sealed record Emit(IReadOnlyList<EventPayload> Events) : EngineResult;
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
    IReadOnlyList<EventPayload> Advance(GameState state);
}

public interface IRulesEngineFactory
{
    IRulesEngine Create(IGameRules rules, GameInfo game);
}
