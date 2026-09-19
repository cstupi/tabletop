using System.Security.Claims;

namespace Tabletop.Management.Http;

public static class CurrentAccount
{
    public static Guid Id(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(Identity.StytchAuthentication.AccountClaim), out var id) ? id
            : throw new UnauthorizedAccessException("The session carries no account_id claim.");
}
