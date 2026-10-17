namespace Tabletop.GameServer.Configuration;

/// <summary>Loads KEY=VALUE lines from <c>directory/.env</c> into the process environment, without overriding variables already set.
/// Never a parent directory's. The server loads it only in Development (LoadForDevelopment); the sim, a developer tool, loads it directly.</summary>
public static class DotEnv
{
    /// <summary>Loads directory/.env when <paramref name="environment"/> is Development.</summary>
    public static void LoadForDevelopment(string directory, string? environment)
    {
        if (environment == Environments.Development) Load(directory);
    }

    /// <summary>The environment the process asks for before the host is built: ASPNETCORE_ENVIRONMENT, else DOTNET_ENVIRONMENT.</summary>
    public static string? ProcessEnvironment() =>
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

    public static void Load(string directory)
    {
        var file = Path.Combine(directory, ".env");
        if (!File.Exists(file)) return;
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
            var key = line[..line.IndexOf('=')].Trim();
            var value = line[(line.IndexOf('=') + 1)..].Trim();
            if (value.Length > 0 && Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, value);
        }
    }
}
