using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Screens;

/// <summary>The settings that apply at once (UX redesign part 1 → Settings), in captioned groups: Theme as a tile per theme in its own colours;
/// Play (animate, camera follows, turn banner, range rings at start); Table (model and terrain hitboxes, with their keys); Sound (on, volume);
/// Graphics (display, quality, render scale, VSync).</summary>
public partial class SettingsDialog : AcceptDialog
{
    SettingsDialog(Session session)
    {
        Title = "Settings"; OkButtonText = "Close";
        var margin = new MarginContainer(); AddChild(margin);
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(side, 8);
        var grid = new GridContainer { Columns = 2 }; margin.AddChild(grid);
        grid.AddThemeConstantOverride("h_separation", 24); grid.AddThemeConstantOverride("v_separation", 18);

        var themes = Group(grid, "THEME");
        var group = new ButtonGroup();
        foreach (var t in UiThemes.All) themes.AddChild(ThemeTile(t, group, UiThemes.Resolve(session.Settings.Theme) == t, () => session.UpdateSettings(s => s with { Theme = t.Id })));

        var play = Group(grid, "PLAY");
        Check(play, "Animate", session.Settings.Animate, session, (s, on) => s with { Animate = on });
        Check(play, "Camera follows", session.Settings.CameraFollow, session, (s, on) => s with { CameraFollow = on });
        Check(play, "Turn banner", session.Settings.TurnBanner, session, (s, on) => s with { TurnBanner = on });
        Check(play, "Range rings at start", session.Settings.RangeRings, session, (s, on) => s with { RangeRings = on });

        var table = Group(grid, "TABLE");
        Check(table, "Model hitboxes", session.Settings.ShowHitboxes, session, (s, on) => s with { ShowHitboxes = on }, "B");
        Check(table, "Terrain hitboxes", session.Settings.ShowTerrainHitboxes, session, (s, on) => s with { ShowTerrainHitboxes = on }, "Shift+B");

        var sound = Group(grid, "SOUND");
        Check(sound, "On", session.Settings.Sound, session, (s, on) => s with { Sound = on });
        var volume = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, Value = session.Settings.Volume, CustomMinimumSize = new Vector2(180, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var volumeValue = new Label { Text = $"{session.Settings.Volume}", ThemeTypeVariation = Variation.Dim, CustomMinimumSize = new Vector2(32, 0) };
        volume.ValueChanged += v => { session.UpdateSettings(s => s with { Volume = (int)v }); volumeValue.Text = $"{(int)v}"; };
        sound.AddChild(volume); sound.AddChild(volumeValue);

        var graphics = Group(grid, "GRAPHICS");
        var display = new OptionButton();
        foreach (var n in Enum.GetNames<DisplayMode>()) display.AddItem(n);
        display.Selected = (int)session.Settings.Display;
        display.ItemSelected += i => session.UpdateSettings(s => s with { Display = (DisplayMode)(int)i });
        Labelled(graphics, "Display", display);
        var scale = new HSlider { MinValue = GraphicsSettings.MinScale, MaxValue = GraphicsSettings.MaxScale, Step = GraphicsSettings.ScaleStep, Value = session.Settings.Graphics.RenderScalePercent,
            CustomMinimumSize = new Vector2(140, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var scaleValue = new Label { Text = $"{session.Settings.Graphics.RenderScalePercent} %", ThemeTypeVariation = Variation.Dim };
        var quality = new OptionButton();
        foreach (var n in Enum.GetNames<GraphicsQuality>()) quality.AddItem(n);
        quality.Selected = (int)session.Settings.Quality;
        quality.ItemSelected += i =>
        {
            session.UpdateSettings(s => s with { Quality = (GraphicsQuality)(int)i, RenderScale = null });   // a Quality resets the scale to its own
            var percent = session.Settings.Graphics.RenderScalePercent;
            scale.SetValueNoSignal(percent); scaleValue.Text = $"{percent} %";   // no signal: the slider must not write the scale back as an override
        };
        Labelled(graphics, "Quality", quality);
        scale.ValueChanged += v => { session.UpdateSettings(s => s with { RenderScale = (int)v }); scaleValue.Text = $"{(int)v} %"; };
        var scaleRow = Labelled(graphics, "Render scale", scale); scaleRow.AddChild(scaleValue);
        Check(graphics, "VSync", session.Settings.VSync, session, (s, on) => s with { VSync = on });
        var fps = new OptionButton();
        foreach (var cap in ClientSettings.FpsCaps) fps.AddItem(cap == 0 ? "Unlimited" : $"{cap} fps");
        fps.Selected = Array.IndexOf(ClientSettings.FpsCaps, session.Settings.FpsCap);
        fps.ItemSelected += i => session.UpdateSettings(s => s with { FpsCap = ClientSettings.FpsCaps[(int)i] });
        Labelled(graphics, "Frame rate limit", fps);
    }

    /// <summary>A caption in the left column and a wrapping row of controls in the right.</summary>
    static HFlowContainer Group(GridContainer grid, string caption)
    {
        grid.AddChild(new Label { Text = caption, ThemeTypeVariation = Variation.Accent, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin });
        var row = new HFlowContainer { CustomMinimumSize = new Vector2(560, 0) };
        row.AddThemeConstantOverride("h_separation", 22); row.AddThemeConstantOverride("v_separation", 10);
        grid.AddChild(row);
        return row;
    }

    static HBoxContainer Labelled(Container row, string label, Control control)
    {
        var box = new HBoxContainer(); box.AddThemeConstantOverride("separation", 8);
        box.AddChild(new Label { Text = label, ThemeTypeVariation = Variation.Dim }); box.AddChild(control); row.AddChild(box);
        return box;
    }

    static void Check(Container row, string text, bool value, Session session, Func<ClientSettings, bool, ClientSettings> set, string? key = null)
    {
        var c = new CheckBox { Text = text, ButtonPressed = value };
        c.Toggled += on => session.UpdateSettings(s => set(s, on));
        if (key is null) { row.AddChild(c); return; }
        var box = new HBoxContainer(); box.AddThemeConstantOverride("separation", 6);
        box.AddChild(c); box.AddChild(KeyCap(key)); row.AddChild(box);
    }

    /// <summary>The key, boxed.</summary>
    static Control KeyCap(string key)
    {
        var cap = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var b = new StyleBoxFlat { BgColor = Ui.Background with { A = 0.6f }, BorderColor = Ui.DimText };
        b.SetBorderWidthAll(1); b.BorderWidthBottom = 2; b.SetCornerRadiusAll(3); b.ContentMarginLeft = b.ContentMarginRight = 6; b.ContentMarginTop = b.ContentMarginBottom = 1;
        cap.AddThemeStyleboxOverride("panel", b);
        cap.AddChild(new Label { Text = key, ThemeTypeVariation = Variation.Dim });
        return cap;
    }

    /// <summary>A theme as a tile drawn in its own colours: its panel, its accent border when picked, its name in its accent.</summary>
    static Button ThemeTile(UiTheme t, ButtonGroup group, bool on, Action pick)
    {
        var tile = new Button { Text = t.Name, ToggleMode = true, ButtonGroup = group, ButtonPressed = on, CustomMinimumSize = new Vector2(150, 56) };
        foreach (var (state, picked) in new[] { ("normal", false), ("hover", false), ("pressed", true), ("hover_pressed", true), ("focus", false) })
        {
            var s = new StyleBoxFlat { BgColor = Ui.Of(t.Panel) with { A = 1 }, BorderColor = picked ? Ui.Of(t.Accent) : Ui.Of(t.PanelBorder) };
            s.SetBorderWidthAll(picked ? 3 : 1); s.SetCornerRadiusAll(t.Shape.CornerRadius); s.SetContentMarginAll(10);
            if (state == "focus") s.DrawCenter = false;
            tile.AddThemeStyleboxOverride(state, s);
        }
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            tile.AddThemeColorOverride(name, Ui.Of(t.Accent));
        tile.Toggled += picked => { if (picked) pick(); };
        return tile;
    }

    public static void Open(Node parent, Session session)
    {
        var d = new SettingsDialog(session); parent.AddChild(d);
        d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };   // Close, the title bar's ✕ and Escape all hide it
        d.PopupCentered(new Vector2I(720, 0));
    }
}
