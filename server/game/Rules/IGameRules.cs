using Tabletop.GameServer.Rules.GrimdarkFuture;

namespace Tabletop.GameServer.Rules;

/// <summary>
/// The per-ruleset C# behind an interface, selected by game.game_rules. This plan gives it an identity and nothing else;
/// the scheduler plan adds its hooks (first team, activatable units, phases, round-boundary hooks) and the interpreter plan
/// adds its (sequences, rolls' conventions, thresholds, caps). Extend; do not replace. The merged interface is listed in
/// full under Interfaces → Produces.
/// </summary>
public interface IGameRules
{
    string Id { get; }
}

public sealed class UnknownGameRulesException(string id) : Exception($"No GameRules named '{id}' ships with this build.");

public static class GameRulesRegistry
{
    public static IGameRules Resolve(string id) => id == GrimdarkFutureRules.RulesId ? GrimdarkFutureRules.Instance : throw new UnknownGameRulesException(id);
}
