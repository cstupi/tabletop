namespace Tabletop.Client.Core;

/// <summary>Where this build keeps settings.json, session.bin and the asset cache. A release build uses Godot's user dir
/// (<c>app_userdata/Tabletop</c>); a development build uses the sibling <c>Tabletop-dev</c>, so neither reads the other's
/// settings or sends the other's session JWT to its servers.</summary>
public sealed record UserData(string Dir)
{
    public string Settings => Path.Combine(Dir, "settings.json");
    public string Session => Path.Combine(Dir, "session.bin");
    public string Cache => Path.Combine(Dir, "cache");
    /// <summary>The server's built-in catalog, last complete sync (<see cref="BuiltinSync"/>); its files are in <see cref="Cache"/>.</summary>
    public string Builtin => Path.Combine(Dir, "builtin.json");

    /// <summary>A release build that finds server addresses in its settings.json is reading a file a development build (or a
    /// release before this rule) wrote, so the session beside it was issued by some other server: it clears that session and
    /// rewrites the settings without the addresses, keeping the rest.</summary>
    public static UserData Open(BuildInfo build, string godotUserDir)
    {
        var dir = godotUserDir.TrimEnd('/', '\\');
        if (!build.IsRelease) return new(dir + "-dev");
        var data = new UserData(dir);
        if (ClientSettings.NamesServers(data.Settings))
        {
            new SessionStore(data.Session).Clear();
            ClientSettings.Load(data.Settings, build).Save(data.Settings, build);
        }
        return data;
    }
}
