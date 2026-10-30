using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The optional-rules partial (optional-rules spec): the catalog the lobby shows and the check a set_configuration passes.</summary>
public sealed partial class GrimdarkFutureRules
{
    public RulesOptionCatalog OptionCatalog => GfOptions.CatalogOf(Edition);
    /// <summary>Side Battle's legs: 3.5.1 p.22 54" and 30", 3.6.0 p.21 48" and 36" on 72×48.</summary>
    public Tabletop.Geometry.ZoneLegs Zones => GfEdition.ZoneLegsOf(Edition);
    public IReadOnlyList<string> OptionProblems(IReadOnlyDictionary<string, string> optionalRules) => GfOptions.Problems(optionalRules);
    public Interpreter.ISightPolicy SightPolicy(GameState state) => GfSight.For(state);
}
