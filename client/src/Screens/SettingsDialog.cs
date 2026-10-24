using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Screens;

/// <summary>The settings that apply at once: theme, animation, camera follow, turn banner, hitboxes, range rings,
/// sound, and Graphics (display, quality, render scale, VSync).</summary>
public partial class SettingsDialog : AcceptDialog
{
    SettingsDialog(Session session)
    {
        Title = "Settings"; OkButtonText = "Close";
        var box = new VBoxContainer(); AddChild(box);
        var theme = new OptionButton();
        foreach (var t in UiThemes.All) theme.AddItem(t.Name);
        theme.Selected = UiThemes.All.ToList().IndexOf(UiThemes.Resolve(session.Settings.Theme));
        theme.ItemSelected += i => session.UpdateSettings(s => s with { Theme = UiThemes.All[(int)i].Id });
        Row(box, "Theme", theme);
        Check(box, "Animate", session.Settings.Animate, session, (s, on) => s with { Animate = on });
        Check(box, "Camera follows the action", session.Settings.CameraFollow, session, (s, on) => s with { CameraFollow = on });
        Check(box, "Turn banner", session.Settings.TurnBanner, session, (s, on) => s with { TurnBanner = on });
        Check(box, "Show hitboxes (B)", session.Settings.ShowHitboxes, session, (s, on) => s with { ShowHitboxes = on });
        Check(box, "Show terrain hitboxes (Shift+B)", session.Settings.ShowTerrainHitboxes, session, (s, on) => s with { ShowTerrainHitboxes = on });
        Check(box, "Range rings on at start", session.Settings.RangeRings, session, (s, on) => s with { RangeRings = on });
        Check(box, "Sound", session.Settings.Sound, session, (s, on) => s with { Sound = on });
        var volume = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, Value = session.Settings.Volume, CustomMinimumSize = new Vector2(160, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        volume.ValueChanged += v => session.UpdateSettings(s => s with { Volume = (int)v });
        Row(box, "Volume", volume);

        box.AddChild(new HSeparator()); box.AddChild(new Label { Text = "Graphics", ThemeTypeVariation = Variation.Accent });
        var display = new OptionButton();
        foreach (var n in Enum.GetNames<DisplayMode>()) display.AddItem(n);
        display.Selected = (int)session.Settings.Display;
        display.ItemSelected += i => session.UpdateSettings(s => s with { Display = (DisplayMode)(int)i });
        Row(box, "Display", display);
        var scale = new HSlider { MinValue = GraphicsSettings.MinScale, MaxValue = GraphicsSettings.MaxScale, Step = GraphicsSettings.ScaleStep, Value = session.Settings.Graphics.RenderScalePercent, CustomMinimumSize = new Vector2(160, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var scaleValue = new Label { Text = $"{session.Settings.Graphics.RenderScalePercent} %" };
        var quality = new OptionButton();
        foreach (var n in Enum.GetNames<GraphicsQuality>()) quality.AddItem(n);
        quality.Selected = (int)session.Settings.Quality;
        quality.ItemSelected += i =>
        {
            session.UpdateSettings(s => s with { Quality = (GraphicsQuality)(int)i, RenderScale = null });   // a Quality resets the scale to its own
            var percent = session.Settings.Graphics.RenderScalePercent;
            scale.SetValueNoSignal(percent); scaleValue.Text = $"{percent} %";   // no signal: the slider must not write the scale back as an override
        };
        Row(box, "Quality", quality);
        scale.ValueChanged += v => { session.UpdateSettings(s => s with { RenderScale = (int)v }); scaleValue.Text = $"{(int)v} %"; };
        var scaleRow = Row(box, "Render scale", scale); scaleRow.AddChild(scaleValue);
        Check(box, "VSync", session.Settings.VSync, session, (s, on) => s with { VSync = on });
    }

    static HBoxContainer Row(Container box, string label, Control control)
    {
        var row = new HBoxContainer(); row.AddChild(new Label { Text = label }); row.AddChild(control); box.AddChild(row);
        return row;
    }

    static void Check(Container box, string text, bool value, Session session, Func<ClientSettings, bool, ClientSettings> set)
    {
        var c = new CheckBox { Text = text, ButtonPressed = value };
        c.Toggled += on => session.UpdateSettings(s => set(s, on));
        box.AddChild(c);
    }

    public static void Open(Node parent, Session session)
    {
        var d = new SettingsDialog(session); parent.AddChild(d);
        d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };   // Close, the title bar's ✕ and Escape all hide it
        d.PopupCentered(new Vector2I(360, 0));
    }
}
