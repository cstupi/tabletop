using Tabletop.Content;

namespace Tabletop.Content.Tests;

/// <summary>The repo's built-in catalog, read once.</summary>
internal static class RepoCatalog
{
    public static string Root { get; } = FindRoot();
    public static string Json { get; } = File.ReadAllText(Path.Combine(Root, "client", "content", "builtin", "catalog.json"));
    public static IReadOnlyDictionary<string, BuiltinTerrain> Terrain { get; } = BuiltinTerrain.FromCatalog(Json);

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tabletop.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Tabletop.sln not found above the test binaries.");
    }
}
