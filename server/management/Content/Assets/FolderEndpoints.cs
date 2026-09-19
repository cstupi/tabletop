using System.Security.Claims;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Assets;

public static class FolderEndpoints
{
    public static void MapFolders(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/folders").RequireAuthorization();
        g.MapGet("", async (ClaimsPrincipal u, FolderRepository folders) => Results.Ok(await folders.List(CurrentAccount.Id(u))));
        g.MapPost("", async (ClaimsPrincipal u, FolderRequest r, FolderRepository folders) =>
            string.IsNullOrWhiteSpace(r.Name) ? Problems.Unprocessable("name is required")
            : await folders.Create(CurrentAccount.Id(u), r) is { } f ? Results.Created($"/v1/folders/{f.Id}", f) : Problems.Unprocessable("parentId is not one of your folders"));
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, FolderRequest r, FolderRepository folders) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return Problems.Unprocessable("name is required");
            var (found, valid) = await folders.Patch(CurrentAccount.Id(u), id, r);
            return !found ? Problems.NotFound() : !valid ? Problems.Unprocessable("parentId must be your folder and not this folder or one below it") : Results.NoContent();
        });
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, FolderRepository folders) => await folders.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
        g.MapPut("/{id:guid}/assets/{assetId:guid}", async (ClaimsPrincipal u, Guid id, Guid assetId, FolderRepository folders) =>
            await folders.AddAsset(CurrentAccount.Id(u), id, assetId) ? Results.NoContent() : Problems.NotFound());
        g.MapDelete("/{id:guid}/assets/{assetId:guid}", async (ClaimsPrincipal u, Guid id, Guid assetId, FolderRepository folders) =>
            await folders.RemoveAsset(CurrentAccount.Id(u), id, assetId) ? Results.NoContent() : Problems.NotFound());
    }
}
