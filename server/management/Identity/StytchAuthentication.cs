using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

public static class StytchAuthentication
{
    /// <summary>Our account UUID. Sessions.Complete adds it to each new session as a session custom claim; refreshes keep it.</summary>
    public const string AccountClaim = "account_id";
    /// <summary>The Stytch user's RBAC role ids, stamped beside account_id by the same call. The game server reads it to admit an admin watcher.</summary>
    public const string RolesClaim = "roles";
    public const string SessionClaim = "https://stytch.com/session";
    /// <summary>Named client so tests can swap its handler; the cache must be a singleton, which a typed client is not.</summary>
    public const string JwksClient = "stytch-jwks";
    public static string Issuer(string projectId) => $"stytch.com/{projectId}";

    public static IServiceCollection AddStytchJwt(this IServiceCollection services)
    {
        services.AddHttpClient(JwksClient, c => c.Timeout = StytchJwks.DefaultFetchTimeout);
        services.AddSingleton(sp => new StytchJwks(sp.GetRequiredService<IHttpClientFactory>().CreateClient(JwksClient), sp.GetRequiredService<ManagementOptions>(),
            log: sp.GetRequiredService<ILogger<StytchJwks>>()));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });
        // The key resolver and the issuer/audience are configured here, rather than in the inline
        // lambda above, because ManagementOptions is not safe to read before builder.Build().
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<StytchJwks, ManagementOptions>((o, jwks, mo) =>
        {
            o.TokenValidationParameters.ValidIssuer = Issuer(mo.StytchProjectId);
            o.TokenValidationParameters.ValidAudience = mo.StytchProjectId;
            // The resolver is synchronous, so it reads the cache only; OnMessageReceived awaits the fetch (if any) before validation.
            o.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) => jwks.Cached(kid);
            o.Events = new JwtBearerEvents
            {
                OnMessageReceived = async ctx =>
                {
                    var header = ctx.Request.Headers.Authorization.ToString();
                    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return;
                    string? kid;
                    try { kid = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(header["Bearer ".Length..].Trim()).Kid; }
                    catch (Exception) { return; }                                // malformed: validation rejects it
                    await jwks.Ensure(kid);
                },
            };
        });
        services.AddAuthorization();
        return services;
    }
}
