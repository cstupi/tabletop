using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>The key hint bar at the foot of setup (spec 2026-11-21-setup-screen-layout-design.md → Key hint bar): one quiet line of the setup
/// keys as key caps, each followed by its word (SetupTools.Hints). The game screen hides it while a text field has the focus.</summary>
public partial class KeyHintBar : PanelContainer
{
    public override void _Ready()
    {
        var box = SetupRail.Band(); box.ContentMarginTop = box.ContentMarginBottom = Space.XS; box.ContentMarginLeft = box.ContentMarginRight = Space.M;
        AddThemeStyleboxOverride("panel", box);
        MouseFilter = MouseFilterEnum.Ignore;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", Space.L); AddChild(row);
        foreach (var hint in SetupTools.Hints)
        {
            var one = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; one.AddThemeConstantOverride("separation", Space.S); row.AddChild(one);
            one.AddChild(HudBits.KeyCaps(hint.Keys.Split(' ')));
            one.AddChild(new Label { Text = hint.Word, ThemeTypeVariation = Variation.Dim, VerticalAlignment = VerticalAlignment.Center });
        }
    }
}
