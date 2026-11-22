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
        s = s with { Animate = false, RememberMe = true };
        s.Save(path);
        var back = ClientSettings.Load(path);
        Assert.Equal(s, back);
    }

    /// <summary>A settings file from a build that stored muted prompt kinds loads with its other settings, and the key is not written back.</summary>
    [Fact]
    public void A_stored_muted_window_kinds_key_loads_and_is_dropped_on_save()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "animate": false, "mutedWindowKinds": ["casting", "before-attacking"], "rememberMe": true, "logOpen": true }""");
        var s = ClientSettings.Load(path);
        Assert.Equal((false, true, true), (s.Animate, s.RememberMe, s.LogOpen));
        s.Save(path);
        Assert.DoesNotContain("mutedWindowKinds", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_old_file_loads_every_new_field_at_its_default()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            { "animate": true, "rememberMe": false, "logFilter": "All", "logOpen": false, "theme": "fantasy", "cameraFollow": true, "turnBanner": true,
            "measureMode": "Horizontal", "showHitboxes": true, "showTerrainHitboxes": true, "sound": true, "volume": 55, "display": "Fullscreen", "quality": "High",
            "vSync": true, "logDetailed": false, "weaponRanges": true, "fpsCap": 0 }
            """);
        var s = ClientSettings.Load(path);
        Assert.Equal((55, DisplayMode.Fullscreen, "fantasy"), (s.Volume, s.Display, s.Theme));
        Assert.Equal((true, 100, 100, 100, false, false, false), (s.WoundNumbers, s.PanSpeed, s.OrbitSpeed, s.ZoomSpeed, s.EdgeScroll, s.InvertOrbit, s.InvertZoom));
        Assert.Empty(s.KeyOverrides);
        Assert.Equal((false, 100, false), (s.ShowFps, s.EffectsVolume, s.MuteInBackground));
        Assert.Equal((100, 150, 4), (s.UiScale, s.TooltipMs, s.ToastSeconds));
        Assert.Equal((false, 100, false), (s.ColourBlind, s.TextSize, s.ReduceMotion));
    }

    [Fact]
    public void A_file_with_the_removed_interface_volume_still_loads()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "volume": 40, "effectsVolume": 70, "interfaceVolume": 20 }""");
        var s = ClientSettings.Load(path);
        Assert.Equal((40, 70), (s.Volume, s.EffectsVolume));
        s.Save(path);
        Assert.DoesNotContain("interfaceVolume", File.ReadAllText(path));
    }

    [Fact]
    public void New_fields_round_trip_and_out_of_range_values_clamp()
    {
        var path = Path.Combine(_dir, "settings.json");
        var keys = new Dictionary<Intent, IReadOnlyList<KeyBinding>> { [Intent.NextUnit] = [new KeyBinding(HotKey.N, true, false)], [Intent.MeasureMode] = [new KeyBinding(HotKey.J, null, null)] };
        var s = ClientSettings.Default with { WoundNumbers = false, PanSpeed = 150, OrbitSpeed = 50, ZoomSpeed = 200, EdgeScroll = true, InvertOrbit = true, InvertZoom = true,
            KeyOverrides = keys, ShowFps = true, EffectsVolume = 40, MuteInBackground = true, UiScale = 125, TooltipMs = 500, ToastSeconds = 8,
            ColourBlind = true, TextSize = 130, ReduceMotion = true, Display = DisplayMode.Exclusive };
        s.Save(path);
        var back = ClientSettings.Load(path);
        Assert.Equal(s, back);
        Assert.Equal(new KeyBinding(HotKey.N, true, false), back.KeyOverrides[Intent.NextUnit][0]);
        Assert.Equal(new KeyBinding(HotKey.J, null, null), back.KeyOverrides[Intent.MeasureMode][0]);

        File.WriteAllText(path, """
            { "panSpeed": 900, "uiScale": 10, "textSize": 117, "tooltipMs": -5, "toastSeconds": 99, "effectsVolume": 300,
            "keys": { "Confirm": [{ "key": "J" }], "Nonsense": [{ "key": "J" }], "Ping": [{ "key": "NotAKey" }] } }
            """);
        back = ClientSettings.Load(path);
        Assert.Equal((200, 75, 100, 0, 10, 100), (back.PanSpeed, back.UiScale, back.TextSize, back.TooltipMs, back.ToastSeconds, back.EffectsVolume));
        Assert.Empty(back.KeyOverrides);   // Confirm is not rebindable; unknown names drop
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
        Assert.Equal(("scifi", true, true, true), (d.Theme, d.CameraFollow, d.TurnBanner, d.RangeRings));
        Assert.NotEqual(d, d with { Theme = "fantasy" }); Assert.NotEqual(d, d with { CameraFollow = false }); Assert.NotEqual(d, d with { TurnBanner = false }); Assert.NotEqual(d, d with { RangeRings = false }); Assert.NotEqual(d, d with { MeasureMode = MeasureMode.Straight });
        var s = d with { Theme = "fantasy", CameraFollow = false, TurnBanner = false, RangeRings = false, MeasureMode = MeasureMode.Straight }; s.Save(path);
        var back = ClientSettings.Load(path);
        Assert.Equal(("fantasy", false, false, false, MeasureMode.Straight), (back.Theme, back.CameraFollow, back.TurnBanner, back.RangeRings, back.MeasureMode)); Assert.Equal(s, back);
    }

    /// <summary>The owner, 2026-11-14: "It is not showing the range of friendly units for some players". Range rings were off unless a player
    /// turned them on, and every save wrote that off into settings.json. They are on by default and stored under a new name, so a file from
    /// an older build, whose "rangeRings": false nobody chose, reads as on.</summary>
    [Fact]
    public void Range_rings_are_on_by_default_and_an_older_files_off_is_not_kept()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.True(ClientSettings.Load(path).RangeRings);                                             // no file
        File.WriteAllText(path, """{ "animate": true, "rangeRings": false }""");
        Assert.True(ClientSettings.Load(path).RangeRings);                                             // an older build's file
        (ClientSettings.Load(path) with { RangeRings = false }).Save(path);
        Assert.False(ClientSettings.Load(path).RangeRings);                                            // turned off in this build: kept
        Assert.DoesNotContain("rangeRings", File.ReadAllText(path));
    }

    [Theory, InlineData("""{ "theme": "steampunk" }"""), InlineData("""{ "animate": false }""")]
    public void Unknown_theme_in_settings_reads_scifi(string json)
    {
        var path = Path.Combine(_dir, "settings.json"); File.WriteAllText(path, json); var s = ClientSettings.Load(path);
        Assert.Equal(("scifi", true, true, true), (s.Theme, s.CameraFollow, s.TurnBanner, s.RangeRings));
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
    public void Frame_rate_limit_defaults_to_unlimited_and_round_trips()
    {
        var path = Path.Combine(_dir, "settings.json"); var d = ClientSettings.Default;
        Assert.Equal(0, d.FpsCap);
        Assert.Equal(new[] { 0, 30, 60, 120, 144, 240 }, ClientSettings.FpsCaps);
        Assert.NotEqual(d, d with { FpsCap = 60 });
        foreach (var cap in ClientSettings.FpsCaps) { (d with { FpsCap = cap }).Save(path); Assert.Equal(cap, ClientSettings.Load(path).FpsCap); }
        File.WriteAllText(path, """{ "animate": false }"""); Assert.Equal(0, ClientSettings.Load(path).FpsCap);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(-30)]
    [InlineData(100000)]
    public void An_unknown_frame_rate_limit_loads_as_unlimited(int stored)
    {
        var path = Path.Combine(_dir, "settings.json"); File.WriteAllText(path, $$"""{ "fpsCap": {{stored}} }""");
        Assert.Equal(0, ClientSettings.Load(path).FpsCap);
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
        Assert.Equal(new GraphicsProfile(0, 2, 2048, 120f, 60, false, false, 4f), back.Graphics);
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
