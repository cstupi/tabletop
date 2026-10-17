using Tabletop.Management.Configuration;
using Tabletop.Management.Http;

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
        // The credential endpoints: a per-IP budget (RateLimiting.AuthPolicy) and, where the body names an email, a per-email one.
        var creds = g.MapGroup("").RequireRateLimiting(RateLimiting.AuthPolicy).AddEndpointFilter(PerEmail);
        creds.MapPost("/otp", Guard(async (EmailRequest r, StytchClient s) => Results.Ok(new OtpSentResponse((await s.SendEmailOtp(r.Email)).MethodId))));
        creds.MapPost("/otp/verify", Guard(async (OtpVerifyRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.AuthenticateOtp(r.MethodId, r.Code)))));
        creds.MapPost("/password", Guard(async (PasswordRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.AuthenticatePassword(r.Email, r.Password)))));
        creds.MapPost("/password/signup", Guard(async (PasswordRequest r, StytchClient s, Sessions x) => Results.Created("/v1/account", await x.Complete(await s.CreatePassword(r.Email, r.Password)))));
        creds.MapPost("/password/reset", Guard(async (EmailRequest r, StytchClient s, ManagementOptions o) => { await s.StartPasswordReset(r.Email, $"{o.WebUrl.TrimEnd('/')}/reset-password"); return Results.Accepted(); }));
        creds.MapPost("/password/reset/complete", Guard(async (ResetCompleteRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.CompletePasswordReset(r.Token, r.Password)))));
        g.MapPost("/session/refresh", Guard(async (RefreshRequest r, StytchClient s, Sessions x) =>
        {
            // A session Stytch will not extend (expired, revoked, unknown, malformed) is unauthenticated, not unprocessable.
            try { return Results.Ok(await x.Complete(await s.RefreshSession(r.SessionJwt))); }
            catch (StytchException e) when (e.Status is >= 400 and < 500 and not 429) { return Results.Problem(title: "Unauthorized", detail: "The session has ended. Sign in again.", statusCode: 401); }
        }));
        g.MapDelete("/session", async (HttpContext ctx, StytchClient s) =>
        {
            var jwt = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            // Logout has nothing to roll back: whatever Stytch says (already-revoked, rate-limited,
            // down), the client's own session is over as far as this API is concerned.
            try { await s.RevokeSession(jwt); } catch (StytchException) { }
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static async ValueTask<object?> PerEmail(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var email = ctx.Arguments.Select(a => a switch { EmailRequest e => e.Email, PasswordRequest p => p.Email, _ => null }).FirstOrDefault(e => e is not null);
        if (email is not null && ctx.HttpContext.RequestServices.GetRequiredService<EmailThrottle>().TryAcquire(email) is { } wait)
            return Problems.TooManyRequests("Too many attempts. Wait a minute and try again.", wait);
        return await next(ctx);
    }

    private static Delegate Guard<T1, T2>(Func<T1, T2, Task<IResult>> h) => async (T1 a, T2 b, HttpContext ctx) => { try { return await h(a, b); } catch (StytchException e) { return Refused(ctx, e); } };
    private static Delegate Guard<T1, T2, T3>(Func<T1, T2, T3, Task<IResult>> h) => async (T1 a, T2 b, T3 c, HttpContext ctx) => { try { return await h(a, b, c); } catch (StytchException e) { return Refused(ctx, e); } };

    /// <summary>The caller gets Sessions.Problem's fixed text; Stytch's own error is logged.</summary>
    private static IResult Refused(HttpContext ctx, StytchException e)
    {
        ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AuthEndpoints)).LogInformation("{Path}: Stytch {Status} {ErrorType}", ctx.Request.Path, e.Status, e.ErrorType);
        return Sessions.Problem(e);
    }
}
