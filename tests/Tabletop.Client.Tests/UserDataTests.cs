using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class UserDataTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-client-tests").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    static readonly BuildInfo Dev = new(BuildInfo.Dev, null);
    static readonly BuildInfo Release = new("1.2.0", "https://tabletop.example.org");
    const string LocalServers = """{ "theme": "fantasy", "managementUrl": "http://127.0.0.1:5100", "gameServerUrl": "ws://127.0.0.1:5200/ws", "webUrl": "http://127.0.0.1:5173" }""";

    string GodotDir => Path.Combine(_dir, "Tabletop");

    [Fact]
    public void A_release_build_keeps_the_godot_user_dir_and_a_dev_build_takes_a_sibling()
    {
        Assert.Equal(GodotDir, UserData.Open(Release, GodotDir).Dir);
        Assert.Equal(Path.Combine(_dir, "Tabletop-dev"), UserData.Open(Dev, GodotDir).Dir);
        Assert.Equal(Path.Combine(_dir, "Tabletop-dev"), UserData.Open(Dev, GodotDir + Path.DirectorySeparatorChar).Dir);
        var d = UserData.Open(Dev, GodotDir);
        Assert.Equal((Path.Combine(d.Dir, "settings.json"), Path.Combine(d.Dir, "session.bin"), Path.Combine(d.Dir, "cache")), (d.Settings, d.Session, d.Cache));
    }

    [Fact]
    public void A_release_build_drops_a_session_saved_beside_server_addresses_and_keeps_the_rest()
    {
        Directory.CreateDirectory(GodotDir);
        File.WriteAllText(Path.Combine(GodotDir, "settings.json"), LocalServers);
        new SessionStore(Path.Combine(GodotDir, "session.bin")).Write("eyJ.dev.jwt");
        var d = UserData.Open(Release, GodotDir);
        Assert.Null(new SessionStore(d.Session).Read());
        Assert.False(ClientSettings.NamesServers(d.Settings));
        var s = ClientSettings.Load(d.Settings, Release);
        Assert.Equal(("fantasy", "https://tabletop.example.org", "wss://tabletop.example.org/ws"), (s.Theme, s.ManagementUrl, s.GameServerUrl));
    }

    [Fact]
    public void A_release_build_keeps_its_own_session()
    {
        var d = UserData.Open(Release, GodotDir);
        ClientSettings.DefaultFor(Release).Save(d.Settings, Release);
        new SessionStore(d.Session).Write("eyJ.prod.jwt");
        Assert.Equal("eyJ.prod.jwt", new SessionStore(UserData.Open(Release, GodotDir).Session).Read());
    }

    [Fact]
    public void A_dev_build_never_touches_the_release_dir()
    {
        Directory.CreateDirectory(GodotDir);
        File.WriteAllText(Path.Combine(GodotDir, "settings.json"), LocalServers);
        new SessionStore(Path.Combine(GodotDir, "session.bin")).Write("eyJ.some.jwt");
        UserData.Open(Dev, GodotDir);
        Assert.Equal("eyJ.some.jwt", new SessionStore(Path.Combine(GodotDir, "session.bin")).Read());
        Assert.Equal(LocalServers, File.ReadAllText(Path.Combine(GodotDir, "settings.json")));
    }
}
