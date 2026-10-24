using System.Security.Claims;
using Tabletop.Content;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Layouts;

public static class LayoutEndpoints
{
    /// <summary>The shape and size rules, then every asset reference an uploaded asset of the caller's of a fitting kind.</summary>
    static async Task<string?> Problem(TableState s, bool nameRequired, Guid owner, IAssetRepository assets) =>
        LayoutRules.Problem(s, nameRequired) ?? await AssetReferences.Check(assets, owner, LayoutRules.AssetRefs(s));

    public static void MapLayouts(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/layouts").RequireAuthorization();
        g.MapRandomTable();
        g.MapGet("", async (ClaimsPrincipal u, ILayoutRepository layouts) => Results.Ok(await layouts.List(CurrentAccount.Id(u))));
        g.MapPost("", async (ClaimsPrincipal u, TableState state, ILayoutRepository layouts, IAssetRepository assets) =>
        {
            var owner = CurrentAccount.Id(u);
            if (await Problem(state, nameRequired: true, owner, assets) is { } problem) return Problems.Unprocessable(problem);
            if ((await layouts.List(owner)).Count >= Limits.PerAccount) return Problems.Unprocessable($"An account holds at most {Limits.PerAccount} layouts.");
            var saved = await layouts.Save(owner, state);
            return Results.Created($"/v1/layouts/{saved.Id}", saved);
        });
        g.MapGet("/{id:guid}", async (ClaimsPrincipal u, Guid id, ILayoutRepository layouts) => await layouts.Get(CurrentAccount.Id(u), id) is { } t ? Results.Ok(t) : Problems.NotFound());
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, PatchLayoutRequest p, ILayoutRepository layouts, IAssetRepository assets) =>
        {
            var owner = CurrentAccount.Id(u);
            if (p.Name is not null && Limits.Text(p.Name, "name", Limits.Name) is { } name) return Problems.Unprocessable(name);
            if (p.State is not null && await Problem(p.State, nameRequired: false, owner, assets) is { } problem) return Problems.Unprocessable(problem);
            return await layouts.Patch(owner, id, p) is { } t ? Results.Ok(t) : Problems.NotFound();
        });
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, ILayoutRepository layouts) => await layouts.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
    }
}
