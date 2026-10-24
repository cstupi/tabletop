using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Layouts;

public sealed record RandomTableRequest(TableSize Size, Planet Planet, IReadOnlyList<DeploymentZone>? Zones, int? Seed);
public sealed record RandomTable(int Seed, IReadOnlyList<TerrainPlacement> Terrain, IReadOnlyList<string> Unplaced, IReadOnlyList<string> Unlisted);

public static class RandomTableEndpoint
{
    /// <summary>The request's problem, or null: size (Table.IsValid), at most LayoutRules' 16 zones, every shape id ZoneStyles resolves.</summary>
    public static string? Problem(RandomTableRequest r)
    {
        if (!Table.IsValid(r.Size)) return $"The table is more than 0 and at most {Table.MaxEdge}\" a side.";
        var zones = r.Zones ?? [];
        if (zones.Count > LayoutRules.MaxZones) return $"A table holds at most {LayoutRules.MaxZones} deployment zones.";
        foreach (var z in zones)
        {
            try { ZoneStyles.Resolve(z.Shape, r.Size); }
            catch (ArgumentException) { return $"'{z.Shape}' is not a deployment zone shape."; }
        }
        return null;
    }

    /// <summary><c>POST /v1/layouts/random</c> in the layouts group: <see cref="RandomTables.Lay"/> over the embedded terrain.</summary>
    public static void MapRandomTable(this RouteGroupBuilder layouts) =>
        layouts.MapPost("/random", (RandomTableRequest r, BuiltinLayouts builtin) =>
        {
            if (Problem(r) is { } problem) return Problems.Unprocessable(problem);
            var seed = r.Seed ?? Random.Shared.Next();
            var laid = RandomTables.Lay(r.Planet, r.Size, r.Zones ?? [], seed, builtin.Terrain);
            return Results.Ok(new RandomTable(seed, laid.Pieces.Select(p => p.Placement).ToList(), laid.Unplaced, laid.Unlisted));
        });
}
