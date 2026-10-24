using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class GraphicsSettingsTests
{
    [Fact] public void Medium_is_todays_shadows_with_msaa_2x() =>
        Assert.Equal(new GraphicsProfile(2, 4, 4096, 220f, 100), GraphicsSettings.Profile(GraphicsQuality.Medium, null));
    [Fact] public void Low_and_high_match_the_spec_table()
    {
        Assert.Equal(new GraphicsProfile(0, 2, 2048, 120f, 75), GraphicsSettings.Profile(GraphicsQuality.Low, null));
        Assert.Equal(new GraphicsProfile(4, 4, 8192, 220f, 100), GraphicsSettings.Profile(GraphicsQuality.High, null));
    }
    [Fact] public void An_override_replaces_only_the_scale() =>
        Assert.Equal(GraphicsSettings.Profile(GraphicsQuality.High, null) with { RenderScalePercent = 60 }, GraphicsSettings.Profile(GraphicsQuality.High, 60));
    [Fact] public void Below_100_percent_upscales() =>
        Assert.Equal((true, 0.75f, false), (GraphicsSettings.Profile(GraphicsQuality.Low, null).Upscaled, GraphicsSettings.Profile(GraphicsQuality.Low, null).RenderScale, GraphicsSettings.Profile(GraphicsQuality.Medium, null).Upscaled));
    [Theory, InlineData(null, null), InlineData(40, 50), InlineData(120, 100), InlineData(52, 50), InlineData(53, 55), InlineData(75, 75)]
    public void The_scale_clamps_and_snaps_to_steps_of_five(int? given, int? expected) => Assert.Equal(expected, GraphicsSettings.ClampScale(given));
    [Fact] public void A_window_under_640_by_360_reads_as_none()
    {
        Assert.Null(GraphicsSettings.ClampWindow(new WindowRect(0, 0, 639, 720)));
        Assert.Null(GraphicsSettings.ClampWindow(new WindowRect(0, 0, 1280, 359)));
        Assert.Equal(new WindowRect(10, 20, 640, 360), GraphicsSettings.ClampWindow(new WindowRect(10, 20, 640, 360)));
        Assert.Null(GraphicsSettings.ClampWindow(null));
    }
    static readonly WindowRect[] TwoScreens = [new(0, 0, 1920, 1040), new(1920, 0, 2560, 1400)];
    [Fact] public void A_saved_window_on_a_screen_is_kept()
    {
        Assert.Equal(new WindowRect(100, 80, 1280, 720), GraphicsSettings.Place(new WindowRect(100, 80, 1280, 720), TwoScreens));
        Assert.Equal(new WindowRect(2200, 100, 1600, 900), GraphicsSettings.Place(new WindowRect(2200, 100, 1600, 900), TwoScreens));
    }
    [Fact] public void A_saved_window_off_every_screen_or_absent_is_none()
    {
        Assert.Null(GraphicsSettings.Place(new WindowRect(5000, 100, 1280, 720), TwoScreens));   // a monitor since unplugged
        Assert.Null(GraphicsSettings.Place(new WindowRect(-1260, 100, 1280, 720), TwoScreens));  // 20 px showing
        Assert.Null(GraphicsSettings.Place(null, TwoScreens));
    }
}
