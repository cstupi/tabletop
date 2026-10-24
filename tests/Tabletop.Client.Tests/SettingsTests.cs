using Tabletop.Client.Core;
using Tabletop.Protocol;

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
    public void The_web_url_round_trips_and_names_the_download_page()
    {
        var path = Path.Combine(_dir, "settings.json");
        var s = ClientSettings.Default with { WebUrl = "https://play.example.org/" };
        s.Save(path);
        var back = ClientSettings.Load(path);
        Assert.Equal(s, back);
        Assert.Equal("https://play.example.org/download", back.DownloadUrl);
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
        foreach (var json in new[] { """{ "logFilter": "sideways" }""", """{ "logFilter": "7" }""", """{ "logFilter": "2" }""", """{ "animate": false }""" })
        {
            File.WriteAllText(path, json);
            Assert.Equal((LogFilter.All, false), (ClientSettings.Load(path).LogFilter, ClientSettings.Load(path).LogOpen));
        }
    }

    [Fact]
    public void Settings_round_trip_every_new_field()
    {
        var path = Path.Combine(_dir, "settings.json"); var d = ClientSettings.Default;
        Assert.Equal(("scifi", true, true, false), (d.Theme, d.CameraFollow, d.TurnBanner, d.RangeRings));
        Assert.NotEqual(d, d with { Theme = "fantasy" }); Assert.NotEqual(d, d with { CameraFollow = false }); Assert.NotEqual(d, d with { TurnBanner = false }); Assert.NotEqual(d, d with { RangeRings = true }); Assert.NotEqual(d, d with { MeasureMode = MeasureMode.Straight });
        var s = d with { Theme = "fantasy", CameraFollow = false, TurnBanner = false, RangeRings = true, MeasureMode = MeasureMode.Straight }; s.Save(path);
        var back = ClientSettings.Load(path);
        Assert.Equal(("fantasy", false, false, true, MeasureMode.Straight), (back.Theme, back.CameraFollow, back.TurnBanner, back.RangeRings, back.MeasureMode)); Assert.Equal(s, back);
    }

    [Theory, InlineData("""{ "theme": "steampunk" }"""), InlineData("""{ "animate": false }""")]
    public void Unknown_theme_in_settings_reads_scifi(string json)
    {
        var path = Path.Combine(_dir, "settings.json"); File.WriteAllText(path, json); var s = ClientSettings.Load(path);
        Assert.Equal(("scifi", true, true, false), (s.Theme, s.CameraFollow, s.TurnBanner, s.RangeRings));
    }

    [Fact]
    public void Sound_is_on_at_volume_70_round_trips_and_loads_clamped()
    {
        var path = Path.Combine(_dir, "settings.json"); var d = ClientSettings.Default;
        Assert.Equal((true, 70), (d.Sound, d.Volume));
        Assert.NotEqual(d, d with { Sound = false }); Assert.NotEqual(d, d with { Volume = 20 });
        var s = d with { Sound = false, Volume = 20 }; s.Save(path);
        Assert.Equal(s, ClientSettings.Load(path));
        File.WriteAllText(path, """{ "animate": false }""");
        Assert.Equal((true, 70), (ClientSettings.Load(path).Sound, ClientSettings.Load(path).Volume));
        File.WriteAllText(path, """{ "volume": 400 }"""); Assert.Equal(100, ClientSettings.Load(path).Volume);
        File.WriteAllText(path, """{ "volume": -5 }"""); Assert.Equal(0, ClientSettings.Load(path).Volume);
    }

    [Fact]
    public void Graphics_default_to_windowed_medium_vsync_with_no_override()
    {
        var d = ClientSettings.Default;
        Assert.Equal((DisplayMode.Windowed, GraphicsQuality.Medium, (int?)null, true, (WindowRect?)null), (d.Display, d.Quality, d.RenderScale, d.VSync, d.Window));
        Assert.Equal(GraphicsSettings.Profile(GraphicsQuality.Medium, null), d.Graphics);
    }

    [Fact]
    public void Graphics_round_trip_and_each_field_counts_for_equality()
    {
        var path = Path.Combine(_dir, "settings.json"); var d = ClientSettings.Default;
        Assert.NotEqual(d, d with { Display = DisplayMode.Fullscreen }); Assert.NotEqual(d, d with { Quality = GraphicsQuality.High });
        Assert.NotEqual(d, d with { RenderScale = 80 }); Assert.NotEqual(d, d with { VSync = false }); Assert.NotEqual(d, d with { Window = new WindowRect(1, 2, 1280, 720) });
        var s = d with { Display = DisplayMode.Fullscreen, Quality = GraphicsQuality.Low, RenderScale = 60, VSync = false, Window = new WindowRect(100, 80, 1600, 900) }; s.Save(path);
        var back = ClientSettings.Load(path);
        Assert.Equal(s, back);
        Assert.Equal(new GraphicsProfile(0, 2, 2048, 120f, 60), back.Graphics);
    }

    [Fact]
    public void An_old_file_loads_graphics_at_the_defaults()
    {
        var path = Path.Combine(_dir, "settings.json"); File.WriteAllText(path, """{ "animate": false }"""); var s = ClientSettings.Load(path);
        Assert.Equal((DisplayMode.Windowed, GraphicsQuality.Medium, (int?)null, true, (WindowRect?)null), (s.Display, s.Quality, s.RenderScale, s.VSync, s.Window));
    }

    [Theory]
    [InlineData("""{ "display": "borderless", "quality": "ultra", "renderScale": 30, "window": { "x": 0, "y": 0, "width": 200, "height": 100 } }""", 50)]
    [InlineData("""{ "display": "1", "quality": "2", "renderScale": 130 }""", 100)]
    [InlineData("""{ "renderScale": 62 }""", 60)]
    public void Bad_graphics_values_load_at_defaults_or_clamped(string json, int scale)
    {
        var path = Path.Combine(_dir, "settings.json"); File.WriteAllText(path, json); var s = ClientSettings.Load(path);
        Assert.Equal((DisplayMode.Windowed, GraphicsQuality.Medium, (int?)scale, (WindowRect?)null), (s.Display, s.Quality, s.RenderScale, s.Window));
    }
}
