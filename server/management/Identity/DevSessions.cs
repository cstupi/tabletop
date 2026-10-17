using System.Security.Cryptography;
using System.Text;
using Tabletop.Management.Http;

namespace Tabletop.Management.Identity;

/// <summary>Roles, when given, are stamped in place of the Stytch user's, so a test mints an admin without touching the Stytch project.</summary>
public sealed record DevSessionRequest(string Name, IReadOnlyList<string>? Roles = null);

/// <summary>Development only. A real Stytch session JWT for a named account, so the simulator's socket driver runs the real verification path.</summary>
public sealed class DevSessions(StytchClient stytch, IAccountRepository accounts, Sessions sessions)
{
    private static readonly Guid Namespace = new("6f2b1c5e-0d3a-4f18-9b7c-2a5d8e4f1b60");

    /// <summary>A name-based UUIDv8 (RFC 9562 5.8): SHA-256 over the namespace and the name, first sixteen bytes, version and variant stamped.</summary>
    public static Guid AccountIdFor(string name)
    {
        var bytes = SHA256.HashData([.. Namespace.ToByteArray(), .. Encoding.UTF8.GetBytes(name)])[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>example.com is reserved by RFC 2606 and undeliverable. Nothing on this path sends mail.</summary>
    public static string EmailFor(string name) =>
        new string([.. name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-') + "@example.com";

    public async Task<SessionResponse> Mint(string name, IReadOnlyList<string>? roles = null)
    {
        var accountId = AccountIdFor(name);
        await accounts.Insert(accountId, name);                                              // no-op when the row exists
        var externalId = accountId.ToString();
        var userId = await stytch.FindUserId(externalId) ?? await stytch.CreateUser(EmailFor(name), externalId);
        var token = await stytch.CreateEmbeddableToken(userId);
        return await sessions.Complete(await stytch.AuthenticateMagicLink(token), roles);
    }
}

public static class DevSessionEndpoint
{
    /// <summary>Mapped only in dev mode (StartupGuard.DevMode: Development and TABLETOP_DEV_MODE=1). A production build serves no such route.</summary>
    public static void MapDevSession(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/dev/sessions", async (DevSessionRequest r, DevSessions dev) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 64) return Problems.Unprocessable("A name of 1 to 64 characters is required.");
            try { return Results.Ok(await dev.Mint(r.Name.Trim(), r.Roles)); }
            catch (StytchException e) { return Sessions.Problem(e); }
        });
}
