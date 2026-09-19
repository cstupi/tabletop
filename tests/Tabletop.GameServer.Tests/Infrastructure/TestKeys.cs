using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tabletop.GameServer.Auth;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestKeys
{
    public static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-kid-1" };
    public static readonly JsonWebKeySet Jwks = new() { Keys = { JsonWebKeyConverter.ConvertFromRSASecurityKey(Key) } };
    public static IJwksSource Source => new StaticJwksSource(Jwks);

    public static string Mint(Guid accountId, string? issuer = null, string? audience = null, TimeSpan? lifetime = null, bool includeAccountClaim = true)
    {
        var claims = new Dictionary<string, object> { ["sub"] = "user-test-" + accountId.ToString("N")[..12] };
        if (includeAccountClaim) claims[StytchJwtVerifier.AccountClaim] = accountId.ToString();
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? $"stytch.com/{GameServerFactory.ProjectId}",
            Audience = audience ?? GameServerFactory.ProjectId,
            Expires = DateTime.UtcNow + (lifetime ?? TimeSpan.FromMinutes(5)),
            IssuedAt = DateTime.UtcNow,
            Claims = claims,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
        });
    }
}

public sealed class StaticJwksSource(JsonWebKeySet jwks) : IJwksSource
{
    public Task<JsonWebKeySet> GetAsync(CancellationToken ct) => Task.FromResult(jwks);
}
