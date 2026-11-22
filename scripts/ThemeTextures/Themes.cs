namespace Tabletop.ThemeTextures;

/// <summary>Every theme image, the scales each is written at, and where the client and the web app keep them.</summary>
public static class Themes
{
    public static IReadOnlyList<Sheet> All { get; } = [.. Fantasy.All, .. SciFi.All];
    public static readonly int[] Scales = [1, 2];
    public const string Client = "client/content/themes", Web = "web/static/themes";
}
