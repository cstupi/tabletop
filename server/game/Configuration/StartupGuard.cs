using Npgsql;

namespace Tabletop.GameServer.Configuration;

/// <summary>
/// What the environment allows. Development behaviour (SetDice, the developer exception page) needs the Development environment and
/// TABLETOP_DEV_MODE=1 together; Development without the opt-in refuses to start. Every other environment but the test host's is held
/// to production settings: Stytch's live JWKS over https and no database password of the dev stack.
/// </summary>
public static class StartupGuard
{
    public const string DevModeKey = "TABLETOP_DEV_MODE";
    public const string TestEnvironment = "Test";

    /// <summary>The dev stack's passwords (docker-compose.yml, .env.example, scripts/postgres/init): never valid outside Development.</summary>
    public static readonly IReadOnlySet<string> DevSecrets = new HashSet<string>(StringComparer.Ordinal)
    {
        "tabletop", "tabletop-secret", "tabletop-owner", "tabletop-management", "tabletop-game",
    };

    public static bool DevMode(string environment, IConfiguration c) => environment == Environments.Development && c[DevModeKey] == "1";

    public static IReadOnlyList<string> Problems(string environment, IConfiguration c)
    {
        if (environment == Environments.Development)
            return DevMode(environment, c) ? [] : ["ASPNETCORE_ENVIRONMENT is Development but TABLETOP_DEV_MODE is not 1. Development accepts SetDice, which sets a roll's faces: set TABLETOP_DEV_MODE=1 on a developer machine, or run with ASPNETCORE_ENVIRONMENT=Production."];
        if (environment == TestEnvironment) return [];
        var problems = new List<string>();
        if (c[DevModeKey] is { Length: > 0 }) problems.Add($"TABLETOP_DEV_MODE is set in the {environment} environment; it belongs only to Development.");
        var jwks = GameServerOptions.JwksUrl(c, c["TABLETOP_STYTCH_PROJECT_ID"] ?? "");
        if (!Uri.TryCreate(jwks, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || u.Host.Equals("test.stytch.com", StringComparison.OrdinalIgnoreCase))
            problems.Add($"TABLETOP_STYTCH_BASE_URL / TABLETOP_STYTCH_JWKS_URL give '{jwks}', not Stytch's live API over https; set TABLETOP_STYTCH_BASE_URL=https://api.stytch.com and the live project's id.");
        var password = c["TABLETOP_DB_CONNECTION"] is { } cs ? new NpgsqlConnectionStringBuilder(cs).Password : null;
        if (string.IsNullOrEmpty(password) || DevSecrets.Contains(password))
            problems.Add("TABLETOP_DB_CONNECTION has no password or the dev stack's; set the game role's own password.");
        return problems;
    }

    public static void Enforce(string environment, IConfiguration c)
    {
        var problems = Problems(environment, c);
        if (problems.Count > 0)
            throw new InvalidOperationException($"Refusing to start in the {environment} environment:\n  - " + string.Join("\n  - ", problems));
    }
}
