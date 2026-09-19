using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Tabletop.Management.Tests;

/// <summary>Stands in for Stytch's signing key and its JWKS endpoint.</summary>
public static class TestKeys
{
    public const string ProjectId = "project-test-00000000-0000-0000-0000-000000000000";
    private static RsaSecurityKey _key = NewKey("kid-1");
    public static int Fetches;

    private static RsaSecurityKey NewKey(string kid) => new(RSA.Create(2048)) { KeyId = kid };
    public static void Rotate() => _key = NewKey("kid-" + Guid.NewGuid().ToString("N")[..6]);

    public static string Mint(Guid accountId, string projectId = ProjectId, DateTimeOffset? expires = null, string? issuer = null, string? kid = null)
    {
        var key = kid is null ? _key : new RsaSecurityKey(RSA.Create(2048)) { KeyId = kid };
        var exp = expires ?? DateTimeOffset.UtcNow.AddMinutes(5);
        var nbf = exp < DateTimeOffset.UtcNow ? exp.AddMinutes(-1) : DateTimeOffset.UtcNow.AddMinutes(-1);
        var token = new JwtSecurityToken(
            issuer: issuer ?? $"stytch.com/{projectId}", audience: projectId,
            claims: [new Claim("sub", "user-test-1"), new Claim(Identity.StytchAuthentication.AccountClaim, accountId.ToString())],
            notBefore: nbf.UtcDateTime, expires: exp.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static HttpMessageHandler JwksHandler => new Handler();

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Fetches++;
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(_key);
            var body = JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", alg = "RS256", use = "sig", kid = jwk.Kid, n = jwk.N, e = jwk.E } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
