namespace Tabletop.GameServer.Configuration;

/// <summary>Loads KEY=VALUE lines from the nearest .env above <paramref name="startDirectory"/> into the process environment, without overriding variables already set.</summary>
public static class DotEnv
{
    public static void Load(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(file)) continue;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
                var key = line[..line.IndexOf('=')].Trim();
                var value = line[(line.IndexOf('=') + 1)..].Trim();
                if (Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, value);
            }
            return;
        }
    }
}
