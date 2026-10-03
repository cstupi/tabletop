using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>Opens one given Ask the first time Advance runs, and never again (it checks the log, not a flag, so it survives hydration).</summary>
public sealed class FixedAskEngine(Ask ask) : IRulesEngine
{
    public Legality Judge(GameState state, Command command, Actor actor) => Legality.Legal;
    public EngineResult Execute(GameState state, Command command, Actor actor) => new RefuseCommand(RefusalReason.Unknown, "test engine");
    public Emit Advance(GameState state) =>
        state.Applier.Log.Any(e => e.Payload is AskOpened o && o.Ask.Id == ask.Id) ? Emit.None : new Emit([new AskOpened(ask)]);
}

public sealed class FixedAskEngineFactory(Ask ask) : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new FixedAskEngine(ask);
}
