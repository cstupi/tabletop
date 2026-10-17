namespace Tabletop.Management.Configuration;

/// <summary>
/// Reads a .env (the file docker compose reads) into the process environment; existing variables win. Development only, and only
/// from the working directory: a deployed server never reads a .env, and never one from a directory above it.
/// </summary>
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
        var path = Path.Combine(directory, ".env");
        if (!File.Exists(path)) return;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
            var (key, value) = (line[..line.IndexOf('=')].Trim(), line[(line.IndexOf('=') + 1)..].Trim());
            if (value.Length > 0 && Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, value);
        }
    }
}
