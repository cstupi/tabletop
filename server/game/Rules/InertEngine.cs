using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules;

/// <summary>The skeleton's engine: no scheduler, no interpreter. Nothing is judged illegal, nothing can be executed, nothing advances.</summary>
public sealed class InertEngine : IRulesEngine
{
    public Legality Judge(GameState state, Command command, Actor actor) => Legality.Legal;
    public EngineResult Execute(GameState state, Command command, Actor actor) =>
        new RefuseCommand(RefusalReason.Unknown, $"{NameOf(command)} needs the interpreter, which this build does not have.");
    public Emit Advance(GameState state) => Emit.None;
    static string NameOf(Command c) => c.GetType().Name;   // not Protocol's EventKinds.Of, which is keyed on EventPayload
}

public sealed class InertEngineFactory : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new InertEngine();
}
