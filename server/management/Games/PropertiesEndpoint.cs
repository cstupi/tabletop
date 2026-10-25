namespace Tabletop.Management.Games;

public static class PropertiesEndpoint
{
    public static void MapProperties(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/properties", async (IGameRepository games) => Results.Ok(await games.ListProperties())).RequireAuthorization();
        // A pinned set never changes: the client fetches this once per hash and caches it.
        app.MapGet("/v1/properties/{hash}/rules", async (string hash, IGameRepository games, HttpResponse response) =>
            await games.RuleTexts(hash) is { } rows ? Immutable(response, rows) : Results.NotFound()).RequireAuthorization();
        // What the client's menu offers as Add grant: an AddGrant names an entry and one of these effect ids.
        app.MapGet("/v1/properties/{hash}/grants", async (string hash, IGameRepository games, HttpResponse response) =>
            await games.GrantableEffects(hash) is { } rows ? Immutable(response, rows) : Results.NotFound()).RequireAuthorization();
    }

    /// <summary>
    /// A set's rows are written once, when its hash (the SHA-256 of its document) is first seeded, and are the same for every account,
    /// so an answer for a hash never changes. `private`, not `public`: the routes need a session, and a shared cache would serve them
    /// without one. A 404 is not cached: the hash may be seeded later.
    /// </summary>
    static IResult Immutable<T>(HttpResponse response, T rows)
    {
        response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return Results.Ok(rows);
    }
}
