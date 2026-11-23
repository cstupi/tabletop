using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class OptionsModelTests
{
    static OptionRow Row(string id) => OptionsModel.Rows.Single(r => r.Id == id);

    [Fact]
    public void Every_category_but_controls_has_rows_and_every_id_is_unique()
    {
        var m = new OptionsModel(ClientSettings.Default);
        foreach (var c in OptionsModel.Categories.Where(c => c != OptionCategory.Controls)) Assert.NotEmpty(m.In(c));
        Assert.Empty(m.In(OptionCategory.Controls));
        Assert.Equal(OptionsModel.Rows.Count, OptionsModel.Rows.Select(r => r.Id).Distinct().Count());
        Assert.Equal([OptionCategory.Gameplay, OptionCategory.Camera, OptionCategory.Controls, OptionCategory.Video, OptionCategory.Audio, OptionCategory.Interface, OptionCategory.Accessibility], OptionsModel.Categories);
    }

    [Fact]
    public void Each_row_reads_its_default_and_writes_back_what_it_reads()
    {
        var m = new OptionsModel(ClientSettings.Default);
        foreach (var row in OptionsModel.Rows.Where(r => r.Id != OptionsModel.ResolutionId))
        {
            m.Set(row, m.Value(row));
            Assert.True(ClientSettings.Default.Equals(m.Pending), row.Id);
        }
    }

    [Fact]
    public void A_change_is_pending_until_apply_and_revert_drops_it()
    {
        var m = new OptionsModel(ClientSettings.Default);
        m.Set(Row("wound-numbers"), 0);
        m.Set(Row("pan-speed"), 150);
        Assert.True(m.Dirty);
        Assert.True(m.Applied.WoundNumbers);
        m.Revert();
        Assert.False(m.Dirty);
        Assert.True(m.Pending.WoundNumbers);
        m.Set(Row("wound-numbers"), 0);
        var applied = m.Apply();
        Assert.False(applied.WoundNumbers);
        Assert.False(m.Dirty);
    }

    [Fact]
    public void Values_clamp_to_the_row_s_range()
    {
        var m = new OptionsModel(ClientSettings.Default);
        m.Set(Row("pan-speed"), 999);
        Assert.Equal(ClientSettings.MaxSpeed, m.Pending.PanSpeed);
        m.Set(Row("text-size"), 7);
        Assert.Equal(130, m.Pending.TextSize);
    }

    [Fact]
    public void Reset_tab_touches_only_its_own_category()
    {
        var m = new OptionsModel(ClientSettings.Default with { PanSpeed = 150, InvertZoom = true, WoundNumbers = false, UiScale = 125,
            KeyOverrides = new Dictionary<Intent, IReadOnlyList<KeyBinding>> { [Intent.Ping] = [new KeyBinding(HotKey.J)] } });
        m.ResetTab(OptionCategory.Camera);
        Assert.Equal((100, false, false, 125), (m.Pending.PanSpeed, m.Pending.InvertZoom, m.Pending.WoundNumbers, m.Pending.UiScale));
        Assert.NotEmpty(m.Pending.KeyOverrides);
        m.ResetTab(OptionCategory.Controls);
        Assert.Empty(m.Pending.KeyOverrides);
        Assert.False(m.Pending.WoundNumbers);
    }

    [Fact]
    public void Picking_a_quality_resets_render_scale_and_the_quality_s_own_scale_is_no_override()
    {
        var m = new OptionsModel(ClientSettings.Default with { RenderScale = 60 });
        m.Set(Row("quality"), (int)GraphicsQuality.High);
        Assert.Null(m.Pending.RenderScale);
        m.Set(Row("render-scale"), 80);
        Assert.Equal(80, m.Pending.RenderScale);
        m.Set(Row("render-scale"), 100);
        Assert.Null(m.Pending.RenderScale);
    }

    [Fact]
    public void A_conflicting_bind_takes_the_key_and_swap_gives_the_other_intent_the_old_one()
    {
        var m = new OptionsModel(ClientSettings.Default);
        Assert.Equal((Intent.ToggleLog, 0), m.Conflict(Intent.Ping, new KeyBinding(HotKey.G)));
        m.Bind(Intent.Ping, 0, new KeyBinding(HotKey.G), swap: true);
        Assert.Equal(Intent.Ping, m.Keys.Of(HotKey.G, false, false));
        Assert.Equal(Intent.ToggleLog, m.Keys.Of(HotKey.P, false, false));

        m.Revert();
        m.Bind(Intent.Ping, 0, new KeyBinding(HotKey.G), swap: false);
        Assert.Equal(Intent.Ping, m.Keys.Of(HotKey.G, false, false));
        Assert.Empty(m.Keys.Of(Intent.ToggleLog));
        Assert.Null(m.Keys.Of(HotKey.P, false, false));
    }

    [Fact]
    public void Options_confirm_and_cancel_keep_their_keys()
    {
        var m = new OptionsModel(ClientSettings.Default);
        m.Bind(Intent.Confirm, 0, new KeyBinding(HotKey.J), swap: false);
        m.Bind(Intent.Ping, 0, new KeyBinding(HotKey.Escape), swap: true);
        Assert.Equal(Intent.Confirm, m.Keys.Of(HotKey.Enter, false, false));
        Assert.Equal(Intent.Cancel, m.Keys.Of(HotKey.Escape, false, false));
        Assert.Equal(Intent.Ping, m.Keys.Of(HotKey.P, false, false));
    }

    [Fact]
    public void Display_mode_and_window_size_need_confirming_other_changes_do_not()
    {
        var a = ClientSettings.Default with { Window = new WindowRect(10, 10, 1280, 720) };
        Assert.True(OptionsModel.NeedsConfirm(a, a with { Display = DisplayMode.Fullscreen }));
        Assert.True(OptionsModel.NeedsConfirm(a, a with { Window = new WindowRect(10, 10, 1600, 900) }));
        Assert.False(OptionsModel.NeedsConfirm(a, a with { Window = new WindowRect(50, 50, 1280, 720) }));
        Assert.False(OptionsModel.NeedsConfirm(a, a with { VSync = false }));
    }

    [Fact]
    public void Resolutions_fit_the_screen_and_end_with_the_native_size()
    {
        Assert.Equal(11, OptionsModel.Resolutions.Count);
        Assert.Equal([new(1280, 720), new(1280, 800), new(1366, 768), new(1440, 900), new(1600, 900), new(1680, 1050), new(1920, 1080)], OptionsModel.Fitting(new Resolution(1920, 1080)));
        Assert.Equal([new(1280, 720), new(1280, 800), new(1366, 768), new(1536, 864)], OptionsModel.Fitting(new Resolution(1536, 864)));
    }

    [Fact]
    public void The_resolution_row_lists_native_marks_custom_and_centres_the_window()
    {
        var m = new OptionsModel(ClientSettings.Default with { Window = new WindowRect(0, 0, 1500, 800) }) { Screen = new WindowRect(0, 0, 1920, 1080) };
        var row = Row(OptionsModel.ResolutionId);
        Assert.Equal(["1280×720", "1280×800", "1366×768", "1440×900", "1600×900", "1680×1050", "1920×1080 (native)", "Custom (1500×800)"], m.Choices(row));
        Assert.Equal(7, m.Value(row));
        m.Set(row, 4);
        Assert.Equal(new WindowRect(160, 90, 1600, 900), m.Pending.Window);
        Assert.Equal(4, m.Value(row));
        Assert.Equal(7, m.Choices(row).Count);   // Custom goes once a listed size is picked
    }

    /// <summary>Validation, 2026-11-22: Options read 1600×900, the saved rect, while the window stood at 1280×720. A window reads its own size.</summary>
    [Fact]
    public void The_resolution_row_reads_the_window_as_it_stands()
    {
        var saved = ClientSettings.Default with { Window = new WindowRect(160, 90, 1600, 900) };
        var m = new OptionsModel(OptionsModel.WithWindow(saved, new WindowRect(320, 180, 1280, 720))) { Screen = new WindowRect(0, 0, 1920, 1080) };
        Assert.Equal(0, m.Value(Row(OptionsModel.ResolutionId)));                                                 // 1280×720
        Assert.False(m.Dirty);
        var full = saved with { Display = DisplayMode.Fullscreen };
        Assert.Equal(full, OptionsModel.WithWindow(full, new WindowRect(0, 0, 1920, 1080)));                       // a fullscreen rect is never saved
    }

    [Fact]
    public void Outside_a_window_the_resolution_row_shows_native_and_is_disabled()
    {
        var m = new OptionsModel(ClientSettings.Default with { Display = DisplayMode.Fullscreen, Window = new WindowRect(0, 0, 1280, 720) }) { Screen = new WindowRect(0, 0, 2560, 1440) };
        var row = Row(OptionsModel.ResolutionId);
        Assert.False(m.Enabled(row));
        Assert.Equal("2560×1440 (native)", m.Choices(row)[m.Value(row)]);
        Assert.Contains("Render scale", m.Description(row));
        m.Set(row, 0);
        Assert.Equal(new WindowRect(0, 0, 1280, 720), m.Pending.Window);
        Assert.True(m.Enabled(Row("quality")));
    }
}
