using System.Collections.Concurrent;
using System.Numerics;

namespace Tabletop.TerrainMeshes;

/// <summary>Every built-in terrain mesh by its file's basename (client/content/builtin/catalog.json, "meshes"), each painted by
/// <see cref="Bake"/>. <see cref="All"/> builds a mesh once per process (the bake is the slow part); <see cref="Generate"/> builds afresh.</summary>
public static class Catalog
{
    /// <summary>The size a unit-box mesh (one with no authored inches, shared or fitted to several entries) is baked at: its
    /// largest catalog entry's footprint and top.</summary>
    static readonly Dictionary<string, Vector3> Typical = new()
    {
        ["hill"] = new(8, 2, 8), ["hill-rect"] = new(6, 2, 6), ["woods"] = new(6, 6, 6), ["ruins"] = new(6, 4, 6), ["overgrown-ruins"] = new(6, 4, 6),
        ["building"] = new(6, 8, 6), ["wall"] = new(6, 1.5f, 0.5f), ["rock"] = new(4, 4, 4), ["field"] = new(6, 1, 6), ["water"] = new(3, 0.5f, 12),
        ["lake"] = new(8, 0.5f, 8), ["crater"] = new(6, 1, 6), ["haze"] = new(8, 6, 8),
    };

    static readonly IReadOnlyList<(string Name, Func<Model> Build)> Builders =
    [
        ("hill", Hill.Round), ("hill-rect", Hill.Square), ("woods", Woods.Build), ("ruins", Ruins.Build), ("overgrown-ruins", Ruins.Overgrown),
        ("building", Building.Build), ("huts", Huts.Build), ("tower", Tower.Build), ("wall", Wall.Build), ("barricade", Barricade.Build),
        ("rock", Rock.Build), ("crumbly-rock", Crumbly.Rock), ("dunes", Crumbly.Dunes), ("field", Field.Build), ("tall-crops", Crops.Tall),
        ("water", Water.River), ("stream", Rivers.Stream), ("toxic-river", Rivers.Toxic), ("sludge-canal", Sludge.Canal), ("lake", Water.Lake),
        ("crater", Crater.Build), ("haze", Haze.Build), ("death-marsh", Marsh.Build), ("sludge-field", Sludge.Field), ("mine-field", Mines.Build),
        ("quicksand", Quicksand.Build), ("deadly-vegetation", Vegetation.Deadly), ("jungle", Vegetation.Jungle), ("plantation", Crops.Plantation),
        ("beast-lair", Lair.Build),
    ];

    static readonly ConcurrentDictionary<string, Lazy<Model>> Built = new();

    public static readonly IReadOnlyList<(string Name, Func<Model> Build)> All =
        Builders.Select(b => (b.Name, (Func<Model>)(() => Built.GetOrAdd(b.Name, n => new Lazy<Model>(() => Generate(n))).Value))).ToList();

    /// <summary>The named mesh built and painted afresh.</summary>
    public static Model Generate(string name)
    {
        var model = Builders.Single(b => b.Name == name).Build();
        var inches = model.Inches != Vector3.One ? model.Inches : Typical.GetValueOrDefault(name, new Vector3(6, 4, 6));
        return Bake.Apply(model, inches);
    }
}
