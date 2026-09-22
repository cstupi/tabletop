using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

public sealed record EmailRequest(string Email);
public sealed record OtpVerifyRequest(string MethodId, string Code);
public sealed record PasswordRequest(string Email, string Password);
public sealed record ResetCompleteRequest(string Token, string Password);
public sealed record RefreshRequest(string SessionJwt);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/auth");
        g.MapPost("/otp", Guard(async (EmailRequest r, StytchClient s) => Results.Ok(new OtpSentResponse((await s.SendEmailOtp(r.Email)).MethodId))));
        g.MapPost("/otp/verify", Guard(async (OtpVerifyRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.AuthenticateOtp(r.MethodId, r.Code)))));
        g.MapPost("/password", Guard(async (PasswordRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.AuthenticatePassword(r.Email, r.Password)))));
        g.MapPost("/password/signup", Guard(async (PasswordRequest r, StytchClient s, Sessions x) => Results.Created("/v1/account", await x.Complete(await s.CreatePassword(r.Email, r.Password)))));
        g.MapPost("/password/reset", Guard(async (EmailRequest r, StytchClient s, ManagementOptions o) => { await s.StartPasswordReset(r.Email, $"{o.WebUrl.TrimEnd('/')}/reset-password"); return Results.Accepted(); }));
        g.MapPost("/password/reset/complete", Guard(async (ResetCompleteRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.CompletePasswordReset(r.Token, r.Password)))));
        g.MapPost("/session/refresh", Guard(async (RefreshRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.RefreshSession(r.SessionJwt)))));
        g.MapDelete("/session", async (HttpContext ctx, StytchClient s) =>
        {
            var jwt = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            // Logout has nothing to roll back: whatever Stytch says (already-revoked, rate-limited,
            // down), the client's own session is over as far as this API is concerned.
            try { await s.RevokeSession(jwt); } catch (StytchException) { }
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static Delegate Guard<T1, T2>(Func<T1, T2, Task<IResult>> h) => async (T1 a, T2 b) => { try { return await h(a, b); } catch (StytchException e) { return Sessions.Problem(e); } };
    private static Delegate Guard<T1, T2, T3>(Func<T1, T2, T3, Task<IResult>> h) => async (T1 a, T2 b, T3 c) => { try { return await h(a, b, c); } catch (StytchException e) { return Sessions.Problem(e); } };
}
