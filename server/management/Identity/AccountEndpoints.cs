using Tabletop.Management.Http;

namespace Tabletop.Management.Identity;

public sealed record RenameRequest(string Name);

public static class AccountEndpoints
{
    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/account").RequireAuthorization();
        group.MapGet("", async (System.Security.Claims.ClaimsPrincipal user, AccountRepository accounts) =>
            await accounts.Get(CurrentAccount.Id(user)) is { } row ? Results.Ok(row) : Problems.Unauthorized());
        group.MapPatch("", async (System.Security.Claims.ClaimsPrincipal user, RenameRequest r, AccountRepository accounts) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return Problems.Unprocessable("name is required");
            var id = CurrentAccount.Id(user);
            return await accounts.Rename(id, r.Name.Trim()) ? Results.Ok(await accounts.Get(id)) : Problems.Unauthorized();
        });
        group.MapDelete("", async (System.Security.Claims.ClaimsPrincipal user, AccountDeleter deleter) =>
        {
            try { return await deleter.Delete(CurrentAccount.Id(user)) ? Results.NoContent() : Problems.Unauthorized(); }
            catch (StytchException e) { return Sessions.Problem(e); }
        });
    }
}
