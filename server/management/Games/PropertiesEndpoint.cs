namespace Tabletop.Management.Games;

public static class PropertiesEndpoint
{
    public static void MapProperties(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/properties", async (IGameRepository games) => Results.Ok(await games.ListProperties())).RequireAuthorization();
        // A pinned set never changes: the client fetches this once per hash and caches it.
        app.MapGet("/v1/properties/{hash}/rules", async (string hash, IGameRepository games) =>
            await games.RuleTexts(hash) is { } rows ? Results.Ok(rows) : Results.NotFound()).RequireAuthorization();
        // What the client's menu offers as Add grant: an AddGrant names an entry and one of these effect ids.
        app.MapGet("/v1/properties/{hash}/grants", async (string hash, IGameRepository games) =>
            await games.GrantableEffects(hash) is { } rows ? Results.Ok(rows) : Results.NotFound()).RequireAuthorization();
    }
}
