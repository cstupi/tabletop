using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class ServerAddressSettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-client-tests").FullName;
    public void Dispose() => Directory.Delete(_dir, true);
    string File_ => Path.Combine(_dir, "settings.json");

    static readonly BuildInfo Dev = new(BuildInfo.Dev, null);
    static readonly BuildInfo Release = new("1.2.0", "https://tabletop.example.org");
    const string LocalServers = """{ "theme": "fantasy", "managementUrl": "http://127.0.0.1:5100", "gameServerUrl": "ws://127.0.0.1:5200/ws", "webUrl": "http://127.0.0.1:5173" }""";

    [Fact]
    public void A_release_build_ignores_persisted_server_addresses()
    {
        File.WriteAllText(File_, LocalServers);
        var s = ClientSettings.Load(File_, Release);
        Assert.Equal(("https://tabletop.example.org", "wss://tabletop.example.org/ws", "https://tabletop.example.org"), (s.ManagementUrl, s.GameServerUrl, s.WebUrl));
        Assert.Equal("fantasy", s.Theme);
    }

    [Fact]
    public void A_release_build_writes_no_server_addresses()
    {
        var s = ClientSettings.DefaultFor(Release) with { Theme = "fantasy" };
        s.Save(File_, Release);
        var json = File.ReadAllText(File_);
        Assert.DoesNotContain("managementUrl", json);
        Assert.DoesNotContain("gameServerUrl", json);
        Assert.DoesNotContain("webUrl", json);
        Assert.False(ClientSettings.NamesServers(File_));
        Assert.Equal(s, ClientSettings.Load(File_, Release));
    }

    [Fact]
    public void A_dev_build_takes_persisted_server_addresses_and_writes_them()
    {
        File.WriteAllText(File_, """{ "managementUrl": "http://10.0.0.5:5100", "gameServerUrl": "ws://10.0.0.5:5200/ws", "webUrl": "http://10.0.0.5:5173" }""");
        var s = ClientSettings.Load(File_, Dev);
        Assert.Equal(("http://10.0.0.5:5100", "ws://10.0.0.5:5200/ws", "http://10.0.0.5:5173"), (s.ManagementUrl, s.GameServerUrl, s.WebUrl));
        s.Save(File_, Dev);
        Assert.True(ClientSettings.NamesServers(File_));
        Assert.Equal(s, ClientSettings.Load(File_, Dev));
    }

    [Fact]
    public void A_dev_build_defaults_to_the_local_stack()
    {
        var s = ClientSettings.Load(File_, Dev);
        Assert.Equal(("http://127.0.0.1:5100", "ws://127.0.0.1:5200/ws"), (s.ManagementUrl, s.GameServerUrl));
        Assert.False(ClientSettings.NamesServers(File_));
        File.WriteAllText(File_, "{ not json");
        Assert.False(ClientSettings.NamesServers(File_));
    }
}
