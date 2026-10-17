using System.Net;
using Npgsql;

namespace Tabletop.Management.Configuration;

/// <summary>
/// What the environment allows. Development behaviour (POST /v1/dev/sessions, the developer exception page, migrations at startup)
/// needs the Development environment and TABLETOP_DEV_MODE=1 together; Development without the opt-in refuses to start. Every other
/// environment but the test host's is held to production settings: a live Stytch URL, https public origins that are not loopback,
/// and no credential of the dev stack.
/// </summary>
public static class StartupGuard
{
    public const string DevModeKey = "DEV_MODE";
    public const string TestEnvironment = "Testing";

    /// <summary>The dev stack's credentials (docker-compose.yml, .env.example, scripts/postgres/init, deploy/minio): never valid outside Development.</summary>
    public static readonly IReadOnlySet<string> DevSecrets = new HashSet<string>(StringComparer.Ordinal)
    {
        "tabletop", "tabletop-secret", "tabletop-owner", "tabletop-management", "tabletop-game", "tabletop-app", "tabletop-app-secret",
    };

    public static bool DevMode(string environment, IConfiguration c) => environment == Environments.Development && c[DevModeKey] == "1";

    public static IReadOnlyList<string> Problems(string environment, IConfiguration c, ManagementOptions o)
    {
        if (environment == Environments.Development)
            return DevMode(environment, c) ? [] : ["ASPNETCORE_ENVIRONMENT is Development but TABLETOP_DEV_MODE is not 1. Development maps POST /v1/dev/sessions, which mints a session with no login: set TABLETOP_DEV_MODE=1 on a developer machine, or run with ASPNETCORE_ENVIRONMENT=Production."];
        if (environment == TestEnvironment) return [];
        var problems = new List<string>();
        if (c[DevModeKey] is { Length: > 0 }) problems.Add($"TABLETOP_DEV_MODE is set in the {environment} environment; it belongs only to Development.");
        if (Host(o.StytchBaseUrl) is not { } stytch || stytch == "test.stytch.com" || !Https(o.StytchBaseUrl))
            problems.Add($"TABLETOP_STYTCH_BASE_URL is '{o.StytchBaseUrl}', not Stytch's live API; set https://api.stytch.com with the live project's id and secret.");
        PublicOrigin(problems, "TABLETOP_WEB_URL", o.WebUrl);
        PublicOrigin(problems, "TABLETOP_S3_PUBLIC_ENDPOINT", o.S3SigningEndpoint);
        if (DevSecrets.Contains(o.S3AccessKey)) problems.Add("TABLETOP_S3_ACCESS_KEY is the dev stack's; use the bucket-scoped service user's key.");
        if (DevSecrets.Contains(o.S3SecretKey)) problems.Add("TABLETOP_S3_SECRET_KEY is the dev stack's; use the bucket-scoped service user's secret.");
        problems.AddRange(DatabaseProblems(environment, o.DbConnection));
        return problems;
    }

    /// <summary>The checks a migration run (--migrate) makes: it reads only TABLETOP_DB_CONNECTION.</summary>
    public static IReadOnlyList<string> DatabaseProblems(string environment, string connection)
    {
        if (environment == Environments.Development || environment == TestEnvironment) return [];
        var password = new NpgsqlConnectionStringBuilder(connection).Password;
        return string.IsNullOrEmpty(password) || DevSecrets.Contains(password)
            ? ["TABLETOP_DB_CONNECTION has no password or the dev stack's; set the role's own password."]
            : [];
    }

    public static void Enforce(string environment, IConfiguration c, ManagementOptions o) => Throw(environment, Problems(environment, c, o));

    public static void Throw(string environment, IReadOnlyList<string> problems)
    {
        if (problems.Count > 0)
            throw new InvalidOperationException($"Refusing to start in the {environment} environment:\n  - " + string.Join("\n  - ", problems));
    }

    private static void PublicOrigin(List<string> problems, string name, string url)
    {
        if (!Https(url) || Host(url) is not { } host || Loopback(host))
            problems.Add($"{name} is '{url}'; outside Development it must be the public https origin players reach.");
    }

    private static bool Https(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
    private static string? Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : null;

    private static bool Loopback(string host) =>
        host is "localhost" or "0.0.0.0" or "[::]" || host.EndsWith(".localhost", StringComparison.Ordinal)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6Any)));
}
