namespace Tabletop.TerrainMeshes;

/// <summary>Every built-in terrain mesh by its file's basename (client/content/builtin/catalog.json, "meshes").</summary>
public static class Catalog
{
    public static readonly IReadOnlyList<(string Name, Func<Model> Build)> All =
    [
        ("hill", Hill.Round), ("hill-rect", Hill.Square), ("woods", Woods.Build), ("ruins", Ruins.Build), ("overgrown-ruins", Ruins.Overgrown),
        ("building", Building.Build), ("huts", Huts.Build), ("tower", Tower.Build), ("wall", Wall.Build), ("barricade", Barricade.Build),
        ("rock", Rock.Build), ("crumbly-rock", Crumbly.Rock), ("dunes", Crumbly.Dunes), ("field", Field.Build), ("tall-crops", Crops.Tall),
        ("water", Water.River), ("stream", Rivers.Stream), ("toxic-river", Rivers.Toxic), ("sludge-canal", Sludge.Canal), ("lake", Water.Lake),
        ("crater", Crater.Build), ("haze", Haze.Build), ("death-marsh", Marsh.Build), ("sludge-field", Sludge.Field), ("mine-field", Mines.Build),
        ("quicksand", Quicksand.Build), ("deadly-vegetation", Vegetation.Deadly), ("jungle", Vegetation.Jungle), ("plantation", Crops.Plantation),
        ("beast-lair", Lair.Build),
    ];
}
