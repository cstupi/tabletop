namespace Tabletop.Geometry.Wasm.Tests;

/// <summary>The repo root, found by walking up from the test assembly until Tabletop.sln appears.</summary>
public static class RepoPaths
{
    public static string Root { get; } = Find();
    public static string Fixtures => Path.Combine(Root, "viewer", "tests", "fixtures");
    public static string GeometryCases => Path.Combine(Fixtures, "geometry.cases.json");

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tabletop.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Tabletop.sln not found above the test assembly.");
    }
}
