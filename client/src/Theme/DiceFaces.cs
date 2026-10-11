using Godot;
using Tabletop.Protocol;

namespace Tabletop.Client.Theming;

/// <summary>One die as a themed face: a face texture or a numeral, then the modified value when it differs.</summary>
public static class DiceFaces
{
    public static Control Of(Die d)
    {
        var face = new PanelContainer { ThemeTypeVariation = Variation.DieFace, CustomMinimumSize = new(28, 28), Modulate = d.Success ? Colors.White : Ui.Faded };
        var row = new HBoxContainer();
        face.AddChild(row);
        if (Ui.Current.Dice.FaceTexture(d.Raw) is { } path)
            row.AddChild(new TextureRect { Texture = GD.Load<Texture2D>(path), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new(24, 24) });
        else row.AddChild(Numeral(d.Raw.ToString()));
        if (d.Modified != d.Raw) row.AddChild(Numeral($"→{d.Modified}"));
        return face;
    }

    /// <summary>A plain face from <see cref="Of"/> (raw equal to modified) shows another raw value in place: the dice tray's tumble.</summary>
    public static void SetRaw(Control face, int raw)
    {
        if (face.GetChildCount() == 0 || face.GetChild(0) is not HBoxContainer row || row.GetChildCount() == 0) return;
        switch (row.GetChild(0))
        {
            case TextureRect t when Ui.Current.Dice.FaceTexture(raw) is { } path: t.Texture = GD.Load<Texture2D>(path); break;
            case Label l: l.Text = raw.ToString(); break;
        }
    }

    static Label Numeral(string text) => new() { Text = text, ThemeTypeVariation = Variation.Numerals, HorizontalAlignment = HorizontalAlignment.Center };
}
