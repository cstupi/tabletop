using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The optional-rules partial (optional-rules spec): the catalog the lobby shows and the check a set_configuration passes.</summary>
public sealed partial class GrimdarkFutureRules
{
    public RulesOptionCatalog OptionCatalog => GfOptions.CatalogOf(Edition);
    /// <summary>Side Battle's legs: 3.5.1 p.22 54" and 30", 3.6.0 p.21 48" and 36" on 72×48.</summary>
    public Tabletop.Geometry.ZoneLegs Zones => GfEdition.ZoneLegsOf(Edition);

    /// <summary>The edition's terrain words (GfEdition.TerrainWords) by the declaration's snake_case name.</summary>
    public IReadOnlyDictionary<string, string> TerrainWords => _terrainWords ??= Tabletop.Content.BuiltinTerrain.DeclarationNames
        .Where(n => Edition.TerrainWords.ContainsKey(n.Value)).ToDictionary(n => n.Key, n => Edition.TerrainWords[n.Value]);
    IReadOnlyDictionary<string, string>? _terrainWords;
    /// <summary>The edition's terrain rules (GfEdition.TerrainRules) by the declaration's snake_case name.</summary>
    public IReadOnlyDictionary<string, string> TerrainRules => _terrainRules ??= Tabletop.Content.BuiltinTerrain.DeclarationNames
        .Where(n => Edition.TerrainRules.ContainsKey(n.Value)).ToDictionary(n => n.Key, n => Edition.TerrainRules[n.Value]);
    IReadOnlyDictionary<string, string>? _terrainRules;
    /// <summary>p.6 (3.6.0 p.6): "within 3” of a marker", from the marker's centre (GfObjectives.Seize).</summary>
    public double? MarkerRange => GfObjectives.SeizeRange;
    public IReadOnlyList<string> OptionProblems(IReadOnlyDictionary<string, string> optionalRules) => GfOptions.Problems(optionalRules);
    public Interpreter.ISightPolicy SightPolicy(GameState state) => GfSight.For(state);
}
