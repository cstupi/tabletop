using Tabletop.Management.Http;

namespace Tabletop.Management.Identity;

public sealed record RenameRequest(string Name);
/// <summary>The body of <c>PUT /v1/account/username</c>: 3 to 32 of A–Z a–z 0–9 _ -, or null to clear it.</summary>
public sealed record UsernameRequest(string? Username);

public static class AccountEndpoints
{
    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/account").RequireAuthorization();
        group.MapGet("", async (System.Security.Claims.ClaimsPrincipal user, IAccountRepository accounts) =>
            await accounts.Get(CurrentAccount.Id(user)) is { } row ? Results.Ok(row) : Problems.Unauthorized());
        group.MapPatch("", async (System.Security.Claims.ClaimsPrincipal user, RenameRequest r, IAccountRepository accounts) =>
        {
            if (Limits.Text(r.Name?.Trim(), "name", Limits.Name) is { } problem) return Problems.Unprocessable(problem);
            var id = CurrentAccount.Id(user);
            return await accounts.Rename(id, r.Name!.Trim()) ? Results.Ok(await accounts.Get(id)) : Problems.Unauthorized();
        });
        group.MapPut("/username", async (System.Security.Claims.ClaimsPrincipal user, UsernameRequest r, IAccountRepository accounts) =>
        {
            var username = r.Username?.Trim();
            if (username is not null && Limits.Username(username) is { } problem) return Problems.Unprocessable(problem);
            var id = CurrentAccount.Id(user);
            return await accounts.SetUsername(id, username) switch
            {
                UsernameResult.Set => Results.Ok(await accounts.Get(id)),
                UsernameResult.Taken => Problems.Conflict("That username is taken."),
                _ => Problems.Unauthorized(),
            };
        });
        group.MapDelete("", async (System.Security.Claims.ClaimsPrincipal user, AccountDeleter deleter) =>
        {
            try { return await deleter.Delete(CurrentAccount.Id(user)) ? Results.NoContent() : Problems.Unauthorized(); }
            catch (StytchException e) { return Sessions.Problem(e); }
        });
    }
}
