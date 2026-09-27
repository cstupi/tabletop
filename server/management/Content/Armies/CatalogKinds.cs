using Dapper;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Armies;

/// <summary>Which catalog names are model-level, from whatever Properties sets exist for the game rules. Empty until content is loaded.</summary>
public sealed class CatalogKinds(Database db)
{
    public async Task<IReadOnlyDictionary<string, PropertyLevel>> For(string gameRules)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<(string Name, string Kind)>(@"select distinct ce.name, ce.kind from catalog_entry ce join properties p on p.content_hash = ce.properties_hash
            where p.game_rules = @gameRules and ce.kind in ('unit', 'model')", new { gameRules });
        return rows.ToDictionary(r => r.Name, r => r.Kind == "model" ? PropertyLevel.Model : PropertyLevel.Unit);
    }
}
