using Tabletop.Management.Http;

namespace Tabletop.Management.Identity;

public sealed record SessionResponse(string SessionJwt, AccountRow Account);
public sealed record OtpSentResponse(string MethodId);

/// <summary>After any Stytch authenticate: make sure the account exists and is linked to the Stytch user. The JWT's account_id comes from the project's claim template.</summary>
public sealed class Sessions(StytchClient stytch, AccountRepository accounts)
{
    public async Task<SessionResponse> Complete(StytchSession session)
    {
        var name = session.User.Emails.FirstOrDefault()?.Email.Split('@')[0] ?? "player";
        var jwt = session.SessionJwt;
        Guid accountId;
        if (Guid.TryParse(session.User.ExternalId, out var linked))
        {
            accountId = linked;
            await accounts.Insert(accountId, name);                      // no-op when the row exists
        }
        else
        {
            accountId = Guid.CreateVersion7();
            await accounts.Insert(accountId, name);
            await stytch.SetExternalId(session.User.UserId, accountId.ToString());
            // The session above was minted before the user was linked, so its JWT has no account_id.
            // Re-minting is the documented way to pick the template up ("You must authenticate again to update claims").
            // First login only; every later login and every refresh skips this branch.
            jwt = (await stytch.RefreshSession(jwt)).SessionJwt;
        }
        var account = await accounts.Get(accountId) ?? throw new InvalidOperationException("account vanished");
        return new SessionResponse(jwt, account);
    }

    public static IResult Problem(StytchException e) => e.Status switch
    {
        401 or 404 => Results.Problem(title: "Unauthorized", detail: e.ErrorType, statusCode: 401),
        _ when e.ErrorType == "duplicate_email" => Problems.Conflict("An account with that email already exists."),
        >= 500 => Problems.BadGateway($"Stytch: {e.ErrorType}"),
        _ => Problems.Unprocessable(e.ErrorType),
    };
}
