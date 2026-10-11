using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Screens;

/// <summary>The settings that apply at once: theme, animation, camera follow, turn banner and range rings.</summary>
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
        Check(box, "Range rings on at start", session.Settings.RangeRings, session, (s, on) => s with { RangeRings = on });
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
        d.Confirmed += d.QueueFree; d.CloseRequested += d.QueueFree;
        d.PopupCentered(new Vector2I(360, 0));
    }
}
