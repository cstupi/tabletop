using Godot;

namespace Tabletop.Client.Screens;

public partial class UpdateRequiredScreen : Control
{
    public override void _Ready()
    {
        var box = new VBoxContainer(); box.SetAnchorsPreset(LayoutPreset.Center); AddChild(box);
        box.AddChild(new Label { Text = "This version of Tabletop is older than the server accepts. Install the current build and start it again." });
        var quit = new Button { Text = "Quit" }; quit.Pressed += () => GetTree().Quit(); box.AddChild(quit);
    }
}
