using System.Security.Claims;
using Tabletop.Management.Http;

namespace Tabletop.Management.Games;

public static class GameEndpoints
{
    public static void MapGames(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/games").RequireAuthorization();
        g.MapGet("", async (IGameRepository games, int? limit, Guid? after) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<GameSummary>.Of(await games.List(page), page.Limit, x => x.Id));
        });
        g.MapPost("", async (ClaimsPrincipal u, CreateGameRequest r, IGameRepository games) =>
            string.IsNullOrWhiteSpace(r.Name) ? Problems.Unprocessable("name is required")
            : await games.Create(CurrentAccount.Id(u), r) is { } game ? Results.Created($"/v1/games/{game.Id}", game) : Problems.Unprocessable($"No Properties set with hash {r.PropertiesHash}. GET /v1/properties lists the pinned sets."));
        g.MapGet("/{id:guid}", async (Guid id, IGameRepository games) => await games.Get(id) is { } game ? Results.Ok(game) : Problems.NotFound());
        g.MapPost("/{id:guid}/join", async (ClaimsPrincipal u, Guid id, IGameRepository games) => await games.Join(id, CurrentAccount.Id(u)) is { } game ? Results.Ok(game) : Problems.NotFound());
        g.MapPost("/{id:guid}/leave", async (ClaimsPrincipal u, Guid id, IGameRepository games) => await games.Leave(id, CurrentAccount.Id(u)) ? Results.NoContent() : Problems.NotFound());
        g.MapPost("/{id:guid}/end", async (ClaimsPrincipal u, Guid id, IGameRepository games) =>
            await games.IsCreator(id, CurrentAccount.Id(u)) && await games.End(id) ? Results.NoContent() : Problems.NotFound());
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, IGameRepository games) =>
            await games.IsCreator(id, CurrentAccount.Id(u)) && await games.Delete(id) ? Results.NoContent() : Problems.NotFound());
    }
}
