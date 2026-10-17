using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The optional-rules partial (optional-rules spec): the catalog the lobby shows and the check a set_configuration passes.</summary>
public sealed partial class GrimdarkFutureRules
{
    public RulesOptionCatalog OptionCatalog => GfOptions.Catalog;
    public IReadOnlyList<string> OptionProblems(IReadOnlyDictionary<string, string> optionalRules) => GfOptions.Problems(optionalRules);
    public Interpreter.ISightPolicy SightPolicy(GameState state) => GfSight.For(state);
}
