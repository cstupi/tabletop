namespace Tabletop.TerrainMeshes;

/// <summary>Every built-in terrain mesh by its file's basename (client/content/builtin/catalog.json, "meshes").</summary>
public static class Catalog
{
    public static readonly IReadOnlyList<(string Name, Func<Model> Build)> All =
    [
        ("hill", Hill.Round), ("hill-rect", Hill.Square), ("woods", Woods.Build), ("ruins", Ruins.Build), ("building", Building.Build),
        ("wall", Wall.Build), ("rock", Rock.Build), ("field", Field.Build), ("water", Water.River), ("lake", Water.Lake),
        ("pad", Pad.Plaza), ("crater", Pad.Crater), ("haze", Haze.Build),
    ];
}
