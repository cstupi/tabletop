using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Identity;

public sealed record SessionResponse(string SessionJwt, AccountRow Account);
public sealed record OtpSentResponse(string MethodId);

/// <summary>After any Stytch authenticate: make sure the account exists, is linked to the Stytch user, has its starter miniatures, and rides in the session's account_id claim.</summary>
public sealed class Sessions(StytchClient stytch, AccountRepository accounts, StarterAssets starter, ILogger<Sessions> log)
{
    public async Task<SessionResponse> Complete(StytchSession session)
    {
        var name = session.User.Emails.FirstOrDefault()?.Email.Split('@')[0] ?? "player";
        Guid accountId;
        if (Guid.TryParse(session.User.ExternalId, out var linked))
        {
            accountId = linked;
            await accounts.Insert(accountId, name);                      // no-op when the row exists
        }
        else
        {
            accountId = Guid.CreateVersion7();
            // Link first: if this throws, no row is ever inserted for this attempt, so a retry
            // (a fresh v7 id) leaves nothing orphaned. If the row-insert or the stamp below
            // throws instead, the user is already linked, and the next login takes the branch
            // above, which inserts the row that is missing — that is the self-heal.
            await stytch.SetExternalId(session.User.UserId, accountId.ToString());
            await accounts.Insert(accountId, name);
        }
        // Starter content never blocks a login: on failure starter_seeded_at stays null and the next login tries again.
        try { await starter.SeedIfNeeded(accountId); }
        catch (Exception e) { log.LogError(e, "cannot seed the starter miniatures for {AccountId}", accountId); }
        // A new session carries no account_id until it is stamped; a refreshed one keeps its stamp, so this skips.
        var jwt = AccountClaim(session.SessionJwt) == accountId.ToString()
            ? session.SessionJwt
            : (await stytch.StampAccount(session.SessionJwt, accountId)).SessionJwt;
        var account = await accounts.Get(accountId) ?? throw new InvalidOperationException("account vanished");
        return new SessionResponse(jwt, account);
    }

    /// <summary>Read without verifying: the JWT came straight from Stytch over the authenticated call.</summary>
    private static string? AccountClaim(string jwt) =>
        new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(jwt).TryGetPayloadValue<string>(StytchAuthentication.AccountClaim, out var v) ? v : null;

    public static IResult Problem(StytchException e) => e.Status switch
    {
        401 or 404 => Results.Problem(title: "Unauthorized", detail: e.ErrorType, statusCode: 401),
        _ when e.ErrorType == "duplicate_email" => Problems.Conflict("An account with that email already exists."),
        429 => Problems.TooManyRequests(e.ErrorType),
        >= 500 => Problems.BadGateway($"Stytch: {e.ErrorType}"),
        _ => Problems.Unprocessable(e.ErrorType),
    };
}
