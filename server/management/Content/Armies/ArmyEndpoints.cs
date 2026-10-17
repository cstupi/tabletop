using System.Security.Claims;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Armies;

public static class ArmyEndpoints
{
    public static void MapArmies(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/armies").RequireAuthorization();

        g.MapPost("/import", async (ClaimsPrincipal u, ImportRequest r, ArmyForgeClient forge, ICatalogKinds kinds, IArmyRepository armies) =>
        {
            var listId = ArmyForgeClient.ListId(r.Source);
            if (listId is null) return Problems.Unprocessable("source must be an Army Forge share link or list id");
            AfList list;
            try { list = await forge.Fetch(listId); }
            catch (ArmyForgeException e) { return Problems.BadGateway($"Army Forge answered {e.Status} for list {listId}"); }
            catch (HttpRequestException e) { return Problems.BadGateway($"Army Forge unreachable: {e.Message}"); }
            if (!ArmyForgeImport.GameRules.TryGetValue(list.GameSystem, out var gameRules)) return Problems.Unprocessable($"Only Grimdark Future and Age of Fantasy lists import; this list is '{list.GameSystem}'.");
            var army = ArmyForgeImport.ToArmy(list, r.Source, await kinds.For(gameRules));
            await armies.Insert(CurrentAccount.Id(u), army);
            return Results.Created($"/v1/armies/{army.Id}", army);
        });

        g.MapGet("", async (ClaimsPrincipal u, IArmyRepository armies, int? limit, Guid? after) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<ArmySummary>.Of(await armies.List(CurrentAccount.Id(u), page), page.Limit, a => a.Id));
        });
        g.MapGet("/{id:guid}", async (ClaimsPrincipal u, Guid id, IArmyRepository armies) => await armies.Get(CurrentAccount.Id(u), id) is { } a ? Results.Ok(a) : Problems.NotFound());
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, PatchArmyRequest p, IArmyRepository armies) => await armies.Patch(CurrentAccount.Id(u), id, p) is { } a ? Results.Ok(a) : Problems.NotFound());
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, IArmyRepository armies) => await armies.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
    }
}
