using Tabletop.Management.Http;

namespace Tabletop.Management.Identity;

public static class AccountEndpoints
{
    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/account").RequireAuthorization();
        group.MapGet("", async (System.Security.Claims.ClaimsPrincipal user, AccountRepository accounts) =>
            await accounts.Get(CurrentAccount.Id(user)) is { } row ? Results.Ok(row) : Results.Unauthorized());
    }
}
