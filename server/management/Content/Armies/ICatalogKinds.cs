namespace Tabletop.Management.Content.Armies;

/// <summary>Which catalog names are model-level, from whatever Properties sets exist for the game rules. CatalogKinds is the Postgres implementation.</summary>
public interface ICatalogKinds
{
    /// <summary>Unit and model catalog entries of every set for the game rules, by name. Empty until content is loaded.</summary>
    Task<IReadOnlyDictionary<string, PropertyLevel>> For(string gameRules);
}
