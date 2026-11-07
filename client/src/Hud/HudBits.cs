using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>Small pieces several Hud parts draw the same way: a theme icon, a row of key caps, a terrain effect's chip.</summary>
public static class HudBits
{
    /// <summary>The theme's icon at a square size, tinted; an empty box of that size when the theme has none.</summary>
    public static TextureRect Icon(string name, int size, Color? tint = null) => new()
    {
        Texture = Ui.Icon(name), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size), Modulate = tint ?? Ui.Text, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>Each key boxed as a cap, side by side.</summary>
    public static HBoxContainer KeyCaps(IEnumerable<string> keys)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        foreach (var k in keys)
        {
            var cap = new PanelContainer { ThemeTypeVariation = Variation.KeyCap, MouseFilter = Control.MouseFilterEnum.Ignore };
            cap.AddChild(new Label { Text = k, ThemeTypeVariation = Variation.Mono, HorizontalAlignment = HorizontalAlignment.Center });
            row.AddChild(cap);
        }
        return row;
    }

    /// <summary>A terrain effect as a chip: its icon in its colour, then its word.</summary>
    public static PanelContainer EffectChip(TerrainEffectRow e, int iconSize = 20)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, TooltipText = e.Rule ?? "" };
        chip.AddThemeStyleboxOverride("panel", Ui.EffectChip(e.Colour));
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 6); chip.AddChild(row);
        row.AddChild(Icon(e.Icon, iconSize, Ui.Of(e.Colour)));
        row.AddChild(new Label { Text = e.Word, VerticalAlignment = VerticalAlignment.Center });
        return chip;
    }
}
