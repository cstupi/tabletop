using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

public static class StytchAuthentication
{
    /// <summary>Our account UUID. Stytch's custom claim template maps {{ user.external_id }} to this name on every mint; we never send it per call.</summary>
    public const string AccountClaim = "account_id";
    public const string SessionClaim = "https://stytch.com/session";
    /// <summary>Named client so tests can swap its handler; the cache must be a singleton, which a typed client is not.</summary>
    public const string JwksClient = "stytch-jwks";
    public static string Issuer(string projectId) => $"stytch.com/{projectId}";

    public static IServiceCollection AddStytchJwt(this IServiceCollection services)
    {
        services.AddHttpClient(JwksClient);
        services.AddSingleton(sp => new StytchJwks(sp.GetRequiredService<IHttpClientFactory>().CreateClient(JwksClient), sp.GetRequiredService<ManagementOptions>()));
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
            o.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) => jwks.Resolve(kid).GetAwaiter().GetResult();
        });
        services.AddAuthorization();
        return services;
    }
}
