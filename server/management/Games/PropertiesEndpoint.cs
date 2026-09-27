namespace Tabletop.Management.Games;

public static class PropertiesEndpoint
{
    public static void MapProperties(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/properties", async (GameRepository games) => Results.Ok(await games.ListProperties())).RequireAuthorization();
}
