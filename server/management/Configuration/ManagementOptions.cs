using Tabletop.Protocol;

namespace Tabletop.Management.Configuration;

/// <summary>Every setting the server reads, by the .env name without its TABLETOP_ prefix.</summary>
public sealed record ManagementOptions(
    string DbConnection, string S3Endpoint, string S3Bucket, string S3AccessKey, string S3SecretKey,
    string StytchProjectId, string StytchSecret, string StytchBaseUrl, string WebUrl,
    int MinimumClientVersion, int SessionMinutes, string? S3PublicEndpoint = null)
{
    public static ManagementOptions From(IConfiguration c) => new(
        Required(c, "DB_CONNECTION"), Required(c, "S3_ENDPOINT"), Required(c, "S3_BUCKET"),
        Required(c, "S3_ACCESS_KEY"), Required(c, "S3_SECRET_KEY"),
        Required(c, "STYTCH_PROJECT_ID"), Required(c, "STYTCH_SECRET"),
        c["STYTCH_BASE_URL"] ?? "https://test.stytch.com",
        c["WEB_URL"] ?? "http://localhost:5173",
        int.Parse(c["MIN_CLIENT_VERSION"] ?? c["MinimumClientVersion"] ?? ProtocolVersion.Current.ToString()),   // TABLETOP_MIN_CLIENT_VERSION: the same variable the game server reads
        int.Parse(c["SessionMinutes"] ?? "43200"),
        Present(c, "S3_PUBLIC_ENDPOINT"));

    /// <summary>The endpoint presigned URLs name, for a client that reaches MinIO at a different address than the server does.</summary>
    public string S3SigningEndpoint => S3PublicEndpoint ?? S3Endpoint;

    private static string? Present(IConfiguration c, string key) => string.IsNullOrEmpty(c[key]) ? null : c[key];

    private static string Required(IConfiguration c, string key) =>
        Present(c, key) ?? throw new InvalidOperationException($"Missing configuration TABLETOP_{key} (env, .env, or dotnet user-secrets).");
}
