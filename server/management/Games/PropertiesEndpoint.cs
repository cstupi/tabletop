namespace Tabletop.Management.Games;

public static class PropertiesEndpoint
{
    public static void MapProperties(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/properties", async (GameRepository games) => Results.Ok(await games.ListProperties())).RequireAuthorization();
        // A pinned set never changes: the client fetches this once per hash and caches it.
        app.MapGet("/v1/properties/{hash}/rules", async (string hash, GameRepository games) =>
            await games.RuleTexts(hash) is { } rows ? Results.Ok(rows) : Results.NotFound()).RequireAuthorization();
    }
}
