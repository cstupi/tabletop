using System.Security.Claims;
using Tabletop.Content;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Layouts;

public static class LayoutEndpoints
{
    /// <summary>A property the JSON omitted lands as default(VisualRef), which is IsValid == false; a texture, mesh or
    /// marker visual in that state must never reach a row.</summary>
    static bool HasValidVisuals(TableState s) =>
        s.Texture.IsValid && s.Terrain.All(p => p.Mesh.IsValid) && s.Markers.All(m => m.Visual.IsValid);

    public static void MapLayouts(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/layouts").RequireAuthorization();
        g.MapGet("", async (ClaimsPrincipal u, ILayoutRepository layouts) => Results.Ok(await layouts.List(CurrentAccount.Id(u))));
        g.MapPost("", async (ClaimsPrincipal u, TableState state, ILayoutRepository layouts) =>
        {
            if (string.IsNullOrWhiteSpace(state.Name)) return Problems.Unprocessable("A saved layout needs a name.");
            if (!HasValidVisuals(state)) return Problems.Unprocessable("Every texture, terrain mesh and marker visual needs a builtin or an assetId.");
            var saved = await layouts.Save(CurrentAccount.Id(u), state);
            return Results.Created($"/v1/layouts/{saved.Id}", saved);
        });
        g.MapGet("/{id:guid}", async (ClaimsPrincipal u, Guid id, ILayoutRepository layouts) => await layouts.Get(CurrentAccount.Id(u), id) is { } t ? Results.Ok(t) : Problems.NotFound());
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, PatchLayoutRequest p, ILayoutRepository layouts) =>
        {
            if (p.State is not null && !HasValidVisuals(p.State)) return Problems.Unprocessable("Every texture, terrain mesh and marker visual needs a builtin or an assetId.");
            return await layouts.Patch(CurrentAccount.Id(u), id, p) is { } t ? Results.Ok(t) : Problems.NotFound();
        });
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, ILayoutRepository layouts) => await layouts.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
    }
}
