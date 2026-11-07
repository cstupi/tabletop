using Godot;
using Tabletop.Protocol;

namespace Tabletop.Client.Theming;

/// <summary>One die as a themed face: a face texture or a numeral, then the modified value when it differs. A judged pass is framed in the
/// success colour, a fail in the failure colour; an unjudged die in the face's own.</summary>
public static class DiceFaces
{
    public static Control Of(Die d, int size = 28, bool judged = false)
    {
        var face = new PanelContainer { ThemeTypeVariation = d.Success ? Variation.DieFace : Variation.DieMiss, CustomMinimumSize = new(size, size), Modulate = d.Success ? Colors.White : Missed };
        if (judged && d.Success && Ui.Theme.GetStylebox("panel", Variation.DieFace).Duplicate() is StyleBoxFlat pass)
        {
            pass.BorderColor = Ui.Success; pass.SetBorderWidthAll(2); face.AddThemeStyleboxOverride("panel", pass);
        }
        var row = new HBoxContainer();
        face.AddChild(row);
        if (Ui.Current.Dice.FaceTexture(d.Raw) is { } path)
            row.AddChild(new TextureRect { Texture = GD.Load<Texture2D>(path), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new(size - 4, size - 4) });
        else
        {
            var n = Numeral(d.Raw.ToString());
            if (size > 28) n.AddThemeFontSizeOverride("font_size", Ui.Current.Numerals.Size * size / 28);
            row.AddChild(n);
        }
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

    /// <summary>A failed face stays legible: slightly faded, framed in the failure colour.</summary>
    static readonly Color Missed = new(1, 1, 1, 0.75f);

    static Label Numeral(string text) => new() { Text = text, ThemeTypeVariation = Variation.Numerals, HorizontalAlignment = HorizontalAlignment.Center };
}
