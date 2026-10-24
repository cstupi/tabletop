using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Screens;

/// <summary>The settings that apply at once: theme, animation, camera follow, turn banner, hitboxes and range rings.</summary>
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
        var row = new HBoxContainer(); row.AddChild(new Label { Text = "Theme" }); row.AddChild(theme); box.AddChild(row);
        Check(box, "Animate", session.Settings.Animate, session, (s, on) => s with { Animate = on });
        Check(box, "Camera follows the action", session.Settings.CameraFollow, session, (s, on) => s with { CameraFollow = on });
        Check(box, "Turn banner", session.Settings.TurnBanner, session, (s, on) => s with { TurnBanner = on });
        Check(box, "Show hitboxes (B)", session.Settings.ShowHitboxes, session, (s, on) => s with { ShowHitboxes = on });
        Check(box, "Show terrain hitboxes (Shift+B)", session.Settings.ShowTerrainHitboxes, session, (s, on) => s with { ShowTerrainHitboxes = on });
        Check(box, "Range rings on at start", session.Settings.RangeRings, session, (s, on) => s with { RangeRings = on });
        Check(box, "Sound", session.Settings.Sound, session, (s, on) => s with { Sound = on });
        var volume = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, Value = session.Settings.Volume, CustomMinimumSize = new Vector2(160, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        volume.ValueChanged += v => session.UpdateSettings(s => s with { Volume = (int)v });
        var volumeRow = new HBoxContainer(); volumeRow.AddChild(new Label { Text = "Volume" }); volumeRow.AddChild(volume); box.AddChild(volumeRow);
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
