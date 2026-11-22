using Godot;

namespace Tabletop.Client.Theming;

/// <summary>The theme's full-screen backdrop behind a page (spec 2026-11-21-theme-materials-design.md): the picture stretched to cover the
/// control and the tile repeated over or under it (sci-fi's stars over the nebula, fantasy's lamp over the leather). Full-screen pages
/// only (Home, Login, lobby, Options, the update screen), never over the table. Static: drawn once per size or theme.</summary>
public sealed partial class Backdrop : Control
{
    Texture2D? _picture, _tile;

    public Backdrop()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        TextureRepeat = TextureRepeatEnum.Enabled;   // the tile repeats
        Resized += QueueRedraw;
    }

    public override void _EnterTree() { Load(); Ui.Changed += OnTheme; }
    public override void _ExitTree() => Ui.Changed -= OnTheme;

    void OnTheme() { Load(); QueueRedraw(); }

    void Load()
    {
        var m = Ui.Current.Materials;
        _picture = Ui.Image(m.Backdrop); _tile = Ui.Image(m.BackdropTile);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Ui.Background with { A = 1 });
        if (_picture is null || _tile is null) return;
        var over = Ui.Current.Materials.TileOver;
        if (over) Picture(_picture);
        DrawTextureRect(_tile, new Rect2(Vector2.Zero, Size), tile: true);
        if (!over) Picture(_picture);
    }

    /// <summary>The picture scaled to cover the control, centred, its aspect kept.</summary>
    void Picture(Texture2D t)
    {
        var size = t.GetSize(); var k = Mathf.Max(Size.X / size.X, Size.Y / size.Y);
        var drawn = size * k;
        DrawTextureRect(t, new Rect2((Size - drawn) / 2, drawn), tile: false);
    }
}
