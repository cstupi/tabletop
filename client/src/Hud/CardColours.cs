using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Hud;

/// <summary>Every colour the unit card uses, in one place for the theme (plan 04) to replace.</summary>
public static class CardColours
{
    public static readonly Color Text = new(1, 1, 1);
    public static readonly Color Dim = new(1, 1, 1, 0.55f);
    public static readonly Color Within = new(0.45f, 0.85f, 0.45f);
    public static readonly Color Beyond = new(0.95f, 0.55f, 0.35f);

    public static Color Chip(ChipKind k) => k switch { ChipKind.Condition => new(1f, 0.75f, 0.3f), ChipKind.Activated => Dim, _ => Text };
    public static Color Range(bool? within) => within switch { true => Within, false => Beyond, null => Text };
}
