using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class BuildInfoTests
{
    static Dictionary<string, string?> Meta(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => (string?)p.Value);

    [Fact]
    public void A_build_without_release_metadata_is_dev_with_the_local_stack()
    {
        var info = BuildInfo.From(Meta());
        Assert.Equal(("dev", null), (info.Version, info.WebUrl));
        var s = ClientSettings.DefaultFor(info);
        Assert.Equal(("http://127.0.0.1:5100", "ws://127.0.0.1:5200/ws", "http://127.0.0.1:5173"), (s.ManagementUrl, s.GameServerUrl, s.WebUrl));
        Assert.Equal("http://127.0.0.1:5173/download", s.DownloadUrl);
    }

    [Fact]
    public void A_release_build_reaches_management_and_the_socket_through_the_web_origin()
    {
        var info = BuildInfo.From(Meta(("TabletopVersion", "1.2.0"), ("TabletopWebUrl", "https://tabletop.example.org/")));
        Assert.Equal(("1.2.0", "https://tabletop.example.org"), (info.Version, info.WebUrl));
        var s = ClientSettings.DefaultFor(info);
        Assert.Equal(("https://tabletop.example.org", "wss://tabletop.example.org/ws"), (s.ManagementUrl, s.GameServerUrl));
        Assert.Equal("https://tabletop.example.org/download", s.DownloadUrl);
    }

    [Fact]
    public void A_plain_http_origin_keeps_its_port_and_uses_ws()
    {
        var s = ClientSettings.DefaultFor(BuildInfo.From(Meta(("TabletopWebUrl", "http://10.0.0.5:8080"))));
        Assert.Equal(("http://10.0.0.5:8080", "ws://10.0.0.5:8080/ws"), (s.ManagementUrl, s.GameServerUrl));
    }

    [Fact]
    public void Blank_metadata_counts_as_absent()
    {
        var info = BuildInfo.From(Meta(("TabletopVersion", " "), ("TabletopWebUrl", "")));
        Assert.Equal(("dev", null), (info.Version, info.WebUrl));
    }

    [Fact]
    public void The_test_assembly_carries_no_release_metadata() => Assert.Equal("dev", BuildInfo.Current.Version);
}
