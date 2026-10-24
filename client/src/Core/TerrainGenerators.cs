using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Nine fixed pieces, then 4D3 more each rolled on 2D6. Generic, Hospitable, Wasteland and Death are Grimdark Future
/// p.18–19's planets. Ruins is the owner's pack in the same shape, not the rulebook's: a ruined city of ruins, ruined walls and
/// rubble with craters and barricades, no whole buildings, its fixed nine keeping p.12's balance (half block line of sight, a
/// third give cover, a third are difficult). Runs on the client; the result is a batch of adds.</summary>
public static class TerrainGenerators
{
    /// <summary>The pieces land beside the long edge, off the slab, in rows 8" apart, four to a row, each facing a random 15° step; players drag them on. A piece
    /// the catalog (default: the one in use) does not list is skipped.</summary>
    public static IReadOnlyList<AddTerrain> Generate(Planet planet, TableSize table, Random rng, BuiltinCatalog? catalog = null)
    {
        catalog ??= BuiltinContent.Catalog;
        var names = PlanetTerrain.Roll(planet, rng);
        var yaws = names.Select(_ => new Yaw(rng.Next(0, 24) * 15)).ToList();
        return names.Select((name, i) => (name, yaw: yaws[i])).Where(x => catalog.Terrain.ContainsKey(x.name))
            .Select((x, i) => new AddTerrain(catalog.Place(x.name, new Point3(table.Width + 4 + (i % 4) * 8, 4 + (i / 4) * 8, 0), x.yaw))).ToList();
    }
}
