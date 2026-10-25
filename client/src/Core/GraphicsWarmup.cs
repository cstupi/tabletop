namespace Tabletop.Client.Core;

/// <summary>
/// The shader warm-up (spec 2026-10-25-loading-design.md → Shader warm-up): every material the table makes at run time drawn once
/// under each Quality, behind "Preparing graphics…", so their shaders and pipelines compile there and not in a game's first
/// frames. Godot and the driver keep what compiled, so it runs again only when the stamp changes: Godot, the renderer, the GPU,
/// its driver or the client's version.
/// </summary>
public static class GraphicsWarmup
{
    /// <summary>Raised when the warm-up's scene draws something new, so every install warms again.</summary>
    const int SceneVersion = 1;

    /// <summary>The player's Quality first: the one a game will use, compiled before the rest.</summary>
    public static IReadOnlyList<GraphicsQuality> Qualities(GraphicsQuality current) =>
        [current, .. Enum.GetValues<GraphicsQuality>().Where(q => q != current)];

    public static string Stamp(string godot, string method, string driver, string adapter, string adapterVersion, string client) =>
        string.Join('|', $"warmup {SceneVersion}", godot, method, driver, adapter, adapterVersion, client);

    public static bool Needed(string? saved, string current) => saved?.Trim() != current;

    /// <summary>The bar: one step per Quality and <paramref name="extraSteps"/> to build the scene before them.</summary>
    public static LoadingLine Line(int done, int extraSteps) => LoadingView.Graphics(done, Enum.GetValues<GraphicsQuality>().Length + extraSteps);
}
