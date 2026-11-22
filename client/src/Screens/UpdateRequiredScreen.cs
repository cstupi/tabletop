using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Screens;

/// <summary>The server refuses this client's protocol version: send the player to the web app's Download page.</summary>
public partial class UpdateRequiredScreen : Control
{
    public override void _Ready()
    {
        var url = Main.Instance.Session.Settings.DownloadUrl;
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.SetAnchorsPreset(LayoutPreset.Center); box.GrowHorizontal = GrowDirection.Both; box.GrowVertical = GrowDirection.Both;
        AddChild(box); box.AddThemeConstantOverride("separation", Space.M);
        box.AddChild(new Label { Text = "This version of Tabletop is older than the server accepts. Download the current build, install it and start it again.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        box.AddChild(new LinkButton { Text = url, Uri = url });
        box.AddChild(new Label { Text = $"This is version {BuildInfo.Current.Version}.", ThemeTypeVariation = Variation.Dim });
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Space.S); box.AddChild(row);
        var open = new Button { Text = "Open the Download page" }; open.Pressed += () => OS.ShellOpen(url); row.AddChild(open);
        var quit = new Button { Text = "Quit" }; quit.Pressed += () => GetTree().Quit(); row.AddChild(quit);
        open.CallDeferred(Control.MethodName.GrabFocus);
    }
}
