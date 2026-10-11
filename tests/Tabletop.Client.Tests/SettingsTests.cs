using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class SettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-client-tests").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Defaults_when_missing_and_round_trips()
    {
        var path = Path.Combine(_dir, "settings.json");
        var s = ClientSettings.Load(path);
        Assert.Equal(ClientSettings.Default, s);
        Assert.Equal(("http://127.0.0.1:5100", "ws://127.0.0.1:5200/ws", true, false), (s.ManagementUrl, s.GameServerUrl, s.Animate, s.RememberMe));
        s = s.Mute("casting") with { Animate = false, RememberMe = true };
        s.Save(path);
        var back = ClientSettings.Load(path);
        Assert.Equal(s, back);
        Assert.Contains("casting", back.MutedWindowKinds);
        Assert.Empty(back.Unmute("casting").MutedWindowKinds);
    }

    [Fact]
    public void Unreadable_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        Assert.Equal(ClientSettings.Default, ClientSettings.Load(path));
    }

    [Fact]
    public void Session_store_round_trips_owner_only_and_clears()
    {
        var path = Path.Combine(_dir, "session.bin");
        var store = new SessionStore(path);
        Assert.Null(store.Read());
        store.Write("eyJ.jwt.here");
        Assert.Equal("eyJ.jwt.here", store.Read());
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        store.Clear();
        Assert.Null(store.Read());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_session_file_that_is_not_a_jwt_reads_as_null()
    {
        var path = Path.Combine(_dir, "session.bin");
        var store = new SessionStore(path);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Null(store.Read());
        File.WriteAllText(path, "");
        Assert.Null(store.Read());
        File.WriteAllText(path, "eyJ.only-two-segments");
        Assert.Null(store.Read());
    }

    [Fact]
    public void The_log_filter_and_drawer_state_round_trip_and_an_unknown_filter_reads_all()
    {
        var path = Path.Combine(_dir, "settings.json");
        var s = ClientSettings.Default with { LogFilter = LogFilter.Unit, LogOpen = true };
        s.Save(path);
        Assert.Equal(s, ClientSettings.Load(path));
        Assert.NotEqual(ClientSettings.Default, s);
        foreach (var json in new[] { """{ "logFilter": "sideways" }""", """{ "logFilter": "7" }""", """{ "animate": false }""" })
        {
            File.WriteAllText(path, json);
            Assert.Equal((LogFilter.All, false), (ClientSettings.Load(path).LogFilter, ClientSettings.Load(path).LogOpen));
        }
    }
}
